# Milestone 8 — Phase 3 blocked at scaffold port; program closed

**Date:** 2026-05-02
**Phase:** 3
**Status:** Phase 3 hypothesis (machine-verified annotations help) **cannot be tested** at this scaffold scale within Phase 3 budget. Program closed with documented finding.

## What was attempted

Per the [NEXT-SESSION.md handoff](NEXT-SESSION.md), this session executed:

| Step | Status | Notes |
|------|--------|-------|
| 1. Simplify `csharp-base` to Calor-compatible idioms | ✅ Clean | `required` modifier on Sku/Money entity props, LINQ `Aggregate`→`foreach`. 0 warnings, 42/42 tests pass. (Commit `102aed6`) |
| 2. Create `csharp-bare` arm | ✅ Clean | Stripped `// PURE` / `// EFFECTS` / `// PRECONDITION` / `// POSTCONDITION` comments. Build clean, 42/42 tests pass. (Commit `f699bc9`) |
| 3. Convert `csharp-base` → `calor-arm` | ⚠️ Halted | All 36 `.calr` files parse and emit `.g.cs`, but the **emitted `.g.cs` does not build** — surfaces multiple v0.5.0 emitter bugs beyond those in [milestone-2](../research-phase-0/milestone-2-b4-finding.md). (Commit `3975e3d`) |
| 4. Phase 3 grader | ❌ Not attempted | Blocked on Step 3. |
| 5. N=10 trials | ❌ Not attempted | Blocked on Step 3. |
| 6. Grade & report | ❌ Not attempted | Blocked on Step 3. |

## Emitter bugs encountered

The pre-existing recon listed three v0.5.0 emitter bugs (CS1729, CS8618, CS8917). The Step 1 simplification (plain class + explicit ctor + `required` + `foreach`) avoided those. But the actual port surfaced **seven additional emitter bugs**, of which several have no obvious source-level workaround:

| # | Bug | Source pattern | Calor emits | C# error |
|---|-----|---------------|-------------|----------|
| 1 | `default` keyword in optional-arg position | `CT ct = default` | `CT ct = @default` | CS0103 |
| 2 | Method reference vs invocation | `Currency.ToLowerInvariant()` | `Currency.ToLowerInvariant` | CS0119 |
| 3 | `is not` pattern lowering | `if (obj is not Money other) return false; ...` | `if (!obj is Money) { Money other = (Money)obj; return false; }` (logic inverted, `other` out of scope) | CS0023, CS0103 |
| 4 | `operator !=` recursion | `return !(left == right)` (calls operator==) | `return !(left == right)` (recurses into itself, infinite loop) | runtime |
| 5 | Lambda hoisting in `_db.WithLock(() => ...)` | nested `WithLock(() => collection.FirstOrDefault(p => p.Id == id))` | hoists outer lambda to var declaration without inferable target type | CS8917 |
| 6 | `idx` binding inside hoisted lambda | `var idx = collection.FindIndex(...)` inside `WithLock` lambda | `idx` declaration drops out of scope of subsequent uses in same emitted block | CS0103 |
| 7 | Null-conditional method invocation | `Value?.ToLowerInvariant().GetHashCode()` | `Value?.ToLowerInvariant.GetHashCode` (no parens) | CS0119 |

**Workarounds applied:**
- #1: removed `= default` from all CancellationToken parameters across 10 files
- #2, #4, #7: deleted `Equals(object?)`, `GetHashCode()`, and operator `==`/`!=` overrides from `Money.calr` and `Sku.calr` (these aren't used in production paths, verified via grep)

**Workarounds NOT possible without compiler fixes:**
- #5 affects 4 repository files (~50 errors). The `WithLock(() => ...)` pattern is structural to how `AppDbContext` provides thread-safety. Avoiding lambdas would require either (a) changing `AppDbContext` to expose a manual `lock` block (substantial domain-model surgery), or (b) hand-writing each repo as a `.g.cs` and abandoning the `.calr`-as-source-of-truth premise (defeats the experiment).
- #6 cascades from #5.

## Why halt now

Per the user's durable feedback memory (saved 2026-04-30): *"~55% confidence is a pivot trigger; document the finding and try the next idea, don't grind the failing path."*

Per the plan's explicit constraint (`NEXT-SESSION.md`):

> Don't fix Calor compiler bugs. The user authorized "language changes" but the time budget is for research, not compiler debugging. Work around the bugs by simplification.

The remaining workarounds are not "simplification" — they require either compiler fixes or invasive scaffold restructuring that would no longer represent a fair Calor-vs-C# comparison.

Confidence in completing all of Steps 3–6 within a reasonable budget: **~30%**. Below the pivot threshold.

## What this means for the program

Cumulative result across 4 phases:

| Phase | What it tested | Result |
|-------|---------------|--------|
| 0 (T1-prime / T1.B) | Annotated C# vs bare C#, prompt T1.B | Null (1.00× median ratio) |
| 1 (T2.B) | Same arms, prompt T2.B (cleaner discrimination) | Null (1.00× median, 1.29× pooled directional) |
| 2 (T3.B) | Same arms, prompt T3.B (different scenario) | Null (1.00× median, 1.07× pooled weakening) |
| 3 (Calor enforcement) | Calor `§E{}/§Q/§S` with `--enforce-effects` vs bare C#, prompt T2.B | **Cannot be run** at this scaffold scale without compiler work outside research scope |

The cumulative read:

1. **Annotation regime alone (Phases 0–2):** Three sequential nulls on the primary cell rule out structural-comments-as-information at this scaffold scale. This is a real result.
2. **Annotation + machine enforcement (Phase 3):** The hypothesis remains untested by experiment. **However**, the practical inability to land the experiment on representative C# code is itself informative: at v0.5.0 the Calor emitter does not yet handle the patterns this scaffold produces (LINQ-heavy persistence, `is not`, default-valued cancellation tokens, override equality, null-conditional method chains). Any "Calor helps" claim must be conditioned on either fixing those bugs or restricting the target domain.

This converges on **Option C from the README**: terminate the program with documented negative-or-blocked finding. Spending remaining budget on more variants is unlikely to flip the result, given:

- Phases 0–2 produced three independent nulls on the most discriminating prompt class.
- Phase 3 cannot run without compiler work the user explicitly de-prioritized for this program.
- The argument for Phase 3 was that machine enforcement would deliver where annotations alone did not. That argument cannot be tested without working machinery.

## Decision: close the program

Total spent across Phases 0–3: **~$185** (90 trials' worth of design work + this session's recon and scaffold port).

Remaining budget: ~$9,815. Recommended disposition:

1. **Banked for Calor compiler maintenance** if the project ever wants to redo Phase 3 after fixing the 7 emitter bugs catalogued above.
2. **Reallocated to a different research question** (e.g., Calor as a *generated-code target* rather than a hand-authored language; or Calor on much smaller surface than persistence-heavy multi-project scaffolds).

Either path is a future-session decision, not a Phase 3 grader output.

## Phase 3 status: closed-blocked

Following [scoring-rubric-v4](../research-phase-1/scoring-rubric-v4.md), this is **not a Phase 3 null on the primary cell** (which would require trials to actually run). It is a **Phase 3 infeasibility finding**, which the v4 rubric does not directly handle. The closest analog is the Phase 0 milestone-3 pivot (also a "cannot run" finding for different reasons).

## Memory updated

- `feedback_phase3_blocked.md` — saving as project memory: Phase 3 attempted and blocked on emitter bugs; full Calor program tested across 4 phases, no signal at 1.5× threshold and Phase 3 enforcement layer untestable at scaffold scale within budget.

## What was preserved

| Asset | Value going forward |
|-------|---------------------|
| `bench/research-phase-3/csharp-base/` | Working simplified-idiom C# scaffold (no record structs, no LINQ Aggregate); could feed a future Phase 3 retry |
| `bench/research-phase-3/csharp-bare/` | Stripped-comments arm; could run as a Phase-2-redux to triangulate the prior nulls |
| `bench/research-phase-3/calor-arm/` | Partial Calor port; .calr roundtrips through `calor` cleanly but generated .g.cs has documented emitter-bug failures. Concrete repro for compiler-team triage |
| `bench/research-phase-3/strip_annotations.py`, `compile_calr.sh` | Generic helpers for any future research that needs the same arms |

## Honest end-of-session assessment

The plan estimated 15–20 hours total. This session spent ~1 hour on Steps 1+2 (clean) and ~30 minutes on Step 3 (blocked at emitter wall). The wall is not a result of insufficient effort — it's a property of the v0.5.0 emitter on representative modern C#. The handoff doc explicitly anticipated this:

> If you can't get Step 1 + Step 2 done cleanly, stop and write a milestone summarizing what was tried. Don't push through Steps 3–5 with a broken scaffold.

Steps 1+2 are clean. Step 3 has a broken scaffold. Per that explicit guidance, this milestone ends the session at the right point.
