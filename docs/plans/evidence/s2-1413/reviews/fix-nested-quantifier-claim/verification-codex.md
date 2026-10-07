# S2 #1413 fix-nested-quantifier-claim — Codex verification-only pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

**MINOR — The merge-order resolution is not recorded as claimed.** The live [#1498 PR body](https://github.com/juanmicrosoft/calor/pull/1498) does not require #1495 to merge first. The CHANGELOG honestly discloses the dependency (`CHANGELOG.md:31–32`), but this checkout still silently falls back after Unsupported (`ContractInheritanceChecker.cs:1609–1611`, `1682–1684`) and can report “valid” (`489–498`, `1764–1769`). Add the explicit merge prerequisite to the PR body and identify the disposition record that requires it. No additional compiler fix is requested here.

The remaining verification checks pass by static inspection:

- **Stale-cache regression resolved.** `S2NestedQuantifierTests.cs:110–117` computes the key, applies the cache’s scope, seeds Proven through its writer, establishes a successful read, and asserts refusal through the public lookup. The corrected explanation at `87–88` matches `ContractHasher.cs:67`, `178–181` and `VerificationCache.cs:108–113`. Removing the lookup guard would expose the seeded entry.
- **Earlier resolutions remain intact.** Obligation refusal covers preconditions and only applicable facts (`ObligationSolver.cs:62–70`, `167–174`); guard validation refuses before translation (`GuardDiscovery.cs:245`, `262–263`); k-induction rejects the entire invariant when a conjunct cannot parse (`KInductionProver.cs:506–509`).
- **Documentation is accurate about normalization and runtime lowering.** `Z3Verifier.cs:1722–1726` describes the conservative restriction and runtime-lowerable exception. Constant quantifiers still simplify through `ExpressionSimplifier.cs:345–354`.
- **#1495’s proposed handling matches the stated dependency.** Its [Unsupported handling](https://github.com/juanmicrosoft/calor/blob/fd5e8cd6307515167b3e8ae2c4e9dc2ef7a72c4d/src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L1742) reports the refusal, and heuristic acceptance sets Unestablished, suppressing “valid.”
- **No new false proof, false Discharged, or fabricated counterexample identified.** The round-3 commit changes only the cache regression. Refusal evidence produces Unsupported with no counterexample (`ProofOutcome.cs:288–289`).
- **Counts are consistent.** Three compiler Facts give `12587 + 3 = 12590` (`S2NestedQuantifierChannelTests.cs:19,42,67`; `eng/test-manifest.json:8`). Three theory rows plus four verification Facts give `411 + 7 = 418` (`S2NestedQuantifierTests.cs:57–60,70,84,120,140`; manifest `106`). Expected skips remain unchanged.

No dotnet commands were run; the author’s reported test and mutation executions were not independently reproduced.
