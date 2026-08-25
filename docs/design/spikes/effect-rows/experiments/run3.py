#!/usr/bin/env python3
"""Round-3 experiments (Z-series): the adversarial cases the round-2
consistency lens raised, plus the non-function-typed-position cases N5 needs.

Every case here is quoted in docs/design/effect-rows-in-the-type-system.md.
Run: python3 run3.py [substring ...]
"""
import os, subprocess, sys, textwrap

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
EXP = os.path.dirname(os.path.abspath(__file__))

CASES = {}
def case(name, src, args=None):
    CASES[name] = (textwrap.dedent(src).lstrip("\n"), args or [])

# --- N1: does the "later line" branch reach a §E arm at every position? ---
case("Z1-FLD-nextline-E", """
    §M{m001:Z1}
      §CL{c001:C:pub}
        §FLD{i32:x:pri}
        §E{cw}
        §MT{mt001:M:pub} () -> void
          §E{}
""")
case("Z2-B-nextline-E", """
    §M{m001:Z2}
      §F{f001:Main:pub} () -> void
        §E{}
        §B{y:i32} INT:1
        §E{cw}
""")
case("Z3-wrapped-inline-sig-E-inside-parens", """
    §M{m001:Z3}
      §F{f001:Apply:pub} (
          Func<i32,i32>:transform
          §E{cw}
        ) -> i32
        §E{cw}
        §R INT:0
""")
# --- N2 / Z4 / Z6: eff-name collisions ---
case("Z4-typeparam-named-eff", """
    §M{m001:Z4}
      §F{f001:M:pub}<eff> (eff:x) -> void
        §E{}
""")
case("Z6-typeparam-named-cw", """
    §M{m001:Z6}
      §F{f001:M:pub}<T, cw> (T:a, cw:b) -> void
        §E{}
""")
case("Z6b-typeparam-named-fs", """
    §M{m001:Z6}
      §F{f001:M:pub}<T, fs> (T:a, fs:b) -> void
        §E{}
""")
# --- Z5: do inline parameter lists really wrap? ---
case("Z5-wrapped-inline-parameter-list", """
    §M{m001:Z5}
      §F{f001:Apply:pub} (
          i32:a,
          i32:b
        ) -> i32
        §E{}
        §R (+ a b)
""")
# --- N3 / N4: where can an eff variable be declared at all? ---
case("Z7-class-typeparam-reaches-member", """
    §M{m001:Z7}
      §CL{c001:Box:pub}<T>
        §FLD{T:value:pri}
        §MT{mt001:Get:pub} () -> T
          §E{}
          §R value
""")
case("Z8-DEL-typeparam-list", """
    §M{m001:Z8}
      §DEL{d001:Handler}<T>
        §I{T:x}
        §O{void}
        §E{}
      §F{f001:Main:pub} () -> void
        §E{}
""")
case("Z8b-LAM-typeparam-list", """
    §M{m001:Z8}
      §F{f001:Main:pub} () -> void
        §E{}
        §B{f} §LAM{lam1}<T> §R INT:1 §/LAM{lam1}
""")
case("Z7b-IFACE-typeparam-reaches-member", """
    §M{m001:Z7}
      §IFACE{i001:IBox}<T>
        §MT{mt001:Get} () -> T
          §E{}
""")
# --- N5: a same-line row on a NON-function-typed position ---
case("Z9-arrow-void-sameline-E", """
    §M{m001:Z9}
      §F{f001:Log:pub} (i32:x) -> void §E{cw}
        §P x
""")
case("Z9b-I-i32-sameline-E", """
    §M{m001:Z9}
      §F{f001:Log:pub}
        §I{i32:x} §E{cw}
        §O{void}
        §P x
""")
case("Z9c-O-i32-sameline-E", """
    §M{m001:Z9}
      §F{f001:Get:pub}
        §O{i32} §E{cw}
        §R INT:1
""")
# --- minor: §E inside a §C ... §/C argument list ---
case("Z10-E-inside-call-arguments", """
    §M{m001:Z10}
      §F{f001:Helper:pub} (i32:x) -> i32
        §E{}
        §R x
      §F{f002:Main:pub} () -> i32
        §E{}
        §R §C{Helper} §A INT:1 §E{cw} §/C
""")
# --- Y2a re-run, so the doc can quote the REAL output rather than a paraphrase ---
case("Z11-fallible-brace-form-full-output", """
    §M{m001:Z11}
      §F{f001:Parse:pub}
        §I{str:s}
        §O{str!str}
        §E{}
        §R §OK s
""")


def run(name, src, args):
    path = os.path.join(EXP, name + ".calr")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(src)
    out = os.path.join(EXP, name + ".g.cs")
    proc = subprocess.run(["dotnet", DLL, "-i", path, "-o", out] + args,
                          capture_output=True, text=True, cwd=EXP)
    print("=" * 78)
    print("CASE", name, " args:", " ".join(args) or "(none)", " exit:", proc.returncode)
    print("-" * 78)
    print(src.rstrip())
    print("-" * 78)
    body = (proc.stdout + proc.stderr).strip().replace(EXP + "/", "")
    print(body if body else "(no output)")
    print()


if __name__ == "__main__":
    wanted = sys.argv[1:]
    for name, (src, args) in CASES.items():
        if wanted and not any(w in name for w in wanted):
            continue
        run(name, src, args)
