#!/usr/bin/env python3
import argparse
import json
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path


def is_valid_run(return_code: int, executed: int, passed: int) -> bool:
    return return_code == 0 and executed == 2 and passed == 2


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--runs", type=int, default=3)
    parser.add_argument("--output", default="artifacts/flake/flake-report.json")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        if is_valid_run(0, 0, 0) or not is_valid_run(0, 2, 2):
            raise AssertionError("flake gate did not enforce the exact test inventory")
        print("Flake gate negative self-test passed.")
        return 0
    if args.runs < 2:
        parser.error("--runs must be at least 2")

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    logs = output.parent / "logs"
    logs.mkdir(exist_ok=True)
    results = []
    for run in range(1, args.runs + 1):
        results_dir = logs / f"results-{run}"
        started = time.monotonic()
        completed = subprocess.run(
            [
                "dotnet",
                "test",
                "tests/Calor.LanguageServer.Tests/Calor.LanguageServer.Tests.csproj",
                "-c",
                "Release",
                "--no-build",
                "--no-restore",
                "--filter",
                "FullyQualifiedName~LspE2ETests.HoverAsync_ReturnsInfo|FullyQualifiedName~LspE2ETests.CompletionAsync_ReturnsItems",
                "--logger",
                "trx;LogFileName=results.trx",
                "--results-directory",
                str(results_dir),
                "--verbosity",
                "minimal",
            ],
            text=True,
            capture_output=True,
            check=False,
        )
        elapsed = time.monotonic() - started
        (logs / f"run-{run}.log").write_text(
            completed.stdout + completed.stderr, encoding="utf-8"
        )
        trx = results_dir / "results.trx"
        executed = passed = 0
        if trx.is_file():
            counters = ET.parse(trx).find(".//{*}Counters")
            if counters is not None:
                executed = int(counters.attrib.get("executed", "0"))
                passed = int(counters.attrib.get("passed", "0"))
        run_passed = is_valid_run(completed.returncode, executed, passed)
        results.append(
            {
                "run": run,
                "executed": executed,
                "passedTests": passed,
                "passed": run_passed,
                "seconds": round(elapsed, 3),
            }
        )
        print(f"flake run {run}: {'passed' if run_passed else 'failed'} ({passed}/{executed})")

    report = {
        "schemaVersion": 1,
        "test": "live LSP hover and completion",
        "runs": results,
        "passed": all(result["passed"] for result in results),
    }
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
