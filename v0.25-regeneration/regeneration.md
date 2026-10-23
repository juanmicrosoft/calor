# v0.25 re-proof on the frozen candidate, and the per-family READY records (#1430)

**Candidate:** `48cae911700ceb0dfc358e00933650445d81604f` (v0.25.0, the #1576 merge; tree `45d072a9`), frozen by
`docs/plans/evidence/v0.25-candidate/candidate-manifest.json`, which landed in #1579 at `51249109`.
**Requirement:** scope 1.4.0 `acceptancePreconditions` (amendment 1.3.0, `D-A1-1408-REPROVE`): 0.24's proof,
provenance and release checks re-run on the candidate's own SHA. It mirrors 0.24's C2 regeneration 3
(`docs/plans/evidence/c2-1424/regeneration-3/`). **Independence:** reduced; Codex reviewed this work, and no
reviewer independent of the maintainer did.

**Status: the re-proof is not complete. One row is BLOCKED.** The G2 determinism protocol's 0.25 execution returned
**`INCOMPLETE`**: a slow hosted macOS runner hit its job timeout on the last attempt, so the harness cut it. No
attempt disagreed. The protocol's own guard refuses a second execution on this commit, so the row needs a maintainer
decision. Every other check passed. **F2 and F4 are BLOCKED**: F2-INTERP-09's corpus counts changed without the
amendment the T1 register requires, and F4-ITER-04 is measured and does not pass. F1, F3, F5 and F6 have READY
records that pass every S007 check except the approver URL, which is left for A1. Nothing was
published, tagged or released. No `src/` file and no frozen input changed.

Machine-readable: `results.json` (one row per re-proof check), `ledger.json` (every run, minutes, re-runs),
`plan.json`, `publication-candidates.json` (R2 publication format), `raw-artifact-freeze.json` (SHA-256 of every file
of the measurement commit), `archives.json` (14 files over about 1 MB on the orphan branch
`evidence/v0.25-archives` at `621c727a`), `t1-aggregate.json`, `classifier/`, `r2-dry-run/`, `reviews/`. Every
script the runs used is in `tools/` as `.txt`.

**Commit history.** The V025 classifier diffs every commit in the range, so each file here is added once and never
modified. Review fixes therefore rebuild the PR's commits from the same raw measurements and force-push the branch;
`reviews/` says which commits each round reviewed. No measurement was re-run for a review fix.

## Re-proof results

| Check | Result | Evidence |
|---|---|---|
| `proof-s2-witnesses` (#1413) | **PASSED** | All 7 S2 repairs' regression witnesses pass on the candidate, one run per class in a fresh clone: R-CACHE 15, R-IMPL 18 + 6, R-OBL 41, R-TEXT 24, R-QNT 7 + 3, R-NUM 25 + 9, R-OBL-RESIDUALS 21; failed 0. The S2 record validates (DispositionTests 53). No false unconditional proof is unresolved: the S2 closure is CLOSED/SUCCESS with every finding dispositioned, and the verifier-runtime differential regenerates byte-identically (`aa1f86f9…` / `24901f21…`, 0 mismatches; 429 proven, 156 assumed, 585 refuted) |
| `provenance-candidate-manifest` (#1423) | **PASSED** | The 0.25 manifest is frozen for this SHA. From a fresh clone of the landing, `V025CandidateManifestTests` and `ReleaseCandidateInvalidationTests` pass (107), including the classifier range check on this PR's head (`classifier/`) |
| `provenance-regeneration` (#1424): test suites | **PASSED** | 13 of 13 release-critical projects at the manifest's counts. test.yml run 39323630837 (ubuntu): Compiler 14,606 (3 not executed = expectedSkipped 3), Conversion 475, Enforcement 695 (1 not executed = expectedSkipped 1), Evaluation 235, Experimental 25, ILAnalysis 46, LanguageServer 532, RoundTrip.Harness 295, Semantics 55, Tasks 134, Verification 473; failed 0. Clones A and B: Ids 394, Performance 26 |
| release quality | **PASSED** | Every step of publish-nuget.yml's release-quality job in a fresh clone: negative self-tests; manifest, skip, assertion and sample-ID checks; coverage (exit 0); mutation (10 killed); performance (median 12.419 s, maximum 24 s); flake 100/100; round trip; scorecard |
| Tier 1, Tier 2 | **PASSED** | verify-phase1 succeeded. Tier 2: 428 passed / 51 expected-negative / 30 known-failure, 0 failed, 0 unexpected-pass, on ubuntu (run 39323635472) and macOS: equal to the registered expectations. The verdict is FAIL by registered design |
| round trip | **PASSED** | `run --all`: Synthetic, Synthetic2, MediatR, Serilog, FluentValidation all Pass; `roundtrip_delta.py`: no regressions vs baseline |
| byte-identical publication | **PASSED** | Packages (97 entries), release metadata and the website (255 files) are identical across two fresh clones at different paths and Linux CI (two builds) on Microsoft SDK 10.0.401. `dotnetSdkBuild`: macOS `sha256:52a28f74…` (equal to 0.24's macOS lane), Linux `sha256:a5b223b4…` (equal to the manifest's). Release notes, B1 outputs and the B2 headline are identical in A and B |
| other regenerated artifacts | **PASSED** | B1 validator 58 and generator (equal to the committed packet), B2 gate, EvidenceContractTests 324, LedgerCommitStampTests 5, ModeledFormsTests 9 in A and B; Z3 archives and 7 of 7 release assets match their pins; locked restore; submodules at the candidate's gitlinks |
| `provenance-determinism` (#1421) | **BLOCKED** | Run 39323628059, protocol 1.6.0, execution `v025-reproof-1` (the epoch's 1st of 2): **`INCOMPLETE`**, complete false. 660 AGREE-PASS, 1,644 INCOMPLETE, 0 DISAGREE, 0 AGREE-FAIL; the four determinism rows stay OPEN. Cause: osx-arm64 job 2 ran 46 minutes against its 50-minute timeout (harness cut at 45), with a verification-full median of 143.4 s against 105.9 s for osx-arm64 job 1, so attempt 15 was cut. **397 of the epoch's 1,600 runner-minutes** |
| `release-gate` (#1410) | **PASSED** (dry run) | `r2-dry-run/`: a synthetic PARTIAL_READY record passes every surface; the real state (determinism, F2 and F4 BLOCKED) fails with exactly those three G008 rows |

CI used 592 runner-minutes in all (determinism 397, test.yml 183, Tier 2 7, reproducible builds 5).

### Package bytes and the runtime

Clones A and B first produced packages that differed from Linux CI, but only in compiled DLL MVIDs and PDB ids
(`local/compare-clone-a-ci-before-runtime-10.0.13.json`). Roslyn records its host's `runtime-version` in every PDB,
and the PDB feeds the deterministic MVID. The CI runner image ships runtime 10.0.13, so its compiler ran on 10.0.13,
while the private SDK root held only 10.0.12, the runtime bundled with SDK 10.0.401. With 10.0.13 added, two new
fresh clones (`local/pub-a`, `local/pub-b`) matched CI byte for byte. The SDK fingerprint does not capture this.

## Per-family READY records

Each family has `ready/ready-record.json`, one `ready/cases/<case>.json` per registered case (65 in all, including
the interaction, doc-example and not-measured rows), and, where the scope maps a 0.24 repair, `ready/witness-runs/`.
A case record names the candidate, the case, its `requirementSha256` (recomputed, and equal to the T1 register's for
all 65), the result, every registered cell, and the measurements behind them:

- the frozen family drivers (R0, F1, F3, F4, F5), re-run unchanged on the candidate. F1, F3 and F5 give the same
  behavior on every surface as their approved evidence. F4 differs only in `--no-fallback` reporting: the refusal
  is unchanged, and the envelope now also carries counts (F6's reporting, which landed after F4 measured);
- the T1 controls, regenerated on the candidate: `controls-results.json` is byte-identical to the approved results
  (`4f28f62c…`, 133 cells);
- the W1 checks: W1GuideTranscriptTests 13 (and the transcripts regenerate byte-identically), WebsiteExampleChecker 58,
  `self-check docs` with no diagnostic, and the wording scan with 0 findings;
- the family test classes (F1 371, D1 28, F2 53, F3 91 + 33, F4 50, F5 253, F6 36) and the corpus ratchets with
  submodules (binder ratchet 8, which also regenerates byte-identically; Calor0425 ledger 14).

| Family | Status | Cases | Notes |
|---|---|---|---|
| F1 #943 | READY (approver URL for A1) | 14 of 14 passed | Witness runs R-OBL, R-OBL-RESIDUALS |
| F2 #906 | **BLOCKED** | 12 of 13 passed | F2-INTERP-09 failed (counts below); witness runs R-TEXT, R-OBL, R-OBL-RESIDUALS pass |
| F3 #847 | READY (approver URL for A1) | 8 of 8 passed | Witness runs R-OBL, R-OBL-RESIDUALS |
| F4 #1139 | **BLOCKED** | 5 of 6 passed | F4-ITER-04 failed (below) |
| F5 #1132 | READY (approver URL for A1) | 9 of 9 passed | Witness runs R-OBL, R-OBL-RESIDUALS |
| F6 #1144 | READY (approver URL for A1) | 15 of 15 passed | Each cell cites the tests and W1 transcripts for its own surface (`F6_CELLS` in `tools/make_ready.py.txt`); two readings to confirm (item 4 below) |

**F4-ITER-04.** The case expects the three FluentValidation modules (EmptyTester, NotEmptyTester,
ITestValidationContinuation) to come out `preserved` through the pinned CLI with project references. On the
candidate no surface writes preserved output for them. Single-file `calor convert` and MCP `calor_convert` refuse
each module, because the round-trip C# check cannot resolve its references (CS0246: Xunit, FactAttribute, project
types). Project `calor migrate` keeps each module's iterator accessor as `§CSHARP` member interop (path interop, no
semantic loss, no rescue). It then writes nothing, because lossless project migration refuses the whole project: 66
of 66 FluentValidation.Tests files and 139 of 139 FluentValidation files are refused, for reasons unrelated to F4.
The record says `failed`, with the conversion-level evidence.

## For the maintainer

1. **Determinism.** The epoch's only execution on this candidate is INCOMPLETE, from runner speed, not disagreement.
   Protocol 1.6.0 forbids a second execution on the same commit and requires a non-docs change after a failed one,
   while the epoch binds executions to the manifest's candidate (`determinism/determinism-second-execution-preguard.json`).
   A1 cannot accept until this row passes. Options include a protocol amendment (for example, allowing one
   re-execution of the same candidate after an INCOMPLETE harness cut, or a longer osx-arm64 timeout) or a re-freeze.
2. **F4-ITER-04** needs a decision or an amendment: the measurement as registered cannot give `preserved` on any CLI
   surface for these modules today.
3. **F2-INTERP-09 counts (F2 BLOCKED).** The register's counts were measured on F2's branch base (bound 4,820 →
   4,827, visits 35,159 → 35,160, boundaries 129 → 137, opaque 6,944 → 6,994). The candidate also carries F1, F3, F4
   and F5: bound 4,828, visits 34,371, opaque boundaries 148, opaque expressions 7,746. Incomplete diagnostics are 0
   (in-repo and conversion), and the ratchet regenerates byte-identically. The ratchet's own ledger records F2's delta
   re-measured on main after #1132 (bound +1, visits +1, boundaries +8, opaque +50). The T1 register allows the
   counts to change only by an approved, case-linked amendment, and none exists, so the case is `failed` (Codex
   round 1). An amendment that re-registers the counts on the candidate would clear it.
4. **F6 readings to confirm.** (a) Cells that enable both options on surfaces that expose fewer (mcp-both-enabled;
   cli-migrate-both-enabled; mcp-migrate-both-enabled; the precedence cells of F6-REPORT-04 and 05) record what the
   surface does with every option it does expose, and that it reports the others off. The scope note measures both
   options "on every surface that exposes them"; see F6 decisions D1 and D3. (b) SDK cells are measured on the library
   (decision D4; the T1 register lists SDK as not measured, with the library row standing for it). If either reading
   is rejected, those cells fail and F6 is BLOCKED.
5. **Effect-only default compiles.** Several `native` rows compile only with `--permissive-effects`, because the
   converted code declares no effect rows (Calor0410): F1-REFOUT-08 and X-INT-01, F2-INTERP-02 and 07, F5-ARRAY-01
   and 05 and X-INT-02. The T1 register and F5's decision count these as native. A1 should confirm.
6. **Package reproducibility** also depends on the highest installed 10.0 runtime (see above). The publish job runs on
   whatever runtime the runner image has at publication.
7. **Still for A1:** the approver URLs, the claim registry (`adjudication-1430/claim-registry.json`; the dry run used a
   synthetic one), and the benchmark-publication approval.
8. **T1 aggregate** (`t1-aggregate.json`): every case has a result record and every mapped witness run is found,
   except F2-INTERP-09 and F4-ITER-04 (failed cases: the aggregate counts only passed records). Each family still shows "family evidence was not measured on this candidate". That gap refers
   to the frozen family evidence files' own `measuredCommit`, which cannot change. The candidate measurements are in
   the `ready/` records.
9. The dispatch branch `v0.25-reproof/candidate-48cae911` can be deleted after this PR merges.
