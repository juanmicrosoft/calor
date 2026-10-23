# S2 #1413 fix-num-refusal — Codex review round 3

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Earlier findings verified against the current code:

| Earlier finding | Current disposition and evidence |
|---|---|
| Safe promotions wrongly demoted | **Resolved.** `src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:78–87` checks universal safety without preconditions; the caller is at `:515`. Promotion controls require `Proven` at `tests/Calor.Verification.Tests/S2NumericRefusalTests.cs:121`. |
| Direct-AST D2 bypass | **Original witness resolved.** `ContractTranslator.cs:507–514` rejects oversized signed 32-bit `Value`; the regression requires `Unsupported` at `S2NumericRefusalTests.cs:139`. A different representation bypass remains below. |
| Differential gate accepts missing assumptions | **Resolved.** Exact equality is restored at `tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DifferentialGate.cs:343–347`; array allowances are reference-model only at `DifferentialFormRegistry.cs:72`. |
| Shared helpers weaken expectations | **Resolved.** `NumericExecutableSemanticsTests.cs:222–245` requires exact outcomes. `OverflowSoundnessBenchmark.cs:529–543` separates exact checked-arithmetic demotion from `Proven`; the comparison control uses the latter at `:299`. |
| K-induction truncates `for` bounds or substitutes oversized steps | **Proof bypasses resolved.** Bounds check signed range and unsigned magnitude at `KInductionProver.cs:583–585`; explicit unrepresentable steps stop at `:305–307`. The public method still converts `Unsupported` to `Unknown` at `:144–148`, rather than propagating the requested status. |
| Imprecise checked-arithmetic tests | **Resolved.** All three theory rows require exact `Assumed` and `[checked-arithmetic]` at `S2NumericRefusalTests.cs:105–106`. |
| D2 lost through simplification | **Original witness resolved.** `ExpressionSimplifier.cs:33–34` preserves expressions containing refused literals; traversal is at `ContractTranslator.cs:90–94`. The production regression requires `Unsupported` at `S2NumericRefusalTests.cs:154`. |
| Binding passes null into equality | **Resolved.** `ContractTranslator.cs:79–80` checks translation before constructing equality; `Z3Verifier.cs:1289–1290` propagates refusal. The regression at `S2NumericRefusalTests.cs:158–177` exercises the original body. |
| Cache coverage | **Partially resolved.** Both spelling orders are exercised at `S2NumericRefusalTests.cs:192–213`. Actual 1.20/1.21 eviction remains untested; see below. Production invalidation checks exact format equality at `VerificationCacheEntry.cs:225`, consumed by `VerificationCache.cs:234–240`. |
| Reports misdescribe the gate | **Resolved.** Reports, gate, registry, and production overflow test match the merged main parent. JSON totals are **435 Proven, 150 Assumed, 585 Refuted** (`bench/phase0-agent-native/verifier-runtime-differential.json:29–32`); form totals independently sum to 1,170 cases and 40 eliding forms. |
| CHANGELOG overstates checked-arithmetic channels | **Resolved.** `CHANGELOG.md:35–41` scopes the demotion to postconditions. Its new k-induction claim at `:33–34` remains too broad; see below. |
| Manifest’s earlier-test count | **Resolved by the final correction** at `eng/test-manifest.json:108`: 15 verification cases plus the compiler cache-version pin, totaling 16. |

The new counts are correct: **8 facts + 10 theory rows = 18** verification cases, hence 411 → 429; the compiler manifest adds one case.

**MAJOR — `while` k-induction still truncates oversized bounds and can prove a false invariant.**

Paths below are under `src/Calor.Compiler/Verification/Z3/KInduction/`.

The range check added to `KInductionProver.GetIntValue` does not protect the `while` overload. `WhileConditionAnalyzer.cs:405–410` still returns unchecked `BoundIntLiteral.Value`. The prover then constructs 32-bit bounds at `KInductionProver.cs:378–384` and `:478–487`.

Concrete bound-loop witness:

- Condition: `i <= INT:3000000000 && i >= INT:0`, with the upper comparison first.
- Recognized transition: `i = i + 1`.
- Requested invariant: `i < 0`.

The analyzer combines the bounds while retaining the first comparison operator (`WhileConditionAnalyzer.cs:90–96`). The encoded upper bound becomes **−1294967296**. Consequently:

- The base case asserts `i >= 0 && i <= -1294967296`, which is contradictory.
- The inductive query asserts the truncated upper bound for the final iteration (`KInductionProver.cs:443–447`), then negates `i < 0`, creating another contradiction.

The path therefore reaches `KInductionStatus.Proven` at `:457–461`, although the real condition permits entry at `i == 0`, where the invariant is false.

Apply representability checks to the while analyzer’s bounds and transition deltas, including unsigned magnitude. Add a while regression. This is a residual outside the registered postcondition row, but it contradicts the new CHANGELOG claim.

**MAJOR — D2 representation validation still trusts a wrapped signed `Value`.**

The public constructor at `src/Calor.Compiler/Ast/ExpressionNodes.cs:66–84` permits:

```csharp
new IntLiteralNode(span, ulong.MaxValue,
    IntegerLiteralSign.Positive,
    IntegerLiteralBase.Decimal,
    IntegerLiteralWidth.Bits32,
    IntegerLiteralSignedness.Signed)
```

Its positive magnitude is converted with an unchecked cast at `:84`, producing `Value == -1`.

Both refusal checks examine that wrapped value, rather than validating sign and magnitude against the declared width (`ContractTranslator.cs:90–94`, `:507–508`). The literal therefore bypasses D2 and translates as signed 32-bit `-1` at `:525`.

A direct postcondition comparing this literal `< INT:0` translates to true and obtains unconditional `Proven`. Meanwhile, `CSharpEmitter.cs:3226–3228` and `:3244` emit the positive magnitude, `18446744073709551615`.

Validate the literal’s sign/magnitude representation before translating or simplifying it. Preserve valid explicit unsigned literals, and add this wrapped-magnitude SDK regression alongside the original direct-AST witness.

**MINOR — The cache regression still does not establish the claimed eviction or warm path.**

`tests/Calor.Verification.Tests/S2NumericRefusalTests.cs:223–232` compares canonical keys and checks the version constant; it never loads a stale entry.

The new warm test calls `Compile(first)` at `:212` without checking its verdict or confirming that any entry was written. It can pass when persistence fails and both compilations run cold. Existing cache regressions explicitly establish cache writes at `tests/Calor.Compiler.Tests/S2CacheLiteralWidthTests.cs:101–107`; their forged-entry eviction case stamps only **1.19** at `:148`.

Add warm-path assertions and forged numeric `Proven` entries stamped **1.20 and 1.21**, with a current-format control showing that lookup is exercised.

No dotnet or native Z3 execution was performed. Findings follow source tracing; report totals and test-case counts were checked independently.

## Response

| Finding | Disposition |
|---|---|
| MAJOR `while` k-induction truncates oversized bounds | Fixed in code. `WhileConditionAnalyzer.GetIntValue` applies the same representability check as the `for` path: the signed range, and the magnitude for unsigned values. A bound or transition delta outside int32 is no longer analyzed, so no truncated bound reaches the prover. No regression test: I could not drive the analyzer to that path from Calor source. The comparison against an oversized literal goes through a conversion that the analyzer does not unwrap, so it returns Unknown before and after. This residual is outside the registered postcondition row. The CHANGELOG states the k-induction refusal for both loop forms. |
| MAJOR D2 trusts a wrapped signed `Value` | Fixed. `ContractTranslator.IsRefusedLiteral` checks the sign and magnitude against the declared width rather than the possibly wrapped `Value`. A negative magnitude may be one larger than the positive limit. The simplifier guard and the translator share this check. Regression: `WrappedMagnitudeLiteral_IsRefused` (your `ulong.MaxValue` 32-bit witness), which failed before. Explicit unsigned literals are unaffected. |
| MINOR cache eviction and warm path | Fixed. `ForgedProvenEntriesFromOlderFormats_AreNotServed` verifies a demoted overflow shape (`Assumed`) with the cache on. It then rewrites the entry to `Proven`. Stamped with the current format, the forged entry is served, which is the control showing the lookup runs. Stamped 1.20 or 1.21, it is rejected and the verdict stays `Assumed`. The warm-ordering test now asserts the prime verdict and that the cacheable spelling wrote an entry (the refused spelling writes none). |
| Rounds | This was round 3, the last. One verification-only pass follows, as the S2 brief allows. |
