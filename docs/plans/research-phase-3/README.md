# Research Phase 3 — Test Calor's Compiler-Enforcement Layer (Option A)

User authorization: 2026-05-01 ("go with option A").

## Why this phase exists

Phases 0–2 tested the *information* component of Calor's value proposition: structured comments that mirror what `§E{}`, `§Q`, `§S` would say. Three sequential nulls on the primary cell (T1.B, T2.B, T3.B all 1.00× median ratio) ruled out annotation-only as a differentiator at this scaffold scale.

The *enforcement* component remains untested. Calor's compiler rejects code where:
- A method declared `§E{}` (pure) calls something with effects
- A method's preconditions/postconditions can't be satisfied (Z3-verified)
- An effect is used but not declared

Phase 3 tests whether *machine-verified* annotations help where unverified annotations did not. If yes → Calor's value is in the verifier. If no → Calor's full proposition is disconfirmed at this scale.

## Recon findings (2026-05-01)

I attempted Phase 3 setup in one session. Three Calor v0.5.0 emitter bugs documented in milestone-2 block a clean port:

- **CS1729:** `record struct Money(decimal Amount, string Currency)` → `§CL` class loses positional ctor; `§NEW{Money} §A 0 §A "USD"` calls fail to bind.
- **CS8618:** `§PROP` emits `public T X { get; set; }` without `required`/`init` for non-nullable reference types (Sku, Money post-class-rewrite).
- **CS8917:** LINQ `Aggregate(0m, (acc, li) => ...)` lambda hoisted to a `var lam = (acc, li) => ...` declaration, losing inference.

**Recon confirmed** that *plain class with explicit constructor* roundtrips cleanly:
```
class Money { public decimal Amount { get; } public string Currency { get; }
              public Money(decimal a, string c) { ... } }
```
→ Calor `§CL{Money}` with `§CTOR{ctor}` → emitted .g.cs builds clean.

So the workaround path is: simplify the C# scaffold to avoid the three breaking patterns, then convert. This is what Phase 3 needs to do.

## Two paths to choose between

### Path 1: Fix Calor compiler bugs (out-of-band, then port)

Fix CS1729 (record-struct ctor synthesis), CS8618 (`§PROP` modifier emission), CS8917 (lambda hoisting). Then convert the existing csharp-baseline to Calor as-is. Estimated: 3–5 days compiler work + 1 day port + 1 day trials.

**Pro:** Fixes benefit Calor v0.5.x users beyond this experiment.
**Con:** Compiler debugging is a deep rabbit hole; out of strict research-budget scope.

### Path 2 (recommended): Simplify scaffold to working idioms (1 session each)

Hand-modify the C# scaffold to avoid the breaking patterns, then convert.

Required changes:
- `Money` and `Sku`: `record struct` → plain `class` with explicit ctor, equality operators added back (commit-staged)
- Entities (`InventoryItem`, `OrderLineItem`, `Payment`, `StockReservation`): non-nullable `Sku`/`Money` properties need `required` modifier OR explicit ctor
- `Order.CalculateTotal` and `OrderService.RecalculateTotal`: `LineItems.Aggregate(...)` rewritten as `foreach` loop

Then:
- Apply same changes to `csharp-baseline` (the *bare* arm of Phase 3) so the comparison is fair
- Convert via `calor migrate` to produce Calor source
- Hand-augment with `§E{...}`, `§Q (...)`, `§S (...)` declarations on every method
- Compile via `calor --enforce-effects` to enable the enforcement layer
- Verify all 42 tests pass through the .g.cs
- Set up a `.calor-effects.json` manifest declaring effects of external dependencies (logger, repos)
- Run T2.B-style trials (the proven-differentiating prompt class)

Estimated: 4–6 hours scaffold work + 4–6 hours Calor authoring + 2–3 hours pipeline verification + 2 hours trials + 1 hour grading = **~15–20 hours total.**

**Pro:** Tests the actual hypothesis without spending budget on compiler work.
**Con:** The simplified scaffold isn't representative of all modern C# (no record structs, no LINQ Aggregate). Documented as a known limitation.

## What's been done in this session

| File | Status |
|------|--------|
| `bench/research-phase-3/csharp-base/src/WholesaleOrders.Domain/ValueObjects/Money.cs` | Rewritten as plain class with explicit ctor + equality operators. Recon-tested: roundtrips cleanly through `calor convert`. |
| `bench/research-phase-3/csharp-base/src/WholesaleOrders.Domain/ValueObjects/Sku.cs` | Same simplification. |
| Entity property fixes (Sku, Amount of Money) | **Pending** — need `required` modifier or explicit ctor. |
| Aggregate → foreach rewrite | **Pending** in `Order.cs` and `OrderService.cs`. |
| Calor migration | **Pending.** |
| Effect annotations | **Pending.** |
| Trials | **Pending.** |

## Decision rules for Phase 3

Same as v4 rubric:

- T2.B median Quality ratio (Calor / bare-C#) ≥ 1.50× → strong signal → Phase 4 confirmatory
- 1.20× ≤ ratio < 1.50× → suggestive → continue investigation
- 0.80× ≤ ratio < 1.20× → null → annotation+enforcement also doesn't help; **Calor program disconfirmed**
- < 0.80× → negative → Calor introduces friction without benefit; stop

A Phase 3 null at this point would be the strongest evidence yet for Option C (terminate program with documented negative finding). The user has invested ~$185 + 90 trials' worth of design work; a clean Phase 3 null vs the bar would close the loop.

## Recommended next step

**Run Phase 3 in a focused future session.** This isn't the kind of experiment that benefits from my pushing through in a half-completed state. The scaffold simplification + Calor authoring + effect manifest setup is detail-heavy work where mistakes compound.

If the user wants me to try in this same session anyway (by spinning out further to a sub-agent for the scaffold conversion, for instance), I can — but the recommendation is a fresh focused session.
