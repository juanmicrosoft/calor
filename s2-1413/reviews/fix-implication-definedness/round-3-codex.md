# S2 #1413 fix-implication-definedness — Codex review round 3

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review only. I did not run `dotnet`. The counterexamples and regressions below follow from the current code.

Earlier findings verified:

1. **Conditional-divisor laundering and solver-free validity: resolved.** `Unsupported` now visibly reports `Calor0816`, and heuristic acceptance sets `Unestablished` regardless of solver availability ([ContractInheritanceChecker.cs:1636](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1636), [1657](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1657)). The erroneous equality/inequality operator rules are removed ([2176](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L2176)). The regression now requires the warning ([S2ImplicationDefinednessTests.cs:198](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs#L198)).

2. **Unused-string integer conflict: resolved. Assumed conflict handling: partially resolved.** Only referenced parameters are declared ([Z3ImplicationProver.cs:113](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L113)). An `Assumed` contradiction now warns instead of producing the false incompatibility error ([ContractInheritanceChecker.cs:1476](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1476)). However, refusal handling and status propagation remain incomplete; see finding 3.

3. **Overflow plumbing: resolved for comparisons reaching the prover.** The checker supplies the module policy ([ContractInheritanceChecker.cs:75](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L75)); the CLI compares both policies and supplies their shared value ([VerifyCommand.cs:838](src/Calor.Compiler/Commands/VerifyCommand.cs#L838), [878](src/Calor.Compiler/Commands/VerifyCommand.cs#L878)). This matches the checked default and emitter policy ([ModuleNode.cs:45](src/Calor.Compiler/Ast/ModuleNode.cs#L45), [CSharpEmitter.cs:1439](src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L1439)). The property comment is corrected ([Z3ImplicationProver.cs:75](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L75)). Checked and unchecked implications can legitimately differ because throwing antecedents exclude inputs.

4. **Checked arithmetic antecedent and quantified safety: the cited false proofs/refutations are repaired at the prover boundary.** Exact antecedent definedness is asserted; quantified antecedent safety is omitted, and non-exact SAT witnesses are refused ([Z3ImplicationProver.cs:196](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L196), [211](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L211)). However, that refusal introduces the consumer regression in finding 2.

5. **Qualified postcondition domain: the cited identical-contract witness is repaired.** The checker passes the unqualified guarantee and the interface precondition separately ([ContractInheritanceChecker.cs:481](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L481)); the prover places the precondition in its assumption ([Z3ImplicationProver.cs:362](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L362)). `QualifyPostcondition` retains its justified literal-true shortcut ([ContractInheritanceChecker.cs:1514](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1514)).

6. **String SAT counterexamples: unresolved in this checkout.** The claimed future #1497 repairs are absent. Substring still uses total extraction, literals still use UTF-8 bytes, and SAT still returns before reference-model checks. See finding 1.

7. **Tests and documentation: partially resolved.** Static enumeration agrees with **+15 compiler cases and +4 verification cases**. The manifest corrects the prover-test descriptions ([eng/test-manifest.json:108](eng/test-manifest.json#L108)); the new test classes contain no skip paths. I cannot independently confirm the reported pre-fix runs. Missing consumer controls and remaining documentation overclaims are detailed below.

New and remaining findings:

1. **MAJOR — Reference-based SAT results still claim counterexamples without establishing executable definedness.**

   [Z3ImplicationProver.cs:211](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L211) treats quantifier-free, successfully collected arithmetic predicates as exact definedness. SAT then becomes `Disproven` at [219](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L219). String/reference flags are inspected only afterward, in the UNSAT path at [237](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L237).

   The round-2 witnesses therefore remain:

   ```text
   A: (== (substr s INT:-1 INT:1) STR:"")
   C: BOOL:false
   ```

   The translator returns total `MkExtract` ([ContractTranslator.cs:1779](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs#L1779)), while the emitter calls `.Substring` ([CSharpEmitter.cs:8690](src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L8690)). No string input successfully satisfies this antecedent, but the solver’s empty extraction permits a refutation.

   Likewise, `s == "é" → len(s) == 1` remains susceptible to a false refutation because literals still encode UTF-8 bytes ([ContractTranslator.cs:1305](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs#L1305)).

   **A further witness survives independently of #1497:** with an `i32[] arr`, use:

   ```text
   A: (== arr{INT:-1} INT:0)
   C: BOOL:false
   ```

   Array access becomes an unrestricted solver select ([ContractTranslator.cs:1093](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs#L1093)). The definedness collector merely traverses the array and index; it adds no bounds condition ([Z3Verifier.cs:1172](src/Calor.Compiler/Verification/Z3/Z3Verifier.cs#L1172)). Runtime emission indexes the actual array ([CSharpEmitter.cs:5177](src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L5177)). No runtime input successfully satisfies `A`, yet SAT claims otherwise.

   These results can produce false `Calor0810`/`Calor0811` errors and determinate CLI refutations. “The model contains no null” does not establish that its operations complete.

   **Required:** Validate reference/string SAT witnesses against executable semantics, or visibly refuse them. A later PR cannot establish correctness of the current head.

2. **MAJOR — The quantifier repair rejects identical valid contracts through heuristic fallback.**

   Give interface and implementer the **same checked precondition**:

   ```text
   (exists ((i i32))
     (&& (>= i INT:0)
         (&& (< i INT:2) (>= (+ x i) INT:0))))
   ```

   This weakening is valid: whenever the interface expression completes true, the identical implementer expression also completes true.

   The new query omits `D(A)` but retains the universally sufficient `D(C)` ([Z3ImplicationProver.cs:199](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L199)). At `x = int.MaxValue`, both runtime expressions complete true immediately at `i = 0`; the consequent safety predicate nevertheless rejects the later overflow at `i = 1`. Consequently, the query permits SAT and returns `Unsupported`.

   The checker then falls through to heuristics. Structural equality does not handle `ExistsExpressionNode` or `ForallExpressionNode ([ContractInheritanceChecker.cs:2054](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L2054)), so identical expressions reach the unconditional `Calor0810` error at [1804](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1804). Identical quantified guarantees can similarly reach `Calor0811` at [1859](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1859).

   Before this definedness query, identical integer expressions reduced to `A ∧ ¬A` and were proven. This is a fix-induced regression, not merely reduced proof coverage.

   **Required:** Preserve valid identity implications under partial evaluation, and prevent an explicitly unestablished solver outcome from becoming a claimed semantic violation through an incomplete heuristic. Add identical quantified precondition and postcondition controls.

3. **MAJOR — Inherited-conflict refusals remain silent, and conflict demotions still do not propagate status.**

   `AddInheritedConflictViolation` handles `Assumed`, but silently returns for every other non-`Proven` outcome ([ContractInheritanceChecker.cs:1484](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1484)).

   Consider two interfaces with no preconditions and these guarantees:

   ```text
   IA: (== y INT:0)
   IB: (== (/ INT:100 y) INT:-1)
   ```

   They have no jointly satisfying runtime input: `y != 0` fails IA, while `y == 0` throws in IB.

   Their combined antecedent places division on the right of `&&`. Definedness collection refuses that conditional divisor ([Z3Verifier.cs:1118](src/Calor.Compiler/Verification/Z3/Z3Verifier.cs#L1118)). The total solver model satisfies both guarantees at `y = 0`, so the prover returns `Unsupported`. The conflict consumer discards it without `Calor0816`; an implementer without explicit contracts receives `Inherited` ([ContractInheritanceChecker.cs:441](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L441)).

   The `Assumed` branch also never sets `_implicationUnestablished`. The flag remains reset **after** conflict checking ([ContractInheritanceChecker.cs:447](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L447)), so the requested status propagation was not implemented.

   **Required:** Surface conflict refusals and preserve non-establishing status through both the inherited-contract early return and explicit-contract path. Keep the unused-string integer contradiction as the genuine-error control.

4. **MINOR — Regression coverage and changelog still overstate the completed repair.**

   The quantified regression checks only absence of proof/validity diagnostics ([S2ImplicationDefinednessTests.cs:211](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs#L211)); it does not distinguish visible refusal from a heuristic hard error. The partial-precondition control checks only two diagnostic codes ([239](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs#L239)). Neither checks direct inheritance status, and the compile helper does not assert parse success.

   Add controls for findings 1–3 and automated CLI overflow-policy tests. A suitable JSON-capturing CLI harness already exists ([WeakeningCheckCliTests.cs:49](tests/Calor.Compiler.Tests/WeakeningCheckCliTests.cs#L49)).

   [CHANGELOG.md:52](CHANGELOG.md#L52) still broadly promises a throwing implementer precondition becomes `Calor0810` with its input. Conditional divisors are refused, and reference throwing behavior remains incompletely modeled. Its promise that undecided checks visibly report `Calor0816` also excludes the inherited-conflict consumer above. Scope those statements to the cases actually established by this head.

I found no additional defect in the inspected shift masking, cast refusal, or caller-supplied `result` typing. Ordinary pre/post `Assumed` outcomes remain visibly demoted and suppress `Calor0814`; the outstanding problems are SAT witness validity, refusal consumers, and preservation of valid quantified identities.

## Response

| Finding | Disposition |
|---|---|
| 1 MAJOR reference-based SAT results claim counterexamples | Fixed. A SAT result over a query that touched the string theory or an array/user-type sort is now `Unsupported`, with the reason "index bounds, substring ranges, null receivers are not modeled". No `Calor0810`/`Calor0811` and no determinate CLI refutation is built on it. Regressions: `ThrowingReferenceAntecedent_IsNotRefuted` with the reviewer's `§IDX arr -1` and `substr s -1 1` witnesses (both failed before). |
| 2 MAJOR identical quantified contracts rejected via the heuristic | Fixed. Before the solver runs, the checker decides identity by structural equality over every non-span property. Unknown values compare by `Equals`, and collections compare element by element. If A is identical to C, then A completing true means C completes true, so identity is sound under partial evaluation. Regression: `IdenticalQuantifiedContracts_AreValid` (the reviewer's exists precondition, plus an identical quantified postcondition): no `Calor0810`/`Calor0811`, and `Calor0814` is reported. Non-identical contracts that the solver declines still reach the pre-existing fail-closed heuristic. Its "Could not prove …" error claims that nothing was established, not a counterexample, so it is kept. |
| 3 MAJOR silent inherited-conflict refusals; status propagation | Fixed. An `Unknown` or `Unsupported` conflict check now reports `Calor0816` ("Could not establish whether the inherited contracts … are compatible") and sets the unestablished flag. The `Assumed` branch sets it too. The flag is reset before the conflict check, and the inherited-contract early return now yields `Unestablished` rather than `Inherited` when it is set. Regression: `UndecidedInheritedConflict_IsVisibleAndNotClaimedValid` (the reviewer's `y == 0` / `100 / y == -1` witness). The unused-string integer contradiction control (`IncompatibleInheritedGuarantees_StillReportedWithAStringParameter`) is unchanged. |
| 4 MINOR coverage and changelog | The compile helper now asserts that no lexer/parser error occurred. New `WeakeningCheck_DifferentOverflowPolicies_AreIndeterminate` exercises the CLI JSON. The CHANGELOG scopes definedness to quantifier-free integer contracts with unconditional divisors. It also states that reference/string counterexamples are not claimed, that conditional divisors, quantified contracts, and the conflict check report `Calor0816`, and that identical contracts count as compatible. |
| Test notes | 4 of the 5 new tests failed before this round. The CLI overflow test is coverage for round-2 behavior. Calor.Verification.Tests 417 pass, and the targeted inheritance/implication/weakening suites (174) pass. |
