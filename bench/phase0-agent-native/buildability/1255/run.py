#!/usr/bin/env python3
"""Unpaid deterministic #1255 observations, never an epoch or a verdict writer."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET


RELEASE = "514f538024df990af86054af25975b756ba42ab1"
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
TRX = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def trx_results(path):
    tree = ET.parse(path)
    counters = tree.find(".//t:Counters", TRX)
    results = tree.findall(".//t:UnitTestResult", TRX)
    if counters is None or not results:
        raise ValueError(f"No executed tests in {path}")
    return {
        "total": int(counters.attrib["total"]),
        "passed": int(counters.attrib["passed"]),
        "failed": int(counters.attrib["failed"]),
        "tests": [
            {
                "name": result.attrib["testName"],
                "outcome": result.attrib["outcome"],
                "message": result.findtext(
                    "t:Output/t:ErrorInfo/t:Message", default="", namespaces=TRX
                ),
            }
            for result in results
        ],
    }


def run(compiler_root, output):
    compiler_root = compiler_root.resolve()
    output = output.resolve()
    if not output.is_relative_to(REPO) or output == REPO or output.exists():
        raise ValueError("Output must be a new directory inside this worktree")
    head = subprocess.check_output(
        ["git", "-C", str(compiler_root), "rev-parse", "HEAD"], text=True
    ).strip()
    dirty = subprocess.check_output(
        ["git", "-C", str(compiler_root), "status", "--porcelain", "--untracked-files=no"],
        text=True,
    ).strip()
    if head != RELEASE or dirty:
        raise ValueError("Compiler checkout must be clean at the registered v0.18.0 commit")
    output.mkdir(parents=True)
    observations = output / "observations"
    observations.mkdir()
    env = {**os.environ, "CALOR_TELEMETRY": "0", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
    commands = []

    def normalize(text):
        for path, replacement in (
            (output, "<work>"),
            (compiler_root, "<compiler-root>"),
            (REPO, "<repo>"),
        ):
            text = text.replace(str(path), replacement)
        return text

    def execute(name, command):
        result = subprocess.run(command, cwd=REPO, env=env, text=True, capture_output=True)
        log = {
            "command": [normalize(str(arg)) for arg in command],
            "exitCode": result.returncode,
            "stdout": normalize(result.stdout),
            "stderr": normalize(result.stderr),
        }
        (observations / f"{name}.json").write_text(json.dumps(log, indent=2) + "\n")
        commands.append(name)
        return result

    verification = execute(
        "z3", ["python3", str(compiler_root / "scripts/verify-z3-assets.py")]
    )
    if verification.returncode:
        raise RuntimeError("Z3 verification failed; restore the pinned assets explicitly")
    table = execute(
        "row-table",
        [
            "dotnet", "test", str(compiler_root / "tests/Calor.Enforcement.Tests"),
            "--filter", "FullyQualifiedName~RowEscapeTableTests",
            "--logger", "trx;LogFileName=table.trx",
            "--results-directory", str(output / "row-table"), "--verbosity", "quiet",
        ],
    )
    if table.returncode:
        raise RuntimeError("Frozen RowEscapeTableTests failed")
    table_results = trx_results(output / "row-table/table.trx")
    if table_results["total"] != 26 or table_results["passed"] != 26:
        raise RuntimeError("Expected all 26 frozen instrument tests, including the twelve shapes")
    compiler = compiler_root / "src/Calor.Compiler/bin/Debug/net10.0/calor.dll"
    sdk = execute("sdk", ["dotnet", "--version"])
    version = execute("compiler-version", ["dotnet", str(compiler), "--version"])
    if sdk.returncode or version.returncode or not version.stdout.strip().startswith("0.18.0"):
        raise RuntimeError("Unable to verify the registered compiler version")
    candidates = json.loads((HERE / "candidates.json").read_text())
    report = {
        "kind": "deterministic-buildability-spike",
        "issue": 1255,
        "collectionEpoch": None,
        "compilerCommit": RELEASE,
        "compilerAssemblySha256": sha256(compiler),
        "dotnetSdk": sdk.stdout.strip(),
        "arms": {"A": ["--permissive-effects"], "B": []},
        "rowTable": table_results,
        "inputSha256": {
            str(path.relative_to(HERE)): sha256(path)
            for path in sorted(HERE.rglob("*"))
            if path.is_file() and (path.suffix in {".calr", ".cs", ".csproj", ".py"}
                                   or path.name == "spec.md")
            and "evidence" not in path.parts
        },
        "candidates": [],
    }
    report["inputSha256"]["candidates.json"] = sha256(HERE / "candidates.json")
    for candidate in candidates:
        item = {"id": candidate["id"], "shape": candidate["shape"], "variants": {}}
        directory = HERE / candidate["id"]
        for variant in ("starter", "laundering", "honest"):
            variant_results = {}
            for arm, flags in report["arms"].items():
                work = output / candidate["id"] / variant / arm
                work.mkdir(parents=True)
                source = work / "Candidate.calr"
                source.write_text(
                    (directory / "dependency.calr").read_text()
                    + "\n" + (directory / f"{variant}.calr").read_text()
                )
                emitted = work / "Candidate.g.cs"
                compiled = execute(
                    f"{candidate['id']}-{variant}-{arm}-compile",
                    ["dotnet", str(compiler), "-i", str(source), "-o", str(emitted),
                     "--no-telemetry", *flags],
                )
                errors = re.findall(r"\berror (Calor\d+):", compiled.stderr)
                if compiled.returncode not in (0, 1) or any(code != "Calor0410" for code in errors):
                    raise RuntimeError(f"Unrelated compiler failure: {candidate['id']} {variant} {arm}")
                result = {
                    "compileExit": compiled.returncode,
                    "errors": errors,
                    "warningCodes": re.findall(r"\bwarning (Calor\d+):", compiled.stderr),
                    "visible": None,
                    "heldOut": None,
                }
                if compiled.returncode:
                    if emitted.exists() or not errors:
                        raise RuntimeError("Compiler failure was not a clean effect rejection")
                elif not emitted.is_file():
                    raise RuntimeError("Compiler returned success without emitting C#")
                if compiled.returncode == 0 and variant != "starter":
                    for suite in ("visible", "heldOut"):
                        tests = work / suite
                        tests.mkdir()
                        shutil.copyfile(HERE / "runtime/RuntimeTests.csproj", tests / "RuntimeTests.csproj")
                        shutil.copyfile(emitted, tests / "Candidate.g.cs")
                        suite_file = "VisibleTests.cs" if suite == "visible" else "HeldOutTests.cs"
                        shutil.copyfile(HERE / "runtime" / suite_file, tests / "Tests.cs")
                        (tests / "Adapter.cs").write_text(
                            "internal static class Adapter\n{\n"
                            f"    public static int Invoke(int input) => {candidate['invoke']};\n"
                            f"    public static int Expected(int input) => {candidate['expected']};\n"
                            "}\n"
                        )
                        runtime = execute(
                            f"{candidate['id']}-{variant}-{arm}-{suite}",
                            ["dotnet", "test", str(tests / "RuntimeTests.csproj"),
                             "--logger", "trx;LogFileName=tests.trx",
                             "--results-directory", str(tests / "results"), "--verbosity", "quiet"],
                        )
                        result[suite] = trx_results(tests / "results/tests.trx")
                        expected_count = 5 if suite == "visible" else 2
                        if (result[suite]["total"] != expected_count
                                or result[suite]["passed"] + result[suite]["failed"] != expected_count
                                or runtime.returncode != (1 if result[suite]["failed"] else 0)):
                            raise RuntimeError("Runtime result is incomplete or inconsistent with its exit")
                variant_results[arm] = result
            item["variants"][variant] = variant_results
        report["candidates"].append(item)
        print(candidate["id"], json.dumps(item["variants"]["laundering"]))
    report["commands"] = commands
    (observations / "results.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compiler-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    run(args.compiler_root, args.output)
