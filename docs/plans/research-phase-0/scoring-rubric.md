# Scoring Rubric — Phase 0

**Locked once committed. Changes after first model run = rubric-v2 file, not edits here.**

## Primary metric

For each run, compute a single score:

```
RawScore = correctness × invariant_preservation × (1 - regression_rate)
PrimaryMetric = RawScore / dollar_cost
```

Where:
- `correctness ∈ {0, 1}`: 1 iff the new tests for the requested feature pass; 0 otherwise.
- `invariant_preservation ∈ [0, 1]`: fraction of the 7 invariant tests still passing post-edit.
- `regression_rate ∈ [0, 1]`: fraction of the pre-existing tests (excluding the new feature's tests) that newly fail.
- `dollar_cost`: actual API cost of the run in USD.

Aggregate across N=5 trials per (language, prompt) cell using the **mean**. Report median + IQR alongside.

## Pass criterion (pre-committed)

Calor "passes" if, for at least one of (T1.A, T1.B, T1.C):

> `mean(PrimaryMetric_calor) ≥ 1.50 × mean(PrimaryMetric_csharp)` at N=5

AND

> The improvement is not driven entirely by `dollar_cost` (i.e., raw `correctness × invariant × (1 - regression_rate)` is also non-inferior, within 10%).

The second clause prevents Calor from "winning" by being so much faster that it loses correctness — which would be a hollow win for a wide-audience bar.

## Secondary metrics (reported, not pass criteria)

| Metric | What it measures | Why secondary |
|--------|------------------|---------------|
| Turn count | Number of model turns to completion | Correlated with cost; signal redundant |
| Tokens (in/out) | Token usage | Component of cost |
| Tool call count | Tools invoked | Diagnostic for tool-friction effects |
| Time to completion | Wall clock | Dominated by tool latency, not language quality |
| Code-quality grade | 0–3 by automated lints | Soft signal; can't pass-or-fail on this |

## Code-quality grade rubric (0–3)

| Score | Criterion |
|-------|-----------|
| 3 | No new compiler warnings; no new TODOs; no introduced dead code; new code matches surrounding patterns |
| 2 | One of the above violated, mildly |
| 1 | Two of the above violated, OR one badly (e.g., introduced try-catch-empty) |
| 0 | New compile failures masked by `#pragma`, hardcoded test bypasses, or commented-out tests |

Graded by a separate grading script (TBD), not by the same model that did the work.

## Anti-gaming clauses

These were the lessons from the OrderFlow benchmarks. Encoded here as hard rules.

1. **No spec leakage in prompts**: T1 prompts describe the *feature*, not the *invariants*. They never say "make sure not to break INV-3". The model has to figure out what could break.
2. **Tests measured separately from prompts**: the model never sees `InvariantTests.cs` or `StateTransitionTests.cs` source as part of the prompt. It can read them via tools if it chooses to.
3. **Tautological postconditions don't count as "verified"**: post-hoc, the verification verdict is reclassified using the 4-state taxonomy (Proven / Disproven / Unverified / Trivially-True). Trivially-True does NOT improve `correctness`.
4. **Test additions are inspected**: if a model adds new tests as part of T1 work, those tests are not counted toward correctness scoring. Correctness uses the pre-defined feature-acceptance tests only.
5. **Adversarial post-hoc tests**: after the run, additional adversarial tests (matching the OrderFlow `*_adversarial_test.py` pattern) are run on each preserved workspace. These flag silent regressions that the visible tests don't catch.

## Run logging schema

Each run logs to `runs/<lang>/<prompt-id>/run-<seq>/`:

```
run-001/
├── prompt.txt               # Exact prompt given
├── transcript.jsonl         # Full conversation transcript
├── final-diff.patch         # Git diff vs. scaffold base
├── test-results.json        # dotnet test JSON output
├── invariant-results.json   # 7 invariants × pass/fail
├── adversarial-results.json # Post-hoc adversarial test outcomes
├── metrics.json             # turns, tokens, $, time, tool calls
└── code-quality.json        # grading script output
```

## Decision rules (pre-committed)

After all 30 trials complete:

| Result | Action |
|--------|--------|
| At least one prompt hits the 50% bar with non-inferior absolute correctness | **Continue**: pivot to T2 (adversarial-edge-case) on the winning prompt's pattern, expand sample size |
| Calor matches C# (within ±10%) on all three prompts | **Pivot**: T1 was the wrong theory. Move to T2 (adversarial-edge-case) before T3 |
| Calor underperforms C# by >20% on two or more prompts | **Stop and report**: T1 is a negative signal. Document and ask the user before continuing. |
| Mixed results, no clear pattern | **Diagnose**: produce post-hoc analysis, identify confounds (tooling latency, port fidelity), then either re-run with fix or move to T2 |

## What this rubric does NOT measure

- Long-term maintainability (out of scope for Phase 0)
- Subjective code aesthetics
- Whether Calor "feels" easier — only outcomes
- Type safety in isolation (already handled by the C# compiler in both arms)

## Open question (resolved before lock)

Whether to use temperature=0 or default. **Decision: default temperature** — coding agents in production use default settings, and we want ecological validity. Variance is the cost.
