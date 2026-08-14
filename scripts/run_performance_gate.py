#!/usr/bin/env python3
"""Performance ratchet, measured RELATIVE to the runner it happens to land on.

An absolute wall-clock ceiling does not survive shared CI. The previous gate
capped the suite's median at 21.0s, a number calibrated on a dev machine that
runs it in ~19.8s. Measured CI medians were 20.618, 20.692, 21.209 and 22.391s,
with individual samples reaching 22.962s — run-to-run spread larger than the
remaining headroom. The gate therefore failed on how busy the runner was rather
than on any regression, and blocked a release doing it (#965).

So the gate normalises. Each run also times a CALIBRATION invocation: the same
test host, filtered to a single trivial test, so its duration is dominated by
process start, assembly load and JIT. That is a probe of how fast this runner
is right now, and it is deliberately insensitive to the compiler analysis the
suite is measuring. The ratio between them is what is ratcheted:

    slower runner   -> suite and calibration both rise -> ratio flat  -> pass
    real regression -> suite rises, calibration does not -> ratio rises -> fail

Absolute seconds stay in the report so trends remain visible; they are simply
not the gate.
"""
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
    """Negative tests: the gate must fail closed on a regression and on a
    calibration that would divide the signal away."""
    median, passed = assess([8.0, 9.0, 40.0], 7.5)
    if passed or median != 9.0:
        raise AssertionError("performance threshold regression was not detected")

    # A regression at constant runner speed must trip the ratio.
    ratio, ratio_passed = assess([20.0 / 2.0, 30.0 / 2.0, 30.0 / 2.0], 12.0)
    if ratio_passed or ratio != 15.0:
        raise AssertionError("ratio regression was not detected")

    # A uniformly slower runner must NOT trip it: both terms scale together.
    slow, slow_passed = assess([40.0 / 4.0, 40.0 / 4.0, 40.0 / 4.0], 12.0)
    if not slow_passed or slow != 10.0:
        raise AssertionError("uniform runner slowdown was wrongly reported as a regression")

    print("Performance ratchet negative self-tests passed.")


def run_once(command: list[str], log: Path) -> tuple[float, int]:
    """Run a command, stream its output to `log`, return (elapsed, returncode).

    Streams rather than capturing through pipes so the output survives the job
    being killed and is written even if the call raises. Runs of this gate were
    terminated on 2026-08-13 with SIGTERM at varying elapsed times and produced
    no output at all to diagnose; root cause is not established (#965).
    """
    started = time.monotonic()
    with log.open("w", encoding="utf-8") as sink:
        completed = subprocess.run(command, stdout=sink, stderr=subprocess.STDOUT, check=False)
    return time.monotonic() - started, completed.returncode


def test_command(results_dir: Path, test_filter: str | None = None) -> list[str]:
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
    if test_filter:
        command += ["--filter", test_filter]
    return command


def counters(trx: Path) -> tuple[int, int]:
    node = ET.parse(trx).find(".//{*}Counters")
    if node is None:
        return 0, 0
    return (
        int(node.attrib.get("executed", "0")),
        int(node.attrib.get("passed", "0")),
    )


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
    calibration_runs = int(baseline["calibrationRuns"])
    calibration_filter = baseline["calibrationFilter"]
    expected_tests = int(baseline["expectedTestCount"])
    metric = baseline["metrics"]["performance-suite-ratio"]
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    log_dir = output.parent / "logs"
    log_dir.mkdir(exist_ok=True)

    # ── calibration: how fast is this runner, independent of the suite ──
    calibration_samples: list[float] = []
    for index in range(calibration_runs):
        results_dir = log_dir / f"calibration-results-{index + 1}"
        log = log_dir / f"calibration-{index + 1}.log"
        elapsed, code = run_once(test_command(results_dir, calibration_filter), log)
        if code != 0:
            print(f"ERROR: calibration run failed; see {log}")
            return 1
        trx = results_dir / "results.trx"
        if not trx.is_file():
            print(f"ERROR: calibration run produced no TRX report; see {log}")
            return 1
        executed, passed_count = counters(trx)
        # Exactly one test, and it must pass — otherwise the calibration is not
        # measuring what it claims, and a broken filter would silently inflate
        # the denominator and hide a regression.
        if executed != 1 or passed_count != 1:
            print(
                f"ERROR: calibration filter matched {executed} tests with "
                f"{passed_count} passing; expected exactly 1. See {trx}"
            )
            return 1
        calibration_samples.append(elapsed)
        print(f"calibration run {index + 1}: {elapsed:.3f}s")

    calibration = statistics.median(calibration_samples)
    if calibration <= 0:
        print("ERROR: calibration median was not positive")
        return 1

    # ── the suite itself ──
    samples: list[float] = []
    for index in range(warmups + measured):
        results_dir = log_dir / f"results-{index + 1}"
        kind = "warmup" if index < warmups else "measured"
        log = log_dir / f"{kind}-{index + 1}.log"
        elapsed, code = run_once(test_command(results_dir), log)
        if code != 0:
            print(f"ERROR: performance test run failed; see {log}")
            return 1
        trx = results_dir / "results.trx"
        if not trx.is_file():
            print(f"ERROR: performance test run produced no TRX report; see {log}")
            return 1
        executed, passed_count = counters(trx)
        if executed != expected_tests or passed_count != expected_tests:
            print(
                f"ERROR: performance run executed {executed}/{expected_tests} "
                f"expected tests with {passed_count} passing; see {trx}"
            )
            return 1
        if index >= warmups:
            samples.append(elapsed)
        print(f"{kind} run {index + 1}: {elapsed:.3f}s")

    maximum = float(metric["maximumMedian"])
    ratios = [sample / calibration for sample in samples]
    ratio, passed = assess(ratios, maximum)
    seconds_median = statistics.median(samples)

    report = {
        "schemaVersion": 2,
        "metric": "performance-suite-ratio",
        "calibrationSamples": [round(sample, 3) for sample in calibration_samples],
        "calibrationMedian": round(calibration, 3),
        "samples": [round(sample, 3) for sample in samples],
        "secondsMedian": round(seconds_median, 3),
        "ratios": [round(value, 3) for value in ratios],
        "ratioMedian": round(ratio, 3),
        "maximumMedian": maximum,
        "noisePolicy": metric["noisePolicy"],
        "passed": passed,
    }
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(
        f"suite median: {seconds_median:.3f}s / calibration {calibration:.3f}s "
        f"= ratio {ratio:.3f} (maximum {maximum:.3f})"
    )
    if not passed:
        print("ERROR: performance ratio exceeded the ratcheted maximum")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
