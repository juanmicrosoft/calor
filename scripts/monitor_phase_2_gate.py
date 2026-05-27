#!/usr/bin/env python3
"""4-hour monitoring tick for the §10 Phase 2 gate.

Per v6 §3.3 the operator schedules this script to run every 4 hours
during the gate's 4–5 day execution window. It is the *only* automated
observer between the operator's eyes and the gate's $2–4k spend.

What it does:
    1. Reads `phase-2-gate-config.json` for the abort thresholds.
    2. Parses `results/<run>/runs.jsonl` (the driver's output).
    3. Computes: total records, harness_crash rate, harness_error
       breakdown, consecutive-failure run, and per-arm summary
       (mean turn_count, mean output_tokens, success rate).
    4. Compares against `abort_triggers` in the config.
    5. Writes a structured record to `monitor.log` (JSONL).
    6. Exits 0 if all green, 1 if any threshold tripped (so the
       scheduler surfaces an alert).

Usage:
    python3 scripts/monitor_phase_2_gate.py \
        --config docs/plans/phase-2-gate-config.json \
        --runs results/phase-2-gate-<DATE>/runs.jsonl \
        --log  results/phase-2-gate-<DATE>/monitor.log

Exit codes:
    0 — all thresholds within bounds; gate may continue
    1 — at least one abort_trigger tripped; operator review required
    2 — runs.jsonl missing or unparseable; the gate may not have
        started, or it crashed catastrophically

This script never modifies the gate run. It only observes.
"""
from __future__ import annotations

import argparse
import datetime
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


def parse_runs(runs_path: Path) -> list[dict[str, Any]]:
    """Read runs.jsonl. Tolerant of partial lines (driver still writing)."""
    if not runs_path.is_file():
        raise FileNotFoundError(f"runs.jsonl not found at {runs_path}")
    out: list[dict[str, Any]] = []
    with runs_path.open("r", encoding="utf-8") as f:
        for line_no, line in enumerate(f, start=1):
            line = line.strip()
            if not line:
                continue
            try:
                out.append(json.loads(line))
            except json.JSONDecodeError:
                # last line might be partial — driver still writing
                if line_no == sum(1 for _ in runs_path.open()):
                    continue
                # otherwise data corruption: report but keep going
                print(
                    f"warn: line {line_no} of runs.jsonl is not valid JSON; skipping",
                    file=sys.stderr,
                )
    return out


def compute_metrics(records: list[dict[str, Any]]) -> dict[str, Any]:
    """Compute the metrics referenced by abort_triggers."""
    n = len(records)
    if n == 0:
        return {
            "n_records": 0,
            "harness_crash_pct": 0.0,
            "harness_error_pct": 0.0,
            "consecutive_failures": 0,
            "harness_error_breakdown": {},
            "per_arm": {},
        }

    n_crash = sum(1 for r in records if r.get("harness_crash"))
    n_err = sum(
        1
        for r in records
        if r.get("harness_error") and r["harness_error"] != "dry_run"
    )

    # Consecutive failures, looking from the most recent record backwards.
    consec = 0
    for r in reversed(records):
        if not r.get("success", False):
            consec += 1
        else:
            break

    err_breakdown = Counter(
        r["harness_error"] for r in records if r.get("harness_error")
    )

    per_arm: dict[str, dict[str, float]] = {}
    arm_buckets: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for r in records:
        arm_buckets[str(r.get("arm", "?"))].append(r)
    for arm, recs in sorted(arm_buckets.items()):
        m = len(recs)
        per_arm[arm] = {
            "n": m,
            "success_rate_pct": (
                100.0 * sum(1 for r in recs if r.get("success")) / m if m else 0.0
            ),
            "mean_turns": (
                sum(int(r.get("turn_count", 0)) for r in recs) / m if m else 0.0
            ),
            "mean_output_tokens": (
                sum(int(r.get("total_output_tokens", 0)) for r in recs) / m
                if m
                else 0.0
            ),
        }

    return {
        "n_records": n,
        "harness_crash_pct": 100.0 * n_crash / n,
        "harness_error_pct": 100.0 * n_err / n,
        "consecutive_failures": consec,
        "harness_error_breakdown": dict(err_breakdown),
        "per_arm": per_arm,
    }


def check_triggers(
    metrics: dict[str, Any], triggers: dict[str, Any]
) -> list[str]:
    """Return list of tripped trigger names; empty list = all green."""
    tripped: list[str] = []
    if metrics["harness_crash_pct"] > float(triggers.get("harness_crash_rate_pct", 100)):
        tripped.append(
            f"harness_crash_rate_pct: {metrics['harness_crash_pct']:.2f}% "
            f"> {triggers['harness_crash_rate_pct']}%"
        )
    if metrics["harness_error_pct"] > float(triggers.get("harness_error_rate_pct", 100)):
        tripped.append(
            f"harness_error_rate_pct: {metrics['harness_error_pct']:.2f}% "
            f"> {triggers['harness_error_rate_pct']}%"
        )
    if metrics["consecutive_failures"] >= int(triggers.get("consecutive_failures", 1_000_000)):
        tripped.append(
            f"consecutive_failures: {metrics['consecutive_failures']} "
            f">= {triggers['consecutive_failures']}"
        )
    return tripped


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--config", required=True, type=Path)
    p.add_argument("--runs", required=True, type=Path)
    p.add_argument("--log", required=True, type=Path)
    args = p.parse_args()

    cfg = json.loads(args.config.read_text(encoding="utf-8"))
    triggers = cfg.get("abort_triggers", {})

    try:
        records = parse_runs(args.runs)
    except FileNotFoundError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2

    metrics = compute_metrics(records)
    tripped = check_triggers(metrics, triggers)

    now = datetime.datetime.now(datetime.timezone.utc).isoformat()
    log_record = {
        "tick_at": now,
        "runs_path": str(args.runs),
        "metrics": metrics,
        "tripped": tripped,
        "halt_recommended": bool(tripped),
    }

    args.log.parent.mkdir(parents=True, exist_ok=True)
    with args.log.open("a", encoding="utf-8") as f:
        f.write(json.dumps(log_record) + "\n")

    # Operator-readable summary on stdout.
    print(f"=== Phase 2 gate monitor — {now} ===")
    print(f"records: {metrics['n_records']}")
    print(f"harness_crash: {metrics['harness_crash_pct']:.2f}%")
    print(
        f"harness_error: {metrics['harness_error_pct']:.2f}% "
        f"({dict(metrics['harness_error_breakdown'])})"
    )
    print(f"consecutive failures: {metrics['consecutive_failures']}")
    for arm, m in metrics["per_arm"].items():
        print(
            f"  arm {arm}: n={m['n']:>3} "
            f"success={m['success_rate_pct']:>5.1f}% "
            f"mean_turns={m['mean_turns']:>5.1f} "
            f"mean_out_tok={m['mean_output_tokens']:>7.0f}"
        )
    if tripped:
        print("\n!!! HALT RECOMMENDED !!!")
        for t in tripped:
            print(f"  - {t}")
        print(
            f"\nReview {args.log} and consult "
            "docs/plans/phase-2-spend-authorisation.md §4 "
            "before deciding whether to override."
        )
        return 1
    print("\nall thresholds within bounds; gate may continue")
    return 0


if __name__ == "__main__":
    sys.exit(main())
