"""Summarize D1 observations without turning source obligations into rejections."""
import argparse
import collections
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("observations", type=Path)
parser.add_argument("reports", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
projects = json.loads((args.observations / "corpus-observations.json").read_text())
controls = json.loads((args.observations / "controls.json").read_text())
metadata = json.loads((args.observations / "metadata-controls.json").read_text())

# These assertions validate this measurement, not a shipping nullability policy.
assert controls["originalRuntime"] == controls["convertedRuntime"]
assert len(controls["originalRuntime"]) == 8
assert sum(len(r["attempts"]) for r in controls["originalRuntime"]) == 13
assert not controls["compileDiagnostics"] and not controls["conversionLosses"]
runtime = {r["method"]: r["attempts"] for r in controls["originalRuntime"]}
assert runtime["Echo"][0]["resultIsNull"] and runtime["Default"][0]["resultIsNull"]
assert not runtime["New"][0]["resultIsNull"] and not runtime["Coalesce"][0]["resultIsNull"]
assert any(r["expression"] == "Legacy.API.Read()" and r["category"] == "genuinely-oblivious-source"
           for r in metadata["boundaries"])
assert any(r["expression"] == 'System.IO.Directory.GetParent("/")' and r["category"] == "explicit-nullable"
           for r in metadata["boundaries"])
assert any("CS0246" in d for d in metadata["diagnostics"])
assert any(r["expression"] == "new Item()" and r["category"] == "known-safe-expression"
           for r in controls["sourceObservations"])

summaries = []
classifications = []
for project in projects:
    name = project["name"]
    report = json.loads((args.reports / (name + "-roundtrip.json")).read_text())
    evidence = report["evidence"]
    assert evidence["DeclaredTestAttemptsPerLeg"] == 2
    assert len(evidence["Inputs"]) == report["evidence_counts"]["inventory"]
    for file in project["physicalInputs"]:
        assert file["actualSha256"].lower() == file["input"]["Sha256"].lower()
    for context in project["contexts"]:
        assert all(i["state"] == "verified" for i in context["inputs"])
    source = [r for file in project["sourceFiles"] for r in file["boundaries"]]
    bound = [r for file in project["convertedFiles"] for r in file["observation"].get("boundaries", [])]
    calls = [r for file in project["convertedFiles"] for r in file["observation"].get("expressionCalls", [])]
    assert not any("parseErrors" in f["observation"] for f in project["convertedFiles"])
    attempts = []
    for attempt in evidence["TestAttempts"]:
        result = attempt["Result"]
        valid = bool(result["Results"]) and not result["ParseErrors"] and not result["UsedConsoleFallback"]
        attempts.append({
            "leg": attempt["Leg"], "ordinal": attempt["Attempt"], "exit": result["ExitCode"],
            "validDenominator": valid, "total": result["TotalTests"] if valid else None,
            "passed": result["Passed"] if valid else None,
            "failed": result["Failed"] if valid else None,
            "skipped": result["Skipped"] if valid else None,
            "command": result.get("Command")
        })
    if name != "Serilog":
        assert {(a["leg"], a["ordinal"]) for a in attempts} == {
            ("baseline", 1), ("baseline", 2), ("candidate", 1), ("candidate", 2)}
        assert all(a["validDenominator"] and a["exit"] == 0 and a["failed"] == 0 for a in attempts)
    else:
        assert all(not a["validDenominator"] and a["exit"] != 0 for a in attempts)
    summaries.append({
        "project": name, "e1Counts": report["evidence_counts"], "fileOutcomes": report["files"],
        "attempts": attempts, "e1GateFailures": report["gate_failures"],
        "sourceFileContexts": len(project["sourceFiles"]), "sourceBoundaryObservations": len(source),
        "sourceCategories": dict(collections.Counter(r["category"] for r in source)),
        "sourceBoundariesByKind": dict(collections.Counter(r["boundary"] for r in source)),
        "sourceObligationFiles": sorted({f["file"] for f in project["sourceFiles"]
                                       if any(r["category"] == "genuinely-oblivious-source" for r in f["boundaries"])}),
        "sourceObliviousByOrigin": dict(collections.Counter(r["annotationOrigin"] for r in source
                                                          if r["category"] == "genuinely-oblivious-source")),
        "physicalDirectiveCount": sum(len(f["physicalNullableDirectiveLines"]) for f in project["physicalInputs"]),
        "contextNullable": [c["CompilationProperties"]["Nullable"] for c in project["contexts"]],
        "reconstructionErrorCount": [len(c["reconstructionErrors"]) for c in project["contexts"]],
        "referenceCounts": [len(c["references"]) for c in project["contexts"]],
        "calorBoundaryObservations": len(bound),
        "calorCurrentPredicateTrue": sum(r["currentPredicate"] for r in bound),
        "calorProjection": dict(collections.Counter(r["shadowConservative"] for r in bound)),
        "calorExpressionCalls": len(calls),
        "calorExpressionReceiverKinds": dict(collections.Counter(
            r["receiver"]["kind"] if r["receiver"] else "unobserved" for r in calls)),
        "calorStatementCallsUnassessed": sum(f["observation"].get("statementCallCount", 0) for f in project["convertedFiles"]),
        "candidateProductionRejections": None,
        "candidateRejectionDenominator": None,
        "acceptance": "Unadjudicated"
    })
    for file in report["file_detail"]:
        candidate = file["candidate"]
        for diagnostic in [None] + candidate["Diagnostics"] + candidate["AnalysisDiagnostics"]:
            classifications.append({
                "fileId": candidate["FileId"], "candidateId": candidate.get("CandidateId"),
                "diagnostic": diagnostic, "finalStatus": file["status"],
                "classification": "infrastructure-failure" if name == "Serilog" and file["status"] != "Excluded" else "unresolved",
                "analyst": "Copilot nominal-policy-1400 / ada009ab-5eb3-41ac-82cc-68fa9b4e158a; model unexposed",
                "review": "Unadjudicated; independent reviews and parent decision required",
                "rationale": "Configured exclusion retained, not conversion failure or safe coverage" if file["status"] == "Excluded" else
                    "NU1301 restore failure; no valid tests or conversion denominator" if name == "Serilog" else
                    "E1 observation retained, not accepted policy fallout; missing original/Calor identity join, selected map or faithful migration proof",
                "evidence": name + "-roundtrip.json",
            })
    for file in project["sourceFiles"]:
        for row in file["boundaries"]:
            if row["category"] not in ("genuinely-oblivious-source", "explicit-nullable", "explicit-null-expression"):
                continue
            classifications.append({
                "sourceBoundaryId": hashlib.sha256((name + "/" + file["file"] + ":" +
                    str(row["span"]["SpanStart"]) + ":" + row["boundary"]).encode()).hexdigest(),
                "file": file["file"], "candidateId": file["e1CandidateId"], "sourceSpan": row["span"],
                "category": row["category"], "classification": "unresolved",
                "analyst": "Copilot nominal-policy-1400 / ada009ab-5eb3-41ac-82cc-68fa9b4e158a; model unexposed",
                "rationale": "Source-side nullable/oblivious obligation only; not mapped to an actual resolved Calor receiving boundary",
                "evidence": "corpus-observations.json", "acceptance": "Unadjudicated"
            })
for name, value in [("summary.json", summaries), ("classification-draft.json", classifications)]:
    (args.output / name).write_text(json.dumps(value, indent=2) + "\n")
print("Validated five inventories, all declared subject attempts, observer calibration and 13 paired runtime outputs.")
