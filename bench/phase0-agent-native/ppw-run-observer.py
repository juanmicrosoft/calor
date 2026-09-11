#!/usr/bin/env python3
"""Trusted, typed observation broker for gateway-isolated PP-W runs."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import http.client
import importlib.util
import json
import os
from pathlib import Path
import secrets
import shutil
import tempfile
import threading
from urllib.parse import urlsplit
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
OBSERVATION_KIND = "ppw-dotnet-observation-v1"
REGISTER_KIND = "ppw-observer-registration-v1"
RESULT_PREFIX = "PPW_XUNIT_RESULT_V1:"
TRANSIENT_DIRECTORIES = {
    "bin", "obj", ".ppw-client-tmp", ".ppw-generated-tmp",
    ".ppw-dotnet-home", ".ppw-shim",
}


def require(condition, message):
    if not condition:
        raise ValueError("PP-W observer: " + message)


def module(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), ROOT / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def canonical(path):
    path = Path(path)
    require(path.is_absolute() and ".." not in path.parts, "absolute contained path required")
    require(not any(item.is_symlink() for item in (path, *path.parents)), "linked path refused")
    return path


def source_tree_files(source, fragment_glob):
    patterns = ("*.cs", "*.calr", fragment_glob)
    return sorted({path for pattern in patterns for path in source.rglob(pattern)
                   if "bin" not in path.parts and "obj" not in path.parts})


def source_files(workspace, fragment_glob):
    return source_tree_files(workspace / "src", fragment_glob)


def source_digest(workspace, fragment_glob):
    digest = hashlib.sha256()
    for path in source_files(workspace, fragment_glob):
        digest.update(path.relative_to(workspace).as_posix().encode())
        digest.update(b"\0")
        digest.update(path.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def stable_tree_manifest(root):
    result = {}
    for path in sorted(root.rglob("*")):
        relative = path.relative_to(root)
        if TRANSIENT_DIRECTORIES.intersection(relative.parts):
            continue
        require(not path.is_symlink(), "linked workspace content cannot be frozen")
        if path.is_file():
            require(path.stat().st_nlink == 1, "hard-linked workspace content cannot be frozen")
            result[relative.as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def test_receipt(stdout, stderr, nonce, returncode):
    frames = [line for line in (stdout + "\n" + stderr).splitlines()
              if line.startswith(RESULT_PREFIX)]
    require(len(frames) == 1, "xUnit result receipt is missing or ambiguous")
    prefix = RESULT_PREFIX + nonce + ":"
    require(frames[0].startswith(prefix), "xUnit result receipt nonce differs")
    receipt = json.loads(frames[0][len(prefix):])
    require(isinstance(receipt, dict)
            and set(receipt) == {"failed", "passed", "skipped", "total"}
            and all(type(receipt[name]) is int and receipt[name] >= 0 for name in receipt)
            and receipt["total"] ==
                receipt["failed"] + receipt["passed"] + receipt["skipped"]
            and receipt["total"] > 0,
            "xUnit result receipt is malformed")
    require(returncode == (0 if receipt["failed"] == 0 else 1),
            "xUnit result receipt disagrees with process status")
    return receipt["passed"], receipt["failed"]


class RunObserver:
    """Owns authoritative telemetry and invokes generated code only under a child policy."""
    def __init__(self, *, work_root, output, hidden_roots, protected_root, arm, fragment_glob,
                 heldout_count, compiler, permissive, test_runtime, execution_runtime):
        self.work_root = canonical(work_root)
        self.output = canonical(output)
        self.hidden_roots = tuple(canonical(path) for path in hidden_roots)
        self.protected_root = canonical(protected_root)
        self.arm = arm
        self.fragment_glob = fragment_glob
        self.heldout_count = heldout_count
        self.compiler = canonical(compiler)
        self.permissive = permissive
        self.test_runtime = test_runtime
        self.execution_runtime = execution_runtime
        self.execution_root = Path(tempfile.mkdtemp(prefix=".ppw-observer-", dir=self.work_root))
        self.model_hidden_roots = self.hidden_roots + (self.execution_root,)
        self.workspace = None
        self.grading_workspace = None
        self.declared_source = None
        self.last_hash = None
        self.iteration = 0
        self.operation_lock = threading.Lock()
        self.active_lock = threading.Lock()
        self.active_processes = set()
        self.cancelling = False
        self.isolation = module("ppw-gateway-client.py")
        self.test_host = module("ppw-test-host.py")
        self.isolation.validate_runtime(execution_runtime)
        self.test_host.validate_runtime(test_runtime)

    def register(self, value):
        require(isinstance(value, dict) and set(value) == {"kind", "workspace", "output"}
                and value["kind"] == REGISTER_KIND, "invalid registration request")
        workspace = canonical(value["workspace"])
        output = canonical(value["output"])
        require(workspace.parent == self.work_root and output == self.output
                and workspace.is_dir() and (workspace / "src").is_dir(),
                "registration paths differ from the active run")
        require(self.workspace is None, "workspace is already registered")
        self.workspace = workspace
        self.last_hash = source_digest(workspace, self.fragment_glob)
        return {"ok": True}

    def close(self):
        self.cancel()
        shutil.rmtree(self.execution_root, ignore_errors=True)

    @contextmanager
    def private_snapshot(self):
        require(self.workspace is not None, "workspace is not registered")
        source = self.grading_workspace or self.workspace
        with tempfile.TemporaryDirectory(
                prefix="observation-", dir=self.execution_root) as temporary:
            operation_root = Path(temporary)
            snapshot = operation_root / "workspace"
            ignored = shutil.ignore_patterns(*TRANSIENT_DIRECTORIES)
            before = stable_tree_manifest(source)
            shutil.copytree(source, snapshot, ignore=ignored)
            require(before == stable_tree_manifest(source)
                    and before == stable_tree_manifest(snapshot),
                    "workspace changed while a private observation snapshot was being frozen")
            yield operation_root, snapshot

    def process_started(self, process):
        with self.active_lock:
            cancelling = self.cancelling
            if not cancelling:
                self.active_processes.add(process)
        if cancelling:
            self.isolation.terminate_process_group(process)
            raise ValueError("observer operation was cancelled")

    def process_finished(self, process):
        with self.active_lock:
            self.active_processes.discard(process)

    def cancel(self):
        with self.active_lock:
            self.cancelling = True
            processes = tuple(self.active_processes)
        for process in processes:
            self.isolation.terminate_process_group(process)
        with self.operation_lock:
            pass
        with self.active_lock:
            self.cancelling = False
        return {"ok": True}

    def seal(self):
        self.cancel()
        with self.operation_lock:
            require(self.workspace is not None and self.declared_source is None,
                    "workspace is not available for sealing")
            source = self.workspace / "src"
            require(source.is_dir(), "workspace source is missing")
            self.declared_source = self.execution_root / "declared-src"
            os.replace(source, self.declared_source)
            build_state = self.declared_source / "obj/calor/.calor-build-state.json"
            if build_state.is_file():
                shutil.copy2(build_state, self.output / "calor-build-state.json")
                (self.output / ".build-state-source").write_text(
                    "agent-workspace\n", encoding="utf-8")
            root_manifest = stable_tree_manifest(self.workspace)
            source_manifest = stable_tree_manifest(self.declared_source)
            self.grading_workspace = self.execution_root / "grading-workspace"
            ignored = shutil.ignore_patterns(*TRANSIENT_DIRECTORIES)
            shutil.copytree(self.workspace, self.grading_workspace, ignore=ignored)
            shutil.copytree(
                self.declared_source, self.grading_workspace / "src", ignore=ignored)
            require(root_manifest == stable_tree_manifest(self.workspace)
                    and source_manifest == stable_tree_manifest(self.declared_source),
                    "workspace changed while declared source was being frozen")
            require(all((self.grading_workspace / relative).is_file()
                        and hashlib.sha256(
                            (self.grading_workspace / relative).read_bytes()).hexdigest() == digest
                        for relative, digest in root_manifest.items())
                    and all((self.grading_workspace / "src" / relative).is_file()
                            and hashlib.sha256(
                                (self.grading_workspace / "src" / relative).read_bytes()
                            ).hexdigest() == digest
                            for relative, digest in source_manifest.items()),
                    "frozen grading copy differs from declared workspace")
            archive = self.output / "final-src"
            require(not archive.exists(), "declared source archive already exists")
            files = source_tree_files(
                self.grading_workspace / "src", self.fragment_glob)
            archive.mkdir()
            for path in files:
                destination = archive / path.relative_to(self.grading_workspace / "src")
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, destination)
            require(sum(path.is_file() for path in archive.rglob("*")) == len(files),
                    "declared source archival is incomplete")
            snapshot = module("harness-capture.py").policy_snapshot(self.grading_workspace)
            with (self.output / "policy-after.json").open("x", encoding="utf-8") as stream:
                json.dump(snapshot, stream, sort_keys=True, allow_nan=False)
                stream.write("\n")
            return {"ok": True, "archivedFiles": len(files)}

    def generated(self, command, *, sandbox_workspace, cwd, timeout=180, readable=(),
                  input_text=None):
        require(self.workspace is not None, "workspace is not registered")
        sandbox_workspace = canonical(sandbox_workspace)
        require(sandbox_workspace.is_relative_to(self.execution_root),
                "generated execution must use private observer storage")
        temporary = sandbox_workspace / ".ppw-generated-tmp"
        dotnet_home = sandbox_workspace / ".ppw-dotnet-home"
        temporary.mkdir(exist_ok=True)
        dotnet_home.mkdir(exist_ok=True)
        environment = dict(os.environ, CALOR_P0_SHIM_OFF="1",
                           TMPDIR=str(temporary), DOTNET_CLI_HOME=str(dotnet_home),
                           DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1", MSBUILDDISABLENODEREUSE="1",
                           DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_EnableDiagnostics="0",
                           NUGET_PACKAGES=self.test_runtime["packages"])
        hidden_roots = self.hidden_roots + (self.execution_root, self.workspace)
        return self.isolation.execute_generated(
            sandbox_workspace, self.output, self.protected_root, hidden_roots,
            command, cwd=cwd, environment=environment, timeout=timeout,
            readable=(sandbox_workspace, *readable), input_text=input_text,
            started=self.process_started, finished=self.process_finished)

    def build_source(self, workspace, log_name):
        dotnet = self.execution_runtime["dotnetExecutable"]
        result = self.generated(
            [dotnet, "build", str(workspace / "src/Src.csproj"), "--nologo", "-v", "q"],
            sandbox_workspace=workspace, cwd=workspace / "src")
        (self.output / log_name).write_text(result.stdout + result.stderr, encoding="utf-8")
        return result.returncode == 0

    def prepare_test_project(self, copied_project, candidate_source):
        original_source = str(self.workspace / "src")
        tree = ET.parse(copied_project)
        root = tree.getroot()
        candidate_binary = candidate_source / "bin/Debug/net10.0/Src.dll"
        require(candidate_binary.is_file(), "prebuilt candidate assembly is missing")
        source_reference = None
        for item_group in list(root):
            if item_group.tag.rsplit("}", 1)[-1] != "ItemGroup":
                continue
            for item in list(item_group):
                name = item.tag.rsplit("}", 1)[-1]
                if name == "ProjectReference":
                    include = item.get("Include")
                    require(include is not None and Path(include).name == "Src.csproj",
                            "trusted tests contain an unexpected project reference")
                    item_group.remove(item)
                elif name == "Reference" and item.get("Include") == "Src":
                    source_reference = item
                if name == "Reference":
                    for attribute, value in tuple(item.attrib.items()):
                        item.set(attribute, value.replace(original_source, str(candidate_source)))
                    for child in item:
                        if child.text is not None:
                            child.text = child.text.replace(
                                original_source, str(candidate_source))
        if source_reference is None:
            item_group = ET.SubElement(root, "ItemGroup")
            source_reference = ET.SubElement(item_group, "Reference", Include="Src")
        hint = next((item for item in source_reference
                     if item.tag.rsplit("}", 1)[-1] == "HintPath"), None)
        if hint is None:
            hint = ET.SubElement(source_reference, "HintPath")
        hint.text = str(candidate_binary)
        private = next((item for item in source_reference
                        if item.tag.rsplit("}", 1)[-1] == "Private"), None)
        if private is None:
            private = ET.SubElement(source_reference, "Private")
        private.text = "true"
        for item in root.iter():
            if item.tag.rsplit("}", 1)[-1] == "Import":
                imported = item.get("Project", "")
                require(original_source not in imported and str(candidate_source) not in imported,
                        "trusted tests cannot import model-controlled build logic")
        tree.write(copied_project, encoding="unicode")

    def run_suite(self, project, log_name, operation_root, candidate_workspace):
        project = canonical(project)
        require(project.is_relative_to(self.output), "test project is outside authoritative output")
        suite_root = operation_root / ("suite-" + secrets.token_hex(8))
        source = suite_root / "source"
        before = stable_tree_manifest(project.parent)
        shutil.copytree(project.parent, source, ignore=shutil.ignore_patterns("bin", "obj"))
        require(before == stable_tree_manifest(project.parent)
                and before == stable_tree_manifest(source),
                "trusted test source changed while it was being copied")
        copied_project = source / project.name
        self.prepare_test_project(copied_project, candidate_workspace / "src")
        immutable_source = stable_tree_manifest(source)
        binary = self.test_host.validate_runtime(self.test_runtime)
        dotnet = self.execution_runtime["dotnetExecutable"]
        build_root = suite_root / "build"
        build_root.mkdir()
        candidate_output = candidate_workspace / "src/bin/Debug/net10.0"
        build = self.generated([
            dotnet, "build", str(copied_project), "--nologo", "-v", "quiet",
            "--output", str(build_root / "bin"),
            "--property:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
            "--property:NuGetAudit=false", "--property:RestoreIgnoreFailedSources=true",
        ], sandbox_workspace=build_root, cwd=build_root, timeout=180,
            readable=(source, candidate_output))
        if build.returncode != 0:
            (self.output / log_name).write_text(
                build.stdout + build.stderr, encoding="utf-8")
        require(build.returncode == 0, "held-out build failed")
        target = build_root / "bin" / (copied_project.stem + ".dll")
        require(target.is_file(), "compiled test assembly is missing")
        require(immutable_source == stable_tree_manifest(source),
                "trusted test source changed during compilation")
        run_root = suite_root / "run"
        run_root.mkdir()
        nonce = secrets.token_hex(32)
        result = self.generated(
            [dotnet, str(binary), str(target), "--ppw-result-nonce", nonce],
            sandbox_workspace=run_root, cwd=run_root, timeout=120,
            readable=(build_root / "bin",))
        require(immutable_source == stable_tree_manifest(source),
                "trusted test source changed during execution")
        output = result.stdout + result.stderr
        (self.output / log_name).write_text(output, encoding="utf-8")
        passed, failed = test_receipt(result.stdout, result.stderr, nonce, result.returncode)
        return result.returncode, passed, failed

    def envelope(self, workspace):
        if self.arm != "calor" or not self.compiler.is_file():
            return [], False, None
        assembled = workspace / "src/obj/ppw-source/Program.calr"
        inputs = [assembled] if assembled.is_file() else [
            path for path in source_files(workspace, self.fragment_glob)
            if path.suffix == ".calr"
        ]
        if not inputs:
            return [], False, None
        command = [self.execution_runtime["dotnetExecutable"], str(self.compiler)]
        for path in inputs:
            command += ["--input", str(path)]
        command += ["--enforce-effects", "--contract-mode", "debug", "--no-telemetry", "--format", "json"]
        if self.permissive:
            command.append("--permissive-effects")
        result = self.generated(
            command, sandbox_workspace=workspace, cwd=workspace / "src", timeout=60)
        try:
            document = json.loads(result.stdout)
        except (TypeError, json.JSONDecodeError):
            return [], False, None
        diagnostics = []
        for item in document.get("diagnostics", [])[:50]:
            if isinstance(item, dict) and isinstance(item.get("code"), str):
                value = {"code": item["code"]}
                if isinstance(item.get("declarationId"), str) and item["declarationId"]:
                    value["declarationId"] = item["declarationId"]
                diagnostics.append(value)
        return diagnostics, len(document.get("diagnostics", [])) > 50, "version" in document

    def observe(self, value):
        with self.operation_lock:
            return self._observe(value)

    def _observe(self, value):
        require(isinstance(value, dict) and set(value) ==
                {"kind", "command", "exitCode", "feedbackLatencyMs"}
                and value["kind"] == OBSERVATION_KIND
                and value["command"] in ("build", "test", "run")
                and type(value["exitCode"]) is int and type(value["feedbackLatencyMs"]) is int
                and value["feedbackLatencyMs"] >= 0, "invalid observation request")
        current = source_digest(self.workspace, self.fragment_glob)
        edited = current != self.last_hash
        self.last_hash = current
        iteration = None
        if edited:
            self.iteration += 1
            iteration = self.iteration
        with self.private_snapshot() as (operation_root, snapshot):
            build_ok = self.build_source(snapshot, ".src_build.txt")
            passed, failed = 0, self.heldout_count
            if build_ok:
                _, passed, failed = self.run_suite(
                    self.output / "heldout/HeldOut.csproj", ".ho_last.txt",
                    operation_root, snapshot)
            diagnostics, truncated, envelope_valid = self.envelope(snapshot)
        record = {
            "schema": "loop-telemetry/2",
            "ts": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "pair": self.output.parents[1].name,
            "arm": self.output.parent.name,
            "run": int(self.output.name.removeprefix("run-")),
            "iteration": iteration,
            "cmd": value["command"],
            "exit": value["exitCode"],
            "edited": edited,
            "feedback_latency_ms": value["feedbackLatencyMs"],
            "heldout_pass": passed,
            "heldout_fail": failed,
            "src_tree_hash": current,
            "edit_mechanism": "raw",
            "edit_target_ids": [],
            "diagnostics": diagnostics,
            "diagnostics_truncated": truncated,
            "envelope_valid": envelope_valid,
            "apply_verdict": None,
            "rejected_edit": None,
        }
        with (self.output / "journal.jsonl").open("a", encoding="utf-8") as stream:
            json.dump(record, stream, sort_keys=True, allow_nan=False)
            stream.write("\n")
        return {"ok": True}

    def final(self):
        with self.operation_lock:
            return self._final()

    def _final(self):
        require(self.grading_workspace is not None, "workspace is not sealed")
        with self.private_snapshot() as (operation_root, snapshot):
            build_ok = self.build_source(snapshot, ".src_final.txt")
            passed, failed = 0, self.heldout_count
            if build_ok:
                _, passed, failed = self.run_suite(
                    self.output / "heldout/HeldOut.csproj", ".ho_final.txt",
                    operation_root, snapshot)
            probe_caught = False
            probe = self.output / "probe/Probe.csproj"
            if build_ok and probe.is_file():
                _, probe_passed, probe_failed = self.run_suite(
                    probe, ".probe_final.txt", operation_root, snapshot)
                probe_caught = probe_passed == 1 and probe_failed == 0
        return {"buildOk": build_ok, "heldoutPassed": passed,
                "heldoutFailed": failed, "probeCaught": probe_caught}

    def source_inspection(self, pair, baseline, final, runtime):
        with self.operation_lock:
            return self._source_inspection(pair, baseline, final, runtime)

    def _source_inspection(self, pair, baseline, final, runtime):
        require(self.grading_workspace is not None, "workspace is not sealed")
        inspection = module("ppw-source-inspection.py")
        binary = inspection.validate_runtime(runtime, self.compiler)
        requests, hashes = [], {}
        for name, directory in {"baseline": canonical(baseline), "final": canonical(final)}.items():
            values, hashes[name] = inspection.inputs(pair, directory, name)
            requests.extend(values)
        with tempfile.TemporaryDirectory(
                prefix="inspection-", dir=self.execution_root) as temporary:
            run_root = Path(temporary)
            result = self.generated(
                [self.execution_runtime["dotnetExecutable"], str(binary)],
                sandbox_workspace=run_root, cwd=run_root, timeout=60,
                readable=(canonical(baseline), canonical(final)),
                input_text=json.dumps({"sources": requests}))
        require(result.returncode == 0, "source inspector execution failed")
        report = json.loads(result.stdout)
        require(report.get("compilerSha256") == runtime["compilerSha256"],
                "source inspector compiler identity differs")
        report["inputSha256"] = hashes
        report["inspectorSha256"] = runtime["files"]["ppw-source-inspector.dll"]
        report["inspectorRuntimeSha256"] = runtime["runtimeSha256"]
        return report


def request(url, operation, value):
    endpoint = urlsplit(url)
    require(endpoint.scheme == "http" and endpoint.hostname == "127.0.0.1" and endpoint.port,
            "observer URL must be an IPv4 loopback HTTP endpoint")
    body = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    client = http.client.HTTPConnection(endpoint.hostname, endpoint.port, timeout=600)
    client.request("POST", endpoint.path + "/" + operation, body,
                   {"Content-Type": "application/json", "Content-Length": str(len(body))})
    response = client.getresponse()
    payload = response.read()
    client.close()
    require(response.status == 200, "observer operation failed")
    result = json.loads(payload)
    require(isinstance(result, dict), "observer response is malformed")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("operation", choices=("register", "observe", "seal", "final",
                                              "source-inspection"))
    parser.add_argument("--url", required=True)
    parser.add_argument("--workspace")
    parser.add_argument("--output")
    parser.add_argument("--command", choices=("build", "test", "run"))
    parser.add_argument("--exit-code", type=int)
    parser.add_argument("--feedback-latency-ms", type=int)
    parser.add_argument("--pair")
    parser.add_argument("--baseline")
    parser.add_argument("--final-source")
    parser.add_argument("--runtime-manifest")
    args = parser.parse_args()
    if args.operation == "register":
        value = {"kind": REGISTER_KIND, "workspace": args.workspace, "output": args.output}
    elif args.operation == "observe":
        value = {"kind": OBSERVATION_KIND, "command": args.command, "exitCode": args.exit_code,
                 "feedbackLatencyMs": args.feedback_latency_ms}
    elif args.operation in ("seal", "final"):
        value = {}
    else:
        value = {
            "pair": json.loads(Path(args.pair).read_text(encoding="utf-8")),
            "baseline": args.baseline,
            "final": args.final_source,
            "runtime": json.loads(Path(args.runtime_manifest).read_text(encoding="utf-8")),
        }
    print(json.dumps(request(args.url, args.operation, value), sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError) as error:
        print(str(error), file=os.sys.stderr)
        raise SystemExit(2)
