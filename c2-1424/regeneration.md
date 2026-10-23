# v0.24 C2: regeneration 1 on the frozen candidate (#1424)

**Gate:** C2. **Epic:** #1409. **Contract:** 1.3.2. **Candidate:** `696ab82470626164979a07792ed74932ab88d9e6`
(v0.24.0, C1 #1508, landed at `fd34f41f`). **Status: INCOMPLETE. This is the record of regeneration 1 of 2.**
Seven rows are BLOCKED (six artifacts, one gate), from five causes.

**Decision (maintainer, 2026-10-07): "Fix all 5, re-freeze, regenerate."** Fix PRs address the
five causes below. The 62 new Verification tests are registered in the C1 re-freeze PR. Then C1
freezes a second candidate, and C2 runs regeneration 2, the last one allowed. Under stopping rule
4 the re-freeze invalidates every result here for the new candidate. This record stays as the
history of regeneration 1.

**Regeneration count.** Section 9 allows "at most 2 complete runs", and section 10 asks for the
work "within the regeneration ceilings". Read literally, an incomplete run is not a complete run,
and the contract does not say how to count one. This record counts it as regeneration 1 of 2, as
the maintainer instructed. That is the stricter reading, and no contract text forbids it, so the
next regeneration is the last. The 1,500-minute ceiling applies per regeneration, so this run's
323 minutes do not carry over.

This file lives at `docs/plans/evidence/c2-1424/regeneration.md`, not `docs/plans/v0.24-c2-regeneration.md`.
After C1 landed, its classifier exempts only newly added data files under `c2-1424/` and
`adjudication-1408/`; any other new doc invalidates the candidate. For the same reason the G3
execution-2 records are in `g3-exec-2/` here, not under `g3-1135/`.

Machine-readable: `plan.json`, `ledger.json` (every run and local attempt, minutes, environment),
`results.json` (one row per inventory artifact), `claim-registry.json`,
`fresh-clone/comparison.json`, `publication/wording-scan.json`, `determinism-registry-drift.json`,
`regeneration-1-manifest.json` (SHA-256 of every file here), `archives.json` (the large files kept
off `main`, see below). `claim-registry.json` is a **draft**,
not the frozen section 8 registry: the release notes are changing, and regeneration 2 commits the
frozen registry.
Nothing was published: no package push, tag, release, asset upload, Pages deploy, or publication PR.

## How it ran

- **CI**, by `workflow_dispatch` on the temporary branch `c2-1424/candidate-696ab824` (it points
  exactly at the candidate; `main` had moved to the C1 landing). Every run's `head_sha` is the
  candidate. Five runs, **323 of 1,500 runner-minutes** (263 counting only jobs that got a runner).
- In the first `test.yml` and `tier2.yml` runs, 18 jobs could not get a runner ("The job was not
  started because it repeatedly failed to be acquired"); GitHub opened an Actions incident at
  15:14Z. The first determinism run's attempt jobs never appeared either; its run ended at 15:12Z,
  so that cause is likely but not confirmed. After githubstatus reported Actions operational
  (15:32Z), `test.yml` and `tier2.yml` were dispatched once more. Both runs of each are kept. This
  is not run-until-green: the failed jobs never started, and the rule in `plan.json` allows one new
  dispatch per lane only for that cause. The determinism protocol was **not** dispatched again
  (below).
- **Local**, macOS arm64, SDK 10.0.401 / runtime 10.0.12: a detached checkout of the candidate and
  a fresh clone at the candidate. The agent worktree was removed by the agent harness mid-run, so
  release-quality attempt 1 has no verdict; attempt 2 and later local work ran in the fresh clone.

## What matched

| Artifact | Result |
|---|---|
| `verifier-runtime-differential` | Regenerated 4 times (macOS worktree, macOS fresh clone, two ubuntu CI runs): all byte-identical to the committed reports and the amendment-1.3.2 pins (`aa1f86f9…`, `24901f21…`); 429 / 156 / 585 |
| `release-test-suites` | 13 of 13 release-critical projects pass with the manifest's counts (11 in CI, Ids and Performance locally); Compiler 12,947 with 3 skipped |
| `modeled-forms-whitelist`, `evidence-contract`, `ledger-provenance-index`, `benchmark-corpus`, `test-manifest`, `toolchain-pins` | Validators pass |
| `z3-upstream-pins`, `z3-release-binaries`, `corpus-submodules` | Re-materialized; every hash and gitlink matches its pin |
| `sdk-consumer-check` | All 5 RIDs pass in CI |
| `roundtrip-reports`, `roundtrip-baselines` | Locally all 5 projects Pass (with the .NET 8 runtime the job installs); no regression vs the baseline. CI's round-trip job skipped itself (its change filter saw no change) |
| B1 generator (`benchmark-results`, stale) | Byte-identical to the committed B1 packet, twice |
| `tier2-corpus-verification` (stale) | 428 / 51 / 30 on ubuntu and macOS; FAIL by design (30 known failures) |
| G3 execution 2 records | Committed; hashes equal C1's entry |

Fresh-clone reproduction of the deterministic subset: the differential reports, the B1 generator
outputs, and the rendered release notes are byte-identical to the worktree's.

## What differed, and what is BLOCKED

1. **Determinism protocol cannot pass on the candidate.** The registered case registry
   (`g2-1421/cases.json`) lists 411 `Calor.Verification.Tests` results; the candidate has 473. The
   62 new tests came with the S2 repairs, after G3 execution 2. The `verification-full` profile runs
   the whole project, and the decider rejects any record with unregistered tests, so no execution
   can be `DETERMINISTIC`. A run would cost up to 655 minutes and use the protocol's last execution
   (2 of 3 used). The one dispatch (run 37642265108) never started an attempt, so the candidate is
   not executed. Fixing the registry is a protocol amendment under `docs/plans/evidence/g2-1421/`,
   which invalidates the candidate. **G3 stays not closed on the candidate.**
2. **Release notes and website fail R2's wording scan (`G012`).** Line 12 of the 0.24.0 notes reads
   "It is not independently adjudicated or independently verified". R2 accepts a phrase only right
   after `not`, `no`, or `without`, so "independently verified" is flagged. The same text fails on
   the built changelog page and in `search-index.json`. The publish job's notes check and the
   website gate would refuse. Fixing it changes `CHANGELOG.md` (the candidate) or R2's scanner
   (also the candidate).
3. **The benchmark headline cannot be produced from the candidate.** B2's gate refuses with
   `B2-08`: `tests/Calor.Compiler.Tests/EvidenceContract` differs between the candidate and `main`,
   because C1 added its tests there after the candidate, and C1's files are frozen. As a diagnostic
   only, with `origin/main` set to the candidate in a scratch clone, the gate passes and writes the
   same headline twice (`db1a6fd2…`, stamp index `d4291447…`; no run timestamp in it). The notes say
   no benchmark results are published with 0.24.0, but R2's record schema still requires a
   `benchmark-results` block.
4. **Performance ratchet fails.** The nightly `performance.yml` run on the candidate (37629089909,
   not C2's) failed `Binding_MediumModule_Under500ms` on ubuntu; locally the gate's median was
   47.1 s against a 24.0 s maximum on a loaded machine. `release-quality` runs this gate before
   `publish`.

Not reproducible, so R2's byte comparison cannot hold for a rebuilt artifact:

- **Candidate packages.** Two packs of the same build differ only in zip timestamps; a build in
  another checkout path also changes the DLLs and PDBs (absolute PDB path embedded). Hashes and
  every entry hash are in `publication/candidate-packages.json`. The 118 MB of `.nupkg` bytes are
  not committed (maintainer decision "Store hashes only"). The record keeps their SHA-256 values,
  sizes, per-entry hashes, and the commands that produced them (`publication/40-pack-1.log`,
  `41-pack-2.log`, `plan.json`).
- **Release metadata** embeds the package hashes and inherits this. Both files are committed.
- **Website.** Every Next.js build gets a random build id (253 files, a different tree each time).
  Build 1 is kept as `publication/website-worktree-build-1.tar.gz` (11 MB, on the archives branch),
  and all three tree listings are committed.

Keeping the bytes would not have unblocked publication anyway. The publication workflows at the candidate
rebuild the packages and the site and compare the new hashes with the record, so they never
consume retained bytes. A reproducible build, or a publication path that publishes the retained
bytes, would each change the candidate.

## What was open for decision (decided as above)

1. Whether to return to #1423 for a new candidate that fixes items 1–3 above (protocol registry
   amendment, notes wording, the B2/C1 freshness conflict), or to record them `BLOCKED`, which makes
   the terminal outcome `MILESTONE-FAILED`. Under stopping rule 4, any candidate change invalidates
   all of this evidence, and one more complete regeneration remains (section 9 allows 2; whether
   this incomplete one counts is undefined).
2. How publication can match recorded hashes: a reproducible build or a publication path for the
   retained bytes (either is a candidate change).
3. The performance gate failure.
4. The claim registry (`claim-registry.json`, 26 claims drafted from the 0.24.0 notes). It stays a
   draft: the notes are changing, and section 8 freezes the registry at regeneration 2.
5. Every stale artifact (`tier1`, `tier2`, benchmark rows, `website-deployment`) is still classified
   stale in the frozen inventory, so A1 can only adjudicate it `BLOCKED` unless an amendment
   reclassifies it.

Other open C1 items are unchanged: child issues open, no maintenance-release path, the release-PR
row, branch-protection settings.

Everything else is retained, including raw coverage XML. Nothing relies on Actions retention.

**Where the large files are.** The 14 files over about 1 MB are not on `main` (maintainer
decision, 2026-10-08). That is 119,726,710 bytes: the large `.tar.gz` archives, four CI job logs,
and two TRX files. They live on the orphan branch `evidence/v0.24-archives` at commit `63e6a4a0`,
at the same paths. That branch is never merged. `archives.json` lists each path with its SHA-256
and size. To check one, run `git show 63e6a4a0:<path> | shasum -a 256`. Paths cited in
`results.json` and this file resolve either on `main` or through `archives.json`.

**Classifier.** `CandidateManifestTests.RangeFromTheCandidateIsNotInvalidated`, run from a fresh
checkout of the C1 landing `fd34f41f` with `CALOR_C1_CLASSIFY_TARGET` set to this PR's commit:
`NOT-INVALIDATED`. The output is in `classifier.txt` and `classifier-record.json`.
