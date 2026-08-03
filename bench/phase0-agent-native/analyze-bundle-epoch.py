#!/usr/bin/env python3
"""
Aggregate a WS-W4 bundle dry-run epoch (result.json files written by
run-bundle.sh) into per-(bundle, arm) feasibility metrics.

Reports, per arm group:
  * outcome mix (caught / escaped / broke-regression / invalid)
  * cost/run (mean, from summed total_cost_usd — never hand-priced)
  * output tokens (mean), iterations-to-declared-done (mean), wall-clock (mean)
  * variance of iterations-to-green (a feasibility / MDE input)

And the D-W4.4 CEILING-RECURRENCE signal: the C#-arm escaped-bug incidence
(fraction of C#-arm runs whose injected bug ESCAPED). If the C# arm catches ~all
injected bugs itself (incidence ~ 0), the ceiling persists at real scale.

Usage: analyze-bundle-epoch.py <epoch_out_dir> [--json]
"""
import json
import os
import sys
import statistics as stats


def find_results(root):
    out = []
    for dirpath, _dirs, files in os.walk(root):
        if "result.json" in files:
            try:
                with open(os.path.join(dirpath, "result.json")) as f:
                    out.append(json.load(f))
            except (OSError, json.JSONDecodeError):
                pass
    return out


def mean(xs):
    xs = [x for x in xs if x is not None]
    return round(stats.mean(xs), 4) if xs else None


def pvariance(xs):
    xs = [x for x in xs if x is not None]
    return round(stats.pvariance(xs), 4) if len(xs) > 1 else 0.0


def summarize(results):
    groups = {}
    for r in results:
        groups.setdefault((r.get("bundle", "?"), r.get("arm", "?")), []).append(r)

    lines = []
    per_group = []
    for (bundle, arm), rs in sorted(groups.items()):
        n = len(rs)
        outcomes = {}
        for r in rs:
            outcomes[r.get("outcome", "?")] = outcomes.get(r.get("outcome", "?"), 0) + 1
        g = {
            "bundle": bundle, "arm": arm, "runs": n,
            "caught": outcomes.get("caught", 0),
            "escaped": outcomes.get("escaped", 0),
            "brokeRegression": outcomes.get("broke-regression", 0),
            "invalid": outcomes.get("invalid", 0),
            "skipped": outcomes.get("skipped", 0),
            "meanCostUsd": mean([r.get("costUsd") for r in rs]),
            "meanOutputTokens": mean([r.get("tokens", {}).get("output") for r in rs]),
            "meanItersToDone": mean([r.get("iterationsToDeclaredDone") for r in rs]),
            "varItersToDone": pvariance([r.get("iterationsToDeclaredDone") for r in rs]),
            "meanWallSec": mean([r.get("wallClockSeconds") for r in rs]),
        }
        per_group.append(g)
        lines.append(
            f"  {bundle}  [{arm}]  n={n}  "
            f"caught={g['caught']} escaped={g['escaped']} broke={g['brokeRegression']} "
            f"invalid={g['invalid']} skipped={g['skipped']}  "
            f"cost/run=${g['meanCostUsd']}  outTok={g['meanOutputTokens']}  "
            f"iters~={g['meanItersToDone']}(var {g['varItersToDone']})  wall={g['meanWallSec']}s"
        )

    # Ceiling-recurrence signal: C#-arm escaped incidence. Keyed on the BASE arm
    # (label may carry a "+variant" suffix) and restricted to REAL agent runs —
    # synthetic null-agent runs (fix/noop smokes) and skipped/invalid runs are not
    # evidence of the ceiling.
    def base_arm(r):
        return (r.get("arm") or "").split("+", 1)[0]
    cs = [r for r in results
          if base_arm(r) == "csharp" and not r.get("nullAgent")
          and r.get("outcome") not in ("invalid", "skipped")]
    cs_escaped = sum(1 for r in cs if r.get("outcome") == "escaped")
    # Per-TASK escapes: the D-W4.4 floor is phrased per task (>=1 C#-arm escaped
    # bug per ~5 tasks), so report how many distinct bundles had >=1 C# escape.
    cs_tasks = {r.get("bundle") for r in cs}
    cs_escaped_tasks = {r.get("bundle") for r in cs if r.get("outcome") == "escaped"}
    incidence = (cs_escaped / len(cs)) if cs else None
    if not cs:
        interp = "no valid C#-arm runs"
    elif cs_escaped == 0:
        interp = ("C#-arm escaped 0 => the C# arm catches ~all injected bugs "
                  "itself; the ceiling PERSISTS at real scale (D-W4.4 -> PP-W2 "
                  "not adjudicated, publish 'ceiling persists').")
    else:
        interp = (f"C#-arm escaped {cs_escaped}/{len(cs)} runs across "
                  f"{len(cs_escaped_tasks)}/{len(cs_tasks)} tasks (incidence "
                  f"{incidence:.3f}) => the C# arm does NOT catch all injected "
                  "bugs at real scale; the ceiling does NOT uniformly persist and "
                  "there is detectable escaped-bug headroom. Whether this clears "
                  "the A-1.4 floor, and whether the Calor arm's escaped incidence "
                  "is lower (the actual PP-W2 question), are tranche-2/epoch calls "
                  "— this signal is the C#-arm ceiling check only, not the verdict.")
    ceiling = {
        "csharpValidRuns": len(cs),
        "csharpEscaped": cs_escaped,
        "csharpEscapedTasks": len(cs_escaped_tasks),
        "csharpTasks": len(cs_tasks),
        "csharpEscapedIncidence": round(incidence, 4) if incidence is not None else None,
        "interpretation": interp,
    }

    total_cost = round(sum(r.get("costUsd", 0) or 0 for r in results), 4)
    return {
        "totalRuns": len(results),
        "totalRealizedCostUsd": total_cost,
        "groups": per_group,
        "ceilingRecurrence": ceiling,
    }, lines


def main(argv):
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2
    root = argv[1]
    as_json = "--json" in argv[2:]
    results = find_results(root)
    if not results:
        print(f"No result.json under {root}", file=sys.stderr)
        return 1
    summary, lines = summarize(results)
    if as_json:
        print(json.dumps(summary, indent=2))
        return 0
    print(f"WS-W4 bundle dry-run epoch: {summary['totalRuns']} run(s), "
          f"realized cost ${summary['totalRealizedCostUsd']}")
    print("Per (bundle, arm):")
    print("\n".join(lines))
    c = summary["ceilingRecurrence"]
    print(f"\nD-W4.4 ceiling-recurrence signal: C#-arm escaped {c['csharpEscaped']}/"
          f"{c['csharpValidRuns']} valid runs (incidence {c['csharpEscapedIncidence']})")
    print(f"  {c['interpretation']}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
