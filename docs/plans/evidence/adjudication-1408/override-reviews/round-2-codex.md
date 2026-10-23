# 0.24.0 maintainer override — Codex review, round 2

- **Scope:** `git diff origin/main...HEAD` at the round 1 fix commit, rebased on `68c299bd2`
  (#1543 merged; the terminal record is on main with sha256 `adc24ef5…dfaddbd`, as the override
  record names).
- **Command:** as in round 1. Round 2 of 2. Raw output: `round-2-codex.raw.txt`.

## Verdict

**REQUEST-CHANGES** (2 MAJOR). Both findings say round 1's resolutions 3 and 4 were incomplete.
Codex raised no new issue on the override trigger, the identity path, the release test gates, or
the notes.

## Findings and resolutions

| # | Finding | Resolution |
|---|---|---|
| 1 | Metadata maps were built as dicts, so a wrong entry next to the right one passed. A `SHA1` checksum passed. A namespace for another commit passed when the candidate appeared only in a decoy suffix. | SBOM entries and provenance subjects are compared as sorted lists, so duplicates fail. Each SBOM file needs exactly one `SHA256` checksum, and each subject digest holds only `sha256`. The namespace must fully match `https://github.com/juanmicrosoft/calor/sbom/<40-hex>/<name>`, and its commit must be the candidate. Theory `ContradictoryMetadataFails` (duplicate SBOM entry, duplicate subject, SHA1, namespace decoy). Checked against real `generate-release-metadata.py` output for a locally packed `calor.0.24.0.nupkg`: no violations. |
| 2 | The branch check searched raw text, so commented-out or no-op identity verifier calls passed. | `ModeSwitchViolations` requires a line in each branch that starts with `python3 <its verifier>`. Comments, `:` and `echo` no-ops do not count. Each workflow must also have an exact number of verifying switches: publish-nuget 4, nextjs-gh-pages 4, verify-release 1, benchmark 0. So deleting a switch fails too. Mutation theory `ModeSwitchCheckRejectsADisabledIdentityVerifier` (`: `, `# `, `echo `). |

Locally after the fixes: `ReleaseGate` 142/142 and `check_test_quality.py` clean.
