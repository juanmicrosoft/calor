# S2 #1413 fix-nested-quantifier-claim — Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Round 1 verification:

- **Resolved — Nested obligation assumptions.** `src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:62–68` now refuses nested quantifiers in the condition, preconditions, and collected facts before translation. The regression is at `tests/Calor.Compiler.Tests/S2NestedQuantifierChannelTests.cs:20–39`. The check introduces the scope regression described below.
- **Resolved — GuardDiscovery validation.** `src/Calor.Compiler/Verification/Obligations/GuardDiscovery.cs:262–263` returns false for a nested condition; line 245 assigns that result to `Validated`.
- **Resolved — Dropped k-induction conjuncts.** `src/Calor.Compiler/Verification/Z3/KInduction/KInductionProver.cs:506–509` returns null on any unparsed conjunct. The regression is at `tests/Calor.Compiler.Tests/S2NestedQuantifierChannelTests.cs:43–62`.
- **Unresolved — Interface refusal visibility.** Both silent heuristic fallbacks remain at `src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1609–1611` and `1682–1684`. The promised separate PR is not present in this checkout.
- **Partially resolved — Runtime rationale and wording.** The conservative restriction and runtime-lowerable exception are accurately documented at `src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:1722–1726`. The CHANGELOG acknowledges runtime-lowerable forms, but still omits the qualification about simplification.
- **Partially resolved — Cache test.** `tests/Calor.Verification.Tests/S2NestedQuantifierTests.cs:104–106` now checks that no JSON entry was written. It still never seeds a pre-existing entry.

Findings:

- **MINOR — New regression: unrelated scoped facts suppress independent proofs.** The new scan checks *every* collected fact at `src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:64`. The actual assumption query excludes facts outside the obligation’s scope at lines 169–170 and excludes all collected facts for `RefinementEntry` at line 165. Those exclusions are missing from the refusal check.

  A concrete static witness is:

  ```calor
  §M{m1:M}
    §F{f1:Probe:priv} () -> i32
      §E{}
      §PROOF{p1:claim} BOOL:true
      §IF{if1} (forall ((i i32)) (-> (&& (>= i INT:0) (< i INT:1)) (cast bool (forall ((j i32)) (-> (&& (>= j INT:0) (< j INT:1)) (< j INT:2))))))
        §R INT:1
      §R INT:0
  ```

  `FactCollector` records the later if-condition with a range restricted to its then-body (`src/Calor.Compiler/Verification/Obligations/FactCollector.cs:114–117`, `216–218`). Nevertheless, the earlier `BOOL:true` obligation becomes Unsupported. The cast permits runtime lowering through `src/Calor.Compiler/CodeGen/CSharpEmitter.cs:4174–4175`, `9536`, and `8509`. This adds an unnecessary warning/guard under the default policy and becomes a compilation error under `ObligationPolicy.Strict` (`src/Calor.Compiler/Verification/Obligations/ObligationPolicy.cs:42`, `60`). Apply the refusal to the facts eligible for this obligation and add a scope regression.

- **MINOR — Interface refusal remains hidden and can still become “valid.”** Unsupported still enters heuristics silently at `src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1609–1611` and `1682–1684`. For an interface postcondition `nestedCondition → P` and implementer postcondition `P`, the fallback accepts the consequent at lines 1764–1769. With no violations, lines 489–498 report `ContractInheritanceValid` instead of exposing the refusal. This contradicts `CHANGELOG.md:28–29`, which includes interface checks in the advertised Unsupported behavior. Land the dependent fix before claiming this channel is covered, or accurately scope the release note.

- **MINOR — The stale-cache guarantee remains untested.** The fresh-directory test attempts a refused write and then checks a miss (`tests/Calor.Verification.Tests/S2NestedQuantifierTests.cs:99–106`). Correct write refusal alone makes that lookup miss, even if lookup refusal is removed. “No stale entry can exist” at line 103 does not follow: an earlier compiler could have written one. Seed a compatible Proven entry directly, establish that the cache can otherwise read it, and assert that the nested key is refused. Static inspection shows the production lookup guard is present at `src/Calor.Compiler/Verification/Z3/Cache/VerificationCache.cs:108–110`; the missing piece is regression coverage.

- **MINOR — The CHANGELOG still overstates source-level refusal.** `CHANGELOG.md:24–31` promises no proof for any nested quantifier without mentioning normalization. `Program.cs:935–936` simplifies contracts before verification, and `ExpressionSimplifier.cs:345–354` recursively folds `(forall ((i i32)) (forall ((j i32)) BOOL:true))` to true. That source can still receive Proven and be cached as the normalized Boolean expression. This is a valid proof, but contradicts the unconditional release wording. State that refusal applies to nesting remaining after simplification.

I found no additional Proven/Discharged escape for expressions retaining nested quantifiers. The structural walker includes expression wrappers (`src/Calor.Compiler/Analysis/RecursiveAstWalker.cs:118–150`), body encoding rejects quantifier expressions before binding substitution (`src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:932–975`), and all four contract-cache operations honor `SawUnhashedKind`.

The manifest increments match seven verification cases and two compiler cases (`eng/test-manifest.json:106`, `8`). No dotnet commands were run; reported pre-fix failures were not independently verified.

## Response

| Finding | Disposition |
|---|---|
| MINOR unrelated scoped facts suppress independent proofs | Fixed: the refusal now checks only the facts the query would use. These are facts whose scope covers the obligation, and none at all for `RefinementEntry`, mirroring the solver's own fact selection. Regression: `NestedFactOutOfScope_DoesNotBlockAnUnrelatedObligation` (the reviewer's scoped `(cast bool …)` witness). |
| MINOR interface refusal hidden | Depends on #1495. That PR reports a solver `Unsupported` as `Calor0816` and never turns heuristic acceptance into `Calor0814`. The CHANGELOG now says the interface-channel refusal is reported once #1495 merges. |
| MINOR stale-cache guarantee untested | Not addressed by a test: no key can be computed for a nested-quantifier AST, because the hasher marks it unhashable. An entry an older compiler wrote under a different serialization is therefore unreachable from the current lookup, which is gated on `SawUnhashedKind` (VerificationCache.cs:108–110, confirmed by the reviewer). |
| MINOR CHANGELOG and simplification | Fixed: the entry says the rule applies after simplification, so a nested quantifier that simplifies to `true` is still proven. |
