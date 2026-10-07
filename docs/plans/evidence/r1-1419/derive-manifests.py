#!/usr/bin/env python3
"""Derive (or --check) the per-baseline row manifests for the #1419 registration.

Each investigated baseline (B1, N1) gets its own manifest: the frozen rows with baseline-prefixed
ids, the case ids, a baseline-specific result path, and git object ids of every registered entry
point at that baseline's commit. The manifests are never merged: B1 and N1 rows are separate.

Needs full git history for both commits (git fetch origin; the commits are on main's history).
#1311 runs `derive-manifests.py --check` as its first preflight step; a mismatch stops the sweep.
"""
import hashlib
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
TREES = ["src", "src/Calor.Compiler/Verification", "src/Calor.Compiler/Parsing", "src/Calor.Compiler/Binding",
         "src/Calor.Compiler/TypeChecking", "src/Calor.Compiler/CodeGen", "src/Calor.Runtime"]
WHITELIST_DOC = "docs/verification-modeled-forms.md"
# Front-end files whose B1 blobs gate the R1 case-validity test (it re-checks validity only while
# the working tree still has B1's front end; the frozen cases target B1 and N1, not later trees).
FRONT_END = ["src/Calor.Compiler/Parsing/Lexer.cs", "src/Calor.Compiler/Parsing/Parser.cs",
             "src/Calor.Compiler/Parsing/AttributeHelper.cs", "src/Calor.Compiler/Binding/Binder.cs",
             "src/Calor.Compiler/Binding/Scope.cs", "src/Calor.Compiler/TypeChecking/TypeChecker.cs",
             "src/Calor.Compiler/TypeChecking/CalorType.cs", "src/Calor.Compiler/Effects/EffectEnforcementPass.cs"]


def git(*args):
    return subprocess.run(["git", "-C", str(ROOT), *args], check=True, capture_output=True, text=True).stdout.strip()


def case_ids(templates):
    per_row, ids = {}, {}
    for t in templates["templates"]:
        for _ in range(t["instances"]):
            per_row[t["row"]] = per_row.get(t["row"], 0) + 1
            ids.setdefault(t["row"], []).append(f"R1-{t['row']}-{per_row[t['row']]:03d}")
    return ids


def manifest(reg, templates, baseline):
    commit = baseline["commit"]
    catalog = reg["denominator"]["entryPointCatalog"]
    doc_bytes = subprocess.run(["git", "-C", str(ROOT), "show", f"{commit}:{WHITELIST_DOC}"],
                               check=True, capture_output=True).stdout
    ids = case_ids(templates)
    result_root = reg["noPooling"]["resultPaths"][baseline["id"]]
    return {
        "schemaVersion": 1,
        "baseline": baseline["id"],
        "commit": commit,
        "registrationVersion": reg["registrationVersion"],
        "binary": baseline["binary"]["kind"],
        "resultPath": result_root,
        "entryPoints": {key: {"path": ep["path"], "symbols": ep["symbols"], "blob": git("rev-parse", f"{commit}:{ep['path']}")}
                        for key, ep in sorted(catalog.items())},
        "trees": {path: git("rev-parse", f"{commit}:{path}") for path in TREES},
        "frontEndBlobs": {path: git("rev-parse", f"{commit}:{path}") for path in FRONT_END},
        "whitelistDocument": {"path": WHITELIST_DOC, "sha256": hashlib.sha256(doc_bytes).hexdigest()},
        "translatorSemanticsVersion": "z3-executable-semantics-v2",
        "rows": [{
            "id": f"{baseline['id']}:{row['id']}",
            "row": row["id"],
            "classification": row["classification"],
            "releaseCritical": row["releaseCritical"],
            "channel": row["channel"],
            "entryPoints": row["entryPoints"],
            "caseIds": ids.get(row["id"], []),
            "resultPath": f"{result_root}rows/{row['id']}.jsonl",
        } for row in reg["denominator"]["rows"]],
    }


def main():
    check = "--check" in sys.argv
    reg = json.loads((HERE / "registration.json").read_text(encoding="utf-8"))
    templates = json.loads((HERE / "templates.json").read_text(encoding="utf-8"))
    failed = False
    for baseline in reg["baselines"]:
        if baseline["id"] not in ("B1", "N1"):
            continue
        text = json.dumps(manifest(reg, templates, baseline), indent=2, ensure_ascii=False) + "\n"
        path = HERE / f"manifest-{baseline['id']}.json"
        if check:
            if path.read_text(encoding="utf-8").replace("\r\n", "\n") != text:
                print(f"MISMATCH {path.name}")
                failed = True
            else:
                print(f"ok {path.name}")
        else:
            path.write_text(text, encoding="utf-8")
            print(f"wrote {path.name}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
