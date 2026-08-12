# §3.1 entry spike — pre-registration

**Status: registered, not yet measured.** This document exists to be committed
*before* any binding is attempted, so the denominator and the adjudication rule
cannot be chosen after seeing results. Nothing in §2 or §3 may be edited once
measurement begins; §5 is filled in afterwards.

## 1. Why this spike gates 0.14

The roadmap (§3.1) names metadata-backed .NET binding as the component that can
sink 0.14 — not the flow checker, which has TIER1A. It is a slice of Roslyn's
binder: exact receiver type, overload resolution, parameter and return types,
generic substitution, nullable annotations.

This repo's own postmortem records systematic underestimation in exactly this
area (`tier1a-postmortem.md` §7.1 V4: overload resolution — 18 overloads on
`Console.WriteLine` — absent from the original estimate; V3's "3–5 days, not
1–2" correction on the adjacent TIER2D spike). The spike is sized on that
record, not on optimism.

**What the compiler does today**, for contrast: call resolution is string-based.
#925 is the measured form of the consequence — a qualified `§C{Module.Function}`
resolves as an *unknown external call*, so the callee's declared effects never
reach the answer. The index built in 0.13.x inherits the same limit and reports
it as residual rather than hiding it.

## 2. The registered shapes

Six families. Each is a `.calr` fixture under
`tests/TestData/BclCallShapes/`, calling real BCL surface, with the **expected
exact signature** recorded alongside it. The expected signature is written by
reading the BCL, not by running the binder.

| id | family | why it is in the set |
|----|--------|----------------------|
| BCL-01 | **overload resolution** | `Console.WriteLine` has 18 overloads; picking by arity alone is indistinguishable from correct on most of them and wrong on the rest. The postmortem names this as the omission that broke the last estimate. |
| BCL-02 | **generic substitution** | `List<T>.Add`, `Dictionary<K,V>.TryGetValue` — the receiver's type arguments must flow into the parameter types, or `Add(string)` and `Add(int)` are the same call. |
| BCL-03 | **extension methods** | `IEnumerable<T>.Select`/`Where` — the receiver is the *first parameter* of a static method on another type. A binder that only looks at instance members finds nothing and silently reports unresolved. |
| BCL-04 | **`params` arrays** | `string.Format(string, params object[])` — arity does not match the declaration, so an arity-keyed lookup fails on a call that is perfectly legal. |
| BCL-05 | **nullable annotations** | `Dictionary.TryGetValue(K, out V?)`, `string?` returns — 0.14's entire premise is that these annotations survive the boundary, so failing to read them is failing the release. |
| BCL-06 | **`ref` / `out`** | `int.TryParse(string, out int)` — the modifier is part of the signature; ignoring it resolves to an overload that does not exist. **See §2.1: this one is not currently expressible.** |

### 2.1 Pre-measurement finding: BCL-06 is not expressible today

Recorded here because it was found while writing the fixtures — *before* any
measurement — and it changes what a BCL-06 failure would mean.

**Calor has no call-site syntax for `ref`/`out` arguments.** `§O` is a
statement-level output declaration, not an argument form, and the parser passes
`argumentModifiers: null` at every call-construction site. The converter emits
`:out` on parameter *declarations* only. So `Int32.TryParse(text, out value)`
cannot be written in Calor at all.

That is a **language gap, not a binding gap**, and the two adjudicate
differently: a binder that cannot resolve a call nobody can write has not
failed. §4 therefore separates them, which it did not in the first draft of this
document. The fixture is retained as a registered shape so the gap stays
visible and is re-tested once the syntax exists.

**These six are the denominator** (five expressible, per §2.1). The spike is measured against them and
nothing else. Shapes discovered later to be hard may not be removed; they may
only be recorded in §5 as newly-registered follow-ups with their own outcome.

## 3. Method

1. Load real reference assemblies (the same `TRUSTED_PLATFORM_ASSEMBLIES` set
   the test suite already uses for Roslyn compilation).
2. For each registered call site, attempt to resolve it to **one exact member**:
   declaring type, full parameter list with modifiers, return type, and the
   substituted type arguments where generic.
3. A shape counts as **resolved** only if the resolved signature matches the
   pre-recorded expected signature *exactly*. Resolving to the wrong overload
   counts as **wrong**, not as resolved — that distinction is the whole point,
   because a plausible wrong answer is what this area produces.
4. Everything else is **unresolved**.

Resolved / wrong / unresolved are reported per shape, never only as a total: a
70% total made of five half-working families is a different situation from four
solid families and two absent ones, and only the per-shape table distinguishes
them.

## 4. Adjudication — fixed before measuring

| outcome | rule | what 0.14 does |
|---|---|---|
| **GREEN** | all five expressible families resolved, zero wrong | metadata binding proceeds as planned; §3.5 gate 2's bar freezes at the measured fraction over the conversion corpus |
| **AMBER** | ≥4 of the five expressible families resolved, zero wrong | proceed on the **planned exit ramp** (§3.1): ship exact-signature binding for the resolved subset, explicit fail-safe `unresolved` for the rest, and the unsupported families published as a ledger |
| **RED** | any family resolves **wrong**, or ≤3 *expressible* families resolved | metadata binding is re-scoped before any of it merges. A wrong answer is disqualifying at any count, because unresolved is honest and wrong is not |

**Not-expressible is its own category, and it is excluded from the counts
above.** A family Calor cannot express (BCL-06 today, per §2.1) is neither
resolved nor unresolved by the binder — counting it as a binder failure would
adjudicate a language gap as a binding gap and could push a working mechanism to
RED. Such families are reported separately, with the missing syntax named, and
each one is a registered 0.14 work item in its own right.

Expressible families as registered: **five** (BCL-01..05). GREEN/AMBER/RED
counts are over those five.

**No row is a "try harder and re-measure" row.** If the result is RED, the
re-scope is the deliverable, and a second measurement requires this document to
be amended with the reason before it is taken.

The spike's conclusion **freezes §3.5 gate 2's bar**. That bar is a fraction
over the pinned conversion-subject corpus, not over these six shapes — these
establish whether the mechanism works at all; the corpus establishes how far it
reaches.

## 5. Results

*Empty by construction. Filled in after measurement, together with the
adjudicated row and the date.*
