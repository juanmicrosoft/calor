"""Deterministic prompt-body size comparison (no LLM calls). Companion to smoke."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from phase3_smoke_comprehension import SPECS, strip_closer_lines, build_prompt

ROOT = Path(__file__).resolve().parent.parent
print(f'{"file":<40} {"closer_chars":>12} {"indent_chars":>12} {"delta_chars":>12} {"delta_pct":>10}')
print('-' * 90)
totals = {'closer': 0, 'indent': 0}
body_totals = {'closer': 0, 'indent': 0}
for spec in SPECS:
    src = (ROOT / spec.relpath).read_text(encoding='utf-8')
    src_i = strip_closer_lines(src)
    pc = build_prompt(spec, src, spec.relpath)
    pi = build_prompt(spec, src_i, spec.relpath)
    delta = len(pi) - len(pc)
    pct = (delta / len(pc)) * 100
    totals['closer'] += len(pc)
    totals['indent'] += len(pi)
    body_totals['closer'] += len(src)
    body_totals['indent'] += len(src_i)
    body_delta_pct = (len(src_i) - len(src)) / len(src) * 100
    print(f'{Path(spec.relpath).name:<40} {len(pc):>12} {len(pi):>12} {delta:>+12} {pct:>+9.2f}%   (body {body_delta_pct:+.2f}%)')
print('-' * 90)
delta_total = totals['indent'] - totals['closer']
pct_total = (delta_total / totals['closer']) * 100
body_delta_total = body_totals['indent'] - body_totals['closer']
body_pct_total = (body_delta_total / body_totals['closer']) * 100
print(f'{"TOTAL PROMPT":<40} {totals["closer"]:>12} {totals["indent"]:>12} {delta_total:>+12} {pct_total:>+9.2f}%')
print(f'{"TOTAL FILE BODY ONLY":<40} {body_totals["closer"]:>12} {body_totals["indent"]:>12} {body_delta_total:>+12} {body_pct_total:>+9.2f}%')

