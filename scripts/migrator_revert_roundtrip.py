#!/usr/bin/env python3
"""
migrator_revert_roundtrip.py — Tier 2 round-trip migrate/revert check.

For each `.calr` file under a directory tree, perform:
    original → migrate → revert → byte-equal to original

A source byte round trip through `calor fix`, not an AST round trip.
Repaired by #1241: checkout-built compiler, scratch copy of tracked files
plus a control file the rewrite must change (else: vacuous, FAIL); empty
selection fails; missing flags exit 3 (TimeoutOrUnavailable).

Usage:
    python3 scripts/migrator_revert_roundtrip.py <root-dir> \\
        [--mode drop-structural-ids|compact-ids]
"""

from __future__ import annotations

import argparse
import hashlib
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))
import checkout_compiler  # noqa: E402
from migrator_corpus_dryrun import (  # noqa: E402
    CONTROLS, UNAVAILABLE, calor_command, migrator_available, tracked_files)


def file_sha1(p: Path) -> str:
    return hashlib.sha1(p.read_bytes()).hexdigest()


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("root", help="Root directory.")
    p.add_argument("--mode", default="drop-structural-ids",
                   choices=("drop-structural-ids", "compact-ids"))
    args = p.parse_args(argv)

    root = Path(args.root)
    if not root.is_dir():
        print(f"migrator_revert_roundtrip: not a directory: {root}",
              file=sys.stderr)
        return 2

    try:
        calor_cmd = calor_command()
        files = tracked_files(root)
    except checkout_compiler.CompilerResolutionError as e:
        print(f"migrator_revert_roundtrip: {e}", file=sys.stderr)
        return 2
    if not files:
        print("migrator_revert_roundtrip: FAIL: empty selection",
              file=sys.stderr)
        return 1
    forward_flag = f"--{args.mode}"
    if not migrator_available(calor_cmd, [forward_flag, "--revert", "--log"]):
        print("migrator_revert_roundtrip: TimeoutOrUnavailable: required "
              "flags not present in `calor fix --help` (not a pass)",
              file=sys.stderr)
        return UNAVAILABLE

    failures = 0
    print(f"migrator_revert_roundtrip: {len(files)} file(s) (mode={args.mode})")
    with tempfile.TemporaryDirectory() as td:
        work_root = Path(td) / "work"
        log_path = Path(td) / "migration.log.json"
        checkout_compiler.copy_tracked(
            REPO_ROOT, [str(f.relative_to(REPO_ROOT)) for f in files],
            work_root)
        control = work_root / "__control__" / "control.calr"
        control.parent.mkdir(parents=True)
        control.write_text(CONTROLS[args.mode], encoding="utf-8")
        control_sha = file_sha1(control)
        # Forward migration on the temp tree. `calor fix` takes the
        # root directory as a positional argument; the forward run
        # writes the migration log to --log.
        fwd = subprocess.run(
            calor_cmd + ["fix", str(work_root), forward_flag,
                         "--log", str(log_path)],
            capture_output=True, text=True, cwd=REPO_ROOT,
        )
        if fwd.returncode != 0:
            print("migrator_revert_roundtrip: forward FAIL",
                  file=sys.stderr)
            print(fwd.stderr[-1000:], file=sys.stderr)
            return 1
        if file_sha1(control) == control_sha:
            print("migrator_revert_roundtrip: FAIL: the forward rewrite did "
                  "not change the control file; the round trip exercised "
                  "nothing", file=sys.stderr)
            return 1
        rewritten = sum(file_sha1(work_root / f.relative_to(REPO_ROOT))
                        != file_sha1(f) for f in files)
        print(f"migrator_revert_roundtrip: forward rewrite changed "
              f"{rewritten} corpus file(s) plus the control")
        # Revert: same subcommand with --revert reads the same log to
        # reverse the rewrite.
        rev = subprocess.run(
            calor_cmd + ["fix", str(work_root), forward_flag,
                         "--revert", "--log", str(log_path)],
            capture_output=True, text=True, cwd=REPO_ROOT,
        )
        if rev.returncode != 0:
            print("migrator_revert_roundtrip: revert FAIL",
                  file=sys.stderr)
            print(rev.stderr[-1000:], file=sys.stderr)
            return 1
        if file_sha1(control) != control_sha:
            print("  byte-mismatch post-revert: control", file=sys.stderr)
            failures += 1
        # Compare.
        for f in files:
            rel = f.relative_to(REPO_ROOT)
            after = work_root / rel
            if not after.is_file():
                print(f"  missing post-revert: {rel}", file=sys.stderr)
                failures += 1
                continue
            if file_sha1(f) != file_sha1(after):
                print(f"  byte-mismatch post-revert: {rel}", file=sys.stderr)
                failures += 1

    if failures:
        print(f"\nmigrator_revert_roundtrip: {failures} failure(s)",
              file=sys.stderr)
        return 1
    print("migrator_revert_roundtrip: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
