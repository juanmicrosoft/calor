# S2 #1413 fix-nested-quantifier-claim — Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

- **MAJOR — Obligations can still be discharged using nested quantifiers.** The new check examines only `obligation.Condition` (`src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:61`). Preconditions and applicable flow facts are translated and asserted without that check at lines 147–154 and 164–173. UNSAT still becomes Discharged at line 265. A concrete witness is a nested, bounded precondition whose innermost predicate is `(> n INT:0)`, followed by `§PROOF{p1:claim} (> n INT:0)`. Use nonempty bounds `[0,1)` for both quantifiers: the assumptions are satisfiable and entail the proof. The proof channel can therefore claim Discharged while runtime lowering rejects its supporting precondition. Check every assumption actually used by the obligation query.

- **MAJOR — GuardDiscovery can publicly validate a refused nested proof.** `GuardDiscovery.ValidateSingleGuard` translates the nested condition without refusal, asserts both it and its negation, and returns true for UNSAT (`src/Calor.Compiler/Verification/Obligations/GuardDiscovery.cs:261–292`). This is reachable through MCP: supplying `obligation_id` selects `DiscoverForObligation` regardless of Unsupported status, then invokes validation and returns `Validated` (`src/Calor.Compiler/Mcp/Tools/RefineTool.cs:378–404`). The new nested-proof test’s obligation can consequently be Unsupported in verification but advertised as having a validated guard. Apply the refusal here and avoid claiming validation.

- **MAJOR — The public k-induction channel can still return Proven for an invariant containing nested quantifiers.** `ParseInvariant` silently drops conjunction members it cannot parse (`src/Calor.Compiler/Verification/Z3/KInduction/KInductionProver.cs:497–509`). For a loop from 0 to 3 with step 1, pass:
  ```text
  i >= 0 && (forall ((j i32)) (forall ((k i32)) false))
  ```
  It proves only `i >= 0`, then returns Proven with the entire original invariant string at line 324. `LoopInvariantSynthesizer.VerifyInvariant` exposes that result as Synthesized (`src/Calor.Compiler/Verification/Z3/KInduction/LoopInvariantSynthesizer.cs:144–159`). This is an API witness, not a demonstrated Calor compilation route. Reject any unparsed conjunct; the advertised “every verifier channel” guarantee currently excludes this entry point.

- **MINOR — Interface refusal is swallowed.** Both Unsupported branches silently fall back to heuristics (`src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1609–1611`, `1682–1684`). The new refusal reason never reaches users through those checks. Contract verification does report it (`ContractVerificationPass.cs:272–288`), but interface checks need an explicit diagnostic if the changelog promises a visible Unsupported result.

- **MINOR — The runtime rationale and changelog are too broad.** The emitter does not categorically prohibit nesting. It recognizes `(cast bool …)` as Boolean (`src/Calor.Compiler/CodeGen/CSharpEmitter.cs:4174–4175`), certifies a bounded outer quantifier, and recursively emits its body (`9536`, `9565`, `8509`). Wrapping a bounded inner quantifier in `(cast bool …)` therefore permits nested runtime lowering without Calor0326. Also, `§S (forall ((i i32)) (forall ((j i32)) BOOL:true))` simplifies to true before verification (`Program.cs:935–936`; `ExpressionSimplifier.cs:345–354`) and can still be Proven and cached. These are not false proofs, but they contradict the unconditional wording in `CHANGELOG.md:24` and `QuantifierNesting.cs:16`. Describe a conservative verifier restriction and distinguish original source from the normalized expression.

- **MINOR — The cache test does not establish both advertised guarantees.** `NestedForall_IsNeverCached` attempts a write and then checks a miss (`tests/Calor.Verification.Tests/S2NestedQuantifierTests.cs:99–102`). It passes if storage wrongly succeeds while lookup correctly refuses, and it never seeds an old Proven entry to exercise stale-cache refusal. Assert zero writes and separately seed a compatible entry before lookup. Coverage also omits nested assumptions, GuardDiscovery, exists/mixed nesting, and postcondition strengthening.

The structural walker covers implication, conditional, and string-operation wrappers (`RecursiveAstWalker.cs:118–150`). I found no binding-substitution escape: quantifier initializers/bodies already fail the body encoder’s expression whitelist (`Z3Verifier.cs:932–975`), and substitution itself lacks quantifier cases (`1478–1483`). The four contract-cache operations honor `SawUnhashedKind`; no direct bypass was found.

The manifest increase is correct: three theory cases plus four facts equals seven, taking 411 to 418. The reported six pre-fix failures were not independently verified; no dotnet commands were run.

## Response

| Finding | Disposition |
|---|---|
| MAJOR obligations discharged from nested assumptions | Fixed: the obligation is `Unsupported` when its condition **or any precondition or collected fact** contains a nested quantifier. Regression: `S2NestedQuantifierChannelTests.NestedQuantifierPrecondition_DoesNotDischargeAnObligation` (failed before the fix). |
| MAJOR GuardDiscovery validates a refused nested proof | Fixed: `ValidateSingleGuard` returns false for a nested condition, so no "validated" guard is advertised. |
| MAJOR k-induction drops unparsable conjuncts | Fixed (pre-existing, same laundering class): `ParseInvariant` returns null when any conjunct cannot be parsed, so nothing is proved for a partially understood invariant. Regression: `KInduction_UnparsableConjunct_IsNotDropped` (failed before the fix). |
| MINOR interface refusal swallowed by heuristics | Addressed by #1495, which never reports `Calor0814` "valid" when only the syntactic heuristic accepted after the solver declined. Merge order: this PR's refusal is silent on that channel until #1495 merges. |
| MINOR wording too broad | The doc comment, the refusal reason, and the CHANGELOG now say this is a conservative verifier restriction that also covers some nested forms the emitter can lower (for example `(cast bool …)`), applied to the expression after simplification. |
| MINOR cache test | Now asserts that nothing was written (no stored key, so no stale entry can be served). |
