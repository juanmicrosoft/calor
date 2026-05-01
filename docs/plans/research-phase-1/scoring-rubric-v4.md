# Scoring Rubric v4 — Phase 1 (T2 Adversarial-Edge-Case Pilot)

**Supersedes:** `scoring-rubric-v3.md` for Phase 1 work only. v3 remains authoritative for the historical Phase 0 record.
**Why v4:** see `methodology-changelog.md` (entry "v4 supersedes v3").
**Status:** locked at commit time; not editable thereafter.

## Hypothesis (T2)

Coding agents introduce invariant-violating bugs **more often** in tasks where the natural implementation crosses an existing invariant boundary, when the codebase lacks explicit annotations of those invariants.

Concretely: T2.B (primary) tests whether the annotated arm produces fewer INV-3 violations on a partial-release feature whose obvious implementation can over-release.

## What we measure (changed from v3)

```
Quality = bug_avoidance × correctness × (1 − regression_rate)
CostEfficiency = Quality / dollar_cost
```

Where:
- `bug_avoidance ∈ {0, 1}` — 1 iff the model's implementation does NOT introduce the targeted invariant violation. Detected by the bug-class probes in the grader.
- `correctness ∈ {0, 1}` — 1 iff the feature works as specified (the basic acceptance tests pass).
- `regression_rate ∈ [0, 1]` — fraction of pre-existing tests now failing.

**Difference from v3:** `bug_avoidance` replaces `invariant_preservation` as a separate factor. v3 used `invariant_preservation` as a soft fraction; v4 makes it a hard yes/no on the targeted bug class. This is more discriminating: a model that introduces the bug gets Quality=0 even if the feature appears to work.

## Aggregation (unchanged from v3)

| Aggregator | Quality | CostEfficiency |
|------------|---------|-----------------|
| Primary | median + IQR | median + IQR |
| Secondary | geometric mean | trimmed mean (10%) |

## Decision table (unchanged thresholds)

Primary cell: **T2.B annotated vs bare**, median QualityRatio.

| QualityRatio | Action |
|--------------|--------|
| ≥ 1.50× | Strong signal → confirmatory study (~$1.5K, N=20) |
| 1.20×–1.50× | Suggestive → run T3 before deciding |
| 0.80×–1.20× | Null → pivot to T3 |
| < 0.80× | Negative → stop and report |

## Blocking prerequisites

| # | Prerequisite | Status |
|---|--------------|--------|
| **C1** | T2 prompts locked (`phase-1-prompts.md`) | committed at first commit |
| **C2** | Graders for T2.A / T2.B / T2.C exist; each has bug-class detection tests | committed at first commit |
| **C3** | One T2.B dry-run on annotated arm; if cost > $25 per run, halt | inline in the run loop (first trial validates) |

No new scaffold work needed — Phase 0 scaffolds (`csharp-baseline/` and `csharp-bare/`) are reused.

## Anti-gaming clauses (carried from v3)

1. No spec leakage in prompts — they describe the *feature*, not the invariants
2. Tests not in prompt — graders applied post-hoc
3. Tautological postconditions reclassified
4. Adversarial post-hoc tests are the bug-avoidance probes themselves; they DO affect Quality (different from v3)
5. No subjective code-quality grade
6. Operator blinding via run-folder anonymization where feasible

## What v4 does NOT do

- Does not measure latency, turn count, or token usage as decision inputs (reported but not decisive)
- Does not aggregate Quality across prompts (T2.B is the only decision prompt)
- Does not blind the grader on language label (mitigated by run-folder names)
- Does not re-run T1 trials with new metrics

## Cost discipline (unchanged)

Per-run cap $25. Phase 1 cap $200. Program kill-switches at $5K (no signal) and $9K.
