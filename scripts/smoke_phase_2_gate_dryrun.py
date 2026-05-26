#!/usr/bin/env python3
"""
smoke_phase_2_gate_dryrun.py — Exercise the Tier 3 gate end-to-end
without spending LLM budget.

Runs ``run_phase_2_gate.py --dry-run --simulate gate-pass`` and asserts
that ``analyze_gate_results.py`` returns exit 0 (gate passes), then runs
``--simulate gate-fail`` and asserts exit 1 (gate fails). This proves
the whole pipeline (collect → dispatch → JSONL → analyze → markdown
report → pass/fail) wires up correctly.

Exits 0 on success, 1 on any unexpected outcome.
"""

from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
GATE = REPO_ROOT / "scripts" / "run_phase_2_gate.py"
ANALYZE = REPO_ROOT / "scripts" / "analyze_gate_results.py"


def _run(cmd: list[str]) -> int:
    print(f"+ {' '.join(cmd)}")
    return subprocess.call(cmd, cwd=str(REPO_ROOT))


def _exercise(simulate: str, expected_exit: int, tmp: Path) -> bool:
    out_dir = tmp / f"dryrun-{simulate}"
    rc = _run([
        sys.executable, str(GATE),
        "--dry-run", "--simulate", simulate,
        "--output-dir", str(out_dir),
        "--skip-model-ping",
        "--seeds", "1,2,3",
    ])
    if rc != 0:
        print(f"smoke: run_phase_2_gate.py exited {rc} for {simulate}",
              file=sys.stderr)
        return False
    rc = _run([
        sys.executable, str(ANALYZE),
        "--runs", str(out_dir / "runs.jsonl"),
        "--output", str(out_dir / "report.md"),
    ])
    if rc != expected_exit:
        print(
            f"smoke: analyze_gate_results.py exited {rc}; expected "
            f"{expected_exit} for simulate={simulate}",
            file=sys.stderr,
        )
        return False
    print(f"smoke: simulate={simulate} -> exit {rc} (expected {expected_exit}) OK")
    return True


def main() -> int:
    tmp = Path(tempfile.mkdtemp(prefix="calor-gate-dryrun-"))
    try:
        ok = (
            _exercise("gate-pass", 0, tmp)
            and _exercise("gate-fail", 1, tmp)
        )
        if not ok:
            return 1
        print("smoke: phase-2 gate dry-run pipeline OK")
        return 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
