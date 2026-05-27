#!/usr/bin/env python3
"""
analyze_gate_results.py — Tier 3 / §10 gate analysis pipeline.

Reads per-run JSONL output from scripts/run_phase_2_gate.py
(default: results/runs.jsonl) and produces
docs/plans/phase-2-measurement-results.md with the four RFC §10.3 kill
criteria computed.

Pinned analysis seed: 42. Bonferroni correction: alpha' = 0.0125.

Per RFC §16.E item 8, this script writes the results document
regardless of pass/fail.

Usage:
    python3 scripts/analyze_gate_results.py \\
        --runs results/runs.jsonl \\
        --output docs/plans/phase-2-measurement-results.md \\
        [--seed 42] [--alpha 0.0125]
"""

from __future__ import annotations

import argparse
import json
import math
import statistics
import sys
from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

# ---------------------------------------------------------------------------
# Per-RFC §10.3
ALPHA_BONFERRONI = 0.0125  # 0.05 / 4 criteria
CLIFFS_DELTA_MEDIUM = 0.33  # medium effect threshold
TURN_COUNT_REDUCTION_THRESHOLD = 0.10  # 10%
TOKEN_COUNT_REDUCTION_THRESHOLD = 0.15  # 15%
DEFAULT_SEED = 42

# ---------------------------------------------------------------------------
# Lightweight stats — avoid hard scipy dependency at import time so the
# script can at least produce a partial report when scipy is not installed.
try:
    from scipy import stats as _scipy_stats  # type: ignore

    _SCIPY = True
except Exception:  # pragma: no cover - environmental
    _scipy_stats = None  # type: ignore
    _SCIPY = False


@dataclass(frozen=True)
class RunRecord:
    task_id: str
    arm: str  # 'A' | 'B' | 'C'
    seed: int
    model: str
    success: bool
    turn_count: int
    total_output_tokens: int
    identity_preservation_errors: int
    edit_correctness_errors: int
    harness_crash: bool


# ---------------------------------------------------------------------------


def load_runs(path: Path) -> list[RunRecord]:
    records: list[RunRecord] = []
    with path.open("r", encoding="utf-8") as fh:
        for line_no, raw in enumerate(fh, start=1):
            raw = raw.strip()
            if not raw:
                continue
            try:
                obj = json.loads(raw)
            except json.JSONDecodeError as exc:
                raise SystemExit(
                    f"analyze_gate_results: malformed JSON at "
                    f"{path}:{line_no}: {exc}"
                )
            records.append(
                RunRecord(
                    task_id=str(obj["task_id"]),
                    arm=str(obj["arm"]),
                    seed=int(obj["seed"]),
                    model=str(obj.get("model", "<unknown>")),
                    success=bool(obj["success"]),
                    turn_count=int(obj["turn_count"]),
                    total_output_tokens=int(obj["total_output_tokens"]),
                    identity_preservation_errors=int(
                        obj.get("identity_preservation_errors", 0)
                    ),
                    edit_correctness_errors=int(
                        obj.get("edit_correctness_errors", 0)
                    ),
                    harness_crash=bool(obj.get("harness_crash", False)),
                )
            )
    return records


def group_by(
    runs: Iterable[RunRecord], key: str
) -> dict[tuple, list[RunRecord]]:
    grouped: dict[tuple, list[RunRecord]] = defaultdict(list)
    for r in runs:
        if key == "arm":
            grouped[(r.arm,)].append(r)
        elif key == "task_arm":
            grouped[(r.task_id, r.arm)].append(r)
        else:
            raise ValueError(f"unknown grouping key: {key}")
    return dict(grouped)


# ---------------------------------------------------------------------------
# Criterion 1 — McNemar success-rate non-inferiority (Arm C vs Arm A)
# Paired by (task_id, seed): a "discordant pair" is when one arm succeeded
# and the other did not.


def mcnemar_pvalue(b: int, c: int) -> float:
    """Two-sided McNemar (exact binomial when b+c is small, else chi-square)."""
    n = b + c
    if n == 0:
        return 1.0
    if n < 25:
        # Exact two-sided binomial test, P = 0.5.
        k = min(b, c)
        # Sum binomial probabilities for tails.
        cum = 0.0
        for i in range(0, k + 1):
            cum += math.comb(n, i) * (0.5**n)
        return min(1.0, 2.0 * cum)
    # Chi-square with continuity correction.
    chi = ((abs(b - c) - 1) ** 2) / (b + c)
    # Survival function of chi-square with 1 df: erfc(sqrt(chi/2)).
    return math.erfc(math.sqrt(chi / 2.0))


def criterion_1(runs: list[RunRecord]) -> dict:
    by_pair: dict[tuple[str, int], dict[str, bool]] = defaultdict(dict)
    for r in runs:
        if r.arm in ("A", "C"):
            by_pair[(r.task_id, r.seed)][r.arm] = r.success
    b = c = 0  # b = A succ, C fail; c = A fail, C succ.
    n_pairs = 0
    for arms in by_pair.values():
        if "A" not in arms or "C" not in arms:
            continue
        n_pairs += 1
        if arms["A"] and not arms["C"]:
            b += 1
        elif not arms["A"] and arms["C"]:
            c += 1
    p = mcnemar_pvalue(b, c)
    a_succ_rate = sum(1 for r in runs if r.arm == "A" and r.success) / max(
        1, sum(1 for r in runs if r.arm == "A")
    )
    c_succ_rate = sum(1 for r in runs if r.arm == "C" and r.success) / max(
        1, sum(1 for r in runs if r.arm == "C")
    )
    passes = p > ALPHA_BONFERRONI
    return {
        "name": "Criterion 1: Success-rate non-inferiority (Arm C vs Arm A)",
        "n_pairs": n_pairs,
        "discordant_b_A_only": b,
        "discordant_c_C_only": c,
        "p_value": p,
        "arm_A_success_rate": a_succ_rate,
        "arm_C_success_rate": c_succ_rate,
        "pass_condition": f"McNemar p > {ALPHA_BONFERRONI} (non-inferiority)",
        "passes": passes,
    }


# ---------------------------------------------------------------------------
# Wilcoxon signed-rank (paired) — fallback to scipy if available.


def wilcoxon_signed_rank(x: list[float], y: list[float]) -> float:
    if len(x) != len(y) or len(x) == 0:
        return 1.0
    if _SCIPY:
        try:
            stat = _scipy_stats.wilcoxon(x, y, alternative="two-sided")  # type: ignore
            return float(stat.pvalue)
        except ValueError:
            return 1.0  # all-zero differences
    # Fallback: normal approximation, two-sided.
    diffs = [(xi - yi) for xi, yi in zip(x, y) if (xi - yi) != 0]
    n = len(diffs)
    if n == 0:
        return 1.0
    abs_ranks = sorted(((abs(d), i) for i, d in enumerate(diffs)))
    ranks: dict[int, float] = {}
    i = 0
    while i < n:
        j = i
        while j + 1 < n and abs_ranks[j + 1][0] == abs_ranks[i][0]:
            j += 1
        avg_rank = (i + j) / 2.0 + 1.0
        for k in range(i, j + 1):
            ranks[abs_ranks[k][1]] = avg_rank
        i = j + 1
    w_plus = sum(ranks[i] for i, d in enumerate(diffs) if d > 0)
    mean_w = n * (n + 1) / 4.0
    var_w = n * (n + 1) * (2 * n + 1) / 24.0
    if var_w == 0:
        return 1.0
    z = (w_plus - mean_w) / math.sqrt(var_w)
    # Two-sided p from normal CDF.
    p = math.erfc(abs(z) / math.sqrt(2.0))
    return min(1.0, max(0.0, p))


def cliffs_delta(x: list[float], y: list[float]) -> float:
    """|delta| = |#(x>y) - #(x<y)| / (n*m). Sign: positive when x > y."""
    nx, ny = len(x), len(y)
    if nx == 0 or ny == 0:
        return 0.0
    gt = lt = 0
    for xi in x:
        for yi in y:
            if xi > yi:
                gt += 1
            elif xi < yi:
                lt += 1
    return (gt - lt) / (nx * ny)


# ---------------------------------------------------------------------------
# Criterion 2 — Identity-preservation non-regression (Arm C vs Arm A)


def criterion_2(runs: list[RunRecord]) -> dict:
    paired = _pair_metric(runs, "A", "C", lambda r: r.identity_preservation_errors)
    a_vals = [p[0] for p in paired]
    c_vals = [p[1] for p in paired]
    p = wilcoxon_signed_rank(a_vals, c_vals)
    passes = p > ALPHA_BONFERRONI or (sum(c_vals) <= sum(a_vals))
    return {
        "name": (
            "Criterion 2: Identity-preservation non-regression "
            "(Arm C vs Arm A)"
        ),
        "n_pairs": len(paired),
        "arm_A_errors_total": sum(a_vals),
        "arm_C_errors_total": sum(c_vals),
        "wilcoxon_p_value": p,
        "pass_condition": (
            f"Wilcoxon p > {ALPHA_BONFERRONI} (non-regression) "
            "OR Arm C errors <= Arm A errors"
        ),
        "passes": passes,
    }


def _pair_metric(
    runs: list[RunRecord],
    arm_a: str,
    arm_b: str,
    metric_fn,
) -> list[tuple[float, float]]:
    a_map: dict[tuple[str, int], float] = {}
    b_map: dict[tuple[str, int], float] = {}
    for r in runs:
        key = (r.task_id, r.seed)
        if r.arm == arm_a:
            a_map[key] = float(metric_fn(r))
        elif r.arm == arm_b:
            b_map[key] = float(metric_fn(r))
    paired = []
    for key, av in a_map.items():
        if key in b_map:
            paired.append((av, b_map[key]))
    return paired


# ---------------------------------------------------------------------------
# Criterion 3 — Material agent benefit on turn-count OR token-count
# (Arm C vs Arm A).


def _eval_metric_benefit(
    runs: list[RunRecord],
    metric_fn,
    reduction_threshold: float,
    label: str,
) -> dict:
    paired = _pair_metric(runs, "A", "C", metric_fn)
    a_vals = [p[0] for p in paired]
    c_vals = [p[1] for p in paired]
    if not a_vals:
        return {
            "metric": label,
            "median_A": float("nan"),
            "median_C": float("nan"),
            "median_reduction": 0.0,
            "wilcoxon_p_value": 1.0,
            "cliffs_delta": 0.0,
            "passes": False,
            "n_pairs": 0,
        }
    median_a = statistics.median(a_vals)
    median_c = statistics.median(c_vals)
    reduction = (median_a - median_c) / median_a if median_a else 0.0
    p = wilcoxon_signed_rank(a_vals, c_vals)
    delta = cliffs_delta(a_vals, c_vals)
    passes = (
        reduction >= reduction_threshold
        and p < ALPHA_BONFERRONI
        and abs(delta) >= CLIFFS_DELTA_MEDIUM
    )
    return {
        "metric": label,
        "n_pairs": len(paired),
        "median_A": median_a,
        "median_C": median_c,
        "median_reduction": reduction,
        "wilcoxon_p_value": p,
        "cliffs_delta": delta,
        "passes": passes,
    }


def criterion_3(runs: list[RunRecord]) -> dict:
    turn = _eval_metric_benefit(
        runs, lambda r: r.turn_count, TURN_COUNT_REDUCTION_THRESHOLD, "turn_count"
    )
    tok = _eval_metric_benefit(
        runs,
        lambda r: r.total_output_tokens,
        TOKEN_COUNT_REDUCTION_THRESHOLD,
        "total_output_tokens",
    )
    return {
        "name": (
            "Criterion 3: Material agent benefit on turn-count OR "
            "token-count (Arm C vs Arm A)"
        ),
        "turn_count_branch": turn,
        "token_count_branch": tok,
        "passes": turn["passes"] or tok["passes"],
        "winning_branch": (
            "turn_count" if turn["passes"] else
            ("total_output_tokens" if tok["passes"] else None)
        ),
        "pass_condition": (
            f"(turn_count median reduction >= {TURN_COUNT_REDUCTION_THRESHOLD:.0%} "
            f"AND Wilcoxon p < {ALPHA_BONFERRONI} AND |Cliff's δ| >= "
            f"{CLIFFS_DELTA_MEDIUM}) OR (total_output_tokens median reduction "
            f">= {TOKEN_COUNT_REDUCTION_THRESHOLD:.0%} AND Wilcoxon p < "
            f"{ALPHA_BONFERRONI} AND |Cliff's δ| >= {CLIFFS_DELTA_MEDIUM})"
        ),
    }


# ---------------------------------------------------------------------------
# Criterion 4 — Phase 2 distinguishable from Phase 1 (Arm C vs Arm B)
# on criterion (3)'s winning metric.


def criterion_4(runs: list[RunRecord], c3: dict) -> dict:
    metric_name = c3["winning_branch"]
    if metric_name is None:
        # Criterion 3 did not pass, so criterion 4 is moot. Per RFC §10.3
        # a single red criterion fails the gate; we still compute c4
        # for both metrics to leave a record in the report.
        turn_p = _arm_vs_arm_wilcoxon(runs, "B", "C", lambda r: r.turn_count)
        tok_p = _arm_vs_arm_wilcoxon(
            runs, "B", "C", lambda r: r.total_output_tokens
        )
        return {
            "name": (
                "Criterion 4: Phase 2 distinguishable from Phase 1 "
                "(Arm C vs Arm B) on criterion 3's winning metric"
            ),
            "note": "Criterion 3 did not pass; reporting both metrics.",
            "turn_count_wilcoxon_p_value": turn_p,
            "token_count_wilcoxon_p_value": tok_p,
            "passes": False,
        }
    metric_fn = (
        (lambda r: r.turn_count)
        if metric_name == "turn_count"
        else (lambda r: r.total_output_tokens)
    )
    p = _arm_vs_arm_wilcoxon(runs, "B", "C", metric_fn)
    passes = p < ALPHA_BONFERRONI
    return {
        "name": (
            "Criterion 4: Phase 2 distinguishable from Phase 1 "
            "(Arm C vs Arm B) on criterion 3's winning metric"
        ),
        "metric": metric_name,
        "wilcoxon_p_value": p,
        "pass_condition": f"Wilcoxon p < {ALPHA_BONFERRONI}",
        "passes": passes,
    }


def _arm_vs_arm_wilcoxon(
    runs: list[RunRecord], arm_a: str, arm_b: str, metric_fn
) -> float:
    paired = _pair_metric(runs, arm_a, arm_b, metric_fn)
    if not paired:
        return 1.0
    a_vals = [p[0] for p in paired]
    b_vals = [p[1] for p in paired]
    return wilcoxon_signed_rank(a_vals, b_vals)


# ---------------------------------------------------------------------------


def render_report(
    runs: list[RunRecord],
    c1: dict,
    c2: dict,
    c3: dict,
    c4: dict,
    seed: int,
) -> str:
    n_total = len(runs)
    arms = sorted(set(r.arm for r in runs))
    tasks = sorted(set(r.task_id for r in runs))
    models = sorted(set(r.model for r in runs))
    all_pass = (
        c1["passes"] and c2["passes"] and c3["passes"] and c4["passes"]
    )
    decision = "SHIP Phase 2" if all_pass else "DO NOT SHIP Phase 2 — revert per RFC §11"

    now_utc = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    lines = [
        "# Phase 2 Measurement Results",
        "",
        f"**Generated:** {now_utc}",
        f"**Analysis seed:** {seed}",
        f"**Total runs:** {n_total}",
        f"**Arms present:** {', '.join(arms) or '<none>'}",
        f"**Tasks present:** {len(tasks)}",
        f"**Models present:** {', '.join(models) or '<none>'}",
        f"**Bonferroni-corrected alpha':** {ALPHA_BONFERRONI}",
        "",
        f"## Ship/no-ship decision: **{decision}**",
        "",
        "Decision rule: all four RFC §10.3 kill criteria must be PASS.",
        "",
        "---",
        "",
        "## Criterion 1",
        f"**{c1['name']}**",
        "",
        f"- Pairs (task × seed): {c1['n_pairs']}",
        f"- Discordant pairs (A succ / C fail): {c1['discordant_b_A_only']}",
        f"- Discordant pairs (A fail / C succ): {c1['discordant_c_C_only']}",
        f"- Arm A success rate: {c1['arm_A_success_rate']:.3f}",
        f"- Arm C success rate: {c1['arm_C_success_rate']:.3f}",
        f"- McNemar p-value: {c1['p_value']:.6f}",
        f"- Pass condition: {c1['pass_condition']}",
        f"- **Result: {'PASS' if c1['passes'] else 'FAIL'}**",
        "",
        "## Criterion 2",
        f"**{c2['name']}**",
        "",
        f"- Pairs (task × seed): {c2['n_pairs']}",
        f"- Arm A identity-preservation errors: {c2['arm_A_errors_total']}",
        f"- Arm C identity-preservation errors: {c2['arm_C_errors_total']}",
        f"- Wilcoxon p-value: {c2['wilcoxon_p_value']:.6f}",
        f"- Pass condition: {c2['pass_condition']}",
        f"- **Result: {'PASS' if c2['passes'] else 'FAIL'}**",
        "",
        "## Criterion 3",
        f"**{c3['name']}**",
        "",
    ]
    for branch_key in ("turn_count_branch", "token_count_branch"):
        b = c3[branch_key]
        lines += [
            f"### Branch: {b['metric']}",
            f"- Pairs: {b['n_pairs']}",
            f"- Median Arm A: {b['median_A']:.3f}",
            f"- Median Arm C: {b['median_C']:.3f}",
            f"- Median reduction: {b['median_reduction']*100:.2f}%",
            f"- Wilcoxon p-value: {b['wilcoxon_p_value']:.6f}",
            f"- |Cliff's δ|: {abs(b['cliffs_delta']):.4f} (sign: "
            f"{'A>C' if b['cliffs_delta']>0 else 'A<C'})",
            f"- Branch result: {'PASS' if b['passes'] else 'FAIL'}",
            "",
        ]
    lines += [
        f"- Pass condition: {c3['pass_condition']}",
        f"- Winning branch: {c3['winning_branch'] or '<none>'}",
        f"- **Result: {'PASS' if c3['passes'] else 'FAIL'}**",
        "",
        "## Criterion 4",
        f"**{c4['name']}**",
        "",
    ]
    if c4.get("note"):
        lines += [
            f"- Note: {c4['note']}",
            f"- Turn-count Wilcoxon p-value: "
            f"{c4['turn_count_wilcoxon_p_value']:.6f}",
            f"- Token-count Wilcoxon p-value: "
            f"{c4['token_count_wilcoxon_p_value']:.6f}",
        ]
    else:
        lines += [
            f"- Metric: {c4['metric']}",
            f"- Wilcoxon p-value: {c4['wilcoxon_p_value']:.6f}",
            f"- Pass condition: {c4['pass_condition']}",
        ]
    lines += [
        f"- **Result: {'PASS' if c4['passes'] else 'FAIL'}**",
        "",
        "---",
        "",
        "## Summary table",
        "",
        "| # | Criterion | Result |",
        "|---|-----------|--------|",
        f"| 1 | Success-rate non-inferiority | {'PASS' if c1['passes'] else 'FAIL'} |",
        f"| 2 | Identity-preservation non-regression | {'PASS' if c2['passes'] else 'FAIL'} |",
        f"| 3 | Material agent benefit (turn or token) | {'PASS' if c3['passes'] else 'FAIL'} |",
        f"| 4 | Phase 2 distinguishable from Phase 1 | {'PASS' if c4['passes'] else 'FAIL'} |",
        "",
        f"**Decision: {decision}**",
        "",
        "Per RFC §16.E item 8, this document is committed regardless of",
        "pass/fail.",
    ]
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------------------


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--runs", default="results/runs.jsonl",
                   help="Path to JSONL run records.")
    p.add_argument("--output", default="docs/plans/phase-2-measurement-results.md",
                   help="Output Markdown path.")
    p.add_argument("--seed", type=int, default=DEFAULT_SEED)
    args = p.parse_args(argv)

    runs_path = Path(args.runs)
    if not runs_path.exists():
        print(f"analyze_gate_results: --runs file not found: {runs_path}",
              file=sys.stderr)
        return 2
    runs = load_runs(runs_path)
    if not runs:
        print("analyze_gate_results: no runs in JSONL", file=sys.stderr)
        return 2

    c1 = criterion_1(runs)
    c2 = criterion_2(runs)
    c3 = criterion_3(runs)
    c4 = criterion_4(runs, c3)

    md = render_report(runs, c1, c2, c3, c4, args.seed)
    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(md, encoding="utf-8")
    print(f"analyze_gate_results: wrote {out}")
    all_pass = c1["passes"] and c2["passes"] and c3["passes"] and c4["passes"]
    return 0 if all_pass else 1


if __name__ == "__main__":
    sys.exit(main())
