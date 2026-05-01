# Scoring Rubric v2 — Phase 0 (Directional Pilot)

**Supersedes:** `scoring-rubric.md` (v1).
**Status:** locked when committed alongside this commit; not editable thereafter.
**Why v2:** see `methodology-changelog.md`.

## Framing — pilot, not confirmatory

Phase 0 is a **directional pilot**. With N=5 per cell, the experiment is statistically underpowered to declare pass/fail at any specific effect-size threshold. The role of Phase 0 is to identify which theory (T1.A, T1.B, T1.C, or none) shows enough directional signal to justify a powered confirmatory study (N≈20) at ~$1,500–2,000.

**This rubric does not authorize a "Calor passed" claim from Phase 0 alone.** It authorizes a "spend confirmatory budget on this theory" decision.

## Pre-registered hypothesis

**H1 (primary):** On multi-file maintenance tasks with explicit cross-file coordination requirements (T1.B-style — adding a feature whose correctness depends on multiple components agreeing), Calor's machine-verifiable annotation regime (`§E{}`, `§Q`, `§S`) produces higher-Quality outcomes than C# at comparable cost.

**H1.A, H1.C (secondary, descriptive only):** Whether Calor helps on simpler attribute-addition (T1.A) or on more pervasive cross-aggregate refactors (T1.C). These are recorded but do **not** determine the pilot's go/no-go decision.

The decision to designate T1.B as primary is made *before* any data is collected. The reasoning: Calor's effect system is theoretically strongest where the model must reason about which methods can mutate state. T1.B's background-worker pattern exercises exactly this. T1.A is a field-add (low coordination); T1.C is a cross-aggregate refactor (also high coordination, but harder to grade because correctness is more interpretation-dependent).

## What we measure

Two metrics, **always reported separately, never conflated:**

```
Quality       = correctness × invariant_preservation × (1 − regression_rate)
CostEfficiency = Quality / dollar_cost
```

Where:
- `correctness ∈ {0, 1}` — 1 iff the feature-acceptance tests for the prompt all pass
- `invariant_preservation ∈ [0, 1]` — fraction of 7 invariant tests still passing
- `regression_rate ∈ [0, 1]` — fraction of pre-existing tests now failing (excluding feature-acceptance tests)
- `dollar_cost` — actual API cost of the run, in USD

`Quality` is bounded in [0, 1]. `CostEfficiency` is unbounded but typically in [0.01, 0.5].

The `mean(score / cost)` aggregator from v1 is **deleted**: means of ratios are biased toward denominator outliers.

## Aggregation

Per cell (language × prompt) at N=5 trials:

| Aggregator | Quality | CostEfficiency |
|------------|---------|-----------------|
| Primary | **median + IQR** | **median + IQR** |
| Secondary | geometric mean | trimmed mean (10%) |
| Reported but not used for decisions | mean | mean |

Variance reported as IQR alongside median, never as standard deviation (high-variance noisy distributions; SD is misleading).

## Reading the signal

The decision is made on a single number: the median Quality ratio for T1.B.

```
QualityRatio_T1B = median(Quality_calor_T1B) / median(Quality_csharp_T1B)
CostEffRatio_T1B = median(CostEff_calor_T1B) / median(CostEff_csharp_T1B)
```

**Decision table (pre-committed):**

| QualityRatio | CostEffRatio | Interpretation | Action |
|--------------|--------------|----------------|--------|
| ≥ 1.50× | ≥ 1.00× | Strong directional signal | Commit ~$1,500 to confirmatory study of T1.B at N=20 |
| ≥ 1.50× | < 1.00× | Quality wins but Calor is materially more expensive | Investigate cost drivers (MCP latency, prompt overhead) before deciding |
| 1.20×–1.50× | ≥ 0.80× | Suggestive, not strong | Run T2 (adversarial-edge-case) before deciding on confirmatory T1.B |
| 0.80×–1.20× | any | No directional signal on T1 | Pivot to T2 |
| < 0.80× | any | Calor underperforms | **Stop T1 line of investigation; report.** |

The CostEff guard (≥ 1.00× in the strong-positive cell) prevents a hollow win where Calor "passes" purely by being faster/cheaper. v1's "non-inferior within 10%" was too loose at N=5.

T1.A and T1.C medians are computed and reported in the milestone update but do not enter the decision table.

## Sample size honesty

At N=5 per cell:
- Welch's t on log-Quality has ~30% power for Cohen's d=1.0 effect, ~60% for d=1.5
- A 1.5× median ratio could appear from sampling noise alone with non-trivial probability under a null hypothesis

Phase 0 therefore **does not run inferential statistics or claim significance**. The decision table is a deterministic function of observed medians, not a hypothesis test. Any "Calor passed" claim requires the confirmatory N=20 study.

## Blocking prerequisites

T1.B first run cannot begin until **all six** are complete and committed:

| # | Prerequisite | Why |
|---|--------------|-----|
| **B1** | **Information equality**: C# scaffold has comment annotations equivalent to Calor's `§E{}`, `§Q`, `§S`. Same information; only Calor enforces it via compiler. | Closes the central confound: a Calor "win" would otherwise prove "more information helps," not "Calor helps." |
| **B2** | **MESS labels stripped**: inline `// MESS-N:` comments removed from C# scaffold (and not added to Calor variant). Code inconsistencies preserved; hand-holding documentation removed. | Real maintenance tasks don't label their inconsistencies. Labeled mess inflates difficulty asymmetrically toward whichever variant the model reads first. |
| **B3** | **MESS coverage table**: each retained MESS-N maps to ≥1 test that detects a wrong "fix." MESS items that cannot be detection-tested are dropped from the scaffold (or explicitly scoped out of regression scoring). | Untested mess can be silently "improved" by a model with no scoring penalty, hiding real regressions. |
| **B4** | **Calor compiler scale validation**: `OrderService.cs` (~250 LoC, the largest service) successfully converts via `calor_convert` and the resulting `.calr` + `.g.cs` build clean. | Binary go/no-go. If this fails, the experiment cannot run on Calor and the program shape changes. |
| **B5** | **Graders written and committed**: `graders/T1.B/AcceptanceTests.cs` exists, tests through the API surface using `WebApplicationFactory<Program>` with string-keyed JSON (not bound to model's type names). Independently reviewed (≥1 second-eye reviewer). | The rubric is unscorable without them. Independent review mitigates designer's-blindspot bias. |
| **B6** | **Dry-run cost validation**: one T1.B trial on Opus 4.7, on the C# scaffold. Captures actual turns, tokens, $. If single run > $25 (per-run cap from v1), halt and re-budget before proceeding. | Cost envelope must be empirically confirmed before committing to N=10 (5×2). |

These are not "would be nice." Each closes a defect identified in the v1 critiques. T1.B does not run until all six are done.

## Cost discipline (unchanged from v1)

| Trigger | Action |
|---------|--------|
| Single run > $25 | Halt run, log, investigate before continuing |
| Phase 0 cumulative > $1,000 | Stop, re-budget, milestone update |
| Program cumulative > $5,000 with no signal | Stop and report |
| Program cumulative > $9,000 | Stop regardless |

Per-run hard cap remains $25; per-cell budget remains ~$75 (5 × $15 mid-estimate). Cost-budget.md remains the authoritative cost reference.

## Anti-gaming clauses

Carried forward from v1, with one removed (item 4) and one strengthened (items 1, 2):

1. **No spec leakage in prompts.** T1 prompts describe the *feature*, not the *invariants*. Operator may not edit prompts to nudge the model.
2. **Tests are not part of the prompt.** The model sees `tests/WholesaleOrders.Tests/` only if it chooses to read it via tools. The graders directory (`graders/`) is **never** present in the model's workspace; it is copied in by the operator after the model declares completion, before grading.
3. **Tautological postconditions reclassified.** Postconditions of the form `§S (cond)` where `cond` is implied by the precondition (e.g., `§S (>= a 0)` when `§Q (>= a 0)` was the precondition) are flagged as Trivially-True and do **not** improve `correctness`.
4. ~~Adversarial post-hoc tests~~ — kept as a diagnostic tool, but no longer affect `correctness` directly. They flag silent regressions for the milestone narrative; they don't alter the decision-table input.
5. **Subjective code-quality grade dropped.** v1's 0–3 grade was subjective and unblinded. Removed entirely; the objective metrics (correctness, invariant, regression) suffice.
6. **Operator blinding on grading where possible.** When running graders, the operator does not know which variant produced the diff. Achieved by anonymizing run-folder names before applying graders.

## Run logging schema (unchanged from v1)

Each run logs to `bench/research-phase-0/runs/<lang>/<prompt-id>/run-<seq>/`:

```
run-001/
├── prompt.txt
├── transcript.jsonl
├── final-diff.patch
├── test-results.json
├── invariant-results.json
├── adversarial-results.json
├── metrics.json
└── cost-receipt.json     ← actual API cost from the session
```

`metrics.json` schema:

```json
{
  "turns": int,
  "tokens_in": int,
  "tokens_out": int,
  "tool_calls": int,
  "wall_clock_s": int,
  "model": "claude-opus-4-7",
  "completed_naturally": bool,
  "halt_reason": "done" | "turn_cap" | "time_cap" | "cost_cap"
}
```

## What this rubric does NOT do

- Does not authorize a "Calor passes Phase 0" claim. Pilot only.
- Does not aggregate Quality across prompts (T1.B is the only prompt that affects the decision).
- Does not blind grader on language label automatically (but anonymizes run-folder names).
- Does not subjectively grade code style.
- Does not run inferential statistics. The decision table is deterministic on observed medians.
- Does not score MESS items that have no detection test (those MESSes are dropped from the scaffold per B3).

## Open items the rubric does not resolve

These are scoped out of v2 and addressed elsewhere:

- **EF Core vs in-memory `AppDbContext` ecological validity** — accepted as a known threat to external validity. Phase 0 does not generalize to systems where DB migration / transaction-isolation issues dominate. Documented in `scaffold-spec.md`.
- **Operator discretion on hard caps** — manual procedure documented in `cost-budget.md`. No automated harness.
- **1M-context tier pricing** — actual cost measured in B6 (dry-run); rubric will not run if dry-run reveals pricing-tier issues that blow the per-run cap.
- **Re-run policy on rate-limit / network errors** — if a run halts on infrastructure error, it restarts from prompt-zero with a new run-seq. This may inflate cost; documented in cost-budget.md.

## Lock signature

Locked when this file is committed in the same commit as `methodology-changelog.md` and an updated `README.md`. After lock, edits are forbidden. New rubric (e.g., `scoring-rubric-v3.md`) requires explicit user authorization and a written justification.
