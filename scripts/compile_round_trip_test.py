#!/usr/bin/env python3
"""Compile a sample, then round-trip, then compile the round-trip.
Report: baseline_compile_count, rt_compile_count, regression_files."""
from __future__ import annotations

import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from calor_indent_xform import from_indent, load_openers, to_indent  # type: ignore

CALOR = Path('src/Calor.Compiler/bin/Debug/net10.0/calor.exe').resolve()


def compile_file(src: Path, tmpdir: Path) -> bool:
    out = tmpdir / (src.stem + '.g.cs')
    try:
        r = subprocess.run(
            [str(CALOR), '--input', str(src), '-o', str(out)],
            capture_output=True, text=True, timeout=30, encoding='utf-8',
        )
        return r.returncode == 0
    except (subprocess.TimeoutExpired, OSError):
        return False


def main() -> int:
    openers = load_openers()
    # Use samples/ — smaller set, faster check
    base = Path('samples')
    files = sorted(base.rglob('*.calr'))
    if not files:
        print('no .calr files', file=sys.stderr)
        return 1

    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        baseline_ok: list[Path] = []
        for f in files:
            if compile_file(f, tmp):
                baseline_ok.append(f)
        print(f'baseline: {len(baseline_ok)}/{len(files)} compile')

        rt_ok = 0
        rt_failed: list[Path] = []
        for f in baseline_ok:
            src = f.read_text(encoding='utf-8')
            try:
                rt = from_indent(to_indent(src, openers), openers)
            except Exception:  # noqa: BLE001
                rt_failed.append(f)
                continue
            rt_file = tmp / (f.stem + '_rt.calr')
            rt_file.write_text(rt, encoding='utf-8')
            if compile_file(rt_file, tmp):
                rt_ok += 1
            else:
                rt_failed.append(f)

        print(f'round-trip:  {rt_ok}/{len(baseline_ok)} compile after round-trip')
        if rt_failed:
            print(f'\nRegressions ({len(rt_failed)}):')
            for f in rt_failed[:20]:
                print(f'  {f}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
