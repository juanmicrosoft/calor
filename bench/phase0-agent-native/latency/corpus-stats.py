#!/usr/bin/env python3
"""Corpus content statistics for the D3.3 latency fixture (loop plan WS3).

Computes the content criteria the fixture's generator must match, over the
corpora agents actually work on (samples/ and bench pair fixtures by
default). Marker counting is regex-level on §-markers — a stats tool, not a
parser; adequate because the criteria are density ratios, not semantics.

Metrics (recorded in the fixture README per the plan's D3.3 governance):
  - lines/module-file        non-blank lines per .calr file
  - functions/module         §F markers per §M module
  - contract density         (§Q + §S + §IV) markers per function
  - effect density           §E markers per function
  - calls/function           §C markers per function
  - cross-module ref depth   per project directory: longest chain in the
                             module-level call graph (edges A→B when a
                             function in module A calls a public function
                             defined in module B; cycles contribute their
                             longest acyclic traversal)

Usage: corpus-stats.py [root ...]   (default: samples/ bench/.../pairs/)
Prints a human table and, with --json FILE, writes machine output.
"""
import argparse
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
DEFAULT_ROOTS = [REPO / "samples", REPO / "bench" / "phase0-agent-native" / "pairs"]

MODULE_RE = re.compile(r"^\s*§M\{[^:}]+:([A-Za-z0-9_]+)")
FUNC_RE = re.compile(r"^\s*§F\{[^:}]+:([A-Za-z0-9_]+)(?::([a-z]+))?\}")
CONTRACT_RE = re.compile(r"^\s*§(Q|S|IV)\b")
EFFECT_RE = re.compile(r"^\s*§E\{")
CALL_RE = re.compile(r"§C\{([A-Za-z0-9_.]+)\}")


def pctl(sorted_vals, p):
    if not sorted_vals:
        return None
    k = (len(sorted_vals) - 1) * p / 100
    lo, hi = int(k), min(int(k) + 1, len(sorted_vals) - 1)
    if lo == hi:
        return sorted_vals[lo]
    return sorted_vals[lo] + (sorted_vals[hi] - sorted_vals[lo]) * (k - lo)


def scan_file(path):
    """Per-file counts plus per-module structure for the call graph."""
    text = path.read_text(encoding="utf-8", errors="replace")
    lines = [l for l in text.splitlines() if l.strip()]
    modules = []          # [{name, functions: [name], public: [name], calls: [target]}]
    current = None
    for line in text.splitlines():
        m = MODULE_RE.match(line)
        if m:
            current = {"name": m.group(1), "functions": [], "public": [], "calls": []}
            modules.append(current)
            continue
        if current is None:
            continue
        f = FUNC_RE.match(line)
        if f:
            current["functions"].append(f.group(1))
            if (f.group(2) or "pub") == "pub":
                current["public"].append(f.group(1))
        current["calls"].extend(c.split(".")[0] for c in CALL_RE.findall(line))
    return {
        "path": path,
        "lines": len(lines),
        "modules": modules,
        "contracts": sum(1 for l in text.splitlines() if CONTRACT_RE.match(l)),
        "effects": sum(1 for l in text.splitlines() if EFFECT_RE.match(l)),
        "calls": sum(len(m["calls"]) for m in modules),
        "functions": sum(len(m["functions"]) for m in modules),
    }


def longest_chain(edges, nodes):
    """Longest path length (in edges) in the module graph; cycles bounded."""
    best = 0
    memo = {}

    def dfs(node, seen):
        nonlocal best
        if node in memo:
            return memo[node]
        depth = 0
        for nxt in edges.get(node, ()):  # noqa: B007
            if nxt in seen:
                continue
            depth = max(depth, 1 + dfs(nxt, seen | {nxt}))
        memo[node] = depth
        best = max(best, depth)
        return depth

    for n in nodes:
        dfs(n, {n})
    return best


def project_depth(files):
    """Cross-module reference depth for one project (directory of files)."""
    defined = {}  # function name -> module name (public functions only)
    modules = set()
    for f in files:
        for m in f["modules"]:
            modules.add(m["name"])
            for fn in m["public"]:
                defined.setdefault(fn, m["name"])
    edges = {}
    for f in files:
        for m in f["modules"]:
            own = set(m["functions"])
            for target in m["calls"]:
                target_mod = defined.get(target)
                if target_mod and target_mod != m["name"] and target not in own:
                    edges.setdefault(m["name"], set()).add(target_mod)
    return longest_chain(edges, modules)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("roots", nargs="*", type=Path, default=None)
    ap.add_argument("--json", type=Path, help="write machine-readable stats here")
    args = ap.parse_args()
    roots = args.roots or DEFAULT_ROOTS

    files = []
    for root in roots:
        if not root.exists():
            print(f"warning: root not found: {root}", file=sys.stderr)
            continue
        for p in sorted(root.rglob("*.calr")):
            if any(part in ("bin", "obj", "heldout") for part in p.parts):
                continue
            files.append(scan_file(p))
    files = [f for f in files if f["functions"] > 0]
    if not files:
        print("no .calr files with functions found", file=sys.stderr)
        return 1

    lines = sorted(f["lines"] for f in files)
    funcs_per_module = sorted(
        len(m["functions"]) for f in files for m in f["modules"] if m["functions"])
    contract_density = sorted(f["contracts"] / f["functions"] for f in files)
    effect_density = sorted(f["effects"] / f["functions"] for f in files)
    call_density = sorted(f["calls"] / f["functions"] for f in files)

    # One project per directory that holds >0 corpus files.
    by_dir = {}
    for f in files:
        by_dir.setdefault(f["path"].parent, []).append(f)
    depths = sorted(project_depth(fs) for fs in by_dir.values())

    def row(name, vals):
        return {
            "metric": name,
            "n": len(vals),
            "p25": pctl(vals, 25), "p50": pctl(vals, 50),
            "p75": pctl(vals, 75), "p90": pctl(vals, 90),
            "max": vals[-1] if vals else None,
        }

    stats = {
        "roots": [str(r) for r in roots],
        "files": len(files),
        "projects": len(by_dir),
        "metrics": [
            row("lines_per_module_file", lines),
            row("functions_per_module", funcs_per_module),
            row("contract_markers_per_function", contract_density),
            row("effect_markers_per_function", effect_density),
            row("calls_per_function", call_density),
            row("cross_module_ref_depth_per_project", depths),
        ],
    }

    print(f"corpus: {len(files)} files, {len(by_dir)} project dirs "
          f"(roots: {', '.join(str(r) for r in roots)})")
    print(f"{'metric':38} {'n':>5} {'p25':>7} {'p50':>7} {'p75':>7} {'p90':>7} {'max':>7}")
    for m in stats["metrics"]:
        print(f"{m['metric']:38} {m['n']:>5} "
              + " ".join(f"{(m[k] if m[k] is not None else float('nan')):>7.2f}"
                         for k in ("p25", "p50", "p75", "p90", "max")))

    if args.json:
        args.json.write_text(json.dumps(stats, indent=2) + "\n", encoding="utf-8")
        print(f"wrote {args.json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
