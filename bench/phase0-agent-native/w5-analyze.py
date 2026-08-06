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

# Both arms are the CALOR arm; they differ only in the pinned compiler build. Arms are
# separated by the BUILD each run used (#813 stamps it per run), NOT by the arm label —
# the label only has to keep their output directories apart. Keying on the build means a
# mislabelled or reordered collection cannot silently swap the arms.
BOOT = 2000
MIN_RUNS_PER_CELL = 2      # below this, the PAIR drops
MIN_PAIRS = 3              # below this, reported-not-adjudicated
MIN_VALID_PER_ARM = 12     # below this, reported-not-adjudicated
LOWER_BOUND_GATE = 1.0     # (a)
POINT_GATE = 1.25          # (b)
random.seed(4537)          # same seed convention as m5-analyze

pins = json.load(open(os.path.join(EPOCH, "pins.json")))
w5 = pins.get("ppW5")
if not w5 or not w5.get("pairs"):
    sys.exit("ERROR: pins.json has no ppW5 block — this epoch was not run as a PP-W5 parity "
             "epoch (--kind pp-w5-parity). Refusing to guess the registered pair set: an "
             "adjudicator that infers its own population is not adjudicating a frozen gate.")
PAIRS = list(w5["pairs"])
CONTROL_DLL = pins["armA"]["calorDll"]
TREATMENT_DLL = pins["armB"]["calorDll"]

# runs[pairId][role] = [{tokensOut, invalid}]
runs = {}
for f in sorted(glob.glob(os.path.join(EPOCH, "*", "*", "run-*", "result.json"))):
    r = json.load(open(f))
    pid = "-".join(r["pair"].split("-")[:2])
    dll = r.get("calorDll") or ""
    # Arms are separated by the BUILD that produced the run, which #813 stamps per run.
    # Matching on the pinned path rather than on ordering means a mis-ordered or partial
    # collection cannot silently swap the arms.
    if dll == CONTROL_DLL:
        role = "control"
    elif dll == TREATMENT_DLL:
        role = "treatment"
    else:
        continue
    runs.setdefault(pid, {}).setdefault(role, []).append({
        "tokensOut": (r.get("tokens", {}) or {}).get("output", 0),
        "invalid": bool(r.get("invalid", False)),
    })


def vals(pid, role):
    """Output tokens over the pair's NON-INVALID runs. Never-green-but-valid runs
    count, per the frozen M-L5 definition."""
    return [x["tokensOut"] for x in runs.get(pid, {}).get(role, [])
            if not x["invalid"] and x["tokensOut"] is not None]


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

for pid in surviving:
    print(f"  {pid}: control mean {statistics.mean(vals(pid,'control')):.0f} tok "
          f"({len(vals(pid,'control'))} runs), treatment mean {statistics.mean(vals(pid,'treatment')):.0f} tok "
          f"({len(vals(pid,'treatment'))} runs), ratio {point_ratio(pid):.3f}")

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
