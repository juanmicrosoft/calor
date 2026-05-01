# Milestone 5 — T1-prime pilot results: NULL signal, pivot to T2

**Date:** 2026-05-01
**Phase:** 0 complete (30 trials run, all graded)
**Decision (per scoring-rubric-v3.md decision table):** **pivot to T2**

## Headline

Phase 0 ran the directional pilot for T1-prime (annotation regime on C#) at N=5 × 3 prompts × 2 arms = 30 trials on Opus 4.7. The primary cell — **T1.B annotated vs bare** — shows **no directional signal**: median Quality ratio = 1.0×. The pilot fails to authorize a confirmatory study, and per the pre-committed decision table this triggers a pivot to T2 (adversarial-edge-case detection).

## T1.B (primary) — null

| Trial | Annotated arm acc | Bare arm acc |
|-------|-------------------|--------------|
| 1 | 7/7 (q=1) | 7/7 (q=1) |
| 2 | 7/7 (q=1) | 4/7 (q=0 — grader miss on separate `IReservationExpirationService`) |
| 3 | 6/7 (q=0 — `Sends_Notification` failed) | 7/7 (q=1) |
| 4 | 7/7 (q=1) | 7/7 (q=1) |
| 5 | 7/7 (q=1) | 7/7 (q=1) |
| **Median strict Quality** | **1.0** | **1.0** |
| Mean strict Quality | 0.80 | 0.80 |
| Mean fractional acceptance | 0.97 | 0.91 |

**QualityRatio_T1B (median) = 1.0 / 1.0 = 1.00×.**
Falls in the **0.80×–1.20× "no directional signal"** band → pivot to T2.

## T1.A (secondary, descriptive)

| Trial | Annotated arm acc | Bare arm acc |
|-------|-------------------|--------------|
| 1 | 2/4 | 3/4 |
| 2 | 3/4 | 3/4 |
| 3 | 3/4 | 3/4 |
| 4 | 2/4 | 2/4 |
| 5 | 2/4 | 3/4 |
| **Median fractional acc** | **0.50** | **0.75** |
| Mean fractional acc | 0.60 | 0.70 |

**Strict Quality = 0 for all 10 trials** (no trial passed all 4 acceptance tests; consistent grader failure on at least one — likely the priority-rejection-of-invalid-string test, or the cross-priority shipment-scheduling test).

Notable: **bare arm slightly outperforms annotated** on T1.A (0.75 vs 0.50 median fractional). Directionally *opposite* of the annotation hypothesis. With N=5 this is well within sampling noise, but it's not a hint of a hidden positive signal — if anything, the comments may be marginally distracting.

## T1.C (secondary, descriptive)

| Trial | Annotated arm acc | Bare arm acc |
|-------|-------------------|--------------|
| 1 | 0/4 | 0/4 |
| 2 | 0/4 | 1/4 |
| 3 | 1/4 | 1/4 |
| 4 | 1/4 | 1/4 |
| 5 | 1/4 | 1/4 |
| **Median fractional acc** | **0.25** | **0.25** |
| Mean fractional acc | 0.15 | 0.20 |

**Strict Quality = 0 for all 10 trials.** The grader is largely broken for T1.C — the API conventions for "create shipment with line items" varied widely across implementations (some used `LineItems`, some `Items`, some flat arrays vs nested), and the grader's HTTP probe didn't accommodate the variation. Response: T1.C produces no useful signal.

T1.C's broken grader is itself a finding: black-box HTTP graders are fragile against the kind of design variation Opus 4.7 exhibits at this task complexity. T2 graders should rely less on specific endpoint paths and more on observable side effects through the existing services.

## What this finding means

Two interpretations, both consistent with the data:

1. **Annotation regime alone doesn't help on this task class.** Opus 4.7 is good enough at reading the existing scaffold to infer effects, preconditions, and postconditions on its own. Adding `// EFFECTS: db:w, log` comments doesn't change what the model does because it would have done the same thing anyway.

2. **The tasks are too easy to differentiate, OR the graders are too coarse.** T1.B is solvable ~100% of the time; both arms hit Quality 1.0 in 4/5 trials each. T1.A and T1.C have grader fragility hiding any signal that might exist. Variance came from grader probing reflection logic, not from model capability.

Interpretation (2) is consistent with prior phases: Sonnet 4.6 with a careful spec already matched Calor on small tasks. Opus 4.7 on a 45-file scaffold is more capable still. The distance between "annotation-rich" and "bare" for these prompts is below the model's capability gap.

This is itself an important Phase 0 finding: **Calor's annotation regime cannot be validated using feature-add tasks at the scale tested**, because the model trivially solves them either way. To find signal, we need either:

- Harder tasks where the model's capability is the bottleneck (T2 adversarial-edge: deliberately tricky bug-prone changes).
- Larger codebases where context capacity is the bottleneck (Phase 5 OSS port).
- Multi-step tasks with cross-aggregate coordination where annotation might reduce error rates (T3 compositional).

## Costs and trial logistics

| | Count | Avg duration | Avg cost (est) | Total cost (est) |
|---|------|--------------|----------|------------------|
| T1.A trials | 10 | ~4 min | ~$1.5 | ~$15 |
| T1.B trials | 10 | ~4 min | ~$1.6 | ~$16 |
| T1.C trials | 10 | ~6.5 min (longer prompt + more cross-file work) | ~$2.0 | ~$20 |
| **Phase 0 trials total** | **30** | | | **~$51** |

Plus ~$5 in pre-flight grader fixes and dry-run iteration.

**Cumulative program spend: ~$56** of the $10K budget. Well below all kill-switches.

## Grader robustness — known limitations exposed

The graders required multiple patches during execution to handle reasonable implementation variation:

- T1.B v3.1: probe `CreatedAt` not just `ExpiresAt`-named props
- T1.B v3.2: pass `DateTimeOffset.UtcNow` for required `asOf` params
- T1.B v3.3: prefer bulk methods over per-reservation `ExpireAsync(Guid)`
- T1.B v3.4: scope-wrap DI service resolution (CreateScope)
- T1.A v3.1: scope-wrap DI service resolution
- T1.C v3.1: same DI scope fix; still leaves most acceptance tests failing because the model's API surface varies too much

Even after these, **2 of the 10 T1.B trials were missed** (annotated-3 timing/notification edge case; bare-2 used a separate service the grader doesn't probe). Most of the T1.A and T1.C grader failures are similar: black-box probing can't keep up with the diversity of "anything reasonable" implementations.

For T2 onwards: graders should be more behavioral and less reflection-driven. Calling specific endpoints with specific inputs and checking specific outputs is more robust than probing for method names — but it requires the *prompts* to specify those endpoints. Trade-off: more prescriptive prompts may foreclose the design freedom that's part of what we're testing.

## Decision: pivot to T2

Per scoring-rubric-v3.md decision table for `0.80× ≤ QualityRatio ≤ 1.20×`: **"Pivot to T2 (adversarial-edge-case detection)."**

T2's working hypothesis (preliminary, to be locked in scoring-rubric-v4): coding agents asked to make subtle, edge-case-prone modifications miss documented invariants more often when the codebase lacks explicit annotations of those invariants. T2 prompts will be specifically designed where the *obvious* implementation has a subtle bug that careful invariant-reading would catch.

T2 design work goes into Phase 1 launch (prompts + graders + scaffold pivots) before launching trials.

## What I'm NOT recommending based on this finding

- **Abandon Calor.** The result is "annotation alone doesn't matter on T1-prime tasks at this scale." It does *not* show that Calor's compiler enforcement adds no value — that wasn't tested (B4 said v0.5.0 emitter bugs prevent the Calor port at this scaffold's complexity). It also doesn't show that annotations don't help on harder tasks (T2/T3) — that's what we test next.
- **Spend the remaining ~$9.94K on more T1-prime variants.** The signal at N=5 is clear; doubling N won't change a 1.00× into >1.20×. Diminishing returns.
- **Conclude that the program was a mistake.** This pilot tested a *specific reduced version* of Calor's value proposition and got no signal. The full proposition (verified annotations on hard tasks at scale) remains untested.

## Prerequisites status (final)

| | Prereq | Status |
|--|--------|--------|
| B1 | C# annotations on public methods | ✅ |
| B2 | MESS labels stripped | ✅ |
| B3 | MESS coverage table + SerializationTests | ✅ |
| ~~B4~~ | ~~Calor compiler scale validation~~ | dropped (v0.5.0 emitter bugs documented in milestone-2) |
| B5 | T1.A/B/C graders | ✅ (with multiple v3.x patches; T1.C grader remains fragile) |
| B6 | Dry-run T1.B on Opus 4.7 | ✅ |
| B7 | csharp-bare arm | ✅ |

Phase 0 is complete. Decision authorized: pivot to T2.

## Files and run records

All 30 trial workspaces preserved at `bench/research-phase-0/runs/<arm>-<prompt>-<seq>/` with `metrics.json` and `final-diff.patch`. Aggregated stats produced by `bench/research-phase-0/summarize.py`.

## What's next (Phase 1 plan, draft)

1. **Phase 1 design doc** — `docs/plans/research-phase-1/` directory, mirroring the phase-0 structure.
2. **scoring-rubric-v4** — adapt v3 for T2's adversarial-edge-case hypothesis. Likely changes: prompts include known-bug-prone patterns; correctness becomes "did the model introduce the obvious bug or avoid it?" rather than "does the feature work."
3. **T2 prompt design** — write 3 prompts where careful invariant-reading prevents a silent-bug class. Examples:
   - "Add bulk discount: orders with >100 items get 5% off on the line item total" (silent rounding bugs likely)
   - "Add a 'reservation hold' that prevents inventory release for 24h" (terminal-state INV-3 violations likely)
   - "Migrate orders from single currency to multi-currency" (Money.Add invariant violations likely)
4. **Graders for T2** — behavioral, not reflection-based. Each grader exercises the bug class via specific test sequences and asserts the model's implementation didn't introduce the bug.
5. **Run T2 N=5 × 3 × 2 = 30 trials.** Estimate ~$60 at current cost rates. Total program spend after Phase 1: ~$120.
6. **Decision per v4 rubric.** If T2 shows ≥1.5× median Quality ratio with non-inferior cost, authorize confirmatory study. Else pivot to T3.

I will start Phase 1 design after this milestone is committed and reviewed.
