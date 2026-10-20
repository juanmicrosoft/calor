# 0.25 re-freeze PR (#1611): Codex round 2

- **Invocation 2 of at most 4.**
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `238767f79` (the round-1 fixes), whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 1 MAJOR.

Codex confirmed: the four supersession-version controls discriminate; the verifier and all 62 tamper controls
pass; 14,704 + 10 = 14,714; the residual deferral is appropriate; the regeneration matches.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | .NET's `$` matches before a final `\n`, so `candidate.commit = "<sha>\n"` passed the full-SHA check, failed equality and was skipped: the round-1 bypass (malformed manifest, classifier edit, valid manifest) still gave NOT-INVALIDATED. | Fixed: `\A[0-9a-f]{40}\z`. Two rows added to `AManifestNamingNoFullCandidateIsUndecided` (trailing newline, uppercase SHA), both UNDECIDED. |

Test manifest 14,714 -> 14,716.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. **MAJOR — [ReleaseCandidateInvalidation.cs:236](tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs:236):** Round 1's landing bypass remains reachable with a trailing newline. .NET's `$` accepts the position before a final `\n`, so `candidate.commit = "<candidate SHA>\n"` passes validation but fails equality and is skipped. Executing the actual classifier with injected history—malformed manifest → classifier edit → valid manifest—returned `NOT-INVALIDATED`; the valid-name control returned `INVALID`. Require exactly 40 characters or use `\A[0-9a-f]{40}\z`, and add this case to the malformed-manifest theory expecting `UNDECIDED`.

The four supersession-version fixes discriminate independently. The in-memory baseline verifier and all 62 tamper controls pass. **14,704 + 10 = 14,714** is correct. The residual deferral is appropriate. Generator regeneration matches; `--check` fails only on the inaccessible remote-tag lookup. Standard .NET tests cannot start in the read-only sandbox. No files modified.
```
