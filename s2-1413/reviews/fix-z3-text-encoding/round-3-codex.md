# S2 #1413 fix-z3-text-encoding — Codex review round 3

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review only; I did not run dotnet. I reviewed HEAD `be1a34b1` against the stacked base `f108e8e4`, without reopening #1495’s separate review.

Earlier findings were checked against the current code:

| Earlier finding | Current disposition |
|---|---|
| Implication string proofs bypass demotion | **Resolved by stacking.** Both entry points call `Solve` at `Z3ImplicationProver.cs:160,431`; string demotion occurs at lines 237–255. |
| Indexed `IndexOf` differs from emitted code | **Resolved by refusal** at `ContractTranslator.cs:1834`. Range collection excludes it at line 1391. |
| Local-dependent ranges are omitted; conditional refusal bypassed | **Original witnesses resolved.** Substitution occurs at `Z3Verifier.cs:1036,1064`, and untranslatable ranges fail at line 1212. The replacement has new defects below. |
| Caller variables and quantifier binders capture `$length` | **Resolved in production code.** The common factory rejects `$` at `ContractTranslator.cs:1115`; quantifier paths call it at lines 940,985,1015. The regression test is ineffective; see finding 5. |
| Null-satisfiable preconditions reported unsatisfiable/vacuous | **Reported witness resolved** at `Z3Verifier.cs:174,313`. The new detector also rejects genuinely impossible, null-independent preconditions; see finding 4. |
| Incorrect assumption evidence | **Partially resolved.** The main string reason is corrected at `Z3Verifier.cs:575`. Canonical entries still misdescribe the current model; see finding 6. |
| `SubstringFrom` test measures rendered length | **Reported escape-counting defect resolved** by decoding escapes before counting at `S2Z3TextEncodingTests.cs:124–126`. |

1. **MAJOR — The collector’s local substitution changes integer promotion and admits throwing substring counterexamples.**

   The collector stores the raw initializer at [Z3Verifier.cs:1064](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:1064). The encoder deliberately treats an int local differently, using `BindInt32Constant` at line 1302: `uint + intVariable` promotes to `long`, whereas `uint + positiveIntLiteral` stays `uint`.

   Consequently, the range constraint and encoded body describe different starts:

   ```calor
   §M{m1:M}
     §F{f1:Probe:pub} (u32:x, str:s) -> i32
       §E{}
       §Q (== x UINT:4294967295)
       §Q (== s STR:"")
       §S (== result INT:1)
       §B{i:i32} INT:1
       §R (len (substr s (? (> (+ x i) LONG:4294967295) INT:1 INT:0) INT:0))
   ```

   At runtime, `x + i` is `4294967296L`, so the start is 1 and `"".Substring(1, 0)` throws. The collector substitutes literal `1`; its addition wraps to unsigned zero, so its start is 0 and its range condition permits the empty string. The encoded body uses start 1, but Z3 totalizes that substring to empty, permitting a fabricated `Refuted` result with `result = 0`.

   Share the encoder’s binding representation with the collector, including constant-expression distinctions. Add this mixed-type range witness and analogous divisor coverage.

2. **MAJOR — Substitution moves eager initializers into conditional positions and breaks valid proofs.**

   Initializers are collected at their binding site, but their complete trees are also substituted into every use at [Z3Verifier.cs:1044](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:1044). Conditional arms then trigger the refusal at line 1216.

   ```calor
   §M{m1:M}
     §F{f1:Probe:pub} (str:s, bool:c) -> i32
       §E{}
       §Q (== s STR:"ab")
       §S (== result INT:1)
       §B{t:str} (substr s INT:1 INT:1)
       §R (? c (len t) (len t))
   ```

   The substring executes unconditionally, once, before the conditional expression. This head re-walks its substituted initializer inside both conditional arms and returns `Unsupported`. The same regression affects an eagerly computed division subsequently read in a branch.

   Preserve initializer evaluation sites when collecting exception conditions. Substituting a value must not introduce another apparent execution of its initializer.

3. **MAJOR — Current-format cache keys still collapse UTF-16 values that this repair now distinguishes.**

   [ContractHasher.cs:194](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs:194) appends literal UTF-16 text, then line 384 hashes it through `Encoding.UTF8.GetBytes`. That encoding replaces unpaired surrogates with U+FFFD. [The .NET implementation](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Text/UTF8Encoding.cs) explicitly sets that replacement fallback.

   Two otherwise identical functions therefore share a key when one returns `STR:"\ud800"` and the other returns `STR:"\ud801"`, even with the same postcondition:

   ```calor
   §S (== result STR:"\ud800")
   ```

   Under the repaired UTF-16 model, the first is `Assumed`; the second is `Refuted`. Cache lookup at `VerificationCache.cs:108` cannot distinguish them. Format 1.21 evicts historical entries but does not prevent collisions between new entries.

   This also affects caller-supplied identifier text: API references to different unpaired-surrogate names hash identically despite `Z3Name` distinguishing their constants.

   Hash an injective representation of UTF-16 code units, and test cache reuse across these inputs.

4. **MINOR — The null detector mistakes any free symbol for a possibly null value.**

   [ContractTranslator.cs:1277](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:1277) uses `HasFreeSymbol`, which recursively includes boolean and numeric symbols.

   For a sole `bool:c` parameter:

   ```calor
   §Q (== (? c STR:"a" STR:"b") STR:"c")
   ```

   the precondition is always false, and every possible string operand is a non-null literal. Nevertheless, the conditional’s boolean symbol sets `TouchedNullTolerantReferenceForm`, changing definitive unsatisfiability and vacuity into `Unsupported`.

   Track possible null values rather than arbitrary symbolic dependencies. This contradicts the author’s claim that literal-only forms remain unaffected.

5. **MINOR — The `$` quantifier regression passes for an unrelated reason.**

   [S2Z3TextEncodingTests.cs:181](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/tests/Calor.Verification.Tests/S2Z3TextEncodingTests.cs:181) renames the binder to `a$length`, but line 182 reuses the body containing `q`.

   Removing the new factory reservation would still leave `q` undeclared, so translation would still return null and the assertion would pass. Rename the body references too, assert a valid control quantifier translates, and cover the original length-capture witness.

6. **MINOR — Assumption envelopes still omit substring bounds and describe obsolete byte semantics.**

   [Z3Verifier.cs:28](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:28) and line 37 describe only nonzero division, although they now label substring-range assumptions. Line 70 still asserts UTF-8 byte counting. The obligation reason at `ObligationSolver.cs:242` also remains byte-counted.

   Historical constants can remain stable while new, accurate entries describe substring bounds and the current string model.

The remaining audit found no missed caller-controlled Z3 symbol-creation site in `src`; `Z3Name` itself remains injective. Postcondition, obligation, and stacked implication demotion remain present. The manifest increase is correctly **21 cases**: 15 facts plus six theory rows. Pinned-binary surrogate handling was not exercised. The CHANGELOG’s “counterexample is never an input where the body throws” claim remains contradicted by finding 1.

## Response

| Finding | Disposition |
|---|---|
| 1 MAJOR local substitution changes integer promotion | Fixed by removing the substitution. The divisor/range collector no longer substitutes bindings. A range or divisor whose operands read a body local cannot be translated, so the collector reports a failure and the result is `Unsupported`, not a proof and not a refutation. Divisors over locals are back to their pre-PR behavior. Regression: `MixedWidthLocalInASubstringStart_IsNotRefuted` (the reviewer's `u32 + i` witness). `SubstringOverALocal_IsUnsupported` replaces the earlier "stays Assumed" expectation. |
| 2 MAJOR substitution re-evaluates eager initializers in conditional arms | Fixed by the same removal. An initializer is collected once, at its binding site, with the binding's own conditionality. Regression: `EagerSubstringInitializerReadInBothArms_StaysAssumed` (the reviewer's `(? c (len t) (len t))` witness). |
| 3 MAJOR cache keys collapse unpaired surrogates (UTF-8 replacement) | Resolved by #1494 (R-CACHE, approved after its verification pass). In that PR, `ContractHasher` hashes its input through `LosslessBytes` (the UTF-16 code units), and the key scope is hashed losslessly. Both the literal case and caller-supplied identifier text are covered. The disposition record requires #1494 to merge before #1497. It is not duplicated here, because the two PRs would conflict in `ContractHasher.cs` and the cache format number. |
| 4 MINOR any free symbol counts as possibly null | Fixed. Only a free term of a string, array, or user-type sort (an uninterpreted constant, an uninterpreted application, or a bound variable) counts as possibly null. Boolean and numeric symbols do not. Regression: `LiteralOnlyStringsUnderABooleanSymbol_StayVacuous` (the reviewer's `(? c "a" "b") == "c"` witness: still vacuous, not `Unsupported`). |
| 5 MINOR the `$` regression passes for an unrelated reason | Fixed. The witness now renames both the binder and the body reference to `a$length`. A control asserts that the original quantifier translates. |
| 6 MINOR assumption envelopes | Partly addressed. The obligation solver's string reason no longer says "byte-counted", and the verifier's reason was corrected in round 2. The canonical assumption strings are kept, for two reasons. They are pinned verbatim by the hash-pinned `docs/verification-modeled-forms.md` (the F-4 differential), and the G3 differential oracle's allowed-assumption sets key on them. Adding or rewording entries is therefore a contract change outside this repair. The disposition record lists it as a residual. |
| CHANGELOG | The substring sentence now reads "not an input where that substring throws". It also says that a conditional position or a range over a local gives `Unsupported`. |
| Stack | #1495's round-3 commit was merged into this branch, and the manifest conflict was resolved with the script. The diff against #1495 is 259 non-test lines. Calor.Verification.Tests 441 and Calor.Compiler.Tests 12605 pass. 4 of the new or changed tests failed before this round. |
