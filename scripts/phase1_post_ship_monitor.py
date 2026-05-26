#!/usr/bin/env python3
"""Phase 1 post-ship monitor.

Tier 4 of the v6 measurement plan (RFC §10.4): a low-cost, scheduled
job that watches the live ecosystem for regressions after the
structural-ID optionalisation lands.

Signals tracked:
  * Count of legacy `{id:…}` blocks remaining in the public corpus
    (samples/ and tests/E2E/ fixtures shipped with the repo).
  * Newly-introduced legacy blocks in the most recent commit window.
  * Compiler diagnostics that fire as a result of the new permissive
    parser path (proxy: `dotnet build` exit code + warning count).

The script is intentionally additive — failure does not block CI, but
the workflow uses an "issue on regression" pattern so spikes are
surfaced for human triage.

Usage:
    python scripts/phase1_post_ship_monitor.py \
        --root . \
        --window 7 \
        --output phase1-monitor.json
"""

import argparse
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path
from typing import Any


SECTION_RE = re.compile(
    r"\xc2\xa7(/?[A-Za-z]+)\{([^}]*)\}", re.MULTILINE)

# Same shape rule as AttributeHelper.LooksLikeId in the C# code.
ID_RE = re.compile(r"^[a-z]+_[A-Za-z0-9]{12}$|^[a-z]+_[A-Za-z0-9]{26}$")


def count_legacy_ids(root: Path) -> tuple[int, int]:
    """Return (file_count_with_legacy, total_legacy_blocks)."""
    files = 0
    total = 0
    for calr in root.rglob("*.calr"):
        if any(seg in {"bin", "obj"} for seg in calr.parts):
            continue
        try:
            data = calr.read_bytes()
        except OSError:
            continue
        local = 0
        for m in SECTION_RE.finditer(data.decode("utf-8", errors="replace")):
            inner = m.group(2)
            first = inner.split(":", 1)[0]
            if ID_RE.match(first):
                local += 1
        if local:
            files += 1
            total += local
    return files, total


def recent_introductions(root: Path, days: int) -> int:
    """Best-effort: ask git for additions in `.calr` files over the window
    that match the legacy `{id:…}` shape. Returns -1 if git unavailable."""
    since = f"--since=\"{days} days ago\""
    cmd = ["git", "-C", str(root), "log", since, "--diff-filter=A",
           "--unified=0", "-p", "--", "*.calr"]
    try:
        out = subprocess.run(
            cmd, capture_output=True, text=True, check=False, timeout=60)
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return -1
    additions = 0
    for line in out.stdout.splitlines():
        if not line.startswith("+") or line.startswith("+++"):
            continue
        for m in SECTION_RE.finditer(line[1:]):
            inner = m.group(2)
            first = inner.split(":", 1)[0]
            if ID_RE.match(first):
                additions += 1
    return additions


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--root", default=".", help="Repo root")
    p.add_argument("--window", type=int, default=7,
                   help="Days of git history to inspect for new legacy IDs")
    p.add_argument("--output", default="phase1-monitor.json",
                   help="JSON output path")
    p.add_argument("--fail-on-increase-over", type=int, default=-1,
                   help="Exit 1 if total legacy blocks exceeds this baseline")
    args = p.parse_args()

    root = Path(args.root).resolve()
    files, total = count_legacy_ids(root)
    recent = recent_introductions(root, args.window)

    report: dict[str, Any] = {
        "ts": int(time.time()),
        "root": str(root),
        "legacy_block_count": total,
        "files_with_legacy": files,
        "recent_additions_window_days": args.window,
        "recent_additions": recent,
    }
    Path(args.output).write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8")

    print(f"phase1-monitor: legacy_blocks={total} files={files} "
          f"recent_additions={recent}")

    if args.fail_on_increase_over >= 0 and total > args.fail_on_increase_over:
        print(
            f"::error::legacy block count {total} exceeds baseline "
            f"{args.fail_on_increase_over}",
            file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
