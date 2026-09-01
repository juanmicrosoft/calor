#!/usr/bin/env python3
"""PP-W-rows adjudicator — the W2 instrument (annex A-1.12, §A.2 row PP-W6).

WRITTEN BEFORE THE EPOCH RAN. Everything below is a transcription of the
frozen A-1.12 row; nothing here may be re-tuned once numbers exist. Where the
row and this script disagree, the row wins and this script is the bug.

THE CLAIM (verbatim from roadmap v0.16 §4.1's heading)
    "with rows, fail-closed, agents launder fewer effects on callback-heavy
     code than under the pre-rows language as it was usable (warnings
     included), at no large loop tax"

TWO LEGS, ONE FOUR-VALUED OUTCOME, strict precedence
    NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT   (a partition, not labels)

LEG A — escapes.
  ESCAPE SEMANTICS, REGISTERED, and NOT the pair specs' `escapedBugs` rule:
  an escape is AT LEAST ONE of the pair's named `effectObservingTests`
  failing on a workspace that BUILT at declared-done. A run whose final state
  does not build contributes NO escape; it stays in the cell's denominator and
  is published in its own category ("did not build at declared-done"), per arm
  and per pair, and it still counts against §2's censoring cap.
  Reading it the other way (gates §2's "a non-compiling final state counts as
  all tests failing") INVERTS leg A's sign: arm B is precisely the arm on
  which the laundering shortcut does not compile. `escapedBugs` is also the
  AGGREGATE Failed: count for the whole held-out suite, so it counts failures
  of tests that are not effect-observing at all — measured on W-006, an unfixed
  `Map` off-by-one gives Failed: 8 where the two SURVIVORS are exactly the
  effect-observing pair. The per-TEST names are read instead, from the
  `heldoutFinal` field or, for a run collected before that field existed, from
  the archived `.ho_final.txt` / `.src_final.txt`.
  ONE REFINEMENT, post hoc and arm-symmetric: the failure must carry the
  SILENCE signature. W-001's and W-003's effect-observing tests assert the
  RETURN VALUE before the silence assertion, so a silent-but-WRONG solution
  fails a named effect-observing test having laundered nothing. Failures
  without the signature are published per cell and never scored.
  Statistic: the MEDIAN over BLIND pairs of the per-pair escape-rate delta
  (A - B), with the frozen median convention (odd -> the middle value; even ->
  the mean of the two middle values). Two-level cluster bootstrap (pairs
  resampled, runs resampled within pair), one-sided, alpha = 0.05.
  BAR: the one-sided 95 % LOWER bound of that delta exceeds 0.
  The pre-registered effect size is Delta = 0.5; the dry run sizes N and never
  moves the bar. Warning-vs-error cells carry the same statistic and are
  published BESIDE the verdict, never pooled into it. W-005 is leg A only.

LEG B — loop tax. PP-E1's rule verbatim, on the CORRECTED per-run
  `tokens.output` as derived by token-usage.py (A-1.9.1; the naive
  `usage.output_tokens` is archived for audit and never adjudicated), over
  `pins.json` `ppW.legBPairs` ONLY:
      FAILS iff BOTH the one-sided 95 % cluster-bootstrap LOWER bound of the
      median paired per-pair ratio (arm B / arm A) exceeds 1.0
      AND the point estimate exceeds the margin 1.20.
  Iterations-to-green are recorded observational.
  PRE-REGISTERED SENSITIVITY, an obligation and not a courtesy: the point and
  the bound are published against ALL THREE candidate margins — 1.20
  (registered), 1.30 (pooled) and 1.35 (w5-parity-002). THE VERDICT IS READ AT
  1.20 AND ONLY AT 1.20.

THE DRY RUN SIZES N AND NEVER MOVES THE BAR. Under --dry-run this script
  refuses to emit a verdict and refuses to write the ledger; what it produces
  is the power curve over candidate N at Delta = 0.5, computed under the UPPER
  95 % confidence bound of the dry run's own between-pair variance, with the
  runs and dollars each N costs against the frozen $150 ceiling. When no
  affordable N reaches 80 % power the PP registers its achievable power and
  arms UNDERPOWERED rather than overrunning.

DENOMINATORS ARE READ FROM THE EPOCH, NEVER FROM A SCRIPT DEFAULT.
  `ppW.legBPairs` and `ppW.blindPairs` come out of the epoch's `pins.json`
  (the defect `ppe1-analyze.py:66` has is a hardcoded pair list). They are then
  CHECKED against the sets A-1.12 froze; a live epoch naming anything else is
  an invalid epoch under validity condition (4) -> route (c). A `pins.json` with
  no `ppW` block is NOT valid, and there is no fallback to a default denominator.
  The inherited `ppL5` / `ppL6` / `ppW5` fields carry the W2-/W3- loop suite and
  the N1- neutral set from `run-m5-epoch.sh`'s base pins. They are NOT this
  experiment's pairs; reading one would be a silent wrong-denominator bug, so
  they are ignored and NAMED as ignored in the output.

VALIDITY CONDITIONS (frozen)
  (1) a run without `transcript.jsonl` is invalid            -> route (b)
  (2) `turns.assistantMessages` must be the TOP-LEVEL count  -> route (c)
  (3) two distinct Calor.Tasks hashes across the arms, and each arm's
      `compilerHash` set a singleton, the two disjoint       -> route (c)
  (4) `pins.json` carries the `ppW` block with legBPairs, blindPairs, both arm
      commits and the harness commit                         -> route (c)
  (5) §2's 40 % censoring cap per arm                        -> route (c)
  (6) the PP-W5 validity floor: a cell (pair x arm) with < 2 valid runs drops
      its PAIR, disclosed                                    -> route (c)
  Plus: the pre-rows arm canary verdict archived in `result.json`
  (`armCanary`) must be "ok" on the control arm; a null-agent run or a
  non-live epoch is refused outright (a plumbing check is not a measurement).

OUTCOME MAP, frozen, in precedence order; ANY ROUTE NOT LISTED IS A MISS.
  NOT-ADJUDICATED
    (a)  any UNMUTATED starter fails to reproduce its frozen multiset on its
         arm (severity and exit are part of the multiset)
    (a') fewer than two blind cells survive (a)
    (b)  any run lacks W1's transcript
    (c)  the PP-W5 validity floor / distinct-hash / censoring routes
    (d)  W2 does not ship in 0.16.0 and only where roadmap §9 cut line 2 was
         invoked in writing — INERT BY CONSTRUCTION since A-1.12 merged: cut
         line 2's antecedent is "if A-1.12 has not registered by the branch
         cut", which merging A-1.12 made permanently false. A W2 slip for any
         reason is a MISS.
  OWN-GOAL CLAUSE, governing (a)-(d): a not-adjudicated route caused by this
    workstream's own change is adjudicated MISS, and the cause is published
    WITH THE ARTIFACT that shows it, never asserted in prose.
  MISS          leg A below its bar on a valid harness, or leg B fails (both
                its conditions), or an own-goal.
  UNDERPOWERED  leg A at bar with leg B's point over the margin and the bound
                not firing, OR the realized median within-cell CV over the
                0.41 cap, OR registered achievable power < 80 % at Delta = 0.5.
  HIT           leg A at bar and leg B not failing — read as "rows,
                fail-closed, caught the registered classes at no large loop
                tax", NEVER "rows are free".

PER-CELL REPORTING A-1.12 REQUIRES
  * the SHAPE-REALIZED INDICATOR per cell, published beside the escape rate:
    without it a null leg A cannot be told from "the shape under test was
    never written". The indicator must be FALSE on the pair's own frozen
    starter and TRUE on its frozen `clean` seed; an indicator satisfiable by
    the starter measures nothing and is an instrument defect (own-goal).
  * arm-B escapes CLASSIFIED by which instance of the argument-resolution
    escape they used — this-qualified / property / inherited / alias-of-this /
    other-instance / instance-method-group / other — by inspecting the final
    source. A two-way "this-route or other" split is insufficient.
    A `this.`-style arm-B escape does NOT fire (a'): route (a) reads UNMUTATED
    STARTERS, and an agent writing `this.` in its SOLUTION makes no starter
    fail anything. A depressed leg-A delta is a MISS.
  * warning-vs-error cells published beside the blind verdict, never pooled.
  * runs that did not build at declared-done, per arm and per pair.

Usage:
    ppw-analyze.py <epoch-dir> [--dry-run] [--starter-compiles PATH] [--out PATH]
    ppw-analyze.py --ledger [--out PATH] [--epochs-root PATH]

  --dry-run   accept an epoch that is NOT the registered `w-rows-001` (the
              A:81 dry run, a null-agent smoke, a synthetic fixture). A DRY RUN
              EMITS NO VERDICT AT ALL and is never recorded in the ledger: it
              exists to SIZE N — the smallest runs-per-cell reaching >= 80 %
              power at Delta = 0.5 under the UPPER confidence bound of its
              observed between-pair variance — and to report that variance.
              THE DRY RUN SIZES N AND NEVER MOVES THE BAR.
  --ledger    write `bench/phase0-agent-native/effect-rows-benefit-ledger.json`
              (timestamp-free and byte-stable) from the registered epoch under
              --epochs-root, or in its not-run form when that epoch is absent.

Python 3.9 compatible; standard library only.
"""
import argparse
import glob
import hashlib
import importlib.util
import json
import math
import os
import random
import re
import statistics
import sys
from collections import Counter, OrderedDict

BENCH = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(BENCH, "..", ".."))

# ---- frozen constants (A-1.12; never re-tuned) ------------------------------
EPOCH_ID = "w-rows-001"
KIND = "pp-w-rows"

# The six pairs, by short id -> directory name. A-1.12 registers both spellings
# "so the two can never drift"; pins.json carries the DIRECTORY names.
PAIR_DIRS = OrderedDict([
    ("W-001", "W-001-middleware-stage"),
    ("W-002", "W-002-map-and-report"),
    ("W-003", "W-003-match-fallback"),
    ("W-004", "W-004-counter-peek"),
    ("W-005", "W-005-pipeline-trace"),
    ("W-006", "W-006-map-doubler"),
])
# A-1.12 registers BOTH spellings of every pair — the short id `W-001` and the
# directory `W-001-middleware-stage` — "so the two can never drift". The epoch
# runner stamps pins.json from `run-m5-epoch.sh`'s pair ids, which are the SHORT
# ones (`run-ppw-epoch.sh` passes `--pairs "W-001 …"` and the collector globs
# `*/W-001-*/`), while everything downstream of here keys on the directory. Both
# are accepted and mapped to the directory; anything that is neither spelling is
# a wrong denominator, never a near-miss to be guessed at.
def normalize_pair(value):
    """Directory name for either registered spelling of a pair id, else None."""
    if value in PAIR_DIRS:
        return PAIR_DIRS[value]
    if value in PAIR_DIRS.values():
        return value
    return None


REGISTERED_BLIND = ["W-001-middleware-stage", "W-004-counter-peek", "W-006-map-doubler"]
REGISTERED_LEG_B = ["W-001-middleware-stage", "W-002-map-and-report", "W-003-match-fallback",
                    "W-004-counter-peek", "W-006-map-doubler"]
REGISTERED_WARNING_VS_ERROR = ["W-002-map-and-report", "W-003-match-fallback", "W-005-pipeline-trace"]

LEG_A_EFFECT_SIZE = 0.5      # pre-registered Delta; the dry run sizes N, never the bar
LEG_A_BAR = 0.0              # the one-sided 95 % lower bound of the median delta must exceed this
BLIND_FLOOR = 2              # below two surviving blind cells -> route (a')

MARGIN = 1.20                            # registered; the verdict is read here and ONLY here
SENSITIVITY_MARGINS = [1.20, 1.30, 1.35]  # 1.30 pooled, 1.35 w5-parity-002 — an OBLIGATION
LOWER_BOUND_GATE = 1.0
CV_CAP = 0.41                # 1.5 x e1-rows-parity-001's median within-cell CV 0.2746 = 0.4119
MIN_RUNS_PER_CELL = 2        # PP-W5 validity floor: below this the PAIR drops, disclosed
CENSOR_CAP = 0.40            # gates §2
MIN_POWER = 0.80             # registered achievable power below this reads UNDERPOWERED
SPEND_CEILING_USD = 150.0
BOOT = 2000
SEED = 4537

# The A:81 dry run's SIZING loop — a different job from adjudication, with its own
# counts, and it may never move the bar. It answers one question: the smallest N
# (runs per cell) reaching >= MIN_POWER at Delta = LEG_A_EFFECT_SIZE under the
# UPPER confidence bound of the dry run's observed between-pair variance.
SIZING_SIMS = 400            # simulated epochs per candidate N
SIZING_BOOT = 400            # the derivation's inner Monte-Carlo loop count
SIZING_MAX_N = 12
COST_PER_RUN_USD = 1.0048    # mean total_cost_usd over the 40 e1-rows-parity-001 runs
PILOT_RUNS = 2               # the S1 pilot; counts against the ceiling
DRY_RUN_RUNS = 36            # 6 pairs x 3 runs x 2 arms; counts against the ceiling

# The arm-B argument-resolution escape route, enumerated as A-1.12 enumerates it.
ESCAPE_CATEGORIES = ["this-qualified", "property", "inherited", "alias-of-this",
                     "other-instance", "instance-method-group", "other"]

ARM_A_COMMIT = "283ec9f9964ddd5b21da15b646a0dd77d53de99e"   # tag v0.14.3 + the Tasks passthrough
ARM_B_COMMIT = "3bb2601e0cbd93fc25fdaaf2a0ea5183b8a2dd6a"   # v0.15.0, strict, no flags
STARTER_FREEZE_COMMIT = "7d621c0d"

LEDGER_PATH = os.path.join(BENCH, "effect-rows-benefit-ledger.json")
SEEDED_COMPILES = os.path.join(BENCH, "pairs", "ppw-seeded-compiles.json")
REGISTERED_COMPILE_ROLES = ["starter", "shortcut", "clean"]

# Limitations measured on the instrument and DISCLOSED rather than fixed, because
# fixing them after the fixtures were frozen would be the post-hoc change gates §0.3
# forbids. Each names its direction, because a limitation whose sign is unstated is
# not a disclosure.
DISCLOSED_LIMITATIONS = [
    {"id": "stderr-laundering-invisible-on-arm-A",
     "detail": "a laundering implementation that prints to Console.Error instead of stdout "
               "compiles on arm A with NO Calor0410 and passes the held-out suite (the tests "
               "capture Console.Out), so it is an UNDETECTED escape. On arm B the same program "
               "is refused (4x error Calor0410, build exit 1).",
     "direction": "removes escapes from ARM A ONLY, shrinking the A - B delta. It biases "
                  "AGAINST this workstream's own hypothesis and cannot manufacture a HIT."},
    {"id": "W-004-arm-B-zero-is-compiler-behaviour",
     "detail": "W-004's laundering site INVOKES the field (§C{this.onChange}) rather than "
               "passing it, so 0.15 charges it through the unknown-call path and the arm-B "
               "escape rate is expected to be a hard zero.",
     "direction": "not test blindness and not a defect: a zero there is the compiler working. "
                  "W-004's `unregistered-this-qualified-escape-b` seed is a CHARGED CONTROL, "
                  "unlike the identically-named seeds on W-001 and W-006, which do escape — "
                  "the #1136 boilerplate pasted across those cells is cell-specific only in "
                  "its first sentence and must not be read as making them the same cell."},
    {"id": "silence-signature-required",
     "detail": "W-001's and W-003's effect-observing tests assert the return value BEFORE the "
               "silence assertion, so a silent-but-wrong solution fails a named test having "
               "laundered nothing. Leg A therefore requires the SILENCE failure signature, not "
               "merely a named-test failure; the excluded cases are published per cell as "
               "namedTestFailuresWithoutSilence.",
     "direction": "arm-symmetric — it removes noise from both arms and cannot move the sign."},
]

NOT_RUN_REASON = ("epoch w-rows-001 has not run; adjudication is at the 0.16.0 release commit "
                  "(A-1.12: the 0.16.0 release PR's author runs it via run-ppw-epoch.sh)")


def _load(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(BENCH, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TOKEN_USAGE = _load("token_usage", "token-usage.py")
HARNESS_CAPTURE = _load("harness_capture", "harness-capture.py")


# ---------------------------------------------------------------------------
# arithmetic
# ---------------------------------------------------------------------------
def median(values):
    """A-1.12's frozen median convention, written out rather than delegated:
    odd -> the middle value; even -> the mean of the two middle values. So
    k = 3 blind pairs is no more powerful than k = 2, and the floor of two is
    what carries the power."""
    ordered = sorted(values)
    n = len(ordered)
    if n == 0:
        return None
    mid = n // 2
    if n % 2 == 1:
        return ordered[mid]
    return (ordered[mid - 1] + ordered[mid]) / 2.0


def cv(values):
    """Population-sd convention, as in the margin derivation."""
    if not values:
        return None
    mean = statistics.mean(values)
    return None if mean == 0 else statistics.pstdev(values) / mean


def bootstrap_lower_delta(pairs, arm_a, arm_b, boot=BOOT, seed=SEED):
    """Leg A: two-level cluster bootstrap of the median per-pair escape-rate
    delta (A - B). Pairs resampled, runs resampled within pair; the 5th
    percentile is the one-sided 95 % lower bound."""
    if len(pairs) < 1:
        return None
    rng = random.Random(seed)
    boots = []
    for _ in range(boot):
        deltas = []
        for _ in range(len(pairs)):
            pair = pairs[rng.randrange(len(pairs))]
            a, b = arm_a[pair], arm_b[pair]
            if not a or not b:
                continue
            ra = sum(rng.choice(a) for _ in a) / float(len(a))
            rb = sum(rng.choice(b) for _ in b) / float(len(b))
            deltas.append(ra - rb)
        if deltas:
            boots.append(median(deltas))
    if not boots:
        return None
    boots.sort()
    return boots[int(0.05 * len(boots))]


def bootstrap_lower_ratio(pairs, control, treatment, boot=BOOT, seed=SEED):
    """Leg B: PP-E1's bootstrap verbatim (ppe1-analyze.py::bootstrap_lower)."""
    if not pairs:
        return None
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
            boots.append(median(ratios))
    if not boots:
        return None
    boots.sort()
    return boots[int(0.05 * len(boots))]


# ---------------------------------------------------------------------------
# N-sizing (the A:81 dry run's whole job)
# ---------------------------------------------------------------------------
# One-sided upper 95 % chi-square bounds on a variance, by degrees of freedom:
# sigma^2_UCB = df * s^2 / chi2_{0.05, df}. Tabulated (standard library only).
_CHI2_LOWER_TAIL_05 = {1: 0.003932, 2: 0.102587, 3: 0.351846, 4: 0.710721,
                       5: 1.145476, 6: 1.635383, 7: 2.167350, 8: 2.732637,
                       9: 3.325113, 10: 3.940299}


def variance_upper_bound(values):
    """The one-sided upper 95 % confidence bound on the variance of `values`.
    A-1.12: the dry run sizes N "under the UPPER confidence bound of its
    variance" — sizing on the point estimate would under-size by design."""
    n = len(values)
    if n < 2:
        return None
    s2 = statistics.variance(values)
    df = n - 1
    chi2 = _CHI2_LOWER_TAIL_05.get(df)
    if chi2 is None:
        return s2
    return df * s2 / chi2


def _simulate_power(k, n, delta, sigma, sims=SIZING_SIMS, boot=SIZING_BOOT, seed=SEED):
    """Power of the REGISTERED leg-A procedure at `n` runs per cell: draw k
    pair-level true deltas around `delta` with spread `sigma`, draw Bernoulli
    escapes per run, run the same two-level cluster bootstrap, and count how
    often its one-sided 95 % lower bound exceeds the bar."""
    rng = random.Random(seed)
    pairs = ["p%d" % i for i in range(k)]
    hits = 0
    for _ in range(sims):
        arm_a, arm_b = {}, {}
        for pair in pairs:
            d = max(-1.0, min(1.0, rng.gauss(delta, sigma)))
            # centre the pair on 0.5 so both rates stay inside [0, 1]
            a = max(0.0, min(1.0, 0.5 + d / 2.0))
            b = max(0.0, min(1.0, a - d))
            arm_a[pair] = [1.0 if rng.random() < a else 0.0 for _ in range(n)]
            arm_b[pair] = [1.0 if rng.random() < b else 0.0 for _ in range(n)]
        lower = bootstrap_lower_delta(pairs, arm_a, arm_b, boot=boot, seed=rng.randrange(1 << 30))
        if lower is not None and lower > LEG_A_BAR:
            hits += 1
    return hits / float(sims)


def size_n(observed_deltas, k, sims=SIZING_SIMS, boot=SIZING_BOOT, max_n=SIZING_MAX_N,
           cost_per_run=None, prior_usd=None):
    """The dry run's deliverable. Returns the power curve, the smallest N that
    reaches MIN_POWER at Delta = LEG_A_EFFECT_SIZE, and what that N costs
    against the frozen $150 ceiling — with the achievable power at the largest
    affordable N when nothing affordable reaches the bar, because A-1.12 says
    the PP then "registers its achievable power and arms UNDERPOWERED" rather
    than overrunning."""
    sigma_ucb = variance_upper_bound(observed_deltas)
    measured_cost = cost_per_run is not None
    if cost_per_run is None:
        cost_per_run = COST_PER_RUN_USD
    result = OrderedDict()
    result["costPerRunUsd"] = round(cost_per_run, 4)
    result["costBasis"] = ("measured on this dry epoch's own paid runs"
                           if measured_cost else
                           "e1-rows-parity-001 (A-1.12's pre-registered estimate); this epoch "
                           "reported no paid run to measure")
    result["priorSpendUsd"] = (None if prior_usd is None else round(prior_usd, 2))
    result["rule"] = ("the smallest N (runs per cell) reaching >= %.2f power at Delta = %.1f "
                      "under the UPPER 95 %% confidence bound of the dry run's observed "
                      "between-pair variance; the dry run sizes N and NEVER moves the bar"
                      % (MIN_POWER, LEG_A_EFFECT_SIZE))
    result["blindPairsObserved"] = k
    result["blindFloor"] = BLIND_FLOOR
    result["observedDeltas"] = [round(d, 4) for d in observed_deltas]
    result["observedVariance"] = (None if len(observed_deltas) < 2
                                  else round(statistics.variance(observed_deltas), 6))
    result["varianceUpperBound95"] = None if sigma_ucb is None else round(sigma_ucb, 6)
    result["sims"] = sims
    result["bootstrapResamples"] = boot
    if sigma_ucb is None or k < 1:
        result["powerCurve"] = []
        result["recommendedN"] = None
        result["note"] = ("fewer than two blind pairs carried a delta: the dry run cannot size N "
                          "(the floor of two is what carries the power)")
        return result
    sigma = math.sqrt(sigma_ucb)
    # The WHOLE curve is computed, never short-circuited at the first N that clears
    # the bar: the curve is the publishable artifact, and with a finite number of
    # simulations a single N can clear it on noise. The recommendation is the
    # smallest N that clears the bar AND is not contradicted by the next N up.
    curve = []
    for n in range(2, max_n + 1):
        power = _simulate_power(k, n, LEG_A_EFFECT_SIZE, sigma, sims=sims, boot=boot)
        cost = _epoch_cost(n, cost_per_run, prior_usd)
        curve.append({"n": n, "power": round(power, 3),
                      "runs": _epoch_runs(n),
                      "estimatedUsd": round(cost, 2),
                      "fitsCeiling": cost <= SPEND_CEILING_USD})
    recommended = _smallest_sufficient_n(curve)

    # The SAME calculation on the POINT variance, published beside it and never
    # used: A-1.12 sizes N "under the UPPER confidence bound of its variance", and
    # with k = 3 pairs that bound is 19.5x the point estimate (chi-square, df = 2).
    # Publishing both makes the cost of the registered basis visible on the face of
    # the output instead of buried in this comment.
    point_curve = []
    if result["observedVariance"]:
        point_sigma = math.sqrt(result["observedVariance"])
        for n in range(2, max_n + 1):
            power = _simulate_power(k, n, LEG_A_EFFECT_SIZE, point_sigma, sims=sims, boot=boot)
            point_curve.append({"n": n, "power": round(power, 3)})
    result["atPointVarianceNotRegistered"] = {
        "note": "NOT the registered basis — A-1.12 sizes N under the UPPER confidence bound of "
                "the variance. Published so the cost of that choice is visible.",
        "powerCurve": point_curve,
        "recommendedN": _smallest_sufficient_n(point_curve)}
    result["powerCurve"] = curve
    result["recommendedN"] = recommended
    affordable = [c for c in curve if c["fitsCeiling"]]
    max_affordable = max((c["n"] for c in affordable), default=None)
    result["maxAffordableN"] = max_affordable
    if recommended is not None:
        entry = next(c for c in curve if c["n"] == recommended)
        result["recommendedFitsCeiling"] = entry["fitsCeiling"]
        result["recommendedRuns"] = entry["runs"]
        result["recommendedUsd"] = entry["estimatedUsd"]
        result["achievablePower"] = entry["power"]
        result["armsUnderpowered"] = not entry["fitsCeiling"]
        result["note"] = (None if entry["fitsCeiling"] else
                          "the N that reaches the power bar does not fit the $%.0f ceiling; "
                          "A-1.12: the PP registers its achievable power and arms UNDERPOWERED "
                          "rather than overrunning" % SPEND_CEILING_USD)
    else:
        best = max(curve, key=lambda c: c["power"]) if curve else None
        result["recommendedFitsCeiling"] = None
        result["achievablePower"] = best["power"] if best else None
        result["armsUnderpowered"] = True
        result["note"] = ("no N up to %d reaches %.2f power at Delta = %.1f under the upper "
                          "variance bound; the PP registers its achievable power and arms "
                          "UNDERPOWERED" % (max_n, MIN_POWER, LEG_A_EFFECT_SIZE))
    return result


def _smallest_sufficient_n(curve):
    """The smallest N clearing MIN_POWER whose successor does not contradict it
    (a lone blip above the bar between two below it is noise, not a size)."""
    for index, entry in enumerate(curve):
        if entry["power"] < MIN_POWER:
            continue
        following = curve[index + 1:]
        if not following or any(e["power"] >= MIN_POWER for e in following):
            return entry["n"]
    return None


def _epoch_runs(n):
    """The registered spend arithmetic: the S1 pilot + the dry run + a 6 x N x 2
    main epoch, all of which count against the ceiling."""
    return PILOT_RUNS + DRY_RUN_RUNS + len(PAIR_DIRS) * n * 2


def _epoch_cost(n, cost_per_run=COST_PER_RUN_USD, prior_usd=None):
    """Dollars against the frozen ceiling. A-1.12 pre-registered the arithmetic
    on the e1-rows-parity-001 per-run cost while saying in the same breath that
    "the per-run cost is unmeasured until the dry run reports it" — so when a dry
    epoch has reported one, the MEASURED cost is what sizes the ceiling, and what
    was actually spent replaces the modelled prior. Neither moves the bar; both
    move the affordable N, which is the number the dry run exists to produce."""
    if prior_usd is None:
        prior_usd = (PILOT_RUNS + DRY_RUN_RUNS) * cost_per_run
    return prior_usd + len(PAIR_DIRS) * n * 2 * cost_per_run


# ---------------------------------------------------------------------------
# the arm-B escape classifier (A-1.12: "classified by WHICH INSTANCE it used")
# ---------------------------------------------------------------------------
_CL = re.compile(r"§CL\{([^}]*)\}")
_FLD = re.compile(r"§FLD\{([^}]*)\}")
_PROP = re.compile(r"§PROP\{([^}]*)\}")
_MT = re.compile(r"§MT\{([^}]*)\}")
_FN = re.compile(r"§F\{([^}]*)\}")
_ARG = re.compile(r"§A\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)")
_THIS_ARG = re.compile(r"§A\s+this\.([A-Za-z_][A-Za-z0-9_]*)")
_BIND_THIS = re.compile(r"§B\{([A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\}\s+this\b")
_BIND_TYPED = re.compile(r"§B\{([A-Za-z_][A-Za-z0-9_]*):([A-Za-z_][A-Za-z0-9_<>,]*)\}")
_BIND_FROM = re.compile(
    r"§B\{([A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\}[ \t]+([A-Za-z_][A-Za-z0-9_]*)[ \t]*$", re.M)
_PARAM = re.compile(r"\(([^)]*)\)")

_VIS = {"pub", "pri", "pro", "int", "public", "private", "protected", "internal"}
_MODS = {"abstract", "sealed", "static", "partial", "record", "readonly"}
_ID = re.compile(r"^[a-z]+[0-9]+$")


def _class_name_and_base(inner):
    """§CL{id:Name[:Base][:vis|mods]} — the parser's disambiguation, reduced."""
    toks = [t.strip() for t in inner.split(":")]
    if toks and _ID.match(toks[0]):
        toks = toks[1:]
    if not toks:
        return None, None
    name = toks[0]
    base = None
    for tok in toks[1:]:
        if tok and tok not in _VIS and tok not in _MODS:
            base = tok
            break
    return name, base


def _declared_names(pattern, text, index):
    """§FLD{Type:name:vis} -> index 1; §PROP{id:Name:...} / §MT{id:Name...} -> 1."""
    names = set()
    for match in pattern.finditer(text):
        toks = [t.strip() for t in match.group(1).split(":")]
        if len(toks) > index:
            names.add(toks[index])
    return names


def _class_table(sources):
    """Per-class member tables + module-level function names, by an indentation
    walk (Calor block structure is indentation-only). Class scoping is what
    makes the `inherited` instance distinguishable from a charged own field."""
    classes = OrderedDict()
    module_fns = set()
    line_class = []               # the enclosing class name for each line, or None
    for text in sources:
        current = None            # (name, indent)
        for raw in text.splitlines():
            stripped = raw.strip()
            indent = len(raw) - len(raw.lstrip(" "))
            if stripped and current is not None and indent <= current[1]:
                current = None
            match = _CL.search(raw)
            if match and stripped.startswith("§CL"):
                name, base = _class_name_and_base(match.group(1))
                if name:
                    classes.setdefault(name, {"base": base, "fields": set(),
                                              "props": set(), "methods": set()})
                    classes[name]["base"] = base
                    current = (name, indent)
                line_class.append(current[0] if current else None)
                continue
            line_class.append(current[0] if current else None)
            if current is None:
                for m in _FN.finditer(raw):
                    toks = [t.strip() for t in m.group(1).split(":")]
                    if len(toks) > 1:
                        module_fns.add(toks[1])
                continue
            entry = classes[current[0]]
            for m in _FLD.finditer(raw):
                toks = [t.strip() for t in m.group(1).split(":")]
                if len(toks) > 1:
                    entry["fields"].add(toks[1])
            for m in _PROP.finditer(raw):
                toks = [t.strip() for t in m.group(1).split(":")]
                if len(toks) > 1:
                    entry["props"].add(toks[1])
            for m in _MT.finditer(raw):
                toks = [t.strip() for t in m.group(1).split(":")]
                if len(toks) > 1:
                    entry["methods"].add(toks[1])
    return classes, module_fns, line_class


def classify_escape(sources):
    """Classify the arm-B escape spelling in a run's final `.calr` sources.

    Returns (label, categories): `categories` is every escaping instance found,
    in A-1.12's registered order, and `label` is the first of them (`other`
    when an escape is present but matches no enumerated instance). A two-way
    "this-route or other" split is explicitly insufficient — the row registers
    the seven-way one, and the property form is the one an agent is most likely
    to write.

    Only ARGUMENT positions (`§A <expr>`) are escape candidates. INVOKING is
    charged — `§C{this.onChange}` takes the unknown-call path and errors, which
    is why W-004 fails closed while W-001 and W-006 do not — so `§C{...}`
    targets are never classified as escapes. That passing/invoking split is the
    registered table's own, not an approximation of it.
    """
    classes, module_fns, _ = _class_table(sources)
    class_names = set(classes)
    all_props = set().union(*[c["props"] for c in classes.values()]) if classes else set()
    all_fields = set().union(*[c["fields"] for c in classes.values()]) if classes else set()
    all_methods = set().union(*[c["methods"] for c in classes.values()]) if classes else set()

    found = set()
    for text in sources:
        this_aliases = {m.group(1) for m in _BIND_THIS.finditer(text)}
        typed = {m.group(1): m.group(2) for m in _BIND_TYPED.finditer(text)}
        bound_from = {m.group(1): m.group(2) for m in _BIND_FROM.finditer(text)}
        params = {}
        for match in _PARAM.finditer(text):
            for part in match.group(1).split(","):
                bits = [b.strip() for b in part.split(":")]
                if len(bits) >= 2 and bits[1] and re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", bits[1]):
                    params[bits[1]] = bits[0]
        _, _, line_class = _class_table([text])
        for index, raw in enumerate(text.splitlines()):
            enclosing = line_class[index] if index < len(line_class) else None
            own = classes.get(enclosing) if enclosing else None
            if _THIS_ARG.search(raw):
                found.add("this-qualified")
            for match in _ARG.finditer(raw):
                expr = match.group(1)
                if "." in expr:
                    receiver, member = expr.split(".", 1)
                    if "." in member or receiver == "this":
                        continue      # a deeper path, or the `this.` case above
                    if receiver in this_aliases:
                        found.add("alias-of-this")
                    elif member in all_methods:
                        found.add("instance-method-group")
                    elif typed.get(receiver) in class_names or params.get(receiver) in class_names:
                        found.add("other-instance")
                    elif receiver in class_names or receiver.endswith("Module"):
                        continue      # a MODULE qualifier does NOT defeat the charge
                    elif member in all_fields or member in all_props:
                        found.add("other")
                    continue
                # a simple name, no receiver at all
                source = bound_from.get(expr, expr)
                if own is not None and source in own["props"]:
                    # the property instance, direct (`§A Stage`) or through the local
                    # the row registers as NOT preserving the row (`§B{f} Stage`, W-006)
                    found.add("property")
                elif source in all_props and own is None:
                    found.add("property")
                elif own is not None and source in own["fields"]:
                    continue          # charged: the class's own directly-declared rowed field
                elif source in module_fns or source in params or source in typed:
                    continue          # charged: resolved through the pass's own symbol table
                elif own is not None and own["base"] and source not in own["fields"] \
                        and source not in own["methods"]:
                    # a member the enclosing class does not declare, on a class that has
                    # a base: the inherited instance (the pass does not index it)
                    found.add("inherited")
                elif source in all_fields or source in all_props:
                    found.add("other")
    categories = [c for c in ESCAPE_CATEGORIES if c in found]
    return (categories[0] if categories else None), categories


def read_declared_done(run_dir, record):
    """The two facts leg A's registered escape needs, and where they come from.

    `result.json`'s `escapedBugs` is NOT one of them, and may not be substituted:
    it is the aggregate xUnit `Failed:` count for the WHOLE held-out suite, so it
    (a) counts failures of tests that are not effect-observing — on W-006 an
    unfixed `Map` off-by-one gives Failed: 8 where the two SURVIVORS are exactly
    the effect-observing pair, i.e. a maximal `escapedBugs` on a run that
    laundered nothing — and (b) does not condition on the workspace having built,
    while `run-pair.sh`'s invalid path writes `escapedBugs = heldoutTestCount`.

    Preferred source: the `finalBuild` / `heldoutFinal` fields `run-pair.sh`
    archives. Fallback, for a run collected before those fields existed: the
    archived logs themselves — `.ho_final.txt` (written only after a successful
    build, since `extract_metrics` runs the held-out suite inside the build's
    success branch) and `.src_final.txt`. Neither present -> (None, None, None)
    and the run is invalid, because guessing here is how leg A's sign gets lost.
    """
    final_build = record.get("finalBuild") or {}
    heldout = record.get("heldoutFinal") or {}
    built = final_build.get("ok")
    failed = heldout.get("failedTests")
    silence = heldout.get("silenceFailures")
    if isinstance(built, bool) and isinstance(failed, list) and isinstance(silence, list):
        return built, failed, silence

    ho_path = os.path.join(run_dir, ".ho_final.txt")
    src_path = os.path.join(run_dir, ".src_final.txt")
    if not os.path.exists(ho_path) and not os.path.exists(src_path):
        return (built if isinstance(built, bool) else None), failed, silence
    parsed = HARNESS_CAPTURE.read_heldout_final(ho_path)
    if parsed["source"] == "missing":
        # The declared-done build failed: no held-out run happened, so no escape.
        return False, [], []
    src_ok = True
    if os.path.exists(src_path):
        with open(src_path, encoding="utf-8", errors="replace") as fh:
            src_ok = ": error " not in fh.read()
    return src_ok, parsed["failedTests"], parsed["silenceFailures"]


def read_final_sources(run_dir):
    root = os.path.join(run_dir, "final-src")
    out = []
    for path in sorted(glob.glob(os.path.join(root, "**", "*.calr"), recursive=True)):
        with open(path, encoding="utf-8") as fh:
            out.append(fh.read())
    return out


# ---------------------------------------------------------------------------
# pair metadata
# ---------------------------------------------------------------------------
PAIRS_ROOT = os.path.join(BENCH, "pairs")


def load_pair(pair_dir, pairs_root=None):
    path = os.path.join(pairs_root or PAIRS_ROOT, pair_dir, "pair.json")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def blob_sha(path):
    """The `git hash-object` value: SHA-1 over "blob <len>\0" + content."""
    with open(path, "rb") as fh:
        data = fh.read()
    return hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest()


def starter_blob_drift(pair_dir, pair, pairs_root=None):
    """A-1.12 freezes each arm's starter by blob SHA. A starter whose bytes have
    MOVED was edited by this workstream — the own-goal clause's first named
    example ("a starter broken by a W-series edit"). A starter whose bytes are
    intact but whose multiset differs is route (a) and NOT auto-attributed: the
    cause could be the compiler rather than the fixture, and gates §0.3 forbids
    guessing. Returns a list of {arm, path, expected, observed}."""
    base = os.path.join(pairs_root or PAIRS_ROOT, pair_dir)
    drift = []
    for arm_key, arm in sorted((pair.get("arms") or {}).items()):
        frozen = (arm.get("starterBlob") or {}).get("sha")
        fixture = arm.get("fixture")
        if not frozen or not fixture:
            continue
        files = sorted(glob.glob(os.path.join(base, fixture, "**", "*.calr"), recursive=True))
        observed = [blob_sha(f) for f in files]
        if frozen not in observed:
            drift.append({"arm": arm_key, "path": "%s/%s" % (pair_dir, fixture),
                          "expected": frozen, "observed": observed})
    return drift


def indicator_self_check(pair_dir, pair, pairs_root=None):
    """A-1.12: "for every pair, the indicator must be FALSE on that pair's own
    frozen starter and TRUE on its frozen `clean` seed". An indicator
    satisfiable by the starter measures nothing and would make the obligation
    self-satisfying — #1123's review found exactly that on W-004."""
    spec = pair.get("shapeRealizedIndicator") or {}
    source_regex = spec.get("sourceRegex")
    if not source_regex:
        return {"pair": pair_dir, "hasIndicator": False, "falseOnStarter": None,
                "trueOnClean": None, "ok": False}
    rx = re.compile(source_regex)
    base = os.path.join(pairs_root or PAIRS_ROOT, pair_dir)

    def hits(rel):
        if not rel:
            return None
        files = sorted(glob.glob(os.path.join(base, rel, "**", "*.calr"), recursive=True))
        if not files:
            return None
        return any(rx.search(open(f, encoding="utf-8").read()) for f in files)

    starter_hits, clean_hits = [], []
    for arm_key, arm in (pair.get("arms") or {}).items():
        starter_hits.append(hits(arm.get("fixture")))
    for arm_letter in ("a", "b"):
        clean_hits.append(hits(((pair.get("seeded") or {}).get("clean") or {}).get(arm_letter)))
    false_on_starter = all(h is False for h in starter_hits) and bool(starter_hits)
    true_on_clean = all(h is True for h in clean_hits) and bool(clean_hits)
    return {"pair": pair_dir, "hasIndicator": True,
            "falseOnStarter": false_on_starter, "trueOnClean": true_on_clean,
            "ok": false_on_starter and true_on_clean}


def frozen_compiles(pairs_root=None):
    """The frozen per-arm seeded-compile multisets (S3, `a1230e2a`), reduced to
    (exit, [code severity line:column]) per pair x role x arm."""
    path = (os.path.join(pairs_root, "ppw-seeded-compiles.json") if pairs_root
            else SEEDED_COMPILES)
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    out = {}
    for cell in data.get("compiles", []):
        if cell.get("role") not in REGISTERED_COMPILE_ROLES:
            continue
        key = (cell["pair"], cell["role"], cell["arm"])
        out[key] = {
            "exitCode": cell.get("exitCode"),
            "diagnostics": ["%s %s %d:%d" % (d["code"], d["severity"], d["line"], d["column"])
                            for d in cell.get("diagnostics", [])],
        }
    return out


def starter_compiles_from(path):
    """Read a starter-compile record (ppw-compile.py's schema) into the same
    reduced shape, keyed (pair, arm)."""
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    out = {}
    for cell in data.get("compiles", []):
        if cell.get("role") != "starter":
            continue
        out[(cell["pair"], cell["arm"])] = {
            "exitCode": cell.get("exitCode"),
            "diagnostics": sorted("%s %s %d:%d" % (d["code"], d["severity"], d["line"], d["column"])
                                  for d in cell.get("diagnostics", [])),
        }
    return out


# ---------------------------------------------------------------------------
# the analysis
# ---------------------------------------------------------------------------
def _artifact(path):
    """Artifacts are published as REPO-RELATIVE paths: the ledger must be byte
    stable across checkouts, so no absolute path may ever reach it."""
    absolute = os.path.abspath(path)
    return (os.path.relpath(absolute, REPO).replace(os.sep, "/")
            if absolute.startswith(REPO + os.sep) else os.path.basename(absolute))


def analyze(epoch_dir, dry_run=False, starter_compiles=None, pairs_root=None,
            sizing_sims=SIZING_SIMS, sizing_boot=SIZING_BOOT):
    """Pure: returns (analysis dict, printable lines)."""
    lines = []
    pins_path = os.path.join(epoch_dir, "pins.json")
    if not os.path.exists(pins_path):
        raise SystemExit("ERROR: %s not found — not an epoch directory" % pins_path)
    with open(pins_path, encoding="utf-8") as fh:
        pins = json.load(fh)
    epoch_name = pins.get("epochId") or os.path.basename(os.path.abspath(epoch_dir))

    if not dry_run and (pins.get("kind") != KIND or epoch_name != EPOCH_ID):
        raise SystemExit(
            "ERROR: %s records kind=%r epochId=%r; PP-W-rows is the registered epoch %s of kind "
            "%s. Pass --dry-run to exercise the arithmetic on another epoch (the output is then "
            "labelled and is never recorded in the ledger)."
            % (pins_path, pins.get("kind"), epoch_name, EPOCH_ID, KIND))

    blockers = []          # route (c) unless another route names them
    own_goal_causes = []   # {cause, artifact} — published WITH the artifact, never in prose

    # ---- validity condition (4): the ppW block, both arm commits, the harness commit
    ppw = pins.get("ppW") if isinstance(pins.get("ppW"), dict) else None
    if ppw is None:
        blockers.append("pins.json carries no ppW block (validity condition (4))")
        own_goal_causes.append({"cause": "the epoch's pins.json has no ppW block — a harness "
                                         "misconfigured by its author",
                                "artifact": _artifact(os.path.join(epoch_dir, "pins.json"))})
        leg_b_pairs, blind_pairs = [], []
    else:
        try:
            read = HARNESS_CAPTURE.read_leg_b_pairs(pins_path)
            leg_b_pairs, blind_pairs = read["legBPairs"], read["blindPairs"]
            unknown = sorted({p for p in leg_b_pairs + blind_pairs
                              if normalize_pair(p) is None})
            if unknown:
                blockers.append("pins.json ppW names pair id(s) %s that are neither registered "
                                "spelling (short id or directory) — the denominator cannot be "
                                "resolved and is never guessed" % unknown)
            leg_b_pairs = [normalize_pair(p) for p in leg_b_pairs if normalize_pair(p)]
            blind_pairs = [normalize_pair(p) for p in blind_pairs if normalize_pair(p)]
        except (ValueError, KeyError) as exc:
            blockers.append("pins.json ppW block is malformed: %s" % exc)
            own_goal_causes.append({"cause": "the epoch's ppW block is malformed — a harness "
                                             "misconfigured by its author",
                                    "artifact": _artifact(os.path.join(epoch_dir, "pins.json"))})
            leg_b_pairs, blind_pairs = [], []

    # Inherited from run-m5-epoch.sh's base pins and NOT this experiment's pairs:
    # ppL5.pairs is the W2-/W3- loop suite, ppL6.pairs the N1- neutral set. Reading
    # either would be a silent wrong-denominator bug, so they are named as ignored
    # rather than left to look like they were missed.
    stale_ignored = [key for key in ("ppL5", "ppL6", "ppW5") if key in pins]

    arm_a, arm_b = pins.get("armA") or {}, pins.get("armB") or {}
    if not arm_a.get("commit") or not arm_b.get("commit"):
        blockers.append("pins.json does not record both arm commits (validity condition (4))")
    if not pins.get("harnessCommit"):
        blockers.append("pins.json does not record the harness commit — the "
                        "<CalorPermissiveEffects> template and the pre-spend canary live in the "
                        "harness, so a run whose template came from an unrecorded harness "
                        "checkout is invalid (validity condition (4))")

    # The sets are READ from pins (never a script default) and then CHECKED against
    # the frozen registration: a live epoch naming anything else is invalid (4).
    if not dry_run:
        if sorted(leg_b_pairs) != sorted(REGISTERED_LEG_B):
            blockers.append("pins.json ppW.legBPairs is %s; A-1.12 freezes %s"
                            % (sorted(leg_b_pairs), sorted(REGISTERED_LEG_B)))
        if sorted(blind_pairs) != sorted(REGISTERED_BLIND):
            blockers.append("pins.json ppW.blindPairs is %s; A-1.12 freezes %s"
                            % (sorted(blind_pairs), sorted(REGISTERED_BLIND)))

    control_root = arm_a.get("repoRoot") or ""
    treatment_root = arm_b.get("repoRoot") or ""
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
    if not dry_run and pins.get("mode", "live") != "live":
        blockers.append("pins.json records mode=%r; only a live epoch can adjudicate PP-W-rows"
                        % pins.get("mode"))

    # ---- collect runs --------------------------------------------------------
    all_pairs = sorted(set(leg_b_pairs) | set(blind_pairs) | set(REGISTERED_WARNING_VS_ERROR)) \
        if (leg_b_pairs or blind_pairs) else sorted(PAIR_DIRS.values())
    if not dry_run:
        all_pairs = sorted(PAIR_DIRS.values())
    pair_meta = {}
    for pair_dir in all_pairs:
        try:
            pair_meta[pair_dir] = load_pair(pair_dir, pairs_root)
        except OSError:
            blockers.append("pairs/%s/pair.json is missing" % pair_dir)

    runs = {}
    unattributed = 0
    null_agent_runs = 0
    missing_transcript = []
    token_sources = Counter()
    spend_usd = 0.0
    paid_runs = 0        # runs that actually billed — the denominator of the measured per-run cost
    for path in sorted(glob.glob(os.path.join(epoch_dir, "*", "*", "run-*", "result.json"))):
        run_dir = os.path.dirname(path)
        with open(path, encoding="utf-8") as fh:
            record = json.load(fh)
        pair_dir = os.path.basename(os.path.dirname(os.path.dirname(run_dir)))
        if pair_dir not in all_pairs:
            continue
        root = record.get("armRepoRoot") or ""
        if root and root == control_root:
            role = "control"
        elif root and root == treatment_root:
            role = "treatment"
        else:
            unattributed += 1
            continue
        if record.get("nullAgent"):
            null_agent_runs += 1
        invalid = bool(record.get("invalid", False))
        rel = os.path.relpath(run_dir, epoch_dir)

        # (1) a run without W1's transcript is INVALID -> route (b)
        has_transcript = os.path.exists(os.path.join(run_dir, "transcript.jsonl"))
        if not has_transcript:
            missing_transcript.append(rel)

        # (2) `turns.assistantMessages` must be the registered TOP-LEVEL count —
        # distinct assistant message.id values whose parent_tool_use_id is null,
        # with forwarded subagent messages counted SEPARATELY and never folded in.
        # A-1.12 discloses the exact divergence to look for: a runner that wrote
        # top-level + subagent under that name is recording a different metric
        # under a registered name, which would make the turn count depend on
        # whether --forward-subagent-text was passed.
        turns = record.get("turns") or {}
        top = turns.get("assistantMessages")
        reason = None
        if not invalid and has_transcript:
            recomputed = HARNESS_CAPTURE.count_turns(
                os.path.join(run_dir, "transcript.jsonl"))["assistantMessages"]
            if not isinstance(top, int):
                reason = "turns.assistantMessages is %r, not an integer count" % top
            elif top != recomputed:
                reason = ("turns.assistantMessages is %d but the archived transcript holds %d "
                          "distinct TOP-LEVEL assistant message.id value(s); the recorded field "
                          "is not the registered quantity (a runner writing top-level + subagent "
                          "under that name records a different metric under a registered name)"
                          % (top, recomputed))
        elif not invalid and not isinstance(top, int):
            reason = "turns.assistantMessages is %r, not an integer count" % top
        if reason:
            blockers.append("%s: %s (validity condition (2))" % (rel, reason))

        # The per-arm canary verdict, archived in result.json by `run-pair.sh`'s
        # `arm_canary`. BOTH arms are canaried and the two verdicts are DIFFERENT
        # strings, because they prove opposite facts about the same laundering
        # program: the control arm proves its Calor.Tasks honours
        # <CalorPermissiveEffects> (`warning Calor0410`, build succeeds ->
        # "permissive-ok"), the treatment arm proves its own rejects the same
        # program (`error Calor0410`, build fails -> "strict-ok"). An arm carrying
        # the OTHER arm's verdict is the arm swap this check exists to catch.
        # "skipped" is reachable only for a --null-agent run into a '-null'
        # directory and can never appear in an epoch that adjudicates.
        canary = record.get("armCanary")
        expected_canary = ("permissive-ok" if record.get("controlArmKind") == "pre-rows"
                           else "strict-ok")
        canary_ok = (canary == expected_canary)
        if not invalid and not canary_ok:
            blockers.append("%s: the %s arm's canary verdict is %r, not %r — %s"
                            % (rel,
                               "pre-rows control" if expected_canary == "permissive-ok"
                               else "strict treatment",
                               canary, expected_canary,
                               "the arm's Calor.Tasks does not honour <CalorPermissiveEffects>"
                               if expected_canary == "permissive-ok"
                               else "the arm's Calor.Tasks did not reject a laundered effect"))

        # leg A's two facts, both registered
        built, failed_tests, silence_failures = read_declared_done(run_dir, record)
        observing = ((pair_meta.get(pair_dir) or {}).get("tests") or {}).get("effectObservingTests")
        leg_a_readable = (isinstance(built, bool) and isinstance(failed_tests, list)
                          and isinstance(silence_failures, list)
                          and isinstance(observing, list) and len(observing) > 0)
        if not invalid and not leg_a_readable:
            if not observing:
                blockers.append("pairs/%s names no effectObservingTests — the pair cannot produce "
                                "a leg-A rate (route (c))" % pair_dir)
            else:
                # Fail closed. A run that does not archive `finalBuild.ok` and
                # `heldoutFinal.failedTests` cannot be scored under the registered
                # escape rule at all, and `escapedBugs` may not be substituted for
                # them — that substitution is the sign inversion this row replaces.
                blockers.append("%s: no finalBuild.ok / heldoutFinal.failedTests — the registered "
                                "leg-A escape (>= 1 named effectObservingTest failing on a "
                                "workspace that BUILT) cannot be computed for this run, and "
                                "escapedBugs may not stand in for it (route (c))" % rel)

        escape = None
        named_failure = None
        if leg_a_readable:
            # THE REGISTERED RULE: an escape is >= 1 named effectObservingTest failing on a
            # workspace that BUILT. A non-building final state contributes NO escape.
            #
            # REFINED, post hoc and arm-symmetrically: the failure must be the SILENCE
            # assertion. W-001's Twice_IsSilent_AfterProbe and W-003's Sum2_* assert the
            # RETURN VALUE before the silence assertion, so a silent-but-WRONG solution
            # fails a named effect-observing test having laundered nothing (measured:
            # `Expected: 2 / Actual: 1`). Reading any failure of a named test as an escape
            # would count that as laundering. The refinement fires identically on both
            # arms, so it removes noise and cannot move the delta's sign.
            named_failure = bool(built) and any(t in failed_tests for t in observing)
            escape = bool(built) and any(t in silence_failures for t in observing)

        tokens, source = (None, "invalid") if invalid else _corrected_tokens(run_dir)
        token_sources[source] += 1
        cost = _run_cost(run_dir)
        if cost:
            spend_usd += cost
            paid_runs += 1

        category, categories = (None, [])
        if escape and role == "treatment":
            category, categories = classify_escape(read_final_sources(run_dir))
            if category is None:
                category, categories = "other", ["other"]

        shape_realized = None
        spec = (pair_meta.get(pair_dir) or {}).get("shapeRealizedIndicator") or {}
        if spec.get("sourceRegex"):
            sources = read_final_sources(run_dir)
            rx = re.compile(spec["sourceRegex"])
            shape_realized = any(rx.search(s) for s in sources) if sources else None

        runs.setdefault(pair_dir, {}).setdefault(role, []).append({
            "run": record.get("run"),
            "dir": rel,
            "invalid": invalid,
            "censored": bool(record.get("censored", False)) or invalid,
            "hasTranscript": has_transcript,
            "canaryOk": canary_ok,
            "builtAtDeclaredDone": built,
            "legAReadable": leg_a_readable,
            "escape": escape,
            "namedTestFailure": named_failure,
            "escapeCategory": category,
            "escapeCategories": categories,
            "shapeRealized": shape_realized,
            "compilerHash": record.get("compilerHash"),
            "optionsHash": (record.get("buildState") or {}).get("optionsHash"),
            "tokens": tokens,
            "tokenSource": source,
            "itg": record.get("iterationsToGreen"),
        })

    if unattributed:
        blockers.append("%d run(s) carry no armRepoRoot matching either pinned arm" % unattributed)
    if null_agent_runs and not dry_run:
        blockers.append("%d null-agent run(s) in the epoch — a plumbing check is not a measurement "
                        "and cannot adjudicate PP-W-rows (run it under another epoch id)"
                        % null_agent_runs)

    # (3) each arm's compilerHash set must be a singleton, and the two disjoint
    hashes = {}
    for role in ("control", "treatment"):
        seen = {r["compilerHash"] for p in runs for r in runs[p].get(role, [])
                if not r["invalid"] and r["compilerHash"]}
        hashes[role] = sorted(seen)
        if len(seen) != 1:
            blockers.append("the %s arm records %d distinct compilerHash value(s) %s — validity "
                            "condition (3) requires exactly one per arm"
                            % (role, len(seen), sorted(seen)))
    if hashes["control"] and hashes["treatment"] and \
            set(hashes["control"]) & set(hashes["treatment"]):
        blockers.append("the two arms share a compilerHash — the agent-visible compiler is the "
                        "same on both arms (validity condition (3))")

    # The optionsHash leg, which is what witnesses the PERMISSIVE POLICY: a control
    # arm built from the registered commit but run STRICT leaves compilerHash
    # unchanged and moves only optionsHash, so the compilerHash leg alone cannot see
    # it. The epoch's own ppW.validity says exactly this; the runner checks only the
    # compilerHash half.
    options = {}
    for role in ("control", "treatment"):
        seen = {r["optionsHash"] for p in runs for r in runs[p].get(role, [])
                if not r["invalid"] and r["optionsHash"]}
        options[role] = sorted(seen)
    if not options["control"] or not options["treatment"]:
        blockers.append("buildState.optionsHash is not recorded on both arms — the permissive "
                        "policy is then unwitnessed, since a control arm built from the "
                        "registered commit but run STRICT leaves compilerHash unchanged and "
                        "moves only optionsHash (validity condition (3))")
    elif set(options["control"]) & set(options["treatment"]):
        blockers.append("the two arms share a buildState.optionsHash %s — the control arm was "
                        "not run under <CalorPermissiveEffects>, whatever its commit says "
                        "(validity condition (3))"
                        % sorted(set(options["control"]) & set(options["treatment"])))

    # ---- the PP-W5 validity floor -------------------------------------------
    def valid_runs(pair, role):
        return [r for r in runs.get(pair, {}).get(role, [])
                if not r["invalid"] and r["hasTranscript"] and r["canaryOk"]]

    dropped, surviving = [], []
    for pair in all_pairs:
        c, t = valid_runs(pair, "control"), valid_runs(pair, "treatment")
        if len(c) < MIN_RUNS_PER_CELL or len(t) < MIN_RUNS_PER_CELL:
            dropped.append({"pair": pair, "controlValid": len(c), "treatmentValid": len(t)})
        else:
            surviving.append(pair)
    if dropped:
        blockers.append("the PP-W5 validity floor dropped %d pair(s) (a cell with < %d valid "
                        "runs drops its pair): %s"
                        % (len(dropped), MIN_RUNS_PER_CELL, ", ".join(d["pair"] for d in dropped)))

    # (5) §2's censoring cap, per arm
    censored = {}
    for role in ("control", "treatment"):
        total_runs = sum(len(runs.get(p, {}).get(role, [])) for p in all_pairs)
        cen = sum(1 for p in all_pairs for r in runs.get(p, {}).get(role, []) if r["censored"])
        frac = (cen / total_runs) if total_runs else 0.0
        censored[role] = round(frac, 3)
        if frac > CENSOR_CAP:
            blockers.append("%s censored fraction %.0f%% exceeds the §2 cap of %.0f%%"
                            % (role, frac * 100, CENSOR_CAP * 100))

    # ---- route (a): the UNMUTATED starters against their frozen multisets ----
    frozen = frozen_compiles(pairs_root)
    route_a = {"fires": None, "evaluated": False, "source": None, "evidence": []}
    sc_path = starter_compiles or os.path.join(epoch_dir, "ppw-starter-compiles.json")
    if os.path.exists(sc_path):
        observed = starter_compiles_from(sc_path)
        route_a["evaluated"] = True
        route_a["source"] = _artifact(sc_path)
        failures = []
        for pair in sorted(PAIR_DIRS.values()):
            for arm in ("A", "B"):
                want = frozen.get((pair, "starter", arm))
                got = observed.get((pair, arm))
                if want is None:
                    continue
                want_norm = {"exitCode": want["exitCode"], "diagnostics": sorted(want["diagnostics"])}
                if got is None:
                    failures.append({"pair": pair, "arm": arm, "reason": "not compiled",
                                     "expected": want_norm, "observed": None})
                elif got != want_norm:
                    failures.append({"pair": pair, "arm": arm, "reason": "multiset differs",
                                     "expected": want_norm, "observed": got})
        route_a["fires"] = bool(failures)
        route_a["evidence"] = failures
    else:
        route_a["fires"] = None
        blockers.append("route (a) cannot be evaluated: no starter-compile record at %s. An "
                        "adjudication that never re-checked the unmutated starters against their "
                        "frozen multisets is not an adjudication." % os.path.basename(sc_path))

    failing_blind = {f["pair"] for f in route_a["evidence"]} if route_a["evaluated"] else set()
    blind_surviving_a = [p for p in sorted(blind_pairs) if p not in failing_blind]
    # (a') is "fewer than two blind cells SURVIVE (a)", so it is only meaningful
    # once (a) has been evaluated over a known blind set. An epoch with no blind
    # set at all is a validity-(4) defect and takes route (c); letting (a') fire
    # on it would report the wrong route for the wrong reason.
    route_a_prime = (route_a["evaluated"] and bool(blind_pairs)
                     and len(blind_surviving_a) < BLIND_FLOOR)

    # ---- the shape-realized indicator's own obligation ----------------------
    indicator_checks = []
    blob_drift = []
    for pair_dir in sorted(PAIR_DIRS.values()):
        pair = pair_meta.get(pair_dir)
        if pair is None:
            continue
        for d in starter_blob_drift(pair_dir, pair, pairs_root):
            blob_drift.append(dict(d, pair=pair_dir))
            blockers.append("pairs/%s's %s starter no longer hashes to the blob A-1.12 froze "
                            "(%s)" % (pair_dir, d["arm"], d["expected"]))
            own_goal_causes.append({
                "cause": "pairs/%s's %s starter was edited: its bytes no longer hash to the "
                         "frozen blob %s — a starter broken by a W-series edit"
                         % (pair_dir, d["arm"], d["expected"]),
                "artifact": "bench/phase0-agent-native/pairs/%s" % d["path"]})
        check = indicator_self_check(pair_dir, pair, pairs_root)
        indicator_checks.append(check)
        if not check["ok"]:
            blockers.append("pairs/%s's shapeRealizedIndicator does not satisfy A-1.12's "
                            "obligation (FALSE on the frozen starter, TRUE on the frozen clean "
                            "seed): %r" % (pair_dir, check))
            own_goal_causes.append({
                "cause": "pairs/%s's shape-realized indicator is satisfiable by its own starter "
                         "(or not by its clean seed) — an indicator that measures nothing"
                         % pair_dir,
                "artifact": "bench/phase0-agent-native/pairs/%s/pair.json" % pair_dir})

    # A-1.12's own-goal clause covers causes an analyzer cannot see — "a Calor0410
    # demotion changed before the epoch" is one. The epoch may therefore RECORD an
    # own goal in `pins.ppW.ownGoal`, and it is honoured only when it names a cause
    # AND an artifact that EXISTS: "the cause must be published WITH THE ARTIFACT
    # that shows it, never asserted in prose". It can add an own goal, never remove
    # one the instrument derived.
    recorded_own_goal = (ppw or {}).get("ownGoal")
    if isinstance(recorded_own_goal, dict):
        cause = recorded_own_goal.get("cause")
        artifact = recorded_own_goal.get("artifact")
        if cause and artifact and os.path.exists(os.path.join(REPO, artifact)):
            own_goal_causes.append({"cause": "recorded by the epoch: %s" % cause,
                                    "artifact": artifact})
        else:
            blockers.append("pins.json ppW.ownGoal names no cause, or an artifact that does not "
                            "exist (%r): an own goal must be published WITH the artifact that "
                            "shows it, never asserted in prose" % artifact)

    harness_valid = not blockers

    # ---- leg A ---------------------------------------------------------------
    per_cell = []
    escape_rates = {}
    for pair in sorted(all_pairs):
        for role, arm_key in (("control", "A"), ("treatment", "B")):
            cell = valid_runs(pair, role)
            readable = [r for r in cell if r["escape"] is not None]
            escapes = [1.0 if r["escape"] else 0.0 for r in readable]
            non_building = [r["dir"] for r in cell if r["builtAtDeclaredDone"] is False]
            realized = [r for r in cell if r["shapeRealized"] is True]
            cats = Counter(r["escapeCategory"] for r in readable
                           if r["escape"] and r["escapeCategory"])
            named_only = [r["dir"] for r in readable if r["namedTestFailure"] and not r["escape"]]
            per_cell.append({
                "pair": pair,
                "arm": arm_key,
                "class": _pair_class(pair),
                "validRuns": len(cell),
                "readableRuns": len(readable),
                "escapes": int(sum(escapes)),
                "escapeRate": (round(sum(escapes) / len(escapes), 4) if escapes else None),
                # A named effect-observing test failed WITHOUT the silence signature:
                # a silent-but-wrong solution, not laundering. Published, never scored.
                "namedTestFailuresWithoutSilence": sorted(named_only),
                "didNotBuildAtDeclaredDone": len(non_building),
                "didNotBuildRuns": sorted(non_building),
                "shapeRealized": len(realized),
                "shapeRealizedRate": (round(len(realized) / len(cell), 4) if cell else None),
                "escapeCategories": {k: v for k, v in sorted(cats.items())},
            })
            if escapes:
                escape_rates[(pair, role)] = escapes

    def delta_stats(pairs):
        pairs = [p for p in sorted(pairs)
                 if p in surviving
                 and (p, "control") in escape_rates and (p, "treatment") in escape_rates]
        if not pairs:
            return {"pairs": [], "perPair": [], "medianDelta": None, "lowerBound95": None,
                    "meetsBar": None}
        arm_a_rates = {p: escape_rates[(p, "control")] for p in pairs}
        arm_b_rates = {p: escape_rates[(p, "treatment")] for p in pairs}
        per_pair = []
        for p in pairs:
            ra = sum(arm_a_rates[p]) / len(arm_a_rates[p])
            rb = sum(arm_b_rates[p]) / len(arm_b_rates[p])
            per_pair.append({"pair": p, "armA": round(ra, 4), "armB": round(rb, 4),
                             "delta": round(ra - rb, 4)})
        point = median([p["delta"] for p in per_pair])
        lower = bootstrap_lower_delta(pairs, arm_a_rates, arm_b_rates)
        return {"pairs": pairs, "perPair": per_pair,
                "medianDelta": None if point is None else round(point, 4),
                "lowerBound95": None if lower is None else round(lower, 4),
                "meetsBar": (None if lower is None else lower > LEG_A_BAR)}

    leg_a_blind = delta_stats(blind_pairs)
    leg_a_warning = delta_stats([p for p in REGISTERED_WARNING_VS_ERROR if p in all_pairs])

    # ---- leg B ---------------------------------------------------------------
    def tokens_for(pair, role):
        return [r["tokens"] for r in valid_runs(pair, role)
                if r["tokens"] is not None and r["tokenSource"] != "missing"]

    leg_b_surviving = [p for p in sorted(leg_b_pairs) if p in surviving
                       and len(tokens_for(p, "control")) >= MIN_RUNS_PER_CELL
                       and len(tokens_for(p, "treatment")) >= MIN_RUNS_PER_CELL
                       and statistics.mean(tokens_for(p, "control") or [0]) > 0]
    control = {p: tokens_for(p, "control") for p in leg_b_surviving}
    treatment = {p: tokens_for(p, "treatment") for p in leg_b_surviving}
    leg_b_per_pair = []
    for p in leg_b_surviving:
        ca, tb = statistics.mean(control[p]), statistics.mean(treatment[p])
        leg_b_per_pair.append({"pair": p, "controlMean": round(ca, 2), "controlRuns": len(control[p]),
                               "treatmentMean": round(tb, 2), "treatmentRuns": len(treatment[p]),
                               "ratio": round(tb / ca, 4)})
    point = median([p["ratio"] for p in leg_b_per_pair]) if leg_b_per_pair else None
    lower = bootstrap_lower_ratio(leg_b_surviving, control, treatment) if leg_b_surviving else None

    cell_cvs, cv_values = [], []
    for p in leg_b_surviving:
        for role, cell in (("control", control[p]), ("treatment", treatment[p])):
            value = cv(cell)
            if value is not None:
                cv_values.append(value)
                cell_cvs.append({"pair": p, "arm": role, "cv": round(value, 4)})
    median_cv = median(cv_values) if cv_values else None
    max_cv = max(cv_values) if cv_values else None

    bound_fires = None if lower is None else lower > LOWER_BOUND_GATE
    sensitivity = []
    for margin in SENSITIVITY_MARGINS:
        exceeds = None if point is None else point > margin
        sensitivity.append({
            "margin": margin,
            "registered": margin == MARGIN,
            "population": {1.20: "e1-rows-parity-001", 1.30: "pooled",
                           1.35: "w5-parity-002"}[margin],
            "pointEstimate": None if point is None else round(point, 4),
            "lowerBound95": None if lower is None else round(lower, 4),
            "pointExceedsMargin": exceeds,
            "fails": (None if (exceeds is None or bound_fires is None)
                      else bool(exceeds and bound_fires)),
        })
    leg_b_fails = next(s["fails"] for s in sensitivity if s["registered"])
    point_exceeds = next(s["pointExceedsMargin"] for s in sensitivity if s["registered"])

    itg = {}
    for role in ("control", "treatment"):
        values = [r["itg"] for p in all_pairs for r in runs.get(p, {}).get(role, [])
                  if not r["invalid"] and r["itg"] is not None]
        itg[role] = {str(k): v for k, v in sorted(Counter(values).items())}

    achievable_power = (ppw or {}).get("achievablePower")

    # ---- the four-valued outcome, in its frozen precedence -------------------
    routes = OrderedDict()
    routes["a"] = {"fires": bool(route_a["fires"]), "evaluated": route_a["evaluated"],
                   "rule": "any unmutated starter fails to reproduce its frozen multiset on its "
                           "arm (severity and exit are part of the multiset)",
                   "artifact": route_a["source"], "evidence": route_a["evidence"]}
    routes["aPrime"] = {"fires": bool(route_a_prime),
                        "rule": "fewer than two blind cells survive (a)",
                        "blindSurvivingA": blind_surviving_a, "floor": BLIND_FLOOR,
                        "artifact": route_a["source"]}
    routes["b"] = {"fires": bool(missing_transcript),
                   "rule": "any run lacks W1's transcript.jsonl",
                   "artifact": (missing_transcript[0] if missing_transcript else None),
                   "evidence": sorted(missing_transcript)}
    routes["c"] = {"fires": not harness_valid,
                   "rule": "the PP-W5 validity floor / distinct-hash / censoring routes, plus "
                           "validity conditions (2) and (4), which A-1.12 wires here",
                   "artifact": _artifact(pins_path),
                   "evidence": blockers}
    routes["d"] = {"fires": False, "inert": True,
                   "rule": "W2 does not ship in 0.16.0 AND roadmap §9 cut line 2 was invoked in "
                           "writing — INERT BY CONSTRUCTION: cut line 2's antecedent is \"if "
                           "A-1.12 has not registered by the 0.16 branch cut\", which merging "
                           "A-1.12 made permanently false. A W2 slip for any reason is a MISS.",
                   "artifact": None}
    firing = [k for k in ("a", "aPrime", "b", "c", "d") if routes[k]["fires"]]
    own_goal = bool(own_goal_causes)

    leg_a_meets = leg_a_blind["meetsBar"]
    underpowered = bool(
        (leg_a_meets is True and point_exceeds is True and bound_fires is False)
        or (median_cv is not None and median_cv > CV_CAP)
        or (isinstance(achievable_power, (int, float)) and achievable_power < MIN_POWER))

    if dry_run:
        # A-1.12 / A:81: THE DRY RUN SIZES N AND NEVER MOVES THE BAR. Emitting
        # HIT or MISS from a dry epoch is the worst failure mode this script has,
        # so no verdict is produced at all — the leg statistics and the sizing
        # calculation are, because sizing is the dry run's whole job.
        verdict = None
        route = None
        reason = ("DRY RUN: no verdict. This epoch is not the registered %s, so it sizes N and "
                  "reports variance; it never adjudicates and is never recorded in the ledger."
                  % EPOCH_ID)
    elif firing and not own_goal:
        verdict = "NOT-ADJUDICATED"
        route = firing[0]
        reason = "route (%s): %s" % (route.replace("Prime", "'"), routes[route]["rule"])
    elif own_goal or leg_a_meets is False or leg_b_fails is True:
        verdict = "MISS"
        route = firing[0] if firing else None
        if own_goal:
            reason = "own goal: " + "; ".join(c["cause"] for c in own_goal_causes)
        elif leg_a_meets is False:
            reason = ("leg A below its bar on a valid harness: median blind delta %s, one-sided "
                      "95%% lower bound %s (bar: > 0)"
                      % (leg_a_blind["medianDelta"], leg_a_blind["lowerBound95"]))
        else:
            reason = ("leg B fails: point %s > margin %.2f AND lower bound %s > %s"
                      % (point, MARGIN, lower, LOWER_BOUND_GATE))
    elif underpowered:
        verdict = "UNDERPOWERED"
        route = None
        reason = ("leg A at bar with leg B's point over the margin and the bound not firing"
                  if (leg_a_meets is True and point_exceeds and not bound_fires)
                  else "realized median within-cell CV %s over the %.2f cap" % (median_cv, CV_CAP)
                  if (median_cv is not None and median_cv > CV_CAP)
                  else "registered achievable power %s < %s at Delta = %s"
                       % (achievable_power, MIN_POWER, LEG_A_EFFECT_SIZE))
    elif leg_a_meets is True:
        verdict = "HIT"
        route = None
        reason = ("rows, fail-closed, caught the registered classes at no large loop tax "
                  "(never \"rows are free\"): median blind delta %s, lower bound %s; leg B point "
                  "%s, lower bound %s at margin %.2f"
                  % (leg_a_blind["medianDelta"], leg_a_blind["lowerBound95"], point, lower, MARGIN))
    else:
        # "ANY ROUTE NOT LISTED HERE IS A MISS." A valid harness that still yields no
        # leg-A statistic is not one of (a)-(d), and NOT-ADJUDICATED is exactly the
        # verdict this workstream may not quietly award itself.
        verdict = "MISS"
        route = None
        reason = ("leg A produced no statistic on a harness with no firing route — not one of "
                  "routes (a)-(d), and any route not listed in the frozen map is a MISS")

    analysis = OrderedDict()
    analysis["gate"] = "PP-W-rows (roadmap v0.16 §4.1; annex A-1.12, §A.2 row PP-W6)"
    analysis["epoch"] = epoch_name
    analysis["dryRun"] = dry_run
    analysis["dryRunNote"] = (None if not dry_run else
                              "DRY RUN: this epoch is not the registered w-rows-001; it exercises "
                              "the arithmetic only and is never recorded in the ledger")
    analysis["registeredEpoch"] = EPOCH_ID
    analysis["constants"] = OrderedDict([
        ("legABar", LEG_A_BAR), ("legAEffectSize", LEG_A_EFFECT_SIZE), ("blindFloor", BLIND_FLOOR),
        ("margin", MARGIN), ("sensitivityMargins", SENSITIVITY_MARGINS),
        ("lowerBoundGate", LOWER_BOUND_GATE), ("cvCap", CV_CAP),
        ("minRunsPerCell", MIN_RUNS_PER_CELL), ("censorCap", CENSOR_CAP),
        ("minPower", MIN_POWER), ("spendCeilingUsd", SPEND_CEILING_USD),
        ("bootstrapResamples", BOOT), ("seed", SEED)])
    analysis["armA"] = {"label": arm_a.get("label"), "role": "control",
                        "commit": arm_a.get("commit"), "calorTasksSha": a_tasks,
                        "compilerHashes": hashes["control"],
                        "optionsHashes": options["control"],
                        "provenance": "tag v0.14.3 PLUS the one commit %s on the never-merged "
                                      "branch arm/v0.14.3-pre-rows that threads the existing "
                                      "--permissive-effects policy through Calor.Tasks; the +1 "
                                      "is the registered additive mechanism, not drift"
                                      % ARM_A_COMMIT,
                        "editMechanism": arm_a.get("editMechanism")}
    analysis["armB"] = {"label": arm_b.get("label"), "role": "treatment",
                        "commit": arm_b.get("commit"), "calorTasksSha": b_tasks,
                        "compilerHashes": hashes["treatment"],
                        "optionsHashes": options["treatment"],
                        "provenance": "the v0.15.0 release tag, strict, no flags",
                        "editMechanism": arm_b.get("editMechanism")}
    analysis["harnessCommit"] = pins.get("harnessCommit")
    analysis["inheritedPinsIgnored"] = stale_ignored
    analysis["modelPin"] = pins.get("modelPin")
    analysis["legBPairs"] = sorted(leg_b_pairs)
    analysis["blindPairs"] = sorted(blind_pairs)
    analysis["warningVsErrorPairs"] = sorted(REGISTERED_WARNING_VS_ERROR)
    analysis["pairsSurviving"] = sorted(surviving)
    analysis["pairsDropped"] = dropped
    analysis["missingTranscript"] = sorted(missing_transcript)
    analysis["censored"] = censored
    analysis["tokenSources"] = dict(sorted(token_sources.items()))
    analysis["spendUsd"] = round(spend_usd, 4)
    # Runs that actually billed. It is NOT len(runs): a run the API refused (the
    # weekly usage limit that truncated w-rows-dry-001 at 19 of 36) archives a
    # result.json with total_cost_usd 0, and dividing the spend by the planned run
    # count would under-state the per-run cost by that fraction.
    analysis["paidRuns"] = paid_runs
    analysis["measuredCostPerRunUsd"] = (round(spend_usd / paid_runs, 4) if paid_runs else None)
    analysis["spendCeilingExceeded"] = spend_usd > SPEND_CEILING_USD
    analysis["disclosedLimitations"] = DISCLOSED_LIMITATIONS
    analysis["indicatorSelfCheck"] = indicator_checks
    analysis["starterBlobDrift"] = blob_drift
    analysis["perCell"] = per_cell
    analysis["legA"] = OrderedDict([
        ("rule", "an escape is >= 1 named effectObservingTest failing on a workspace that BUILT; "
                 "a non-building final state contributes NO escape and is published in its own "
                 "category, still counting against the censoring cap"),
        ("statistic", "the median over BLIND pairs of the per-pair escape-rate delta (A - B); "
                      "odd -> the middle value, even -> the mean of the two middle values"),
        ("bar", "the one-sided 95 % lower bound of that delta exceeds 0"),
        ("blind", leg_a_blind),
        ("warningVsError", leg_a_warning),
        ("meetsBar", leg_a_meets)])
    analysis["legB"] = OrderedDict([
        ("rule", "fails iff BOTH the one-sided 95 %% cluster-bootstrap lower bound of the median "
                 "paired per-pair output-tokens-to-green ratio (arm B / arm A) exceeds %s AND the "
                 "point estimate exceeds the margin %.2f; per-run figure = token-usage.py "
                 "output_tokens_corrected (A-1.9.1), never usage.output_tokens"
         % (LOWER_BOUND_GATE, MARGIN)),
        ("pairs", leg_b_surviving),
        ("perPair", leg_b_per_pair),
        ("pointEstimate", None if point is None else round(point, 4)),
        ("lowerBound95", None if lower is None else round(lower, 4)),
        ("boundFires", bound_fires),
        ("realizedMedianWithinCellCv", None if median_cv is None else round(median_cv, 4)),
        ("maxWithinCellCv", None if max_cv is None else round(max_cv, 4)),
        ("withinCellCv", cell_cvs),
        ("sensitivity", sensitivity),
        ("fails", leg_b_fails),
        ("iterationsToGreen", itg)])
    # The A:81 dry run's deliverable: size N from the realized between-pair spread
    # of the leg-A deltas, under the UPPER bound of that variance.
    sizing = None
    if dry_run:
        deltas = [entry["delta"] for entry in leg_a_blind["perPair"]]
        # The dry run's OTHER deliverable: the per-run cost, which A-1.12 pinned
        # from e1-rows-parity-001 while stating it was "unmeasured until the dry
        # run reports it". `paid_runs` counts the runs that actually billed, so a
        # dry epoch truncated part-way (a usage limit, an aborted collector) sizes
        # from what it spent rather than from the run count it planned.
        measured_cost = (spend_usd / paid_runs) if paid_runs else None
        prior = prior_dry_spend(epoch_dir)
        sizing = size_n(deltas, len(deltas), sims=sizing_sims, boot=sizing_boot,
                        cost_per_run=measured_cost,
                        prior_usd=(None if measured_cost is None
                                   else spend_usd + prior["spendUsd"]
                                   + PILOT_RUNS * measured_cost))
        sizing["priorDryEpochs"] = prior["epochs"]
    analysis["sizing"] = sizing
    analysis["achievablePower"] = achievable_power
    analysis["harnessValid"] = harness_valid
    analysis["blockers"] = blockers
    analysis["routes"] = routes
    analysis["ownGoal"] = own_goal
    analysis["ownGoalCauses"] = own_goal_causes
    analysis["ownGoalClause"] = ("a not-adjudicated route caused by this workstream's own change "
                                 "is adjudicated MISS, and the cause is published WITH THE "
                                 "ARTIFACT that shows it, never asserted in prose")
    analysis["precedence"] = "NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT"
    analysis["underpowered"] = underpowered
    analysis["verdict"] = verdict
    analysis["route"] = route
    analysis["reason"] = reason

    lines.append("=== PP-W-rows — %s%s ===" % (epoch_name, " (DRY RUN)" if dry_run else ""))
    lines.append("arm A = %s @ %s   arm B = %s @ %s"
                 % (arm_a.get("label"), arm_a.get("commit"), arm_b.get("label"), arm_b.get("commit")))
    lines.append("blind %s   legB %s   surviving %s"
                 % (sorted(blind_pairs), sorted(leg_b_pairs), sorted(surviving)))
    for cell in per_cell:
        lines.append("  %s arm %s (%s): escapes %s/%s rate %s | did-not-build %s | shape-realized "
                     "%s/%s | %s"
                     % (cell["pair"], cell["arm"], cell["class"], cell["escapes"],
                        cell["readableRuns"], cell["escapeRate"],
                        cell["didNotBuildAtDeclaredDone"], cell["shapeRealized"],
                        cell["validRuns"], cell["escapeCategories"] or "-"))
    lines.append("leg A (blind): median delta %s  lower bound %s  meets bar %s"
                 % (leg_a_blind["medianDelta"], leg_a_blind["lowerBound95"], leg_a_meets))
    lines.append("leg A (warning-vs-error, published beside, never pooled): median delta %s  "
                 "lower bound %s" % (leg_a_warning["medianDelta"], leg_a_warning["lowerBound95"]))
    lines.append("leg B: point %s  lower bound %s  median within-cell CV %s (cap %s)"
                 % (point, lower, median_cv, CV_CAP))
    for s in sensitivity:
        lines.append("  margin %.2f (%s%s): point exceeds %s, fails %s"
                     % (s["margin"], s["population"], ", REGISTERED" if s["registered"] else "",
                        s["pointExceedsMargin"], s["fails"]))
    if blockers:
        lines.append("HARNESS INVALID (route (c)):")
        for b in blockers:
            lines.append("  - %s" % b)
    for c in own_goal_causes:
        lines.append("OWN GOAL: %s  [%s]" % (c["cause"], c["artifact"]))
    if stale_ignored:
        lines.append("inherited pins IGNORED (not this experiment's pairs): %s"
                     % ", ".join(stale_ignored))
    if sizing is not None:
        lines.append("--- N sizing (the dry run's job; it never moves the bar) ---")
        lines.append("  observed blind deltas %s  variance %s  upper 95%% bound %s"
                     % (sizing["observedDeltas"], sizing["observedVariance"],
                        sizing["varianceUpperBound95"]))
        for entry in sizing["powerCurve"]:
            lines.append("  N=%-3d power %.3f  %d runs  ~$%.2f  fits ceiling %s"
                         % (entry["n"], entry["power"], entry["runs"],
                            entry["estimatedUsd"], entry["fitsCeiling"]))
        lines.append("  recommended N = %s (achievable power %s); %s"
                     % (sizing["recommendedN"], sizing.get("achievablePower"),
                        sizing.get("note") or "fits the $150 ceiling"))
        point = sizing.get("atPointVarianceNotRegistered") or {}
        lines.append("  (on the POINT variance, NOT the registered basis: N = %s)"
                     % point.get("recommendedN"))
    if verdict is None:
        lines.append("verdict: (none — %s)" % reason)
    else:
        lines.append("verdict: %s  (route %s) — %s" % (verdict, route, reason))
    return analysis, lines


def _pair_class(pair_dir):
    if pair_dir in REGISTERED_BLIND:
        return "blind"
    if pair_dir in REGISTERED_WARNING_VS_ERROR:
        return "warning-vs-error"
    return "unregistered"


def _corrected_tokens(run_dir):
    """The one blessed derivation of the cost-leg token figure (A-1.9.1)."""
    usage = TOKEN_USAGE.compute(TOKEN_USAGE.load_envelope(os.path.join(run_dir, "agent.json")))
    return usage["output_tokens_corrected"], usage["source"]


def prior_dry_spend(epoch_dir):
    """Every OTHER pp-w-rows dry epoch's spend, summed.

    The ceiling is cumulative over the whole proof point, and this experiment
    has already spent across more than one collection: the weekly usage limit
    truncated w-rows-dry-001 at 19 of 36 runs, so w-rows-dry-002 was collected
    to finish the three pairs it never reached. Pricing the affordable N against
    only the epoch in hand would forget the first collection entirely and
    over-state what is left — here by $27 of the $150, which is one whole
    runs-per-cell step.

    Sibling epochs are found beside `epoch_dir`, keyed on `pins.json` kind, and
    the registered epoch is never counted as prior spend to itself.
    """
    root = os.path.dirname(os.path.abspath(epoch_dir))
    here = os.path.basename(os.path.abspath(epoch_dir))
    total, runs = 0.0, 0
    epochs = []
    for name in sorted(os.listdir(root) if os.path.isdir(root) else []):
        if name == here or name.startswith("."):
            continue
        sibling = os.path.join(root, name)
        try:
            with open(os.path.join(sibling, "pins.json"), encoding="utf-8") as fh:
                pins = json.load(fh)
        except (OSError, ValueError):
            continue
        if not isinstance(pins, dict) or pins.get("kind") != KIND:
            continue
        spend, paid = 0.0, 0
        for path in glob.glob(os.path.join(sibling, "*", "*", "run-*", "result.json")):
            cost = _run_cost(os.path.dirname(path))
            if cost:
                spend += cost
                paid += 1
        if paid:
            epochs.append({"epoch": name, "paidRuns": paid, "spendUsd": round(spend, 4)})
            total += spend
            runs += paid
    return {"epochs": epochs, "paidRuns": runs, "spendUsd": round(total, 4)}


def _run_cost(run_dir):
    try:
        with open(os.path.join(run_dir, "agent.json"), encoding="utf-8") as fh:
            envelope = json.load(fh)
    except (OSError, ValueError):
        return None
    cost = envelope.get("total_cost_usd") if isinstance(envelope, dict) else None
    return float(cost) if isinstance(cost, (int, float)) else None


# ---------------------------------------------------------------------------
# the ledger (A-1.12: "INSTRUMENT that reads the outcome")
# ---------------------------------------------------------------------------
def build_ledger(analysis=None, pairs_root=None):
    if analysis is not None and analysis.get("dryRun"):
        raise SystemExit("ERROR: refusing to record a DRY RUN in the ledger. A dry run sizes N "
                         "and never adjudicates (A:81); only the registered epoch %s may reach "
                         "effect-rows-benefit-ledger.json." % EPOCH_ID)
    """Timestamp-free and byte-stable: nothing here reads the clock, the
    current HEAD, or any absolute path. `measuredCommit` is the epoch's arm-B
    commit — the product under test — falling back to the registered one."""
    frozen = frozen_compiles(pairs_root)
    ledger = OrderedDict()
    ledger["schemaVersion"] = 1
    ledger["proofPoint"] = "PP-W6 / PP-W-rows"
    ledger["registration"] = ("docs/plans/agent-native-gates.md §A.2 row PP-W6 + §A.3 entry A-1.12 "
                              "(2026-08-27)")
    ledger["claim"] = ("with rows, fail-closed, agents launder fewer effects on callback-heavy "
                       "code than under the pre-rows language as it was usable (warnings "
                       "included), at no large loop tax")
    ledger["measuredCommit"] = (analysis["armB"]["commit"] if analysis and analysis["armB"].get("commit")
                                else ARM_B_COMMIT)
    ledger["starterFreezeCommit"] = STARTER_FREEZE_COMMIT
    ledger["analyzer"] = "bench/phase0-agent-native/ppw-analyze.py"
    ledger["runner"] = "bench/phase0-agent-native/run-ppw-epoch.sh"
    ledger["epoch"] = EPOCH_ID
    ledger["epochRun"] = analysis is not None
    ledger["disclosedLimitations"] = DISCLOSED_LIMITATIONS
    ledger["escapeSemantics"] = (
        "an escape is AT LEAST ONE of the pair's named effectObservingTests failing on a workspace "
        "that BUILT at declared-done; a run whose final state does not build contributes NO escape "
        "and is published in a separate \"did not build at declared-done\" category, per arm and "
        "per pair, still counting against §2's censoring cap. The pair specs' escapedBugs rule "
        "(gates §2: a non-compiling final state counts as all tests failing) INVERTS leg A's sign "
        "here and is not used — and `escapedBugs` is in any case the aggregate Failed: count for "
        "the whole suite, which also counts failures of tests that are not effect-observing. The "
        "failure must additionally carry the SILENCE signature, because two pairs' "
        "effect-observing tests assert the return value first and a silent-but-wrong solution "
        "would otherwise read as laundering.")
    ledger["precedence"] = "NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT"
    ledger["constants"] = OrderedDict([
        ("legABar", LEG_A_BAR), ("legAEffectSize", LEG_A_EFFECT_SIZE), ("blindFloor", BLIND_FLOOR),
        ("margin", MARGIN), ("sensitivityMargins", SENSITIVITY_MARGINS),
        ("lowerBoundGate", LOWER_BOUND_GATE), ("cvCap", CV_CAP),
        ("minRunsPerCell", MIN_RUNS_PER_CELL), ("censorCap", CENSOR_CAP),
        ("minPower", MIN_POWER), ("spendCeilingUsd", SPEND_CEILING_USD),
        ("bootstrapResamples", BOOT), ("seed", SEED)])
    ledger["arms"] = OrderedDict([
        ("A", {"label": "calor+v0.14.3-pre-rows", "role": "control",
               "commit": ARM_A_COMMIT, "baseTag": "v0.14.3",
               "note": "tag v0.14.3 + the Calor.Tasks/Sdk.targets permissive passthrough on the "
                       "never-merged branch arm/v0.14.3-pre-rows; compiler semantics are "
                       "v0.14.3's. Valid as a CONTROL arm only, under A-1.12's additive "
                       "\"pre-rows control arm\" pin (permissiveEffects true AND controlArmKind "
                       "\"pre-rows\" AND this commit)."}),
        ("B", {"label": "calor+v0.15.0", "role": "treatment", "commit": ARM_B_COMMIT,
               "baseTag": "v0.15.0", "note": "strict, no flags; src/ equals 7d621c0d"})])

    pairs = []
    for short, pair_dir in PAIR_DIRS.items():
        pair = load_pair(pair_dir, pairs_root)
        arms = pair.get("arms") or {}
        entry = OrderedDict()
        entry["id"] = short
        entry["directory"] = pair_dir
        entry["class"] = _pair_class(pair_dir)
        entry["legB"] = pair_dir in REGISTERED_LEG_B
        entry["blind"] = pair_dir in REGISTERED_BLIND
        entry["effectObservingTests"] = list((pair.get("tests") or {}).get("effectObservingTests") or [])
        entry["starterBlobs"] = OrderedDict([
            ("A", (arms.get("calor-pre-rows") or {}).get("starterBlob", {})),
            ("B", (arms.get("calor") or {}).get("starterBlob", {}))])
        entry["shapeRealizedIndicator"] = (pair.get("shapeRealizedIndicator") or {}).get("sourceRegex")
        entry["indicatorSelfCheck"] = indicator_self_check(pair_dir, pair, pairs_root)
        entry["frozenCompiles"] = OrderedDict(
            (role, OrderedDict((arm, frozen[(pair_dir, role, arm)])
                               for arm in ("A", "B") if (pair_dir, role, arm) in frozen))
            for role in REGISTERED_COMPILE_ROLES
            if any((pair_dir, role, arm) in frozen for arm in ("A", "B")))
        pairs.append(entry)
    ledger["pairs"] = pairs
    ledger["registeredLegBPairs"] = list(REGISTERED_LEG_B)
    ledger["registeredBlindPairs"] = list(REGISTERED_BLIND)
    ledger["registeredWarningVsErrorPairs"] = list(REGISTERED_WARNING_VS_ERROR)
    ledger["publishedSiblings"] = [
        {"id": "W-001s", "status": "published sibling, not adjudicated",
         "note": "a printing §LAM bound by §B and passed to RunTwice on W-001's starters; not a "
                 "seventh pair and outside every denominator"},
        {"id": "unregistered-fourth-blind-cell", "status": "measured and NOT registered",
         "note": "the A3-match field shape with a second §FLD{Func<i32>:none:pri} for onNone; "
                 "archived under W-003-match-fallback/seeded/, outside the denominator and not "
                 "promotable into it after results are seen"}]

    if analysis is None:
        ledger["legBPairsFromPins"] = None
        ledger["blindPairsFromPins"] = None
        ledger["perCell"] = []
        ledger["legA"] = None
        ledger["legB"] = None
        ledger["routes"] = None
        ledger["ownGoal"] = False
        ledger["ownGoalCauses"] = []
        ledger["verdict"] = "NOT-ADJUDICATED"
        ledger["route"] = None
        ledger["reason"] = NOT_RUN_REASON
        return ledger

    ledger["legBPairsFromPins"] = analysis["legBPairs"]
    ledger["blindPairsFromPins"] = analysis["blindPairs"]
    ledger["harnessCommit"] = analysis["harnessCommit"]
    ledger["compilerHashes"] = {"A": analysis["armA"]["compilerHashes"],
                                "B": analysis["armB"]["compilerHashes"]}
    ledger["pairsSurviving"] = analysis["pairsSurviving"]
    ledger["pairsDropped"] = analysis["pairsDropped"]
    ledger["missingTranscript"] = analysis["missingTranscript"]
    ledger["censored"] = analysis["censored"]
    ledger["spendUsd"] = analysis["spendUsd"]
    ledger["spendCeilingExceeded"] = analysis["spendCeilingExceeded"]
    ledger["perCell"] = analysis["perCell"]
    ledger["legA"] = analysis["legA"]
    ledger["legB"] = analysis["legB"]
    ledger["achievablePower"] = analysis["achievablePower"]
    ledger["harnessValid"] = analysis["harnessValid"]
    ledger["blockers"] = analysis["blockers"]
    ledger["routes"] = analysis["routes"]
    ledger["ownGoal"] = analysis["ownGoal"]
    ledger["ownGoalCauses"] = analysis["ownGoalCauses"]
    ledger["verdict"] = analysis["verdict"]
    ledger["route"] = analysis["route"]
    ledger["reason"] = analysis["reason"]
    return ledger


def serialize(obj):
    return json.dumps(obj, indent=2, ensure_ascii=False) + "\n"


# ---------------------------------------------------------------------------
def main(argv=None):
    parser = argparse.ArgumentParser(
        prog="ppw-analyze.py",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("epoch_dir", nargs="?", default=None)
    parser.add_argument("--dry-run", action="store_true",
                        help="accept an epoch that is not the registered w-rows-001. A dry run "
                             "SIZES N and reports variance; it emits NO verdict and is never "
                             "recorded in the ledger (A:81: the dry run never moves the bar).")
    parser.add_argument("--sizing-sims", type=int, default=SIZING_SIMS,
                        help="simulated epochs per candidate N in the dry run's sizing loop")
    parser.add_argument("--sizing-boot", type=int, default=SIZING_BOOT,
                        help="bootstrap resamples inside the sizing loop (never the "
                             "adjudicator's %d)" % BOOT)
    parser.add_argument("--starter-compiles", default=None,
                        help="a ppw-compile.py record of the UNMUTATED starters on both arms "
                             "(default: <epoch>/ppw-starter-compiles.json). Route (a) cannot be "
                             "evaluated without it.")
    parser.add_argument("--ledger", action="store_true",
                        help="write effect-rows-benefit-ledger.json (timestamp-free, byte-stable)")
    parser.add_argument("--epochs-root", default=os.path.join(BENCH, "epochs"),
                        help="where to look for the registered epoch (--ledger mode)")
    parser.add_argument("--pairs-root", default=None,
                        help="where the six W-00x pair directories live (default: "
                             "bench/phase0-agent-native/pairs); a mutation lane sets it")
    parser.add_argument("--out", default=None)
    args = parser.parse_args(argv)

    if args.ledger:
        epoch_dir = args.epoch_dir or os.path.join(args.epochs_root, EPOCH_ID)
        analysis = None
        if os.path.exists(os.path.join(epoch_dir, "pins.json")):
            analysis, lines = analyze(epoch_dir, dry_run=False,
                                      starter_compiles=args.starter_compiles,
                                      pairs_root=args.pairs_root)
            for line in lines:
                print(line)
        else:
            print("epoch %s not present under %s — writing the not-run ledger (%s)"
                  % (EPOCH_ID, args.epochs_root, NOT_RUN_REASON))
        out = args.out or LEDGER_PATH
        with open(out, "w", encoding="utf-8") as fh:
            fh.write(serialize(build_ledger(analysis, pairs_root=args.pairs_root)))
        print("wrote %s" % out)
        return 0

    if not args.epoch_dir:
        parser.error("an epoch directory is required (or --ledger)")
    analysis, lines = analyze(args.epoch_dir, dry_run=args.dry_run,
                              starter_compiles=args.starter_compiles,
                              pairs_root=args.pairs_root,
                              sizing_sims=args.sizing_sims, sizing_boot=args.sizing_boot)
    out = args.out or os.path.join(
        args.epoch_dir, "ppw-analysis.dry-run.json" if args.dry_run else "ppw-analysis.json")
    with open(out, "w", encoding="utf-8") as fh:
        fh.write(serialize(analysis))
    for line in lines:
        print(line)
    print("wrote %s" % out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
