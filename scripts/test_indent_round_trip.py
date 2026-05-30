#!/usr/bin/env python3
"""Round-trip test: closer → to_indent → from_indent → closer (modulo whitespace + IDs).

Reports pass/fail per file, with a diff hint on failure.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from calor_indent_xform import (  # type: ignore[import-not-found]
    canonical,
    from_indent,
    load_openers,
    to_indent,
)


def round_trip_check(src: str, openers_map: dict[str, str]):
    """Return (ok, indent_form, closer_form, original_canonical, roundtrip_canonical)."""
    indent_form = to_indent(src, openers_map)
    closer_form = from_indent(indent_form, openers_map)
    orig_c = canonical(src)
    rt_c = canonical(closer_form)
    return (orig_c == rt_c), indent_form, closer_form, orig_c, rt_c


def diff_lines(a: str, b: str, limit: int = 6) -> list[str]:
    aL = a.splitlines()
    bL = b.splitlines()
    diffs = []
    for i in range(max(len(aL), len(bL))):
        la = aL[i] if i < len(aL) else '<EOF>'
        lb = bL[i] if i < len(bL) else '<EOF>'
        if la != lb:
            diffs.append(f'  line {i+1}: orig={la!r}  rt={lb!r}')
            if len(diffs) >= limit:
                diffs.append('  ...')
                break
    return diffs


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('paths', nargs='+', help='Files or globs to test')
    ap.add_argument('--verbose', action='store_true')
    args = ap.parse_args()

    openers_map = load_openers()
    files = []
    for p in args.paths:
        path = Path(p)
        if path.is_dir():
            files.extend(sorted(path.rglob('*.calr')))
        elif '*' in p:
            files.extend(sorted(Path('.').glob(p)))
        else:
            files.append(path)

    if not files:
        print('No files found', file=sys.stderr)
        return 1

    n_pass = n_fail = 0
    failures = []
    for f in files:
        try:
            src = f.read_text(encoding='utf-8')
        except (OSError, UnicodeDecodeError) as e:
            print(f'SKIP  {f}: {e}', file=sys.stderr)
            continue
        ok, ind, cls, oc, rc = round_trip_check(src, openers_map)
        if ok:
            n_pass += 1
            if args.verbose:
                print(f'PASS  {f}')
        else:
            n_fail += 1
            failures.append(f)
            print(f'FAIL  {f}')
            for ln in diff_lines(oc, rc):
                print(ln)

    print(f'\n{n_pass}/{n_pass + n_fail} pass ({n_fail} fail)')
    return 0 if n_fail == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
