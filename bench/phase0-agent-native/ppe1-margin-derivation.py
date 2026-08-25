#!/usr/bin/env python3
"""PP-E1 cost-leg margin derivation (annex A-1.11, §A.2 row's Basis column).

WRITTEN AT REGISTRATION, results-blind: no effect-row implementation exists, so
no leg-B epoch has run. This script derives the FROZEN margin from archived
epochs only, by the PP-W5 method (A-1.4 tranche 1):

    existing-epoch within-cell variance
      -> null simulation (no arm effect) -> point-estimate p95
      -> margin = that p95 rounded UP to the 0.05 grid
      -> conjoined with the one-sided 95% cluster-bootstrap lower bound > 1.0

Two populations are reported:

  m5-compare-001  N1 cells (arms `calor`, `calor+mcp-file`) -- PP-W5's OWN
                  derivation population. Re-run here on the NAIVE token field
                  that row named, as a method-reproduction check: it must
                  return the registered null p95 of 1.247 within Monte-Carlo
                  noise, or this script is not the method it claims to be.

  w5-parity-002   N1 cells (arms `calor+control`, `calor+treatment`) -- the
                  realized parity epoch: same four N1 pairs, 5 runs/arm, both
                  arms Calor, the same two-toolchain shape leg B will run.
                  This is PP-E1's derivation population, read with the
                  CORRECTED token figure (#881 / annex A-1.9.1) via the shared
                  bench/phase0-agent-native/token-usage.py.

Simulation shape is PP-W5's, transcribed: 300 null simulations x 400-resample
two-level cluster bootstrap (pairs resampled, runs resampled within pair),
seed 4537 -- the seed convention of m5-analyze.py / w5-analyze.py.

Usage (from the repository root):

    python3 bench/phase0-agent-native/ppe1-margin-derivation.py \
        > bench/phase0-agent-native/ppe1-margin-derivation.txt

Python 3.9 compatible; standard library only.
"""
import glob
import json
import os
import random
import statistics
import subprocess
import sys

BENCH = os.path.dirname(os.path.abspath(__file__))
TOKEN_USAGE = os.path.join(BENCH, "token-usage.py")

SIMS = int(os.environ.get("PPE1_SIMS", "300"))
BOOT = int(os.environ.get("PPE1_BOOT", "400"))
SEED = 4537
MARGINS = (1.25, 1.30, 1.35, 1.40, 1.45, 1.50)
EFFECTS = (1.0, 1.25, 1.4, 1.6)


def corrected_tokens(agent_json):
    """The one blessed derivation of the cost-leg token figure (A-1.9.1)."""
    proc = subprocess.run([sys.executable, TOKEN_USAGE, agent_json],
                          capture_output=True, text=True)
    return json.loads(proc.stdout)


def collect(epoch, arm_map):
    """cells[pair][arm] = [{naive, corrected}]; invalid runs are excluded."""
    cells = {}
    pattern = os.path.join(BENCH, "epochs", epoch, "N1-*", "*", "run-*", "result.json")
    for path in sorted(glob.glob(pattern)):
        record = json.load(open(path))
        arm_dir = os.path.basename(os.path.dirname(os.path.dirname(path)))
        if arm_dir not in arm_map or record.get("invalid"):
            continue
        pair = "-".join(record["pair"].split("-")[:2])
        naive = (record.get("tokens", {}) or {}).get("output", 0)
        agent = os.path.join(os.path.dirname(path), "agent.json")
        usage = corrected_tokens(agent) if os.path.exists(agent) else {}
        cells.setdefault(pair, {}).setdefault(arm_map[arm_dir], []).append(
            {"naive": naive, "corrected": usage.get("output_tokens_corrected") or naive})
    return cells


def cv(values):
    """Population-sd convention, as in the PP-W5 derivation."""
    mean = statistics.mean(values)
    return None if mean == 0 else statistics.pstdev(values) / mean


def median_ratio(pairs, control, treatment):
    ratios = [statistics.mean(treatment[p]) / statistics.mean(control[p])
              for p in pairs if statistics.mean(control[p]) > 0]
    return statistics.median(ratios)


def bootstrap_lower(pairs, control, treatment):
    """Two-level cluster bootstrap (gates §6.1); 5th percentile = one-sided 95% lower."""
    boots = []
    for _ in range(BOOT):
        ratios = []
        for _ in range(len(pairs)):
            pair = pairs[random.randrange(len(pairs))]
            c = statistics.mean(random.choice(control[pair]) for _ in control[pair])
            t = statistics.mean(random.choice(treatment[pair]) for _ in treatment[pair])
            if c > 0:
                ratios.append(t / c)
        if ratios:
            boots.append(statistics.median(ratios))
    boots.sort()
    return boots[int(0.05 * len(boots))] if boots else None


def simulate(cells, key):
    """Null (and scaled-treatment) simulation: both arms drawn from the pair's pooled runs."""
    pairs = sorted(cells)
    pooled = {p: [r[key] for arm in cells[p] for r in cells[p][arm]] for p in pairs}
    sizes = {p: [len(cells[p][a]) for a in sorted(cells[p])] for p in pairs}
    results = {}
    for effect in EFFECTS:
        points = []
        conjunction = {m: 0 for m in MARGINS}
        point_only = {m: 0 for m in MARGINS}
        for _ in range(SIMS):
            control, treatment = {}, {}
            for pair in pairs:
                n_c, n_t = sizes[pair][0], sizes[pair][-1]
                control[pair] = [random.choice(pooled[pair]) for _ in range(n_c)]
                treatment[pair] = [random.choice(pooled[pair]) * effect for _ in range(n_t)]
            point = median_ratio(pairs, control, treatment)
            points.append(point)
            lower = bootstrap_lower(pairs, control, treatment)
            for margin in MARGINS:
                if point > margin:
                    point_only[margin] += 1
                    if lower is not None and lower > 1.0:
                        conjunction[margin] += 1
        points.sort()
        results[effect] = {
            "p50": points[len(points) // 2],
            "p95": points[int(0.95 * len(points))],
            "conjunction": {m: conjunction[m] / SIMS for m in MARGINS},
            "point_only": {m: point_only[m] / SIMS for m in MARGINS},
        }
    return results


def grid_round_up(value, step=0.05):
    """The frozen margin rule: round the null p95 UP to the 0.05 grid.
    PP-W5's 1.247 -> 1.25; PP-E1's 1.321 -> 1.35."""
    import math
    return round(math.ceil(value / step - 1e-9) * step, 2)


def report(title, cells, keys):
    print(f"\n=== {title} ===")
    for pair in sorted(cells):
        for arm in sorted(cells[pair]):
            naive = [r["naive"] for r in cells[pair][arm]]
            corrected = [r["corrected"] for r in cells[pair][arm]]
            print(f"  {pair:8s} {arm:10s} n={len(corrected)} "
                  f"mean_corrected={statistics.mean(corrected):8.0f} cv_corrected={cv(corrected):.4f} "
                  f"mean_naive={statistics.mean(naive):8.0f} cv_naive={cv(naive):.4f}")
    for key in keys:
        values = [cv([r[key] for r in cells[p][a]]) for p in sorted(cells) for a in sorted(cells[p])]
        values = [v for v in values if v is not None]
        print(f"  within-cell CV ({key}): median {statistics.median(values):.4f}  "
              f"max {max(values):.4f}  cells {len(values)}")
        random.seed(SEED)
        results = simulate(cells, key)
        null = results[1.0]
        print(f"  [{key}] null point: median {null['p50']:.4f}  p95 {null['p95']:.4f}  "
              f"-> margin on the 0.05 grid: {grid_round_up(null['p95']):.2f}")
        for margin in MARGINS:
            print(f"      margin {margin:.2f}: null false-fail — point-only "
                  f"{null['point_only'][margin]:.3f}, conjunction {null['conjunction'][margin]:.3f}")
        for effect in (1.25, 1.4, 1.6):
            row = results[effect]
            print(f"      power @ {effect}x (conjunction): " + "  ".join(
                f"m={m:.2f} {row['conjunction'][m]:.3f}" for m in MARGINS))


if __name__ == "__main__":
    print(f"PP-E1 margin derivation — {SIMS} null simulations x {BOOT}-resample "
          f"two-level cluster bootstrap, seed {SEED}")
    report("m5-compare-001 — PP-W5's derivation population (method reproduction)",
           collect("m5-compare-001", {"calor": "A", "calor+mcp-file": "B"}),
           ("naive", "corrected"))
    report("w5-parity-002 — PP-E1's derivation population (realized parity epoch)",
           collect("w5-parity-002", {"calor+control": "control", "calor+treatment": "treatment"}),
           ("naive", "corrected"))
