#!/usr/bin/env python3
"""
verify_corpus.py — Tier 2 corpus-wide verification driver (v6 §3.2).

Re-runs Tier 1 over the full corpus, plus corpus-specific checks:

    1. Tier 1 in extended mode (samples/ + tests/).
    2. Migrator corpus dry-run (no-op until PR-1c).
    3. Aggregate token-delta check (against RFC v5 §16.F band).
    4. Diagnostic snapshot tests via `dotnet test`.
    5. Migrator round-trip (no-op until PR-1c).

Runtime target: < 30 minutes on a developer machine.

Usage:
    python3 scripts/verify_corpus.py
"""

from __future__ import annotations

import argparse
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REPO_ROOT / "scripts"
SAMPLES = REPO_ROOT / "samples"
TESTS = REPO_ROOT / "tests"


def run_step(name: str, cmd: list[str]) -> int:
    start = time.monotonic()
    print(f"\n=== {name} ===")
    print(f"$ {' '.join(cmd)}")
    cp = subprocess.run(cmd, cwd=REPO_ROOT)
    elapsed = time.monotonic() - start
    print(f"--- {name}: {'OK' if cp.returncode == 0 else 'FAIL'} "
          f"({elapsed:.1f}s)")
    return cp.returncode


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--skip-dotnet", action="store_true")
    p.add_argument("--budget-seconds", type=int, default=30 * 60)
    args = p.parse_args(argv)

    py = sys.executable
    start = time.monotonic()
    failures: list[str] = []

    if run_step(
        "Tier 1 (extended)",
        [py, str(SCRIPTS / "verify_phase1.py"), "--corpus", "all",
         *(["--skip-dotnet"] if args.skip_dotnet else [])],
    ) != 0:
        failures.append("Tier 1 (extended)")

    if run_step(
        "migrator_corpus_dryrun",
        [py, str(SCRIPTS / "migrator_corpus_dryrun.py"), str(TESTS)],
    ) != 0:
        failures.append("migrator_corpus_dryrun")

    if run_step(
        "token_delta_corpus",
        [py, str(SCRIPTS / "token_delta_corpus.py"), str(TESTS)],
    ) != 0:
        failures.append("token_delta_corpus")

    if not args.skip_dotnet:
        if run_step(
            "dotnet test (DiagnosticSnapshot category)",
            ["dotnet", "test", "-c", "Release",
             "--filter", "Category=DiagnosticSnapshot",
             "--nologo", "--verbosity", "minimal"],
        ) != 0:
            failures.append("DiagnosticSnapshot tests")

    if run_step(
        "migrator_revert_roundtrip",
        [py, str(SCRIPTS / "migrator_revert_roundtrip.py"), str(TESTS)],
    ) != 0:
        failures.append("migrator_revert_roundtrip")

    elapsed = time.monotonic() - start
    print(f"\n=== Tier 2 total: {elapsed:.1f}s "
          f"(budget {args.budget_seconds}s) ===")
    if failures:
        print(f"Tier 2 FAIL: {', '.join(failures)}", file=sys.stderr)
        return 1
    print("Tier 2 OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
