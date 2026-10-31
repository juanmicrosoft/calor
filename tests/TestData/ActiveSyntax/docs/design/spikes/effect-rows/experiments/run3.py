#!/usr/bin/env python3
"""Round-3 experiments (Z-series): the adversarial cases the round-2
consistency lens raised, plus the non-function-typed-position cases N5 needs.

Every case here is quoted in docs/design/effect-rows-in-the-type-system.md.
Run: python3 run3.py [substring ...]
"""
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

# --- N1: does the "later line" branch reach a §E arm at every position? ---
case("Z1-FLD-nextline-E", """
    §M{Z1}
      §CL{C:pub}
        §FLD{i32:x:priv}
        §E{cw}
        §MT{M:pub} () -> void
          §E{}
""")
case("Z2-B-nextline-E", """
    §M{Z2}
      §F{Main:pub} () -> void
        §E{}
        §B{y:i32} INT:1
        §E{cw}
""")
case("Z3-wrapped-inline-sig-E-inside-parens", """
    §M{Z3}
      §F{Apply:pub} (
          Func<i32,i32>:transform
          §E{cw}
        ) -> i32
        §E{cw}
        §R INT:0
""")
# --- N2 / Z4 / Z6: eff-name collisions ---
case("Z4-typeparam-named-eff", """
    §M{Z4}
      §F{M:pub}<eff> (eff:x) -> void
        §E{}
""")
case("Z6-typeparam-named-cw", """
    §M{Z6}
      §F{M:pub}<T, cw> (T:a, cw:b) -> void
        §E{}
""")
case("Z6b-typeparam-named-fs", """
    §M{Z6}
      §F{M:pub}<T, fs> (T:a, fs:b) -> void
        §E{}
""")
# --- Z5: do inline parameter lists really wrap? ---
case("Z5-wrapped-inline-parameter-list", """
    §M{Z5}
      §F{Apply:pub} (
          i32:a,
          i32:b
        ) -> i32
        §E{}
        §R (+ a b)
""")
# --- N3 / N4: where can an eff variable be declared at all? ---
case("Z7-class-typeparam-reaches-member", """
    §M{Z7}
      §CL{Box:pub}<T>
        §FLD{T:value:priv}
        §MT{Get:pub} () -> T
          §E{}
          §R value
""")
case("Z8-DEL-typeparam-list", """
    §M{Z8}
      §DEL{Handler}<T>
        §I{T:x}
        §O{void}
        §E{}
      §F{Main:pub} () -> void
        §E{}
""")
case("Z8b-LAM-typeparam-list", """
    §M{Z8}
      §F{Main:pub} () -> void
        §E{}
        §B{f} §LAM{}<T> §R INT:1 §/LAM
""")
case("Z7b-IFACE-typeparam-reaches-member", """
    §M{Z7}
      §IFACE{IBox}<T>
        §MT{Get} () -> T
          §E{}
""")
# --- N5: a same-line row on a NON-function-typed position ---
case("Z9-arrow-void-sameline-E", """
    §M{Z9}
      §F{Log:pub} (i32:x) -> void §E{cw}
        §P x
""")
case("Z9b-I-i32-sameline-E", """
    §M{Z9}
      §F{Log:pub}
        §I{i32:x} §E{cw}
        §O{void}
        §P x
""")
case("Z9c-O-i32-sameline-E", """
    §M{Z9}
      §F{Get:pub}
        §O{i32} §E{cw}
        §R INT:1
""")
# --- minor: §E inside a §C ... §/C argument list ---
case("Z10-E-inside-call-arguments", """
    §M{Z10}
      §F{Helper:pub} (i32:x) -> i32
        §E{}
        §R x
      §F{Main:pub} () -> i32
        §E{}
        §R §C{Helper} §A INT:1 §E{cw} §/C
""")
# --- W1 (round 3): can a MEMBER carry its own type-parameter list on an
# interface and on the implementing class? If so, R2's middleware spelling can
# bind `eff e` at position 1 (already permitted by §7.3) instead of needing a
# class/interface-level binder that §7.3 forbids.
case("W1a-IFACE-member-typeparams", """
    §M{W1}
      §IFACE{IP}
        §MT{Handle}<T> (T:r) -> T
          §E{}
""")
case("W1b-IFACE-and-impl-member-typeparams", """
    §M{W1}
      §IFACE{IP}
        §MT{Handle}<T> (T:r) -> T
          §E{}
      §CL{Logging:pub}
        §IMPL{IP}
        §MT{Handle:pub}<T> (T:r) -> T
          §E{}
          §R r
""")
# W1c: alpha-equivalence of member type parameters across the interface
# boundary -- the property `fits` will need for a member-level `eff`.
# StrictnessBatchTests.cs:172 pins the analogous property for OVERRIDES.
case("W1c-IFACE-impl-member-typeparams-renamed", """
    §M{W1}
      §IFACE{IP}
        §MT{Handle}<T> (T:r) -> T
          §E{}
      §CL{Logging:pub}
        §IMPL{IP}
        §MT{Handle:pub}<U> (U:r) -> U
          §E{cw}
          §P r
          §R r
""")

# Current Y2a nullable/throw replacement, not a replay of the retired string error channel.
case("Z11-fallible-brace-form-full-output", """
    §M{Z11}
      §F{Parse:pub}
        §I{?str:s}
        §O{str}
        §E{alloc,throw}
        §R (?? s §TH §NEW{ArgumentException} §A "Missing value" §/NEW)
""")


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
