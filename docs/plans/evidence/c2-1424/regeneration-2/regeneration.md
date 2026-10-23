# v0.24 C2: regeneration 2 on the re-frozen candidate (#1424)

**Gate:** C2. **Epic:** #1409. **Contract:** 1.4.0. **Candidate:** `5e52d8abb0881d0337e2ac12c6a031b2c2d2a14d`
(v0.24.0; the #1533 merge). C1 re-froze it in #1534, which landed at `7eca4868`. This is regeneration 2 of 2, the last
allowed. Regeneration 1, on `696ab824`, is history in `../regeneration.md`; under stopping rule 4 none of its
results carry over.

**Status: one FAILED row.** All 34 inventory artifacts were regenerated, validated, or (for
historical-only ones) left as history. Every publication candidate is byte-reproducible. The
determinism protocol, however, returned `NON-DETERMINISTIC`. The cause is a harness deadline cut on
one slow Windows runner; no two observed values disagreed. The protocol has no execution slot left.
That row is the maintainer's decision. Nothing was published.

Machine-readable: `plan.json`, `ledger.json`, `results.json` (one row per inventory artifact plus
the gates), `publication-candidates.json` (the hashes in R2's terminal-record format),
`claim-registry.json` (28 claims), `raw-artifact-freeze.json` (SHA-256 of every file here),
`archives.json` (the 12 files over about 1 MB, which live on the orphan branch
`evidence/v0.24-archives` at `e1c65e27`).

## How it ran

- **CI.** 4 `workflow_dispatch` runs on the temporary branch `c2-1424/candidate-5e52d8ab`, which
  points exactly at the candidate. Every run's `head_sha` is the candidate. The lanes are
  determinism protocol, `test.yml`, `tier2.yml`, and `reproducible-builds.yml`. They used **573 of
  1,500** regeneration runner-minutes.
- **Local.** Two fresh clones of the candidate at different paths, on macOS arm64. Each had an
  isolated `HOME`, `CALOR_HOME`, and caches, and used Microsoft's SDK 10.0.401 build (runtimes
  10.0.12 and 8.0.31). Clone A also ran every `release-quality` step, Tier 2, the Z3 mirror check,
  and the submodules.
- Two local steps ran twice, and both attempts of each are recorded. The coverage step's first
  attempt failed one test because the private dotnet root's path contains "calor". The first Z3
  mirror download had no GitHub login. Details are in `ledger.json` deviations.

## What matched

| Surface | Result |
|---|---|
| Packages | `calor.0.24.0.nupkg` `d59aea56…`, `Calor.Sdk.0.24.0.nupkg` `157fc9ca…`. Identical in clone A, clone B, and two Linux CI builds: 97 entries, contents and timestamps |
| Release metadata | provenance `8e4be880…`, SBOM `ebbd1f2b…`. Identical everywhere |
| Website | R2 tree digest `13de7f6e…` (253 files). Identical everywhere |
| Release notes | R2 digest `05b8c219…`. R2 wording scan clean on notes, website, packages, and headline |
| Benchmark headline | The B2 gate passes against the real `main`. Headline `6af1adef…` and stamp index `91d63dcd…`, identical in A and B |
| Differential reports | `aa1f86f9…` / `24901f21…` in A, B, and ubuntu CI. Equal to the committed reports |
| Test suites | 13 of 13 release-critical projects at the manifest's counts. Compiler 13,036, 3 skipped |
| Release quality | Coverage, mutation, performance (median 12.3 s against a 24 s maximum), flake 100/100, round trip 5/5, scorecard |
| Pins | Z3 archives and the 7 mirror assets, locked restore, and corpus gitlinks all verify |
| Tier 2 | 428 / 51 / 30 on ubuntu and macOS. The verdict is FAIL by registered design: 30 known failures |

The hashes above come from Microsoft's SDK 10.0.401 build. The publish job must use that build:
Homebrew's source-built 10.0.401 packs different bytes (#1526). The site still downloads Google
Fonts at build time.

## FAILED: determinism protocol on the candidate

Run 37826943308 used protocol 1.4.0 and the 3rd and last execution slot. Its verdict is
**`NON-DETERMINISTIC`**, with `complete: false`:

- 660 `AGREE-PASS`, 470 `DISAGREE`, 1,174 `INCOMPLETE`.
- 149 attempts completed and 1 is invalid.
- All four determinism rows remain `OPEN`.

**Cause.** win-x64 job 1's runner was slow. Its verification-full median was 233 s, against
149.5 s for win-x64 job 2 on the same image and CPU count. Its 15th attempt did not fit before the
harness's own deadline inside the 75-minute job, so the harness cut that invocation. The cut
attempt filled every case it had not observed with `Timeout`. The frozen rule compares values from
an invalid attempt, so every such case reads `DISAGREE`. The protocol records this as
`harnessCutInconsistency`; the decider was not changed after execution 1.

All 470 `DISAGREE` cases are that one fill against identical observed values in all 149 completed
attempts, on all five platforms. The 1,174 `INCOMPLETE` cases are the 1,170 cells, the 3 generated
artifacts, and the `oracle-isolated` invocation. The cut invocation had already recorded every cell
and all 3 artifact hashes, and each equals every other attempt's. But a value from an invalid
attempt never counts toward agreement, and the cut attempt never ran `oracle-isolated`. Under the
protocol this establishes nothing: only `DETERMINISTIC` does.

**Erratum.** Regeneration 1's CI records (`../ci/run-*.json`, `../ledger.json`) recorded the
Runner Image Provisioner version as the image version. They stay unchanged as history.
Regeneration 2 parses the `Runner Image` block; for example, win-x64's image is `20260925.250.1`,
with provisioner `20260901.588`.

**Consequence.** G3 is not closed on the candidate. No execution slot remains (3 of 3 used), and C2
did not re-run anything. Determinism-compute stands at 1,207 of 2,000. The decision is the
maintainer's.

## R2 dry run and classifier

See `r2-dry-run/` and `classifier.txt` (added with the follow-up commit).

## For A1

1. The determinism row (above).
2. Tier 2's FAIL verdict is registered. The 30 known failures stay failures.
3. Byte reproducibility holds only with Microsoft's SDK 10.0.401 build. The site's fonts are
   fetched at build time.
4. The headline bytes assume `main` has no published headline at publication.
5. The open C1 items are unchanged: child issues, maintenance path, release-PR row, branch
   protection, and the classifier as a non-tripwire.
