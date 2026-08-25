#!/usr/bin/env python3
"""Executed experiments for the effect-rows design doc, Draft v2.

Each case writes a .calr, compiles it with the worktree-built compiler, and
prints the exact stdout/stderr so the doc can quote it.
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

# ---------------------------------------------------------------- X1 / X2
# The canonical two-line §O / §E form (54 corpus occurrences) vs the
# same-line form (0 corpus occurrences). Both must bind §E to the
# DECLARATION today; X1b/X2b prove it by making the declaration pure and
# the body impure.
case("X1a-twoline-O-then-E", """
    §M{m001:X1}
      §F{f001:Log:pub}
        §I{str:m}
        §O{void}
        §E{cw}
        §P m
""")
case("X1b-twoline-O-then-E-pure", """
    §M{m001:X1}
      §F{f001:Log:pub}
        §I{str:m}
        §O{void}
        §E{}
        §P m
""")
case("X2a-sameline-O-and-E", """
    §M{m001:X2}
      §F{f001:Log:pub}
        §I{str:m}
        §O{void} §E{cw}
        §P m
""")
case("X2b-sameline-O-and-E-pure", """
    §M{m001:X2}
      §F{f001:Log:pub}
        §I{str:m}
        §O{void} §E{}
        §P m
""")

# ---------------------------------------------------------------- X3
# Draft v1's `!e` sigil, run through the real parser.
case("X3-bang-sigil-in-E", """
    §M{m001:X3}
      §F{f001:Map:pub}
        §O{void}
        §E{!e}
""")
case("X3b-bang-sigil-mixed", """
    §M{m001:X3}
      §F{f001:Map:pub}
        §O{void}
        §E{alloc, !e}
""")

# ---------------------------------------------------------------- X4
# The live fallible-type suffix `T!E` that owns `!` in the type grammar.
case("X4-fallible-type-suffix", """
    §M{m001:X4}
      §F{f001:Parse:pub} (str:s) -> str!str
        §E{}
        §R §OK s
""")

# ---------------------------------------------------------------- X5
# Alternative sigils, to see which the lexer/ParseValue already own.
case("X5a-caret-sigil", """
    §M{m001:X5}
      §F{f001:Map:pub}
        §O{void}
        §E{^e}
""")
case("X5b-bare-identifier", """
    §M{m001:X5}
      §F{f001:Map:pub}
        §O{void}
        §E{e}
""")

# ---------------------------------------------------------------- X6
# The chosen spelling: `eff e` in the existing type-parameter list,
# mirroring the in/out variance branch. Must FAIL today.
case("X6a-eff-modifier-in-typaram-list", """
    §M{m001:X6}
      §F{f001:Map:pub}<T, U, eff e> ([T]:xs) -> [U]
        §E{}
        §R §ARR{U} INT:0
""")
case("X6b-variance-modifier-on-function", """
    §M{m001:X6}
      §F{f001:Map:pub}<T, out U> ([T]:xs) -> [U]
        §E{}
        §R §ARR{U} INT:0
""")

# ---------------------------------------------------------------- X7-X9
# Positions that already parse §E today (2, 3) and positions that do not
# (4, 7).
case("X7-LAM-with-E", """
    §M{m001:X7}
      §F{f001:Main:pub} () -> void
        §E{}
        §B{f} §LAM{lam1:x:i32} §E{} (+ x 1) §/LAM{lam1}
""")
case("X8-DEL-with-E", """
    §M{m001:X8}
      §DEL{d1:Transform}
        §I{i32:x}
        §O{i32}
        §E{cw}
      §F{f001:Main:pub} () -> void
        §E{}
""")
case("X9a-I-with-sameline-E", """
    §M{m001:X9}
      §F{f001:Apply:pub}
        §I{Func<i32,i32>:transform} §E{cw}
        §I{i32:value}
        §O{i32}
        §E{cw}
        §R §C{transform} §A value §/C
""")
case("X9b-FLD-with-sameline-E", """
    §M{m001:X9}
      §CL{c001:Counter:pub}
        §FLD{Action<i32>:onChange:pri} §E{cw}
        §MT{mt001:Bump:pub} (i32:n) -> void
          §E{cw}
          §C{onChange} §A n §/C
""")
case("X9c-inline-param-with-E", """
    §M{m001:X9}
      §F{f001:Apply:pub} (Func<i32,i32>:transform §E{cw}, i32:value) -> i32
        §E{cw}
        §R §C{transform} §A value §/C
""")

# ---------------------------------------------------------------- X10-X12
# The BEFORE behaviour the doc's worked examples quote.
case("X10-E1-before-calor0418", """
    §M{m001:Ex1}
      §F{f001:Apply:pub} (Func<i32,i32>:transform, i32:value) -> i32
        §E{cw}
        §R §C{transform} §A value §/C
""")
case("X11-E3-before-silent-laundering", """
    §M{m001:Ex3}
      §F{f001:Apply:pub} (Func<i32,i32>:transform, i32:value) -> i32
        §E{}
        §R INT:0

      §F{f002:Shout:pub} (i32:x) -> i32
        §E{cw}
        §P x
        §R x

      §F{f003:Main:pub} () -> i32
        §E{cw}
        §R §C{Apply} §A Shout §A INT:1 §/C
""")
case("X12-calor0410-real-message", """
    §M{m001:X12}
      §F{f001:Log:pub} (str:m) -> void
        §E{}
        §P m
""")
case("X12b-calor0410-two-effects", """
    §M{m001:X12}
      §F{f001:Log:pub} (str:m) -> void
        §E{}
        §P m
        §C{System.IO.File.WriteAllText} §A m §A m §/C
""")

# ---------------------------------------------------------------- X13
# Field-level: does a row-less function-typed field invocation reach 0418?
case("X13-field-delegate-invocation", """
    §M{m001:X13}
      §CL{c001:Counter:pub}
        §FLD{Action<i32>:onChange:pri}
        §MT{mt001:Bump:pub} (i32:n) -> void
          §E{}
          §C{onChange} §A n §/C
""")

# ---------------------------------------------------------------- X14
# The §LAM-bound-local rejection the doc's §5 changes.
case("X14-lambda-bound-local", """
    §M{m001:X14}
      §F{f001:UseLambda:pub} () -> i32
        §E{}
        §B{f} §LAM{lam1:x:i32} (+ x 1) §/LAM{lam1}
        §R §C{f} §A INT:1 §/C
""")


def run(name, src, args):
    path = os.path.join(EXP, name + ".calr")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(src)
    out = os.path.join(EXP, name + ".g.cs")
    proc = subprocess.run(
        ["dotnet", DLL, "-i", path, "-o", out] + args,
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
