#!/usr/bin/env python3
"""
validate_task_template.py — §5.3 task template validator.

Checks:
    1. `task.md` exists and is non-empty.
    2. `setup/` contains at least one `.calr` file.
    3. `expected/` contains at least one file consumed by `acceptance.sh`.
    4. `acceptance.sh` is executable and exits 0 when invoked against
       `expected/`.
    5. `acceptance.sh` exits non-zero when invoked against `setup/`
       (the task is non-trivial).

Usage:
    python3 scripts/validate_task_template.py <task-dir>
    python3 scripts/validate_task_template.py --all <templates-root>
"""

from __future__ import annotations

import argparse
import os
import shutil
import stat
import subprocess
import sys
import tempfile
from pathlib import Path


def _find_bash() -> str | None:
    """Locate a usable bash. On Windows, prefer Git Bash over the WSL
    shim (`C:\\Windows\\System32\\bash.exe`), which fails when WSL is not
    installed.
    """
    candidates: list[str] = []
    if os.name == "nt":
        candidates.extend([
            r"C:\Program Files\Git\bin\bash.exe",
            r"C:\Program Files (x86)\Git\bin\bash.exe",
        ])
    found = shutil.which("bash")
    if found:
        # Skip the WSL shim under System32 if Git Bash is available.
        if os.name == "nt" and "system32" in found.lower():
            for c in candidates:
                if Path(c).is_file():
                    return c
        candidates.append(found)
    for c in candidates:
        if Path(c).is_file():
            return c
    return None


def validate_one(task_dir: Path) -> list[str]:
    errors: list[str] = []
    if not task_dir.is_dir():
        return [f"not a directory: {task_dir}"]

    task_md = task_dir / "task.md"
    setup = task_dir / "setup"
    expected = task_dir / "expected"
    acceptance = task_dir / "acceptance.sh"

    if not task_md.is_file() or task_md.stat().st_size == 0:
        errors.append(f"task.md missing or empty: {task_md}")
    if not setup.is_dir() or not any(setup.rglob("*.calr")):
        errors.append(f"setup/ missing or has no .calr files: {setup}")
    if not expected.is_dir() or not any(expected.iterdir()):
        errors.append(f"expected/ missing or empty: {expected}")
    if not acceptance.is_file():
        errors.append(f"acceptance.sh missing: {acceptance}")
        return errors

    # bash must be available. On Windows, `bash.exe` is the WSL shim;
    # prefer Git Bash when WSL is not installed.
    bash_path = _find_bash()
    if not bash_path:
        errors.append("bash not on PATH; cannot validate acceptance script")
        return errors

    # Make a working copy of expected/ so the script can't accidentally
    # mutate templates. Pass the script as a relative path (basename) and
    # the work dir via Path.as_posix() so Git Bash on Windows can locate
    # them (Git Bash does not understand Windows backslashed paths from
    # the `bash <path>` argv).
    with tempfile.TemporaryDirectory() as td:
        work_expected = Path(td) / "work_expected"
        shutil.copytree(expected, work_expected)
        cp = subprocess.run(
            [bash_path, acceptance.name, work_expected.as_posix()],
            capture_output=True, text=True, cwd=str(task_dir),
        )
        if cp.returncode != 0:
            errors.append(
                f"acceptance.sh expected/ FAIL (exit={cp.returncode}):\n"
                f"  stdout: {cp.stdout.strip()[-500:]}\n"
                f"  stderr: {cp.stderr.strip()[-500:]}"
            )
        # Now: acceptance must FAIL when given setup/ (task is non-trivial).
        work_setup = Path(td) / "work_setup"
        shutil.copytree(setup, work_setup)
        cp2 = subprocess.run(
            [bash_path, acceptance.name, work_setup.as_posix()],
            capture_output=True, text=True, cwd=str(task_dir),
        )
        if cp2.returncode == 0:
            errors.append(
                "acceptance.sh setup/ unexpectedly succeeded — task is "
                "trivial (already in expected state)"
            )

    return errors


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    g = p.add_mutually_exclusive_group(required=True)
    g.add_argument("task_dir", nargs="?", help="Single task directory.")
    g.add_argument("--all", dest="all_root",
                   help="Templates root; validate every immediate sub-dir.")
    args = p.parse_args(argv)

    if args.all_root:
        root = Path(args.all_root)
        if not root.is_dir():
            print(f"not a directory: {root}", file=sys.stderr)
            return 2
        tasks = sorted(p for p in root.iterdir() if p.is_dir())
        any_err = False
        for t in tasks:
            errs = validate_one(t)
            if errs:
                any_err = True
                print(f"FAIL {t.name}:")
                for e in errs:
                    print(f"  - {e}")
            else:
                print(f"OK   {t.name}")
        return 1 if any_err else 0

    errs = validate_one(Path(args.task_dir))
    if errs:
        for e in errs:
            print(f"FAIL: {e}", file=sys.stderr)
        return 1
    print("OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
