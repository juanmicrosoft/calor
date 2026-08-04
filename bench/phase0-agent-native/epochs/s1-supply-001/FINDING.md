# s1-supply-001 — S1 enumeration-only pre-pass: where the supply ceiling actually comes from

**Run:** 2026-08-04, conversion-only, no builds/tests/recovery/bundles, **no eligibility evaluated**.
**Corpus pins:** MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
**Invocation:** `enumerate-supply MediatR Serilog FluentValidation --output bench/phase0-agent-native/epochs/s1-supply-001`
**Governing doc:** [`substrate-s1-kickoff.md`](../../../../docs/plans/substrate-s1-kickoff.md) D-1, D-5.
**Revision 2** — adversarial review of the first record found that it attributed the ceiling to the corpus when a third of it is converter fidelity. The pass now measures all three populations; the numbers below are from the corrected tool.

## The numbers

| Project | Sites (all) | Lost to conversion | Supply (native) | Supply (with-loss) | ratio |
|---|---:|---:|---:|---:|---:|
| MediatR | 2 | 2 | **0** | 0 | n/a |
| Serilog | 15 | 5 | **3** | 7 | 2.33 |
| FluentValidation | 8 | 2 | **6** | 0 | 0.00 |
| **TOTAL** | **25** | **9** | **9** | 7 | 0.78 |

**These are upper bounds** on the native column: build and `RecoverBuildAsync` are skipped, so files that convert but do not compile still count as native. Recovery is strictly subtractive (`Replaced → Reverted` is the only post-conversion status transition, and the loss ledger is populated inside conversion and never after), so the bound is one-directional.

## The ceiling is not one thing, and that is the finding

The first version of this record said "the whole corpus supplies 9" and called MediatR "structurally unable to contribute". Both were wrong in the same way — they attributed to the *corpus* a number produced jointly by three things:

1. **The corpus** — 25 enumerable sites exist across the three subjects.
2. **Converter fidelity** — **9 of those 25 (36%) are in files the converter could not convert at all.** MediatR's supply is 0 *because both of its sites are in an unconvertible file*, not because MediatR lacks them. That is a WS-S1 result, not a venue result.
3. **The frozen operator site predicate** — the largest constraint, and the least visible. `EffectViolation` requires a **block-bodied method with a predefined `int`/`long` return type** (`ExpressibleMutationOperators.cs` `IsIntOrLong`), a restriction that exists only because the corruption mechanism is `return (ORIGINAL) + __calorTaint`. It is unrelated to addressability: the `using`-nested `Directory.Exists` that makes `Calor0410` fire works regardless of return type. An independent census during review put the eligible site universe at **9 of 917 block-bodied methods (~1%)**, with expression-bodied methods and `int`/`long` property getters excluded purely as an artifact of the visitor. A corruption that generalizes (`__calorTaint == 1 ? default(T)! : ORIGINAL`) would widen this substantially at no cost to the differential.

**So "the corpus has no supply" and "our operators are too narrow" are not distinguishable from the native column alone — and the evidence points more at (2) and (3) than at (1).** That matters because the two readings have opposite remedies: venue retirement versus operator breadth plus converter fidelity, which are precisely WS-S0.5's D-S0.5.2 and WS-S1.

**One part of the ceiling is genuinely corpus-side and survives scrutiny:** `IndexOutOfBounds` and `DivByZero` contributed **zero** candidates anywhere. Review confirmed by direct source census that the guard shapes those operators need (`if (d != 0)` with no `else`; `if (i < X.Length)` with no `else`) are near-absent in this corpus — 0 and 1 instance respectively. Widening those matchers would not manufacture meaningful supply.

## D-5 — **SPLIT**, not the ACCEPT the first record reported

Pooled with-loss/native = 0.78 ≥ 0.50, which by the letter of the pre-committed rule is ACCEPT. But the numerator is **6/7 `NullDeref`**, and per operator:

| Operator | With-loss | Native | ratio | verdict |
|---|---:|---:|---:|---|
| `EffectViolation` | 1 | 6 | **0.17** | REJECT |
| `NullDeref` | 6 | 3 | **2.00** | ACCEPT |

The pooled verdict is carried entirely by `NullDeref` — an operator whose own doc comment states that Calor's null checker models `Option`/`Result` unwrap shapes and **not** plain reference null-deref, so "converted corpus code is not expected to trigger it". It exists to disclose that gap honestly, not to supply eligible tasks.

So the pooled figure would have licensed building site-level clause (a) on the strength of supply that eligibility is expected to delete. The rule was honored in letter while its intent — *is site-level clause (a) broadly worth building?* — went untested. **The tool now reports SPLIT when the pooled verdict and the dominant-operator verdict disagree**, and D-5 is referred for an explicit decision with the split on the record, rather than settled by a threshold that did not actually settle it.

Also noted: pooling was never the pre-committed part. The kickoff fixed the threshold, not the aggregation, and the same document forbids pooling for the power claim.

## Consequences for the probe and for A-1.5

1. **`--max-candidates 10` binds on nothing** (0/3/6 per project). The lexicographic-prefix bias D-3 worried about is irrelevant at this n.
2. **D-3's power statement collapses.** At n = 9 pooled a 25% clause-(b) pass rate yields zero passes ~7.5% of the time; per project the probe distinguishes nothing. The funnel probe as designed cannot resolve what it was commissioned to resolve.
3. **The §4 go/no-go input is now three numbers, not one** — 25 sites, 9 reachable after conversion, 9 lost to the converter — and the plan's trigger is worded against "the corpus's candidate-supply ceiling". Adjudicating it on the native column alone would retire the venue on a number whose binding constraints are a mutation-operator design choice and converter fidelity, both of which v0.12 is already chartered to move. **This record does not adjudicate it; A-1.5 does.**

## What this record does not claim

- Not 0 eligible tasks — eligibility was never evaluated; that is the point of the pass.
- No adjudication of PP-S3, PP-S1, or the §4 go/no-go.
- Not a claim about real C# generally — only about these three pinned subjects, under this frozen operator set.
