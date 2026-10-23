# 0.25 re-freeze PR (#1611): Codex round 3

- **Invocation 3 of at most 4.**
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `2bdb89f41` (the round-2 fix), whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 1 MINOR.

Codex confirmed: both earlier fixes reproduce; the verifier and all 62 tamper controls pass; the first freeze's
landing is unchanged; a simulated re-freeze landing is NOT-INVALIDATED and a later classifier edit INVALID; the
residual deferral is appropriate; 14,714 + 2 = 14,716; the regeneration matches.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MINOR | A manifest with a duplicate key made `JsonNode` throw `ArgumentException`, so the classifier crashed (fail-closed) instead of returning UNDECIDED. | Fixed: `ArgumentException` is caught like a parse error (the manifest names no candidate, an error). Row `DUPLICATE-KEYS` in `AManifestNamingNoFullCandidateIsUndecided`. |

Test manifest 14,716 -> 14,717.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. **MINOR — [ReleaseCandidateInvalidation.cs:234](tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs:234):** Duplicate JSON keys escape the malformed-manifest guard. Reproducer: `{"candidate":{"commit":"<full candidate SHA>"},"candidate":null}` in the malformed-manifest → classifier-edit → valid-manifest history. The actual classifier throws `ArgumentException` instead of returning `UNDECIDED`. This fails closed, but breaks its reporting contract. Handle duplicate-key parsing errors and add a theory row asserting `UNDECIDED`.

Both prior-round fixes reproduce correctly. The in-memory baseline verifier and all 62 tamper controls pass. First-freeze landing remains unchanged; simulated re-freeze landing is `NOT-INVALIDATED`, and a later classifier edit is `INVALID`. Residual deferral is appropriate. **14,714 + 2 = 14,716.**

Generator regeneration matches; `--check` fails only on the sandbox-inaccessible remote-tag lookup. Standard .NET tests cannot start under the read-only sandbox. No files modified.
```
