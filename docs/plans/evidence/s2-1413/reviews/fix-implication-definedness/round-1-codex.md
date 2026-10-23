# S2 #1413 fix-implication-definedness — Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review only. I did not run `dotnet`; the witnesses below are derived from the code.

1. **BLOCKING — A definedness refusal can become a false `Calor0814` claim.**

   [`Z3ImplicationProver.cs:180`](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:180) returns `Unsupported` for a conditional-position divisor **before checking satisfiability**. `ContractInheritanceChecker.cs:1626` silently falls back to heuristics. Those heuristics incorrectly treat `!=` as weaker than `==` (`:2142–2151`).

   Concrete preconditions:

   ```text
   Interface:   (== (|| (== x INT:0) (== (% x y) INT:0)) BOOL:true)
   Implementer: (!= (|| (== x INT:0) (== (% x y) INT:0)) BOOL:true)
   ```

   At `x = 0, y = 1`, the interface accepts and the implementer rejects. Both expressions complete normally.

   The new collector refuses the consequent’s conditional modulo. Structural comparison recognizes the matching operands, the erroneous operator heuristic accepts the weakening, and `_implicationUnestablished` remains false. With no postconditions, the checker emits `Calor0814` at `:495–506`. Before this patch, Z3 would find the counterexample and emit `Calor0810`.

   **Required:** Make definedness refusals visible and prevent heuristic fallback from establishing validity. Add this checker-level regression; the current prover-only `Unsupported` test misses the laundering.

2. **MAJOR — `AddInheritedConflictViolation` silently discards `Assumed`, hiding previously diagnosed incompatibility.**

   [`ContractInheritanceChecker.cs:1469`](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1469) asks whether combined inherited postconditions imply `false`, then returns silently for every result except `Proven` (`:1474`).

   Take two interfaces with no preconditions, identical signatures containing an unused `str:s` parameter, and guarantees `result == 0` and `result == 1`. Their guarantees are plainly incompatible. Declaring `s` touches string theory (`ContractTranslator.cs:1135`), so the new prover demotes the contradiction proof to `Assumed`.

   An implementer without explicit contracts then receives `Inherited` and the inheritance informational diagnostic (`ContractInheritanceChecker.cs:418–444`), without `Calor0818`, `Calor0819`, or `Unestablished`. Compilation checks diagnostic errors, so this checker regression allows compilation to continue (`Program.cs:902–918`).

   Also, `_implicationUnestablished` is reset **after** the conflict check (`:447`); merely setting it inside that check would not fix propagation.

   **Required:** Report conflict-check demotions and carry their status through both early-return and explicit-contract paths. Prefer preserving proofs of contradictions independent of unused reference parameters.

3. **MAJOR — The weakening CLI ignores module overflow policy and loses valid unchecked proofs.**

   [`VerifyCommand.cs:835`](src/Calor.Compiler/Commands/VerifyCommand.cs:835) passes expressions and parameters to `RunWeakeningProofs`, but neither module’s overflow policy. The prover created at `:865` therefore always uses `CheckIntegerOverflow = true`.

   Compare identical `overflow=unchecked` files with:

   ```text
   §S (!= (* result INT:2) INT:1)
   ```

   This guarantee is total and always true under wrapping arithmetic. Both postcondition directions are now demoted for possible **checked** overflow, producing `indeterminate: true` instead of the valid intact verdict (`:899–904`).

   The inheritance checker’s plumbing is correct: it uses `module.ShouldCheckIntegerOverflow()` (`ContractInheritanceChecker.cs:74–75`), matching the emitter (`CSharpEmitter.cs:1439`) and checked module default (`ModuleNode.cs:45–49`). For otherwise identical modeled expressions, the added checked safety gate only removes proofs; it cannot introduce a checked proof that unchecked semantics refute. The CLI’s default is conservative, but it breaks valid unchecked proofs.

   **Required:** Carry both policies into the comparison. Handle policy differences explicitly; one shared flag cannot express two execution policies.

4. **MAJOR — Throwing antecedents remain unsound as SAT counterexamples. This is a retained, pre-existing defect.**

   [`Z3ImplicationProver.cs:208`](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:208) returns a refutation directly for SAT, without checking antecedent definedness.

   Under checked arithmetic:

   ```text
   A: (< (+ x INT:1) INT:0)
   C: (< x INT:0)
   ```

   Every input where `A` evaluates successfully to true satisfies `C`. The solver nevertheless refutes `A → C` at `x = int.MaxValue`: wrapping makes `A` true, whereas runtime evaluation throws.

   This produces a false `Calor0810` in the precondition direction, or false `Calor0811` with `A` as the implementer guarantee. It also affects weakening verdicts: frozen `Q = C`, final `Q = A` is a real checked precondition strengthening—it excludes `x = -1`. Both total-model implication directions are SAT, so the CLI reports determinate “incomparable, not weakened” (`VerifyCommand.cs:881–915`).

   The comment at `:168–169` is defensible **for UNSAT proofs**: totalizing exceptional antecedent evaluations overapproximates successfully satisfied antecedents. That argument also supports an UNSAT `combined → false` conflict proof. It does not validate SAT witnesses.

   **Required:** Validate refutation witnesses against antecedent definedness, or visibly return a non-establishing result when the counterexample relies on exceptional evaluation.

5. **MINOR — The changelog overstates coverage.**

   [`CHANGELOG.md:51`](CHANGELOG.md:51) says possibly-zero-divisor checks report `Calor0819` and no longer report `Calor0814`. Conditional divisors instead return `Unsupported`, whose checker fallback can still emit `Calor0814`—finding 1.

   “Checks over plain integers are unchanged” also contradicts the intentional checked integer multiplication demotion. Narrow this to total integer comparisons and describe refusal outcomes separately.

6. **MINOR — The tests discriminate the intended repairs, but miss the consumer regressions above.**

   The tests are not tautological: they check production diagnostics or prover outcomes and include independent C# witnesses. The new tests require Z3 without skipping. Static enumeration agrees with the manifest: **+7 compiler cases** and **+4 verification cases**.

   The supplied eight pre-fix failures are consistent with seven new negative cases plus the changed existing string test; the four new controls should remain passing. I did not independently verify that run.

   Missing coverage includes checker handling of `Unsupported`, inherited-conflict `Assumed`, direct `Unestablished` status assertions, CLI overflow policy, and exceptional antecedent refutations.

The ordinary pre/post `Assumed` branches do visibly warn and suppress `Calor0814`. Skipping heuristics there is acceptable under the stated evidence contract: these outcomes replace old UNSAT “proofs,” not old SAT violations. The hidden-error regressions are the refusal fallback and conflict consumer above.

I found no additional direct false-`Proven` route in the inspected shift, cast, quantifier, or `result` handling: shifts mask counts; casts are untranslated; risky quantified divisors are refused; checked quantifier arithmetic requires universal safety; checker and CLI postcondition paths preserve output typing. The `QualifyPostcondition(..., true)` simplification is also justified: an absent or literal-true guarantee imposes no postcondition obligation on interface-admitted executions.

## Response

The fix changed shape in response: the prover now decides `A ∧ D(A) ∧ ¬(D(C) ∧ C)`, where D is a contract's definedness (divisor side conditions and, in a checked module, overflow safety), instead of demoting whenever D(C) is not entailed.

| Finding | Disposition |
|---|---|
| 1 BLOCKING conditional divisor -> `Unsupported` -> heuristic -> false `Calor0814` | Fixed twice over. (a) The prover no longer returns `Unsupported` before solving: an UNSAT verdict with unmodeled D(C) is `Assumed` (visible `Calor0819`). A SAT verdict whose model may rely on a throwing contract is `Unsupported` with a "no counterexample established" reason. (b) The checker never reports `Calor0814` when the syntactic heuristic accepted after the solver declined (`Unestablished`). Regression: `ConditionalDivisor_NoProofAndNoHeuristicValidityClaim` (the reviewer's witness). |
| 2 MAJOR inherited-conflict `Assumed` silently dropped | Fixed: `AddInheritedConflictViolation` reports `Calor0818` for an `Assumed` contradiction, as it did before (an error claims no proof). The demotion came from the unused string parameter. The prover now declares only the parameters the contracts read, so unused reference-typed parameters no longer demote integer-only implications. Regressions: `IncompatibleInheritedGuarantees_StillReportedWithAStringParameter` and `UnusedStringParameter_DoesNotDemoteAnIntegerImplication`. |
| 3 MAJOR weakening CLI ignores overflow policy | Fixed: `RunWeakeningProofs` takes the modules' policy. If the frozen and final files use different policies, the contracts are reported incomparable (indeterminate). Not covered by a new automated test (the CLI path writes JSON to stdout). |
| 4 MAJOR (pre-existing) SAT refutation from a throwing antecedent | Fixed by the exact query: the antecedent's definedness is asserted, so the model is an input where A completes true. When D(A) cannot be modeled, a SAT result is `Unsupported`, not a refutation. Regression: `ThrowingInterfacePrecondition_DoesNotYieldAFalseRefutation` (the reviewer's witness, now proven). |
| 5 MINOR CHANGELOG overstated | Rewritten for the new behavior. A throwing contract on an accepted input is now an LSP error with that input; reference-sort checks give `Calor0819`; heuristic acceptance gives no `Calor0814`; the CLI policy rule is stated. |
| 6 MINOR coverage gaps | Added the four checker-level regressions above. The modulo and checked-overflow witnesses now assert the refutation at the throwing input (`y=0`). The prover tests assert `Disproven` (`result=0`) for the throwing interface guarantee and `Assumed` for the conditional divisor. |

Consequence for the disposition record: IMPL-DIVISION-TOTALIZED-001 (F-B1/N1-030) is now a **fix**: the false proof becomes a genuine LSP error with the oracle's own witness. IMPL-ASSUMPTION-FORMS stays a **demotion** (null strings cannot be modeled).
