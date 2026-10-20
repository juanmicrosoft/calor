# 0.25 re-freeze PR (#1611): Codex round 1

- **Invocation 1 of at most 4** (fresh budget: 3 rounds plus 1 verification pass).
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `d9174ab9e`, whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 1 MAJOR, 1 MINOR.

Codex confirmed: 14,696 + 8 = 14,704; deferring the gate residual is right, since its fix changes a candidate
input; the generator's regeneration matched (only the remote-tag lookup was unreachable in the sandbox) and its
self-tests passed.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | `FindLanding` rejected only a missing name, so a manifest naming an empty, blank or malformed candidate was skipped, and a later valid manifest could become the landing and exempt a classifier edit in between. | Fixed: only a manifest naming a full 40-hex SHA may be skipped as another candidate's; anything else is an error (UNDECIDED). Theory `AManifestNamingNoFullCandidateIsUndecided` (6 rows: `{}`, empty, blank, short SHA, a number, not JSON), each followed by a classifier edit and a valid manifest. |
| 2 | MINOR | The supersedes verification ignored the recorded protocol, scope, register and contract versions. | Fixed in the verifier and the generator: they must equal the candidate's G2 protocol, scope, T1 register and D1 contract versions, and the amendment PR is #1610. Four tamper controls. |

Test manifest 14,704 -> 14,714.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. **MAJOR — `tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs:235`:** Only `null` is rejected. `{"candidate":{"commit":""}}` is silently skipped, as are whitespace and malformed SHAs. A later valid manifest can therefore become the landing, making intervening classifier edits exempt as `BeforeLanding`. Require a full candidate SHA before allowing a mismatch to be skipped. Add controls asserting `UNDECIDED` for empty/malformed names followed by a valid manifest and classifier edit.

2. **MINOR — `tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:512`:** Supersession verification checks `amendments.mergeCommit` but ignores the recorded protocol/scope/register/contract versions. For example, changing `supersedes.amendments.protocol` to `"1.6.0"` passes both this verifier and the generator's supersession checks. Compare these fields against the candidate's packets and protocol; add isolated tamper controls.

The count is correct: **14,696 + 8 = 14,704**. The gate residual is appropriately deferred because its fix changes a candidate input. Generator `--check` reported only the inaccessible remote-tag check; regeneration matched. Generator self-tests passed. .NET tests could not start under the read-only sandbox. No files modified.
```
