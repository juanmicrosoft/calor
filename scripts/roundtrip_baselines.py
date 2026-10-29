#!/usr/bin/env python3
"""Blocking roundtrip floors and all-or-nothing, monotonic baseline updates (#1030)."""

from __future__ import annotations

import argparse
import copy
from datetime import datetime
import hashlib
import json
import math
import os
from pathlib import Path
import re
import sys
import tempfile


MODES = ("lossy", "default")
PROJECTS = ("FluentValidation", "MediatR", "Serilog", "Synthetic", "Synthetic2")
COUNTS = ("round_trip_passed", "round_trip_total")
FRACTIONS = ("native_fraction", "coverage_fraction")
METRICS = COUNTS + FRACTIONS


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def unique_object(pairs: list[tuple]) -> dict:
    result = {}
    for key, value in pairs:
        require(key not in result, f"duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json(path: Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_object)
    require(isinstance(data, dict), f"{path}: expected an object")
    return data


def validate_metrics(metrics: dict, label: str) -> None:
    require(isinstance(metrics, dict) and set(metrics) == set(METRICS),
            f"{label}: expected exactly {METRICS}")
    for key in COUNTS:
        require(type(metrics[key]) is int and metrics[key] >= 0,
                f"{label}: {key} must be a nonnegative integer")
    require(metrics["round_trip_passed"] <= metrics["round_trip_total"],
            f"{label}: passed exceeds total")
    for key in FRACTIONS:
        value = metrics[key]
        require(type(value) in (float, int) and 0 <= value <= 1 and math.isfinite(value),
                f"{label}: {key} must be finite and in [0, 1]")
    require(metrics["native_fraction"] <= metrics["coverage_fraction"],
            f"{label}: native exceeds coverage")


def load_baseline(path: Path) -> dict:
    baseline = read_json(path)
    require(type(baseline.get("schema_version")) is int and baseline["schema_version"] == 1,
            "unsupported baseline schema_version")
    floors = baseline.get("blocking_floors")
    require(isinstance(floors, dict) and set(floors) == set(MODES),
            "baseline must contain blocking floors for exactly lossy and default")
    for mode in MODES:
        require(isinstance(floors[mode], dict) and set(floors[mode]) == set(PROJECTS),
                f"{mode}: baseline project inventory changed; review the updater too")
        for project in PROJECTS:
            validate_metrics(floors[mode][project], f"{mode}/{project}")
    require(isinstance(baseline.get("projects"), dict)
            and set(baseline["projects"]) == set(PROJECTS),
            "baseline must retain the Lossy advisory project inventory")
    for project in PROJECTS:
        validate_metrics(baseline["projects"][project], f"advisory/{project}")
    return baseline


def load_reports(directory: Path, mode: str) -> tuple[dict, dict]:
    require(directory.is_dir(), f"{mode}: report directory is missing: {directory}")
    reports, hashes = {}, {}
    for path in sorted(directory.glob("*-roundtrip.json")):
        report = read_json(path)
        project = report.get("project")
        require(project in PROJECTS, f"{path}: unknown project {project!r}")
        require(project not in reports, f"{mode}: duplicate project {project}")
        require(path.name == f"{project}-roundtrip.json", f"{path}: unexpected report filename")
        require(type(report.get("report_schema_version")) is int
                and report["report_schema_version"] == 2, f"{path}: expected report schema 2")
        require(report.get("conversion_mode") == mode, f"{path}: wrong or missing conversion_mode")
        require(report.get("verdict") == "pass" and report.get("inconclusive") is False
                and report.get("gate_failures") == [] and report.get("build_succeeded") is True,
                f"{path}: failed, inconclusive, or incomplete harness result")
        tests = report.get("round_trip")
        require(isinstance(tests, dict), f"{path}: missing round_trip")
        for key in ("total", "passed", "failed", "skipped"):
            require(type(tests.get(key)) is int and tests[key] >= 0, f"{path}: invalid {key}")
        require(tests["total"] > 0 and tests["passed"] > 0
                and tests["passed"] + tests["failed"] + tests["skipped"] == tests["total"],
                f"{path}: empty or inconsistent test inventory")
        fidelity = report.get("fidelity")
        require(isinstance(fidelity, dict) and isinstance(fidelity.get("coverage"), dict),
                f"{path}: missing fidelity coverage")
        coverage = fidelity["coverage"]
        metrics = {
            "round_trip_passed": tests["passed"],
            "round_trip_total": tests["total"],
            "native_fraction": coverage.get("native_fraction"),
            "coverage_fraction": coverage.get("coverage_fraction"),
        }
        validate_metrics(metrics, f"{mode}/{project}")
        reports[project] = metrics
        hashes[f"{mode}/{path.name}"] = hashlib.sha256(path.read_bytes()).hexdigest()
    require(set(reports) == set(PROJECTS),
            f"{mode}: incomplete project inventory; missing {sorted(set(PROJECTS) - set(reports))}")
    return reports, hashes


def check_floors(baseline: dict, reports: dict, mode: str) -> None:
    failures = []
    for project in PROJECTS:
        for metric in METRICS:
            floor = baseline["blocking_floors"][mode][project][metric]
            actual = reports[project][metric]
            if actual < floor:
                failures.append(f"{mode}/{project}: {metric} {actual} is below floor {floor}")
    require(not failures, "\n".join(failures))


def updated_baseline(baseline: dict, reports_root: Path, source_run: str,
                     source_commit: str, updated_at: str) -> dict:
    require(re.fullmatch(r"https://github\.com/[^/]+/[^/]+/actions/runs/[0-9]+", source_run)
            is not None, "source-run must be a GitHub Actions run URL")
    require(re.fullmatch(r"[0-9a-f]{40}", source_commit) is not None,
            "source-commit must be a full Git SHA")
    require(re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z", updated_at)
            is not None, "updated-at must be a UTC timestamp (YYYY-MM-DDTHH:MM:SSZ)")
    datetime.strptime(updated_at, "%Y-%m-%dT%H:%M:%SZ")
    result = copy.deepcopy(baseline)
    hashes = {}
    for mode in MODES:
        reports, mode_hashes = load_reports(reports_root / mode, mode)
        check_floors(baseline, reports, mode)
        hashes.update(mode_hashes)
        result["blocking_floors"][mode] = reports
        if mode == "lossy":
            # Historical advisory values can exceed today's blocking floors.
            # Preserve them rather than laundering that old regression.
            for project in PROJECTS:
                result["projects"][project] = {
                    metric: max(baseline["projects"][project][metric], reports[project][metric])
                    for metric in METRICS
                }
    if result == baseline:
        return result
    result["_source_run"] = source_run
    result["_updated_at"] = updated_at
    result["_writeback"] = {
        "source_commit": source_commit,
        "report_sha256": hashes,
    }
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("check", "update"))
    parser.add_argument("--baseline", type=Path, default=Path("eng/roundtrip-baselines.json"))
    parser.add_argument("--reports", type=Path, required=True)
    parser.add_argument("--mode", choices=MODES)
    parser.add_argument("--source-run")
    parser.add_argument("--source-commit")
    parser.add_argument("--updated-at")
    args = parser.parse_args(argv)
    try:
        baseline = load_baseline(args.baseline)
        if args.command == "check":
            require(args.mode in MODES, "check requires --mode")
            reports, _ = load_reports(args.reports, args.mode)
            check_floors(baseline, reports, args.mode)
            print(f"{args.mode}: all five projects meet the committed blocking floors")
        else:
            require(all((args.source_run, args.source_commit, args.updated_at)),
                    "update requires --source-run, --source-commit and --updated-at")
            result = updated_baseline(baseline, args.reports, args.source_run,
                                      args.source_commit, args.updated_at)
            if result == baseline:
                print("No floor improvements; baseline unchanged")
                return 0
            # Validate the entire batch before replacing the sole output.
            temporary = None
            try:
                with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", delete=False,
                                                 dir=args.baseline.parent) as output:
                    temporary = Path(output.name)
                    json.dump(result, output, indent=2, allow_nan=False)
                    output.write("\n")
                os.replace(temporary, args.baseline)
            finally:
                if temporary is not None:
                    temporary.unlink(missing_ok=True)
            print(f"Raised blocking floors in {args.baseline}; source {args.source_commit}")
    except (OSError, ValueError, TypeError, KeyError) as error:
        print(f"roundtrip baseline error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
