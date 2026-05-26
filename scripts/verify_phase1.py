#!/usr/bin/env python3
"""
verify_phase1.py — Tier 1 self-verification driver (v6 §3.1).

Runs the Tier 1 checks in order. Designed to complete in < 60 seconds
on a developer machine (per v6 §3.1.a). On budget overrun, the
remediation order is §3.6.

Components:
    1. dotnet test --filter Category=Unit (existing, optional)
    2. byte-preservation on samples/
    3. AST round-trip on samples/ (default) or tests/ (extended)
    4. token-delta spot check on a single fixture (informational)

Exit codes:
    0  all checks PASS
    1  one or more checks FAIL
    2  bad arguments

Usage:
    python3 scripts/verify_phase1.py
    python3 scripts/verify_phase1.py --corpus all       # extended
    python3 scripts/verify_phase1.py --skip-dotnet      # CI shortcut
    python3 scripts/verify_phase1.py --self-test
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REPO_ROOT / "scripts"
SAMPLES = REPO_ROOT / "samples"
TESTS = REPO_ROOT / "tests"


def run_step(name: str, cmd: list[str], cwd: Path = REPO_ROOT) -> int:
    start = time.monotonic()
    print(f"\n=== {name} ===")
    print(f"$ {' '.join(cmd)}")
    cp = subprocess.run(cmd, cwd=cwd)
    elapsed = time.monotonic() - start
    print(f"--- {name}: {'OK' if cp.returncode == 0 else 'FAIL'} "
          f"({elapsed:.1f}s)")
    return cp.returncode


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--corpus", default="default",
                   choices=("default", "all"),
                   help="default = samples/ only; all = samples/ + tests/.")
    p.add_argument("--skip-dotnet", action="store_true",
                   help="Skip `dotnet test` (CI uses separate jobs).")
    p.add_argument("--self-test", action="store_true",
                   help="Run each component's --self-test mode.")
    p.add_argument("--budget-seconds", type=int, default=60,
                   help="Wall-clock budget (v6 §3.1.a, default 60).")
    args = p.parse_args(argv)

    py = sys.executable
    start = time.monotonic()
    failures: list[str] = []

    if args.self_test:
        if run_step(
            "byte_preservation --self-test",
            [py, str(SCRIPTS / "byte_preservation_check.py"), "--self-test"],
        ) != 0:
            failures.append("byte_preservation self-test")
        if run_step(
            "ast_roundtrip --self-test",
            [py, str(SCRIPTS / "ast_roundtrip_check.py"), "--self-test"],
        ) != 0:
            failures.append("ast_roundtrip self-test")
    else:
        if not args.skip_dotnet:
            # `Category=Unit` is the conventional filter; if no unit-marked
            # tests exist, the runner returns 0 trivially.
            if run_step(
                "dotnet test (Category=Unit)",
                ["dotnet", "test", "-c", "Release",
                 "--filter", "Category=Unit",
                 "--nologo", "--verbosity", "minimal"],
            ) != 0:
                failures.append("dotnet test")
        if run_step(
            "byte_preservation (identity check on samples/)",
            [py, str(SCRIPTS / "byte_preservation_check.py"),
             str(SAMPLES / "Contracts" / "Calculator.calr")
                if (SAMPLES / "Contracts" / "Calculator.calr").exists()
                else str(next(SAMPLES.rglob("*.calr"), SAMPLES))
                if any(SAMPLES.rglob("*.calr")) else str(SAMPLES),
             str(SAMPLES / "Contracts" / "Calculator.calr")
                if (SAMPLES / "Contracts" / "Calculator.calr").exists()
                else str(next(SAMPLES.rglob("*.calr"), SAMPLES))
                if any(SAMPLES.rglob("*.calr")) else str(SAMPLES)],
        ) != 0:
            failures.append("byte_preservation identity")
        ast_target = SAMPLES if args.corpus == "default" else TESTS
        if run_step(
            f"ast_roundtrip (target={ast_target.name})",
            [py, str(SCRIPTS / "ast_roundtrip_check.py"), str(ast_target)],
        ) != 0:
            failures.append("ast_roundtrip")
        # Token-delta spot check is informational; not gating.
        any_calr = next(SAMPLES.rglob("*.calr"), None)
        if any_calr:
            run_step(
                "token_delta_spot (informational)",
                [py, str(SCRIPTS / "token_delta_spot.py"), str(any_calr)],
            )

    elapsed = time.monotonic() - start
    print(f"\n=== Tier 1 total: {elapsed:.1f}s "
          f"(budget {args.budget_seconds}s) ===")
    if elapsed > args.budget_seconds:
        print(f"WARNING: Tier 1 exceeded budget by "
              f"{elapsed - args.budget_seconds:.1f}s. See v6 §3.6.",
              file=sys.stderr)
    if failures:
        print(f"\nTier 1 FAIL: {', '.join(failures)}", file=sys.stderr)
        return 1
    print("Tier 1 OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
