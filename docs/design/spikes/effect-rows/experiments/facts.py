#!/usr/bin/env python3
"""Re-measure every number the three review lenses disputed, at the worktree HEAD."""
import os, subprocess, re

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..", ".."))
os.chdir(ROOT)

# --- determinism ---------------------------------------------------------
# CI runs Linux, the doc was authored on macOS. Three things differ and all
# three broke the first version of this harness:
#   * recursive grep / glob results come back in FILESYSTEM order, which is
#     not the same on APFS and ext4  -> every multi-file result is sorted;
#   * collation depends on the locale                 -> LC_ALL=C everywhere;
#   * a shallow CI checkout has no git history        -> no fact is derived
#     from `git log`; see the F-3 probe at the end of facts2.py.
_ENV = {**os.environ, "LC_ALL": "C", "LANG": "C"}


def sh(cmd):
    """Run a shell probe with a fixed locale. Output is NOT reordered."""
    return subprocess.run(
        cmd, shell=True, capture_output=True, text=True, env=_ENV).stdout.strip()


def sh_sorted(cmd):
    """Run a shell probe and sort the lines deterministically.

    Lines shaped `path:line:text` sort by path then by NUMERIC line, so the
    output is stable regardless of the order the filesystem handed the files
    to grep. Anything else sorts bytewise.
    """
    lines = [line for line in sh(cmd).split("\n") if line != ""]

    def key(line):
        parts = line.split(":", 2)
        if len(parts) >= 2 and parts[1].isdigit():
            return (parts[0], int(parts[1]), line)
        return (line, 0, line)

    return "\n".join(sorted(lines, key=key))

print("### BindStatementNode / BindingNode")
print("BindingNode hits:", sh("grep -rn '\\bBindingNode\\b' src/ | wc -l"))
print(sh_sorted("grep -rn 'class BindStatementNode' src/"))
print(sh_sorted("grep -rn 'class ParameterNode\\|class OutputNode\\|class ClassFieldNode' src/"))

print("\n### ast-schema / ArchitectureTests")
print("eng/ast-schema.json exists:", os.path.exists(os.path.join(ROOT, "eng/ast-schema.json")))
print(sh("grep -n 'AstSchema_CoversEveryNodeDispatchAndChildRelation' tests/Calor.Compiler.Tests/ArchitectureTests.cs"))
print(sh("grep -c 'BindStatementNode' eng/ast-schema.json"))

print("\n### cross-module IsSubsetOf site")
# Line numbers stripped (v0.16 W5 review): the pin is WHICH files/sites call
# IsSubsetOf, not where in the file — every insertion above the site used to
# break the transcript.
print(sh_sorted("grep -n 'IsSubsetOf' src/Calor.Compiler/Effects/*.cs | sed -E 's/:[0-9]+:/:/'"))

print("\n### ProjectIndex references outside Commands/")
print(sh_sorted("grep -rln 'ProjectIndex' src/ --include='*.cs' | sort"))

print("\n### Effects/*.cs file count")
print(sh("ls src/Calor.Compiler/Effects/*.cs | wc -l"))
print(sh_sorted("ls src/Calor.Compiler/Effects/*.cs"))

print("\n### tests/TestData function-typed .calr")
print("count:", sh("grep -rlE 'Func<|Action<|Action[}:]|Predicate<|§DEL|§LAM' tests/TestData --include='*.calr' | wc -l"))

print("\n### whole-corpus function-typed positions (IsFunctionTypeName shapes)")
# `docs/design/spikes/` is excluded: the emitter spike commits before/after .calr
# fixtures as EVIDENCE, and they are deliberately full of Func<>/Action<>. They
# are artifacts, not corpus, so counting them would make the design doc's "5
# shapes in 5 files, all conversion snapshots" (§1) false by self-reference.
# HigherOrderDemandLedgerTests and LosslessFormattingTests exclude the same path
# for the same reason. This line does not change the count.
CORPUS = "git ls-files '*.calr' | grep -v '^docs/design/spikes/'"
print(sh(f"{CORPUS} | xargs grep -hoE 'Func<|Action<|Action[}}:]|Predicate<|Comparison<|Converter<|EventHandler' 2>/dev/null | wc -l"))
print(sh_sorted(f"{CORPUS} | xargs grep -lE 'Func<|Action<|Action[}}:]|Predicate<|Comparison<|Converter<|EventHandler' 2>/dev/null"))

print("\n### Conversion tests: effect pass?")
print(sh("sed -n '38,72p' tests/Calor.Conversion.Tests/TestHelpers.cs"))

print("\n### orphaned / mis-cited pins")
for ln in [152,172,219,245,260,472,502,555,582,587,607,640,656,728,745,612,106,133]:
    print(f"StrictnessBatchTests.cs:{ln}:", sh(f"sed -n '{ln}p' tests/Calor.Enforcement.Tests/StrictnessBatchTests.cs"))

print("\n### demand ledger test exact-equality")
print(sh("sed -n '186,200p' tests/Calor.Compiler.Tests/Effects/HigherOrderDemandLedgerTests.cs"))

print("\n### BoundTypeTests DisplayString pins")
print(sh_sorted("grep -n 'DisplayString' tests/Calor.Compiler.Tests/Binding/BoundTypes/BoundTypeTests.cs"))

print("\n### calor-direction.md lines 33, 57, 112")
for ln in [33, 57, 112]:
    print(f":{ln}:", sh(f"sed -n '{ln}p' docs/design/calor-direction.md")[:150])

print("\n### roadmap gate-1 class enumeration")
print(sh("sed -n '/1. \\*\\*Effect laundering, closed classes/,/Discriminating pin/p' docs/plans/roadmap-v0.13-v0.15.md"))
