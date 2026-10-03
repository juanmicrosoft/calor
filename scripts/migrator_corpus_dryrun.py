#!/usr/bin/env python3
"""
migrator_corpus_dryrun.py — Tier 2 migrator dry-run over the corpus.

Invokes the migrator in `--dry-run` mode over a directory tree and
asserts it does not modify any tracked `.calr` file on disk. It does
not check byte preservation (byte_preservation_check.py does).

Repaired by #1241: it runs the checkout-built compiler (never an
installed `calor`), selects tracked files only (never bin/ or obj/
copies), fails on an empty selection, exits 3 (TimeoutOrUnavailable,
not a pass) when the migrator is missing, and fails when the dry run
of a synthetic control file reports no change (a vacuous run).

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
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))
import checkout_compiler  # noqa: E402

UNAVAILABLE = 3
# A file the forward rewrite of each mode must change, so that neither
# migrator check can pass without exercising the migrator.
CONTROLS = {
    "drop-structural-ids": "§M{m_01J5X7K9M2NP:Ctl}\n"
    "  §F{f_01J5X7K9M2NQ:One:pub} () -> i32\n    §R INT:1\n"
    "  §/F{f_01J5X7K9M2NQ}\n§/M{m_01J5X7K9M2NP}\n",
    "compact-ids": "§M{m_01J5X7K9M2NPQRSTABWXYZ1234:Ctl}\n"
    "  §F{f_01J5X7K9M2NPQRSTABWXYZ1235:One:pub} () -> i32\n    §R INT:1\n",
}


def calor_command() -> list[str]:
    """The checkout-built compiler; raises instead of falling back."""
    return checkout_compiler.resolve(REPO_ROOT).command


def tracked_files(root: Path) -> list[Path]:
    rel = root.resolve().relative_to(REPO_ROOT.resolve()).as_posix()
    return [REPO_ROOT / f
            for f in checkout_compiler.tracked_calr(REPO_ROOT, [rel])]


def migrator_available(calor_cmd: list[str], flags: list[str]) -> bool:
    probe = subprocess.run(
        calor_cmd + ["fix", "--help"], capture_output=True, text=True
    )
    text = probe.stdout + probe.stderr
    return probe.returncode == 0 and all(f in text for f in flags)


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

    try:
        calor_cmd = calor_command()
        files = tracked_files(root)
    except checkout_compiler.CompilerResolutionError as e:
        print(f"migrator_corpus_dryrun: {e}", file=sys.stderr)
        return 2
    print(f"migrator_corpus_dryrun: {len(files)} tracked file(s) in scope "
          f"(mode={args.mode})")
    if not files:
        print("migrator_corpus_dryrun: FAIL: empty selection",
              file=sys.stderr)
        return 1

    if not migrator_available(calor_cmd, [f"--{args.mode}", "--dry-run"]):
        print("migrator_corpus_dryrun: TimeoutOrUnavailable: the migrator "
              "subcommand or flag is missing (not a pass)", file=sys.stderr)
        return UNAVAILABLE

    failures = 0
    # `calor fix` takes a positional <root> directory and walks it
    # recursively for .calr files; it does not accept --input. For
    # Tier 2 corpus coverage we invoke fix once over the whole root
    # and rely on `--dry-run` to ensure no file changes.
    hashes_before = {f: file_sha1(f) for f in files}
    with tempfile.TemporaryDirectory() as td:
        control = Path(td) / "control.calr"
        control.write_text(CONTROLS[args.mode], encoding="utf-8")
        before = file_sha1(control)
        for target in (root, Path(td)):
            cmd = calor_cmd + [
                "fix", str(target), f"--{args.mode}", "--dry-run",
            ]
            cp = subprocess.run(cmd, capture_output=True, text=True,
                                cwd=REPO_ROOT)
            if cp.returncode != 0:
                print(f"  FAIL: migrator exit {cp.returncode}",
                      file=sys.stderr)
                print(f"    {cp.stderr.strip()[-1000:]}", file=sys.stderr)
                return 1
        m = re.search(r"files_changed=(\d+)", cp.stdout)
        if not m or int(m.group(1)) < 1 or file_sha1(control) != before:
            print("  FAIL: the dry run of the control file reported no "
                  "change or wrote it; the check exercised nothing",
                  file=sys.stderr)
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
