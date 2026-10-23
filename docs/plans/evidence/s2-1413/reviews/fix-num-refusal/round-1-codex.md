# S2 #1413 fix-num-refusal — Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

**BLOCKING — The checked-arithmetic test demotes shapes that cannot overflow.**  
`src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:501` treats failure to simplify safety to literal `true` as evidence of overflow sensitivity. Z3 simplification is incomplete.

Concrete control:

```calor
§M{m1:Control}
  §F{f1:Probe:pub} (i32:x) -> i32
    §E{}
    §S (> (+ x LONG:1) x)
    §R INT:0
```

The addition runs at signed 64-bit width; no `i32` input can overflow it. Nevertheless, the new branch marks it `checkedArithmeticAssumed`. Promotions are implemented at `ContractTranslator.cs:764` and `:794`.

I reproduced the safety expressions through native Z3 **4.15.7**, without dotnet: safety for `i32 + LONG:1`, `u32 + i32`, and `i32 - u32` did **not** simplify to `true`, while negated safety was **UNSAT** in every case. Thus the implementation is broader than the frozen overflow-sensitive row. Check universal safety independently of preconditions, or classify these provably safe promotions explicitly; this does not restore the prohibited precondition-based upgrade.

**MAJOR — D2 refusal depends exclusively on lexer provenance; public AST construction bypasses it.**  
`ContractTranslator.cs:495` refuses only `WidthInferred`. At `:509`, an unmarked oversized signed literal is automatically modeled at 64 bits—even when its declared width is 32 bits.

For example:

```csharp
new IntLiteralNode(TextSpan.Empty, long.MaxValue) { IsLong = false }
```

`IntLiteralNode` permits this construction (`Ast/ExpressionNodes.cs:35`, `:41`) and leaves `WidthInferred == false`. A direct postcondition `x <= literal`, with `x:i64`, therefore obtains an unconditional proof instead of refusal.

Validate the literal’s representation as well as its provenance. Preserve legitimate explicit `LONG:`, `UINT:`, and `ULONG:` nodes. Add direct-AST regression cases.

**MAJOR — The differential gate now accepts missing required assumptions.**  
`tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DifferentialGate.cs:346` replaces exact equality with nonempty subset membership.

For `array-element-type:i64/u64`, both `reference-model` and `checked-arithmetic` are allowed (`DifferentialFormRegistry.cs:80`). Consequently, a postcondition regression that drops either required assumption passes. The channel difference cited in the comment does not justify accepting arbitrary subsets.

Specify exact expected assumptions **per channel**, preserving the obligation channel’s frozen classification. Also, `DifferentialGate.cs:340` unconditionally accepts `Proven`, so these allowances do not enforce the new postcondition demotion.

**MAJOR — Shared test helpers weaken expectations beyond the frozen changes.**  

- `NumericExecutableSemanticsTests.cs:220` accepts either `Proven` or checked-arithmetic `Assumed` for every successful numeric case, including comparisons, equality, bitwise operations, and shifts. Those cases previously required `Proven`; the overflow row does not authorize their blanket relaxation. It also permits the old `Proven` verdict for overflow-sensitive cases.
- `OverflowSoundnessBenchmark.cs:527` similarly accepts either outcome. This weakens the six bounded overflow tests instead of requiring their registered demotion.
- It also weakens the comparison-only `Unsigned_AlwaysNonNegative_MustBeProven` control (`OverflowSoundnessBenchmark.cs:289`) and the same control inside `RunFullBenchmark` (`:369`). Neither contains overflow-sensitive arithmetic.

The three exact changes in `VerifierTests.cs:79`, `:496`, and `:782`, and the three D1 changes in `W1Slice1SoundnessTests`, follow the stated frozen policy. The shared-helper relaxations do not.

**MAJOR — K-induction still bypasses oversized-literal refusal and truncates bounds.**  
Binding discards `WidthInferred` (`Binding/Binder.cs:2973`; `BoundNodes.cs:561`). K-induction retrieves only the numeric value (`KInductionProver.cs:580`) and constructs **32-bit** bounds regardless of their bound type (`:256`).

For a bound corresponding to `INT:3000000000`, the upper bound becomes `-1294967296`. With entry `0` and invariant `i >= 0`, the base case succeeds; the inductive assumptions `i >= 0 && i <= -1294967296` are contradictory, producing `KInductionStatus.Proven` at `:324`.

This is an existing residual outside the registered numeric postcondition channel, but the new translator refusal does not close it.

**MINOR — The new tests do not pin most checked demotions or the cache repair.**  
`S2NumericRefusalTests.cs:105` merely rejects `Proven`; `Refuted`, `Unsupported`, or timeout can pass. Only the duplicated `u32` witness at `:122` requires exact `Assumed` plus the exact assumption. The signed/i64 cases need equally precise assertions.

All new cases disable caching (`:24`). There is no test for the new `WidthInferred` hash distinction, warm `LONG:`/oversized-`INT:` ordering, or eviction of numeric `Proven` entries stamped 1.20/1.21. The changed cache-version test still accepts 1.20 (`S2CacheLiteralWidthTests.cs:284`).

The manifest increment is arithmetically correct: **3 facts + 8 theory rows = 11**, hence 411 → 422. The claimed eight pre-fix failures were not independently executed.

**MINOR — The regenerated reports are numerically consistent but misdescribe their gate.**  
The JSON form totals sum correctly to **429 Proven, 156 Assumed, 585 Refuted**, covering 1,170 cases and 40 eliding forms. However:

- `bench/phase0-agent-native/verifier-runtime-differential.md:46` still promises the **exact** production assumption set.
- Its array encoding note at `:40`, and the corresponding JSON note, still claim proofs depend **only** on reference-model, despite the added checked-arithmetic allowance.
- The generator retains both stale descriptions (`DifferentialGate.cs:674`).

Update the generator and regenerate after restoring precise expectations.

**MINOR — CHANGELOG overstates the channels repaired.**  
`CHANGELOG.md:33`–`:36` describes checked contracts generally, but unconditional numeric proofs remain:

- **Preconditions:** `Z3Verifier.cs:168` reports satisfiability as `Proven`, without arithmetic demotion.
- **Obligations:** `ObligationSolver.cs:201` still uses precondition/fact entailment. With `x < int.MaxValue`, an obligation `x + 1 > x` remains `Proven`/`Discharged`; `CSharpEmitter.cs:9928` removes its guard.
- **Implications:** `Z3ImplicationProver.cs:150`–`:160` applies no arithmetic demotion. `true → (x + 1 - 1 == x)` remains a bit-vector proof.
- **Guard discovery:** `GuardDiscovery.cs:280` validates `P ∧ ¬P` without arithmetic safety.
- **Vacuous postconditions:** `Z3Verifier.cs:301` returns `Proven(vacuous)` before translating the refused postcondition. These retain guards.

These are **not all violations of the frozen numeric row**: that row explicitly names the postcondition channel (`registration.json:1209`), while `OBL-UNGATED-FORMS` and `IMPL-ASSUMPTION-FORMS` are separately classified modeled (`:2258`, `:2332`). Scope the release note accurately rather than silently broadening those registrations.

Source inspection found no additional direct translator bypass for narrow signed/unsigned mixes or shifts: refusal precedes promotion. Casts and direct increments are untranslatable; width-changing body bindings are refused. Negative and hexadecimal typed `INT:` literals receive the marker, and ordinary simplification preserves marked long literals. Cache format 1.22 and the recursive hash marker appear correctly implemented; the missing issue is regression coverage.

No dotnet commands were run.

## Response

| Finding | Disposition |
|---|---|
| BLOCKING simplification-based sensitivity demotes shapes that cannot overflow | Fixed. `Z3Verifier.CanFailForSomeInput` asks the solver whether some value of the operand types violates the checked-arithmetic conditions. The query has no preconditions, and an unknown answer counts as sensitive. `i32 + LONG:1`, `u32 - int`, and guarded arithmetic that cannot overflow when it is evaluated stay `Proven`. Regressions: `Control_ArithmeticThatCannotOverflow_StaysProven` (2 rows, both failed before). As a consequence, the #1135 differential forms (guarded multiplications) and `ProductionOverflowRuntimeTests` (guarded arithmetic) keep their original `Proven`. The gate, the registry, the committed reports, and that test are byte-identical to main. |
| MAJOR D2 depends only on lexer provenance | Fixed. A signed literal declared 32-bit whose value lies outside int32 is refused too, whether or not the lexer marked it. `LONG:`/`UINT:`/`ULONG:` and 64-bit nodes stay modeled. Regression: `OversizeLiteralBuiltWithoutTheLexer_IsRefused` (your `IsLong = false` witness, which failed before). |
| MAJOR differential gate accepts missing assumptions | Resolved by the fix above. The gate is restored to main: exact assumption equality, and no per-channel relaxation. |
| MAJOR shared helpers weaken expectations | Fixed. `NumericExecutableSemanticsTests` now uses an independent oracle, C# itself. A checked `+`/`-`/`*`/negation that throws `OverflowException` for some boundary values of its types must be exactly `Assumed` with the checked-arithmetic assumption. Everything else must be exactly `Proven`. That includes comparisons, bitwise operators, shifts, and division (D8, which is not checked-arithmetic). `OverflowSoundnessBenchmark` splits the helpers: the six bounded shapes require exact `Assumed` (checked-arithmetic). `Unsigned_AlwaysNonNegative` and the comparison entry in `RunFullBenchmark` require `Proven`. |
| MAJOR k-induction truncates oversized bounds | Fixed. A loop bound outside int32 makes k-induction `Unsupported` (`GetIntValue`). No dedicated regression: k-induction has no Calor-source entry point in these tests. This is outside the registered postcondition row. |
| MINOR imprecise new tests; no cache coverage | Fixed. The checked-arithmetic theory asserts exact `Assumed` with exactly `[checked-arithmetic]` on all three rows. A new test asserts that an inferred-width literal has its own cache key and that the format is ≥ 1.22. |
| MINOR reports misdescribe the gate | Resolved: the reports and the gate are unchanged from main. |
| MINOR CHANGELOG overstates channels | Fixed. The entry scopes the demotion to postconditions. It states that arithmetic that cannot overflow is still `Proven`, and that obligations, preconditions, and interface checks are unchanged (the registered row covers postconditions only). |
| Test counts | `S2NumericRefusalTests` has 15 cases. 11 failed before R-NUM; 3 of the 4 round-1 additions failed before round 1, and the fourth is the cache-key and format check. R-NUM changes 15 earlier tests: `NumericExecutableSemanticsTests` 2, `OverflowSoundnessBenchmark` 7, `W1Slice1SoundnessTests` 3, `VerifierTests` 3. Full Verification 426 and Compiler 12602 pass. Non-test lines: 109. |
