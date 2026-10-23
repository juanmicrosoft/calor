# S2 #1413 fix-z3-text-encoding — Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review only; I did not run dotnet. The blockers are the implication channel and the mismatch between indexed `IndexOf` verification and emitted code.

1. **BLOCKING — This head can introduce a false `Proven` through the implication prover.** Both implication entry points send UNSAT directly to `SolverVerdict`, without checking `TouchedStringTheory`: [Z3ImplicationProver.cs:158](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:158) and line 273.

   With a string parameter `s`, consider `true` implying:

   ```calor
   (&& (== (len STR:"é") INT:1)
       (== (isempty s) (== s STR:"")))
   ```

   Previously the literal-length conjunct was false in Z3. After this repair, both conjuncts are solver tautologies, so this channel returns `Proven`. At runtime, `s = null` makes the second conjunct false: `IsNullOrEmpty(null)` is true, while `null == ""` is false. The inheritance checker accepts `ImplicationStatus.Proven` as successful weakening/strengthening.

   The separate implication-demotion PR is therefore a required dependency. It is absent from this checkout; the claim that every string proof stays `Assumed` cannot be approved here.

2. **MAJOR — The new `IndexOf` range assumption describes an overload the compiler does not emit.** [ContractTranslator.cs:1373](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:1373) uses argument 2 as the start index, and line 1825 passes that start to Z3. But [CSharpEmitter.cs:8684](src/Calor.Compiler/CodeGen/CSharpEmitter.cs:8684) drops it:

   ```calor
   (indexof STR:"abcabc" STR:"b" INT:3 :ordinal)
   ```

   Z3 returns 4; emitted `"abcabc".IndexOf("b", StringComparison.Ordinal)` returns 1. With start 7, the new condition excludes the execution, although emitted code still returns 1 without an index exception. The value mismatch is pre-existing; adding this exclusion is new.

   Align emission and verification, or explicitly refuse this form pending repair. Add an end-to-end test that checks generated C# and runtime behavior.

3. **MAJOR — Range collection breaks valid proofs using immutable locals.** The body encoder substitutes bindings before translation at [Z3Verifier.cs:1241](src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:1241). The collector walks the original expressions at lines 1026 and 1042, then calls the new helper at line 1191.

   This previously provable body becomes `Unsupported`:

   ```calor
   §Q (== s STR:"ab")
   §S (== result INT:1)
   §B{i:i32} INT:1
   §R (len (substr s i INT:1))
   ```

   The encoder resolves `i` through its substitution environment. The range helper translates the original name `i`, which was never declared in the translator, and returns null. Local receivers and counts have the same problem.

   Collect constraints with a flow-sensitive binding environment, while preserving evaluation at initializer sites. Test local receiver, start, and count cases.

4. **MAJOR — Synthetic array lengths still collide with accepted API variable names.** This is pre-existing, but it defeats the requested all-input namespace audit. [ContractTranslator.cs:1180](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:1180) constructs `a$length` and stores it in the ordinary variable dictionary. Thus:

   ```csharp
   translator.DeclareVariable("a", "i32[]");
   translator.DeclareVariable("a$length", "u32");
   ```

   creates the user variable with exactly the synthetic length’s name and BV32 sort. `Z3Name` preserves that equality; `TranslateArrayLength` retrieves it at line 2047. Main-channel reference demotion prevents guard deletion, but it does not make these independent values independent.

   Reserve/reject synthetic names or give internal symbols a separate namespace. This finding concerns API/AST inputs; I am not claiming `$` is an ordinary source identifier.

5. **MINOR — String ranges are reported as division assumptions.** A proof involving only `Substring` can acquire `ExceptionalPathDivisionAssumption` and the reason “the body divides” at [Z3Verifier.cs:549](src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:549). Contract ranges similarly acquire `ContractExpressionDivisionAssumption`. Give range conditions accurate evidence labels. The string assumption/reason also still describes literals as byte-counted.

The remaining audit results:

- **`Z3Name` itself is injective** over UTF-16 strings, including literal `~`, surrogates, controls, `$`, `|`, and spaces. The reserved escape marker and fixed four-digit payload make decoding unambiguous. Every source-controlled symbol creation site found in `src` uses it. The factory’s fixed `__z3_test__` constant is harmless; quantifiers use the encoded variable helper, and `IsolatedSolver.Translate` copies ASTs.
- **Substring bounds are mathematically correct:** nonnegative start/count plus an unbounded-integer sum bounded by length handles zero counts and the end boundary correctly. BV conversion respects tracked signedness. The problems are binding resolution and `IndexOf` emission.
- **Surrogates are not inherently rejected by Z3’s escape parser.** Upstream code checks the numeric upper bound, accepts surrogate values independently, and defaults to Unicode encoding. This supports the proposed representation; I could not verify the pinned 4.15.7 binary. Under ASCII encoding, oversized escapes remain literal text rather than throwing. [Escape parser](https://raw.githubusercontent.com/Z3Prover/z3/master/src/util/zstring.cpp), [character bounds](https://raw.githubusercontent.com/Z3Prover/z3/master/src/util/zstring.h).
- **Postcondition and obligation demotion remain intact.** Cache serialization preserves `Assumed` and its assumptions; exact format matching rejects old entries. Format 1.21 invalidates old results despite unchanged `SemanticsVersion`. The cache note should also mention changed range verdicts, including ASCII cases.
- **The manifest’s +14 is correct:** eight facts plus six theory rows. The two modified tests still explicitly pin demotion. However, the new suite lacks `SubstringFrom`, indexed `IndexOf`, local-binding, and range-boundary coverage. I cannot independently confirm the reported pre-fix failure count.
- **CHANGELOG lines 30–35 overclaim:** emitted indexed `IndexOf` does not have the asserted throwing behavior, and implication proofs at this head do not stay `Assumed`.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING implication channel at this head has no string demotion | Merge-order dependency: this PR **must merge after #1495**. #1495 demotes every implication whose query touches the string sort to `Assumed`, and it is the S2 repair for the implication channel. The dependency is stated in the PR body and the disposition record. No code is duplicated here, to avoid conflicting copies of the prover. |
| 2 MAJOR `IndexOf` with a start index: the emitter drops the start | Fixed by refusal: `TranslateStringIndexOf` refuses the 3-argument form (Unsupported), and the range condition no longer covers `IndexOf`. The two existing tests that asserted the offset form was modeled (`VerifierTests.ProvesIndexOfWithOffsetSkipsEarlierMatches`, `TranslatorTests.TranslatesStringIndexOfWithStartOffset`) now pin the refusal and the emitted value (`"abcabc".IndexOf("b", Ordinal) == 1`). Regression: `IndexOfWithStart_IsUnsupported`. |
| 3 MAJOR ranges over body locals made valid proofs Unsupported | Fixed: when a range cannot be translated at the collector (a body-local name the encoder substitutes later), no side condition is added. That only weakens a refutation's guarantee, never a proof. Regression: `SubstringOverALocal_StaysAssumed`. |
| 4 MAJOR (pre-existing, API only) `a$length` collision | Fixed: `DeclareVariable`/`DeclareArrayVariable` refuse names containing `$`, which is reserved for synthetic solver variables. Regression: `DollarNames_AreReservedForSyntheticVariables`. |
| 5 MINOR range conditions labeled as division | The demotion reasons now say "divides or takes a substring". The assumption constants are unchanged because they are referenced by committed evidence. String proofs carry the string-model assumption in any case. |
| Coverage | Added `SubstringFromCounterexample_IsOneWhereTheBodyReturns`. The CHANGELOG no longer claims `IndexOf` ranges and states the refusal. |
