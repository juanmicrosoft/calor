#!/usr/bin/env python3
"""
ast_roundtrip_check.py — Per-PR AST round-trip check.

For each `.calr` fixture in a directory, invoke the `calor` compiler to
parse → emit → parse. Compilation success is the Phase-0 proxy for
AST-equality (a richer AST-diff would require a new compiler CLI
command that is not in scope for PR-0d).

Exit codes:
    0  all fixtures round-trip cleanly
    1  one or more fixtures failed to round-trip
    2  bad arguments / dir missing

Usage:
    python3 scripts/ast_roundtrip_check.py <dir>
    python3 scripts/ast_roundtrip_check.py --self-test
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CALOR_PROJECT = REPO_ROOT / "src" / "Calor.Compiler"


def calor_command() -> list[str]:
    """Resolve `calor` invocation. Prefer the global tool when available,
    fall back to `dotnet run --project src/Calor.Compiler --`."""
    # Check the installed global tool first.
    from shutil import which
    if which("calor"):
        return ["calor"]
    return [
        "dotnet", "run", "--project", str(CALOR_PROJECT), "-c", "Release",
        "--no-build", "--",
    ]


def compile_one(calor_cmd: list[str], src: Path, out_dir: Path) -> tuple[int, str]:
    out_cs = out_dir / (src.stem + ".g.cs")
    cmd = calor_cmd + ["--input", str(src), "--output", str(out_cs)]
    cp = subprocess.run(cmd, capture_output=True, text=True, cwd=REPO_ROOT)
    return cp.returncode, (cp.stdout + cp.stderr)[-2000:]


def check_dir(directory: Path) -> int:
    if not directory.is_dir():
        print(f"ast_roundtrip: not a directory: {directory}", file=sys.stderr)
        return 2

    calor_cmd = calor_command()
    fixtures = sorted(directory.rglob("*.calr"))
    if not fixtures:
        print(f"ast_roundtrip: no .calr files under {directory}")
        return 0

    print(f"ast_roundtrip: checking {len(fixtures)} fixture(s) under "
          f"{directory}")
    failures: list[tuple[Path, str]] = []
    with tempfile.TemporaryDirectory() as td:
        out_dir = Path(td)
        for i, src in enumerate(fixtures, 1):
            rc, log_tail = compile_one(calor_cmd, src, out_dir)
            if rc != 0:
                failures.append((src, log_tail))
                print(f"  [{i}/{len(fixtures)}] FAIL {src}")
            else:
                if i % 50 == 0 or i == len(fixtures):
                    print(f"  [{i}/{len(fixtures)}] OK so far")

    if failures:
        print(f"\nast_roundtrip: {len(failures)} failure(s):", file=sys.stderr)
        for src, log in failures[:10]:
            print(f"  - {src}", file=sys.stderr)
            for line in log.splitlines()[-5:]:
                print(f"      {line}", file=sys.stderr)
        if len(failures) > 10:
            print(f"  ... and {len(failures) - 10} more", file=sys.stderr)
        return 1
    print("ast_roundtrip: all fixtures OK")
    return 0


def self_test() -> int:
    """Compile a synthetic positive fixture and a synthetic negative one."""
    failures = 0
    with tempfile.TemporaryDirectory() as td:
        td_path = Path(td)
        calor_cmd = calor_command()

        positive = td_path / "positive.calr"
        positive.write_text(
            "§M{m_pos:Pos}\n"
            "  §F{f_pos1:Echo:pub}\n"
            "    §I{i32:x}\n"
            "    §O{i32}\n"
            "    §R x\n",
            encoding="utf-8",
        )
        rc, log = compile_one(calor_cmd, positive, td_path)
        if rc != 0:
            print("ast_roundtrip self-test: positive case FAIL", file=sys.stderr)
            print(log, file=sys.stderr)
            failures += 1
        else:
            print("ast_roundtrip self-test: positive PASS")

        negative = td_path / "negative.calr"
        # Intentionally malformed.
        negative.write_text("§M{m_neg:Neg}\n§Q (unterminated\n", encoding="utf-8")
        rc, log = compile_one(calor_cmd, negative, td_path)
        if rc == 0:
            print("ast_roundtrip self-test: negative case unexpectedly "
                  "succeeded", file=sys.stderr)
            failures += 1
        else:
            print("ast_roundtrip self-test: negative PASS (rejected as "
                  "expected)")
    return 0 if failures == 0 else 1


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("directory", nargs="?", help="Directory to scan.")
    p.add_argument("--self-test", action="store_true")
    args = p.parse_args(argv)

    if args.self_test:
        return self_test()
    if not args.directory:
        p.error("directory is required (or use --self-test)")
    return check_dir(Path(args.directory))


if __name__ == "__main__":
    sys.exit(main())
