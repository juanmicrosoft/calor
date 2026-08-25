#!/usr/bin/env python3
"""Re-measure every number the three review lenses disputed, at the worktree HEAD."""
import os, subprocess, re, os, json, glob

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..", ".."))
os.chdir(ROOT)

def sh(cmd):
    return subprocess.run(cmd, shell=True, capture_output=True, text=True).stdout.strip()

print("### BindStatementNode / BindingNode")
print("BindingNode hits:", sh("grep -rn '\\bBindingNode\\b' src/ | wc -l"))
print(sh("grep -rn 'class BindStatementNode' src/"))
print(sh("grep -rn 'class ParameterNode\\|class OutputNode\\|class ClassFieldNode' src/"))

print("\n### ast-schema / ArchitectureTests")
print(sh("ls -la eng/ast-schema.json 2>&1 | head -1"))
print(sh("grep -n 'AstSchema_CoversEveryNodeDispatchAndChildRelation' tests/Calor.Compiler.Tests/ArchitectureTests.cs"))
print(sh("grep -c 'BindStatementNode' eng/ast-schema.json"))

print("\n### cross-module IsSubsetOf site")
print(sh("grep -n 'IsSubsetOf' src/Calor.Compiler/Effects/*.cs"))

print("\n### ProjectIndex references outside Commands/")
print(sh("grep -rln 'ProjectIndex' src/ | sort"))

print("\n### Effects/*.cs file count")
print(sh("ls src/Calor.Compiler/Effects/*.cs | wc -l"))
print(sh("ls src/Calor.Compiler/Effects/*.cs"))

print("\n### tests/TestData function-typed .calr")
print("count:", sh("grep -rlE 'Func<|Action<|Action[}:]|Predicate<|§DEL|§LAM' tests/TestData --include='*.calr' | wc -l"))

print("\n### whole-corpus function-typed positions (IsFunctionTypeName shapes)")
print(sh("git ls-files '*.calr' | xargs grep -hoE 'Func<|Action<|Action[}:]|Predicate<|Comparison<|Converter<|EventHandler' 2>/dev/null | wc -l"))
print(sh("git ls-files '*.calr' | xargs grep -lE 'Func<|Action<|Action[}:]|Predicate<|Comparison<|Converter<|EventHandler' 2>/dev/null"))

print("\n### Conversion tests: effect pass?")
print(sh("sed -n '38,72p' tests/Calor.Conversion.Tests/TestHelpers.cs"))

print("\n### orphaned / mis-cited pins")
for ln in [152,172,219,245,260,472,502,555,582,587,607,640,656,728,745,612,106,133]:
    print(f"StrictnessBatchTests.cs:{ln}:", sh(f"sed -n '{ln}p' tests/Calor.Enforcement.Tests/StrictnessBatchTests.cs"))

print("\n### demand ledger test exact-equality")
print(sh("sed -n '186,200p' tests/Calor.Compiler.Tests/Effects/HigherOrderDemandLedgerTests.cs"))

print("\n### BoundTypeTests DisplayString pins")
print(sh("sed -n '130,148p' tests/Calor.Compiler.Tests/Binding/BoundTypeTests.cs 2>/dev/null || grep -rn 'DisplayString' tests/ --include='BoundTypeTests.cs'"))

print("\n### calor-direction.md lines 33, 57, 112")
for ln in [33, 57, 112]:
    print(f":{ln}:", sh(f"sed -n '{ln}p' docs/design/calor-direction.md")[:150])

print("\n### roadmap gate-1 class enumeration")
print(sh("sed -n '/1. \\*\\*Effect laundering, closed classes/,/Discriminating pin/p' docs/plans/roadmap-v0.13-v0.15.md"))
