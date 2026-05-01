# Milestone 2 — Calor port hits compiler emission bugs at scale

**Date:** 2026-05-01
**Phase:** 0
**Status:** B1 ✅ / B2 ✅ / B3 ✅ / **B4 ⚠️ partial fail / B5 blocked / B6 blocked**

## What's done since Milestone 1

- **Rubric v2** committed (`6c5db3b`): pilot framing, T1.B as primary, median over mean-of-ratios, Quality and CostEfficiency split, six blocking prerequisites.
- **B1** (information equality): EFFECTS / PRECONDITION / POSTCONDITION comments added to all public service / validator / value-object methods.
- **B2** (label strip): all `// MESS-N:` inline labels removed.
- **B3** (MESS coverage): `SerializationTests.Customer_Json_Includes_LegacyCustomerCode` added; MESS-3 dropped (untestable); MESS-4 dropped (subjective). Coverage table at `docs/plans/research-phase-0/mess-coverage.md`.
- Scaffold + B1–B3 committed at `3b423c1`.

C# baseline build: 0 warnings, 0 errors. **42/42 tests pass** (was 41 in milestone-1; +1 SerializationTests).

## B4 finding — Calor port

**Single-file conversion + roundtrip works:** `OrderService.cs` (158 LoC) → Calor (255 LoC) → `.g.cs` (209 LoC) compiles cleanly under `--permissive-effects`. Public method surface preserved.

**Whole-project conversion fails:** `calor migrate src/WholesaleOrders.Domain` converts 11/13 files cleanly and 2 partial. After deleting the source `.cs` and rebuilding from the emitted `.g.cs`, **12 build errors** across 7 files. Three distinct root causes:

| Error | Count | Root cause |
|-------|-------|-----------|
| **CS1729** — no constructor matching N args | 4 | C# `record struct Money(decimal Amount, string Currency)` and `record struct Sku(string Value)` were converted to `§CL` classes with `§PROP` declarations. The emitter does not synthesize a positional constructor; methods that do `new Money(0m, "USD")` fail to bind. |
| **CS8618** — non-nullable property without `required` or initializer | 6 | Calor emitter generates `public T X { get; set; }` for `§PROP` without `required` modifier or `init` accessor, even when the original C# field was a non-nullable value object that needs explicit initialization. |
| **CS8917** — delegate type cannot be inferred | 1 | LINQ `Aggregate((acc, li) => ...)` is hoisted into a separate `var _lam016 = (acc, li) => ...` declaration without type annotations, breaking inference that worked in the original inline call. |

### What this means

The Calor compiler at v0.5.0 has fidelity gaps with idiomatic modern C#:
- Record structs with primary constructors don't roundtrip
- Non-nullable value-object properties emit unsafe code
- LINQ lambdas lose type inference when hoisted

These aren't exotic features — `record struct` is standard in modern C# (introduced in C# 10), and `Aggregate` with inline lambda is everywhere. Production .NET codebases will trip these regularly.

For the experiment as designed, **the Calor variant cannot be auto-converted to a working baseline**. B4 fails as posed. B5 (graders) and B6 (dry-run) are blocked.

## Options

| | Option | Cost | Tradeoff |
|---|--------|------|----------|
| (a) | Patch Calor compiler bugs | 4–12 hours per bug, 3 bugs | Real Calor improvement; fixes benefit beyond this experiment; in scope per user's "full control of language changes" mandate. Slow. |
| (b) | Hand-augment converted `.calr` files to use working idioms (explicit ctors, init accessors, inline lambdas) | 2–4 hours | Preserves scaffold representativeness. Reveals that real-world Calor code requires non-mechanical authoring even when starting from C#. |
| (c) | Simplify C# scaffold to avoid Calor weak spots (use class with explicit ctor instead of record struct, etc.) | 1–2 hours | Hidden experiment bias: the scaffold is no longer representative of modern .NET; biases Calor's apparent capability upward. |
| (d) | Stop and report B4 as the Phase 0 finding | 0 hours | Honest. The finding "Calor v0.5.0 cannot handle a 45-file representative .NET project without manual intervention" is a meaningful research output — but it ends the program. |

## Recommendation: hybrid (b) + targeted (a)

Spend ~2 hours on (b) — hand-augment the affected files (Money, Sku, Order, the entities with non-nullable Sku) to use Calor idioms that emit clean C#. This unblocks B5/B6 and lets the experiment run.

Then, **independently and in parallel**, file the three compiler bugs as Calor.Compiler issues with reproductions. Fixing them is not Phase 0 work; it's separate Calor v0.5.x maintenance. Fixing CS1729 (record-struct ctor) and CS8618 (init accessors) is probably 4–8 hours each; CS8917 (lambda hoisting) is more.

This way:
- The research program proceeds without further delay.
- The Calor compiler gets the benefit of real-world bug surface.
- The scaffold representativeness is preserved (we hand-augment to working idioms; we don't simplify the original C# to avoid the patterns).
- An honest footnote in the eventual milestone-N report says "the Calor variant required manual augmentation due to v0.5.0 emitter limitations; bugs filed and fixes pending."

## What I'm doing now

Proceeding with (b) — hand-augmenting `Money.calr`, `Sku.calr`, and the entity `.calr` files in `bench/research-phase-0/calor-baseline/`. Targeted edits, not a rewrite. Verify the Domain project builds clean. Then expand to Infra / Services / Api / Tests.

If hand-augmentation hits a wall (e.g., Calor's `§CL` syntax has no way to declare a positional ctor at all), I'll escalate back to the user with a hard-stop assessment.

I'll commit the augmented Calor variant as a separate commit under `bench/research-phase-0/calor-baseline/` so the diff is auditable.

## Updated confidence

- Down to **~55%** that Phase 0 produces a clean directional signal in this scaffold without further Calor work.
- The Calor port fidelity gap is itself a notable finding regardless of T1.B's outcome.
- If hand-augmentation succeeds, confidence returns to ~70%.
- If hand-augmentation fails, the finding sharpens: Calor at v0.5.0 cannot represent the kind of code we want to test on, and the program needs to either fund compiler work or pivot to a different theory.
