#!/usr/bin/env python3
"""M5 comparison-epoch adjudicator (loop plan v0.9 / loop-m5-comparison.md).

Reads an m5-comparison epoch dir (produced by run-m5-epoch.sh) and computes:
  - PP-L5: median paired tokens-to-green ratio (armB/armA) on the warm set,
           one-sided cluster bootstrap vs the 0.85 threshold (gates Annex A / §6.1).
  - PP-L6(b): neutral-set iterations-to-green parity (armB/armA), same bootstrap.
  - Censored caps: 40% absolute (neutral) and 5pp differential (loop plan §4/m6).
Writes analysis.json and prints a report. PP adjudication semantics are frozen
in agent-native-gates.md — this only computes them.

M-L5 tokens-to-green = per-run agent output tokens; per-pair means, then the
median of paired per-pair ratios (gates §2). A run is censored if invalid or
never-green (iterationsToGreen > budget). Bootstrap is seeded for reproducibility.
"""
import json, sys, glob, os, random, statistics

EPOCH = sys.argv[1] if len(sys.argv) > 1 else "."
ARM_A, ARM_B = "calor", "calor+mcp-file"   # run-pair ARM_LABELs (raw vs mcp-file)
BOOT = 2000
random.seed(4537)

pins = json.load(open(os.path.join(EPOCH, "pins.json")))
WARM = set(pins.get("ppL5", {}).get("pairs", []))
NEUTRAL = set(pins.get("ppL6", {}).get("pairs", []))
THRESH = pins.get("ppL5", {}).get("threshold", 0.85)

# runs[pairId][arm] = list of {tokensOut, itg, censored}
runs = {}
budget = None
for f in sorted(glob.glob(os.path.join(EPOCH, "*", "*", "run-*", "result.json"))):
    r = json.load(open(f))
    pid = "-".join(r["pair"].split("-")[:2])
    arm = r["arm"]
    runs.setdefault(pid, {}).setdefault(arm, []).append({
        "tokensOut": r.get("tokens", {}).get("output", 0),
        "itg": r.get("iterationsToGreen"),
        "invalid": r.get("invalid", False),
        # never-green: itg beyond the iteration budget (budget+1 sentinel) or invalid
        "censored": bool(r.get("censored", False)) or bool(r.get("invalid", False)),
    })

def arm_pair_mean(pid, arm, field):
    """Mean of `field` over the pair's non-invalid runs for an arm (None if none)."""
    xs = [x[field] for x in runs.get(pid, {}).get(arm, []) if not x["invalid"] and x[field] is not None]
    return statistics.mean(xs) if xs else None

def censored_frac(pids, arm):
    tot = cen = 0
    for pid in pids:
        for x in runs.get(pid, {}).get(arm, []):
            tot += 1; cen += 1 if x["censored"] else 0
    return (cen / tot) if tot else 0.0, cen, tot

def paired_ratios(pids, field):
    """List of (pid, armB_mean/armA_mean) over pairs where both arms have data."""
    out = []
    for pid in sorted(pids):
        a = arm_pair_mean(pid, ARM_A, field); b = arm_pair_mean(pid, ARM_B, field)
        if a and b and a > 0:
            out.append((pid, b / a))
    return out

def median(xs): return statistics.median(xs) if xs else None

def cluster_bootstrap_median(ratios, one_sided_thresh):
    """One-sided cluster bootstrap over pairs (gates §6.1). Returns
    (point_median, ci_lo, ci_hi, p_ge_thresh) — p = P(bootstrap median >= thresh),
    i.e. one-sided support for 'median < thresh'. Significant if p < 0.05."""
    vals = [r for _, r in ratios]
    if len(vals) < 2:
        return (median(vals), None, None, None)
    boots = []
    n = len(vals)
    for _ in range(BOOT):
        sample = [vals[random.randrange(n)] for _ in range(n)]
        boots.append(statistics.median(sample))
    boots.sort()
    lo = boots[int(0.025 * BOOT)]; hi = boots[int(0.975 * BOOT)]
    p_ge = sum(1 for m in boots if m >= one_sided_thresh) / BOOT
    return (statistics.median(vals), lo, hi, p_ge)

# ---- PP-L5 (warm set, tokens-to-green) ----
warm_ratios = paired_ratios(WARM, "tokensOut")
pt, lo, hi, p = cluster_bootstrap_median(warm_ratios, THRESH)
a_cf = censored_frac(WARM, ARM_A); b_cf = censored_frac(WARM, ARM_B)
diff_cap_ok = abs(a_cf[0] - b_cf[0]) <= 0.05
neutral_cap_ok = True  # 40% cap applies to NEUTRAL tasks (checked in PP-L6)
ppl5_decidable = len(warm_ratios) >= 2 and diff_cap_ok
ppl5_hit = (p is not None and p < 0.05) if ppl5_decidable else None

# ---- PP-L6 (neutral set, iterations-to-green parity + censored caps) ----
neut_ratios = paired_ratios(NEUTRAL, "itg")
npt, nlo, nhi, npp = cluster_bootstrap_median(neut_ratios, 1.0)
na_cf = censored_frac(NEUTRAL, ARM_A); nb_cf = censored_frac(NEUTRAL, ARM_B)
neutral_40_ok = na_cf[0] <= 0.40 and nb_cf[0] <= 0.40
neutral_diff_ok = abs(na_cf[0] - nb_cf[0]) <= 0.05

analysis = {
  "epochId": pins.get("epochId"), "kind": "m5-comparison",
  "armA": pins.get("armA"), "armB": pins.get("armB"),
  "ppL5": {
     "metric": "tokens-to-green", "threshold": THRESH,
     "medianPairedRatio_BoverA": pt, "ci95": [lo, hi], "pBootstrap_ge_thresh": p,
     "decidable": ppl5_decidable, "hit": ppl5_hit,
     "nPairs": len(warm_ratios),
     "perPair": [{"pair": pid, "ratio": round(r, 4)} for pid, r in warm_ratios],
     "censoredFrac": {"armA": round(a_cf[0], 3), "armB": round(b_cf[0], 3), "diffWithin5pp": diff_cap_ok},
     "note": "hit = one-sided cluster bootstrap rejects median>=0.85 at a=0.05; if !decidable or diff>5pp, reported-not-adjudicated (gates)",
  },
  "ppL6": {
     "neutralItgMedianRatio_BoverA": npt, "ci95": [nlo, nhi],
     "censoredFrac": {"armA": round(na_cf[0], 3), "armB": round(nb_cf[0], 3),
                      "cap40_ok": neutral_40_ok, "diffWithin5pp": neutral_diff_ok},
     "nPairs": len(neut_ratios),
     "note": "release blocker: no significant neutral regression + config invariance (config invariance enforced per-run by check_pins)",
  },
}
json.dump(analysis, open(os.path.join(EPOCH, "analysis.json"), "w"), indent=2)

def fmt(x): return "n/a" if x is None else (f"{x:.4f}" if isinstance(x, float) else str(x))
print(f"=== M5 {analysis['epochId']} — PP-L5 (tokens-to-green) ===")
print(f"  warm pairs with data: {len(warm_ratios)}   median paired ratio B/A: {fmt(pt)}  (threshold <= {THRESH})")
print(f"  95% CI [{fmt(lo)}, {fmt(hi)}]   p(median>=thresh)={fmt(p)}   decidable={ppl5_decidable}  HIT={ppl5_hit}")
print(f"  censored: armA {a_cf[0]*100:.0f}%  armB {b_cf[0]*100:.0f}%  (diff<=5pp: {diff_cap_ok})")
for pid, r in warm_ratios: print(f"    {pid}: B/A={r:.3f}")
print(f"=== PP-L6 (neutral parity) ===")
print(f"  neutral pairs: {len(neut_ratios)}   itg median ratio B/A: {fmt(npt)}  CI[{fmt(nlo)},{fmt(nhi)}]")
print(f"  censored: armA {na_cf[0]*100:.0f}%  armB {nb_cf[0]*100:.0f}%  (40% cap: {neutral_40_ok}, diff<=5pp: {neutral_diff_ok})")
print("wrote analysis.json")
