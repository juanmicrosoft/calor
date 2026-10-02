#!/usr/bin/env python3
"""Generate the #1276 (0.24 B1) benchmark registration from the frozen cutoff tree.

Reads every file under tests/TestData/Benchmarks from the cutoff commit with `git show`, pairs
each .calr with the .cs of the same directory and stem, attaches each pair's task statement from
manifest.json at the cutoff, and writes the content-addressed packet under
docs/plans/evidence/b1-1276/registration/. It never compiles, runs, or compares a pair: dispositions
are assigned only after the registration merges, by the registered oracle.

Usage: python3 scripts/b1_1276_registration.py [--check]
--check regenerates in memory and fails if any committed packet file differs.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from pathlib import Path

CUTOFF = "72a0a855d8cd7f1e85c5bcd99474b83d1556d4e1"
CORPUS = "tests/TestData/Benchmarks"
PACKET = Path("docs/plans/evidence/b1-1276/registration")
DOC = "docs/plans/b1-1276-benchmark-registration.md"
SCENARIO_EXCLUSION = "X1-scenario-variant"
ORACLE_FILES = [
    "tests/Calor.Evaluation/Equivalence/PairDifferentialOracle.cs",
    "tests/Calor.Evaluation/Equivalence/InputGenerator.cs",
    "tests/Calor.Evaluation/Equivalence/PairOracleCommand.cs",
]


def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(root), *args])


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def lf_sha(path: Path) -> str:
    return sha(path.read_bytes().replace(b"\r\n", b"\n"))


def dump(obj) -> str:
    return json.dumps(obj, indent=2, ensure_ascii=False) + "\n"


def pair_row(pair_id, calor, csharp, blobs, source, task, exclusion=None):
    gen = "b1-1276-signature-inputs-v1"
    row = {
        "pairId": pair_id, "source": source,
        "calorPath": f"{CORPUS}/{calor}", "calorSha256": sha(blobs[calor]),
        "csharpPath": f"{CORPUS}/{csharp}", "csharpSha256": sha(blobs[csharp]),
        "taskStatement": task, "taskStatementSha256": sha(task.encode("utf-8")),
        "inputSet": {"generator": gen, "seedKeyPrefix": f"{gen}|{pair_id}|", "definition": "registration.json#/oracle/inputs"},
        "expectedOutputs": {"rule": "cross-arm-agreement", "goldenValues": "none",
                            "definition": "registration.json#/oracle/expectedOutputs"},
        "failureBehavior": "oracle-v1-default",
        "disposition": "EXCLUDED-PRE-REGISTERED" if exclusion else "UNCLASSIFIED",
        "included": False,
        "equivalenceEvidence": {"status": "not-run", "exclusionId": exclusion} if exclusion
        else {"status": "pending-oracle", "oracle": "b1-1276-pair-oracle@1"},
        "reviewer": "unassigned until the oracle assigns a disposition",
    }
    if exclusion:
        row["exclusionId"] = exclusion
    return row


# (manifest list, calor field, csharp field, task-statement fields, variant-of fields, exclusion)
SOURCES = [
    ("benchmarks", "calorFile", "csharpFile", ["id", "name", "category", "level", "features", "notes"], None, None),
    ("bugScenarios", "calorBuggy", "csharpBuggy", ["id", "description", "category", "expectedError"],
     ("calorFixed", "csharpFixed"), SCENARIO_EXCLUSION),
    ("editTasks", "calorAfter", "csharpAfter", ["id", "description", "category"], ("calorBefore", "csharpBefore"), SCENARIO_EXCLUSION),
]


def build(root: Path) -> dict[str, str]:
    listing = git(root, "ls-tree", "-r", "--name-only", CUTOFF, "--", CORPUS).decode().splitlines()
    rel = sorted(p[len(CORPUS) + 1:] for p in listing)
    blobs = {p: git(root, "show", f"{CUTOFF}:{CORPUS}/{p}") for p in rel}
    manifest = json.loads(blobs["manifest.json"])

    pairs = []
    for name, calor, csharp, fields, variant, exclusion in SOURCES:
        for index, e in enumerate(manifest[name]):
            lines = [f"source: manifest.json {name}[{index}]"]
            lines += [f"{k}: {', '.join(map(str, e[k])) if isinstance(e[k], list) else e[k]}" for k in fields]
            lines += [f"variantOf: {e[variant[0]]} / {e[variant[1]]}"] if variant else []
            pairs.append(pair_row(e[calor][:-5], e[calor], e[csharp], blobs, f"manifest.{name}", "\n".join(lines) + "\n", exclusion))

    # Completeness: every .calr and every hand-written .cs belongs to exactly one pair, same stem.
    roles = {}
    for p in pairs:
        calor, csharp = p["calorPath"][len(CORPUS) + 1:], p["csharpPath"][len(CORPUS) + 1:]
        assert calor[:-5] == csharp[:-3] == p["pairId"], p["pairId"]
        for path, role in ((calor, "calor-arm"), (csharp, "csharp-arm")):
            assert path not in roles, f"{path} belongs to two pairs"
            roles[path] = {"role": role, "pairId": p["pairId"]}
    non_pair = {
        "manifest.json": "Benchmark manifest: task metadata for every pair (read for task statements).",
        "Comprehension/questions.json": "LLM comprehension question bank; not a program.",
    }
    for path in rel:
        if path in roles:
            continue
        if path.endswith(".g.cs"):
            roles[path] = {"role": "non-pair", "reason": "Calor compiler output committed for a TokenEconomics .calr "
                           "(regenerated by BulkBenchmarkCompilationTests); an output of the Calor arm, not an independent C# arm."}
        else:
            assert path in non_pair, f"unpaired corpus file {path}"
            roles[path] = {"role": "non-pair", "reason": non_pair[path]}
    assert len({p["pairId"] for p in pairs}) == len(pairs)

    inventory = {
        "schemaVersion": 1, "cutoffCommit": CUTOFF, "root": CORPUS,
        "hashing": "SHA-256 of the git blob bytes at the cutoff (no line-ending normalization).",
        "files": [{"path": f"{CORPUS}/{p}", "sha256": sha(blobs[p]), **roles[p]} for p in rel],
    }
    excluded = [p["pairId"] for p in pairs if p["disposition"] == "EXCLUDED-PRE-REGISTERED"]
    exclusions = {
        "schemaVersion": 1, "cutoffCommit": CUTOFF,
        "pairExclusions": [{
            "exclusionId": SCENARIO_EXCLUSION,
            "reason": "Scenario variant of another registered pair: a bug-injected copy (manifest bugScenarios) or an "
                      "edited copy (manifest editTasks) of a pair already in the benchmarks list. Counting it as a separate "
                      "program pair would duplicate a program in the population, and the per-pair metric loop "
                      "(BenchmarkRunner over manifest benchmarks) never reads it. The reason is structural and was fixed "
                      "before any oracle run; it does not depend on any equivalence outcome.",
            "pairs": excluded,
        }],
        "scopeExclusions": [{
            "path": "benchmarks/",
            "reason": "Excluded from 0.24 scope by contract 1.0.1 artifact-inventory excludedFromScope (no workflow consumes "
                      "it). At the cutoff it holds 7 .calr files and no C# arm, so it contributes 0 pairs.",
        }],
    }
    files = {
        "pairs.json": dump({"schemaVersion": 1, "registrationId": "b1-1276-benchmark-registration",
                            "cutoffCommit": CUTOFF, "pairCount": len(pairs), "pairs": pairs}),
        "corpus-inventory.json": dump(inventory),
        "exclusions.json": dump(exclusions),
    }

    metric_set = json.loads((root / PACKET / "metric-set.json").read_text(encoding="utf-8"))
    for f in metric_set["implementationFiles"]:
        f["sha256"] = sha(git(root, "show", f"{CUTOFF}:{f['path']}"))
        assert f["sha256"] == sha((root / f["path"]).read_bytes()), f"{f['path']} differs from the cutoff blob"
    files["metric-set.json"] = dump(metric_set)

    registration = json.loads((root / PACKET / "registration.json").read_text(encoding="utf-8"))
    registration["oracle"]["implementation"] = [{"path": p, "sha256": lf_sha(root / p)} for p in ORACLE_FILES]
    registration["denominator"]["pairs"] = len(pairs)
    registration["denominator"]["toOracle"] = len(pairs) - len(excluded)
    registration["denominator"]["excludedPreRegistered"] = len(excluded)
    key = registration["comparability"]
    key["pairManifestSha256"] = sha(files["pairs.json"].encode())
    key["metricSetSha256"] = sha(files["metric-set.json"].encode())
    key["exclusionsSha256"] = sha(files["exclusions.json"].encode())
    files["registration.json"] = dump(registration)

    hashed = {f"{PACKET.as_posix()}/{name}": sha(text.encode()) for name, text in sorted(files.items())}
    hashed[DOC] = lf_sha(root / DOC)
    files["sha256.json"] = dump({"algorithm": "sha256", "normalization": "LF", "files": hashed})
    return files


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = Path(git(Path.cwd(), "rev-parse", "--show-toplevel").decode().strip())
    files = build(root)
    stale = [n for n, t in files.items() if not (root / PACKET / n).exists()
             or (root / PACKET / n).read_bytes() != t.encode("utf-8")]
    if args.check:
        for name in stale:
            print(f"stale: {PACKET / name}", file=sys.stderr)
        return 1 if stale else 0
    for name, text in files.items():
        (root / PACKET / name).write_bytes(text.encode("utf-8"))
    print(f"wrote {len(files)} files; {len(json.loads(files['pairs.json'])['pairs'])} pairs")
    return 0


if __name__ == "__main__":
    sys.exit(main())
