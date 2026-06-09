#!/usr/bin/env python3
"""Phase 2 — bulk round-trip validation.

For each real .calr fixture (samples/, src/, tests/, benchmarks/):
  1. Read the closer-form source.
  2. Run `to_indent` to produce indent-form source.
  3. Invoke `calor diagnose` on BOTH versions.
  4. Compare diagnostic counts (closer vs indent should match).

Reports any fixture where indent diagnostics differ from closer diagnostics.
"""
from __future__ import annotations

import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
XFORM = ROOT / 'scripts' / 'calor_indent_xform.py'
CALOR_DLL = ROOT / 'src' / 'Calor.Compiler' / 'bin' / 'Debug' / 'net10.0' / 'calor.dll'

INCLUDE_DIRS = ['samples', 'src', 'tests', 'benchmarks']
EXCLUDE_RE = ['__golden', 'corpus_pristine', '/expected/', '\\expected\\',
              '/results/', '\\results\\', '/bench/', '\\bench\\']


def discover_fixtures() -> list[Path]:
    fixtures: list[Path] = []
    for d in INCLUDE_DIRS:
        for p in (ROOT / d).rglob('*.calr'):
            s = str(p).replace('\\', '/')
            if any(ex in s for ex in EXCLUDE_RE):
                continue
            if '\\bin\\' in str(p) or '\\obj\\' in str(p):
                continue
            fixtures.append(p)
    return fixtures


def diagnose(path: Path) -> tuple[int, str]:
    """Return (error_count, stdout_or_stderr)."""
    try:
        r = subprocess.run(
            ['dotnet', str(CALOR_DLL), 'diagnose', str(path)],
            capture_output=True, text=True, timeout=30,
            encoding='utf-8', errors='replace',
        )
    except subprocess.TimeoutExpired:
        return (-1, 'TIMEOUT')
    out = r.stdout
    err_count = 0
    for line in out.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            j = json.loads(line)
            if j.get('severity') == 'error':
                err_count += 1
        except json.JSONDecodeError:
            pass
    return (err_count, out)


def main() -> int:
    fixtures = discover_fixtures()
    print(f'Discovered {len(fixtures)} fixtures', flush=True)

    sys.path.insert(0, str(ROOT / 'scripts'))
    from calor_indent_xform import to_indent, load_openers
    openers = load_openers()

    matches = 0
    transform_fails = 0
    parse_mismatches = 0
    closer_errors = 0
    indent_errors = 0
    bad: list[str] = []

    for i, f in enumerate(fixtures):
        if i % 50 == 0:
            print(f'  [{i}/{len(fixtures)}] processed...', flush=True)
        try:
            closer_src = f.read_text(encoding='utf-8')
        except Exception as e:
            transform_fails += 1
            bad.append(f'{f}: read error: {e}')
            continue
        try:
            indent_src = to_indent(closer_src, openers)
        except Exception as e:
            transform_fails += 1
            bad.append(f'{f}: to_indent error: {e}')
            continue

        c_err, c_out = diagnose(f)
        with tempfile.NamedTemporaryFile(
            mode='w', suffix='.calr', delete=False, encoding='utf-8'
        ) as tmp:
            tmp.write(indent_src)
            tmp_path = Path(tmp.name)
        try:
            i_err, i_out = diagnose(tmp_path)
        finally:
            tmp_path.unlink(missing_ok=True)

        if c_err > 0:
            closer_errors += 1
        if i_err > 0:
            indent_errors += 1

        if c_err == i_err:
            matches += 1
        else:
            parse_mismatches += 1
            if len(bad) < 30:
                bad.append(
                    f'{f}: closer_err={c_err} indent_err={i_err}'
                )

    print()
    print('=== PHASE 2 ROUND-TRIP VALIDATION ===')
    print(f'Total fixtures:         {len(fixtures)}')
    print(f'Transform failures:     {transform_fails}')
    print(f'Diagnostic matches:     {matches}')
    print(f'Diagnostic mismatches:  {parse_mismatches}')
    print(f'Closer had errors:      {closer_errors}')
    print(f'Indent had errors:      {indent_errors}')
    print()
    if bad:
        print('First 30 issues:')
        for b in bad[:30]:
            print(f'  {b}')

    return 0 if parse_mismatches == 0 and transform_fails == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
