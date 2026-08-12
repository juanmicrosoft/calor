#!/usr/bin/env python3
import argparse
import json
import subprocess
import time
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--runs", type=int, default=3)
    parser.add_argument("--output", default="artifacts/flake/flake-report.json")
    args = parser.parse_args()
    if args.runs < 2:
        parser.error("--runs must be at least 2")

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    logs = output.parent / "logs"
    logs.mkdir(exist_ok=True)
    results = []
    for run in range(1, args.runs + 1):
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
        results.append(
            {"run": run, "passed": completed.returncode == 0, "seconds": round(elapsed, 3)}
        )
        print(f"flake run {run}: {'passed' if completed.returncode == 0 else 'failed'}")

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
