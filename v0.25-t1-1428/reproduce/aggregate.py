#!/usr/bin/env python3
"""0.25 T1 (#1428): collect per-family evidence into one register for a candidate SHA.

usage: aggregate.py <candidate-sha> [--evidence-rev REV] [--out PATH]

Every input is read from a commit (`git show <rev>:<path>`), never from the working tree. By
default that is the candidate itself. Evidence measured on a frozen candidate necessarily lands in
a later commit, so --evidence-rev names the commit holding the evidence (the candidate must be one
of its ancestors); records still count only when they name the candidate. The script only reports: it does
not freeze a candidate, write READY records, or edit scope.json. It refuses to write under
docs/plans/evidence/, where READY result records live (S007).

For each family it reports the scope readiness, the registered cases and requirement digests,
whether each evidence file still has its approved hash, which commit the evidence was measured
on and whether that is the candidate, the T1 control results, and the READY gaps that remain:
missing per-case result records and witness runs for this candidate, and the register's
readyNeeds. Exit status: 0 when the register was produced; 2 on a bad argument or an unknown SHA.
"""
import argparse
import hashlib
import json
import os
import subprocess
import sys

SCOPE = "docs/plans/evidence/v0.25-r0-1426/scope.json"
REGISTER = "docs/plans/evidence/v0.25-t1-1428/register.json"


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True, check=False)


def show(sha, path):
    r = subprocess.run(["git", "show", f"{sha}:{path}"], capture_output=True, check=False)
    return r.stdout if r.returncode == 0 else None


def sha256(data):
    return hashlib.sha256(data.decode("utf-8").replace("\r\n", "\n").encode("utf-8")).hexdigest()


def files_at(sha, prefix):
    r = git("ls-tree", "-r", "--name-only", sha, "--", prefix)
    return r.stdout.split() if r.returncode == 0 else []


def measured_commit(doc):
    for key in ("measuredCommit", "commit"):
        if isinstance(doc.get(key), str):
            return doc[key]
    run = doc.get("testRun") or doc.get("rebasedOntoMain") or {}
    return run.get("commit") or run.get("main")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("candidate")
    ap.add_argument("--evidence-rev")
    ap.add_argument("--out")
    a = ap.parse_args()

    root = git("rev-parse", "--show-toplevel").stdout.strip()
    full = git("rev-parse", "--verify", "--quiet", a.candidate + "^{commit}").stdout.strip()
    if not full:
        print(f"unknown commit: {a.candidate}", file=sys.stderr)
        return 2
    ev = git("rev-parse", "--verify", "--quiet", (a.evidence_rev or full) + "^{commit}").stdout.strip()
    if not ev or git("merge-base", "--is-ancestor", full, ev).returncode != 0:
        print(f"evidence revision {a.evidence_rev} is unknown or does not contain the candidate", file=sys.stderr)
        return 2
    if a.out:
        target = os.path.realpath(a.out)
        if target.startswith(os.path.realpath(os.path.join(root, "docs/plans/evidence")) + os.sep):
            print("refusing to write under docs/plans/evidence/: this register is not a READY record", file=sys.stderr)
            return 2

    scope_bytes, register_bytes = show(ev, SCOPE), show(ev, REGISTER)
    if scope_bytes is None or register_bytes is None:
        print(f"{ev} has no {SCOPE} or {REGISTER}", file=sys.stderr)
        return 2
    scope, register = json.loads(scope_bytes), json.loads(register_bytes)
    mapped = scope.get("mappedBlockers", {}).get("applicable", [])
    # Candidate records: every JSON file a scope readiness record cites, plus every evidence JSON file whose
    # path or content names the candidate SHA. Each is parsed and checked on its content, never accepted by name.
    cited = set()
    for f in scope["families"]:
        r = f.get("readiness", {})
        cited.update(c.get("evidence") for c in r.get("cases", []) if c.get("evidence"))
        cited.update(b.get("witnessRun") for b in r.get("blockersResolved", []) if b.get("witnessRun"))
    # Any evidence JSON file that mentions the candidate SHA in its path or its content, whatever its name.
    hits = git("grep", "-l", "-F", full, ev, "--", "docs/plans/evidence/*.json").stdout.split()
    named = {h.split(":", 1)[1] for h in hits if ":" in h}
    named |= {p for p in files_at(ev, "docs/plans/evidence") if p.endswith(".json") and full in p}
    records = []
    for p in sorted(cited | named):
        data = show(ev, p)
        try:
            doc = json.loads(data) if data else None
        except ValueError:
            doc = None
        if isinstance(doc, dict):
            records.append((p, doc))

    families = []
    for fam in register["families"]:
        fid = fam["id"]
        sfam = next(f for f in scope["families"] if f["id"] == fid)
        evidence = []
        for e in fam["evidence"]:
            data = show(ev, e["path"])
            doc = json.loads(data) if data else {}
            mc = measured_commit(doc) if data else None
            evidence.append({
                "path": e["path"],
                "approvedHash": data is not None and sha256(data) == e["sha256"],
                "measuredCommit": mc,
                "onCandidate": bool(mc) and full.startswith(mc),
            })
        cells = {c["id"]: c.get("cells") or [] for c in scope["cases"]}
        cases = []
        for c in fam["cases"]:
            hit = next((p for p, r in records if r.get("caseId") == c["id"] and r.get("candidate") == full
                        and r.get("requirementSha256") == c["requirementSha256"] and r.get("result") == "passed"
                        and all((r.get("cells") or {}).get(x) == "passed" for x in cells.get(c["id"], []))), None)
            cases.append({**c, "readyResultRecord": hit})
        witnesses = []
        for m in mapped:
            if fid in m["families"]:
                run = next((p for p, r in records if r.get("repair") == m["repair"] and r.get("candidate") == full
                            and r.get("witnesses") == m["regressionWitness"] and r.get("result") == "passed"), None)
                witnesses.append({"repair": m["repair"], "pr": m["pr"], "mergeCommit": m["mergeCommit"],
                                  "regressionWitness": m["regressionWitness"], "witnessRun": run})
        gaps = []
        if not all(e["approvedHash"] for e in evidence):
            gaps.append("evidence file hash differs from the register's approved hash")
        if not all(e["onCandidate"] for e in evidence):
            gaps.append("family evidence was not measured on this candidate")
        missing = [c["id"] for c in cases if not c["readyResultRecord"]]
        if missing:
            gaps.append(f"{len(missing)} of {len(cases)} registered cases have no result record for this candidate: {', '.join(missing)}")
        gaps += [f"no witness run of {w['repair']} (#{w['pr']}) on this candidate" for w in witnesses if not w["witnessRun"]]
        families.append({
            "id": fid, "issue": fam["issue"], "scopeReadiness": sfam["readiness"]["status"],
            "blockers": sfam["blockers"], "evidence": evidence, "cases": cases, "mappedBlockerWitnesses": witnesses,
            "readyGaps": gaps, "readyNeeds": fam["readyNeeds"],
        })

    results_bytes = show(ev, register["results"]["path"])
    results = json.loads(results_bytes) if results_bytes else {"controls": {}}
    controls = [{
        "id": c["id"], "covers": c["covers"], "expected": c["expected"],
        "outcomes": {s: cell["outcome"] for s, cell in results["controls"].get(c["id"], {}).get("surfaces", {}).items()},
    } for c in register["controls"]]

    out = {
        "schema": "calor.v0.25.t1-aggregate/1",
        "candidate": full,
        "evidenceRevision": ev,
        "kind": "aggregate (not a READY record and not a candidate manifest)",
        "scopeVersion": scope["scopeVersion"],
        "registerVersion": register["registerVersion"],
        "registerConsumesCurrentScope": register["consumes"]["scopeVersion"] == scope["scopeVersion"],
        "acceptancePreconditions": scope.get("acceptancePreconditions"),
        "families": families,
        "controls": {
            "resultsApprovedHash": results_bytes is not None and sha256(results_bytes) == register["results"]["sha256"],
            "rows": controls,
            "note": "Committed observations at this commit. They count for READY only after the controls are re-run on the frozen candidate and the register approves that results hash.",
        },
        "corpusCounts": register["corpusCounts"],
        "surfaces": register["surfaces"],
    }
    text = json.dumps(out, indent=2, ensure_ascii=False) + "\n"
    if a.out:
        with open(a.out, "w", encoding="utf-8") as f:
            f.write(text)
    else:
        sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
