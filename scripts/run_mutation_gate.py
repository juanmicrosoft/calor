#!/usr/bin/env python3
import argparse
import json
import re
import subprocess
import time
from pathlib import Path


def replace_occurrence(text: str, before: str, after: str, occurrence: int) -> str:
    starts = [match.start() for match in re.finditer(re.escape(before), text)]
    if len(starts) < occurrence:
        raise ValueError(f"requested occurrence {occurrence}, found {len(starts)}")
    start = starts[occurrence - 1]
    return text[:start] + after + text[start + len(before) :]


def test_status(return_code: int, output: str) -> str:
    if return_code == 0:
        return "survived"
    if "Failed!" in output and re.search(r"Failed:\s+[1-9]\d*", output):
        return "killed"
    return "error"


def self_test() -> None:
    if test_status(0, "Passed!") != "survived":
        raise AssertionError("a surviving mutant did not fail the gate")
    if test_status(1, "Failed! - Failed: 1") != "killed":
        raise AssertionError("a killed mutant was not recognized")
    if test_status(1, "Build FAILED.") != "error":
        raise AssertionError("a build failure was incorrectly credited as a killed mutant")
    print("Mutation gate negative self-test passed.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", default="eng/mutation-baselines.json")
    parser.add_argument("--output", default="artifacts/mutation/mutation-report.json")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0

    baseline = json.loads(Path(args.baseline).read_text(encoding="utf-8"))
    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    logs = output_path.parent / "logs"
    logs.mkdir(exist_ok=True)
    results = []

    for mutant in baseline["mutants"]:
        source = Path(mutant["file"])
        original_bytes = source.read_bytes()
        original = original_bytes.decode("utf-8")
        before = mutant["before"]
        occurrence = int(mutant.get("occurrence", 1))
        try:
            mutated = replace_occurrence(original, before, mutant["after"], occurrence)
        except ValueError as error:
            print(f"ERROR: {mutant['id']}: {error}")
            return 1

        source.write_bytes(mutated.encode("utf-8"))
        started = time.monotonic()
        try:
            completed = subprocess.run(
                [
                    "dotnet",
                    "test",
                    mutant["project"],
                    "-c",
                    "Release",
                    "--no-restore",
                    "--filter",
                    mutant["filter"],
                    "--verbosity",
                    "minimal",
                ],
                text=True,
                capture_output=True,
                check=False,
            )
        finally:
            source.write_bytes(original_bytes)

        elapsed = time.monotonic() - started
        combined = completed.stdout + completed.stderr
        log_path = logs / f"{mutant['id']}.log"
        log_path.write_text(combined, encoding="utf-8")
        status = test_status(completed.returncode, combined)
        results.append(
            {
                "id": mutant["id"],
                "component": mutant["component"],
                "file": mutant["file"],
                "testProject": mutant["project"],
                "testFilter": mutant["filter"],
                "status": status,
                "elapsedSeconds": round(elapsed, 3),
            }
        )
        print(f"{mutant['component']}: {status} ({elapsed:.2f}s)")

    killed = sum(result["status"] == "killed" for result in results)
    total = len(results)
    score = 100 * killed / total if total else 0
    report = {
        "schemaVersion": 1,
        "killed": killed,
        "total": total,
        "score": round(score, 2),
        "minimumScore": float(baseline["minimumScore"]),
        "mutants": results,
    }
    output_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if any(result["status"] == "error" for result in results):
        print("ERROR: mutation execution had build or infrastructure errors")
        return 1
    if score < float(baseline["minimumScore"]):
        print(f"ERROR: mutation score {score:.2f}% is below the ratchet")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
