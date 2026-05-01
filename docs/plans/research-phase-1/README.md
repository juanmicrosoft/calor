# Research Phase 1 — T2 Adversarial-Edge-Case Detection

Builds on Phase 0 (which produced a null result on T1-prime — see `docs/plans/research-phase-0/milestone-5-t1prime-results.md`). Phase 1 tests a different theory.

## Hypothesis (T2)

**Coding agents introduce invariant-violating bugs more often when the codebase lacks explicit annotations of those invariants — in tasks where the natural implementation path crosses an existing invariant boundary.**

T1-prime tested feature-add tasks where models trivially solve the task either way. T2 tests *adversarial-edge* tasks where:

- The new feature naturally interacts with an existing invariant (INV-1, INV-3, or INV-5 from the scaffold)
- The "obvious" implementation introduces a subtle bug (silent invariant violation)
- A careful read of the existing annotations would steer the agent away from the bug

## Why T2 might surface signal where T1-prime didn't

T1-prime saturated at Quality=1.0 in 4/5 trials per arm because the prompts (`feature-add`) were below Opus 4.7's capability gap. T2 deliberately picks tasks where the bug class is one Opus 4.7 *can* miss without careful invariant-reading. The annotation arm's `// PRECONDITION: ...` and `// POSTCONDITION: ...` comments — if the hypothesis holds — should reduce the bug-introduction rate.

If T2 also returns null, the conclusion sharpens: annotation regime alone doesn't help on this scaffold's task complexity. Calor's value, if any, is in the enforcement layer (which Phase 0 couldn't test due to v0.5.0 emitter bugs) or at much larger scale (Phase 5 OSS port).

## Same scaffold, same arms

- `bench/research-phase-0/csharp-baseline/` (annotated arm) and `csharp-bare/` (bare arm) reused unchanged
- T2 prompts run against fresh copies of these two arms
- Same model (Opus 4.7), same per-run cost cap ($25), same trial protocol

## Three prompts, INV-1 / INV-3 / INV-5 each

- **T2.A** — Promo code discount: risks **INV-1** (`Order.TotalAmount = Σ(LineItem.Quantity × LineItem.UnitPrice)`)
- **T2.B** — Partial reservation release: risks **INV-3** (`Reservation.Status` terminal-state absorbing for Released and Fulfilled)
- **T2.C** — Order recall (Shipped → Submitted): risks **INV-5** (`Shipped` order has all line items reserved)

Each prompt has a behavioral grader that probes the specific invariant *plus* runs existing INV-1/3/5 tests as regression check.

## Decision rule (same as v3 rubric)

Primary cell: T2.B (most coordination-rich). Same decision table:

| QualityRatio | Action |
|--------------|--------|
| ≥ 1.50× | Strong signal → confirmatory study |
| 1.20×–1.50× | Suggestive → run T3 before deciding |
| 0.80×–1.20× | Null → pivot to T3 (compositional correctness) or escalate to user |
| < 0.80× | Negative → stop and report |

**If T2 returns null too**, two more pivots remain in the program (T3 compositional, T6/T7 language redesign), but it sharpens the case that the program may be testing a hypothesis (annotation regime helps coding agents) that's already been disconfirmed at this scaffold scale.

## Cost estimate

30 trials × ~$2 each = ~$60. Plus ~$5 grader iteration. Total Phase 1: ~$65.

Cumulative program spend after Phase 1: ~$120 of $10,000.

## Files

| File | Purpose |
|------|---------|
| `phase-1-prompts.md` | Locked T2.A/B/C prompts |
| `scoring-rubric-v4.md` | Adapts v3 framework for T2 hypothesis |
| `methodology-changelog.md` | (in phase-0; v4 entry appended) |
| `runs/` | Per-trial results (`bench/research-phase-1/runs/`) |
