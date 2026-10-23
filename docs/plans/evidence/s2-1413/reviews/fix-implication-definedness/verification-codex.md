# S2 #1413 fix-implication-definedness — Codex verification-only pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: APPROVE

Static verification only; I did not run `dotnet`. No BLOCKING or MAJOR findings remain in the inspected round-3 fixes.

1. **Reference SAT witnesses: resolved.** String, array, and user-type SAT queries return `Unsupported` before counterexample construction ([Z3ImplicationProver.cs:213](src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs#L213)). This covers the negative-index, substring, and Unicode witnesses without relying on #1497. The CLI treats `Unsupported` as non-definitive ([VerifyCommand.cs:887](src/Calor.Compiler/Commands/VerifyCommand.cs#L887)).

2. **Quantified identity: resolved.** Identity is checked before either implication query ([ContractInheritanceChecker.cs:1598](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1598), [1692](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1692)). The comparison includes node types, non-span properties, and collection elements ([2068](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L2068)). For the cited contracts, successful evaluation of the antecedent establishes the identical consequent. The regression requires `Calor0814` and excludes both violation diagnostics ([S2ImplicationDefinednessTests.cs:371](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs#L371)). The disclosed non-identical fallback still emits “Could not prove” errors without fabricated counterexamples ([ContractInheritanceChecker.cs:1815](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1815)).

3. **Inherited-conflict visibility and status: resolved.** Both `Assumed` and refusal outcomes warn and set the flag ([ContractInheritanceChecker.cs:1476](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1476)). The reset precedes conflict checking ([409](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L409)); both inherited and explicit paths preserve `Unestablished` ([443](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L443), [497](src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L497)). The unused-string genuine-conflict control remains ([S2ImplicationDefinednessTests.cs:317](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs#L317)).

4. **Documentation and counts: consistent.** The changelog scopes modeled definedness and documents reference refusals ([CHANGELOG.md:51](CHANGELOG.md#L51)). Static enumeration gives **18 compiler cases** and **6 verification cases**, matching **12587 → 12605** and **411 → 417**; expected skips remain unchanged ([test-manifest.json:8](eng/test-manifest.json#L8), [106](eng/test-manifest.json#L106)). Parse checks and the CLI overflow-policy control are present ([S2ImplicationDefinednessTests.cs:61](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs#L61), [409](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs#L409)).

No new false proof, false `Discharged`, or fabricated counterexample was identified in these changes. Identity does not manufacture a solver outcome; non-establishing outcomes remain excluded from `Discharged` ([ProofOutcome.cs:396](src/Calor.Compiler/Verification/ProofOutcome.cs#L396)).

**MINOR — Status regression coverage remains incomplete.** The conflict test asserts the warning and absence of `Calor0814`, but neither directly asserts `Unestablished` nor exercises the explicit-contract path ([S2ImplicationDefinednessTests.cs:403](tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs#L403)). The implementation is correct statically.

The reported 417/174 passing runs and pre-fix failures remain author-provided evidence, not independently verified here.
