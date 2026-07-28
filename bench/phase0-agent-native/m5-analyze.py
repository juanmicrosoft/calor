#!/usr/bin/env python3
"""M5 comparison-epoch adjudicator (loop plan v0.9 / loop-m5-comparison.md).

Reads an m5-comparison epoch dir (produced by run-m5-epoch.sh) and computes:
  - PP-L5: median paired tokens-to-green ratio (armB/armA) on the warm set,
           adjudicated by the gates §6.1 decision rule.
  - PP-L6(b): neutral-set iterations-to-green parity (no significant regression),
              same bootstrap, plus the censored caps.

Frozen definitions (agent-native-gates.md), implemented here — not changed:
  - M-L5 tokens-to-green = per-run agent output tokens; per-pair means, then the
    median of paired per-pair ratios (§2).
  - §6.1 decision rule: cluster bootstrap over pairs, RUNS NESTED WITHIN PAIRS,
    one-sided, alpha=0.05. Pass iff the point estimate meets the threshold AND
    the one-sided 95% CI excludes zero effect (ratio = 1.0). No third outcome.
  - Censored: 40% absolute cap on neutral tasks (either arm) invalidates; a 5pp
    differential (loop plan §4/m6) makes PP-L5 reported-not-adjudicated.

A run is censored if invalid or never-green (iterationsToGreen > budget). The
two-level bootstrap is seeded for reproducibility.
"""
import json, sys, glob, os, random, statistics

EPOCH = sys.argv[1] if len(sys.argv) > 1 else "."
ARM_A, ARM_B = "calor", "calor+mcp-file"   # run-pair ARM_LABELs (raw vs mcp-file)
BOOT = 2000
ALPHA = 0.05
random.seed(4537)

pins = json.load(open(os.path.join(EPOCH, "pins.json")))
WARM = list(pins.get("ppL5", {}).get("pairs", []))
NEUTRAL = list(pins.get("ppL6", {}).get("pairs", []))
THRESH = pins.get("ppL5", {}).get("threshold", 0.85)

# runs[pairId][arm] = list of {tokensOut, itg, invalid, censored}
runs = {}
for f in sorted(glob.glob(os.path.join(EPOCH, "*", "*", "run-*", "result.json"))):
    r = json.load(open(f))
    pid = "-".join(r["pair"].split("-")[:2])
    runs.setdefault(pid, {}).setdefault(r["arm"], []).append({
        "tokensOut": (r.get("tokens", {}) or {}).get("output", 0),
        "itg": r.get("iterationsToGreen"),
        "invalid": bool(r.get("invalid", False)),
        "censored": bool(r.get("censored", False)) or bool(r.get("invalid", False)),
    })

def vals(pid, arm, field):
    """field values over the pair's NON-INVALID runs for an arm (M-L5 uses
    per-run tokens; never-green-but-valid runs count, per the frozen def)."""
    return [x[field] for x in runs.get(pid, {}).get(arm, [])
            if not x["invalid"] and x[field] is not None]

def paired_pids(pids, field):
    return [p for p in pids if vals(p, ARM_A, field) and vals(p, ARM_B, field)
            and statistics.mean(vals(p, ARM_A, field)) > 0]

def point_ratio(pid, field):
    a = statistics.mean(vals(pid, ARM_A, field)); b = statistics.mean(vals(pid, ARM_B, field))
    return b / a

def point_median(pids, field):
    pp = paired_pids(pids, field)
    if not pp:
        return (None, 0)
    return (statistics.median([point_ratio(p, field) for p in pp]), len(pp))

def two_level_bootstrap(pids, field):
    """Cluster bootstrap over pairs with runs resampled WITHIN each resampled
    pair (gates §6.1). Returns the sorted list of bootstrap median-ratios."""
    pp = paired_pids(pids, field)
    if len(pp) < 2:
        return []
    a_runs = {p: vals(p, ARM_A, field) for p in pp}
    b_runs = {p: vals(p, ARM_B, field) for p in pp}
    boots = []
    n = len(pp)
    for _ in range(BOOT):
        rs = []
        for _ in range(n):
            p = pp[random.randrange(n)]
            ar, br = a_runs[p], b_runs[p]
            am = statistics.mean(random.choice(ar) for _ in ar)
            bm = statistics.mean(random.choice(br) for _ in br)
            if am > 0:
                rs.append(bm / am)
        if rs:
            boots.append(statistics.median(rs))
    boots.sort()
    return boots

def one_sided_upper(boots):   # 95th percentile — one-sided 95% upper bound
    return boots[min(len(boots) - 1, int(0.95 * len(boots)))] if boots else None

def one_sided_lower(boots):   # 5th percentile — one-sided 95% lower bound
    return boots[int(0.05 * len(boots))] if boots else None

def p_ge(boots, x):
    return (sum(1 for m in boots if m >= x) / len(boots)) if boots else None

def censored_frac(pids, arm):
    tot = cen = 0
    for pid in pids:
        for x in runs.get(pid, {}).get(arm, []):
            tot += 1; cen += 1 if x["censored"] else 0
    return ((cen / tot) if tot else 0.0, cen, tot)

# ---- PP-L5 (warm set, tokens-to-green): reduction gate, threshold 0.85 ----
pt, n_warm = point_median(WARM, "tokensOut")
boots5 = two_level_bootstrap(WARM, "tokensOut")
upper5 = one_sided_upper(boots5)                 # one-sided 95% upper bound
p_zero5 = p_ge(boots5, 1.0)                       # P(median >= 1.0): excludes zero effect iff < alpha
a_cf5 = censored_frac(WARM, ARM_A); b_cf5 = censored_frac(WARM, ARM_B)
diff5_ok = abs(a_cf5[0] - b_cf5[0]) <= 0.05
decidable5 = (n_warm >= 2) and diff5_ok
meets_thresh = (pt is not None and pt <= THRESH)
excludes_zero5 = (p_zero5 is not None and p_zero5 < ALPHA)   # one-sided 95% CI excludes 1.0
ppl5_hit = (meets_thresh and excludes_zero5) if decidable5 else None

# ---- PP-L6(b) (neutral set, itg parity): no-significant-regression + caps ----
npt, n_neut = point_median(NEUTRAL, "itg")
boots6 = two_level_bootstrap(NEUTRAL, "itg")
lower6 = one_sided_lower(boots6)
# regression = armB significantly WORSE (itg ratio significantly > 1.0)
p_reg = (sum(1 for m in boots6 if m <= 1.0) / len(boots6)) if boots6 else None
sig_regression = (p_reg is not None and p_reg < ALPHA)  # one-sided 95% lower bound > 1.0
na_cf = censored_frac(NEUTRAL, ARM_A); nb_cf = censored_frac(NEUTRAL, ARM_B)
cap40_ok = na_cf[0] <= 0.40 and nb_cf[0] <= 0.40
diff6_ok = abs(na_cf[0] - nb_cf[0]) <= 0.05
ppl6_pass = (cap40_ok and diff6_ok and not sig_regression) if (n_neut >= 2 and boots6) else None

analysis = {
  "epochId": pins.get("epochId"), "kind": "m5-comparison",
  "armA": pins.get("armA"), "armB": pins.get("armB"), "boot": BOOT, "alpha": ALPHA,
  "ppL5": {
     "metric": "tokens-to-green", "threshold": THRESH,
     "medianPairedRatio_BoverA": pt, "nPairs": n_warm,
     "oneSided95UpperBound": upper5, "pMedian_ge_1.0": p_zero5,
     "meetsThreshold": meets_thresh, "excludesZeroEffect": excludes_zero5,
     "decidable": decidable5, "hit": ppl5_hit,
     "perPair": [{"pair": p, "ratio": round(point_ratio(p, "tokensOut"), 4)}
                 for p in paired_pids(WARM, "tokensOut")],
     "censoredFrac": {"armA": round(a_cf5[0], 3), "armB": round(b_cf5[0], 3), "diffWithin5pp": diff5_ok},
     "rule": "gates §6.1: hit iff point<=0.85 AND one-sided 95% CI excludes ratio 1.0; !decidable if <2 warm pairs or censored-diff>5pp",
  },
  "ppL6": {
     "metric": "neutral iterations-to-green parity (no significant regression)",
     "medianPairedRatio_BoverA": npt, "nPairs": n_neut,
     "oneSided95LowerBound": lower6, "pMedian_le_1.0": p_reg,
     "significantRegression": sig_regression, "pass": ppl6_pass,
     "censoredFrac": {"armA": round(na_cf[0], 3), "armB": round(nb_cf[0], 3),
                      "cap40_ok": cap40_ok, "diffWithin5pp": diff6_ok},
     "note": "release blocker; config invariance also enforced per-run by run-pair.sh check_pins",
  },
}
json.dump(analysis, open(os.path.join(EPOCH, "analysis.json"), "w"), indent=2)

def f(x): return "n/a" if x is None else (f"{x:.4f}" if isinstance(x, float) else str(x))
print(f"=== M5 {analysis['epochId']} — PP-L5 (tokens-to-green, gates §6.1) ===")
print(f"  warm pairs with data: {n_warm}   median paired ratio B/A: {f(pt)}  (threshold <= {THRESH})")
print(f"  one-sided 95% upper bound: {f(upper5)}   P(median>=1.0)={f(p_zero5)}")
print(f"  meetsThreshold={meets_thresh}  excludesZeroEffect(1.0)={excludes_zero5}  decidable={decidable5}  ==> HIT={ppl5_hit}")
print(f"  censored: armA {a_cf5[0]*100:.0f}%  armB {b_cf5[0]*100:.0f}%  (diff<=5pp: {diff5_ok})")
for p in paired_pids(WARM, "tokensOut"):
    print(f"    {p}: B/A={point_ratio(p,'tokensOut'):.3f}")
print(f"=== PP-L6 (neutral parity) ===")
print(f"  neutral pairs: {n_neut}   itg median ratio B/A: {f(npt)}  one-sided 95% lower: {f(lower6)}")
print(f"  significantRegression={sig_regression}  censored armA {na_cf[0]*100:.0f}% armB {nb_cf[0]*100:.0f}%  (40% cap: {cap40_ok}, diff<=5pp: {diff6_ok})  ==> PASS={ppl6_pass}")
print("wrote analysis.json")
