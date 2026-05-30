#!/usr/bin/env python3
"""Categorize round-trip failures by symptom."""
from __future__ import annotations

import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from calor_indent_xform import (  # type: ignore[import-not-found]
    canonical,
    from_indent,
    load_openers,
    to_indent,
)


def classify(orig_c: str, rt_c: str) -> str:
    if orig_c == rt_c:
        return 'PASS'
    o_lines = orig_c.splitlines()
    r_lines = rt_c.splitlines()
    for i in range(min(len(o_lines), len(r_lines))):
        if o_lines[i] != r_lines[i]:
            ol, rl = o_lines[i], r_lines[i]
            # Heuristics:
            if rl.startswith('§/') and not ol.startswith('§/'):
                return 'closer_misplaced'
            if 'PP' in ol or 'PP' in rl:
                return 'preprocessor_block'
            if 'DOC' in ol or 'DOC' in rl:
                return 'doc_block'
            if '§C{' in ol or '§A' in ol or '§/C' in ol:
                return 'inline_method_call'
            if any(t in ol for t in ('§W{', '§TR{', '§IF{')):
                return 'inline_block_opener'
            return f'other (line {i+1}: {ol!r} vs {rl!r})'
    return 'length_mismatch'


def main() -> int:
    paths = ['samples', 'tests', 'benchmarks']
    openers = load_openers()
    cats: Counter[str] = Counter()
    examples: dict[str, str] = {}
    total = 0
    for p in paths:
        for f in Path(p).rglob('*.calr'):
            total += 1
            try:
                src = f.read_text(encoding='utf-8')
            except (OSError, UnicodeDecodeError):
                continue
            try:
                ind = to_indent(src, openers)
                cls = from_indent(ind, openers)
            except (Exception,) as e:  # noqa: BLE001
                cats[f'exception: {type(e).__name__}'] += 1
                continue
            cat = classify(canonical(src), canonical(cls))
            cats[cat] += 1
            if cat != 'PASS' and cat not in examples:
                examples[cat] = str(f)

    print(f'Total: {total}\n')
    for cat, n in cats.most_common():
        ex = examples.get(cat, '')
        print(f'  {n:5d}  {cat}    {ex}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
