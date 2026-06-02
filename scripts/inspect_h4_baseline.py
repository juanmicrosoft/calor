"""Inspect prior H4 recovery results baseline."""
import json
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
data = json.loads((REPO / 'scripts' / 'phase3-h4-recovery-results.json').read_text(encoding='utf-8'))

print(f'total trials: {len(data)}')
print(f'arms: {Counter(t["arm"] for t in data)}')

for arm in ('closer', 'indent'):
    real = [t for t in data if t['arm'] == arm and t.get('error', '') != 'no_compile_error']
    fixed = sum(1 for t in real if t['fix_compiles'])
    total_cost = sum(t['cost_usd'] for t in real)
    print(f'  {arm}: {fixed}/{len(real)} fixed ({fixed/max(len(real),1)*100:.0f}%)  '
          f'avg ${total_cost/max(len(real),1):.4f}/trial  total ${total_cost:.3f}')
