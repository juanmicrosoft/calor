#!/usr/bin/env python3
"""0.25 R0 (#1426) baseline reproduction.

Run from the repository root after `src/Calor.Compiler/scripts/download-z3.sh` and
`dotnet build src/Calor.Compiler/Calor.Compiler.csproj -c Release`:

    python3 docs/plans/evidence/v0.25-r0-1426/reproduce/reproduce.py

For every fixture under fixtures/ it runs the real converter through three public surfaces
(CLI `calor convert` with no flags, CLI with `--passthrough`, and the MCP `calor_convert` tool
with default arguments), compiles each produced .calr with the real `calor` compiler, and
executes `Probe.Run()` on the original and every generated C# file with ProbeRunner.cs.txt.
It also scans website/content/**/*.mdx for complete ```calor programs (first non-blank line
starts with §M) and records which ones the current compiler rejects (W0 / #1143 baseline).

Writes baseline-results.json and generated/ next to this folder. Absolute paths are
normalized to <repo> so the output is comparable across machines.
"""
import concurrent.futures
import glob
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", ".."))
EVID = os.path.relpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."), ROOT)
CALOR = os.path.join("src", "Calor.Compiler", "bin", "Release", "net10.0", "calor.dll")
GEN = os.path.join(EVID, "generated")
# Calor files here use .calr.txt: every tracked *.calr is part of the repository's Calor corpus,
# which ledger tests count. The compiler accepts the extension unchanged.
CALR = ".calr.txt"
# C# fixtures and the runner use .cs.txt: the Calor-first guard (scripts/check-calor-first-diff.sh)
# rejects new .cs files outside tests/ and bench/. They are copied to a temporary .cs to run.
CS = ".cs.txt"
TMP = tempfile.mkdtemp(prefix="r0-1426-")
MCP_MODES = {"default": {}, "passthroughOnError": {"passthroughOnError": True},
             "passthroughOnError-moduleName": {"passthroughOnError": True, "moduleName": "Custom"}}


def norm(text):
    return text.replace(ROOT + os.sep, "").replace(ROOT, "<repo>") if isinstance(text, str) else text


def run(args, stdin=None):
    p = subprocess.run(["dotnet", CALOR] + args, cwd=ROOT, input=stdin, capture_output=True,
                       text=True, env=dict(os.environ, CALOR_TELEMETRY="0", DOTNET_NOLOGO="1"))
    return p.returncode, p.stdout, p.stderr


def sha(path):
    with open(os.path.join(ROOT, path), "rb") as f:
        return hashlib.sha256(f.read().replace(b"\r\n", b"\n")).hexdigest()


def summarize_envelope(stdout):
    try:
        env = json.loads(stdout)
    except json.JSONDecodeError:
        return {"envelope": "unparseable"}
    data = env.get("data") or {}
    return {k: data.get(k) for k in ("success", "nativeConversionCount", "interopPreservationCount",
                                     "lossySubstitutionCount", "dropCount")} | {
        "lossFeatures": sorted({l.get("feature") for l in data.get("losses") or []}),
        "errorCodes": sorted({d["code"] for d in env.get("diagnostics", []) if d.get("severity") == "error"}),
    }


def attribution(stderr):
    m = re.search(r"preserved as §CSHARP interop block\(s\)([^—]*)—", stderr)
    return m.group(1).strip() if m else None


def compile_calr(calr, out_cs, extra=()):
    code, out, err = run(["-i", calr, "-o", out_cs, "--format", "json", "--no-cache"] + list(extra))
    try:
        diags = json.loads(out).get("diagnostics", [])
    except json.JSONDecodeError:
        diags = []
    errors = [d for d in diags if d.get("severity") == "error"]
    first = errors[0] if errors else None
    return {"exit": code, "errorCodes": sorted({d["code"] for d in errors}),
            "warningCodes": sorted({d["code"] for d in diags if d.get("severity") == "warning"}),
            "firstError": norm(first["message"]) if first else None,
            "firstErrorLine": (first.get("location") or {}).get("line") if first else None}


def compile_with_fallback(calr, gcs):
    """Default compile; when it fails only on effect diagnostics (Calor04xx), also compile with
    --permissive-effects (the mode the CLI help names for converted code) so behavior can still
    be compared. Both results are recorded; the default result is never replaced."""
    res = {"compile": compile_calr(calr, gcs)}
    if res["compile"]["exit"] == 0:
        res["generated"] = gcs
    elif res["compile"]["errorCodes"] and all(c.startswith("Calor04") for c in res["compile"]["errorCodes"]):
        res["compilePermissiveEffects"] = compile_calr(calr, gcs, ["--permissive-effects"])
        if res["compilePermissiveEffects"]["exit"] == 0:
            res["generated"] = gcs
            res["generatedMode"] = "permissive-effects"
    return res


def mcp_convert(cases):
    """One MCP stdio session: calor_convert with default args, with passthroughOnError=true, and
    (a separate witness) passthroughOnError=true plus a custom moduleName."""
    msgs = [{"jsonrpc": "2.0", "id": 0, "method": "initialize",
             "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                        "clientInfo": {"name": "r0-1426", "version": "1"}}}]
    ids = {}
    for i, (case, src) in enumerate(cases):
        for j, (mode, extra) in enumerate(MCP_MODES.items()):
            rid = 1 + i * len(MCP_MODES) + j
            ids[rid] = (case, mode)
            msgs.append({"jsonrpc": "2.0", "id": rid, "method": "tools/call",
                         "params": {"name": "calor_convert",
                                    # Only `source`: every other argument keeps the tool default.
                                    "arguments": {"source": src} | extra}})
    code, out, _ = run(["mcp", "--stdio", "--no-telemetry"], stdin="\n".join(json.dumps(m) for m in msgs) + "\n")
    results = {}
    for line in out.splitlines():
        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue
        if msg.get("id") not in ids:
            continue
        result = msg.get("result") or {}
        text = "".join(c.get("text", "") for c in result.get("content", []) if c.get("type") == "text")
        try:
            payload = json.loads(text)
        except json.JSONDecodeError:
            payload = {"raw": text[:400]}
        results[ids[msg["id"]]] = (result.get("isError", False), payload)
    return results


def probe(files):
    # Run outside the repository so its central package management and lock files do not apply.
    runner_dir = os.path.join(TMP, "runner")
    os.makedirs(runner_dir, exist_ok=True)
    runner = os.path.join(runner_dir, "ProbeRunner.cs")
    shutil.copyfile(os.path.join(ROOT, EVID, "reproduce", "ProbeRunner" + CS), runner)
    p = subprocess.run(["dotnet", "run", runner, "--"] + [os.path.join(ROOT, f) for f in files],
                       cwd=runner_dir, capture_output=True, text=True,
                       env=dict(os.environ, CALOR_RUNTIME_DLL=os.path.join(
                           ROOT, "src", "Calor.Runtime", "bin", "Release", "net10.0", "Calor.Runtime.dll")))
    out = {}
    for line in p.stdout.splitlines():
        if line.startswith("{"):
            r = json.loads(line)
            out[os.path.relpath(r["file"], ROOT)] = r
    if not out:
        sys.exit("ProbeRunner failed:\n" + p.stdout + p.stderr)
    return out


def website_scan():
    blocks = []
    for path in sorted(glob.glob(os.path.join(ROOT, "website", "content", "**", "*.mdx"), recursive=True)):
        text = open(path, encoding="utf-8").read()
        for n, m in enumerate(re.finditer(r"```calor[^\n]*\n(.*?)```", text, re.S), start=1):
            body = m.group(1)
            first = next((l for l in body.splitlines() if l.strip()), "")
            if first.lstrip().startswith("§M"):
                blocks.append((os.path.relpath(path, ROOT), n, body))
    tmp = tempfile.mkdtemp(prefix="r0-1426-web-")

    def check(item):
        rel, n, body = item
        src = os.path.join(tmp, f"{abs(hash((rel, n)))}.calr")
        open(src, "w", encoding="utf-8").write(body)
        res = compile_calr(src, src + ".g.cs")
        return {"file": rel, "calorFence": n, "exit": res["exit"], "errorCodes": res["errorCodes"],
                "parseError": any(c.startswith(("Calor00", "Calor01", "Calor08")) for c in res["errorCodes"])}

    with concurrent.futures.ThreadPoolExecutor(8) as pool:
        rows = list(pool.map(check, blocks))
    failing = [r for r in rows if r["exit"] != 0]
    return {"completeProgramFences": len(rows), "rejectedByCompiler": len(failing),
            "rejectedWithParseErrors": sum(r["parseError"] for r in failing), "rejected": failing,
            "note": "Default compile of each ```calor fence whose first non-blank line starts with §M "
                    "(the self-check check-6 rule). A rejection is not necessarily a defect: some "
                    "pages show intentionally failing examples.",
            "selfCheckDocsReadsWebsite": "website" in open(os.path.join(
                ROOT, "src", "Calor.Compiler", "Commands", "SelfCheckCommand.cs"), encoding="utf-8").read()}


def main():
    shutil.rmtree(os.path.join(ROOT, GEN), ignore_errors=True)
    os.makedirs(os.path.join(ROOT, GEN))
    git = lambda *a: subprocess.run(["git", *a], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    head = git("rev-parse", "HEAD")
    # The compiler under test is identified by its source tree, so a rerun on a docs-only commit
    # on top of the measured base is comparable; uncommitted src/ changes are refused.
    if git("status", "--porcelain", "--", "src"):
        sys.exit("src/ has uncommitted changes; the measured compiler would not match any commit")
    src_tree = git("rev-parse", "HEAD:src")
    fixtures = sorted(glob.glob(os.path.join(ROOT, EVID, "fixtures", "*" + CS)))
    cases, probe_files = [], []
    for path in fixtures:
        case = os.path.basename(path)[:-len(CS)]
        rel = os.path.relpath(path, ROOT)
        row = {"case": case, "fixture": rel, "fixtureSha256": sha(rel), "surfaces": {}}
        probe_files.append(rel)
        source = os.path.join(TMP, case + ".cs")  # the converter keys direction on the extension
        shutil.copyfile(path, source)
        for mode, extra in (("cli-default", []), ("cli-passthrough", ["--passthrough"])):
            calr = os.path.join(GEN, f"{case}.{mode}{CALR}")
            code, out, err = run(["convert", source, "-o", calr, "--format", "json", "--no-telemetry"] + extra)
            surf = {"convertExit": code} | summarize_envelope(out) | {"humanAttribution": attribution(err)}
            if os.path.exists(os.path.join(ROOT, calr)):
                text = open(os.path.join(ROOT, calr), encoding="utf-8").read()
                surf["csharpInteropBlocks"] = text.count("§CSHARP{")
                surf["inlineInteropExpressions"] = text.count("§CS{")
                surf |= compile_with_fallback(calr, os.path.join(GEN, f"{case}.{mode}.g.cs"))
                if "generated" in surf:
                    probe_files.append(surf["generated"])
            row["surfaces"][mode] = surf
        cases.append(row)

    # Native Calor witnesses (no C# original): compile only and record diagnostics with lines.
    calr_cases = []
    for path in sorted(glob.glob(os.path.join(ROOT, EVID, "fixtures", "*" + CALR))):
        rel = os.path.relpath(path, ROOT)
        case = os.path.basename(path)[:-len(CALR)]
        calr_cases.append({"case": case, "fixture": rel, "fixtureSha256": sha(rel),
                           "compile": compile_calr(rel, os.path.join(GEN, f"{case}.g.cs"))})

    mcp = mcp_convert([(c["case"], open(os.path.join(ROOT, c["fixture"]), encoding="utf-8").read()) for c in cases])
    for row in cases:
        for mode in MCP_MODES:
            is_error, payload = mcp.get((row["case"], mode), (True, {"missing": True}))
            surf = {"isError": is_error}
            calor = payload.get("calorSource")
            surf["success"] = payload.get("success")
            loss = payload.get("lossSummary") or {}
            for k in ("nativeConversions", "interopPreservations", "lossySubstitutions", "drops"):
                surf[k] = loss.get(k)
            surf["lossFeatures"] = sorted({l.get("feature") for l in loss.get("locations") or []})
            if isinstance(calor, str):
                calr = os.path.join(GEN, f"{row['case']}.mcp-{mode}{CALR}")
                open(os.path.join(ROOT, calr), "w", encoding="utf-8").write(calor)
                surf["csharpInteropBlocks"] = calor.count("§CSHARP{")
                surf["inlineInteropExpressions"] = calor.count("§CS{")
                surf |= compile_with_fallback(calr, os.path.join(GEN, f"{row['case']}.mcp-{mode}.g.cs"))
                if "generated" in surf:
                    probe_files.append(surf["generated"])
            else:
                surf["payloadKeys"] = sorted(payload.keys())
            row["surfaces"]["mcp-" + mode] = surf

    runs = probe(probe_files)
    for row in cases:
        original = runs.get(row["fixture"], {})
        row["original"] = {k: original.get(k) for k in ("compiled", "result", "exception")}
        for surf in row["surfaces"].values():
            if "generated" in surf:
                g = runs.get(surf["generated"], {})
                surf["execution"] = {k: g.get(k) for k in ("compiled", "result", "exception")} | {
                    "compileMode": surf.get("generatedMode", "default"),
                    "errors": g.get("errors", [])[:3],
                    "matchesOriginal": g.get("compiled") is True
                    and (g.get("result"), g.get("exception")) == (original.get("result"), original.get("exception"))}

    sdk = subprocess.run(["dotnet", "--version"], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    report = {"schema": "v0.25-r0-1426-baseline/1", "measuredCommit": head, "srcTree": src_tree,
              "environment": {"dotnetSdk": sdk, "platform": f"{os.uname().sysname} {os.uname().machine}",
                              "python": sys.version.split()[0], "compiler": CALOR,
                              "compilerConfiguration": "Release", "generatedCSharpRetained": False},
              "cases": cases, "calorCases": calr_cases, "website": website_scan()}
    out = json.dumps(report, indent=2, ensure_ascii=False, default=norm)
    out = out.replace(ROOT + "/", "").replace(ROOT, "<repo>")
    open(os.path.join(ROOT, EVID, "baseline-results.json"), "w", encoding="utf-8").write(out + "\n")
    # Generated C# is large (#line directives) and fully reproducible from the retained .calr
    # files; only the .calr outputs are kept as evidence.
    for path in glob.glob(os.path.join(ROOT, GEN, "*.g.cs")):
        os.remove(path)
    print(f"wrote {EVID}/baseline-results.json ({len(cases)} cases)")


if __name__ == "__main__":
    main()
