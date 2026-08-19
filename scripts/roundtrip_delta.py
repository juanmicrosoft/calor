#!/usr/bin/env python3
"""Round-trip delta alarm helper (issue #1005).

Reads the committed round-trip baseline (bench/roundtrip-baseline.json) and the
per-project reports produced by the round-trip harness (*-roundtrip.json under
the reports directory), then emits a Markdown summary of per-project pass-count
drops. Intended for CI: warning-only, never blocks merge.

Design notes:
- Approach (A): committed baseline in bench/. Auditable in git history, doesn't
  depend on GitHub Actions artifact retention. Read-only for now; a follow-up
  will teach the harness to update the file on main-branch merges.
- Only pass-count drops trigger the alarm (per the issue). Native% and
  coverage% deltas are surfaced when the pass count already regressed, for
  context.
- Projects present in the baseline but missing from the current run are flagged
  as MISSING (usually means the harness crashed for that corpus). New projects
  in the current run that aren't in the baseline are ignored (no comparison).

Usage:
    python3 scripts/roundtrip_delta.py \\
        --baseline bench/roundtrip-baseline.json \\
        --reports conversion-reports/ \\
        --output-markdown delta.md

    python3 scripts/roundtrip_delta.py --self-test

Exit codes:
    0: script ran (regardless of whether regressions were found)
    2: usage error or missing input file
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import sys
import tempfile
from typing import Any


COMMENT_MARKER = "<!-- calor-roundtrip-delta -->"


def load_baseline(path: str) -> dict[str, dict[str, Any]]:
    """Load the committed baseline. Returns {project_name: metrics}."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return data.get("projects", {})


def load_reports(reports_dir: str) -> dict[str, dict[str, Any]]:
    """Load all *-roundtrip.json under the reports directory."""
    out: dict[str, dict[str, Any]] = {}
    if not os.path.isdir(reports_dir):
        return out
    for path in sorted(glob.glob(os.path.join(reports_dir, "*-roundtrip.json"))):
        try:
            with open(path, encoding="utf-8") as f:
                report = json.load(f)
        except (json.JSONDecodeError, OSError):
            continue
        project = report.get("project")
        if not project:
            continue
        rt = report.get("round_trip") or {}
        fidelity = report.get("fidelity") or {}
        coverage = fidelity.get("coverage") or {}
        out[project] = {
            "round_trip_passed": rt.get("passed"),
            "round_trip_total": rt.get("total"),
            "native_fraction": coverage.get("native_fraction"),
            "coverage_fraction": coverage.get("coverage_fraction"),
            "verdict": report.get("verdict"),
            "inconclusive": report.get("inconclusive", False),
        }
    return out


def compute_deltas(
    baseline: dict[str, dict[str, Any]],
    current: dict[str, dict[str, Any]],
) -> list[dict[str, Any]]:
    """Return a list of per-project delta records for projects that regressed
    or went missing. Empty list means no alarm."""
    deltas: list[dict[str, Any]] = []
    for project, base in baseline.items():
        cur = current.get(project)
        if cur is None:
            deltas.append({
                "project": project,
                "kind": "missing",
                "baseline_passed": base.get("round_trip_passed"),
                "baseline_total": base.get("round_trip_total"),
            })
            continue
        if cur.get("inconclusive"):
            deltas.append({
                "project": project,
                "kind": "inconclusive",
                "baseline_passed": base.get("round_trip_passed"),
                "baseline_total": base.get("round_trip_total"),
                "current_passed": cur.get("round_trip_passed"),
                "current_total": cur.get("round_trip_total"),
            })
            continue
        base_passed = base.get("round_trip_passed")
        cur_passed = cur.get("round_trip_passed")
        if base_passed is None or cur_passed is None:
            continue
        if cur_passed < base_passed:
            deltas.append({
                "project": project,
                "kind": "regressed",
                "baseline_passed": base_passed,
                "baseline_total": base.get("round_trip_total"),
                "current_passed": cur_passed,
                "current_total": cur.get("round_trip_total"),
                "passed_delta": cur_passed - base_passed,
                "baseline_native": base.get("native_fraction"),
                "current_native": cur.get("native_fraction"),
                "baseline_coverage": base.get("coverage_fraction"),
                "current_coverage": cur.get("coverage_fraction"),
                "verdict": cur.get("verdict"),
            })
    return deltas


def _fmt_pct(value: Any) -> str:
    if value is None:
        return "—"
    try:
        return f"{100.0 * float(value):.1f}%"
    except (TypeError, ValueError):
        return "—"


def format_markdown(deltas: list[dict[str, Any]]) -> str:
    """Render the delta report as a PR comment body. Returns empty string if
    no deltas (caller should not post a comment in that case)."""
    if not deltas:
        return ""
    lines = [
        COMMENT_MARKER,
        "## Round-Trip Incremental Delta (warning only)",
        "",
        "One or more corpus projects lost ground vs the committed main-branch "
        "baseline (`bench/roundtrip-baseline.json`). This is a **warning only** — "
        "absolute thresholds (per-project native% and coverage%) may still be green, "
        "and this comment does not block merge.",
        "",
        "| Project | Δ passed | Baseline | Current | Native (base → cur) | Coverage (base → cur) | Note |",
        "|---------|---------:|---------:|--------:|---------------------|----------------------|------|",
    ]
    for d in deltas:
        project = d["project"]
        if d["kind"] == "missing":
            base_ratio = f"{d['baseline_passed']}/{d['baseline_total']}"
            lines.append(
                f"| {project} | — | {base_ratio} | (no report) | — | — | "
                "harness produced no report for this project |"
            )
            continue
        if d["kind"] == "inconclusive":
            base_ratio = f"{d['baseline_passed']}/{d['baseline_total']}"
            cur_ratio = f"{d['current_passed']}/{d['current_total']}"
            lines.append(
                f"| {project} | — | {base_ratio} | {cur_ratio} | — | — | "
                "current run marked inconclusive — fidelity not adjudicated |"
            )
            continue
        base_ratio = f"{d['baseline_passed']}/{d['baseline_total']}"
        cur_ratio = f"{d['current_passed']}/{d['current_total']}"
        delta = d["passed_delta"]
        native_cell = (
            f"{_fmt_pct(d['baseline_native'])} → {_fmt_pct(d['current_native'])}"
        )
        coverage_cell = (
            f"{_fmt_pct(d['baseline_coverage'])} → {_fmt_pct(d['current_coverage'])}"
        )
        verdict_note = f"verdict: {d.get('verdict', 'n/a')}"
        lines.append(
            f"| {project} | {delta:+d} | {base_ratio} | {cur_ratio} | "
            f"{native_cell} | {coverage_cell} | {verdict_note} |"
        )
    lines.extend([
        "",
        "**What to do.** If this regression is expected (e.g. a new corpus test "
        "surfaced a known gap), update `bench/roundtrip-baseline.json` in this PR "
        "to acknowledge the new floor. Otherwise investigate whether a recent "
        "conversion or emitter change lost coverage.",
        "",
        "_Origin: [issue #1005](https://github.com/juanmicrosoft/calor/issues/1005) "
        "— round-trip corpus incremental delta alarm._",
    ])
    return "\n".join(lines) + "\n"


def _self_test() -> int:
    """Inline unit tests. Run: python3 scripts/roundtrip_delta.py --self-test"""
    failures: list[str] = []

    def check(cond: bool, msg: str) -> None:
        if not cond:
            failures.append(msg)

    # Case 1: identical baseline and current — no deltas.
    baseline = {"MediatR": {"round_trip_passed": 155, "round_trip_total": 157}}
    current = {"MediatR": {"round_trip_passed": 155, "round_trip_total": 157}}
    deltas = compute_deltas(baseline, current)
    check(deltas == [], f"case 1: expected no deltas, got {deltas}")
    check(format_markdown(deltas) == "", "case 1: expected empty markdown")

    # Case 2: pass count dropped.
    current = {"MediatR": {"round_trip_passed": 150, "round_trip_total": 157,
                            "native_fraction": 0.45, "coverage_fraction": 0.70,
                            "verdict": "pass"}}
    baseline = {"MediatR": {"round_trip_passed": 155, "round_trip_total": 157,
                             "native_fraction": 0.50, "coverage_fraction": 0.75}}
    deltas = compute_deltas(baseline, current)
    check(len(deltas) == 1, f"case 2: expected 1 delta, got {deltas}")
    check(deltas[0]["passed_delta"] == -5, "case 2: expected -5 delta")
    md = format_markdown(deltas)
    check(COMMENT_MARKER in md, "case 2: marker missing")
    check("MediatR" in md, "case 2: MediatR row missing")
    check("-5" in md, "case 2: -5 not rendered")
    check("50.0%" in md, "case 2: baseline native % not rendered")
    check("45.0%" in md, "case 2: current native % not rendered")

    # Case 3: pass count increased — no alarm.
    current = {"MediatR": {"round_trip_passed": 156, "round_trip_total": 157}}
    deltas = compute_deltas(baseline, current)
    check(deltas == [], f"case 3: expected no deltas on improvement, got {deltas}")

    # Case 4: project missing from current run.
    current = {}
    deltas = compute_deltas(baseline, current)
    check(len(deltas) == 1 and deltas[0]["kind"] == "missing",
          f"case 4: expected missing delta, got {deltas}")
    check("no report" in format_markdown(deltas),
          "case 4: 'no report' note missing")

    # Case 5: inconclusive current run.
    current = {"MediatR": {"round_trip_passed": 0, "round_trip_total": 0,
                            "inconclusive": True}}
    deltas = compute_deltas(baseline, current)
    check(len(deltas) == 1 and deltas[0]["kind"] == "inconclusive",
          f"case 5: expected inconclusive delta, got {deltas}")
    check("inconclusive" in format_markdown(deltas),
          "case 5: inconclusive note missing")

    # Case 6: end-to-end with real-shaped JSON on disk.
    with tempfile.TemporaryDirectory() as tmp:
        baseline_path = os.path.join(tmp, "baseline.json")
        with open(baseline_path, "w") as fh:
            json.dump({"projects": {
                "MediatR": {"round_trip_passed": 155, "round_trip_total": 157,
                            "native_fraction": 0.5, "coverage_fraction": 0.75},
                "Serilog": {"round_trip_passed": 811, "round_trip_total": 811},
            }}, fh)
        reports_dir = os.path.join(tmp, "reports")
        os.makedirs(reports_dir)
        # MediatR drops 5, Serilog missing.
        with open(os.path.join(reports_dir, "MediatR-roundtrip.json"), "w") as fh:
            json.dump({
                "project": "MediatR",
                "verdict": "pass",
                "round_trip": {"total": 157, "passed": 150, "failed": 5, "skipped": 2},
                "fidelity": {"coverage": {"native_fraction": 0.48,
                                          "coverage_fraction": 0.74}},
            }, fh)
        loaded_baseline = load_baseline(baseline_path)
        loaded_current = load_reports(reports_dir)
        deltas = compute_deltas(loaded_baseline, loaded_current)
        kinds = sorted(d["kind"] for d in deltas)
        check(kinds == ["missing", "regressed"],
              f"case 6: expected [missing, regressed], got {kinds}")
        md = format_markdown(deltas)
        check("Serilog" in md and "MediatR" in md,
              "case 6: both projects should appear in output")

    if failures:
        for f_msg in failures:
            print(f"FAIL: {f_msg}", file=sys.stderr)
        return 1
    print("all self-tests passed")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--baseline", help="Path to baseline JSON")
    parser.add_argument("--reports", help="Directory containing *-roundtrip.json")
    parser.add_argument("--output-markdown",
                        help="Write PR comment body here (only if regressions found)")
    parser.add_argument("--self-test", action="store_true",
                        help="Run inline unit tests and exit")
    args = parser.parse_args(argv)

    if args.self_test:
        return _self_test()

    if not args.baseline or not args.reports:
        parser.error("--baseline and --reports are required (or use --self-test)")

    if not os.path.exists(args.baseline):
        print(f"baseline file not found: {args.baseline}", file=sys.stderr)
        return 2

    baseline = load_baseline(args.baseline)
    current = load_reports(args.reports)
    deltas = compute_deltas(baseline, current)

    if not deltas:
        print("no regressions vs baseline")
        # Emit an empty markdown file to signal "nothing to post".
        if args.output_markdown:
            with open(args.output_markdown, "w", encoding="utf-8") as fh:
                fh.write("")
        return 0

    markdown = format_markdown(deltas)
    if args.output_markdown:
        with open(args.output_markdown, "w", encoding="utf-8") as fh:
            fh.write(markdown)
    else:
        sys.stdout.write(markdown)

    # Human-friendly summary to the job log.
    print(f"detected {len(deltas)} regression(s) vs baseline:")
    for d in deltas:
        print(f"  - {d['project']}: {d['kind']}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
