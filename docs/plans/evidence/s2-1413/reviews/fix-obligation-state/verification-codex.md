# S2 #1413 fix-obligation-state — Codex verification-only pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static verification only; I did not run `dotnet`. The three original round-3 witnesses are repaired, but the final proof-condition exemption reopens the getter mutation defect.

**BLOCKING — Getter reads inside retained proof checks can invalidate facts used to discharge later proofs.**

In the getter regression at [S2ObligationStateTests.cs:261](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/tests/Calor.Compiler.Tests/S2ObligationStateTests.cs:261), replace the binding that reads `this.Trigger` with:

```calor
§PROOF{p0:trigger} (== this.Trigger INT:0)
§PROOF{p1:claim} (> x INT:0)
§R x
```

Keep the original getter, which assigns `this.Value = -5` and returns `0`, and invoke `box.Probe(ref box.Value)` with `Value = 1`.

The static execution and solver paths are:

- `OutsideProofs` excludes the entire proof condition from heap-read classification. Thus `MutatesHeap` stays false and the alias expansion does not kill the fact about `x`. See [FactCollector.cs:75](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/FactCollector.cs:75) and [FactCollector.cs:153](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/FactCollector.cs:153).
- `p0` is Unsupported: the translator cannot resolve undeclared receiver `this`. Its runtime guard remains, invokes the getter, changes aliased `x` to `-5`, and passes because the getter returns `0`. See [ContractTranslator.cs:519](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:519) and [CSharpEmitter.cs:9945](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/CodeGen/CSharpEmitter.cs:9945).
- The independent query for `p1` still asserts entry precondition `x > 0`, discharges the identical claim, and removes its guard. At runtime that claim is false. See [ObligationSolver.cs:168](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:168) and [CSharpEmitter.cs:9928](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/CodeGen/CSharpEmitter.cs:9928).

This directly checks the new exemption in the stated fix. Evaluating a proof condition does not establish getter purity. Remove the blanket exemption or distinguish proven pure reads, and add this sequential-proof regression.

The remaining dispositions check out:

| Round-3 finding | Verification |
|---|---|
| 1 — Opaque entry predicates | Resolved for the reported witness. Parameter predicates set opacity at `FactCollector.cs:171`; preconditions and that flag cause an early Unsupported return at `ObligationSolver.cs:71`, before translation or assertion. |
| 2 — Getter alias mutation | The original binding witness is resolved by heap-read classification and alias expansion (`FactCollector.cs:97`, `153`). The proof-condition exception is unsound as described above. |
| 3 — Dropped-refinement dependencies | Resolved. `ObligationSolver.cs:306–310` checks preconditions, applicable facts, and the obligation; SAT is demoted to Unsupported at `312–321`. This adds no assumptions and cannot manufacture a proof. |
| 4 — Divergent inventories | Resolved structurally: whole-body collection and scoped guards use `MayChangeHeap` (`FactCollector.cs:199`, `422`); multidimensional reads are included at `98`. Both inherit the blocking exemption. |

The throwing-predecessor residual is documented at `CHANGELOG.md:60` and `FactCollector.cs:115`. Implicit exceptions remain unmodeled; explicit throw statements are tracked by `CanDivert` at `FactCollector.cs:333`. The named subtype control remains present at `ObligationTests.cs:1622`, with its non-Unsupported assertion at `1645`.

Counts are consistent: **22 Facts + 17 theory rows = 39 cases**, including the three additions at `S2ObligationStateTests.cs:219`, `240`, and `260`. **12587 + 39 = 12626**, matching `eng/test-manifest.json:8`; Verification.Tests remains **411**, matching line `106`. The reported passing runs and pre-fix failures remain author-provided evidence, not independently execution-verified.

## Response

| Finding | Disposition |
|---|---|
| BLOCKING getter reads inside retained proof checks invalidate facts used by later proofs | Fixed. The proof-condition exemption is removed. Every member or element read, including one inside a `§PROOF` condition, now counts as a possible heap effect for `MutatesHeap`. That drives entry-fact staleness, alias expansion, and guard facts, so no fact survives a read that could run a getter. Only the counterexample-exactness check, which demotes SAT to `Unsupported`, ignores reads inside the obligation's own span (new `FactCollector.IsStaleBefore`). A read in the obligation's own condition is that obligation's evaluation. This keeps the G3 differential oracle's single-proof refutations, which share one span. Residual: a getter in an obligation's own condition that writes state the same condition reads afterwards could give a spurious refutation. It cannot give a false Discharged, because facts still use the global rule. Regression: `PropertyGetterReadInAnEarlierProof_KillsAliasedRefParameterFact` (the reviewer's `p0`/`p1` witness), which failed before this change. Calor.Compiler.Tests 12627 and Calor.Verification.Tests 411 pass. The PR has 589 non-test lines. |
