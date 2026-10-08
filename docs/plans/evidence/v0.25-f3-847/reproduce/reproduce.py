#!/usr/bin/env python3
"""0.25 F3 (#847) per-surface evidence. From the repository root, after download-z3.sh and a
Release build of src/Calor.Compiler and src/Calor.Runtime:
    python3 docs/plans/evidence/v0.25-f3-847/reproduce/reproduce.py
Reuses the R0 (#1426) driver and execution oracle unchanged: every F3 fixture (the four R0
fixtures plus F3-LOCAL-05 here) is converted through `calor convert` (default, --passthrough)
and MCP `calor_convert` (default, passthroughOnError, passthroughOnError + moduleName), each
output is compiled with default options (and, only if that fails on Calor04xx alone, again with
--permissive-effects), and Probe.Run() of the original and generated C# is compared.
Writes results.json and generated/ next to this directory."""
import glob
import importlib.util
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", ".."))
EVID = os.path.relpath(os.path.join(HERE, ".."), ROOT)
R0 = os.path.join(ROOT, "docs", "plans", "evidence", "v0.25-r0-1426")

spec = importlib.util.spec_from_file_location("r0", os.path.join(R0, "reproduce", "reproduce.py"))
r0 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r0)
GEN = os.path.join(EVID, "generated")
r0.GEN = GEN
CS, CALR = r0.CS, r0.CALR

FIXTURES = sorted(glob.glob(os.path.join(R0, "fixtures", "F3-*" + CS))) + sorted(
    glob.glob(os.path.join(ROOT, EVID, "fixtures", "*" + CS)))


def surface_summary(text, compiled):
    return {"csharpInteropBlocks": text.count("§CSHARP{"), "inlineInteropExpressions": text.count("§CS{"),
            "nestedLocalFunctions": sum(1 for line in text.splitlines()
                                        if line.startswith("      ") and line.lstrip().startswith("§F{"))} | compiled


def main():
    git = lambda *a: subprocess.run(["git", *a], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    if git("status", "--porcelain", "--", "src"):
        sys.exit("src/ has uncommitted changes; the measured compiler would not match any commit")
    shutil.rmtree(os.path.join(ROOT, GEN), ignore_errors=True)
    os.makedirs(os.path.join(ROOT, GEN))
    cases, probe_files = [], []
    for path in FIXTURES:
        case = os.path.basename(path)[:-len(CS)]
        rel = os.path.relpath(path, ROOT)
        row = {"case": case, "fixture": rel, "fixtureSha256": r0.sha(rel), "surfaces": {}}
        probe_files.append(rel)
        source = os.path.join(r0.TMP, case + ".cs")
        shutil.copyfile(path, source)
        for mode, extra in (("cli-default", []), ("cli-passthrough", ["--passthrough"])):
            calr = os.path.join(GEN, f"{case}.{mode}{CALR}")
            code, out, err = r0.run(["convert", source, "-o", calr, "--format", "json", "--no-telemetry"] + extra)
            surf = {"convertExit": code} | r0.summarize_envelope(out)
            if os.path.exists(os.path.join(ROOT, calr)):
                text = open(os.path.join(ROOT, calr), encoding="utf-8").read()
                surf |= surface_summary(text, r0.compile_with_fallback(calr, os.path.join(GEN, f"{case}.{mode}.g.cs")))
                if "generated" in surf:
                    probe_files.append(surf["generated"])
            row["surfaces"][mode] = surf
        cases.append(row)

    mcp = r0.mcp_convert([(c["case"], open(os.path.join(ROOT, c["fixture"]), encoding="utf-8").read()) for c in cases])
    for row in cases:
        for mode in r0.MCP_MODES:
            is_error, payload = mcp.get((row["case"], mode), (True, {"missing": True}))
            loss = payload.get("lossSummary") or {}
            surf = {"isError": is_error, "success": payload.get("success"),
                    "lossFeatures": sorted({l.get("feature") for l in loss.get("locations") or []})}
            calor = payload.get("calorSource")
            if isinstance(calor, str):
                calr = os.path.join(GEN, f"{row['case']}.mcp-{mode}{CALR}")
                open(os.path.join(ROOT, calr), "w", encoding="utf-8").write(calor)
                surf |= surface_summary(calor, r0.compile_with_fallback(
                    calr, os.path.join(GEN, f"{row['case']}.mcp-{mode}.g.cs")))
                if "generated" in surf:
                    probe_files.append(surf["generated"])
            row["surfaces"]["mcp-" + mode] = surf

    runs = r0.probe(probe_files)
    for row in cases:
        original = runs.get(row["fixture"], {})
        row["original"] = {k: original.get(k) for k in ("compiled", "result", "exception")}
        for name, surf in row["surfaces"].items():
            if "generated" in surf:
                g = runs.get(surf["generated"], {})
                surf["execution"] = {k: g.get(k) for k in ("compiled", "result", "exception")} | {
                    "compileMode": surf.get("generatedMode", "default"),
                    "matchesOriginal": g.get("compiled") is True
                    and (g.get("result"), g.get("exception")) == (original.get("result"), original.get("exception"))}
            native = surf.get("csharpInteropBlocks") == 0 and surf.get("inlineInteropExpressions") == 0
            surf["outcome"] = (("native" if native else "preserved") + "-"
                               + ("match" if surf.get("execution", {}).get("matchesOriginal") else "mismatch")
                               + ("" if surf.get("generatedMode") is None else "@" + surf["generatedMode"]))

    sdk = subprocess.run(["dotnet", "--version"], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    report = {"schema": "v0.25-f3-847-results/1", "measuredCommit": git("rev-parse", "HEAD"),
              "srcTree": git("rev-parse", "HEAD:src"),
              "environment": {"dotnetSdk": sdk, "platform": f"{os.uname().sysname} {os.uname().machine}",
                              "compiler": r0.CALOR, "compilerConfiguration": "Release"},
              "cases": cases}
    out = json.dumps(report, indent=2, ensure_ascii=False, default=r0.norm)
    out = out.replace(ROOT + "/", "").replace(ROOT, "<repo>")
    open(os.path.join(ROOT, EVID, "results.json"), "w", encoding="utf-8").write(out + "\n")
    for path in glob.glob(os.path.join(ROOT, GEN, "*.g.cs")):
        os.remove(path)
    for row in cases:
        print(row["case"], {k: v["outcome"] for k, v in row["surfaces"].items()})


if __name__ == "__main__":
    main()
