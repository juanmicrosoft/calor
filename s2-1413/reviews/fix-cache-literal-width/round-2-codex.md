# S2 #1413 fix-cache-literal-width — Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

1. **MINOR — Pre-existing SDK collision remains: UTF-8 replacement can turn an Unsupported obligation into cached Proven.** [ContractHasher.cs:409](src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs:409) uses `Encoding.UTF8.GetBytes`, which replaces unpaired surrogates with `U+FFFD`. Length-prefixing at [ContractHasher.cs:176](src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs:176) does not distinguish these equal-length strings.

   Concrete public-AST witness, with identical Boolean parameter named `"p\uFFFD"`, precondition `Reference("p\uFFFD")`, output `i32`, and body `return INT:0`:

   | Obligation | Postcondition | Cold outcome |
   |---|---|---|
   | Prime | `Reference("p\uFFFD")` | Proven: `p ∧ ¬p` is unsatisfiable |
   | Final | `Reference("p\uD800")` | Unsupported: unknown variable |

   Both postconditions hash the bytes for `REF:2#p�`. However, [ContractTranslator.cs:478](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:478) performs exact managed-string dictionary lookup, so the final reference fails translation. [ContractVerificationPass.cs:135](src/Calor.Compiler/Verification/ContractVerificationPass.cs:135) serves the prime’s unconditional Proven before that refusal runs.

   **This is SDK-only refusal bypass, not a demonstrated executable-source false proof.** It counts against completeness because it is the same different-semantics/same-key defect. Serialize UTF-16 code units losslessly, or reject malformed UTF-16 and disable caching for that obligation. Add key-distinctness and warm-cache regressions.

The six round-1 findings are materially addressed. The strengthened warm tests discriminate failed writes, and the forged-entry control exercises actual cache reads and 1.18 rejection. `python3 -B scripts/check_test_quality.py` passes; the new class contributes 14 cases, matching `12518 → 12532`. I found no introduced valid-proof regression, user-cache pollution, or inaccurate CHANGELOG claim.

.NET tests were not run, as instructed.

## Response

| Finding | Disposition |
|---|---|
| 1 MINOR (pre-existing, same defect class) UTF-8 hashing maps an unpaired surrogate and U+FFFD to the same bytes | Fixed: `ContractHasher` hashes the raw UTF-16 code units (`LosslessBytes`), and `VerificationCache` hashes its key scope the same way. Regression `CacheKey_UnpairedSurrogateDoesNotCollideWithReplacementCharacter` (hasher keys and a real store/lookup round trip, plus the key scope) fails before the fix. The hash text changes for every key; the 1.19 format bump in this PR already invalidates older entries. |

Round-1 findings: the reviewer confirmed all six resolved.
