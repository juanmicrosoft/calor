#!/usr/bin/env python3
"""Round 2 experiments: discriminating cases for the same-line rule,
the fallible-type brace form, and the §B suffix position."""
import os, subprocess, sys, textwrap

EXP = os.environ["CALOR_EXPERIMENT_ORIGINAL_DIRECTORY"]
sys.path.insert(0, EXP)
from transcript_output import transcript_output

ROOT = os.path.abspath(os.path.join(EXP, "..", "..", "..", "..", ".."))
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


# --- scratch directory ----------------------------------------------------
# The live test supplies excluded build-output scratch and removes it afterwards.
WORK = os.environ["CALOR_EXPERIMENT_WORKDIR"]
os.makedirs(WORK, exist_ok=True)

CASES = {}
def case(name, src, args=None):
    CASES[name] = (textwrap.dedent(src).lstrip("\n"), args or [])

# Y1 — DISCRIMINATOR for the §I same-line collision. The function's own §E
# is pure and on its own line; a same-line §E{cw} sits on the §I. If the
# flat loop treats both as the declaration's row, the LAST one wins and the
# function is pure -> Calor0410 fires on the §P.
case("Y1a-I-sameline-cw-then-decl-pure", """
    §M{Y1}
      §F{Log:pub}
        §I{str:m} §E{cw}
        §O{void}
        §E{}
        §P m
""")
# Y1b — same, but with NO later §E: the same-line §E{cw} is the only one.
case("Y1b-I-sameline-cw-only", """
    §M{Y1}
      §F{Log:pub}
        §I{str:m} §E{cw}
        §O{void}
        §P m
""")
# Y1c — same-line §E{} on §I, declaration §E{cw} later: if the LAST wins the
# function is impure and compiles.
case("Y1c-I-sameline-pure-then-decl-cw", """
    §M{Y1}
      §F{Log:pub}
        §I{str:m} §E{}
        §O{void}
        §E{cw}
        §P m
""")

# Y2 — current nullable/throw replacements for the retired T!E/§OK probes.
# No arbitrary string error payload is represented by these current inputs.
case("Y2a-fallible-brace-form", """
    §M{Y2}
      §F{Parse:pub}
        §I{?str:s}
        §O{str}
        §E{alloc,throw}
        §R (?? s §TH §NEW{ArgumentException} §A "Missing value" §/NEW)
""")
case("Y2b-fallible-in-B", """
    §M{Y2}
      §F{Main:pub} () -> void
        §E{}
        §B{r:?i32} INT:1
""")

# Y3 — the §B suffix position (position 6c).
case("Y3a-B-with-sameline-E", """
    §M{Y3}
      §F{Main:pub} () -> void
        §E{}
        §B{f:Func<i32,i32>} §E{cw} §LAM{x:i32} (+ x 1) §/LAM
""")
case("Y3b-B-plain", """
    §M{Y3}
      §F{Main:pub} () -> void
        §E{}
        §B{f:Func<i32,i32>} §LAM{x:i32} (+ x 1) §/LAM
""")

# Y4 — the §O suffix position with a function type, discriminating.
case("Y4a-O-sameline-E-decl-later", """
    §M{Y4}
      §F{Make:pub}
        §O{Func<i32>} §E{cw}
        §E{}
        §R §LAM{} §R INT:1 §/LAM
""")

# Y5 — arrow-return same-line §E (position 6b).
case("Y5a-arrow-sameline-E", """
    §M{Y5}
      §F{Log:pub} (str:m) -> void §E{cw}
        §P m
""")

# Y6 — §MT (method) two-line §O/§E, to confirm position 1 covers §MT.
case("Y6a-MT-twoline", """
    §M{Y6}
      §CL{C:pub}
        §MT{Log:pub}
          §I{str:m}
          §O{void}
          §E{}
          §P m
""")

# Y7 — an interface method's §E (site 5's destination row today).
case("Y7a-IFACE-E", """
    §M{Y7}
      §IFACE{IRenderer}
        §MT{Render}
          §O{void}
          §E{}
      §CL{ConsoleRenderer:pub}
        §IMPL{IRenderer}
        §MT{Render:pub}
          §O{void}
          §E{cw}
          §P "rendering"
""")

# Y8 — permissive demotion of Calor0420 (consistency lens M2 / :517-519).
case("Y8a-override-broader-permissive", """
    §M{Y8}
      §CL{Base:pub}
        §MT{Render:pub:virt}
          §O{void}
          §E{}
      §CL{Derived:pub}
        §EXT{Base}
        §MT{Render:pub:over}
          §O{void}
          §E{cw}
          §P "laundered"
""", ["--permissive-effects"])
case("Y8b-override-broader-strict", """
    §M{Y8}
      §CL{Base:pub}
        §MT{Render:pub:virt}
          §O{void}
          §E{}
      §CL{Derived:pub}
        §EXT{Base}
        §MT{Render:pub:over}
          §O{void}
          §E{cw}
          §P "laundered"
""")

# Y9 — §B{f} §LAM with no type and no row: today's inferred path (§5.4 / M7).
case("Y9a-B-untyped-lambda-then-invoke-permissive", """
    §M{Y9}
      §F{UseLambda:pub} () -> i32
        §E{}
        §B{f} §LAM{x:i32} (+ x 1) §/LAM
        §R §C{f} §A INT:1 §/C
""", ["--permissive-effects"])


def run(name, src, args):
    path = os.path.join(WORK, name + ".calr")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(src)
    out = os.path.join(WORK, name + ".g.cs")
    proc = subprocess.run(["dotnet", DLL, "-i", path, "-o", out, "--format", "json"] + args,
                          capture_output=True, text=True, cwd=WORK)
    print("=" * 78)
    print("CASE", name, " args:", " ".join(args) or "(none)", " exit:", proc.returncode)
    print("-" * 78)
    print(src.rstrip())
    print("-" * 78)
    body = transcript_output(proc, out).strip().replace(WORK + "/", "")
    print(body if body else "(no output)")
    print()


if __name__ == "__main__":
    wanted = sys.argv[1:]
    for name, (src, args) in CASES.items():
        if wanted and not any(w in name for w in wanted):
            continue
        run(name, src, args)
