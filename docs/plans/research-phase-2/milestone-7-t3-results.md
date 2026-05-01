# Milestone 7 — T3 results: NULL on primary, signal weakening across phases

**Date:** 2026-05-01
**Phase:** 2 complete
**Decision:** **stop annotation-only experiments at this scaffold scale**; surface a strategic fork to the user.

## Per-prompt results (30 trials on Opus 4.7)

| Cell | n | Bug-detector pass | Mean Quality | Median Quality |
|------|---|-------------------|--------------|-----------------|
| T3A-annotated | 5 | 5/5 (100%) | 1.00 | 1 |
| T3A-bare | 5 | 5/5 (100%) | 1.00 | 1 |
| **T3B-annotated** (primary) | 5 | **5/5** | **1.00** | **1** |
| **T3B-bare** (primary) | 5 | **5/5** | **1.00** | **1** |
| T3C-annotated | 5 | 5/5 (100%) | 1.00 | 1 |
| T3C-bare | 5 | 4/5 (80%) | 0.80 | 1 |

**Decision per v4 rubric:** T3.B median ratio = 1.00× → **NULL** → continue or stop.

**Pooled across 30 T3 trials:** annotated 100% vs bare 93.3% = **1.07× directional**. Weaker than Phase 1's 1.29×.

## Cross-phase trend

| Phase | Primary cell | Median ratio | Pooled ratio |
|-------|--------------|--------------|---------------|
| 0 (T1-prime) | T1.B | 1.00× | ~1.00× (not computed in P0) |
| 1 (T2 adv-edge) | T2.B | 1.00× | **1.29×** |
| 2 (T3 compositional) | T3.B | 1.00× | **1.07×** |

**Phase 1 was the high-water mark.** Phase 2 weakened it. Three sequential nulls on the primary cell, with the pooled signal regressing toward 1.0×.

## What this means

The annotation-only hypothesis (the structural part of Calor's value proposition, without compiler enforcement) does not produce a robust directional advantage on this scaffold at the difficulty levels tested. Three phases × $\~$60 each × 30 trials each = 90 trials say:

- **No primary cell hits the 1.50× pass bar.**
- The single suggestive pooled result (Phase 1 at 1.29×) didn't replicate.
- Across all 90 trials: annotated 26/45 (58%) bug-avoid vs bare 24/45 (53%) = 1.08×. Below the bar.

This is a substantive negative finding: **at this scaffold scale, on Opus 4.7, structured comments mirroring Calor's annotation regime do not differentially help coding agents avoid invariant-violating bugs.**

The hypothesis isn't completely dead — but the residual probability that further annotation-only testing surfaces a 1.5× signal is low. **Per the autonomy memo's "pivot fast when confidence is low" rule, continuing to design more T-prompts in this same shape is not high-confidence work.**

## Cost

- Phase 2 trials: 30 × ~$2 ≈ $60
- Grader iteration: ~$3
- **Phase 2 total: ~$63**
- **Cumulative program spend: ~$185** of $10,000 (1.9%)

## Strategic fork — needs user input

The remaining program budget is ~$9,815. Three plausible directions, with confidence assessments:

### Option A — Test Calor's compiler-enforcement layer (the actual hypothesis)
Fix the three v0.5.0 emitter bugs (CS1729, CS8618, CS8917) documented in `milestone-2-b4-finding.md`, port the scaffold to Calor, run T1/T2/T3 against a Calor variant whose annotations are *enforced*. **Confidence: moderate (~60%) on getting clean experiment data.** The compiler bugs are real and bounded; once fixed, the experiment can run. **This is the actual unanswered question** — Phase 0–2 only tested the *information* component; enforcement is what makes Calor structurally different.

Estimated cost: ~$300 trials + 2–4 days of compiler work. The compiler work isn't "research budget" per se but is in scope per "full control of language changes."

### Option B — Test on a more invariant-rich scaffold
The current scaffold's invariants are inferable from code structure (entity classes, status enums, value objects). A scaffold with subtler, less-inferable invariants (financial calculations, security-critical paths, distributed-system semantics) might surface annotation effects. **Confidence: low-moderate (~40%).** No prior evidence; pure speculation that subtler invariants would change the outcome.

Estimated cost: ~3 days new scaffold + ~$200 trials.

### Option C — Stop the program. Report negative finding.
The annotation-only hypothesis is disconfirmed on representative .NET tasks at this scale. Calor's value, if any, is elsewhere (compiler enforcement, machine-verified contracts, refinement types) and was not testable in Phase 0–2. **Confidence: high (~95%) on the reportable conclusion.**

Estimated cost: ~$0. Write up the program as a documented negative result.

### Option D — Try fundamentally different theories (T6/T7 language redesign)
Stop testing the current Calor design and propose redesigns (Calor-Lite, structural-AST form). **Confidence: low (~25%).** Pure design exercise without empirical validation paths defined.

## My recommendation

**Option A.** Calor's compiler enforcement is the actual differentiator. Three phases of annotation-only testing have ruled out one component of the value proposition; we should test the other before declaring the program done. The compiler bugs are bounded and the user explicitly authorized "full control of language changes."

If A fails (compiler can't be fixed in reasonable time, OR fixed compiler still produces no signal), we have decisive Option C-style negative findings.

If A succeeds, we have evidence the *combination* of annotation + enforcement helps, and the confirmatory study path opens.

**This is a fork worth user input.** Per the autonomy memo: "If you genuinely need user input, batch the question with substantive progress." Phases 0–2 are that substantive progress. The user should weigh whether to commit ~$1,500 + compiler-fix time to Option A, or stop with the current findings.
