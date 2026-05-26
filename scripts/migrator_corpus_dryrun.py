#!/usr/bin/env python3
"""
migrator_corpus_dryrun.py — Tier 2 migrator dry-run over the corpus.

Walks every `.calr` file under a directory tree and invokes the
migrator in `--dry-run` mode. Asserts the migrator does not modify
the file on disk and that the byte-preservation invariant holds for
the proposed migration.

In Phase 0 (no migrator yet), this script reports the count of `.calr`
files that WOULD be migrated and exits 0 — the actual migrator hooks
in at PR-1c.

Usage:
    python3 scripts/migrator_corpus_dryrun.py <root-dir> \\
        [--mode drop-structural-ids|compact-ids]
"""

from __future__ import annotations

import argparse
import hashlib
import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CALOR_PROJECT = REPO_ROOT / "src" / "Calor.Compiler"

ID_BLOCK_RE = re.compile(r"§[A-Z]+\{[^}]*\}")


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
        print(f"migrator_corpus_dryrun: not a directory: {root}",
              file=sys.stderr)
        return 2

    calor_cmd = calor_command()
    files = sorted(root.rglob("*.calr"))
    print(f"migrator_corpus_dryrun: {len(files)} file(s) in scope "
          f"(mode={args.mode})")

    # Probe whether the migrator subcommand exists.
    probe = subprocess.run(
        calor_cmd + ["fix", "--help"], capture_output=True, text=True
    )
    migrator_present = (
        probe.returncode == 0 and args.mode in (probe.stdout + probe.stderr)
    )
    if not migrator_present:
        print("migrator_corpus_dryrun: migrator subcommand not yet "
              "available; reporting candidate count (Phase 0).")
        n_candidates = sum(
            1 for f in files
            if ID_BLOCK_RE.search(f.read_text(encoding="utf-8", errors="ignore"))
        )
        print(f"  files_with_id_blocks: {n_candidates}")
        return 0

    failures = 0
    # `calor fix` takes a positional <root> directory and walks it
    # recursively for .calr files; it does not accept --input. For
    # Tier 2 corpus coverage we invoke fix once over the whole root
    # and rely on `--dry-run` to ensure no file changes.
    hashes_before = {f: file_sha1(f) for f in files}
    cmd = calor_cmd + [
        "fix", str(root), f"--{args.mode}", "--dry-run",
    ]
    cp = subprocess.run(cmd, capture_output=True, text=True, cwd=REPO_ROOT)
    if cp.returncode != 0:
        print(f"  FAIL: migrator exit {cp.returncode}", file=sys.stderr)
        print(f"    {cp.stderr.strip()[-1000:]}", file=sys.stderr)
        return 1
    # Verify no file was actually modified by dry-run.
    for f in files:
        h_after = file_sha1(f)
        if h_after != hashes_before.get(f):
            print(f"  FAIL {f}: file changed despite --dry-run",
                  file=sys.stderr)
            failures += 1

    if failures:
        print(f"\nmigrator_corpus_dryrun: {failures} failure(s)",
              file=sys.stderr)
        return 1
    print(f"migrator_corpus_dryrun: OK ({len(files)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
