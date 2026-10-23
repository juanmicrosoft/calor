# v0.25 re-proof on the re-frozen candidate `d1d7afde`, and the per-family READY records (#1430)

**Candidate:** `d1d7afde672691613f7bb34d253e63401ee423ac` (v0.25.0, the #1610 merge; tree `d00b3c43`). It was
re-frozen by `docs/plans/evidence/v0.25-candidate/candidate-manifest.json`, landed in #1611 at `8f815f9e`, and it
supersedes `48cae911`. The re-proof on `48cae911` is in `../regeneration.md` (#1581); none of its results carry
over. **Requirement:** scope 1.5.0 `acceptancePreconditions`: 0.24's proof, provenance and release checks re-run on
the candidate's own SHA. **Independence:** reduced. Codex reviewed this work, and no reviewer independent of the
maintainer did.

**Status: every re-proof check passed.** The G2 determinism protocol (1.7.0) returned **`DETERMINISTIC`** on its
first window execution: 2,304 AGREE-PASS, 0 DISAGREE, 0 INCOMPLETE. No second execution ran. All six families have
READY records that pass every S007 check except the approver URL, which is left for A1. F4-ITER-04 is recorded as
`skipped`, as scope 1.5.0 requires. Nothing was published, tagged or released, and no `src/` file or frozen input
changed.

Machine-readable files: `results.json` (one row per re-proof check), `ledger.json` (runs and minutes), `plan.json`,
`publication-candidates.json` (R2 publication format), and `raw-artifact-freeze.json` (SHA-256 of every file in the
measurement commit). `archives.json` indexes the files over about 1 MB, which live on the orphan branch
`evidence/v0.25-archives`. Also here: `t1-aggregate.json`, `classifier/`, `r2-dry-run/` and `reviews/`. Every script
is in `tools/` as `.txt`.

**Commit history.** The V025 classifier diffs every commit in the range, so each file here is added once and never
modified. Review fixes rebuild the PR's commits from the same raw measurements and force-push the branch.

## Re-proof results

| Check | Result | Evidence |
|---|---|---|
| `proof-s2-witnesses` (#1413) | **PASSED** | All 7 S2 repairs' regression witnesses pass on the candidate, one run per class in a fresh clone: R-CACHE 15, R-IMPL 18 + 6, R-OBL 41, R-TEXT 24, R-QNT 7 + 3, R-NUM 25 + 9, R-OBL-RESIDUALS 21. None failed. The S2 record validates (DispositionTests 53). No false unconditional proof is unresolved: the S2 closure is CLOSED/SUCCESS with every finding dispositioned, and the verifier-runtime differential regenerates byte-identically to the committed reports (0 mismatches) |
| `provenance-candidate-manifest` (#1423) | **PASSED** | The re-frozen manifest names this SHA. From a fresh clone of the landing, `V025CandidateManifestTests` and `ReleaseCandidateInvalidationTests` pass, including the classifier range check on this PR's head (`classifier/`) |
| `provenance-regeneration` (#1424): test suites | **PASSED** | 13 of 13 release-critical projects are at the manifest's counts. test.yml run 39516188077 (ubuntu): Compiler 14,696 (3 not executed, equal to expectedSkipped), Conversion 475, Enforcement 695 (1 not executed, equal to expectedSkipped), Evaluation 235, Experimental 25, ILAnalysis 46, LanguageServer 532, RoundTrip.Harness 295, Semantics 55, Tasks 134, Verification 473; none failed. Clones A and B: Ids 394, Performance 26 |
| release quality | **PASSED** | Every step of publish-nuget.yml's release-quality job ran in a fresh clone: negative self-tests; manifest, skip, assertion and sample-ID checks; coverage; mutation; performance (median 13.549 s, maximum 24 s); flake 100/100; round trip; scorecard |
| Tier 1, Tier 2 | **PASSED** | verify-phase1 succeeded. Tier 2 gave 428 passed / 51 expected-negative / 30 known-failure, 0 failed, 0 unexpected-pass, on ubuntu (run 39516190367) and on macOS. Those equal the registered expectations; the verdict is FAIL by registered design |
| round trip | **PASSED** | `run --all`: Synthetic, Synthetic2, MediatR, Serilog and FluentValidation all pass; `roundtrip_delta.py` reports no regressions vs baseline |
| byte-identical publication | **PASSED** | Packages (97 entries), release metadata and the website (255 files) are identical across two fresh clones at different paths and Linux CI (run 39516192814, two builds). Packages and metadata were built with Microsoft SDK 10.0.401, with the compiler host on runtime 10.0.12, the SDK's own (`check-runtime`; the release jobs prune 10.0.13). `dotnetSdkBuild`: macOS `sha256:52a28f74…`, Linux `sha256:a5b223b4…` (equal to the manifest's). `dotnetRuntime` is 10.0.12 on both. The website is built by Node and runs no .NET. The CI website job records the runner image's .NET (10.0.402, runtime 10.0.13) with Node 20.20.2, and its tree equals the local one. Release notes, B1 outputs and the B2 headline are identical in A and B |
| other regenerated artifacts | **PASSED** | B1 validator 58 and generator (equal to the committed packet), the B2 gate, EvidenceContractTests 324, LedgerCommitStampTests 5 and ModeledFormsTests 9, all in A and B. Z3 archives and 7 of 7 release assets match their pins; locked restore works; submodules are at the candidate's gitlinks |
| `provenance-determinism` (#1421) | **PASSED** | Run 39516185697 (run 8), protocol 1.7.0, execution `v025-reproof-2`, the window's 1st of 2: **`DETERMINISTIC`**, complete. 2,304 AGREE-PASS; 0 DISAGREE, AGREE-FAIL or INCOMPLETE; no harness cut; all four determinism rows RESOLVED. The osx-arm64 jobs ran 36 minutes against their 70-minute timeout. It used **353 runner-minutes; the epoch is at 750 of 2,100** |
| `release-gate` (#1410) | **PASSED** (dry run) | `r2-dry-run/`: a synthetic ACCEPTED record passes every surface. The same record with the determinism row BLOCKED (a synthetic negative control) fails with exactly that G008 row |

CI used 535 runner-minutes in all: determinism 353, test.yml 172, Tier 2 5 and reproducible builds 5.

## Per-family READY records

Each family has `ready/d1d7afde/ready-record.json`, one `ready/d1d7afde/cases/<case>.json` per registered case (65
in all), and, where the scope maps a 0.24 repair, `ready/d1d7afde/witness-runs/`. They sit under a new path because
`ready/` files are never rewritten, and T1 1.0.3 pins the `48cae911` F2-INTERP-09 record. Each case record names the
candidate, the case, its `requirementSha256` (recomputed, and equal to the T1 register's for all 65), the result,
every registered cell, the measurements, and the scope 1.5.0 `approvedReadings` it relies on. The measurements are:

- the frozen family drivers (R0, F1, F3, F4, F5), re-run unchanged. F1, F3 and F5 behave the same on every surface
  as their approved evidence. F4 differs only in `--no-fallback` reporting: the refusal is unchanged, and the
  envelope also carries counts (F6's reporting);
- the T1 controls, regenerated on the candidate: `controls-results.json` is byte-identical to the approved results
  (`4f28f62c…`, 133 cells);
- the W1 checks: W1GuideTranscriptTests 13 (the transcripts regenerate byte-identically), WebsiteExampleChecker 58,
  `self-check docs` with no diagnostic, and the wording scan with 0 findings;
- the family test classes (F1 371, D1 28, F2 53, F3 91 + 33, F4 50, F5 253, F6 36), the corpus ratchets with
  submodules (the binder ratchet 8, which also regenerates byte-identically; the Calor0425 ledger 14), and the F6
  project-aggregate measurement (`measure/f6-aggregates/`, all consistent).

| Family | Status | Cases | Notes |
|---|---|---|---|
| F1 #943 | READY (approver URL for A1) | 14 of 14 passed | Witness runs R-OBL, R-OBL-RESIDUALS; F1-REFOUT-08 and X-INT-01 cite R-PERMISSIVE-EFFECTS-NATIVE |
| F2 #906 | READY (approver URL for A1) | 13 of 13 passed | Witness runs R-TEXT, R-OBL, R-OBL-RESIDUALS. F2-INTERP-09's candidate counts (bound 4,828, visits 34,371, boundaries 148, opaque 7,746, incomplete 0) equal the counts T1 1.0.3 re-registered |
| F3 #847 | READY (approver URL for A1) | 8 of 8 passed | Witness runs R-OBL, R-OBL-RESIDUALS |
| F4 #1139 | READY (approver URL for A1) | 5 passed, F4-ITER-04 skipped | F4-ITER-04 is not-measured by scope 1.5.0 (`D-F4-ITER-04-NOT-MEASURED`). It is covered by F4-ITER-01, 02, 03, X-INT-03 and DOC-F4-01, all passed here |
| F5 #1132 | READY (approver URL for A1) | 9 of 9 passed | Witness runs R-OBL, R-OBL-RESIDUALS; F5-ARRAY-01, 05 and X-INT-02 cite R-PERMISSIVE-EFFECTS-NATIVE |
| F6 #1144 | READY (approver URL for A1) | 15 of 15 passed | Each cell cites the tests, W1 transcripts or aggregate measurement for its own surface; cells on surfaces with fewer options, and the SDK cells, cite R-F6-FEWER-OPTIONS |

## For the maintainer

1. **Still for A1:** the approver URLs (@juanmicrosoft's pull-request review), the claim registry
   (`adjudication-1430/claim-registry.json`; the dry run used a synthetic one), and the benchmark-publication
   approval.
2. **T1 aggregate** (`t1-aggregate.json`): every passed case has a result record, and every mapped witness run is
   found. The aggregate lists F4-ITER-04 among the cases without one, because the frozen aggregator (T1
   `reproduce/aggregate.py`) counts only `passed` records. Its `skipped` record, which scope 1.5.0 requires, is
   `v0.25-f4-1139/ready/d1d7afde/cases/F4-ITER-04.json`; `t1-aggregate-annotation.json` records this beside the
   aggregator's unchanged output. Each family still shows "family evidence was not measured on this candidate". That refers
   to the frozen family evidence files' own `measuredCommit`, which cannot change. The candidate measurements are in
   the `ready/d1d7afde/` records.
3. **Known residuals of the freeze:** #1578's two items and `GATE-PREVIOUSAFTER-BOOL` (`v0.25-candidate/README.md`).
   Nothing here touches them.
4. The dispatch branch `v0.25-reproof/candidate-d1d7afde` can be deleted after this PR merges. So can
   `v0.25-reproof/candidate-48cae911`.
