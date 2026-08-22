#!/usr/bin/env python3
"""
Measurement script for the v0.14 nullability S1 corpus baseline
(issue #875, F-1 ratchet at tests/Calor.Compiler.Tests/Binding/
BoundTypes/NullabilityBaselineTests.cs).

Walks samples/ and benchmarks/ for .calr files, counts every occurrence
of the §B{...:string} binding shape, and classifies each match by
initializer shape. Writes the result to
bench/phase0-agent-native/nullability-info-baseline.json.

Methodology matches the static grep that produced the original S1 count:

    grep -rn "§B{[^}]*:string}" samples/ benchmarks/  # 42 matches

Shape classification (inspected on the initializer that follows the
binding on the same source line):
  - LITERAL_STRING  : initializer starts with STR:"..."
  - CONCAT          : initializer starts with (+ ...)
  - CALL_CALOR      : initializer is (CALL <ident> ...) with no dot in
                      the callee — treated as a Calor-native function
  - CALL_INTEROP    : initializer is (CALL <ns>.<member> ...) — a
                      dotted callee, treated as C# interop
  - VARIABLE_REF    : anything else (bare identifier, etc.)

The script is deterministic: it sorts files by repo-relative path and
matches within a file in source order. It intentionally does NOT parse
the Calor source — the point is to be a stable static shape count that
is independent of compiler state and can be re-run on any checkout.

Usage:
    python3 bench/phase0-agent-native/nullability_s1_baseline.py
    python3 bench/phase0-agent-native/nullability_s1_baseline.py --check

--check prints the totals and exits non-zero if the count on disk does
not match the count in the existing baseline JSON. Without --check the
script rewrites the JSON in place.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# The regex mirrors StringBindRegex in NullabilityBaselineTests.cs so the
# script and the F-1 ratchet test see the same universe of bindings.
BIND_RE = re.compile(r"§B\{[^}]+:string\}")
# Captures the binding name and the tail of the line (the initializer).
BIND_DETAIL_RE = re.compile(r"§B\{~?(?P<name>[^:}]+):string\}\s*(?P<init>.*)$")
# CALL classification: (CALL <callee> ...)
CALL_TARGET_RE = re.compile(r"^\(CALL\s+(?P<target>[^\s)]+)")


def classify(initializer: str) -> str:
    init = initializer.strip()
    if init.startswith("STR:"):
        return "LITERAL_STRING"
    if init.startswith("(+ ") or init.startswith("(+\t"):
        return "CONCAT"
    call_match = CALL_TARGET_RE.match(init)
    if call_match:
        target = call_match.group("target")
        return "CALL_INTEROP" if "." in target else "CALL_CALOR"
    return "VARIABLE_REF"


def repo_root_from(script_path: Path) -> Path:
    # script lives at <repo>/bench/phase0-agent-native/nullability_s1_baseline.py
    return script_path.resolve().parents[2]


def enumerate_calr_files(repo: Path) -> list[Path]:
    files: list[Path] = []
    for root_name in ("samples", "benchmarks"):
        root = repo / root_name
        if not root.exists():
            continue
        files.extend(sorted(root.rglob("*.calr")))
    return files


def scan(repo: Path) -> dict:
    by_file: dict[str, list[dict]] = {}
    by_shape: dict[str, int] = {}
    total = 0

    for path in enumerate_calr_files(repo):
        rel = path.relative_to(repo).as_posix()
        text = path.read_text(encoding="utf-8", errors="replace")
        entries: list[dict] = []
        for lineno, line in enumerate(text.splitlines(), start=1):
            if not BIND_RE.search(line):
                continue
            # Multiple bindings on one line are extremely rare in Calor
            # (the shape is a statement, one per line) but handle it.
            for match in BIND_RE.finditer(line):
                tail = line[match.end():]
                detail = BIND_DETAIL_RE.search(line[match.start():])
                if detail is not None:
                    name = detail.group("name").strip()
                    init = detail.group("init").strip()
                else:
                    name = ""
                    init = tail.strip()
                shape = classify(init)
                entries.append({
                    "line": lineno,
                    "target": name,
                    "shape": shape,
                    "initializer_start": init,
                })
                by_shape[shape] = by_shape.get(shape, 0) + 1
                total += 1
        if entries:
            by_file[rel] = entries

    # Sort by_shape by key for deterministic JSON output.
    by_shape_sorted = {k: by_shape[k] for k in sorted(by_shape)}
    return {
        "total_bindings": total,
        "by_shape": by_shape_sorted,
        "by_file": by_file,
    }


def merge_into_baseline(baseline_path: Path, measured: dict) -> dict:
    """Preserve existing metadata fields; refresh measured fields."""
    existing = {}
    if baseline_path.exists():
        existing = json.loads(baseline_path.read_text(encoding="utf-8"))

    # Start from existing so we retain description/rule/history/etc.
    out = dict(existing)
    out["measurement_command"] = (
        "python3 bench/phase0-agent-native/nullability_s1_baseline.py"
    )
    out["total_bindings"] = measured["total_bindings"]
    out["by_shape"] = measured["by_shape"]
    out["by_file"] = measured["by_file"]
    return out


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="Do not rewrite the JSON; exit non-zero if measured count "
             "differs from the count already recorded in the baseline.",
    )
    parser.add_argument(
        "--repo",
        type=Path,
        default=None,
        help="Repo root override (default: auto-detected from script path).",
    )
    args = parser.parse_args(argv)

    script_path = Path(__file__)
    repo = args.repo.resolve() if args.repo else repo_root_from(script_path)
    baseline_path = repo / "bench" / "phase0-agent-native" / "nullability-info-baseline.json"

    measured = scan(repo)
    print(f"scanned repo: {repo}")
    print(f"total_bindings: {measured['total_bindings']}")
    print(f"by_shape: {measured['by_shape']}")

    if args.check:
        if not baseline_path.exists():
            print(f"ERROR: baseline JSON missing at {baseline_path}", file=sys.stderr)
            return 2
        existing = json.loads(baseline_path.read_text(encoding="utf-8"))
        expected = existing.get("total_bindings")
        if expected != measured["total_bindings"]:
            print(
                f"MISMATCH: baseline JSON says total_bindings={expected}, "
                f"script measured {measured['total_bindings']}",
                file=sys.stderr,
            )
            return 1
        print(f"OK: measurement matches baseline ({expected})")
        return 0

    out = merge_into_baseline(baseline_path, measured)
    baseline_path.write_text(
        json.dumps(out, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"wrote: {baseline_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
