#!/usr/bin/env python3
"""PP-W-rows (roadmap v0.16 §4.1, §2.3 S3 (c)) — compile every starter and every
seeded extension of the six W-00x pairs on BOTH arms and record the per-arm
diagnostic multisets in `pairs/ppw-seeded-compiles.json`.

Pinned invocation (the PP-E1 discipline, A-1.11):

    dotnet <calor.dll> -i <src> -o <scratch>      # arm B: no flags
    dotnet <calor.dll> -i <src> -o <scratch> --permissive-effects   # arm A

under `LC_ALL=C`, diagnostics sorted by (line, column, code, severity, text).
The scratch output lives outside the repository. Sources are passed as
repo-relative paths from the repo root so any path embedded in a message
(Calor0208's `calor://` URI) is checkout-independent.

Arms:

    A  v0.14.3 (`63316987`) + --permissive-effects — the pre-rows control arm
    B  v0.15.0 (tag v0.15.0 = `3bb2601e`; `src/` equals `7d621c0d`) — strict

Usage:

    # regenerate (writes pairs/ppw-seeded-compiles.json)
    PPW_ARM_A_DLL=<path/to/v0.14.3/calor.dll> PPW_ARM_B_DLL=<path/to/v0.15.0/calor.dll> \
        python3 bench/phase0-agent-native/ppw-compile.py

    # recompute and compare against the committed JSON (exit 1 on any delta)
    PPW_ARM_A_DLL=... PPW_ARM_B_DLL=... python3 bench/phase0-agent-native/ppw-compile.py --check

The dll paths may also be passed as --arm-a-dll / --arm-b-dll. The compiler
build SHAs recorded in the JSON are taken from --arm-a-commit / --arm-b-commit
(defaults: the roadmap's pins) and are informational; the test that guards
the JSON (tests/test_ppw_pairs.py) checks the multisets, not the SHAs.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
PAIRS = os.path.join(HERE, "pairs")
OUT = os.path.join(PAIRS, "ppw-seeded-compiles.json")

PAIR_IDS = [
    "W-001-middleware-stage",
    "W-002-map-and-report",
    "W-003-match-fallback",
    "W-004-counter-peek",
    "W-005-pipeline-trace",
    "W-006-map-doubler",
]

ARM_A_COMMIT = "633169879e16a5e49d3b7ab51089f195d7573a0b"
ARM_B_COMMIT = "3bb2601e0cbd93fc25fdaaf2a0ea5183b8a2dd6a"
STARTER_FREEZE_COMMIT = "7d621c0d"
FLAGS = {"a": ["--permissive-effects"], "b": []}

DIAG = re.compile(r"^(?P<path>.*?)\((?P<line>\d+),(?P<col>\d+)\): "
                  r"(?P<sev>error|warning|info) (?P<code>Calor\d+): (?P<text>.*)$")


def compile_one(dll, rel_src, arm):
    """Compile one repo-relative source on one arm. Returns (exit, emitted, diags)."""
    env = dict(os.environ, LC_ALL="C", LANG="C")
    with tempfile.TemporaryDirectory() as td:
        out = os.path.join(td, "out.g.cs")
        p = subprocess.run(
            ["dotnet", dll, "-i", rel_src, "-o", out] + FLAGS[arm],
            cwd=REPO, env=env, capture_output=True, text=True)
        emitted = os.path.exists(out)
        diags = []
        for line in (p.stdout + "\n" + p.stderr).splitlines():
            m = DIAG.match(line.strip())
            if m:
                diags.append({
                    "line": int(m["line"]),
                    "column": int(m["col"]),
                    "code": m["code"],
                    "severity": m["sev"],
                    "text": m["text"],
                })
        diags.sort(key=lambda d: (d["line"], d["column"], d["code"], d["severity"], d["text"]))
        return p.returncode, emitted, diags


def blob_sha(path):
    return subprocess.run(["git", "hash-object", path], cwd=REPO,
                          capture_output=True, text=True, check=True).stdout.strip()


def enumerate_sources():
    """Yield (pair_id, role, arm, rel_path) for every starter and seed of every pair."""
    for pid in PAIR_IDS:
        pdir = os.path.join(PAIRS, pid)
        with open(os.path.join(pdir, "pair.json"), encoding="utf-8") as fh:
            manifest = json.load(fh)
        for arm_key, arm in manifest["arms"].items():
            arm_id = arm["armId"]
            fixture = os.path.join(pdir, arm["fixture"])
            for name in sorted(os.listdir(fixture)):
                if name.endswith(".calr"):
                    yield pid, "starter", arm_id, os.path.relpath(os.path.join(fixture, name), REPO)
        for role, per_arm in manifest["seeded"].items():
            for arm_id in ("a", "b"):
                if arm_id not in per_arm:
                    continue
                sdir = os.path.join(pdir, per_arm[arm_id])
                for name in sorted(os.listdir(sdir)):
                    if name.endswith(".calr"):
                        yield pid, role, arm_id, os.path.relpath(os.path.join(sdir, name), REPO)


def build(dlls, commits):
    compiles = []
    for pid, role, arm_id, rel in enumerate_sources():
        rc, emitted, diags = compile_one(dlls[arm_id], rel, arm_id)
        compiles.append({
            "pair": pid,
            "role": role,
            "arm": arm_id.upper(),
            "path": rel,
            "blobSha": blob_sha(rel),
            "exitCode": rc,
            "emitted": emitted,
            "diagnosticCount": len(diags),
            "diagnostics": diags,
        })
    return {
        "schemaVersion": 1,
        "generatedBy": "bench/phase0-agent-native/ppw-compile.py",
        "roadmap": "docs/plans/roadmap-v0.16.md §4.1 PP-W-rows; §2.3 S3 (c)",
        "starterFreezeCommit": STARTER_FREEZE_COMMIT,
        "invocation": "dotnet <calor.dll> -i <src> -o <scratch> [flags]; LC_ALL=C; sources passed repo-relative from the repo root; diagnostics sorted by (line, column, code, severity, text)",
        "arms": {
            "A": {"label": "calor+v0.14.3 --permissive-effects (pre-rows control arm)",
                  "compilerCommit": commits["a"], "flags": FLAGS["a"], "controlArmKind": "pre-rows"},
            "B": {"label": "calor+v0.15.0 strict (tag v0.15.0; src/ equals 7d621c0d)",
                  "compilerCommit": commits["b"], "flags": FLAGS["b"]},
        },
        "legBPairs": [pid for pid in PAIR_IDS if _manifest(pid)["legB"]],
        "blindPairs": [pid for pid in PAIR_IDS if _manifest(pid)["class"] == "blind"],
        "compiles": compiles,
    }


def _manifest(pid):
    with open(os.path.join(PAIRS, pid, "pair.json"), encoding="utf-8") as fh:
        return json.load(fh)


def comparable(doc):
    """The part of the document the --check mode compares (build SHAs are informational)."""
    return [{k: c[k] for k in ("pair", "role", "arm", "path", "blobSha", "exitCode", "emitted", "diagnostics")}
            for c in doc["compiles"]]


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--arm-a-dll", default=os.environ.get("PPW_ARM_A_DLL"))
    ap.add_argument("--arm-b-dll", default=os.environ.get("PPW_ARM_B_DLL"))
    ap.add_argument("--arm-a-commit", default=ARM_A_COMMIT)
    ap.add_argument("--arm-b-commit", default=ARM_B_COMMIT)
    ap.add_argument("--check", action="store_true", help="recompute and diff against the committed JSON")
    ap.add_argument("--out", default=OUT)
    args = ap.parse_args(argv)
    if not args.arm_a_dll or not args.arm_b_dll:
        ap.error("both arm dlls are required (PPW_ARM_A_DLL / PPW_ARM_B_DLL or --arm-a-dll / --arm-b-dll)")
    for d in (args.arm_a_dll, args.arm_b_dll):
        if not os.path.isfile(d):
            ap.error(f"calor.dll not found: {d}")
    doc = build({"a": args.arm_a_dll, "b": args.arm_b_dll},
                {"a": args.arm_a_commit, "b": args.arm_b_commit})
    if args.check:
        with open(args.out, encoding="utf-8") as fh:
            committed = json.load(fh)
        want, got = comparable(committed), comparable(doc)
        if want == got:
            print(f"OK: {len(got)} compiles match {os.path.relpath(args.out, REPO)}")
            return 0
        by_key = lambda rows: {(r["pair"], r["role"], r["arm"], r["path"]): r for r in rows}
        w, g = by_key(want), by_key(got)
        for key in sorted(set(w) | set(g)):
            if w.get(key) != g.get(key):
                print("DELTA:", key)
                print("  committed:", json.dumps(w.get(key), sort_keys=True)[:600])
                print("  recomputed:", json.dumps(g.get(key), sort_keys=True)[:600])
        return 1
    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    print(f"wrote {os.path.relpath(args.out, REPO)} ({len(doc['compiles'])} compiles)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
