# v0.24 C2: regeneration 3 on the re-frozen candidate (#1424)

**Gate:** C2. **Epic:** #1409. **Contract:** 1.5.0. **Candidate:** `04e61f188e85a710a91242e1ddbf35fcd50e329e`
(v0.24.0; the #1537 merge). C1 re-froze it in #1539, which landed at `b04e963f`. Regeneration 3 is
the last one allowed (contract 1.5.0 exception).

**Status: complete. No row is BLOCKED or FAILED.** The determinism protocol (1.5.0, 4th and last
execution) returned **`DETERMINISTIC`**: 2,304 `AGREE-PASS`, 150 of 150 attempts completed, and all
four determinism rows resolved. Packages, release metadata, and the website are byte-identical
across two fresh clones and Linux CI. The benchmark headline, its stamp entry, and the rendered
notes are byte-identical across the two clones. The differential reports are byte-identical across
the two clones and ubuntu CI. Tier 2's FAIL verdict is the registered design: 30 known failures.
Nothing was published.

## History in this directory

| Directory | Candidate | Status |
|---|---|---|
| `../` (regeneration 1, PR #1517, merged as history) | `696ab824` | Incomplete; 7 BLOCKED rows from 5 causes; superseded |
| `../regeneration-2/` (PR #1535) | `5e52d8ab` | One FAILED row: determinism `NON-DETERMINISTIC` after a harness cut on a slow win-x64 runner. **Superseded and never merged.** Its 211 files are kept here unchanged as history; PR #1535 was closed without merging |
| this directory | `04e61f18` | Complete |

Under stopping rule 4, no result of regeneration 1 or 2 carries over to this candidate.

Machine-readable: `plan.json`, `ledger.json`, `results.json` (one row per inventory artifact plus
the determinism gate), `publication-candidates.json` (the hashes in R2's terminal-record format),
`claim-registry.json` (28 claims; it is frozen when this PR merges, so the maintainer reviews it
first), `raw-artifact-freeze.json` (SHA-256 of every file here), and `archives.json`. The 11
files over about 1 MB live on the orphan branch `evidence/v0.24-archives` at `ca4e8df1`.

## How it ran

- **CI.** 4 `workflow_dispatch` runs on `c2-1424/candidate-04e61f18`, which points exactly at the
  candidate. Every run's `head_sha` is the candidate. They used **583 of 1,500** runner-minutes.
  Before the dispatch, the candidate's own plan guard was run locally against the live run
  history and accepted the execution.
- **Local.** Two fresh clones at different paths, on macOS arm64. Each had an isolated `HOME`,
  `CALOR_HOME`, and caches, and used Microsoft's SDK 10.0.401 build. Clone A ran every
  `release-quality` step, Tier 2, the Z3 mirror check, and the submodules.
- The coverage step used Homebrew's dotnet host from the start, as planned. Regeneration 2 showed
  that a test rejects BCL paths containing "calor", and the private root's path contains it.

## Results

| Surface | Result |
|---|---|
| Determinism protocol | Run 37965084091, `DETERMINISTIC`, complete. 2,304 `AGREE-PASS`, 0 `DISAGREE`, 0 `INCOMPLETE`; 150 of 150 attempts. 401 runner-minutes. Determinism-compute is now 1,608 of 3,200 |
| Packages | `calor.0.24.0.nupkg` `d59aea56…`, `Calor.Sdk.0.24.0.nupkg` `157fc9ca…`. Identical in A, B, and two Linux CI builds |
| Release metadata | provenance `1f00791f…`, SBOM `eb2abeca…`. Identical everywhere |
| Website | R2 tree digest `e684dcab…` (253 files). Identical everywhere |
| Release notes | R2 digest `05b8c219…`. R2 wording scan clean on notes, website, packages, and headline |
| Benchmark headline | B2 gate passes against the real `main`. Headline `312912fd…`, stamp index `c943954b…`, identical in A and B |
| Differential reports | `aa1f86f9…` / `24901f21…` in A, B, and ubuntu CI. Equal to the committed reports |
| Test suites | 13 of 13 release-critical projects at the manifest's counts. Compiler 13,038, 3 skipped |
| Release quality | Coverage, mutation, performance (median 12.9 s against a 24 s maximum), flake 100/100, round trip 5/5, scorecard |
| Pins | Z3 archives and the 7 mirror assets, locked restore, and corpus gitlinks all verify |
| Tier 2 | 428 / 51 / 30 on ubuntu and macOS. The verdict is FAIL by registered design |

One Windows runner was again slow: win-x64 job 2's verification-full median was 229.6 s, against
135.2 s for job 1. Protocol 1.5.0's 110-minute Windows timeout covered it, and no invocation was
cut.

## R2 dry run and classifier

See `r2-dry-run/` and `classifier.txt` (added in the follow-up commit).

## For A1

1. The claim registry (28 claims) must be reviewed by the maintainer before this PR merges.
2. Tier 2's FAIL verdict is registered. The 30 known failures stay failures.
3. Byte reproducibility holds only with Microsoft's SDK 10.0.401 build. The site fetches Google
   Fonts at build time.
4. The headline bytes depend only on the candidate, which has no published headline. If `main`
   later carries different headline or stamp bytes, a publish-time run refuses (`B2-07`); it never
   writes other bytes.
5. The determinism result bounds rare non-determinism (30 attempts per environment); it cannot
   exclude it.
6. The open C1 items are unchanged: child issues, maintenance path, release-PR row, branch
   protection, and the classifier as a non-tripwire.
7. Enforcement's one manifest skip happened in CI (694 of 695 executed) but not in the local run
   (695 of 695).
