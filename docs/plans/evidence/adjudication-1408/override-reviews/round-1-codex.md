# 0.24.0 maintainer override — Codex review, round 1

- **Scope:** `git diff origin/main...HEAD` of `milestone-0.24/maintainer-override-release` at
  commit `f67072fe1` (before the rebase onto `68c299bd2`, which merged #1543).
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` with the
  prompt and the diff on stdin.
- **Date:** 2026-10-10. Round 1 of at most 2, plus 1 verification pass.
- **Focus:** the override cannot run without both the committed record and an explicit
  `maintainer_override` input; the identity path is unchanged; the release test gates still run;
  the notes are true.

## Verdict

**REQUEST-CHANGES** (5 findings, all MAJOR). Codex found no empty-mode, skipped-step, or shell
bypass in the publishing workflows. It confirmed that override publication needs both the explicit
input and the committed record, main ancestry, a matching version, and a FAILED outcome; that the
identity verifier calls keep their arguments and order; that the three release test gates keep
their candidate checkouts and still block `publish`; that override dispatch excludes benchmarks;
and that the recorded terminal-record hash, the review-overrun and two-path explanations, and the
claim 4 and claim 13 rewordings agree with the terminal record.

## Findings and resolutions

| # | Finding | Resolution |
|---|---|---|
| 1 | Package id and version were matched by regex: a commented-out `<version>` or a renamed package passed. | `nuspec_metadata` parses the XML (namespace-agnostic, comments ignored). Each package needs exactly one root `.nuspec` whose `<metadata>` id and version are `calor`/`Calor.Sdk` and the version; a duplicate element fails. Theory `PackageWithAnotherIdOrVersionFails` (3 cases). |
| 2 | A package with no repository commit was accepted. | The real packages carry no commit (checked: `dotnet pack` gives `<repository type="git" url="…" />`), so requiring one would refuse every real release. The check now makes no commit claim. The binding to the candidate is `--expect-head` in the job that packs, plus finding 3's metadata check. Changing pack output is out of scope for a release override. |
| 3 | Metadata was checked by substring. | `check_metadata` requires exactly one `.sbom.spdx.json` and one `.provenance.json` in the generated shape. SBOM `files` and provenance `subject` must map exactly the two package names to their sha256. The provenance commit must be the candidate, and so must the SBOM namespace. Both files get the G012 scan. Test `MetadataForAnotherCommitOrPackageFails` (wrong commit, decoy subject, extra file). |
| 4 | Any mention of the override script counted as a gate, including `--select-mode`. | Mode selection no longer counts as a gate (mutation `GateCheckerRejectsModeSelectionAloneAsAGate`). New theory `EveryModeSwitchRunsTheMatchingVerifierInEachBranch`: in every mode switch that verifies, the `identity)` branch runs `verify_release_adjudication.py` and not the override script, and the reverse. Deleting an identity verifier call now fails a test. |
| 5 | The notes said publication compares package and site hashes with adjudicated ones; override publication does not. | The reproducible-builds entry now says this happens "on the gated path". It also says the 0.24.0 override release skips the comparison and checks only that packages, metadata, and notes match the release commit. Mirrored in `website/content/changelog.mdx`. |

Codex ran no tests (read-only). Locally after the fixes: `ReleaseGate` 135/135, website
`public-claims.spec.ts` 16/16, `check_test_quality.py` clean, and the G012 scan is clean on the
0.24.0 notes and 208 built-site files.
