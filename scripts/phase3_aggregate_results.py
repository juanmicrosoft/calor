"""Quick aggregation of H2-CM and H3 results for the cross-study summary."""
import json
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).parent


def aggregate(path, key_fields):
    d = json.load(open(path, encoding='utf-8'))
    agg = defaultdict(lambda: {'pass': 0, 'fail': 0, 'cost': 0.0, 'dur': 0})
    for t in d:
        # pass detection: compile_ok AND must_contain_ok AND must_not_contain_ok if present
        ok = bool(t.get('compile_ok', t.get('fix_compiles', t.get('pass', False))))
        if 'must_contain_ok' in t:
            ok = ok and bool(t['must_contain_ok'])
        if 'must_not_contain_ok' in t:
            ok = ok and bool(t['must_not_contain_ok'])
        k = tuple(t.get(f, '?') for f in key_fields)
        if ok:
            agg[k]['pass'] += 1
        else:
            agg[k]['fail'] += 1
        agg[k]['cost'] += float(t.get('cost_usd', t.get('cost_proxy_usd', 0)) or 0)
        agg[k]['dur'] += int(t.get('duration_ms', 0) or 0)
    return agg, len(d)


def main():
    print('=== H2-CM (cross-model edit) ===')
    agg, n = aggregate(ROOT / 'phase3-h2-crossmodel-results.json', ('model', 'arm'))
    print(f'total trials: {n}')
    for k, v in sorted(agg.items()):
        total = v['pass'] + v['fail']
        avg_cost = v['cost'] / total if total else 0
        avg_dur = v['dur'] / total if total else 0
        model, arm = k
        print(f'  {model:25s} {arm:7s}  {v["pass"]:>3}/{total}  total=${v["cost"]:.4f}  avg=${avg_cost:.5f}  dur_avg={avg_dur/1000:.1f}s')

    # Per-model arm comparison
    print('\n--- H2-CM per-model deltas ---')
    models = sorted({k[0] for k in agg})
    for m in models:
        c = agg[(m, 'closer')]
        i = agg[(m, 'indent')]
        cnc = c['pass'] + c['fail']
        cni = i['pass'] + i['fail']
        if cnc and cni:
            ac = c['cost'] / cnc
            ai = i['cost'] / cni
            dc = (ai - ac) / ac * 100 if ac else 0
            print(f'  {m:25s}  closer ${ac:.5f}  indent ${ai:.5f}  delta {dc:+.1f}%')

    print('\n=== H3 (deep nesting) ===')
    agg, n = aggregate(ROOT / 'phase3-h3-deep-results.json', ('arm',))
    print(f'total trials: {n}')
    for k, v in sorted(agg.items()):
        total = v['pass'] + v['fail']
        avg_cost = v['cost'] / total if total else 0
        avg_dur = v['dur'] / total if total else 0
        print(f'  {k[0]:7s}  {v["pass"]:>3}/{total}  total=${v["cost"]:.4f}  avg=${avg_cost:.5f}  dur_avg={avg_dur/1000:.1f}s')
    if ('closer',) in agg and ('indent',) in agg:
        c = agg[('closer',)]
        i = agg[('indent',)]
        ac = c['cost'] / max(c['pass'] + c['fail'], 1)
        ai = i['cost'] / max(i['pass'] + i['fail'], 1)
        print(f'  Δ cost per trial: {(ai-ac)/ac*100:+.1f}% (indent vs closer)'.encode('utf-8').decode('utf-8'))

    print('\n=== H4 (error recovery) ===')
    agg_arm = defaultdict(lambda: {'fixed': 0, 'failed': 0, 'skip': 0, 'cost': 0.0})
    d4 = json.load(open(ROOT / 'phase3-h4-recovery-results.json', encoding='utf-8'))
    for t in d4:
        k = t.get('arm', '?')
        agg_arm[k]['cost'] += float(t.get('cost_usd', 0))
        if t.get('error') == 'no_compile_error':
            agg_arm[k]['skip'] += 1
        elif t.get('fix_compiles'):
            agg_arm[k]['fixed'] += 1
        else:
            agg_arm[k]['failed'] += 1
    for k, v in sorted(agg_arm.items()):
        attempted = v['fixed'] + v['failed']
        rate = v['fixed'] / attempted * 100 if attempted else 0
        print(f'  {k:7s}  fixed={v["fixed"]}/{attempted} ({rate:.1f}%)  skipped={v["skip"]}  cost=${v["cost"]:.4f}')


if __name__ == '__main__':
    main()
