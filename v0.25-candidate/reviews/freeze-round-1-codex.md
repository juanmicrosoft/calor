# 0.25 candidate freeze PR (#1579): Codex round 1

- **Invocation 1 of at most 4** (fresh budget: 3 rounds plus 1 verification pass).
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** the first freeze commit on `origin/main` `48cae911`.
- **Verdict:** REQUEST-CHANGES: 4 MAJOR.

Codex confirmed: 14,606 + 17 = 14,623; the recorded GitHub merges, issue states and closure times match;
offline regeneration of the manifest matches exactly.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | The default target used single-commit `diff-tree`, which lists nothing for a merge, so it could skip the landing and miss changes merged into main first. | Fixed: `ReleaseCandidateInvalidation.NewestProfileCommit` walks `candidate..HEAD` in topological order, diffs each commit against its first parent, and treats any git failure as an error. Control: `TheDefaultTargetIsTheMergeThatBringsInTheFreeze` (a change merged into main before the landing is inside the range and invalidates; an injected `diff-tree` failure is an error). |
| 2 | MAJOR | Gate membership and completeness were unchecked (a PR substituted between gates, or a gate removed, passed). | Fixed: a reviewed gate table (`GatePrs`, the 0.24 pattern) binds the 15 gates, their issues and PRs. Controls: a substituted PR and a removed gate. |
| 3 | MAJOR | Toolchain (SDK, workflow pins, runtime, fingerprint and its source), packet summaries and several seals were unchecked. | Fixed: the reproducible-builds hash files the fingerprint came from are retained (`sdk-fingerprint/`, recorded by path and SHA-256) and must name the fingerprint, SDK 10.0.401 and Linux x86_64. The workflow pins are recomputed per workflow, the runtime is compared with the G2 pin, the packet summaries with the candidate's files, and every seal (scope computed and pinned, D1 pinned, T1 baseline and amendments) with the candidate's packets and test pins. 13 new controls. |
| 4 | MAJOR | The trust anchor covered only profile files, not the classifier's dependencies (for example `CandidateInvalidation.Invalid`). | Fixed: with `CALOR_V025_CLASSIFY_TARGET` set, the whole `tests/Calor.Compiler.Tests` project and the candidate directory (reviews excepted) must equal the landing and be clean. |

Test manifest 14,623 → 14,639.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:209
   NewestCandidateCommit uses single-commit diff-tree without merge handling, which emits nothing for merge commits. It therefore skips the freeze landing and can select the branch commit, excluding invalidating changes merged into main before landing. Diff each commit against ^1, preserve topological order, check command failures, and add an intervening-merge control.

2. MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:179
   Merge verification checks only listed PRs, without enforcing gate membership or completeness. In memory, replacing F5's #1554 with W0's #1512 produces zero generator findings; this verifier also accepts it. Removing an untested gate likewise escapes verification. Bind each gate to its reviewed PR set, as the 0.24 precedent does, and add substitution/deletion controls.

3. MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:144
   Toolchain verification covers only Z3 assets. SDK version, workflow pins, runtime, fingerprint, and fingerprint source are unchecked. The generator accepts sha256: followed by 64 zeroes without findings. Recorded packet summaries and several seals, including packets.seals.scope.computed, are also ignored. Retain fingerprint source evidence and independently compare these recorded fields against their sources; add isolated tamper controls.

4. MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:66
   The trust check covers profile-owned files but excludes executable dependencies such as CandidateInvalidation.cs. A later checkout can change its shared Invalid constant to "NOT-INVALIDATED": the drift check passes and line 72 accepts an invalidating report. Require the actual landing checkout with clean relevant sources, or verify the complete classifier dependency set against the landing.

Checks: 14,606 + 17 = 14,623. Recorded GitHub merges, issue states, and closure times match. Offline regeneration matches exactly; unmodified --check cannot query remote tags in this sandbox. VSTest is blocked by socket permissions.
```
