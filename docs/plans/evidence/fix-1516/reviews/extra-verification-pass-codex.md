# fix-1516 (`§PROOF` reads an immutable `§B` local): extra verification pass (Codex)

- **Exception:** `EXC-1538-EXTRA-VERIFICATION` in the R0 scope packet
  (`docs/plans/evidence/v0.25-r0-1426/scope.json`, `capacity.exceptions`, merged at `ed4bc1909`).
  It allows exactly one verification-only Codex pass and no further adversarial round.
- **Maintainer confirmation:** @juanmicrosoft confirmed this exception on 2026-10-12. The packet
  still records it as `confirmed: false`. This record cites the 2026-10-12 confirmation.
- **Reviewer:** Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`).
  This was the one allowed pass. No other Codex invocation was made on this PR under the exception.
- **Scope:** the change made after the verification pass in `verification-codex.md` (which reviewed
  `05124b455`): commit `2be9b3b98`, "compare §PROOF local names without a CodeGen dependency". The
  branch was rebased onto `origin/main` at `ed4bc1909`, which made that commit `a118a5812`. Also in
  scope was any code in this PR's files that changed in the rebase. Main did not modify
  `FactCollector.cs`, `ObligationSolver.cs`, `ContractTranslator.cs`, or
  `ProofLocalObligationTests.cs`. Codex was asked to check the letters-and-digits collision key
  against `CSharpEmitter`'s sanitizer and name-emission paths as they are at the rebased HEAD.
- **Reviewed HEAD:** `a118a5812`.

## Result

**VERDICT: APPROVE.** No BLOCKING, MAJOR, MINOR, or NIT findings. The post-verification change is
**VERIFIED**. The sanitizer keeps the sequence of Unicode letters and digits, so two names that
emit the same C# identifier always share a `LettersAndDigits` key. The new check therefore refuses
at least every collision the emitter can produce. The Verification -> CodeGen dependency is gone,
and the collision tests still exercise the refusal.

## Review output (verbatim)

No new BLOCKING, MAJOR, MINOR, or NIT findings.

**Post-verification change: VERIFIED**, by static review at `a118a5812`.

- **Collision over-approximation holds.** At `src/Calor.Compiler/CodeGen/CSharpEmitter.cs:8229`, both single-identifier sanitization and qualified-name rewriting preserve the sequence of Unicode letters and digits. They perform no case folding or normalization. `_` prefixes, `@` escaping, and punctuation removal cannot change that sequence. I found no pair producing the same identifier with different `LettersAndDigits` keys.
- **No new variable-resolution mismatch.** I checked local and parameter declarations, loop variables, catch/using bindings, patterns, lambdas, quantifiers, scope handling, and generated `__` names. The replacement coarsens the previous collision check, so it can only refuse additional locals. Cross-module qualification respects declared locals; `result` substitution applies during postcondition emission.
- **The refusal list covers the relevant special cases**, at `FactCollector.cs:464`. `null`, `true`, `false`, `this`, and `base` have special expression meanings. Refusing `default` is conservative: `CSharpEmitter.cs:3573` emits `@default` when a local is in scope. No additional contextual literal requires refusal here.
- **Visibility and dependency checks succeed.** All remaining sanitizer calls are inside `CSharpEmitter`. Verification has no CodeGen reference. A read-only reproduction of ArchitectureTests’ dependency comparison matched all eight declared component contracts.
- **Collision tests still exercise the refusal**, at `tests/Calor.Compiler.Tests/ProofLocalObligationTests.cs:195`: `a-b` and `ab` both have key `ab`; both theory rows require `Unsupported` with the collision reason.
- **No relevant rebase regression found.** Compared with reviewed commit `05124b455`, the emitter differs only in sanitizer visibility; ObligationSolver, ContractTranslator, and ProofLocalObligationTests are unchanged.

Builds and tests were not run because they can write files.

VERDICT: APPROVE
