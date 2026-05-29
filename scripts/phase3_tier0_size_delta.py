#!/usr/bin/env python3
"""
phase3_tier0_size_delta.py — cheapest possible Phase 3 directional signal.

Measures the source-size delta you would get by switching from closer-mode
to indent-mode on a real Calor corpus. This is NOT a validation of H1
(agent pass rate); it's a proxy for "is the change even directionally
plausible to help" — if removing closers reduces source by < 5%, the
hypothesised LLM-context savings are unlikely to materialise into agent
performance improvements; if it's > 15%, there's at least mechanical
headroom for H1 to plausibly hold.

For each .calr file in the corpus, compute:
  - Original: lines, bytes, tokens (regex count of § / §/ markers,
    identifiers, literals, operators — a coarse proxy)
  - Indent-mode-simulated: same metrics after stripping pure-closer lines
    (`§/X` or `§/X{...}` on a line by itself, possibly preceded by
    indentation and optionally followed by a comment).

Reports median + p90 + tail deltas across the corpus.

Usage:
  python scripts/phase3_tier0_size_delta.py [--corpus DIR ...]
    [--json OUT.json] [--md OUT.md] [--per-file]
"""

from __future__ import annotations

import argparse
import json
import re
import statistics
import sys
from pathlib import Path

# A pure-closer line: optional leading indentation, then a single closer
# marker `§/X` or `§/X{...}`, then optional trailing whitespace and
# optional comment, then end of line.
CLOSER_LINE_RE = re.compile(
    r"^\s*§/[A-Z][A-Z0-9_]*(?:\{[^}]*\})?\s*(//.*)?$"
)

# Coarse token count: every § marker, every {..} attribute group, every
# identifier-ish run, every operator. Good enough for a proxy.
TOKEN_RE = re.compile(
    r"§/?[A-Z][A-Z0-9_]*|\{[^}]*\}|[A-Za-z_][A-Za-z0-9_]*|\"[^\"]*\"|[+\-*/<>=!&|^%]+|[(){}\[\],:;.]"
)


def measure(source: str) -> dict[str, int]:
    lines = source.count("\n") + (0 if source.endswith("\n") else 1)
    return {
        "lines": lines,
        "bytes": len(source.encode("utf-8")),
        "tokens": len(TOKEN_RE.findall(source)),
    }


def strip_closer_lines(source: str) -> tuple[str, int]:
    """Return (stripped_source, n_removed_closer_lines)."""
    kept: list[str] = []
    removed = 0
    for line in source.splitlines(keepends=True):
        if CLOSER_LINE_RE.match(line.rstrip("\r\n")):
            removed += 1
            continue
        kept.append(line)
    return "".join(kept), removed


def percentile(xs: list[float], p: float) -> float:
    if not xs:
        return 0.0
    s = sorted(xs)
    k = (len(s) - 1) * p
    f = int(k)
    c = min(f + 1, len(s) - 1)
    if f == c:
        return s[f]
    return s[f] + (s[c] - s[f]) * (k - f)


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--corpus", action="append", default=[],
                   help="Directory to scan recursively for .calr files (can repeat)")
    p.add_argument("--json", type=Path, help="Write JSON report")
    p.add_argument("--md", type=Path, help="Write Markdown report")
    p.add_argument("--per-file", action="store_true", help="Include per-file rows")
    p.add_argument("--limit", type=int, default=0, help="Cap files (debug)")
    args = p.parse_args()

    if not args.corpus:
        args.corpus = ["samples", "tests"]

    root = Path.cwd()
    files: list[Path] = []
    for c in args.corpus:
        cdir = root / c
        if not cdir.exists():
            print(f"warning: corpus dir not found: {cdir}", file=sys.stderr)
            continue
        files.extend(cdir.rglob("*.calr"))
    files.sort()
    if args.limit > 0:
        files = files[: args.limit]

    if not files:
        print("no .calr files found", file=sys.stderr)
        return 2

    rows: list[dict] = []
    deltas_lines_pct: list[float] = []
    deltas_bytes_pct: list[float] = []
    deltas_tokens_pct: list[float] = []
    closer_lines_per_file: list[int] = []

    for fp in files:
        try:
            src = fp.read_text(encoding="utf-8")
        except Exception as e:
            print(f"skip {fp}: {e}", file=sys.stderr)
            continue
        orig = measure(src)
        stripped, n_removed = strip_closer_lines(src)
        indent = measure(stripped)

        if orig["lines"] == 0:
            continue
        d_lines = (orig["lines"] - indent["lines"]) / orig["lines"] * 100.0
        d_bytes = (orig["bytes"] - indent["bytes"]) / orig["bytes"] * 100.0
        d_tokens = (orig["tokens"] - indent["tokens"]) / max(orig["tokens"], 1) * 100.0
        deltas_lines_pct.append(d_lines)
        deltas_bytes_pct.append(d_bytes)
        deltas_tokens_pct.append(d_tokens)
        closer_lines_per_file.append(n_removed)

        rows.append({
            "file": str(fp.relative_to(root)),
            "orig_lines": orig["lines"],
            "orig_bytes": orig["bytes"],
            "orig_tokens": orig["tokens"],
            "indent_lines": indent["lines"],
            "indent_bytes": indent["bytes"],
            "indent_tokens": indent["tokens"],
            "closer_lines_removed": n_removed,
            "lines_reduction_pct": round(d_lines, 2),
            "bytes_reduction_pct": round(d_bytes, 2),
            "tokens_reduction_pct": round(d_tokens, 2),
        })

    n = len(rows)
    summary = {
        "corpus_size": n,
        "total_orig_lines": sum(r["orig_lines"] for r in rows),
        "total_orig_bytes": sum(r["orig_bytes"] for r in rows),
        "total_orig_tokens": sum(r["orig_tokens"] for r in rows),
        "total_closer_lines_removable": sum(closer_lines_per_file),
        "total_indent_lines": sum(r["indent_lines"] for r in rows),
        "total_indent_bytes": sum(r["indent_bytes"] for r in rows),
        "total_indent_tokens": sum(r["indent_tokens"] for r in rows),
        "aggregate_lines_reduction_pct": round(
            (sum(r["orig_lines"] for r in rows) - sum(r["indent_lines"] for r in rows))
            / max(sum(r["orig_lines"] for r in rows), 1) * 100.0, 2),
        "aggregate_bytes_reduction_pct": round(
            (sum(r["orig_bytes"] for r in rows) - sum(r["indent_bytes"] for r in rows))
            / max(sum(r["orig_bytes"] for r in rows), 1) * 100.0, 2),
        "aggregate_tokens_reduction_pct": round(
            (sum(r["orig_tokens"] for r in rows) - sum(r["indent_tokens"] for r in rows))
            / max(sum(r["orig_tokens"] for r in rows), 1) * 100.0, 2),
        "median_lines_reduction_pct": round(statistics.median(deltas_lines_pct), 2),
        "median_bytes_reduction_pct": round(statistics.median(deltas_bytes_pct), 2),
        "median_tokens_reduction_pct": round(statistics.median(deltas_tokens_pct), 2),
        "p10_lines_reduction_pct": round(percentile(deltas_lines_pct, 0.10), 2),
        "p90_lines_reduction_pct": round(percentile(deltas_lines_pct, 0.90), 2),
        "p10_bytes_reduction_pct": round(percentile(deltas_bytes_pct, 0.10), 2),
        "p90_bytes_reduction_pct": round(percentile(deltas_bytes_pct, 0.90), 2),
        "p10_tokens_reduction_pct": round(percentile(deltas_tokens_pct, 0.10), 2),
        "p90_tokens_reduction_pct": round(percentile(deltas_tokens_pct, 0.90), 2),
        "files_with_zero_reduction": sum(1 for d in deltas_lines_pct if d == 0.0),
        "files_with_ge_10pct_lines_reduction": sum(1 for d in deltas_lines_pct if d >= 10.0),
        "files_with_ge_20pct_lines_reduction": sum(1 for d in deltas_lines_pct if d >= 20.0),
    }

    output = {"summary": summary}
    if args.per_file:
        output["files"] = rows

    if args.json:
        args.json.write_text(json.dumps(output, indent=2), encoding="utf-8")
    if args.md:
        lines = []
        lines.append("# Phase 3 Tier 0 — source-size delta (closer-mode vs indent-mode)\n")
        lines.append(f"**Corpus:** {summary['corpus_size']} `.calr` files\n")
        lines.append("## Aggregate (sum across corpus)\n")
        lines.append("| Metric | Closer-mode | Indent-mode | Reduction |")
        lines.append("|---|---:|---:|---:|")
        lines.append(f"| Lines | {summary['total_orig_lines']:,} | {summary['total_indent_lines']:,} | **{summary['aggregate_lines_reduction_pct']}%** |")
        lines.append(f"| Bytes | {summary['total_orig_bytes']:,} | {summary['total_indent_bytes']:,} | **{summary['aggregate_bytes_reduction_pct']}%** |")
        lines.append(f"| Tokens (proxy) | {summary['total_orig_tokens']:,} | {summary['total_indent_tokens']:,} | **{summary['aggregate_tokens_reduction_pct']}%** |")
        lines.append(f"\nTotal closer lines removable: **{summary['total_closer_lines_removable']:,}**\n")
        lines.append("## Per-file distribution\n")
        lines.append("| Metric | p10 | median | p90 |")
        lines.append("|---|---:|---:|---:|")
        lines.append(f"| Lines reduction % | {summary['p10_lines_reduction_pct']} | {summary['median_lines_reduction_pct']} | {summary['p90_lines_reduction_pct']} |")
        lines.append(f"| Bytes reduction % | {summary['p10_bytes_reduction_pct']} | {summary['median_bytes_reduction_pct']} | {summary['p90_bytes_reduction_pct']} |")
        lines.append(f"| Tokens reduction % | {summary['p10_tokens_reduction_pct']} | {summary['median_tokens_reduction_pct']} | {summary['p90_tokens_reduction_pct']} |")
        lines.append(f"\n- Files with 0% reduction: **{summary['files_with_zero_reduction']}** / {summary['corpus_size']}")
        lines.append(f"- Files with ≥10% lines reduction: **{summary['files_with_ge_10pct_lines_reduction']}** / {summary['corpus_size']}")
        lines.append(f"- Files with ≥20% lines reduction: **{summary['files_with_ge_20pct_lines_reduction']}** / {summary['corpus_size']}")
        args.md.write_text("\n".join(lines) + "\n", encoding="utf-8")

    print(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
