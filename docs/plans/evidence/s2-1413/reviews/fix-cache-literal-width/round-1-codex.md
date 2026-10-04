# S2 #1413 fix-cache-literal-width — Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

1. **MAJOR — A pre-existing SDK key collision can still serve a false `Proven`.** [ContractHasher.cs:49](src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs:49) appends the output type without length-prefixing.

   Concrete public-AST witness: both obligations have Boolean parameter `p`, whose name is `a:PRECS:BOOL:true`, postcondition `Reference(p)`, and body `return INT:0`.

   | Field | Prime obligation | Final obligation |
   |---|---|---|
   | Output type | `i32` | `i32:PRECS:REF:17#a` |
   | Precondition | `Reference(p)` | `BOOL:true` |

   Both serialize to the same key input, including this segment:
   ```
   :i32:PRECS:REF:17#a:PRECS:BOOL:true;::POST:REF:17#a:PRECS:BOOL:true
   ```
   I confirmed the serialization equality by reconstructing the serializer. The prime query is `p ∧ ¬p`, hence Proven; the final query permits `p=false`, hence Refuted. The translator accepts the injected output spelling as an uninterpreted user type ([ContractTranslator.cs:1138](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:1138)). A cache hit bypasses verification ([ContractVerificationPass.cs:135](src/Calor.Compiler/Verification/ContractVerificationPass.cs:135)), returning the prime’s unconditional proof.

   **Reach is SDK-supplied AST strings; this is not a parser-produced source witness.** Length-prefix the output type and add a warm-cache regression.

2. **MAJOR — The required CI quality check fails.** The new tests introduce three skip sites at [S2CacheLiteralWidthTests.cs:79](tests/Calor.Compiler.Tests/S2CacheLiteralWidthTests.cs:79), lines 94 and 105, but [test-manifest.json:160](eng/test-manifest.json:160) still pins 397.

   Executed `python3 -B scripts/check_test_quality.py`; it exits 1:
   ```
   ERROR: runtime skip site count changed for 'Z3 not available': expected 397, got 400
   ```
   CI invokes this check at [test.yml:84](.github/workflows/test.yml:84).

3. **MINOR — Binding types retain another pre-existing SDK alias.** [ContractHasher.cs:100](src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs:100) hashes a null binding type identically to the literal type `"?"`. However, [Z3Verifier.cs:813](src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:813) accepts null as width-neutral and rejects `"?"`.

   Otherwise identical AST bodies `bind t = INT:1; return t`, with `i32` output and postcondition `result == INT:1`, therefore share a key despite cold outcomes Proven versus Unsupported. Encode null separately. This demonstrates refusal bypass, not an executable-source false-proof witness.

4. **MINOR — The warm regressions do not establish that the cache was warmed.** [S2CacheLiteralWidthTests.cs:97](tests/Calor.Compiler.Tests/S2CacheLiteralWidthTests.cs:97) checks only verdicts. Cache write failures are swallowed at [VerificationCache.cs:300](src/Calor.Compiler/Verification/Z3/Cache/VerificationCache.cs:300). Consequently, these warm tests can pass without the fix when writes fail. Assert that the prime postcondition entry exists and that repeating the prime produces a hit.

   The integer-key tests independently discriminate the old implementation. Test directories are GUID-scoped temporary paths, so the supplied cache overrides do not touch `~/.calor`.

5. **MINOR — The “eviction” regression tests only the constant.** [S2CacheLiteralWidthTests.cs:158](tests/Calor.Compiler.Tests/S2CacheLiteralWidthTests.cs:158) never writes or reads a 1.18 entry. Add an actual old-entry lookup test. Production rejection is correctly implemented: [VerificationCacheEntry.cs:218](src/Calor.Compiler/Verification/Z3/Cache/VerificationCacheEntry.cs:218) rejects mismatched versions before [VerificationCache.cs:253](src/Calor.Compiler/Verification/Z3/Cache/VerificationCache.cs:253) rehydrates the verdict.

6. **NIT — The changelog overstates physical eviction.** [CHANGELOG.md:14](CHANGELOG.md:14) says every older entry is discarded once. Deletion occurs only on lookup; changed literal keys leave old files unreachable until ordinary cleanup. Say “older entries are invalidated.” The description of the INT/LONG defect is otherwise accurate.

The literal-field change itself matches the translator’s integer typing inputs, and I found no valid-proof regression introduced by it. Overflow mode, user-type registry, semantics version, quantifier types, and comparison modes are covered; unsupported casts cannot consume an unhashed key. .NET tests were not run in this read-only workspace.

## Response

| Finding | Disposition |
|---|---|
| 1 MAJOR output type not length-prefixed (SDK-built AST collision) | Fixed: `HashPostcondition` length-prefixes the output type (`AppendRaw`). Regression `PostconditionKey_OutputTypeCannotForgeDelimiters` uses the reviewer's witness and fails before the fix. |
| 2 MAJOR skip-site count | Fixed by removing the skip sites: the tests are plain `[Fact]`s that need Z3 and fail without it (a skipped soundness witness would read as a pass). `scripts/check_test_quality.py` passes. |
| 3 MINOR null binding type vs `"?"` | Fixed: an inferred (null) binding type hashes as `<inferred>`, which no length-prefixed spelling can produce. Regression `PostconditionKey_InferredBindingTypeDiffersFromQuestionMarkSpelling` fails before the fix. |
| 4 MINOR warm tests do not prove the cache was warmed | Fixed: both warm tests assert the prime wrote an entry and the final compile missed it and wrote its own. |
| 5 MINOR eviction tested only via the constant | Fixed: `PreBumpEntries_AreNotServed` forges a `Proven` into the INT text's entry. Control (current format): the forged verdict is served, proving the warm path is read. Stamped `1.18`: rejected, re-verified `Refuted`. |
| 6 NIT changelog wording | Fixed: "invalidated". |
