# S2 #1413 fix-num-refusal — Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Round 1 verification:

| Earlier finding | Current disposition and evidence |
|---|---|
| Safe promotions wrongly demoted | **Resolved.** `Z3Verifier.cs:78–87` now checks whether negated safety is satisfiable without preconditions; the caller is at `:515`. Promotion controls require `Proven` at `S2NumericRefusalTests.cs:109–121`. |
| Direct-AST D2 bypass | **Resolved for direct contract literals.** `ContractTranslator.cs:497–504` also rejects oversized signed literals declared 32-bit. The original witness is covered at `S2NumericRefusalTests.cs:130–139`. A binding-path defect remains below. |
| Differential gate accepts missing assumptions | **Resolved.** Exact equality is restored at `DifferentialGate.cs:343–347`; array forms require only reference-model at `DifferentialFormRegistry.cs:72`. The gate, registry, reports, and production overflow test match the merged main parent. |
| Shared helpers weaken expectations | **Resolved.** `NumericExecutableSemanticsTests.cs:222–245` requires exact `Assumed` or exact `Proven`, using C# boundary evaluations. `OverflowSoundnessBenchmark.cs:529–543` separates the verdicts; the comparison control still requires `Proven` at `:299`, including the benchmark branch at `:394`. |
| K-induction truncates oversized bounds | **Partially resolved.** Signed oversized endpoints are rejected internally at `KInductionProver.cs:251–253` through `:581`. The public method converts that result to `Unknown` at `:144–148`. Oversized steps and wrapped unsigned bounds still bypass refusal; see below. |
| Imprecise tests and missing cache coverage | **Partially resolved.** All checked theory rows now require exact `Assumed` and assumptions at `S2NumericRefusalTests.cs:105–106`. The new cache test at `:143–152` checks canonical-key distinction and the version constant, but never exercises cache lookup or eviction. |
| Reports misdescribe their gate | **Resolved.** Restored reports consistently contain **435 Proven, 150 Assumed, 585 Refuted**, totaling 1,170 cases (`verifier-runtime-differential.json:29–32`). The exact-set statement and reference-only array note now match the gate (`verifier-runtime-differential.md:40`, `:46`). |
| CHANGELOG overstates repaired channels | **Resolved for checked arithmetic.** `CHANGELOG.md:33–39` scopes the change to postconditions and describes universally safe arithmetic separately. |

The new test count is correct: **5 facts + 10 theory rows = 15**, giving **411 → 426**. Claimed test passes and pre-fix failure counts were not independently executed.

**MAJOR — The k-induction fix silently replaces an oversized explicit step with `1`.**

All source paths below are under `src/Calor.Compiler/`.

`Verification/Z3/KInduction/KInductionProver.cs:581` now returns `null` for an oversized literal. However, `:304` consumes that result as:

```csharp
var stepValue = loop.Step != null ? GetIntValue(loop.Step) ?? 1 : 1;
```

Thus an explicit step corresponding to `INT:3000000000` is modeled as `1`. With bounds `0..10` and invariant `i <= 11`, the base and modeled inductive cases succeed, reaching `KInductionStatus.Proven` at `:324`. The refusal fix therefore creates proofs over a substituted transition.

The range check also examines only signed `Value`. Binding preserves an unsigned literal’s wrapped signed value separately from its magnitude (`Binding/Binder.cs:2978–2983`; `Binding/BoundNodes.cs:586–589`). A bound representing `ULONG:18446744073709551615` has `Value == -1`, passes `GetIntValue`, and becomes a 32-bit upper bound of `-1`. Entry `0`, invariant `i >= 0`, and step `1` reproduce the contradictory inductive assumptions and `Proven`.

Reject unrepresentable explicit steps, check unsigned magnitude, and propagate `Unsupported` through the public method. These are residual k-induction defects outside the registered postcondition row. Regression infrastructure already exists in `tests/Calor.Compiler.Tests/Analysis/KInductionTests.cs:18–35`.

**MAJOR — D2 refusal is lost when contract simplification removes an oversized literal.**

Concrete source:

```calor
§M{m1:Control}
  §F{f1:Probe:pub} () -> i32
    §E{}
    §S (== (? BOOL:true LONG:0 INT:3000000000) LONG:0)
    §R INT:0
```

The lexer marks the oversized `INT:` at `Parsing/Lexer.cs:2329–2331`. Both conditional branches nevertheless have signed 64-bit width. `Verification/ExpressionSimplifier.cs:434` considers them the same literal type without checking `WidthInferred`; `:413–418` then replaces the conditional with `LONG:0`.

`Program.cs:935–936` performs this simplification before verification at `:1033–1034`. Consequently, the translator receives `LONG:0 == LONG:0`, reports unconditional `Proven`, and never encounters the D2 refusal.

The predicate is true, but the original source contains the registered unsupported-refused form. Validate numeric refusal forms before simplification, or preserve their refusal classification across rewrites. Add a production-path regression containing this conditional.

**MAJOR — The new nullable literal translation is not propagated through immutable bindings.**

`Verification/Z3/ContractTranslator.cs:503–504` now returns `null` for the author’s direct-AST oversized 32-bit witness. Its binding caller still passes that result directly into `MkEq` at `:82`.

For example, call `VerifyPostcondition` with output `i32`, postcondition `result == 0`, and this body:

```csharp
[
    new BindStatementNode(span, "n", "i32", false,
        new IntLiteralNode(span, long.MaxValue) { IsLong = false },
        attributes),
    new ReturnStatementNode(span, new IntLiteralNode(span, 0))
]
```

`Verification/Z3/Z3Verifier.cs:1286–1287` routes the initializer through `BindInt32Constant`, which constructs an equality with a null operand. Body encoding at `:387` sits outside the verifier’s solver-exception handling, so verification throws instead of returning `Unsupported`.

Make binding translation propagate refusal before constructing constraints. Cover this direct-AST body path alongside the existing direct-postcondition witness.

**MINOR — The cache regression still does not test the claimed eviction or warm-cache behavior.**

`tests/Calor.Verification.Tests/S2NumericRefusalTests.cs:143–152` compares two canonical strings and checks the version constant. It does not instantiate a cache, load a stale entry, or exercise either warm `LONG:`/oversized-`INT:` ordering. All source witnesses disable caching at `:24`.

Production invalidation appears correct: `VerificationCacheEntry.cs:225` requires exact format equality, and `VerificationCache.cs:234–240` rejects invalid entries. Add regression cases demonstrating that numeric `Proven` entries stamped 1.20/1.21 cannot be served and that warming one literal spelling cannot affect the other.

**NIT — The manifest’s final correction still gives the wrong earlier-test count.**

`eng/test-manifest.json:108` ends with “18 earlier tests were updated.” The listed verification changes total **15**: 2 + 7 + 3 + 3. Including the compiler cache-version test gives **16**, while `ProductionOverflowRuntimeTests` is unchanged.

No dotnet commands were run. Findings are based on source tracing; report totals were checked with Python.

## Response

| Finding | Disposition |
|---|---|
| MAJOR k-induction replaces an oversized step with 1; wrapped unsigned bounds | Fixed. An explicit step that cannot be modeled makes k-induction `Unsupported`; it is no longer replaced by 1. An unsigned literal is checked by its magnitude, so `ULONG:` max no longer passes as -1. Regression: `KInductionTests.KInductionProver_RefusesUnrepresentableBoundsAndSteps`. It has a control (the representable loop proves `i <= 11`) and failed before this fix. The public method still reports exhausted attempts as `Unknown`; it never reports `Proven`. |
| MAJOR D2 refusal lost through simplification | Fixed. `ExpressionSimplifier.Simplify` leaves an expression that holds a refused D2 literal unchanged (`ContractTranslator.ContainsRefusedLiteral`), so the refusal reaches the translator. Regression: `OversizeLiteralRemovedBySimplification_IsStillRefused` (your conditional witness, which failed before). |
| MAJOR binding path passes a null literal into an equality | Fixed. `BindInt32Constant` returns null for a refused literal, and the body encoder reports the binding as refused (D2) instead of throwing. Regression: `OversizeLiteralInABodyBinding_IsRefusedNotThrown` (your direct-AST body, which failed before). |
| MINOR no warm-cache or eviction behavior test | Fixed for warm-cache ordering. `WarmCache_DoesNotCarryAVerdictBetweenIntAndLongSpellings` warms one spelling in a real cache directory and checks the other, in both orders: `INT:` stays `Unsupported`, and `LONG:` stays `Proven`. Format eviction is the existing exact-version check, and the test pins the format at ≥ 1.22. |
| NIT manifest count | Corrected with a zero-delta note: 15 earlier `Calor.Verification.Tests` cases plus the compiler cache-version pin, 16 in total. |
| Tests | `S2NumericRefusalTests` has 18 cases (+3) and `KInductionTests` +1. The KInduction test is a plain `[Fact]` that asserts Z3 is present; it adds no skip site. Full Verification 429 passes; the Compiler suite run is reported in the PR. |
