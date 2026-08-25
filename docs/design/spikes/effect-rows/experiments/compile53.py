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
# --- the ledger: regenerate vs verify -------------------------------------
# P29 runs this script on every test run, so writing unconditionally would
# dirty a committed gate-5 instrument and make P30's "measuredCommit is a
# 40-hex SHA" leg self-fulfilling (its sibling would have just written a fresh
# one). Same discipline as CALOR_REGENERATE_S5_LEDGER: opt in to write.
#
# Both modes print the SAME lines, so the transcript is mode-independent.
LEDGER = os.path.join(OUT, "baseline.json")
SCOPE = (
    "Every committed .calr containing a line matching \u00a7O{...} at end-of-line immediately "
    "followed by a line matching \u00a7E{, compiled one file at a time via the CLI default "
    "(EnforceEffects on, UnknownCallPolicy.Strict, no --permissive-effects). "
    "twoLineOE is the occurrence count in that file; exit and codes are the compiler's "
    "verdict at measuredCommit. Design-doc Decision 1 must not disturb any of these.")

recomputed = {
    "fileCount": len(results),
    "occurrenceCount": sum(r["twoLineOE"] for r in results),
    "compileGreen": len(green),
    "compileRed": len(results) - len(green),
    "files": results,
}

if os.environ.get("CALOR_WRITE_O53_BASELINE") == "1":
    sha = subprocess.run(["git", "rev-parse", "HEAD"],
                         capture_output=True, text=True).stdout.strip()
    with open(LEDGER, "w", encoding="utf-8") as fh:
        json.dump({"schemaVersion": 1, "measuredCommit": sha, "scope": SCOPE,
                   **recomputed}, fh, indent=2)
        fh.write("\n")

# Verify in BOTH modes: after a regenerate this is a read-back check, and in
# the default (test) mode it is the actual comparison against the committed
# instrument. The printed verdict is identical either way.
with open(LEDGER, encoding="utf-8") as fh:
    committed = json.load(fh)

mismatches = [k for k in ("fileCount", "occurrenceCount", "compileGreen", "compileRed", "files")
              if committed.get(k) != recomputed[k]]
print()
if mismatches:
    print("baseline.json MISMATCH on: " + ", ".join(mismatches))
    print("Regenerate with CALOR_WRITE_O53_BASELINE=1 and review the diff.")
else:
    print(f"baseline.json verified: {recomputed['fileCount']} files / "
          f"{recomputed['occurrenceCount']} occurrences / "
          f"{recomputed['compileGreen']} green / {recomputed['compileRed']} red")
