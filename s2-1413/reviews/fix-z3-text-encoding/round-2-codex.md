# S2 #1413 fix-z3-text-encoding — Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review only; I did not run dotnet.

Each round 1 finding was checked against the current head:

| Finding | Current disposition |
|---|---|
| 1 — Implication demotion | **Unresolved.** Both entry points still return solver evidence without model demotion: `Z3ImplicationProver.cs:158,273`. A merge dependency does not resolve this checkout. |
| 2 — Indexed `IndexOf` mismatch | **Resolved by refusal** at `ContractTranslator.cs:1805`. Range collection now excludes `IndexOf` at line 1366. |
| 3 — Immutable-local proof regression | **Reported witness restored, repair incomplete.** `Z3Verifier.cs:1195` skips untranslated ranges; `S2Z3TextEncodingTests.cs:142` pins only a case whose precondition already guarantees safety. |
| 4 — Synthetic length collision | **Direct declaration witness resolved** at `ContractTranslator.cs:398,2079`. Quantified declarations bypass that restriction; see finding 3 below. |
| 5 — Incorrect assumption labels | **Partially resolved.** Reasons mention substrings at `Z3Verifier.cs:552,557`; assumption entries remain division-only at lines 28 and 37. The string reason remains byte-counted at line 562. |

1. **BLOCKING — The implication false-proof channel remains open.**

   [Z3ImplicationProver.cs:158](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:158) and line 273 still turn UNSAT directly into `Proven`.

   The original witness still applies: `true` implies

   ```calor
   (&& (== (len STR:"é") INT:1)
       (== (isempty s) (== s STR:"")))
   ```

   After this repair, Z3 proves both conjuncts. At runtime, `s = null` falsifies the second. The inheritance checker accepts `Proven` at `ContractInheritanceChecker.cs:1579,1652`.

   Integrate #1495 before approval and review the resulting head.

2. **MAJOR — Skipping local-dependent ranges restores spurious refutations and bypasses conditional refusal.**

   [Z3Verifier.cs:1195](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:1195) returns success when range translation fails, before checking conditionality. The encoder independently substitutes locals at line 1242.

   Remove the safety precondition from the new local regression:

   ```calor
   §M{m1:M}
     §F{f1:Probe:pub} (str:s) -> i32
       §E{}
       §S (== result INT:1)
       §B{i:i32} INT:1
       §R (len (substr s i INT:1))
   ```

   Every normal return produces 1. Nevertheless, the solver can choose `s = ""`, totalize the substring to `""`, and report `Refuted` with result 0. That execution throws in .NET.

   Putting the same local-dependent operation in a conditional arm also bypasses the promised `Unsupported` result. Collect ranges using the binding environment at evaluation sites; skipping them does not repair the refutation contract.

3. **MAJOR — Quantified variables bypass the new `$` reservation and capture synthetic lengths.**

   Quantifier translation calls `CreateVariableForType` directly at [ContractTranslator.cs:985](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:985) and line 1014; quantified arithmetic safety does likewise at line 940. The private factory at line 1111 has no reservation check.

   Declare array `a`, then supply an API/AST quantifier binding `a$length : u32`. Its body can express:

   ```text
   forall a$length:
       0 <= a$length < 2 => Length(a) == a$length
   ```

   `TranslateArrayLength` retrieves the bound variable from the ordinary dictionary at line 2056. The solver consequently sees `q == q`, although an independent array length cannot equal both 0 and 1.

   This remains an API/AST namespace defect; ordinary source identifier acceptance is not required. Enforce the reservation on every caller-controlled declaration path, or separate synthetic storage and symbols.

4. **MAJOR — Literal repair can now produce a false vacuity proof before string demotion.**

   [Z3Verifier.cs:301](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:301) returns `VacuousProof` before the string-model demotion at line 530.

   Consider a string parameter `s` and:

   ```calor
   §Q (|| (!= (len STR:"é") INT:1)
          (&& (isempty s) (!= s STR:"")))
   §S (== result INT:1)
   §R INT:0
   ```

   Previously, Z3’s length-2 literal made the precondition satisfiable. With this repair, the first disjunct becomes false, and the null-free string model makes the second impossible. The verifier reports `Proven(vacuous)`.

   At runtime, `s = null` satisfies the precondition and the postcondition fails. Runtime checks survive the vacuous flag, but the proof and “no valid call exists” conclusion are false. `VerifyPrecondition` also reports definitive unsatisfiability without a model-fidelity check at line 168. #1495’s implication demotion does not repair these paths.

5. **MINOR — Evidence still misstates the assumptions carrying substring and string proofs.**

   The canonical entries at [Z3Verifier.cs:28](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:28) and line 37 mention only division. The string entry at line 68 and reason at line 562 still claim byte-counted literals. Preserve historical entries if required, but add accurate evidence for the current model.

6. **MINOR — The new `SubstringFrom` test measures rendered text, not modeled string length.**

   [S2Z3TextEncodingTests.cs:124](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-z3-text-encoding/tests/Calor.Verification.Tests/S2Z3TextEncodingTests.cs:124) accepts `s.Value.Length >= 4`. One character rendered as `"\u{0}"` passes that assertion, although `.Substring(2)` throws. Decode and replay the counterexample, or inspect its actual sequence length.

The remaining audit found no missed source-controlled Z3 symbol creation site; `Z3Name` itself remains injective. Main-query postcondition and obligation demotion remain intact, and cache serialization preserves assumptions and rejects older formats. The manifest increase is correctly **18 cases**. The CHANGELOG’s “never” and conditional-refusal claims at lines 29–31 remain unsupported by finding 2. Pinned-binary surrogate handling was not exercised.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING the string-guarantee demotion depends on #1495 | Addressed by stacking. This branch is rebased onto `milestone-0.24/s2-1413-fix-implication-definedness`, and PR #1497's base is that branch, so #1497 cannot merge without #1495. Its diff is measured against that base: 269 non-test lines. |
| 2 MAJOR skipping a range over a local restores spurious refutations | Fixed. The divisor/range collector now substitutes immutable bindings in the same way as the encoder, so `substr s i 1` with a local `i` carries its range condition. A range that still cannot be modeled is again a failure, not a silent skip. Regression: `SubstringOverALocal_WithoutPrecondition_IsNotRefuted` (the reviewer's witness: no refutation at `s = ""`). |
| 3 MAJOR `$` check bypassed by quantifier binders | Fixed. The `$` refusal moved into `CreateVariableForType`, which every caller-named declaration goes through, quantifier binders included. Regression: `DollarQuantifierVariables_AreRefused` (a binder named `a$length` is refused). |
| 4 MAJOR false vacuity before the string demotion; `VerifyPrecondition` unsatisfiability | Fixed. The translator records a null-tolerant form: `==`/`!=` over a string or reference sort, `Equals`, or `IsNullOrEmpty`, with an operand that contains a free symbol. Every other string/array/user-type form throws on null, so without such a form a null cannot satisfy the precondition, and unsatisfiability carries over to runtime. With such a form, both the vacuity path and `VerifyPrecondition` return `Unsupported`, and the reason says the set is unsatisfiable only in the non-null model. Regression: `NullSatisfiablePrecondition_IsNotReportedVacuous` (the reviewer's witness). Literal-only forms are not affected, so the G3 differential oracle's refutable precondition rows stay refuted (436/436 Verification tests pass). |
| 5 MINOR evidence misstates the string assumptions | Partly fixed. The `string-model` reason text and the `StringModelAssumption` doc comment now say that literals are encoded per UTF-16 code unit since #1413, and that the null gap remains. The canonical assumption constants are content-stable, because assumption-set hashing keys on them. They are kept verbatim, and the doc comment says so. |
| 6 MINOR SubstringFrom test measures rendered text | Fixed. The test decodes Z3's `\u{h}` escapes and asserts that the decoded model has at least 2 characters. |
| CHANGELOG "never" and conditional-refusal claims | Updated. The CHANGELOG describes the null-tolerant unsatisfiability demotion. The range claim now holds, because locals are substituted and an unmodelable range is a failure. |
