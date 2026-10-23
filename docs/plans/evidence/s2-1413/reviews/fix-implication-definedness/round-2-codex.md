# S2 #1413 fix-implication-definedness — Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review only. I did not run `dotnet`; the witnesses below follow from the current code.

The round 1 findings are resolved as follows:

1. **Conditional-divisor laundering: partially resolved.** Heuristic acceptance now sets `Unestablished` and suppresses `Calor0814` when a prover exists ([ContractInheritanceChecker.cs:1647](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1647), status selection at `:495`). However, `Unsupported` still falls back silently (`:1630`). The original witness therefore loses its previously diagnosed violation without receiving a visible refusal. See finding 5.

2. **Unused-string inherited conflict: the specific witness is fixed.** Only referenced parameters are declared ([Z3ImplicationProver.cs:113](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:113)); the unused string no longer demotes the integer contradiction. The alternative repair accepts `Assumed` contradictions as definitive incompatibility errors ([ContractInheritanceChecker.cs:1477](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1477)), introducing finding 3.

3. **CLI overflow plumbing: resolved for comparisons reaching the prover.** The CLI compares both module policies and passes their shared policy into the prover ([VerifyCommand.cs:838](src/Calor.Compiler/Commands/VerifyCommand.cs:838), assignment at `:876`). The checker also uses the module policy (`ContractInheritanceChecker.cs:75`), matching the emitter (`CSharpEmitter.cs:1439`).

4. **Checked throwing antecedent: the specific witness is fixed.** Antecedent definedness is asserted before solving ([Z3ImplicationProver.cs:193](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:193)). This correctly excludes `int.MaxValue` from the cited checked-addition antecedent. The broader repair is incomplete: quantified safety is insufficient for this use, and throwing string antecedents still yield false refutations.

5. **Changelog: rewritten, but still overstates coverage.** [CHANGELOG.md:52](CHANGELOG.md:52) promises throwing-contract errors and reference-sort warnings more generally than the implementation provides.

6. **Tests: coverage improved, but incomplete.** The new checker regressions start at [S2ImplicationDefinednessTests.cs:173](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs:173). Static enumeration agrees with the current manifest: **+11 compiler cases and +4 verification cases** (`eng/test-manifest.json:8`, `:106`). The conditional-divisor test asserts only absence of proof/validity diagnostics (`:194`), so it misses the remaining silent acceptance.

New and remaining findings:

1. **BLOCKING — Quantified “sufficient safety” is used as exact definedness, producing false proofs.**

   [ContractTranslator.cs:837](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:837) explicitly describes the arithmetic predicate as **sufficient** safety. For existential quantifiers it requires safety at **every** bound value (`:908–943`). The new prover nevertheless asserts that predicate as `D(A)` ([Z3ImplicationProver.cs:198](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:198)).

   Under checked arithmetic:

   ```text
   A: (exists ((i i32))
        (&& (>= i INT:0)
            (&& (< i INT:2) (>= (+ x i) INT:0))))
   C: (< x INT:2147483647)
   ```

   At `x = int.MaxValue`, runtime evaluation of `A` completes **true at i = 0**. The generated quantifier uses ascending enumeration and `.Any`, which stops there ([CSharpEmitter.cs:8582](src/Calor.Compiler/CodeGen/CSharpEmitter.cs:8582); enumeration in `ContractViolationException.cs:213`). `C` is false.

   But the universal safety predicate considers `i = 1`, where `x + i` overflows, and excludes `x = int.MaxValue`. Every remaining `i32` value satisfies `C`. The query is therefore UNSAT and reports **Proven**, leading to false `Calor0815` and, with no postconditions, `Calor0814`.

   The same approximation also creates false refutations: use `A = (== x INT:2147483647)` and the existential expression as `C`. The consequent completes true, but its sufficient safety predicate is false, so the prover reports `Disproven`.

   This affects inheritance, weakening verdicts, and inherited-conflict checks. For example, inherited guarantees consisting of that existential over `result` and `result == int.MaxValue` are compatible, but their asserted universal safety makes the conjunction unsatisfiable.

   **Required:** Distinguish exact definedness from sufficient safety. Model quantifier termination accurately, or visibly refuse these cases. A sufficient predicate cannot safely restrict the antecedent domain or establish that the consequent throws.

2. **MAJOR — Qualified postconditions now reject identical valid contracts at inputs the interface never accepts.**

   The checker wraps an interface guarantee as `Q_interface → S_interface` ([ContractInheritanceChecker.cs:477](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:477), construction at `:1504`). The new query requires this entire wrapper to complete.

   Give both interface and implementer identical contracts:

   ```text
   §Q (< (+ x INT:1) INT:0)
   §S (== result INT:0)
   ```

   Let the method return `0`. This satisfies the interface on every input its checked precondition accepts.

   The postcondition query nevertheless admits `x = int.MaxValue, result = 0`: the implementer guarantee completes true, while evaluating the qualified interface expression throws in its **precondition**. Its definedness predicate is false, causing a `Disproven` result and `Calor0811`.

   That input never passes the interface precondition and creates no interface postcondition obligation. The `postcondition == true` shortcut fixes only the absent-guarantee case.

   **Required:** Restrict the postcondition obligation to inputs where the interface precondition completes true. Do not turn precondition failure outside that domain into a required postcondition’s throwing counterexample. Add an identical-contract checker regression.

3. **MAJOR — An `Assumed` contradiction is promoted into a false incompatibility error.**

   [ContractInheritanceChecker.cs:1477](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1477) accepts `ProofStatus.Assumed`, then reports that the inherited guarantees are mutually incompatible (`:1485–1491`).

   Two string-result guarantees demonstrate the problem:

   ```text
   IA: (isempty result)
   IB: (!= result STR:"")
   ```

   Both complete true for `result = null`. They are compatible in the nullable runtime domain this repair explicitly addresses.

   Z3 cannot represent that result, so their conjunction is unsatisfiable in its string model. The prover correctly returns **Assumed**, but the checker converts this conditional contradiction into unconditional `Calor0818`, without exposing the assumption.

   “An error claims no proof” does not justify asserting a false semantic incompatibility. The error also stops compilation ([Program.cs:915](src/Calor.Compiler/Program.cs:915)).

   **Required:** Preserve `Assumed` as a visible non-establishing conflict outcome and propagate its status through both checker paths. Keep the unused-string integer contradiction regression as a genuine error control.

4. **MAJOR — Throwing string antecedents still produce false SAT counterexamples.**

   `Definedness` checks divisors and arithmetic overflow only ([Z3ImplicationProver.cs:272](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:272)). String/reference model flags are examined **after** the SAT return (`:213–239`).

   Concrete implication:

   ```text
   A: (== (substr s INT:-1 INT:1) STR:"")
   C: BOOL:false
   ```

   Runtime `Substring(-1, 1)` never completes successfully. Therefore no runtime input satisfies the antecedent.

   The translator instead uses total `MkExtract` ([ContractTranslator.cs:1760](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:1760), return at `:1779`), whose negative-start extraction is empty. Both definedness predicates are non-null, so SAT becomes `Disproven`. The emitter actually calls `.Substring` (`CSharpEmitter.cs:8690`).

   The same SAT bypass permits false refutations from string encoding differences: `s == "é"` implies `len(s) == 1` at runtime, while the committed UTF-8-byte literal encoding gives the solver length 2 (`ContractTranslator.cs:1303`).

   **Required:** Validate reference/string SAT witnesses against executable semantics, or visibly return a non-establishing outcome. The round 1 arithmetic witness is repaired; antecedent partiality generally is not.

5. **MAJOR — Refusals remain silent, and solver absence still permits false `Calor0814`.**

   The original conditional-divisor witness now returns `Unsupported`. The checker silently falls back ([ContractInheritanceChecker.cs:1630](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1630)), and the unchanged heuristic still treats `!=` as weaker than `==` (`:2163`). Setting an internal `Unestablished` status suppresses `Calor0814`, but does not tell the compiler user that this precondition check was refused. Compilation proceeds because there is no error (`Program.cs:915`).

   Additionally, the safeguard explicitly requires `_z3Prover != null` (`ContractInheritanceChecker.cs:1647`, postcondition equivalent at `:1728`). With Z3 unavailable or disabled:

   ```text
   Interface:   (== x INT:0)
   Implementer: (!= x INT:0)
   ```

   The heuristic accepts this false weakening, and the checker still emits `Calor0814`.

   **Required:** Report refusal reasons visibly and prevent heuristic acceptance from establishing validity regardless of solver availability. Fix the erroneous equality/inequality heuristic.

6. **MINOR — Tests and documentation still miss the changed behavior.**

   The conditional-divisor regression ([S2ImplicationDefinednessTests.cs:185](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs:185)) needs a visible diagnostic and direct status assertion. Add controls for quantified early termination, identical contracts with partial preconditions, compatible null-only inherited guarantees, and CLI overflow policies.

   The manifest’s totals are consistent, but its verification note still describes the throwing guarantee as `Assumed` and conditional divisor as `Unsupported`; the tests now expect `Disproven` and `Assumed`, respectively (`eng/test-manifest.json:108`).

   The changelog’s blanket throwing-contract/error and reference-sort/warning claims remain inaccurate. Also, the property comment saying checked mode “can only demote a proof” ([Z3ImplicationProver.cs:79](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:79)) is obsolete: restricting a throwing antecedent can correctly make an implication Proven that unchecked execution refutes.

I found no additional defect in the inspected shift masking, cast refusal, or caller-supplied `result` typing. The ordinary pre/post `Assumed` branches remain visibly demoted; the problems are the new definedness approximation and the consumer paths identified above.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING quantified "sufficient safety" used as exact definedness | Fixed: D() is applied as an assumption only to quantifier-free contracts. For a quantified assumed contract it is omitted; A alone over-approximates, so UNSAT stays sound. A refutation is reported only when both D(A) and D(C) are exact (quantifier-free and modelable); otherwise the result is `Unsupported`. Regression: `QuantifiedInterfacePrecondition_DefinednessDoesNotRestrictTheAssumption` (the reviewer's exists witness: no Calor0815, no Calor0814). |
| 2 MAJOR interface precondition failure read as a broken guarantee | Fixed: `CheckPostconditionStrengthening` takes the interface precondition separately and conjoins it into the assumption, so its throws exclude inputs instead of breaking the guarantee. The checker passes the unqualified guarantee plus the precondition; the heuristic still receives the qualified form. Regression: `IdenticalContracts_WithAPartialInterfacePrecondition_AreNotAViolation`. |
| 3 MAJOR Assumed contradiction promoted to a false Calor0818 | Fixed: an `Assumed` contradiction is now the visible warning `Calor0819` ("contradictory only under an assumption … not reported as incompatible"), not an error. Only a `Proven` contradiction is `Calor0818`. The unused-string integer contradiction stays an error (unused parameters are not declared). Regression: `NullCompatibleInheritedGuarantees_AreNotReportedIncompatible` (the reviewer's witness). |
| 4 MAJOR throwing string antecedents give false refutations | Partly by design across the S2 PRs. The Substring-range definedness and the UTF-16 literal encoding come from #1497 (R-TEXT). With #1497 merged, `substr s -1 1` has a range side condition in the collector this prover uses, and `"é"` has length 1, so neither witness is refuted. #1497 is stated to merge after this PR. A null cannot appear in a solver model, so remaining string refutations name real non-null inputs. Not duplicated here. |
| 5 MAJOR silent refusals; heuristic validity without the solver; `!=`/`==` heuristic | Fixed. A solver `Unsupported` now produces a visible `Calor0816` ("Could not establish … : reason"). Heuristic acceptance never yields `Calor0814`, with or without Z3. The heuristic no longer treats `!=` as weaker than `==` (or the reverse). Regressions: the conditional-divisor test asserts the warning; `WithoutTheSolver_HeuristicNeverClaimsValidityAndInequalityIsNotWeaker`. |
| 6 MINOR tests/docs | Added the regressions above (5 of the 15 tests failed before this round). A zero-delta manifest note corrects the prover-test descriptions. The `CheckIntegerOverflow` comment is rewritten (no "can only demote" claim). The CHANGELOG now scopes definedness to quantifier-free contracts and describes the visible outcomes. |
