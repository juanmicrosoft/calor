#!/usr/bin/env python3
"""
verify_corpus.py — Tier 2 corpus-wide verification driver (v6 §3.2).

Repaired by #1241; all checks use the checkout-built compiler and tracked
fixtures only. 1. Tier 1 extended: samples/ + tests/ against
eng/tier2-fixture-expectations.json (known failures keep Tier 2 red;
not an AST round trip). 2. Migrator dry run. 3. Token-delta counterfactual
(informational). 4. Migrator revert round trip (source bytes, not AST).
Removed: a `dotnet test` run on the `DiagnosticSnapshot` category trait,
which selected zero tests.

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
sys.path.insert(0, str(SCRIPTS))
from verify_phase1 import status_of  # noqa: E402
SAMPLES = REPO_ROOT / "samples"
TESTS = REPO_ROOT / "tests"


def run_step(name: str, cmd: list[str]) -> int:
    start = time.monotonic()
    print(f"\n=== {name} ===")
    print(f"$ {' '.join(cmd)}", flush=True)
    cp = subprocess.run(cmd, cwd=REPO_ROOT)
    elapsed = time.monotonic() - start
    print(f"--- {name}: {status_of(cp.returncode)} "
          f"({elapsed:.1f}s)", flush=True)
    return cp.returncode


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--report", help="Fixture-check JSON report path.")
    p.add_argument("--budget-seconds", type=int, default=30 * 60)
    args = p.parse_args(argv)

    py = sys.executable
    start = time.monotonic()
    failures: list[str] = []

    if run_step(
        "Tier 1 (extended)",
        [py, str(SCRIPTS / "verify_phase1.py"), "--corpus", "all",
         *(["--report", args.report] if args.report else [])],
    ) != 0:
        failures.append("Tier 1 (extended)")

    if run_step(
        "migrator_corpus_dryrun",
        [py, str(SCRIPTS / "migrator_corpus_dryrun.py"), str(TESTS)],
    ) != 0:
        failures.append("migrator_corpus_dryrun")

    run_step(
        "token_delta_corpus (informational)",
        [py, str(SCRIPTS / "token_delta_corpus.py"), str(TESTS)],
    )

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
