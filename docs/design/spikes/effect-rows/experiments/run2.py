#!/usr/bin/env python3
"""Round 2 experiments: discriminating cases for the same-line rule,
the fallible-type brace form, and the §B suffix position."""
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

# Y1 — DISCRIMINATOR for the §I same-line collision. The function's own §E
# is pure and on its own line; a same-line §E{cw} sits on the §I. If the
# flat loop treats both as the declaration's row, the LAST one wins and the
# function is pure -> Calor0410 fires on the §P.
case("Y1a-I-sameline-cw-then-decl-pure", """
    §M{m001:Y1}
      §F{f001:Log:pub}
        §I{str:m} §E{cw}
        §O{void}
        §E{}
        §P m
""")
# Y1b — same, but with NO later §E: the same-line §E{cw} is the only one.
case("Y1b-I-sameline-cw-only", """
    §M{m001:Y1}
      §F{f001:Log:pub}
        §I{str:m} §E{cw}
        §O{void}
        §P m
""")
# Y1c — same-line §E{} on §I, declaration §E{cw} later: if the LAST wins the
# function is impure and compiles.
case("Y1c-I-sameline-pure-then-decl-cw", """
    §M{m001:Y1}
      §F{f001:Log:pub}
        §I{str:m} §E{}
        §O{void}
        §E{cw}
        §P m
""")

# Y2 — the fallible-type suffix in the BRACE form the docs use
# (docs/syntax-reference/effects.md:140 is cited by the consistency lens).
case("Y2a-fallible-brace-form", """
    §M{m001:Y2}
      §F{f001:Parse:pub}
        §I{str:s}
        §O{str!str}
        §E{}
        §R §OK s
""")
case("Y2b-fallible-in-B", """
    §M{m001:Y2}
      §F{f001:Main:pub} () -> void
        §E{}
        §B{r:i32!str} §OK INT:1
""")

# Y3 — the §B suffix position (position 6c).
case("Y3a-B-with-sameline-E", """
    §M{m001:Y3}
      §F{f001:Main:pub} () -> void
        §E{}
        §B{f:Func<i32,i32>} §E{cw} §LAM{lam1:x:i32} (+ x 1) §/LAM{lam1}
""")
case("Y3b-B-plain", """
    §M{m001:Y3}
      §F{f001:Main:pub} () -> void
        §E{}
        §B{f:Func<i32,i32>} §LAM{lam1:x:i32} (+ x 1) §/LAM{lam1}
""")

# Y4 — the §O suffix position with a function type, discriminating.
case("Y4a-O-sameline-E-decl-later", """
    §M{m001:Y4}
      §F{f001:Make:pub}
        §O{Func<i32>} §E{cw}
        §E{}
        §R §LAM{lam1} §R INT:1 §/LAM{lam1}
""")

# Y5 — arrow-return same-line §E (position 6b).
case("Y5a-arrow-sameline-E", """
    §M{m001:Y5}
      §F{f001:Log:pub} (str:m) -> void §E{cw}
        §P m
""")

# Y6 — §MT (method) two-line §O/§E, to confirm position 1 covers §MT.
case("Y6a-MT-twoline", """
    §M{m001:Y6}
      §CL{c001:C:pub}
        §MT{mt001:Log:pub}
          §I{str:m}
          §O{void}
          §E{}
          §P m
""")

# Y7 — an interface method's §E (site 5's destination row today).
case("Y7a-IFACE-E", """
    §M{m001:Y7}
      §IFACE{i001:IRenderer}
        §MT{m001:Render}
          §O{void}
          §E{}
      §CL{c001:ConsoleRenderer:pub}
        §IMPL{IRenderer}
        §MT{mt001:Render:pub}
          §O{void}
          §E{cw}
          §P "rendering"
""")

# Y8 — permissive demotion of Calor0420 (consistency lens M2 / :517-519).
case("Y8a-override-broader-permissive", """
    §M{m001:Y8}
      §CL{c001:Base:pub}
        §MT{mt001:Render:pub:virt}
          §O{void}
          §E{}
      §CL{c002:Derived:pub}
        §EXT{Base}
        §MT{mt002:Render:pub:over}
          §O{void}
          §E{cw}
          §P "laundered"
""", ["--permissive-effects"])
case("Y8b-override-broader-strict", """
    §M{m001:Y8}
      §CL{c001:Base:pub}
        §MT{mt001:Render:pub:virt}
          §O{void}
          §E{}
      §CL{c002:Derived:pub}
        §EXT{Base}
        §MT{mt002:Render:pub:over}
          §O{void}
          §E{cw}
          §P "laundered"
""")

# Y9 — §B{f} §LAM with no type and no row: today's inferred path (§5.4 / M7).
case("Y9a-B-untyped-lambda-then-invoke-permissive", """
    §M{m001:Y9}
      §F{f001:UseLambda:pub} () -> i32
        §E{}
        §B{f} §LAM{lam1:x:i32} (+ x 1) §/LAM{lam1}
        §R §C{f} §A INT:1 §/C
""", ["--permissive-effects"])


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
