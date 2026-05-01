#!/usr/bin/env python3
"""Aggregate metrics across all trials in bench/research-phase-0/runs/."""
import json
import glob
import os
import statistics
import sys

base = os.path.dirname(os.path.abspath(__file__))
runs_dir = os.path.join(base, 'runs')

results = {}
for path in sorted(glob.glob(os.path.join(runs_dir, '*', 'metrics.json'))):
    try:
        m = json.load(open(path))
    except Exception as e:
        print(f'skip {path}: {e}', file=sys.stderr)
        continue
    arm = m.get('arm', '?')
    prompt = m.get('prompt', '?')
    rd = m.get('run_dir', '?')
    ap = m.get('acceptance_passed', 0)
    at = m.get('acceptance_total', 0)
    q = m.get('quality', 0)
    rf = m.get('regression_failed', 0)
    rt = m.get('regression_total', 42)
    key = f'{prompt}-{arm}'
    results.setdefault(key, []).append((rd, ap, at, q, rf, rt))

for key in sorted(results):
    rows = results[key]
    accs = [(ap, at) for _, ap, at, _, _, _ in rows]
    qs = [q for _, _, _, q, _, _ in rows]
    print(f'{key}: n={len(rows)}')
    for r in rows:
        print(f'  {r[0]:32s} acc={r[1]}/{r[2]} strict_q={r[3]} reg_fail={r[4]}/{r[5]}')
    frac = [a/t if t else 0 for a, t in accs]
    print(f'  -> mean fractional acc = {statistics.mean(frac):.2f}, '
          f'median fractional acc = {statistics.median(frac):.2f}, '
          f'mean strict q = {statistics.mean(qs):.2f}, '
          f'median strict q = {statistics.median(qs):.2f}')
    print()

# Compute key ratios for the decision table.
def get_median(key, field='quality'):
    if key not in results:
        return None
    if field == 'frac':
        return statistics.median([(ap/at if at else 0) for _, ap, at, _, _, _ in results[key]])
    return statistics.median([q for _, _, _, q, _, _ in results[key]])

print('=== DECISION TABLE INPUTS ===')
for prompt in ['T1A', 'T1B', 'T1C']:
    a = get_median(f'{prompt}-annotated', 'quality')
    b = get_median(f'{prompt}-bare', 'quality')
    af = get_median(f'{prompt}-annotated', 'frac')
    bf = get_median(f'{prompt}-bare', 'frac')
    print(f'{prompt}: median strict q  annot={a} bare={b} ratio={(a/b if b else "n/a") if a is not None and b is not None else "n/a"}')
    print(f'{prompt}: median frac acc  annot={af:.2f} bare={bf:.2f} ratio={(af/bf if bf else "n/a"):.2f if isinstance(af/bf if bf else 0, float) else "n/a"}' if af is not None and bf else f'{prompt}: median frac acc not enough data')
