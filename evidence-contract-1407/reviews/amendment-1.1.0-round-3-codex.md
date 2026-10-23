# Amendment 1.1.0 — Codex review, round 3 (final)

**Reviewer:** Codex (`codex exec -s read-only --ephemeral`, `model_reasoning_effort=high`), cross-family.
**Input:** the full amendment diff against `origin/main` after round 2 (`307e3a83`), with the round
1 and 2 records. Asked to verify the round 2 fixes and report only concrete defects in the amendment.
**Result:** 0 BLOCKING, 0 MAJOR, 2 MINOR. The round 2 fixes were not disputed.

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MINOR | `GlobRegex` anchored with `$`, which in .NET also matches before a final newline, so `website/content/cli/compile.mdx\n` (a legal git file name) passed `D003`. | **Fixed.** Anchors are now `\A` and `\z`. Trailing-newline controls added to `DocsDeployGlobsMatchWholePaths` and `NonDocumentationPathFails`. |
| 2 | MINOR | `PendingUpdateChangesNoArtifact` compared artifacts before validation with an empty change, so a validator that applied pending edits could still pass. | **Fixed.** The control now uses a pending update that would reclassify the stale `benchmark-provenance` as authoritative and resolve its defect, compares the whole artifact after validation, and requires an evidence row citing it to still fail `E005`. |

Open after three rounds: round 1 finding 4 (the `reviewedInPr: 0` and `#PRNUM` placeholders),
deferred to the maintainer by instruction; they are replaced with the PR number before merge.

Test count after round 3: EvidenceContractTests 166 → 256 (+90); `eng/test-manifest.json`
Calor.Compiler.Tests 12272 → 12362.
