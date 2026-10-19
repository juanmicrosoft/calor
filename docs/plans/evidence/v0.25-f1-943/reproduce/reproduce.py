#!/usr/bin/env python3
"""0.25 F1 (#943) per-surface evidence. From the repository root, after download-z3.sh and a
Release build of src/Calor.Compiler and src/Calor.Runtime:
    python3 docs/plans/evidence/v0.25-f1-943/reproduce/reproduce.py
Reuses the R0 (#1426) driver and ProbeRunner unchanged (imported, not copied). Inputs: the R0 F1
fixtures (F1-REFOUT-01..08; F1-REFOUT-08 is also X-INT-01's witness), this packet's X-INT-05 and
X-INT-08 fixtures, and every D1 (#1427) C# witness (D1-CS-01..28) as a converter oracle. Surfaces:
`calor convert` default and --passthrough, `calor migrate` on a one-file project, and MCP
`calor_convert` default, passthroughOnError, and passthroughOnError + moduleName. Every .calr is
compiled with default options; when that fails only on effect diagnostics (Calor04xx) it is also
compiled with --permissive-effects (R0's compile_with_fallback) so behavior can be compared. Both
results are recorded. Generated C# runs against the original through the R0 ProbeRunner.
Classification per surface: native (no §CSHARP, no §CS{, no reported loss, compiled, matches),
preserved (§CSHARP or §CS{ present, compiled, matches), rejected (refused or compile error), or
mismatch. Set CALOR_F1_CONFIG=Debug to measure a Debug build. Writes results.json and generated/."""
import glob
import importlib.util
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location(
    "r0", os.path.join(HERE, "..", "..", "v0.25-r0-1426", "reproduce", "reproduce.py"))
r0 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r0)

CONFIG = os.environ.get("CALOR_F1_CONFIG", "Release")
r0.CALOR = os.path.join("src", "Calor.Compiler", "bin", CONFIG, "net10.0", "calor.dll")
ROOT = r0.ROOT
EVID = os.path.relpath(os.path.join(HERE, ".."), ROOT)
GEN = os.path.join(EVID, "generated")
R0_FIXTURES = os.path.join(r0.EVID, "fixtures")
F1_FIXTURES = os.path.join(EVID, "fixtures")
CONTRACT = os.path.join("docs", "plans", "evidence", "v0.25-d1-1427", "contract.json")
CLI_MODES = {"cli-default": [], "cli-passthrough": ["--passthrough"]}
# Rows and their allowed outcomes (scope.json `expected`; D1 `conversion` for the witnesses).
R0_ROWS = {f"F1-REFOUT-0{i}": None for i in range(1, 9)}
INTERACTION_ROWS = {"X-INT-05": "native-or-preserved", "X-INT-08": "native-or-preserved",
                    # F1 PR 2: the W1 (#1429) findings, fixed.
                    "P2-OUTVAR-PLACEMENTS": "native", "P2-OUTVAR-CALL-STATEMENT": "native",
                    "P2-OUTVAR-CHAINED": "preserved",
                    "P2-REFRETURN-EXPRESSION": "preserved"}


def cases():
    scope = json.load(open(os.path.join(ROOT, r0.EVID, "scope.json"), encoding="utf-8"))
    expected = {c["id"]: c["expected"] for c in scope["cases"]}
    for row in R0_ROWS:
        yield row, os.path.join(R0_FIXTURES, row + r0.CS), expected[row]
    for row, allowed in INTERACTION_ROWS.items():
        yield row, os.path.join(F1_FIXTURES, row + r0.CS), allowed
    contract = json.load(open(os.path.join(ROOT, CONTRACT), encoding="utf-8"))
    os.makedirs(os.path.join(ROOT, F1_FIXTURES, "d1"), exist_ok=True)
    for c in contract["cases"]:
        if c["kind"] == "csharp":
            path = os.path.join(F1_FIXTURES, "d1", c["id"] + r0.CS)
            source = c["source"] if isinstance(c["source"], str) else "\n".join(c["source"])
            open(os.path.join(ROOT, path), "w", encoding="utf-8").write(source + "\n")
            yield c["id"], path, c["conversion"]


def surface_from_calr(case, mode, calr_text, extra):
    calr = os.path.join(GEN, f"{case}.{mode}{r0.CALR}")
    open(os.path.join(ROOT, calr), "w", encoding="utf-8").write(calr_text)
    surf = dict(extra)
    surf["csharpInteropBlocks"] = calr_text.count("§CSHARP{")
    surf["rawCSharpExpressions"] = calr_text.count("§CS{")
    surf.update(r0.compile_with_fallback(calr, os.path.join(GEN, f"{case}.{mode}.g.cs")))
    return surf


def classify(surf):
    execution = surf.get("execution")
    if execution is None:
        return "rejected"
    if not execution.get("matchesOriginal"):
        return "mismatch"
    interop = surf.get("csharpInteropBlocks", 0) + surf.get("rawCSharpExpressions", 0)
    losses = surf.get("lossFeatures") or []
    return "preserved" if interop or losses else "native"


def allowed(outcome, expected):
    return outcome == expected or outcome in expected.split("-or-")


def main():
    git = lambda *a: subprocess.run(["git", *a], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    if git("status", "--porcelain", "--", "src") and os.environ.get("CALOR_F1_ALLOW_DIRTY") != "1":
        sys.exit("src/ has uncommitted changes; the measured compiler would not match any commit")
    shutil.rmtree(os.path.join(ROOT, GEN), ignore_errors=True)
    os.makedirs(os.path.join(ROOT, GEN))
    rows, probe_files = [], []
    for case, fixture, expected in cases():
        row = {"case": case, "fixture": fixture, "fixtureSha256": r0.sha(fixture), "expected": expected, "surfaces": {}}
        probe_files.append(fixture)
        source = os.path.join(r0.TMP, case + ".cs")
        shutil.copyfile(os.path.join(ROOT, fixture), source)
        for mode, flags in CLI_MODES.items():
            out_calr = os.path.join(r0.TMP, f"{case}.{mode}.calr")
            code, out, _ = r0.run(["convert", source, "-o", out_calr, "--format", "json", "--no-telemetry"] + flags)
            extra = {"convertExit": code} | r0.summarize_envelope(out)
            if os.path.exists(out_calr):
                row["surfaces"][mode] = surface_from_calr(case, mode, open(out_calr, encoding="utf-8").read(), extra)
            else:
                row["surfaces"][mode] = extra | {"output": "none (refused)"}
        project = os.path.join(r0.TMP, "migrate-" + case)
        os.makedirs(project)
        shutil.copyfile(os.path.join(ROOT, fixture), os.path.join(project, "Fixture.cs"))
        report = os.path.join(r0.TMP, case + ".migrate.json")
        code, _, _ = r0.run(["migrate", project, "--skip-verify", "--skip-analyze", "--no-telemetry", "--report", report])
        migrated = glob.glob(os.path.join(project, "**", "*.calr"), recursive=True)
        extra = {"migrateExit": code}
        try:
            data = json.load(open(report, encoding="utf-8")).get("data") or {}
            files = data.get("fileResults") or []
            extra["lossFeatures"] = sorted({l.get("feature") for f in files for l in f.get("losses") or []})
        except (OSError, json.JSONDecodeError):
            extra["lossFeatures"] = None
        row["surfaces"]["cli-migrate"] = (surface_from_calr(case, "cli-migrate", open(migrated[0], encoding="utf-8").read(), extra)
                                          if len(migrated) == 1 else extra | {"output": f"{len(migrated)} files"})
        rows.append(row)

    mcp = r0.mcp_convert([(row["case"], open(os.path.join(ROOT, row["fixture"]), encoding="utf-8").read()) for row in rows])
    for row in rows:
        for mode in r0.MCP_MODES:
            is_error, payload = mcp.get((row["case"], mode), (True, {"missing": True}))
            loss = payload.get("lossSummary") or {}
            extra = {"isError": is_error, "success": payload.get("success"),
                     "lossFeatures": sorted({l.get("feature") for l in loss.get("locations") or []})}
            calor = payload.get("calorSource")
            row["surfaces"]["mcp-" + mode] = (surface_from_calr(row["case"], "mcp-" + mode, calor, extra)
                                              if isinstance(calor, str) else extra)

    for row in rows:
        probe_files += [s["generated"] for s in row["surfaces"].values() if "generated" in s]
    os.environ["CALOR_RUNTIME_DLL"] = os.path.join(ROOT, "src", "Calor.Runtime", "bin", CONFIG, "net10.0", "Calor.Runtime.dll")
    runs = r0.probe(probe_files)
    for row in rows:
        original = runs.get(row["fixture"], {})
        row["original"] = {k: original.get(k) for k in ("compiled", "result", "exception")}
        for surf in row["surfaces"].values():
            if "generated" in surf:
                g = runs.get(surf.pop("generated"), {})
                surf["execution"] = {k: g.get(k) for k in ("compiled", "result", "exception")} | {
                    "matchesOriginal": g.get("compiled") is True
                    and (g.get("result"), g.get("exception")) == (original.get("result"), original.get("exception"))}
            surf["outcome"] = classify(surf)
        row["allowed"] = all(allowed(s["outcome"], row["expected"]) for s in row["surfaces"].values())

    # Generated C# is an intermediate; only the .calr.txt outputs are evidence (Calor-first guard).
    for path in glob.glob(os.path.join(ROOT, GEN, "*.g.cs")):
        os.remove(path)
    result = {"schema": "v0.25-f1-943-evidence/1", "measuredCommit": git("rev-parse", "HEAD"),
              "srcTree": git("rev-parse", "HEAD:src"), "build": CONFIG,
              "dotnetSdk": subprocess.run(["dotnet", "--version"], capture_output=True, text=True).stdout.strip(),
              "platform": f"{os.uname().sysname} {os.uname().machine}", "cases": rows}
    with open(os.path.join(ROOT, EVID, "results.json"), "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False)
        f.write("\n")
    for row in rows:
        print(row["case"], "OK " if row["allowed"] else "BAD", row["expected"],
              {k: s["outcome"] for k, s in row["surfaces"].items()})


if __name__ == "__main__":
    main()
