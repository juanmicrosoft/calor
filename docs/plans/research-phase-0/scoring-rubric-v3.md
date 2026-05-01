# Scoring Rubric v3 — Phase 0 (Annotation-Regime Pilot)

**Supersedes:** `scoring-rubric-v2.md`.
**Status:** locked when committed alongside this commit; not editable thereafter.
**Why v3:** see `methodology-changelog.md`. Calor port (B4) hit v0.5.0 emitter bugs at scaffold complexity. Pivoted to T1-prime (annotation regime on C# only) which doesn't need a working Calor compiler.

## Framing — pilot, not confirmatory (unchanged from v2)

Phase 0 remains a **directional pilot**. With N=5 per cell, the experiment is statistically underpowered for any pass/fail claim at a specific effect-size threshold. The role of Phase 0 is to identify whether the annotation regime shows enough directional signal to justify a powered confirmatory study (N≈20) and to gate further Calor compiler-enforcement work.

## Pre-registered hypothesis

**H1' (primary):** On multi-file C# maintenance tasks with explicit cross-file coordination requirements (T1.B-style), C# scaffolds with structured `// EFFECTS:` / `// PRECONDITION:` / `// POSTCONDITION:` annotations produce higher-Quality outcomes than the same scaffold without those annotations, at comparable cost.

**H1'.A, H1'.C (secondary, descriptive):** Whether annotation regime helps on simpler attribute-addition (T1.A) or cross-aggregate refactors (T1.C). Reported but do not determine the pilot's go/no-go.

## What we measure (unchanged from v2)

```
Quality       = correctness × invariant_preservation × (1 − regression_rate)
CostEfficiency = Quality / dollar_cost
```

Reported separately, never conflated.

## Aggregation (unchanged from v2)

| Aggregator | Quality | CostEfficiency |
|------------|---------|-----------------|
| Primary | median + IQR | median + IQR |
| Secondary | geometric mean | trimmed mean (10%) |
| Reported but not decisive | mean | mean |

## Reading the signal (unchanged from v2)

The decision is made on a single number: the median Quality ratio for T1.B.

```
QualityRatio_T1B = median(Quality_annotated_T1B) / median(Quality_bare_T1B)
CostEffRatio_T1B = median(CostEff_annotated_T1B) / median(CostEff_bare_T1B)
```

| QualityRatio | CostEffRatio | Interpretation | Action |
|--------------|--------------|----------------|--------|
| ≥ 1.50× | ≥ 1.00× | Strong: annotation regime helps and isn't expensive | Invest in Calor compiler work + run confirmatory N=20 study |
| ≥ 1.50× | < 1.00× | Quality wins but annotated runs are pricier | Investigate cost drivers; if cost is fixable, invest in Calor work |
| 1.20×–1.50× | ≥ 0.80× | Suggestive, not strong | Run T2 before deciding; expand annotation work cautiously |
| 0.80×–1.20× | any | No directional signal | **Annotation regime is a non-starter on its own.** Pivot to T2 (adversarial-edge) or different theory; deprioritize Calor compiler work |
| < 0.80× | any | Annotated underperforms | Surprising but informative; investigate what breaks the agent |

## Sample size honesty (unchanged from v2)

At N=5 per cell:
- Welch's t on log-Quality has ~30% power for Cohen's d=1.0, ~60% for d=1.5
- A 1.5× median ratio could appear from sampling noise alone with non-trivial probability under a null hypothesis

Phase 0 therefore **does not run inferential statistics or claim significance**. The decision table is a deterministic function of observed medians, not a hypothesis test.

## Blocking prerequisites

T1.B first run cannot begin until **all six** are complete and committed:

| # | Prerequisite | Status |
|---|--------------|--------|
| **B1** | C# scaffold has `// EFFECTS:` / `// PRECONDITION:` / `// POSTCONDITION:` annotations on public methods | ✅ done at commit `3b423c1` |
| **B2** | `// MESS-N:` labels stripped from scaffold | ✅ done at commit `3b423c1` |
| **B3** | MESS coverage table; retained MESS items have detection tests | ✅ done at commit `3b423c1` |
| ~~B4~~ | ~~Calor compiler scale validation~~ | **Dropped from v3.** Calor not in this experiment. |
| **B5** | `graders/T1.B/AcceptanceTests.cs` exists, tests via `WebApplicationFactory<Program>`, independently reviewed | ⏸️ pending |
| **B6** | One T1.B dry-run on Opus 4.7 (on annotated arm); halt if > $25 | ⏸️ pending |
| **B7** (new) | `bench/research-phase-0/csharp-bare/` exists: copy of `csharp-baseline/` with `// EFFECTS:` / `// PRECONDITION:` / `// POSTCONDITION:` lines stripped. Builds clean. All 42 tests pass. | ⏸️ pending |

## Cost discipline (unchanged from v2)

| Trigger | Action |
|---------|--------|
| Single run > $25 | Halt run, log, investigate before continuing |
| Phase 0 cumulative > $1,000 | Stop, re-budget, milestone update |
| Program cumulative > $5,000 with no signal | Stop and report |
| Program cumulative > $9,000 | Stop regardless |

Re-budgeted for v3: 5 × 3 × 2 = 30 trials, 1.0× cost factor (both arms are C#, no MCP overhead asymmetry). Estimated total ~$450, down from v2's $575.

## Anti-gaming clauses (unchanged from v2)

1. **No spec leakage in prompts.** T1 prompts describe the *feature*, not the *invariants*.
2. **Tests are not part of the prompt.** Graders directory copied in only after the model declares completion.
3. **Tautological postconditions reclassified** as Trivially-True; do not improve `correctness`.
4. **Adversarial post-hoc tests** kept as diagnostic, do not affect `correctness`.
5. **Subjective code-quality grade dropped** entirely.
6. **Operator blinding on grading where possible** — anonymize run-folder names before applying graders.

## Run logging schema (unchanged from v2)

Each run logs to `bench/research-phase-0/runs/<arm>/<prompt-id>/run-<seq>/`:

```
run-001/
├── prompt.txt
├── transcript.jsonl
├── final-diff.patch
├── test-results.json
├── invariant-results.json
├── adversarial-results.json
├── metrics.json
└── cost-receipt.json
```

`<arm>` is `annotated` or `bare` (was `csharp` or `calor` in v2).

## What this rubric does NOT do (unchanged from v2)

- Authorize a "T1-prime passed" claim without confirmatory study.
- Aggregate Quality across prompts.
- Auto-blind grader on language label (mitigated by run-folder anonymization).
- Subjectively grade code style.
- Run inferential statistics.
- Score MESS items lacking detection tests.

## What v3 specifically does NOT measure

- **The value of machine verification.** v3 isolates the *information* component of Calor's annotation system. The *enforcement* component (compiler-rejection of effect violations, Z3 contract verification) is out of scope. A positive v3 result authorizes investing in the enforcement layer; it does not validate that layer.
- **Calor as a whole.** v3 cannot conclude anything about Calor specifically. It tests a hypothesis about annotation regimes in C# — Calor's annotation regime is one specific instance.
- **Whether Calor v0.5.0 can support the experiment.** Already answered (no, see milestone-2-b4-finding.md). v3 sidesteps this question.

## Lock signature

Locked when this file is committed in the same commit as `methodology-changelog.md` (updated with v3 entry) and `README.md` (updated to point to v3).
