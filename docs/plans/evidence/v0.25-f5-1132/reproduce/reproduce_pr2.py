#!/usr/bin/env python3
"""0.25 F5 (#1132) PR 2 (narrowed) per-surface evidence. From the repository root, after
download-z3.sh and a Release build of src/Calor.Compiler and src/Calor.Runtime:
    python3 docs/plans/evidence/v0.25-f5-1132/reproduce/reproduce_pr2.py
Runs PR 1's driver (reproduce.py, imported unchanged) on the same surfaces and fixtures, and
writes pr2/results.json and pr2/generated/ so PR 1's records stay as history. One difference:
PR 2 drops the array-member effect certification, so array members are unknown calls, as on
main. Each .calr is compiled with default options first; when that fails only on effect
diagnostics (Calor04xx), it is also compiled with --permissive-effects, through the R0 driver's
compile_with_fallback, so behavior can still be compared. Both compile results are recorded."""
import importlib.util
import os

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("f5pr1", os.path.join(HERE, "reproduce.py"))
pr1 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pr1)
r0 = pr1.r0

pr1.EVID = os.path.join(pr1.EVID, "pr2")
pr1.GEN = os.path.join(pr1.EVID, "generated")


def surface_from_calr(case, mode, calr_text, extra):
    calr = os.path.join(pr1.GEN, f"{case}.{mode}{r0.CALR}")
    open(os.path.join(r0.ROOT, calr), "w", encoding="utf-8").write(calr_text)
    surf = dict(extra)
    surf["csharpInteropBlocks"] = calr_text.count("§CSHARP{")
    surf["rawCSharpExpressions"] = calr_text.count("§CS{")
    surf.update(r0.compile_with_fallback(calr, os.path.join(pr1.GEN, f"{case}.{mode}.g.cs")))
    return surf


pr1.surface_from_calr = surface_from_calr

if __name__ == "__main__":
    os.makedirs(os.path.join(r0.ROOT, pr1.EVID), exist_ok=True)
    pr1.main()
