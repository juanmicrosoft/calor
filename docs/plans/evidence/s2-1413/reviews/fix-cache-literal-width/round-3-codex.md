# S2 #1413 fix-cache-literal-width — Codex review round 3

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: APPROVE

**NIT — The surrogate regression’s comment reverses the witness.** `tests/Calor.Compiler.Tests/S2CacheLiteralWidthTests.cs:180–182` says the surrogate name supplied the cached Proven. The code correctly primes `"p\uFFFD"` and probes `"p\uD800"` (lines 185–191). Correct the comment.

All prior findings are resolved:

- **Round 1:** output types are length-prefixed; inferred binding types have a distinct marker; added runtime skips are removed; warm tests require successful writes; the forged-entry control demonstrates actual cache reads and 1.18 rejection; CHANGELOG now says “invalidated.”
- **Round 2:** contract hashes and scope hashes preserve UTF-16 code units. The regression checks distinct obligation keys, a successful store/read, a final-obligation miss, and a different-scope miss.

I found no remaining different-verifier-semantics/same-key witness, introduced valid-proof regression, user-cache pollution, or substantive CHANGELOG error. Supported translator and body-encoder inputs are represented in the key; unhandled expression kinds disable cache lookup and storage.

`python3 -B scripts/check_test_quality.py` passes. The class adds **15 cases: 8 facts + 7 theory rows**, matching `12518 → 12533`; expected skips remain 3.

.NET tests were not run, as instructed.

## Response

| Finding | Disposition |
|---|---|
| NIT surrogate test comment reversed the witness direction | Fixed: the comment names the primed name (`p�`) and the probed name (`p\uD800`); the literal U+FFFD in the source is now the `�` escape. |

After this round the branch was squashed and rebased onto `main` at `e967aba8` (PR #1491, the parser fix); the only conflict was `eng/test-manifest.json`, re-bumped with the same +15 delta, and the CHANGELOG entry moved into the existing `### Fixed` section.
