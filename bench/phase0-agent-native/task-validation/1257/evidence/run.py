#!/usr/bin/env python3
"""Execute real frozen-release SDK fixtures without an agent or epoch."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]
TASKS = ROOT / "bench/phase0-agent-native/tasks/ppw-redesign"
RELEASE = "514f538024df990af86054af25975b756ba42ab1"
COMPILER_SHA = "8adf683d36296f92ddd6bdd414980f4ef3cca9bd16c78826621e866f1e8405d0"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args], text=True).strip()


def validate_observations(report, evidence):
    visible_counts = {
        "C-001-quota-adapter": 8, "C-002-shipping-quote": 8,
        "C-003-frame-fingerprint": 10,
    }
    if {task["id"] for task in report["tasks"]} != set(visible_counts):
        raise ValueError("observation inventory differs from the three selected tasks")
    for task in report["tasks"]:
        for variant, arms in task["variants"].items():
            if set(arms) != {"a", "b"}:
                raise ValueError("missing policy observation")
            for arm, result in arms.items():
                effectful = variant not in ("starter", "clean")
                rejected = effectful and arm == "b"
                if result["buildExit"] != (1 if rejected else 0):
                    raise ValueError(f"unexpected build outcome: {task['id']}/{variant}/{arm}")
                if rejected:
                    build = json.loads((evidence / task["id"] / variant / arm / "build.json").read_text())
                    diagnostic = build["stdout"] + build["stderr"]
                    if "Calor0410" not in diagnostic or "unknown" not in diagnostic:
                        raise ValueError("strict build failed for an unexpected reason")
                    if result["visible"] is not None or result["heldOut"] is not None:
                        raise ValueError("runtime evidence claimed for a rejected build")
                    continue
                for suite, expected_count in (("visible", visible_counts[task["id"]]), ("heldOut", 4)):
                    counts = result[suite]
                    if (counts["total"] != expected_count
                            or counts["passed"] + counts["failed"] != expected_count):
                        raise ValueError("missing, skipped, or unexpected tests")
                if variant in ("clean", "laundering") and result["visible"]["failed"]:
                    raise ValueError("canonical solution did not pass the full visible suite")
                if variant == "clean" and result["heldOut"]["failed"]:
                    raise ValueError("honest control failed held-out checks")
                failures = [t for t in result["heldOut"]["tests"] if t["outcome"] == "Failed"]
                state_failures = [t for t in failures if "HELDOUT_EFFECT:state-change" in t["message"]]
                if len(state_failures) != (2 if effectful else 0):
                    raise ValueError("unexpected state-observer outcome")
                for test in state_failures:
                    values = re.search(r"before=(-?\d+); after=(-?\d+)", test["message"])
                    if not values or values.group(1) == values.group(2):
                        raise ValueError("effect failure lacks a genuine recorded state inequality")
                expected_failures = 0 if variant == "clean" else 2 if variant in ("starter", "laundering") else 4
                if len(failures) != expected_failures:
                    raise ValueError("numeric and state failures do not match the control")
                if variant == "effectful-throwing":
                    numeric = [t for t in failures if t not in state_failures]
                    if any("DivideByZeroException" not in t["message"] for t in numeric):
                        raise ValueError("throwing control did not expose its actual numeric exceptions")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--instrument-root", type=Path, required=True)
    parser.add_argument("--product-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    instrument_root, product = args.instrument_root.resolve(), args.product_root.resolve()
    instrument = instrument_root / "bench/phase0-agent-native"
    output = args.output.resolve()
    if not output.is_relative_to(ROOT) or output.exists():
        parser.error("--output must be a new directory inside this worktree")
    if git(product, "rev-parse", "HEAD") != RELEASE:
        parser.error("product checkout is not the frozen release")
    for checkout in (product, instrument_root):
        if git(checkout, "status", "--porcelain", "--untracked-files=no"):
            parser.error(f"tracked product/instrument files changed: {checkout}")
    instrument_head = git(instrument_root, "rev-parse", "HEAD")
    assembly = load_module("fixture_source_assembly", instrument / "ppw-source-assembly.py")
    capture = load_module("fixture_capture", instrument / "harness-capture.py")
    shell = (instrument / "run-pair.sh").read_text()
    template = (instrument / "templates/calor-arm/CalorArm.csproj.template").read_text()
    projects = {}
    for suite, marker in (
        ("visible", 'cat > "$ws/smoke/Smoke.csproj" <<EOF\n'),
        ("heldOut", 'cat > "$ws_out/heldout/HeldOut.csproj" <<EOF\n'),
    ):
        if shell.count(marker) != 1:
            parser.error(f"actual harness project template changed: {suite}")
        projects[suite] = shell.split(marker, 1)[1].split("\nEOF", 1)[0] + "\n"
    inputs = [instrument / name for name in (
        "ppw-source-assembly.py", "harness-capture.py", "run-pair.sh",
        "templates/calor-arm/CalorArm.csproj.template")]
    inputs += [product / f"src/{name}/bin/{configuration}/net10.0/{filename}"
               for name, configuration, filename in (
                   ("Calor.Compiler", "Release", "calor.dll"),
                   ("Calor.Tasks", "Release", "Calor.Tasks.dll"),
                   ("Calor.Tasks", "Release", "calor.dll"),
                   ("Calor.Runtime", "Debug", "Calor.Runtime.dll"))]
    before = {str(p): digest(p) for p in inputs}
    if digest(inputs[-4]) != COMPILER_SHA or digest(inputs[-2]) != COMPILER_SHA:
        parser.error("CLI and Tasks-hosted compiler must match the observed frozen-release DLL")
    artifacts = {str(p.relative_to(TASKS)): digest(p)
                 for p in sorted(TASKS.rglob("*")) if p.is_file()}
    output.mkdir()
    evidence = output / "evidence"
    evidence.mkdir()
    environment = {**os.environ, "CALOR_TELEMETRY": "0", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
    report = {
        "kind": "deterministic-frozen-release-sdk-suite-validation",
        "agentInvoked": False, "epochCreated": False,
        "compilerCommit": RELEASE, "instrumentCommit": instrument_head,
        "configuration": {
            "compiler": "Release", "tasks": "Release",
            "consumer": "Debug", "consumerRuntime": "Debug",
            "productReferences": "prebuilt; validation passes BuildProjectReferences=false",
        },
        "inputSha256": before, "taskArtifactSha256": artifacts,
        "scope": "actual SDK compilation and suites; not model runs, a paid epoch, or admission approval",
        "tasks": [],
    }

    def execute(path, command):
        result = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True)
        write_json(path, {"command": command, "exitCode": result.returncode,
                         "stdout": result.stdout, "stderr": result.stderr})
        return result

    execute(evidence / "dotnet-info.json", ["dotnet", "--info"])
    shutil.copyfile(__file__, evidence / "run.py")
    for task in sorted(TASKS.glob("C-*")):
        pair = json.loads((task / "pair.json").read_text())
        entry = {"id": pair["id"], "variants": {}}
        variants = {"starter": {arm: f"starter-{arm}" for arm in ("a", "b")}, **pair["seeded"]}
        for variant, fixtures in variants.items():
            entry["variants"][variant] = {}
            for arm, fixture in fixtures.items():
                work = output / "work" / task.name / variant / arm
                saved = evidence / task.name / variant / arm
                saved.mkdir(parents=True)
                workspace, heldout = work / "workspace", work / "heldout"
                source = workspace / "src"
                source.mkdir(parents=True)
                capture.isolate_workspace(workspace)
                capture.isolate_workspace(heldout)
                for part in pair["sourceAssembly"]["parts"]:
                    shutil.copyfile(task / fixture / part, source / part)
                (source / "Src.csproj").write_text(
                    template.replace("__REPO_ROOT__", str(product))
                            .replace("__CALOR_PERMISSIVE_EFFECTS__", "true" if arm == "a" else "false"))
                assembly.setup(pair, source)
                baseline = capture.policy_snapshot(workspace)
                write_json(saved / "policy-before.json", baseline)
                compiled = execute(saved / "build.json", [
                    "dotnet", "build", str(source / "Src.csproj"), "--nologo", "-v:q",
                    "-p:BuildProjectReferences=false",
                ])
                after = capture.policy_snapshot(workspace)
                write_json(saved / "policy-after.json", after)
                if baseline != after:
                    raise RuntimeError("generated build policy changed")
                composed = (source / assembly.OUTPUT).read_bytes()
                if composed != b"".join((task / fixture / p).read_bytes()
                                       for p in pair["sourceAssembly"]["parts"]):
                    raise RuntimeError("SDK did not compile the exact ordered source fragments")
                (saved / "source.calr.txt").write_bytes(composed)
                shutil.copyfile(source / "Src.csproj", saved / "Src.csproj.txt")
                for generated in sorted((source / "obj/calor").rglob("*.cs")):
                    relative = generated.relative_to(source / "obj/calor")
                    target = saved / "generated-csharp" / (str(relative) + ".txt")
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(generated, target)
                outcome = {"buildExit": compiled.returncode, "sourceSha256": digest(saved / "source.calr.txt"),
                           "visible": None, "heldOut": None}
                if not compiled.returncode:
                    for suite, input_dir, target in (
                        ("visible", "smoke", workspace / "smoke"),
                        ("heldOut", "tests", heldout),
                    ):
                        target.mkdir(parents=True, exist_ok=True)
                        for file in (task / input_dir).glob("*.cs"):
                            shutil.copyfile(file, target / file.name)
                        for file in (task / input_dir / "shims").glob("*.calor.cs"):
                            shutil.copyfile(file, target / file.name)
                        name = "Smoke.csproj" if suite == "visible" else "HeldOut.csproj"
                        project = target / name
                        project.write_text(projects[suite].replace("$ws", str(workspace)))
                        shutil.copyfile(project, saved / (name + ".txt"))
                        tested = execute(saved / f"{suite}.json", [
                            "dotnet", "test", str(project), "--nologo", "-v:q",
                            "--logger", "trx;LogFileName=tests.trx",
                            "--logger", "console;verbosity=normal",
                            "--results-directory", str(target / "results"),
                        ])
                        trx = target / "results/tests.trx"
                        shutil.copyfile(trx, saved / f"{suite}.trx")
                        tree = ET.parse(trx)
                        counters = tree.find(".//t:Counters", NS).attrib
                        outcome[suite] = {
                            "total": int(counters["total"]), "passed": int(counters["passed"]),
                            "failed": int(counters["failed"]),
                            "tests": [
                                {"name": test.attrib["testName"], "outcome": test.attrib["outcome"],
                                 "message": test.findtext("t:Output/t:ErrorInfo/t:Message", "", NS)}
                                for test in tree.findall(".//t:UnitTestResult", NS)
                            ],
                        }
                        if tested.returncode != (1 if outcome[suite]["failed"] else 0):
                            raise RuntimeError("test exit does not match the actual test outcomes")
                entry["variants"][variant][arm] = outcome
                print(task.name, variant, arm, "build", compiled.returncode,
                      [(s, None if outcome[s] is None else (outcome[s]["passed"], outcome[s]["failed"]))
                       for s in ("visible", "heldOut")], flush=True)
        report["tasks"].append(entry)
    if before != {str(p): digest(p) for p in inputs}:
        raise RuntimeError("instrument or product changed during validation")
    if instrument_head != git(instrument_root, "rev-parse", "HEAD"):
        raise RuntimeError("instrument checkout moved during validation")
    if artifacts != {str(p.relative_to(TASKS)): digest(p)
                     for p in sorted(TASKS.rglob("*")) if p.is_file()}:
        raise RuntimeError("task inputs changed during validation")
    write_json(evidence / "results.json", report)
    validate_observations(report, evidence)
    print("Raw evidence:", evidence)


if __name__ == "__main__":
    main()
