#!/usr/bin/env python3
"""Phase 2 — bulk fixture migration: closer-form → indent-form.

Walks the same fixtures discovered by phase2_validate_corpus.py and rewrites
each one in place using calor_indent_xform.to_indent().

Excludes:
  - Snapshot fixtures (*.approved.calr, *.received.calr) — those are emitter
    output and Calor.Conversion.Tests compares string-equal against them.
  - corpus_pristine / __golden / expected directories.
  - Optionally: a skip-list of files passed via --skip-list.

Usage:
  python scripts/phase2_migrate_fixtures.py            # dry-run (counts only)
  python scripts/phase2_migrate_fixtures.py --apply    # actually write files
  python scripts/phase2_migrate_fixtures.py --apply --only samples
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'scripts'))
from calor_indent_xform import to_indent, load_openers  # noqa: E402

INCLUDE_DIRS = ['samples', 'src', 'tests', 'benchmarks']
EXCLUDE_RE = [
    '__golden', 'corpus_pristine',
    '/expected/', '\\expected\\',
    '/Snapshots/', '\\Snapshots\\',
    '.approved.calr', '.received.calr',
    '/results/', '\\results\\',
    '/bench/', '\\bench\\',
    '/bin/', '\\bin\\',
    '/obj/', '\\obj\\',
]


def should_skip(p: Path) -> bool:
    s = str(p).replace('\\', '/')
    return any(ex.replace('\\', '/') in s for ex in EXCLUDE_RE)


def discover(only: list[str]) -> list[Path]:
    dirs = only if only else INCLUDE_DIRS
    fixtures: list[Path] = []
    for d in dirs:
        for p in (ROOT / d).rglob('*.calr'):
            if should_skip(p):
                continue
            fixtures.append(p)
    return fixtures


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--apply', action='store_true', help='Actually rewrite files')
    ap.add_argument('--only', nargs='*', default=[],
                    help='Restrict to subset of INCLUDE_DIRS (e.g. samples)')
    ap.add_argument('--skip-list', type=Path,
                    help='File of paths to skip (one per line, repo-relative)')
    args = ap.parse_args()

    skip_set: set[str] = set()
    if args.skip_list and args.skip_list.is_file():
        for line in args.skip_list.read_text(encoding='utf-8').splitlines():
            line = line.strip()
            if line and not line.startswith('#'):
                skip_set.add(line.replace('\\', '/'))

    openers = load_openers()
    fixtures = discover(args.only)
    print(f'Discovered {len(fixtures)} candidate fixtures', flush=True)
    if skip_set:
        print(f'Skip-list has {len(skip_set)} entries', flush=True)

    migrated = 0
    skipped_list = 0
    no_change = 0
    failures = 0
    fail_paths: list[str] = []

    for p in fixtures:
        rel = str(p.relative_to(ROOT)).replace('\\', '/')
        if rel in skip_set:
            skipped_list += 1
            continue
        try:
            src = p.read_text(encoding='utf-8')
        except Exception as e:
            failures += 1
            fail_paths.append(f'{rel}: read error: {e}')
            continue
        try:
            new_src = to_indent(src, openers)
        except Exception as e:
            failures += 1
            fail_paths.append(f'{rel}: to_indent error: {e}')
            continue
        if new_src == src:
            no_change += 1
            continue
        if args.apply:
            p.write_text(new_src, encoding='utf-8', newline='\n')
        migrated += 1

    print()
    print('=== PHASE 2 MIGRATION ===')
    print(f'Total candidates:       {len(fixtures)}')
    print(f'Skipped (skip-list):    {skipped_list}')
    print(f'No-change (already):    {no_change}')
    print(f'Migrated:               {migrated} ({"applied" if args.apply else "DRY-RUN"})')
    print(f'Failures:               {failures}')
    if fail_paths:
        print('Failures (first 30):')
        for fp in fail_paths[:30]:
            print(f'  {fp}')

    return 0 if failures == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
