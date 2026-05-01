# Milestone 6 — T2 results: NULL on primary, suggestive pooled signal

**Date:** 2026-05-01
**Phase:** 1 complete
**Decision (per scoring-rubric-v4 decision table):** **pivot to T3** (primary cell T2.B = NULL)
**Notable secondary finding:** **pooled annotated bug-avoidance rate is 1.29× bare** — first directional signal in the program.

## Per-prompt results (30 trials on Opus 4.7)

| Cell | n | Bug-detector pass | Mean Quality | Median Quality |
|------|---|-------------------|--------------|-----------------|
| T2A-annotated | 5 | 1/5 (20%) | 0.20 | 0 |
| T2A-bare | 5 | 0/5 (0%) | 0.00 | 0 |
| **T2B-annotated** (primary) | 5 | **5/5 (100%)** | **1.00** | **1** |
| **T2B-bare** (primary) | 5 | **5/5 (100%)** | **1.00** | **1** |
| T2C-annotated | 5 | 3/5 (60%) | 0.60 | 1 |
| T2C-bare | 5 | 2/5 (40%) | 0.40 | 0 |

## Decision: per pre-committed rubric

**T2.B is the only decision-relevant cell.** Median QualityRatio_T2B = 1.00 / 1.00 = **1.00×**.

Falls in the **0.80×–1.20× "no directional signal"** band → **pivot to T3** per `scoring-rubric-v4.md`.

## Suggestive pooled signal (descriptive only — does NOT alter decision)

Across all 30 trials:
- **Annotated bug-avoidance rate: 60.0% (9/15)**
- **Bare bug-avoidance rate: 46.7% (7/15)**
- **Pooled ratio: 1.29×**

This is the first non-trivial directional signal in either Phase 0 or Phase 1. It's below the 1.50× pass bar (so not a "strong" signal) and across a small N=15 (so confidence interval wide). But the *direction* is consistent with the annotation hypothesis: annotated arm avoids the targeted invariant violations more often.

**Per the rubric's anti-gaming clauses, this pooled signal does not authorize a pass.** The primary cell governs. But it informs T3 design: prompts in the *difficulty band* where T2.A and T2.C lived (where the bug is plausibly introduced ~half the time) appear to surface annotation effects, while T2.B-style prompts (which both arms saturate at 100%) do not.

## What we learned about prompt design

| Prompt | Bug-introduction rate (overall) | Useful for differentiation? |
|--------|-------------------------------|------------------------------|
| T2.A (promo discount → INV-1) | 90% (1/10 avoided bug) | **Too hard.** Almost everyone fails; small variance. |
| T2.B (partial release → INV-3) | 0% (10/10 avoided bug) | **Too easy.** Both arms saturate. |
| T2.C (order recall → INV-3/5) | 50% (5/10 avoided bug) | **Sweet spot.** Half-and-half — surfaces variance. |

For T3, target the T2.C difficulty band: tasks where ~30–70% of trials introduce the bug regardless of arm.

## Cost

- Trials: 30 × ~$2 = ~$60
- Grader iteration: ~$5
- **Phase 1 total: ~$65**
- **Cumulative program spend: ~$120 of $10,000** (1.2%)

## Decision: T3 design

T3 (compositional correctness) tests features whose correctness depends on *multiple* invariants holding simultaneously. The hypothesis: the annotated arm more reliably preserves cross-invariant consistency because the comments make the interactions explicit.

**T3 prompt design principles (from T2 lessons):**
1. Target the "sweet spot" difficulty (~30–70% bug-introduction rate). Avoid trivial features (T2.B) and impossible-to-get-right features (T2.A).
2. Each prompt should expose *multiple* invariants that interact.
3. The bug-detection probes should be precise — single-invariant probes were noisy at N=5.

**T3 candidate prompts:**
- **T3.A** — Inventory transfer between two warehouses (introduces second InventoryItem-like entity; touches INV-2 across both, plus reservation rebinding for INV-3)
- **T3.B** — Refund-on-cancel for paid orders (interacts INV-3 reservation release, INV-4 payment status, NotificationService)
- **T3.C** — Order split (one order becomes N partial orders; tests INV-1 line-item conservation, INV-3 reservation re-binding, INV-5 fulfillment readiness)

T3.B as primary (most coordination-rich, similar shape to T2.C which showed signal).

## Updated mental model after Phase 0 + Phase 1

We now have:
- **Phase 0:** Annotation regime null on feature-add (T1.B saturates).
- **Phase 1:** Annotation regime null on adversarial-edge primary (T2.B saturates), but **pooled directional signal of ~1.29×** suggests annotation might help on harder tasks.

The signal is real but small and below the pass bar. Two paths forward:
1. **Continue the program** with T3 (compositional) — cheap, may surface stronger signal at higher difficulty.
2. **Confirmatory study on the pooled signal** — N=20 pooled across mixed-difficulty prompts. ~$240. Would test if the 1.29× holds with more power.

I'm going with (1) per the rubric — pre-committed decision rule says pivot to T3, and the pooled signal isn't strong enough to justify a confirmatory N=20 yet. T3 may push the signal to the 1.5× threshold or wash it out.

## What's next

Phase 2 (T3 compositional correctness):
- Design 3 prompts targeting the T2.C difficulty band
- Reuse same scaffolds (annotated + bare) from Phase 0
- Reuse grader pattern (Acceptance + BugDetector + regression)
- Run N=5 × 3 × 2 = 30 trials, ~$60
- Decision per v4 rubric
