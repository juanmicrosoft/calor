#!/usr/bin/env python3
"""
token_delta_corpus.py — Tier 2 aggregate token-delta check.

Walks every `.calr` file under a directory tree and either:
- Reports the aggregate counterfactual delta (Phase 0: no migrator yet),
  or
- If `--logs-dir` is supplied, reports the actual delta between original
  files and their migrated counterparts under that directory.

Compares aggregate delta to RFC v5 §16.F expected value
(9.67 × N_ids ± 20% provisional tolerance band per v6 §3.2).

Exit codes:
    0  aggregate delta within band
    1  aggregate delta out of band
    2  bad arguments / dir missing

Usage:
    python3 scripts/token_delta_corpus.py <root-dir> \\
        [--migrated-dir <root-dir-with-migrated-files>]
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

# Delegate token-counting to token_delta_spot's helpers.
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from token_delta_spot import count_id_blocks, count_tokens, TOKENS_PER_ID_MEAN  # noqa: E402


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("root", help="Root directory to walk.")
    p.add_argument("--migrated-dir", default=None,
                   help="Parallel directory of migrated files (optional).")
    p.add_argument("--tolerance", type=float, default=0.20,
                   help="Provisional tolerance band (default 0.20).")
    args = p.parse_args(argv)

    root = Path(args.root)
    if not root.is_dir():
        print(f"token_delta_corpus: not a directory: {root}", file=sys.stderr)
        return 2
    migrated_root = Path(args.migrated_dir) if args.migrated_dir else None

    total_pre = 0
    total_post = 0
    total_ids = 0
    n_files = 0

    for f in sorted(root.rglob("*.calr")):
        n_files += 1
        pre_text = f.read_text(encoding="utf-8")
        total_pre += count_tokens(pre_text)
        total_ids += count_id_blocks(pre_text)
        if migrated_root:
            rel = f.relative_to(root)
            m = migrated_root / rel
            if m.is_file():
                total_post += count_tokens(m.read_text(encoding="utf-8"))
            else:
                total_post += count_tokens(pre_text)  # not migrated.

    expected = int(round(total_ids * TOKENS_PER_ID_MEAN))
    print(f"files_scanned:       {n_files}")
    print(f"total_id_blocks:     {total_ids}")
    print(f"aggregate_pre_tok:   {total_pre}")

    if migrated_root:
        delta = total_pre - total_post
        ratio = delta / expected if expected else 0.0
        in_band = (
            (1.0 - args.tolerance) <= ratio <= (1.0 + args.tolerance)
            if expected > 0 else delta == 0
        )
        print(f"aggregate_post_tok:  {total_post}")
        print(f"aggregate_delta:     {delta}")
        print(f"expected_delta:      {expected}")
        print(f"ratio:               {ratio:.3f}")
        print(f"tolerance_band:      ±{args.tolerance*100:.0f}%")
        print(f"in_band:             {in_band}")
        return 0 if in_band else 1

    print(f"counterfactual_delta: {expected}")
    print(f"counterfactual_post:  {total_pre - expected}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
