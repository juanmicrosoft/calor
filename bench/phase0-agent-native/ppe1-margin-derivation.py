#!/usr/bin/env python3
"""PP-E1 cost-leg margin derivation (annex A-1.11, §A.2 row's Basis column),
extended for PP-W-rows leg B (roadmap v0.16 §2.3(b) / §4.1 "Margin").

WRITTEN AT REGISTRATION, results-blind: no effect-row implementation exists, so
no leg-B epoch has run. This script derives the FROZEN margin from archived
epochs only, by the PP-W5 method (A-1.4 tranche 1):

    existing-epoch within-cell variance
      -> null simulation (no arm effect) -> point-estimate p95
      -> margin = that p95 rounded UP to the 0.05 grid
      -> conjoined with the one-sided 95% cluster-bootstrap lower bound > 1.0

Two populations are reported by default (A-1.11, frozen — the DEFAULT
invocation reproduces the committed ppe1-margin-derivation.txt byte for byte):

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
seed 4537 -- the seed convention of m5-analyze.py / w5-analyze.py. The null
redraw convention, named for A-1.12: RESAMPLE-WITH-REPLACEMENT from each
pair's pooled runs (`simulate()`, `random.choice(pooled[pair])`) — not a
permutation.

PP-W-rows (v0.16, roadmap §2.3(b)) adds, without touching the defaults:

    --population {w5-parity-002,e1-rows-parity-001,pooled}
        w5-parity-002        (default) the A-1.11 output above
        e1-rows-parity-001   the realized PP-E1 leg-B epoch (arms
                             `calor+v0.14.3` control, `calor+0.15.0`
                             treatment), corrected tokens, 40 valid runs
        pooled               e1-rows-parity-001 + w5-parity-002: each pair's
                             null bag is the UNION of both epochs' runs
                             (both arms), while the redraw sizes stay those of
                             e1-rows-parity-001 (5/5 — the design leg B mirrors)
    --sims N / --boot N / --seed S   (env PPE1_SIMS / PPE1_BOOT still honoured
                             for --sims/--boot; --seed default 4537)
    --grid {frozen,extended} frozen = (1.25 ... 1.50), the A-1.11 grid;
                             extended adds 1.15 and 1.20 (§2.3(b)). The grid
                             never touches the RNG sequence, only which
                             margins are tabulated.
    --half-width H           the Monte-Carlo half-width the A-1.12 rule adds
                             to p95 before rounding up (default 0.005); the
                             rule line prints only for non-default runs.

Usage (from the repository root):

    python3 bench/phase0-agent-native/ppe1-margin-derivation.py \
        > bench/phase0-agent-native/ppe1-margin-derivation.txt        # A-1.11
    python3 bench/phase0-agent-native/ppe1-margin-derivation.py \
        --population e1-rows-parity-001 --sims 3000 --seed 4537 --grid extended \
        > bench/phase0-agent-native/ppw-margin-derivation.txt         # PP-W-rows

Python 3.9 compatible; standard library only.
"""
import argparse
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
FROZEN_MARGINS = (1.25, 1.30, 1.35, 1.40, 1.45, 1.50)
EXTENDED_MARGINS = (1.15, 1.20) + FROZEN_MARGINS
MARGINS = FROZEN_MARGINS
EFFECTS = (1.0, 1.25, 1.4, 1.6)
DEFAULT_POPULATION = "w5-parity-002"
DEFAULT_HALF_WIDTH = 0.005

# Arm-directory -> role maps per archived epoch (the roles the ratio reads).
ARM_MAPS = {
    "m5-compare-001": {"calor": "A", "calor+mcp-file": "B"},
    "w5-parity-002": {"calor+control": "control", "calor+treatment": "treatment"},
    "e1-rows-parity-001": {"calor+v0.14.3": "control", "calor+0.15.0": "treatment"},
}
POPULATIONS = ("w5-parity-002", "e1-rows-parity-001", "pooled")


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
        # `naive` = the envelope's final-turn usage.output_tokens. Pre-A-1.9.1
        # epochs (m5-compare-001, w5-parity-002) archived it as tokens.output;
        # epochs run after #1092 archive the CORRECTED figure there and keep
        # the naive one in tokenUsage.output_tokens_naive — read that first so
        # the naive column stays what its name says on every population.
        token_usage = record.get("tokenUsage") or {}
        naive = token_usage.get("output_tokens_naive")
        if not isinstance(naive, int):
            naive = (record.get("tokens", {}) or {}).get("output", 0)
        agent = os.path.join(os.path.dirname(path), "agent.json")
        usage = corrected_tokens(agent) if os.path.exists(agent) else {}
        cells.setdefault(pair, {}).setdefault(arm_map[arm_dir], []).append(
            {"naive": naive, "corrected": usage.get("output_tokens_corrected") or naive})
    return cells


def pool_cells(primary, secondary):
    """Union of two epochs' runs per (pair, arm role); pairs present in the
    primary only (the design epoch) are kept, so its realized sizes govern."""
    pooled = {}
    for pair in primary:
        pooled[pair] = {}
        for arm in primary[pair]:
            pooled[pair][arm] = list(primary[pair][arm]) + list(secondary.get(pair, {}).get(arm, []))
    return pooled


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


def simulate(cells, key, sizes=None):
    """Null (and scaled-treatment) simulation: both arms drawn from the pair's
    pooled runs, WITH REPLACEMENT (the A-1.12-named convention). `sizes`
    (pair -> [n_control, n_treatment]) defaults to the cells' realized sizes."""
    pairs = sorted(cells)
    pooled = {p: [r[key] for arm in cells[p] for r in cells[p][arm]] for p in pairs}
    if sizes is None:
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


def a112_margin(p95, half_width):
    """The A-1.12 rule (roadmap §4.1 "Margin"): the 0.05 grid line above
    (p95 + its Monte-Carlo half-width). 1.1766 + 0.005 = 1.1816 -> 1.20."""
    return grid_round_up(p95 + half_width)


def report(title, cells, keys, sizes=None, rule_half_width=None):
    print(f"\n=== {title} ===")
    for pair in sorted(cells):
        for arm in sorted(cells[pair]):
            naive = [r["naive"] for r in cells[pair][arm]]
            corrected = [r["corrected"] for r in cells[pair][arm]]
            print(f"  {pair:8s} {arm:10s} n={len(corrected)} "
                  f"mean_corrected={statistics.mean(corrected):8.0f} cv_corrected={cv(corrected):.4f} "
                  f"mean_naive={statistics.mean(naive):8.0f} cv_naive={cv(naive):.4f}")
    summary = {}
    for key in keys:
        values = [cv([r[key] for r in cells[p][a]]) for p in sorted(cells) for a in sorted(cells[p])]
        values = [v for v in values if v is not None]
        print(f"  within-cell CV ({key}): median {statistics.median(values):.4f}  "
              f"max {max(values):.4f}  cells {len(values)}")
        random.seed(SEED)
        results = simulate(cells, key, sizes)
        null = results[1.0]
        print(f"  [{key}] null point: median {null['p50']:.4f}  p95 {null['p95']:.4f}  "
              f"-> margin on the 0.05 grid: {grid_round_up(null['p95']):.2f}")
        if rule_half_width is not None:
            print(f"  [{key}] A-1.12 rule: grid line above (p95 {null['p95']:.4f} + half-width "
                  f"{rule_half_width:.3f} = {null['p95'] + rule_half_width:.4f}) "
                  f"-> margin {a112_margin(null['p95'], rule_half_width):.2f}")
        for margin in MARGINS:
            print(f"      margin {margin:.2f}: null false-fail — point-only "
                  f"{null['point_only'][margin]:.3f}, conjunction {null['conjunction'][margin]:.3f}")
        for effect in (1.25, 1.4, 1.6):
            row = results[effect]
            print(f"      power @ {effect}x (conjunction): " + "  ".join(
                f"m={m:.2f} {row['conjunction'][m]:.3f}" for m in MARGINS))
        summary[key] = {"cv_median": statistics.median(values), "cv_max": max(values),
                        "null": null, "results": results}
    return summary


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="PP-E1 / PP-W-rows leg-B margin derivation")
    parser.add_argument("--population", choices=POPULATIONS, default=DEFAULT_POPULATION)
    parser.add_argument("--sims", type=int, default=SIMS)
    parser.add_argument("--boot", type=int, default=BOOT)
    parser.add_argument("--seed", type=int, default=SEED)
    parser.add_argument("--grid", choices=("frozen", "extended"), default="frozen")
    parser.add_argument("--half-width", type=float, default=DEFAULT_HALF_WIDTH)
    return parser.parse_args(argv)


def main(argv=None):
    global SIMS, BOOT, SEED, MARGINS
    args = parse_args(argv)
    SIMS, BOOT, SEED = args.sims, args.boot, args.seed
    MARGINS = EXTENDED_MARGINS if args.grid == "extended" else FROZEN_MARGINS
    non_default = args.population != DEFAULT_POPULATION or args.grid == "extended"
    rule_hw = args.half_width if non_default else None

    print(f"PP-E1 margin derivation — {SIMS} null simulations x {BOOT}-resample "
          f"two-level cluster bootstrap, seed {SEED}")
    if non_default:
        print(f"population {args.population}; grid {args.grid} ({', '.join(f'{m:.2f}' for m in MARGINS)}); "
              f"null redraw: resample-with-replacement from each pair's pooled runs; "
              f"A-1.12 rule half-width {args.half_width:.3f}")

    if args.population == DEFAULT_POPULATION:
        report("m5-compare-001 — PP-W5's derivation population (method reproduction)",
               collect("m5-compare-001", ARM_MAPS["m5-compare-001"]),
               ("naive", "corrected"), rule_half_width=rule_hw)
        report("w5-parity-002 — PP-E1's derivation population (realized parity epoch)",
               collect("w5-parity-002", ARM_MAPS["w5-parity-002"]),
               ("naive", "corrected"), rule_half_width=rule_hw)
        return 0

    e1 = collect("e1-rows-parity-001", ARM_MAPS["e1-rows-parity-001"])
    if not e1:
        print("ERROR: epochs/e1-rows-parity-001 holds no valid N1 runs", file=sys.stderr)
        return 2
    if args.population == "e1-rows-parity-001":
        report("e1-rows-parity-001 — PP-W-rows leg-B derivation population (realized PP-E1 epoch, corrected tokens)",
               e1, ("naive", "corrected"), rule_half_width=rule_hw)
        return 0
    w5 = collect("w5-parity-002", ARM_MAPS["w5-parity-002"])
    sizes = {p: [len(e1[p][a]) for a in sorted(e1[p])] for p in sorted(e1)}
    report("pooled — e1-rows-parity-001 + w5-parity-002 (null bag = union per pair; redraw sizes = e1's)",
           pool_cells(e1, w5), ("naive", "corrected"), sizes=sizes, rule_half_width=rule_hw)
    return 0


if __name__ == "__main__":
    sys.exit(main())
