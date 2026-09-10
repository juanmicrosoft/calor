#!/usr/bin/env python3
"""Observe unregistered candidate sources; never invoke an agent or register a study."""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess


HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
BASE = HERE.parents[1] / "buildability/1255"
spec = importlib.util.spec_from_file_location("buildability_1255", BASE / "run.py")
helpers = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helpers)
ARMS = {"A": "calor-permissive", "B": "calor-strict"}


def input_paths():
    paths = [HERE / "verify.py", HERE / "verification/RuntimeTests.csproj",
             BASE / "run.py", BASE / "runtime/NuGet.Config"]
    for task in sorted(HERE.glob("C-*/pair.json")):
        paths.extend([task, task.parent / "spec.md"])
        for path in sorted(task.parent.rglob("*")):
            if path.is_file() and (path.name.endswith(".calr.inc") or path.suffix == ".cs"):
                paths.append(path)
    return paths


def run(compiler_root, output):
    compiler_root, output = compiler_root.resolve(), output.resolve()
    if not output.is_relative_to(REPO) or output == REPO or output.exists():
        raise ValueError("Output must be a new directory inside this worktree")
    head = subprocess.check_output(
        ["git", "-C", str(compiler_root), "rev-parse", "HEAD"], text=True).strip()
    dirty = subprocess.check_output(
        ["git", "-C", str(compiler_root), "status", "--porcelain", "--untracked-files=no"],
        text=True).strip()
    if head != helpers.RELEASE or dirty:
        raise ValueError("Expected a clean frozen v0.18.0 compiler checkout")
    compiler = compiler_root / "src/Calor.Compiler/bin/Debug/net10.0/calor.dll"
    compiler_hash = helpers.sha256(compiler)
    inputs = {str(path.relative_to(REPO)): helpers.sha256(path) for path in input_paths()}
    observations = output / "observations"
    observations.mkdir(parents=True)
    env = {**os.environ, "CALOR_TELEMETRY": "0", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
    commands = []

    def normalize(value):
        for path, replacement in ((output, "<work>"), (compiler_root, "<compiler-root>"),
                                  (REPO, "<repo>")):
            value = value.replace(str(path), replacement)
        return value

    def execute(name, command):
        result = subprocess.run(command, cwd=REPO, env=env, text=True, capture_output=True)
        (observations / f"{name}.json").write_text(json.dumps({
            "command": [normalize(str(arg)) for arg in command],
            "exitCode": result.returncode,
            "stdout": normalize(result.stdout),
            "stderr": normalize(result.stderr),
        }, indent=2) + "\n")
        commands.append(name)
        return result

    def retain_trx(name, path):
        xml = normalize(path.read_text())
        for placeholder in ("work", "compiler-root", "repo"):
            xml = xml.replace(f"<{placeholder}>", f"&lt;{placeholder}&gt;")
        (observations / f"{name}.trx").write_text(xml)

    z3 = execute("z3", ["python3", str(compiler_root / "scripts/verify-z3-assets.py")])
    sdk = execute("sdk", ["dotnet", "--version"])
    info = execute("runtime-info", ["dotnet", "--info"])
    version = execute("compiler-version", ["dotnet", str(compiler), "--version"])
    if (any(result.returncode for result in (z3, sdk, info, version))
            or not version.stdout.strip().startswith("0.18.0")):
        raise RuntimeError("Frozen product/runtime verification failed")
    table_dll = compiler_root / "tests/Calor.Enforcement.Tests/bin/Debug/net10.0/calor.dll"
    if helpers.sha256(table_dll) != compiler_hash:
        raise RuntimeError("Frozen table tests do not use the observed compiler binary")
    table = execute("row-table", [
        "dotnet", "test", str(compiler_root / "tests/Calor.Enforcement.Tests"),
        "--no-build", "--no-restore", "--filter", "FullyQualifiedName~RowEscapeTableTests",
        "--logger", "trx;LogFileName=table.trx",
        "--results-directory", str(output / "row-table"), "--verbosity", "quiet",
    ])
    table_results = helpers.trx_results(output / "row-table/table.trx")
    retain_trx("row-table", output / "row-table/table.trx")
    if table.returncode or table_results["total"] != 26 or table_results["passed"] != 26:
        raise RuntimeError("Expected all 26 frozen row-table tests")
    report = {
        "kind": "unregistered-deterministic-task-candidates",
        "issue": 1256,
        "collectionEpoch": None,
        "collectionAuthorized": False,
        "taskSetFrozen": False,
        "compilerCommit": head,
        "compilerAssemblySha256": compiler_hash,
        "dotnetSdk": sdk.stdout.strip(),
        "originatingCheckout": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip(),
        "arms": {"A": ["--permissive-effects"], "B": []},
        "inputSha256": inputs,
        "runtimeDependencies": None,
        "rowTable": table_results,
        "candidates": [],
    }
    for pair_path in sorted(HERE.glob("C-*/pair.json")):
        pair = json.loads(pair_path.read_text())
        task = pair_path.parent
        item = {"id": pair["id"], "shape": pair["registeredShape"], "variants": {}}
        for variant in ("starter", "laundering", "honest"):
            item["variants"][variant] = {}
            for arm, key in ARMS.items():
                fixture = (pair["arms"][key]["fixture"] if variant == "starter"
                           else pair["seeded"][variant][arm.lower()])
                parts = [task / fixture / part for part in pair["sourceAssembly"]["parts"]]
                work = output / pair["id"] / variant / arm
                work.mkdir(parents=True)
                source, emitted = work / "Candidate.calr", work / "Candidate.g.cs"
                source.write_text("\n".join(part.read_text() for part in parts))
                name = f"{pair['id']}-{variant}-{arm}"
                compiled = execute(f"{name}-compile", [
                    "dotnet", str(compiler), "-i", str(source), "-o", str(emitted),
                    "--no-telemetry", *report["arms"][arm],
                ])
                errors = re.findall(r"\berror (Calor\d+):", compiled.stderr)
                if (compiled.returncode not in (0, 1)
                        or any(code != "Calor0410" for code in errors)):
                    raise RuntimeError(f"Unrelated compiler failure: {name}")
                if compiled.returncode and (emitted.exists() or not errors):
                    raise RuntimeError(f"Not a clean effect rejection: {name}")
                if not compiled.returncode and not emitted.is_file():
                    raise RuntimeError(f"No generated C#: {name}")
                result = {
                    "sourceSha256": helpers.sha256(source),
                    "compileExit": compiled.returncode,
                    "errors": errors,
                    "warningCodes": re.findall(r"\bwarning (Calor\d+):", compiled.stderr),
                    "visible": None, "heldOut": None,
                }
                if not compiled.returncode:
                    for suite, directory in (("visible", "smoke"), ("heldOut", "tests")):
                        tests = work / suite
                        tests.mkdir()
                        shutil.copyfile(HERE / "verification/RuntimeTests.csproj",
                                        tests / "RuntimeTests.csproj")
                        shutil.copyfile(emitted, tests / "Candidate.g.cs")
                        shutil.copytree(task / directory, tests / "Suite")
                        runtime = execute(f"{name}-{suite}", [
                            "dotnet", "test", str(tests / "RuntimeTests.csproj"),
                            "--logger", "trx;LogFileName=tests.trx",
                            "--logger", "console;verbosity=normal",
                            "--results-directory", str(tests / "results"),
                            "--verbosity", "quiet", *helpers.RUNTIME_PROPERTIES,
                            f"-p:RestoreConfigFile={BASE / 'runtime/NuGet.Config'}",
                        ])
                        trx = tests / "results/tests.trx"
                        result[suite] = helpers.trx_results(trx)
                        retain_trx(f"{name}-{suite}", trx)
                        if runtime.returncode != (1 if result[suite]["failed"] else 0):
                            raise RuntimeError(f"Test exit disagrees with actual execution: {name}")
                        assets = json.loads((tests / "obj/project.assets.json").read_text())
                        dependencies = {
                            key: {"type": value["type"], "sha512": value.get("sha512")}
                            for key, value in assets["libraries"].items()
                        }
                        if report["runtimeDependencies"] is None:
                            report["runtimeDependencies"] = dependencies
                        elif report["runtimeDependencies"] != dependencies:
                            raise RuntimeError("Runtime dependency resolution changed between suites")
                item["variants"][variant][arm] = result
        report["candidates"].append(item)
        print(pair["id"], "observations retained")
    if helpers.sha256(compiler) != compiler_hash:
        raise RuntimeError("Compiler changed while observing candidates")
    if inputs != {str(path.relative_to(REPO)): helpers.sha256(path) for path in input_paths()}:
        raise RuntimeError("Candidate inputs changed during observation")
    report["commands"] = commands
    (observations / "results.json").write_text(json.dumps(report, indent=2) + "\n")
    print("Observations complete; no scientific acceptance or freeze is implied.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compiler-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    run(args.compiler_root, args.output)
