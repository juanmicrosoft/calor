# R2 (#1410) review round 1 — Codex

**Reviewer:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` (OpenAI
Codex v0.159.2), cross-family adversarial review. **Input:** diff of the R2 branch against
`origin/main` (`585b45d6`) on stdin, before the first push. **Result:** 6 BLOCKING, 7 MAJOR,
1 MINOR. **After disposition:** 0 open BLOCKING; 2 BLOCKING items are recorded as maintainer
decisions that R2 cannot settle without a contract amendment or a repository setting.

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Required subjects omit the claim registry; unknown subjects pass | **Fixed.** The record names the #1424 claim registry `{path, sha256}`; registered claims are required subjects; any subject outside inventory, gates, and registry fails (`G008`). Controls: `UnregisteredClaimSubjectFails`, `OmittedRegisteredClaimFails`, `BlockedRegisteredClaimFails`, `ChangedClaimRegistryHashFails`; the `ValidateTerminalRecord` cross-check now uses a non-empty registry |
| 2 | BLOCKING | SBOM/provenance has no adjudicated hash; attestation runs before the byte gate | **Fixed.** New `release-metadata` surface (`--metadata-dir`); packages, metadata, and notes are verified before `attest-build-provenance`; release assets are compared byte-for-byte before reuse. Control: `ChangedReleaseMetadataFails` |
| 3 | BLOCKING | The release PR is exempted although §7 lists it | **Escalated (contract conflict).** The release PR precedes the candidate freeze and so precedes #1408; it cannot consume an identity that does not exist. The exemption wording is removed; the gate checks the PR's products (candidate version, rendered notes). Whether this meets §8 condition 7 or needs a #1407 amendment is recorded as a maintainer decision (release-gate.md §6) |
| 4 | BLOCKING | Older workflow versions (old tags, reruns) remain dispatchable with the repo-scoped NuGet key | **Partly fixed; remainder is a repository setting.** Website, benchmark, and installed-tool workflows now dispatch from `main` only and check out the candidate, so a `main`-only `github-pages` policy blocks old tags. Old `publish-nuget.yml` versions can only be denied the key by moving it to a `main`-only environment; R2 may not change settings, so release-gate.md §5 item 1 records it as an unresolved enforcement prerequisite and states that the gate covers `main`'s workflow files only |
| 5 | BLOCKING | `create-pull-request` re-applies changes onto `main` after verification | **Fixed.** Replaced with an explicit branch from the candidate: verify work tree, commit, push, `gh pr create`. No re-application after the check |
| 6 | BLOCKING | Tag checked at checkout time; a tag moved during tests is not caught | **Fixed (remaining window is a setting).** The remote tag is read through the API immediately before and after `gh release create` and must be absent or at the candidate; a tag ruleset (release-gate.md §5 item 4) closes the last window |
| 7 | MAJOR | Wording scan misses entities, split markup, package descriptions | **Fixed.** Scans as written, entity-decoded, markup replaced by space and removed, Markdown emphasis removed; `.nuspec` descriptions scanned. Controls: `ObfuscatedIndependentClaimOnTheWebsiteFails` (3), `IndependentClaimInThePackageDescriptionFails` |
| 8 | MAJOR | Manifests have no roles or candidate binding; empty publication blocks pass | **Fixed.** Exactly one `candidate-manifest` and one `raw-artifact-freeze`, each containing the candidate SHA; every publication block is schema-checked on every call. Controls: `MissingEvidenceManifestRoleFails` (2), `EvidenceManifestThatDoesNotNameTheCandidateFails`, `EmptyPublicationBlockFailsWithoutAnySurfaceOption` (5) |
| 9 | MAJOR | Benchmark regeneration cannot reproduce adjudicated bytes (timestamp) | **Accepted as fail-closed; recorded.** R2 does not weaken byte equality. Retained-bytes publication or a reproducible generator belongs to #1424/#1422 (release-gate.md §8) |
| 10 | MAJOR | Agent-results commit is unreachable | **Rejected as intended.** `llm-and-agent-results` is historical-only; there is no 0.24 publication path. The step still runs the gate and fails closed; the comment says so |
| 11 | MAJOR | Re-dispatch fails on existing metadata assets | **Fixed.** Existing assets must be byte-identical; missing ones are uploaded; different ones fail |
| 12 | MAJOR | Installed-tool matrix checks out the dispatch ref | **Fixed.** The matrix checks out the candidate; dispatch from `main` only |
| 13 | MAJOR | `--skip-duplicate` accepts a different package already on nuget.org | **Fixed.** An existing version is downloaded and compared entry by entry (only `.signature.p7s` may differ). Controls: `RegistryPackageDifferingOnlyByTheRepositorySignaturePasses`, `RegistryPackageWithOtherContentFails` |
| 14 | MINOR | Non-ancestor control used a missing object | **Fixed.** `DivergentCandidateThatIsNotAnAncestorFails` uses a real side-branch commit; the old case is kept as `CandidateThatIsAbsentFails` |

Also fixed while addressing #13: any unexpected exception (for example a corrupt `.nupkg`) is now
reported as `G000` and fails the gate, instead of escaping as a traceback.

Tests after round 1: 85 ReleaseGate cases (65 before), all passing locally: 73 verifier controls in PR 1 and 12 workflow-structure controls in PR 2. After round 1 the change was split into two stacked PRs to stay within the §9 size ceiling.
