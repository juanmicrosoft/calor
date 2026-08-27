#!/usr/bin/env python3
"""PP-E1 leg B adjudicator — "no large loop tax" (annex A-1.11, §A.2 row PP-E1).

WRITTEN BEFORE THE EPOCH RAN. The rule below is a transcription of the frozen
row, not a derivation: nothing here may be re-tuned once numbers exist.

    Leg B FAILS iff BOTH
      (a) the one-sided 95% two-level cluster-bootstrap LOWER bound of the
          median paired per-pair output-tokens-to-green ratio
          (0.15.0 release build / v0.14.3 release tag) exceeds 1.0, AND
      (b) the point estimate of that median exceeds 1.35.
    UNDERPOWERED (the PP-L5 pattern) iff the point estimate exceeds 1.35 with
      the bound leg not firing, OR the realized median within-cell CV exceeds
      0.66 (= 1.5 x the 0.4392 the margin was calibrated on).

Per-run figure: `tokens.output` AS DERIVED BY token-usage.py (annex A-1.9.1,
#881) — the sum of modelUsage[*].outputTokens over the whole run. The naive
`usage.output_tokens` is archived for audit and is never adjudicated here.

Validity floor (route (c) of the frozen outcome map; the PP-W5 floor, A-1.4),
applied BEFORE any statistic:
  - a cell (pair x arm) with < 2 valid runs drops its PAIR, disclosed;
  - fewer than 3 surviving pairs, OR either arm below 12 valid runs, is
    INVALID — reported, not adjudicated;
  - either arm above the §2 censoring cap (40% censored on neutral tasks) is
    INVALID;
  - the two arms must record DISTINCT repo roots and DISTINCT Calor.Tasks
    hashes in pins.json (the w5-parity-001 void is what this pin exists for).

This script writes `<epoch>/ppe1-analysis.json` with the leg-B verdict INPUTS
(point estimate, lower bound, realized CV, harness validity, fails,
underpowered). It does not write the four-valued verdict: that is derived by
`EffectRowsProbeLedgerTests.PpE1LedgerMatchesRecomputation` from the whole
ledger, in the frozen precedence NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT.

Bootstrap shape: transcribed from ppe1-margin-derivation.py (pairs resampled,
runs resampled within pair, 5th percentile = one-sided 95% lower bound), seed
4537 — the m5-analyze.py / w5-analyze.py convention; 2000 resamples, the
adjudicators' count (the derivation's Monte-Carlo inner loop used 400).

Usage:
    ppe1-analyze.py <epoch-dir> [--dry-run] [--out <path>]

  --dry-run   accept an epoch that is NOT e1-rows-parity-001 (e.g. the
              archived w5-parity-002, whose arms are v0.10.0 vs v0.11 main)
              and label the output as a dry run. The ledger test refuses to
              record a dry-run analysis as leg B.

Python 3.9 compatible; standard library only.
"""
import argparse
import glob
import importlib.util
import json
import os
import random
import statistics
import sys
from collections import Counter

BENCH = os.path.dirname(os.path.abspath(__file__))

# ---- frozen constants (A-1.11; never re-tuned) ------------------------------
EPOCH_ID = "e1-rows-parity-001"
KIND = "pp-e1-rows-parity"
PAIRS = ["N1-001", "N1-002", "N1-003", "N1-005"]   # the four registered N1 neutral pairs
RUNS_PER_ARM = 5
POINT_GATE = 1.35            # (b)
LOWER_BOUND_GATE = 1.0       # (a)
CV_CAP = 0.66                # UNDERPOWERED above this (1.5 x 0.4392)
MIN_RUNS_PER_CELL = 2        # below this, the PAIR drops (disclosed)
MIN_PAIRS = 3                # below this, invalid
MIN_VALID_PER_ARM = 12       # below this, invalid
CENSOR_CAP = 0.40            # gates §2: > 40% censored on neutral tasks invalidates
BOOT = 2000
SEED = 4537


def _load_token_usage():
    spec = importlib.util.spec_from_file_location(
        "token_usage", os.path.join(BENCH, "token-usage.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TOKEN_USAGE = _load_token_usage()


def corrected_output_tokens(run_dir):
    """The one blessed derivation of the cost-leg token figure (A-1.9.1).

    Returns (tokens, source). `source` is token-usage.py's: "modelUsage"
    (the corrected sum), "usage" (no modelUsage in the envelope: the naive
    figure is the best available and is labelled as such) or "missing" (no
    usable envelope — the run cannot contribute a token figure)."""
    usage = TOKEN_USAGE.compute(TOKEN_USAGE.load_envelope(os.path.join(run_dir, "agent.json")))
    return usage["output_tokens_corrected"], usage["source"]


def cv(values):
    """Population-sd convention, as in the margin derivation."""
    mean = statistics.mean(values)
    return None if mean == 0 else statistics.pstdev(values) / mean


def bootstrap_lower(pairs, control, treatment, boot=BOOT, seed=SEED):
    """Two-level cluster bootstrap (gates §6.1); 5th percentile = one-sided 95% lower.
    Transcribed from ppe1-margin-derivation.py::bootstrap_lower."""
    rng = random.Random(seed)
    boots = []
    for _ in range(boot):
        ratios = []
        for _ in range(len(pairs)):
            pair = pairs[rng.randrange(len(pairs))]
            c = statistics.mean(rng.choice(control[pair]) for _ in control[pair])
            t = statistics.mean(rng.choice(treatment[pair]) for _ in treatment[pair])
            if c > 0:
                ratios.append(t / c)
        if ratios:
            boots.append(statistics.median(ratios))
    boots.sort()
    return boots[int(0.05 * len(boots))] if boots else None


def analyze(epoch_dir, dry_run=False):
    """Pure: returns the analysis dict (and a list of printable lines)."""
    lines = []
    pins_path = os.path.join(epoch_dir, "pins.json")
    if not os.path.exists(pins_path):
        raise SystemExit(f"ERROR: {pins_path} not found — not an epoch directory")
    with open(pins_path, encoding="utf-8") as fh:
        pins = json.load(fh)
    epoch_name = pins.get("epochId") or os.path.basename(os.path.abspath(epoch_dir))

    if not dry_run:
        if pins.get("kind") != KIND or epoch_name != EPOCH_ID:
            raise SystemExit(
                f"ERROR: {pins_path} records kind={pins.get('kind')!r} epochId={epoch_name!r}; "
                f"PP-E1 leg B is the registered epoch {EPOCH_ID} of kind {KIND}. Pass --dry-run "
                "to exercise the arithmetic on another epoch (the output is then labelled and "
                "cannot be recorded as leg B).")

    arm_a, arm_b = pins.get("armA") or {}, pins.get("armB") or {}
    control_root, treatment_root = arm_a.get("repoRoot") or "", arm_b.get("repoRoot") or ""
    blockers = []
    # Route (c): the product contrast must be provable FROM THE RECORD.
    if not control_root or not treatment_root or control_root == treatment_root:
        blockers.append("pins.json does not record two distinct per-arm repoRoots "
                        "(the w5-parity-001 void)")
    a_tasks, b_tasks = arm_a.get("calorTasksSha") or "", arm_b.get("calorTasksSha") or ""
    if not a_tasks or not b_tasks:
        blockers.append("pins.json does not record a Calor.Tasks hash for both arms")
    elif a_tasks == b_tasks:
        blockers.append("both arms' Calor.Tasks.dll hash to the same value — the agent-visible "
                        "compiler is identical on both arms")
    if arm_a.get("editMechanism", "raw") != "raw" or arm_b.get("editMechanism", "raw") != "raw":
        blockers.append("edit mechanism must be `raw` on both arms")

    # ---- collect runs, keyed on the PRODUCT root the run built against ------
    runs = {}
    unattributed = 0
    token_sources = Counter()
    for path in sorted(glob.glob(os.path.join(epoch_dir, "*", "*", "run-*", "result.json"))):
        with open(path, encoding="utf-8") as fh:
            record = json.load(fh)
        pair = "-".join(record["pair"].split("-")[:2])
        if pair not in PAIRS:
            continue
        root = record.get("armRepoRoot") or ""
        if root == control_root:
            role = "control"
        elif root == treatment_root:
            role = "treatment"
        else:
            unattributed += 1
            continue
        invalid = bool(record.get("invalid", False))
        censored = bool(record.get("censored", False)) or invalid
        tokens, source = (None, "invalid") if invalid else corrected_output_tokens(os.path.dirname(path))
        token_sources[source] += 1
        if not invalid and source == "missing":
            # No usable envelope: the run has no adjudicable token figure. It is
            # excluded from the token statistics and DISCLOSED, never zero-filled.
            invalid_for_tokens = True
        else:
            invalid_for_tokens = False
        runs.setdefault(pair, {}).setdefault(role, []).append({
            "run": record.get("run"),
            "tokens": tokens,
            "tokensNaive": (record.get("tokens") or {}).get("output"),
            "tokenSource": source,
            "invalid": invalid,
            "invalidForTokens": invalid_for_tokens,
            "censored": censored,
            "itg": record.get("iterationsToGreen"),
        })
    if unattributed:
        blockers.append(f"{unattributed} run(s) carry no armRepoRoot matching either pinned arm")

    def valid_tokens(pair, role):
        return [r["tokens"] for r in runs.get(pair, {}).get(role, [])
                if not r["invalid"] and not r["invalidForTokens"] and r["tokens"] is not None]

    def censored_frac(role):
        total = sum(len(runs.get(p, {}).get(role, [])) for p in PAIRS)
        cen = sum(1 for p in PAIRS for r in runs.get(p, {}).get(role, []) if r["censored"])
        return (cen / total) if total else 0.0, cen, total

    dropped, surviving = [], []
    for pair in PAIRS:
        c, t = valid_tokens(pair, "control"), valid_tokens(pair, "treatment")
        if len(c) < MIN_RUNS_PER_CELL or len(t) < MIN_RUNS_PER_CELL or statistics.mean(c or [0]) <= 0:
            dropped.append({"pair": pair, "controlValid": len(c), "treatmentValid": len(t)})
        else:
            surviving.append(pair)
    total_control = sum(len(valid_tokens(p, "control")) for p in PAIRS)
    total_treatment = sum(len(valid_tokens(p, "treatment")) for p in PAIRS)
    if len(surviving) < MIN_PAIRS:
        blockers.append(f"only {len(surviving)} pair(s) survived (need >= {MIN_PAIRS})")
    if total_control < MIN_VALID_PER_ARM:
        blockers.append(f"control has {total_control} valid runs (need >= {MIN_VALID_PER_ARM})")
    if total_treatment < MIN_VALID_PER_ARM:
        blockers.append(f"treatment has {total_treatment} valid runs (need >= {MIN_VALID_PER_ARM})")
    cen_c, cen_t = censored_frac("control"), censored_frac("treatment")
    if cen_c[0] > CENSOR_CAP:
        blockers.append(f"control censored fraction {cen_c[0]:.0%} exceeds the §2 cap of {CENSOR_CAP:.0%}")
    if cen_t[0] > CENSOR_CAP:
        blockers.append(f"treatment censored fraction {cen_t[0]:.0%} exceeds the §2 cap of {CENSOR_CAP:.0%}")

    harness_valid = not blockers

    # ---- the arithmetic, over the surviving pairs ---------------------------
    control = {p: valid_tokens(p, "control") for p in surviving}
    treatment = {p: valid_tokens(p, "treatment") for p in surviving}
    per_pair = []
    for pair in surviving:
        per_pair.append({
            "pair": pair,
            "controlMean": round(statistics.mean(control[pair]), 2),
            "controlRuns": len(control[pair]),
            "treatmentMean": round(statistics.mean(treatment[pair]), 2),
            "treatmentRuns": len(treatment[pair]),
            "ratio": round(statistics.mean(treatment[pair]) / statistics.mean(control[pair]), 4),
        })
    point = statistics.median([p["ratio"] for p in per_pair]) if per_pair else None
    lower = bootstrap_lower(surviving, control, treatment) if len(surviving) >= 2 else None

    cell_cvs, cv_values = [], []
    for pair in surviving:
        for role, cell in (("control", control[pair]), ("treatment", treatment[pair])):
            value = cv(cell)
            if value is not None:
                cv_values.append(value)   # unrounded, as the margin derivation medians them
                cell_cvs.append({"pair": pair, "arm": role, "cv": round(value, 4)})
    median_cv = statistics.median(cv_values) if cv_values else None
    max_cv = max(cv_values) if cv_values else None

    fails = underpowered = None
    if harness_valid and point is not None and lower is not None and median_cv is not None:
        point_exceeds = point > POINT_GATE
        bound_fires = lower > LOWER_BOUND_GATE
        fails = point_exceeds and bound_fires
        underpowered = (not fails) and (point_exceeds or median_cv > CV_CAP)

    itg = {}
    for role in ("control", "treatment"):
        values = [r["itg"] for p in PAIRS for r in runs.get(p, {}).get(role, [])
                  if not r["invalid"] and r["itg"] is not None]
        itg[role] = {str(k): v for k, v in sorted(Counter(values).items())}

    analysis = {
        "gate": "PP-E1 leg B (annex A-1.11)",
        "epoch": epoch_name,
        "dryRun": dry_run,
        "dryRunNote": (None if not dry_run else
                       "DRY RUN on an archived epoch whose arms are NOT the registered "
                       "0.15.0-vs-v0.14.3 contrast; exercises the arithmetic only and is never "
                       "recorded as leg B"),
        "registeredEpoch": EPOCH_ID,
        "rule": ("fails iff lowerBound95 > 1.0 AND pointEstimate > 1.35; underpowered iff "
                 "pointEstimate > 1.35 with the bound not firing, or realizedMedianWithinCellCv "
                 "> 0.66; per-run figure = token-usage.py output_tokens_corrected (A-1.9.1)"),
        "constants": {"pointGate": POINT_GATE, "lowerBoundGate": LOWER_BOUND_GATE, "cvCap": CV_CAP,
                      "minRunsPerCell": MIN_RUNS_PER_CELL, "minPairs": MIN_PAIRS,
                      "minValidPerArm": MIN_VALID_PER_ARM, "censorCap": CENSOR_CAP,
                      "bootstrapResamples": BOOT, "seed": SEED},
        "armA": {"label": arm_a.get("label"), "role": "control", "commit": arm_a.get("commit"),
                 "repoRoot": control_root, "calorTasksSha": a_tasks,
                 "editMechanism": arm_a.get("editMechanism")},
        "armB": {"label": arm_b.get("label"), "role": "treatment", "commit": arm_b.get("commit"),
                 "repoRoot": treatment_root, "calorTasksSha": b_tasks,
                 "editMechanism": arm_b.get("editMechanism")},
        "modelPin": pins.get("modelPin"),
        "pairsRegistered": PAIRS,
        "pairsSurviving": surviving,
        "pairsDropped": dropped,
        "validRuns": {"control": total_control, "treatment": total_treatment},
        "censored": {"control": round(cen_c[0], 3), "treatment": round(cen_t[0], 3)},
        "tokenSources": dict(sorted(token_sources.items())),
        "perPair": per_pair,
        "withinCellCv": cell_cvs,
        "pointEstimate": None if point is None else round(point, 4),
        "lowerBound95": None if lower is None else round(lower, 4),
        "realizedMedianWithinCellCv": None if median_cv is None else round(median_cv, 4),
        "maxWithinCellCv": None if max_cv is None else round(max_cv, 4),
        "iterationsToGreen": itg,
        "harnessValid": harness_valid,
        "blockers": blockers,
        "legBFails": fails,
        "underpowered": underpowered,
        "legBInput": ("INVALID" if not harness_valid else
                      "FAIL" if fails else "UNDERPOWERED" if underpowered else "PASS"),
        "note": ("leg B does not adjudicate alone: EffectRowsProbeLedgerTests derives the "
                 "four-valued verdict from leg A, the routes and this file"),
    }

    def f(x):
        return "n/a" if x is None else (f"{x:.4f}" if isinstance(x, float) else str(x))
    lines.append(f"=== PP-E1 leg B — {epoch_name}{' (DRY RUN)' if dry_run else ''} ===")
    lines.append(f"control = {arm_a.get('label')} @ {arm_a.get('commit')}   treatment = "
                 f"{arm_b.get('label')} @ {arm_b.get('commit')}")
    lines.append(f"pairs registered {len(PAIRS)}  surviving {len(surviving)}  dropped {len(dropped)}")
    for d in dropped:
        lines.append(f"  DROPPED {d['pair']}: control {d['controlValid']} valid, treatment "
                     f"{d['treatmentValid']} valid (need >= {MIN_RUNS_PER_CELL} each)")
    lines.append(f"valid runs — control {total_control}, treatment {total_treatment}; censored — "
                 f"control {cen_c[0]:.0%}, treatment {cen_t[0]:.0%}; token sources {dict(token_sources)}")
    for p in per_pair:
        lines.append(f"  {p['pair']}: control mean {p['controlMean']:.0f} ({p['controlRuns']}), "
                     f"treatment mean {p['treatmentMean']:.0f} ({p['treatmentRuns']}), ratio {p['ratio']:.4f}")
    lines.append(f"point estimate {f(point)}   one-sided 95% lower bound {f(lower)}   "
                 f"realized median within-cell CV {f(median_cv)} (max {f(max_cv)})")
    for role in ("control", "treatment"):
        lines.append(f"iterations-to-green ({role}, observational): {itg[role]}")
    if blockers:
        lines.append("HARNESS INVALID (route (c)):")
        for b in blockers:
            lines.append(f"  - {b}")
    lines.append(f"leg B input: {analysis['legBInput']}  (fails={fails}, underpowered={underpowered})")
    return analysis, lines


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("epoch_dir")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--out", default=None,
                        help="output path (default: <epoch>/ppe1-analysis.json; a dry run "
                             "defaults to <epoch>/ppe1-analysis.dry-run.json)")
    args = parser.parse_args(argv)
    analysis, lines = analyze(args.epoch_dir, dry_run=args.dry_run)
    out = args.out or os.path.join(
        args.epoch_dir, "ppe1-analysis.dry-run.json" if args.dry_run else "ppe1-analysis.json")
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(analysis, fh, indent=2)
        fh.write("\n")
    for line in lines:
        print(line)
    print(f"wrote {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
