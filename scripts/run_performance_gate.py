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


INDEX_TEST = "IndexBuildStaysWithinItsBudget"


def trx_seconds(duration: str) -> float:
    """A TRX duration ("hh:mm:ss.fffffff") in seconds."""
    hours, minutes, seconds = duration.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(seconds)


def index_build_seconds(trx: Path) -> float | None:
    """The index-build test's own duration in one run's TRX, or None if it is absent."""
    for result in ET.parse(trx).iterfind(".//{*}UnitTestResult"):
        if result.attrib.get("testName", "").endswith(INDEX_TEST) and "duration" in result.attrib:
            return trx_seconds(result.attrib["duration"])
    return None


def self_test() -> None:
    median, passed = assess([8.0, 9.0, 40.0], 7.5)
    if passed or median != 9.0:
        raise AssertionError("performance threshold regression was not detected")
    if abs(trx_seconds("00:00:02.5000000") - 2.5) > 1e-9 or trx_seconds("00:01:01.0000000") != 61.0:
        raise AssertionError("TRX durations are not read correctly")
    median, passed = assess([trx_seconds("00:00:03.9000000"), 4.1, 2.0], 3.75)
    if passed or median != 3.9:
        raise AssertionError("index-build regression was not detected")
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
    index_metric = baseline["metrics"]["index-build-seconds"]
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    log_dir = output.parent / "logs"
    log_dir.mkdir(exist_ok=True)

    samples: list[float] = []
    index_samples: list[float] = []
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
        kind = "warmup" if index < warmups else "measured"
        log = log_dir / f"{kind}-{index + 1}.log"

        # Stream the child's output to a file rather than capturing it through
        # pipes.
        #
        # NO MECHANISM IS CLAIMED HERE. Runs of this gate were terminated on
        # 2026-08-13 with SIGTERM ("The runner has received a shutdown signal",
        # exit 143) at widely varying elapsed times — 24s, 26s, 52s, 79s, 26m —
        # and the same unmodified code then passed on main on 2026-08-14. Root
        # cause is NOT established; see #965.
        #
        # This form is preferred on its own merits, independent of that:
        #   * output survives the job being killed, instead of dying with the
        #     parent's in-memory buffer — which is why the failing runs produced
        #     no gate output at all to diagnose;
        #   * the log is written even if subprocess.run raises, which the old
        #     write-after-return form could not do;
        #   * stdout and stderr are interleaved chronologically, which is what a
        #     debugger wants. Nothing downstream reads these logs, so merging the
        #     streams costs nothing.
        started = time.monotonic()
        with log.open("w", encoding="utf-8") as sink:
            completed = subprocess.run(
                command, stdout=sink, stderr=subprocess.STDOUT, check=False
            )
        elapsed = time.monotonic() - started
        trx = results_dir / "results.trx"
        if completed.returncode != 0:
            print(f"ERROR: performance test run failed; see {log}")
            # Name the failing tests in the job log itself, so a failure can be read
            # without the uploaded artifacts.
            if trx.is_file():
                for result in ET.parse(trx).iterfind(".//{*}UnitTestResult"):
                    if result.attrib.get("outcome") == "Failed":
                        message = result.find(".//{*}Message")
                        print(f"  FAILED {result.attrib.get('testName', '?')}: "
                              f"{(message.text or '').strip()[:500] if message is not None else ''}")
            return 1
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
        # #1738: the project-index build is the suite's largest single cost and has
        # its own ceiling (index-build-seconds), from its duration in each run's TRX.
        index_seconds = index_build_seconds(trx)
        if index_seconds is None:
            print(f"ERROR: {INDEX_TEST} has no duration in {trx}")
            return 1
        if index >= warmups:
            samples.append(elapsed)
            index_samples.append(index_seconds)
        print(f"{kind} run {index + 1}: {elapsed:.3f}s ({INDEX_TEST}: {index_seconds:.3f}s)")

    maximum = float(metric["maximumMedian"])
    median, suite_passed = assess(samples, maximum)
    index_maximum = float(index_metric["maximumMedian"])
    index_median, index_passed = assess(index_samples, index_maximum)
    passed = suite_passed and index_passed
    report = {
        "schemaVersion": 1,
        "metric": "performance-suite-seconds",
        "samples": [round(sample, 3) for sample in samples],
        "median": round(median, 3),
        "maximumMedian": maximum,
        "noisePolicy": metric["noisePolicy"],
        "passed": passed,
        "indexBuild": {
            "metric": "index-build-seconds",
            "samples": [round(sample, 3) for sample in index_samples],
            "median": round(index_median, 3),
            "maximumMedian": index_maximum,
            "noisePolicy": index_metric["noisePolicy"],
            "passed": index_passed,
        },
    }
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"median: {median:.3f}s (maximum {maximum:.3f}s)")
    print(f"{INDEX_TEST} median: {index_median:.3f}s (maximum {index_maximum:.3f}s)")
    if not suite_passed:
        print("ERROR: performance median exceeded the ratcheted maximum")
    if not index_passed:
        print(f"ERROR: {INDEX_TEST} median exceeded the index-build-seconds maximum")
    if not passed:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
