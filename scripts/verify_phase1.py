#!/usr/bin/env python3
"""
verify_phase1.py — Tier 1 self-verification driver (v6 §3.1).

Runs the Tier 1 checks in order. Designed to complete in < 60 seconds
on a developer machine (per v6 §3.1.a). On budget overrun, the
remediation order is §3.6.

Components (repaired by #1241):
    1. fixture compile check on samples/ (default) or samples/ + tests/
       (extended): the checkout-built compiler compiles every tracked
       fixture once against eng/tier2-fixture-expectations.json. This is
       not an AST round trip; no Tier 1 or Tier 2 check performs one.
    2. token-delta spot check on a single fixture (informational)

Removed by #1241 because they established nothing: a `dotnet test` run
filtered on the `Unit` category trait (no test carries it, so it selected
zero tests) and a byte-preservation "identity check" comparing a file
with itself. A checker exit of 3 is TimeoutOrUnavailable, never OK.

Exit codes:
    0  all checks PASS
    1  one or more checks FAIL
    2  bad arguments

Usage:
    python3 scripts/verify_phase1.py
    python3 scripts/verify_phase1.py --corpus all       # extended
    python3 scripts/verify_phase1.py --report fixtures.json
    python3 scripts/verify_phase1.py --self-test
"""

from __future__ import annotations

import argparse
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REPO_ROOT / "scripts"


def status_of(rc: int) -> str:
    return {0: "OK", 3: "TimeoutOrUnavailable"}.get(rc, "FAIL")


def run_step(name: str, cmd: list[str], cwd: Path = REPO_ROOT) -> int:
    start = time.monotonic()
    print(f"\n=== {name} ===")
    print(f"$ {' '.join(cmd)}", flush=True)
    cp = subprocess.run(cmd, cwd=cwd)
    elapsed = time.monotonic() - start
    print(f"--- {name}: {status_of(cp.returncode)} "
          f"({elapsed:.1f}s)", flush=True)
    return cp.returncode


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--corpus", default="default",
                   choices=("default", "all"),
                   help="default = samples/ only; all = samples/ + tests/.")
    p.add_argument("--report", help="Fixture-check JSON report path.")
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
            "fixture_compile_check --self-test",
            [py, str(SCRIPTS / "fixture_compile_check.py"), "--self-test"],
        ) != 0:
            failures.append("fixture_compile_check self-test")
    else:
        roots = ["samples"] + (["tests"] if args.corpus == "all" else [])
        cmd = [py, str(SCRIPTS / "fixture_compile_check.py")]
        cmd += [a for r in roots for a in ("--root", r)]
        cmd += ["--report", args.report] if args.report else []
        if run_step(
            f"fixture_compile_check ({' + '.join(roots)}; compiled once, "
            "no AST round trip)", cmd,
        ) != 0:
            failures.append("fixture_compile_check")
        # Token-delta spot check is informational; not gating.
        sys.path.insert(0, str(SCRIPTS))
        import checkout_compiler
        tracked = checkout_compiler.tracked_calr(REPO_ROOT, ["samples"])
        if tracked:
            run_step(
                "token_delta_spot (informational)",
                [py, str(SCRIPTS / "token_delta_spot.py"),
                 str(REPO_ROOT / tracked[0])],
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
