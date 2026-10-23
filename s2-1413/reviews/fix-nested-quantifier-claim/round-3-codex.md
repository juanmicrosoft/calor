# S2 #1413 fix-nested-quantifier-claim — Codex review round 3

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Earlier findings verified against the current code:

- **Resolved — Nested obligation assumptions.** The refusal checks the condition, preconditions, and applicable collected facts before translation (`src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:62–70`). The nested-precondition regression remains at `tests/Calor.Compiler.Tests/S2NestedQuantifierChannelTests.cs:20–39`.
- **Resolved — Unrelated scoped facts suppressing proofs.** The refusal now uses `fact.AppliesTo(obligation.Span)` and excludes collected facts for `RefinementEntry` (`ObligationSolver.cs:64–66`), matching query selection at lines 167–174. The scope regression is at `S2NestedQuantifierChannelTests.cs:43–64`.
- **Resolved — GuardDiscovery validation.** Nested conditions return false before translation (`src/Calor.Compiler/Verification/Obligations/GuardDiscovery.cs:262–263`); that value becomes `Validated` at line 245.
- **Resolved — Dropped k-induction conjuncts.** Any unparsed conjunct now rejects the entire invariant (`src/Calor.Compiler/Verification/Z3/KInduction/KInductionProver.cs:506–509`). The regression is at `S2NestedQuantifierChannelTests.cs:68–87`.
- **Unresolved in code — Interface refusal visibility and false “valid.”** Both silent fallbacks remain (`src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1609–1611`, `1682–1684`). `CHANGELOG.md:31–32` now accurately discloses the dependency on #1495; that documentation correction does not implement the fix.
- **Resolved — Runtime rationale and simplification wording.** The conservative restriction, runtime-lowerable exception, and normalization boundary are documented at `src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:1722–1726` and `CHANGELOG.md:26–32`. Simplification still folds nested constant quantifiers through `ExpressionSimplifier.cs:345–354`, as intended.
- **Partially resolved — Cache regression.** Zero writes are checked at `tests/Calor.Verification.Tests/S2NestedQuantifierTests.cs:104–106`. A pre-existing entry is still never seeded.

Findings:

- **MINOR — Interface checks still advertise “valid” after refusing nested quantifiers.** The implication prover returns Unsupported for nesting (`src/Calor.Compiler/Verification/Z3/Z3ImplicationProver.cs:217–222`), but the inheritance checker silently enters heuristics (`ContractInheritanceChecker.cs:1682–1684`). The earlier witness remains: interface postcondition `nestedCondition → P`, implementer postcondition `P`. The consequent shortcut accepts it at lines 1764–1769, and the absence of violations produces `ContractInheritanceValid` at lines 489–498. The CHANGELOG now scopes the dependency correctly, but this behavioral finding remains open in the reviewed checkout. Land #1495 before treating this channel as repaired.

- **MINOR — Stale-cache refusal remains independently untested; the author’s “no key can be computed” explanation is incorrect.** `ContractHasher.HashPostcondition` still returns a SHA-256 key (`src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs:67`). The new check sets `SawUnhashedKind` at lines 177–181 while the existing quantifier serialization continues at lines 230–257. The protection is the lookup’s explicit flag check (`VerificationCache.cs:108–110`), not the absence of a computable key or necessarily a different serialization.

  The regression attempts a refused write before checking a miss (`tests/Calor.Verification.Tests/S2NestedQuantifierTests.cs:99–106`). Removing the postcondition lookup guard would therefore leave this test passing. Seed a compatible Proven entry directly at the computed key, establish a readable positive control, then assert that nested lookup refuses it. Correct the “no stale entry can exist” comment at line 103.

I found no additional Proven/Discharged escape for expressions retaining nested quantifiers. The structural walker covers wrappers (`src/Calor.Compiler/Analysis/RecursiveAstWalker.cs:118–150`); body encoding rejects quantifiers before binding substitution (`Z3Verifier.cs:932–975`, `1478–1483`); all four contract-cache operations check `SawUnhashedKind` (`VerificationCache.cs:82`, `109`, `136`, `166`).

The manifest increases match seven verification cases and three compiler cases (`eng/test-manifest.json:106`, `8`). No dotnet commands were run; reported pre-fix failures were not independently verified.

## Response

| Finding | Disposition |
|---|---|
| MINOR interface checks still advertise "valid" after a nested-quantifier refusal | Resolved by merge order, not in this branch. #1495 (R-IMPL) makes the inheritance checker report a solver `Unsupported` as `Calor0816`. It sets the `Unestablished` status, so `Calor0814` is never reported after a refusal or a heuristic acceptance. Its round 3 also covers the inherited-conflict channel. The disposition record and this PR's body require #1495 to merge before #1498. A trial rebase onto #1495 conflicts in `Z3ImplicationProver.cs` and the test manifest. Stacking was therefore not done, to keep both PRs independently reviewable. The CHANGELOG already states the dependency. |
| MINOR stale-cache refusal untested; explanation incorrect | Fixed. `NestedForall_IsNeverCached` now computes the key the hasher still produces. It seeds a Proven entry at the scoped key through the cache's own writer and shows that the entry is readable by key (positive control). It then asserts that `TryGetPostconditionResult` refuses to serve it. Removing the `SawUnhashedKind` lookup guard makes the test fail (checked locally). The comment now says that the key is computable but flagged, not that no key exists. |
