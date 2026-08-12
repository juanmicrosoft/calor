#!/usr/bin/env python3
import argparse
import json
import statistics
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path


def assess(samples: list[float], maximum: float) -> tuple[float, bool]:
    median = statistics.median(samples)
    return median, median <= maximum


def self_test() -> None:
    median, passed = assess([8.0, 9.0, 40.0], 7.5)
    if passed or median != 9.0:
        raise AssertionError("performance threshold regression was not detected")
    print("Performance ratchet negative self-test passed.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", default="eng/performance-baselines.json")
    parser.add_argument("--output", default="artifacts/performance/performance-report.json")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0

    baseline = json.loads(Path(args.baseline).read_text(encoding="utf-8"))
    warmups = int(baseline["warmupRuns"])
    measured = int(baseline["measuredRuns"])
    expected_tests = int(baseline["expectedTestCount"])
    metric = baseline["metrics"]["performance-suite-seconds"]
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    log_dir = output.parent / "logs"
    log_dir.mkdir(exist_ok=True)

    samples: list[float] = []
    for index in range(warmups + measured):
        results_dir = log_dir / f"results-{index + 1}"
        command = [
            "dotnet",
            "test",
            "tests/Calor.Performance.Tests/Calor.Performance.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
            "--no-restore",
            "--logger",
            "trx;LogFileName=results.trx",
            "--results-directory",
            str(results_dir),
            "--verbosity",
            "quiet",
        ]
        started = time.monotonic()
        result = subprocess.run(command, text=True, capture_output=True, check=False)
        elapsed = time.monotonic() - started
        kind = "warmup" if index < warmups else "measured"
        log = log_dir / f"{kind}-{index + 1}.log"
        log.write_text(result.stdout + result.stderr, encoding="utf-8")
        if result.returncode != 0:
            print(f"ERROR: performance test run failed; see {log}")
            return 1
        trx = results_dir / "results.trx"
        if not trx.is_file():
            print(f"ERROR: performance test run produced no TRX report; see {log}")
            return 1
        counters = ET.parse(trx).find(".//{*}Counters")
        executed = int(counters.attrib.get("executed", "0")) if counters is not None else 0
        passed = int(counters.attrib.get("passed", "0")) if counters is not None else 0
        if executed != expected_tests or passed != expected_tests:
            print(
                f"ERROR: performance run executed {executed}/{expected_tests} "
                f"expected tests with {passed} passing; see {trx}"
            )
            return 1
        if index >= warmups:
            samples.append(elapsed)
        print(f"{kind} run {index + 1}: {elapsed:.3f}s")

    maximum = float(metric["maximumMedian"])
    median, passed = assess(samples, maximum)
    report = {
        "schemaVersion": 1,
        "metric": "performance-suite-seconds",
        "samples": [round(sample, 3) for sample in samples],
        "median": round(median, 3),
        "maximumMedian": maximum,
        "noisePolicy": metric["noisePolicy"],
        "passed": passed,
    }
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"median: {median:.3f}s (maximum {maximum:.3f}s)")
    if not passed:
        print("ERROR: performance median exceeded the ratcheted maximum")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
