# Real-Scale Benchmark — Design Sketch (Option A, not built)

**Status:** Design only, per the 2026-07-14 Option B decision (strategy §9).

Premise: agent slippage exists on real codebases (hundreds of functions,
incomplete specs), not authorable fixtures. Escaped-bugs measurement at
that scale requires generated, not authored, tasks.

Sketch: reuse tools/Calor.RoundTrip.Harness project corpus. For each OSS
project with a green test suite: (1) convert to Calor (existing pipeline);
(2) MUTATION-INJECTION: derive tasks by reverting a real upstream bug-fix
commit or injecting a semantic mutation whose covering test is REMOVED
from the visible suite and moved to held-out; (3) both arms receive the
same failing-behavior report (not the test) and must fix it; (4) held-out
= the removed tests plus the project's full suite (regression net).
Escaped bug = held-out failure at declared-done. Difficulty comes free
from real code; arms compare on identical semantic content via the
converter. Open problems: converter coverage bias (shape-C# corpus —
direction doc postscript), effect-manifest gaps on arbitrary dependencies,
run cost (full test suites per iteration), and attribution when the
converter itself introduces divergence (needs a conversion-fidelity gate
per project). Estimated build: 4-6 weeks. Revisit alongside the
next-model-generation re-run.
