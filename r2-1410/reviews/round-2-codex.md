# R2 (#1410) review round 2 — Codex

**Reviewer:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, cross-family
adversarial review. **Input:** combined diff of #1474 and #1475 (head `milestone-0.24/r2-1410-release-gating-workflows`)
against `origin/main` on stdin, with round 1's record available. **Result:** 2 BLOCKING, 5 MAJOR,
1 MINOR. **After disposition:** 0 open BLOCKING.

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Remote tag lookup treats every API failure as "absent" | **Fixed (#1475).** `git/matching-refs` lists the tag; only an empty exact match counts as absent; any API failure stops the job (`inherit_errexit`) |
| 2 | BLOCKING | Pre-push 404 then `--skip-duplicate` 409 lets a different package stand | **Fixed (#1475).** After the push the job polls nuget.org until both packages are served and compares them entry by entry (`--registry-dir`) before the GitHub release is created; a timeout fails the job |
| 3 | MAJOR | Manifest candidate binding was a substring search | **Fixed (#1474).** The manifest's top-level `candidate` (SHA or `{commit}`) must equal the candidate. Controls: `EvidenceManifestBindingAnotherCandidateFails`, `EvidenceManifestBindingTheCandidateAsAnObjectPasses` |
| 4 | MAJOR | Audit could check `v<version>` while the event release is tagged otherwise | **Fixed.** New `--release-tag` (and `--release-title`) must equal `v<version>`; the audit and every release-body check pass the actual tag and title. Control: `NonCanonicalReleaseTagOrTitleFails` (2) |
| 5 | MAJOR | JSON `\u` escapes and hidden-markup negation evade the wording scan | **Fixed (#1474).** JSON string values are scanned decoded; a negation that is not literal in the source fails. Controls: `JsonEscapedIndependentClaimInABenchmarkFileFails`, `NegationSuppliedOnlyByHiddenMarkupFails` |
| 6 | MAJOR | Release titles are not verified | **Fixed.** Title must be exactly `v<version>`, checked on reuse, downstream, and in the audit |
| 7 | MAJOR | Benchmark retry after a pushed branch fails non-fast-forward | **Fixed (#1475).** An existing remote branch is reused only if its parent is the candidate and its tree equals the verified tree; an open PR for the branch is reused |
| 8 | MINOR | Divergent-candidate control also broke manifest binding | **Fixed (#1474).** The fixture binds manifests to the recorded (side-branch) candidate, so ancestry is the only invalid fact |

Tests after round 2: 79 verifier controls in #1474 (+6) and 12 workflow-structure controls in
#1475; 91 ReleaseGate cases in total, all passing locally.
