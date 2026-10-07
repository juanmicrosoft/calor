#!/usr/bin/env python3
"""0.25 F4 (#1139) per-surface evidence. From the repository root, after download-z3.sh and a
Release build of src/Calor.Compiler and src/Calor.Runtime:
    python3 docs/plans/evidence/v0.25-f4-1139/reproduce/reproduce.py
Reuses the R0 (#1426) driver and oracle unchanged (imported, not copied). For the registered F4
C# fixtures it runs: `calor convert` with no flags, with --passthrough and with --no-fallback;
`calor migrate` on a one-file project; and MCP `calor_convert` default, passthroughOnError, and
passthroughOnError + moduleName. Every produced .calr is compiled with the default options (effects
enforced) and its C# is run against the original through the R0 ProbeRunner. F4-ITER-03 (native
§YIELD in an accessor) is compiled as the Calor0209 control. Writes results.json and generated/."""
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

ROOT = r0.ROOT
EVID = os.path.relpath(os.path.join(HERE, ".."), ROOT)
GEN = os.path.join(EVID, "generated")
R0_FIXTURES = os.path.join(r0.EVID, "fixtures")
CASES = ["F4-ITER-01", "F4-ITER-02"]
CLI_MODES = {"cli-default": [], "cli-passthrough": ["--passthrough"], "cli-no-fallback": ["--no-fallback"]}


def surface_from_calr(case, mode, calr_text, extra):
    calr = os.path.join(GEN, f"{case}.{mode}{r0.CALR}")
    open(os.path.join(ROOT, calr), "w", encoding="utf-8").write(calr_text)
    surf = dict(extra)
    surf["yieldTokensInOutput"] = calr_text.count("§YIELD") + calr_text.count("§YBRK")
    surf["csharpInteropBlocks"] = calr_text.count("§CSHARP{")
    # Default compile only: no --permissive-effects fallback is needed or used for F4.
    surf["compile"] = r0.compile_calr(calr, os.path.join(GEN, f"{case}.{mode}.g.cs"))
    if surf["compile"]["exit"] == 0:
        surf["generated"] = os.path.join(GEN, f"{case}.{mode}.g.cs")
    return surf


def main():
    git = lambda *a: subprocess.run(["git", *a], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    if git("status", "--porcelain", "--", "src"):
        sys.exit("src/ has uncommitted changes; the measured compiler would not match any commit")
    shutil.rmtree(os.path.join(ROOT, GEN), ignore_errors=True)
    os.makedirs(os.path.join(ROOT, GEN))
    rows, probe_files = [], []
    for case in CASES:
        fixture = os.path.join(R0_FIXTURES, case + r0.CS)
        row = {"case": case, "fixture": fixture, "fixtureSha256": r0.sha(fixture), "surfaces": {}}
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
        code, _, _ = r0.run(["migrate", project, "--skip-verify", "--skip-analyze", "--no-telemetry",
                             "--report", report])
        migrated = glob.glob(os.path.join(project, "**", "*.calr"), recursive=True)
        extra = {"migrateExit": code}
        try:
            data = json.load(open(report, encoding="utf-8")).get("data") or {}
            files = data.get("fileResults") or []
            extra["lossFeatures"] = sorted({l.get("feature") for f in files for l in f.get("losses") or []})
            extra["issueFeatures"] = sorted({i.get("feature") for f in files for i in f.get("issues") or []} - {None})
            extra["unsupportedFeatures"] = (data.get("summary") or {}).get("unsupportedFeatures")
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

    control = os.path.join(R0_FIXTURES, "F4-ITER-03" + r0.CALR)
    result = {"schema": "v0.25-f4-1139-evidence/1", "measuredCommit": git("rev-parse", "HEAD"),
              "srcTree": git("rev-parse", "HEAD:src"),
              "dotnetSdk": subprocess.run(["dotnet", "--version"], cwd=ROOT, capture_output=True, text=True).stdout.strip(),
              "platform": f"{os.uname().sysname} {os.uname().machine}", "cases": rows,
              "controls": [{"case": "F4-ITER-03", "fixture": control, "fixtureSha256": r0.sha(control),
                            "compile": r0.compile_calr(control, os.path.join(GEN, "F4-ITER-03.g.cs"))}]}
    out = json.dumps(result, indent=2, ensure_ascii=False, default=r0.norm)
    out = out.replace(r0.TMP + "/", "<tmp>/").replace(ROOT + "/", "").replace(ROOT, "<repo>")
    open(os.path.join(ROOT, EVID, "results.json"), "w", encoding="utf-8").write(out + "\n")
    for path in glob.glob(os.path.join(ROOT, GEN, "*.g.cs")):
        os.remove(path)
    print(f"wrote {EVID}/results.json")


if __name__ == "__main__":
    main()
