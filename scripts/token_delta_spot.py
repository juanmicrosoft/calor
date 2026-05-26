#!/usr/bin/env python3
"""
token_delta_spot.py — Single-file token-delta spot check.

Reports the token-count delta (post-migration - pre-migration) for a
single `.calr` file. In Phase 0 (no migrator yet), reports the current
token count and the COUNTERFACTUAL delta that would result from
dropping all `{id...}` blocks at the per-RFC v5 §16.F rate of
~9.67 tokens per ID.

Token counting uses tiktoken with the `cl100k_base` encoding if
available; otherwise falls back to a whitespace word-count
approximation with a documented inflation factor of ~1.3.

Exit codes:
    0  reported successfully
    2  bad arguments / file missing

Usage:
    python3 scripts/token_delta_spot.py <file.calr> \\
        [--migrated <migrated.calr>]
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

# RFC v5 §16.F: mean 9.67 tokens per ID, 95% CI (9.30, 10.04), N=100.
TOKENS_PER_ID_MEAN = 9.67

# Matches §X{id-block}. Captures the id-block (with curly braces) so we
# can count its length.
ID_BLOCK_RE = re.compile(r"§[A-Z]+(\{[^}]*\})", re.UNICODE)


def count_tokens(text: str) -> int:
    try:
        import tiktoken  # type: ignore

        enc = tiktoken.get_encoding("cl100k_base")
        return len(enc.encode(text))
    except Exception:
        # Fallback: whitespace tokens × 1.3.
        return int(len(text.split()) * 1.3)


def count_id_blocks(text: str) -> int:
    return sum(1 for _ in ID_BLOCK_RE.finditer(text))


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("file", help="Input .calr file.")
    p.add_argument("--migrated", default=None,
                   help="Path to the migrated counterpart (optional).")
    args = p.parse_args(argv)

    f = Path(args.file)
    if not f.is_file():
        print(f"token_delta_spot: file missing: {f}", file=sys.stderr)
        return 2

    pre_text = f.read_text(encoding="utf-8")
    pre_tokens = count_tokens(pre_text)
    n_ids = count_id_blocks(pre_text)

    if args.migrated:
        m = Path(args.migrated)
        if not m.is_file():
            print(f"token_delta_spot: migrated missing: {m}", file=sys.stderr)
            return 2
        post_tokens = count_tokens(m.read_text(encoding="utf-8"))
        delta = pre_tokens - post_tokens
        expected = int(round(n_ids * TOKENS_PER_ID_MEAN))
        print(f"file:            {f}")
        print(f"migrated:        {m}")
        print(f"pre_tokens:      {pre_tokens}")
        print(f"post_tokens:     {post_tokens}")
        print(f"delta_actual:    {delta}")
        print(f"delta_expected:  {expected} (n_ids={n_ids} × "
              f"{TOKENS_PER_ID_MEAN})")
        # Within +/-20% tolerance band (provisional per v6 §3.2).
        if expected == 0:
            ratio = float("inf") if delta != 0 else 1.0
        else:
            ratio = delta / expected
        in_band = 0.80 <= ratio <= 1.20 if expected > 0 else delta == 0
        print(f"in_provisional_20pct_band: {in_band}")
        return 0

    expected = int(round(n_ids * TOKENS_PER_ID_MEAN))
    print(f"file:                            {f}")
    print(f"current_tokens:                  {pre_tokens}")
    print(f"id_blocks_found:                 {n_ids}")
    print(f"counterfactual_drop_delta:       {expected} tokens")
    print(f"counterfactual_post_tokens:      {pre_tokens - expected}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
