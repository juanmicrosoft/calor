#!/usr/bin/env python3
"""Compile every committed .calr that contains a two-line §O / §E pair — the
54 occurrences in 23 files that Decision 1 must not disturb. Records today's
verdict per file as the baseline the E2 PR re-runs."""
import os, subprocess, os, re, json
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..", ".."))
def _find_dll():
    override = os.environ.get("CALOR_DLL")
    if override:
        return override
    for cfg in ("Debug", "Release"):
        p = os.path.join(ROOT, "src/Calor.Compiler/bin", cfg, "net10.0/calor.dll")
        if os.path.exists(p):
            return p
    raise SystemExit(
        "calor.dll not found under src/Calor.Compiler/bin/{Debug,Release}/net10.0/. "
        "Run: dotnet build src/Calor.Compiler")


DLL = _find_dll()
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "o53")
os.makedirs(OUT, exist_ok=True)
os.chdir(ROOT)

files = subprocess.run(["git","ls-files","*.calr"],capture_output=True,text=True).stdout.split()
hits=[]
for f in files:
    L=open(f,encoding="utf-8",errors="replace").read().split("\n")
    n=sum(1 for i,l in enumerate(L) if re.search(r"§O\{[^}]*\}\s*$", l) and i+1<len(L) and re.match(r"\s*§E\{", L[i+1]))
    if n: hits.append((f,n))

results=[]
for f,n in sorted(hits):
    out = os.path.join(OUT, os.path.basename(os.path.dirname(f)) + "_" + os.path.basename(f) + ".g.cs")
    p = subprocess.run(["dotnet", DLL, "-i", f, "-o", out], capture_output=True, text=True)
    body = (p.stdout + p.stderr).strip()
    codes = sorted(set(re.findall(r"Calor\d{4}", body)))
    results.append({"file": f, "twoLineOE": n, "exit": p.returncode, "codes": codes})
    print(f"{p.returncode}  n={n:2d}  {f:70s} {','.join(codes) or 'OK'}")

green = [r for r in results if r["exit"] == 0]
print()
print(f"files: {len(results)}   occurrences: {sum(r['twoLineOE'] for r in results)}")
print(f"compile-green today: {len(green)}   compile-red today: {len(results)-len(green)}")
print("red files and their first code:")
for r in results:
    if r["exit"] != 0:
        print("   ", r["file"], r["codes"][:1])
sha = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
ledger = {
    "schemaVersion": 1,
    "measuredCommit": sha,
    "scope": (
        "Every committed .calr containing a line matching §O{...} at end-of-line immediately "
        "followed by a line matching §E{, compiled one file at a time via the CLI default "
        "(EnforceEffects on, UnknownCallPolicy.Strict, no --permissive-effects). "
        "twoLineOE is the occurrence count in that file; exit and codes are the compiler's "
        "verdict at measuredCommit. Design-doc Decision 1 must not disturb any of these."),
    "fileCount": len(results),
    "occurrenceCount": sum(r["twoLineOE"] for r in results),
    "compileGreen": len(green),
    "compileRed": len(results) - len(green),
    "files": results,
}
with open(os.path.join(OUT, "baseline.json"), "w") as fh:
    json.dump(ledger, fh, indent=2)
    fh.write("\n")
