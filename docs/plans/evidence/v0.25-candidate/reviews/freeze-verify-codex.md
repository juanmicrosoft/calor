# 0.25 candidate freeze PR (#1579): Codex verification pass

- **Invocation 4 of 4. The review budget is spent.**
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `6ba84b346` (the round-3 fixes), whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 1 MAJOR.

Codex confirmed: 14,645 + 18 = 14,663; offline regeneration matches exactly. It raised no regression of a round-1,
round-2 or round-3 finding.

## Open finding (not fixed: the budget ran out with it open, so work stopped for a maintainer decision)

| # | Severity | Finding | State |
|---|---|---|---|
| 1 | MAJOR | `TrustAnchorProblems` reads `git ls-files` output with default path quoting. An ignored file with a non-ASCII name (e.g. `tmp/é.cs`) is printed as `"…\303\251.cs"`, so the `.cs` suffix test misses it, while the SDK still compiles it. Proposed fix: `git ls-files -z`, split on NUL, and a quoted-name control. | OPEN. Scope: only the explicit-target mode (`CALOR_V025_CLASSIFY_TARGET`, used by the regeneration and A1 from a landing checkout); the default CI mode does not run the trust anchor. |

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. **MAJOR — [V025CandidateManifestTests.cs:89](tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:89): quoted paths bypass the ignored-input guard.** With default Git quoting, an ignored `tests/Calor.Compiler.Tests/tmp/é.cs` appears as `"tests/Calor.Compiler.Tests/tmp/\303\251.cs"`. The closing quote defeats line 92's `.cs` suffix check, while the SDK still compiles the file. A module initializer can therefore replace the requested target with the landing SHA, preserving the round-3 trust-anchor bypass. Use `git ls-files -z`, split on NUL, and add a control covering quoted filenames.

Count confirmed: **14,645 + 18 = 14,663**. Offline regeneration matched exactly with remote-tag lookup stubbed. .NET tests were blocked by sandbox temp-file permissions. No files modified.
```
