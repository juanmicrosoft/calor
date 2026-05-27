#!/usr/bin/env python3
"""
migrator_revert_roundtrip.py — Tier 2 round-trip migrate/revert check.

For each `.calr` file under a directory tree, perform:
    original → migrate → revert → byte-equal to original

This script delegates the migrate/revert steps to the `calor fix`
subcommand. In Phase 0 (no migrator yet) it reports that the subcommand
is not present and exits 0; the check becomes substantive at PR-1c
(drop-structural-ids) and again at PR-2f (compact-ids).

Usage:
    python3 scripts/migrator_revert_roundtrip.py <root-dir> \\
        [--mode drop-structural-ids|compact-ids]
"""

from __future__ import annotations

import argparse
import hashlib
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CALOR_PROJECT = REPO_ROOT / "src" / "Calor.Compiler"


def calor_command() -> list[str]:
    from shutil import which
    if which("calor"):
        return ["calor"]
    return [
        "dotnet", "run", "--project", str(CALOR_PROJECT), "-c", "Release",
        "--no-build", "--",
    ]


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

    calor_cmd = calor_command()
    probe = subprocess.run(
        calor_cmd + ["fix", "--help"], capture_output=True, text=True
    )
    forward_flag = f"--{args.mode}"
    text = probe.stdout + probe.stderr
    if (
        probe.returncode != 0
        or forward_flag not in text
        or "--revert" not in text
        or "--log" not in text
    ):
        print(f"migrator_revert_roundtrip: required flags not present in "
              f"`calor fix --help`; Phase 0 stub run.")
        return 0

    failures = 0
    files = sorted(root.rglob("*.calr"))
    print(f"migrator_revert_roundtrip: {len(files)} file(s) (mode={args.mode})")
    with tempfile.TemporaryDirectory() as td:
        work_root = Path(td) / "work"
        log_path = Path(td) / "migration.log.json"
        shutil.copytree(root, work_root)
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
        # Compare.
        for f in files:
            rel = f.relative_to(root)
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
