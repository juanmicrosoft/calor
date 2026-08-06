#!/usr/bin/env python3
"""PP-W5 adjudicator — strictness/toolchain parity (gates Annex A, A-1.4 tranche 1).

WRITTEN RESULTS-BLIND, before the epoch ran. The rule below is a transcription of the
frozen row, not a derivation: nothing here may be re-tuned once numbers exist.

    FAILS iff BOTH
      (a) the one-sided 95% cluster-bootstrap LOWER bound of the median paired
          per-pair output-tokens-to-green ratio (treatment / control) exceeds 1.0, AND
      (b) the point estimate of that median exceeds 1.25.

Note the direction. PP-L5 asks whether a ratio dropped BELOW a threshold (an
improvement) and uses the upper bound; PP-W5 asks whether it rose ABOVE 1.0 (a tax)
and uses the LOWER bound. The conjunction is what calibrates the false-fail rate to
~1.7% — the point test alone sits at the null p95 (1.247) and would false-fail ~5%.

Validity floor (A-1.4, the M-G4 pattern), applied BEFORE any statistic:
  - a cell (pair x arm) with < 2 valid runs drops its PAIR, disclosed;
  - if fewer than 3 pairs survive, OR either arm's total valid runs < 12, PP-W5 is
    REPORTED, NOT ADJUDICATED. Never silently computed over degenerate cells.

Power honesty (the PP-L5/PP-G4 pattern): 4 clusters x 5 runs bounds only LARGE
regressions — measured detection 0.33 / 0.62 / 0.87 at true 1.25x / 1.4x / 1.6x. A
pass means "no large tax detected", never "proven equal".

Usage: w5-analyze.py <epoch-dir>
"""
import glob
import json
import os
import random
import statistics
import sys

EPOCH = sys.argv[1] if len(sys.argv) > 1 else sys.exit("usage: w5-analyze.py <epoch-dir>")

# Both arms are the CALOR arm; they differ only in the compiler build.
#
# Arms are keyed on the PRODUCT root (`--arm-repo-root`), not on `calorDll` and not on
# the arm label. w5-parity-001 is why. There, `calorDll` split a clean 20/20 and was
# reported as provenance — but it is only the `--calor-dll` argument echoed back, and
# that flag pins the CLI, which the agent never invokes. Both arms compiled through the
# same Calor.Tasks and the epoch measured nothing. A key that can be satisfied while the
# contrast is absent is not provenance; it is decoration.
BOOT = 2000
MIN_RUNS_PER_CELL = 2      # below this, the PAIR drops
MIN_PAIRS = 3              # below this, reported-not-adjudicated
MIN_VALID_PER_ARM = 12     # below this, reported-not-adjudicated
LOWER_BOUND_GATE = 1.0     # (a)
POINT_GATE = 1.25          # (b)
CENSOR_CAP = 0.40          # gates doc §2: >40% censored on neutral tasks invalidates the gate
random.seed(4537)          # same seed convention as m5-analyze

pins = json.load(open(os.path.join(EPOCH, "pins.json")))
w5 = pins.get("ppW5")
if not w5 or not w5.get("pairs"):
    sys.exit("ERROR: pins.json has no ppW5 block — this epoch was not run as a PP-W5 parity "
             "epoch (--kind pp-w5-parity). Refusing to guess the registered pair set: an "
             "adjudicator that infers its own population is not adjudicating a frozen gate.")
PAIRS = list(w5["pairs"])
CONTROL_ROOT = pins["armA"].get("repoRoot") or ""
TREATMENT_ROOT = pins["armB"].get("repoRoot") or ""

# The product contrast must be provable FROM THE RECORD, not assumed from the flags.
if not CONTROL_ROOT or not TREATMENT_ROOT or CONTROL_ROOT == TREATMENT_ROOT:
    sys.exit("ERROR: pins.json does not record two distinct per-arm repoRoots. `--calor-dll` "
             "pins the CLI, which the agent never invokes; the compiler that builds the "
             "agent's code comes from the arm template's __REPO_ROOT__. Without two roots "
             "both arms share that compiler and the epoch measures nothing — this is what "
             "voided w5-parity-001. Refusing to adjudicate.")
if pins["armA"].get("calorTasksSha") and \
        pins["armA"]["calorTasksSha"] == pins["armB"].get("calorTasksSha"):
    sys.exit("ERROR: both arms' Calor.Tasks.dll hash to the same value — the agent-visible "
             "compiler is identical on both arms. Epoch void.")

# runs[pairId][role] = [{tokensOut, invalid, censored}]
runs = {}
unattributed = 0
for f in sorted(glob.glob(os.path.join(EPOCH, "*", "*", "run-*", "result.json"))):
    r = json.load(open(f))
    pid = "-".join(r["pair"].split("-")[:2])
    # Key on the PRODUCT root the run actually built against.
    root = r.get("armRepoRoot") or ""
    if root == CONTROL_ROOT:
        role = "control"
    elif root == TREATMENT_ROOT:
        role = "treatment"
    else:
        unattributed += 1
        continue
    runs.setdefault(pid, {}).setdefault(role, []).append({
        "tokensOut": (r.get("tokens", {}) or {}).get("output", 0),
        "invalid": bool(r.get("invalid", False)),
        "censored": bool(r.get("censored", False)) or bool(r.get("invalid", False)),
        "itg": r.get("iterationsToGreen"),
    })

if unattributed:
    sys.exit(f"ERROR: {unattributed} run(s) carry no armRepoRoot matching either pinned arm. "
             "A run whose compiler provenance cannot be established cannot be attributed to "
             "an arm, and silently dropping it would bias whichever arm kept its runs.")


def vals(pid, role):
    """Output tokens over the pair's NON-INVALID runs. Never-green-but-valid runs
    count, per the frozen M-L5 definition."""
    return [x["tokensOut"] for x in runs.get(pid, {}).get(role, [])
            if not x["invalid"] and x["tokensOut"] is not None]


def censored_frac(pid_list, role):
    """§2 of the gates doc: a gate is INVALID if either arm exceeds 40% censored on
    neutral tasks. The frozen PP-W5 row says '§2 censoring caps apply'; an earlier
    revision of this adjudicator read only `invalid` and silently folded a censored
    run into the token means."""
    tot = sum(len(runs.get(p, {}).get(role, [])) for p in pid_list)
    cen = sum(1 for p in pid_list for x in runs.get(p, {}).get(role, []) if x["censored"])
    return (cen / tot) if tot else 0.0


dropped = []
surviving = []
for pid in PAIRS:
    c, t = vals(pid, "control"), vals(pid, "treatment")
    if len(c) < MIN_RUNS_PER_CELL or len(t) < MIN_RUNS_PER_CELL:
        dropped.append((pid, len(c), len(t)))
    elif statistics.mean(c) > 0:
        surviving.append(pid)
    else:
        dropped.append((pid, len(c), len(t)))

total_control = sum(len(vals(p, "control")) for p in PAIRS)
total_treatment = sum(len(vals(p, "treatment")) for p in PAIRS)


def point_ratio(pid):
    return statistics.mean(vals(pid, "treatment")) / statistics.mean(vals(pid, "control"))


def two_level_bootstrap(pids):
    """Cluster bootstrap over pairs, runs resampled WITHIN each resampled pair
    (gates §6.1) — the same method PP-L5/PP-L6 use, transcribed not re-derived."""
    if len(pids) < 2:
        return []
    c_runs = {p: vals(p, "control") for p in pids}
    t_runs = {p: vals(p, "treatment") for p in pids}
    boots = []
    n = len(pids)
    for _ in range(BOOT):
        rs = []
        for _ in range(n):
            p = pids[random.randrange(n)]
            cr, tr = c_runs[p], t_runs[p]
            cm = statistics.mean(random.choice(cr) for _ in cr)
            tm = statistics.mean(random.choice(tr) for _ in tr)
            if cm > 0:
                rs.append(tm / cm)
        if rs:
            boots.append(statistics.median(rs))
    boots.sort()
    return boots


def one_sided_lower(boots):
    """5th percentile — the one-sided 95% LOWER bound. PP-W5's direction."""
    return boots[int(0.05 * len(boots))] if boots else None


out = {"epoch": os.path.basename(os.path.abspath(EPOCH)), "gate": "PP-W5"}
print(f"=== PP-W5 — toolchain parity (control = {pins['armA']['commit'][:8]}, "
      f"treatment = {pins['armB']['commit'][:8]}) ===")
print(f"pairs registered: {len(PAIRS)}  surviving: {len(surviving)}  dropped: {len(dropped)}")
for pid, nc, nt in dropped:
    print(f"  DROPPED {pid}: control {nc} valid, treatment {nt} valid (need >= {MIN_RUNS_PER_CELL} each)")
print(f"valid runs — control {total_control}, treatment {total_treatment} (need >= {MIN_VALID_PER_ARM} each)")

blockers = []
if len(surviving) < MIN_PAIRS:
    blockers.append(f"only {len(surviving)} pairs survived (need >= {MIN_PAIRS})")
if total_control < MIN_VALID_PER_ARM:
    blockers.append(f"control has {total_control} valid runs (need >= {MIN_VALID_PER_ARM})")
if total_treatment < MIN_VALID_PER_ARM:
    blockers.append(f"treatment has {total_treatment} valid runs (need >= {MIN_VALID_PER_ARM})")

# §2 censoring caps, which the frozen row invokes and an earlier revision omitted.
cen_c, cen_t = censored_frac(PAIRS, "control"), censored_frac(PAIRS, "treatment")
print(f"censored — control {cen_c:.0%}, treatment {cen_t:.0%} (gate invalid above {CENSOR_CAP:.0%})")
if cen_c > CENSOR_CAP:
    blockers.append(f"control censored fraction {cen_c:.0%} exceeds the §2 cap of {CENSOR_CAP:.0%}")
if cen_t > CENSOR_CAP:
    blockers.append(f"treatment censored fraction {cen_t:.0%} exceeds the §2 cap of {CENSOR_CAP:.0%}")

for pid in surviving:
    print(f"  {pid}: control mean {statistics.mean(vals(pid,'control')):.0f} tok "
          f"({len(vals(pid,'control'))} runs), treatment mean {statistics.mean(vals(pid,'treatment')):.0f} tok "
          f"({len(vals(pid,'treatment'))} runs), ratio {point_ratio(pid):.3f}")

# Iterations-to-green: the frozen row REQUIRES it be recorded (observational,
# floor-bound). w5-parity-001's record omitted it; a mandated quantity left out of the
# record is a lapse even when it changes no verdict.
def itg_summary(role):
    v = [x["itg"] for p in PAIRS for x in runs.get(p, {}).get(role, [])
         if not x["invalid"] and x["itg"] is not None]
    return v
for role in ("control", "treatment"):
    v = itg_summary(role)
    if v:
        from collections import Counter
        print(f"iterations-to-green ({role}, observational): {dict(sorted(Counter(v).items()))}")
        out.setdefault("iterationsToGreen", {})[role] = dict(sorted(Counter(v).items()))

if blockers:
    out["verdict"] = "REPORTED-NOT-ADJUDICATED"
    out["blockers"] = blockers
    print("\nVERDICT: **REPORTED, NOT ADJUDICATED** — the validity floor is not met:")
    for b in blockers:
        print(f"  - {b}")
    print("This is the frozen on-degenerate branch, not a pass and not a fail.")
else:
    point = statistics.median([point_ratio(p) for p in surviving])
    boots = two_level_bootstrap(surviving)
    lower = one_sided_lower(boots)
    fails = (lower is not None and lower > LOWER_BOUND_GATE) and (point > POINT_GATE)
    out.update({"pairs": surviving, "pointEstimate": point, "lowerBound95": lower,
                "verdict": "FAIL" if fails else "PASS"})
    print(f"\nmedian paired ratio (treatment/control): point {point:.4f}, "
          f"one-sided 95% lower bound {lower:.4f}")
    print(f"gate (a) lower > {LOWER_BOUND_GATE}: {'YES' if lower > LOWER_BOUND_GATE else 'no'}")
    print(f"gate (b) point > {POINT_GATE}: {'YES' if point > POINT_GATE else 'no'}")
    print(f"\nVERDICT: **{'FAIL' if fails else 'PASS'}** (fails only if BOTH gates fire)")
    if not fails:
        print("A pass means NO LARGE TAX DETECTED. At 4 clusters x 5 runs the measured")
        print("detection is 0.33 / 0.62 / 0.87 at true 1.25x / 1.4x / 1.6x regressions —")
        print("this is not evidence of equality.")

json.dump(out, open(os.path.join(EPOCH, "w5-analysis.json"), "w"), indent=2)
print(f"\nwrote {os.path.join(EPOCH, 'w5-analysis.json')}")
