# 0.25 A1 sign-off packet (#1430)

**Candidate:** `d1d7afde672691613f7bb34d253e63401ee423ac` (v0.25.0), frozen by
`../v0.25-candidate/candidate-manifest.json` (landing `8f815f9e`, #1611). **Re-proof:** #1615 (`a176f49f`),
`../v0.25-regeneration/d1d7afde/regeneration.md`. **Proposed outcome: `ACCEPTED`** (decision
`D-A1-OUTCOME-ACCEPTED`). **Independence:** reduced. Codex reviewed this packet; no reviewer independent of the
maintainer did.

## What the maintainer signs

Approving this PR on GitHub (an **Approve** review by @juanmicrosoft) and then merging it adopts:

1. the outcome `ACCEPTED` for #1430 on candidate `d1d7afde`;
2. one row per family (6 `READY`), per re-proof check (5 `PASSED`), per release gap (3 `CLOSED`) and per claim
   (57 `BOUNDED`), all with `independence = reduced-maintainer-adjudicated`;
3. the claim registry (`claim-registry.json`, 57 claims, approved as `D-A1-CLAIMS-APPROVED`);
4. the release-state ruling `D-A1-RELEASE-STATE-FILES` (`decisions.json`);
5. **no** benchmark publication: `publication.benchmark-results.approved` is `false`, so `G015` refuses any
   benchmark publication under this identity (0.25.0 publishes no new benchmark results, notes line 40).

The record claims no review independent of the maintainer and Codex, and nothing it authorizes may claim one.

**How the approval reaches the record.** The R2 gate (`scripts/verify_release_adjudication.py`, §10) checks no
approval in the terminal record. It requires each family's READY record to carry
`approver.approvalUrl = https://github.com/juanmicrosoft/calor/pull/<N>#pullrequestreview-<M>`. The re-proof READY
records have `null` there, and `ready/` files are never rewritten. So after your approval, and after you close the
nine blocker issues, one mechanical commit adds eight new files:

- six `<family>/ready/d1d7afde/a1/ready-record.json`. Each is the re-proof record with the `approver` block updated (URL and note), plus an
  `a1.derivedFrom` hash of the record it copies;
- `closures.json`: a `gh` snapshot of the nine blocker issues, all `CLOSED`. Condition C7 cites it, and the
  generator refuses to write the final record without it;
- `terminal-record.json`, which cites the six READY records by path and SHA-256.

`tools/make_a1_records.py.txt` writes them (`--snapshot-closures` writes `closures.json`), and `--check`
reproduces everything except that snapshot. Until then the PR holds
`draft/terminal-record.pending.json`: the same record with `PENDING-A1-APPROVAL` in the approval-dependent fields.
Every file in this PR is newly added, so the V025 classifier exempts it (`reviews/classifier.txt`).

## Decisions (`decisions.json`, @juanmicrosoft, 2026-10-21)

| Id | Decision (verbatim) | Effect |
|---|---|---|
| `D-A1-RELEASE-STATE-FILES` | "Ruling + separate PR." | `eng/release-adjudication-map.json` and four tests that assert the pre-A1 state are release-state files, not candidate inputs. This PR changes neither. A separate gate-state PR (branch `milestone-0.25/a1-1430-gate-state`) sets `0.25.x` to `adjudicated` and updates the four tests after this PR merges |
| `D-A1-OUTCOME-ACCEPTED` | "Close at sign-off: ACCEPTED." | Outcome `ACCEPTED`; the nine blocker issues are closed right after approval |
| `D-A1-CLAIMS-APPROVED` | "Approve all 57." | The claim registry as drafted. After Codex round 1, four claims' wording was corrected without changing any id or the count: `website-example-fixes` now also states notes lines 363-364 (a default compile does not run the obligation solver), and `by-ref-contract-cases-hold`, `no-new-benchmark-results` and `website-examples-checked-in-ci` were reworded to match the notes exactly. Please confirm these when you approve |

## Rows

| Subject | Outcome | Basis |
|---|---|---|
| `family:F1` (#943) | READY | 14 of 14 cases passed on `d1d7afde` |
| `family:F2` (#906) | READY | 13 of 13 passed; F2-INTERP-09 counts equal T1 1.0.3's re-registration |
| `family:F3` (#847) | READY | 8 of 8 passed |
| `family:F4` (#1139) | READY | 5 passed; F4-ITER-04 `skipped`, as amendment 1.5.0 requires |
| `family:F5` (#1132) | READY | 9 of 9 passed |
| `family:F6` (#1144) | READY | 15 of 15 passed |
| `reproof:proof-s2-witnesses` (#1413) | PASSED | All 7 S2 repairs' witnesses pass; differential byte-identical |
| `reproof:provenance-candidate-manifest` (#1423) | PASSED | Re-frozen manifest names `d1d7afde`; manifest tests pass |
| `reproof:provenance-regeneration` (#1424) | PASSED | 13 of 13 release-critical projects at the manifest's counts; release quality, Tier 1/2, round trip, byte-identical packages, metadata and website |
| `reproof:provenance-determinism` (#1421) | PASSED | Run 39516185697, protocol 1.7.0: `DETERMINISTIC`, 2,304 AGREE-PASS, 0 DISAGREE |
| `reproof:release-gate` (#1410) | PASSED | Re-proof dry run; this PR's own R2 run below |
| `gap:#1545`, `gap:#1546`, `gap:#1547` | CLOSED | Closed 2026-10-19 before the freeze; live credential check `CLOSED` |
| 57 × `claim:*` | BOUNDED | See below |

## Issues to close right after approval (D-A1-OUTCOME-ACCEPTED)

Family READY records (the `a1/` copy, carrying your approval URL, is added by the post-approval commit):

| Issue | READY record |
|---|---|
| #943 (F1) | [`v0.25-f1-943/ready/d1d7afde/ready-record.json`](../v0.25-f1-943/ready/d1d7afde/ready-record.json), then `ready/d1d7afde/a1/ready-record.json` |
| #906 (F2) | [`v0.25-f2-906/ready/d1d7afde/ready-record.json`](../v0.25-f2-906/ready/d1d7afde/ready-record.json), then `.../a1/ready-record.json` |
| #847 (F3) | [`v0.25-f3-847/ready/d1d7afde/ready-record.json`](../v0.25-f3-847/ready/d1d7afde/ready-record.json), then `.../a1/ready-record.json` |
| #1139 (F4) | [`v0.25-f4-1139/ready/d1d7afde/ready-record.json`](../v0.25-f4-1139/ready/d1d7afde/ready-record.json), then `.../a1/ready-record.json` |
| #1132 (F5) | [`v0.25-f5-1132/ready/d1d7afde/ready-record.json`](../v0.25-f5-1132/ready/d1d7afde/ready-record.json), then `.../a1/ready-record.json` |
| #1144 (F6) | [`v0.25-f6-1144/ready/d1d7afde/ready-record.json`](../v0.25-f6-1144/ready/d1d7afde/ready-record.json), then `.../a1/ready-record.json` |

The supporting issues have no READY record of their own; the scope defines S007 READY records only for families.
Their candidate evidence is in every family READY record:

| Issue | Evidence on `d1d7afde` |
|---|---|
| #1143 (W0) | `w1Checks.websiteExampleChecker` "58 passed" and `selfCheckDocs` 0 diagnostics in each READY record above; [`../v0.25-w0-1143/README.md`](../v0.25-w0-1143/README.md) |
| #1428 (T1) | `t1Controls` (controls-results.json byte-identical to the approved `4f28f62c…`, 133 cells) in each READY record; T1 register 1.0.3 approval seal recomputes in the R2 run |
| #1429 (W1) | `w1Checks.transcripts` (W1GuideTranscriptTests 13, byte-identical regeneration) and `wordingScan` 0 findings in each READY record; W1 records [`../v0.25-w1-1429/records.json`](../v0.25-w1-1429/records.json) |

## Claims (57, approved as `D-A1-CLAIMS-APPROVED`)

Lines are in the rendered notes (`../v0.25-regeneration/d1d7afde/local/clone-a/release-notes.md`, R2 digest
`c20087ec…`). Each row in the record carries the claim text, evidence by SHA-256, and its limitations. Claims
`json-verbose-stdout-single-document`, `assess-coverage-by-ref-descriptions`, `expression-bodied-compound-assignment`,
`local-shadows-function-in-calls`, `double-literals-kept`, `partial-properties-preserved`, `named-refinement-arithmetic`,
`decimal-arithmetic-type` and `proof-refers-to-local` rest on the candidate's green test suites only; their rows say so.

| # | Claim | Lines | In plain language |
|---|---|---|---|
| 1 | `freeze-notes-before-a1` | 10-12 | The release notes were fixed when the candidate was frozen and cannot change. |
| 2 | `freeze-nothing-released` | 14-15 | Nothing of 0.25 had been published at the freeze. |
| 3 | `freeze-no-family-ready` | 16-18, 112, 156 | At the freeze no family was marked ready; readiness is decided after the freeze, by this review. |
| 4 | `reproof-required-no-024-reuse` | 19-21 | 0.24's failed review means 0.25 had to redo 0.24's checks on its own code. |
| 5 | `candidate-refrozen-once` | 22-30 | The first frozen candidate failed the re-check, three rules were amended, and the candidate was frozen again. |
| 6 | `release-gaps-closed` | 31-32, 405-408 | The three release-process gaps left by 0.24 were closed before the freeze, and publishing needs the review's identity. |
| 7 | `reduced-independence` | 33-35 | Only Codex and the maintainer reviewed 0.25; nothing here is a stronger claim than 'bounded'. |
| 8 | `known-open-defects` | 36-38, 111-112 | Two known converter limitations ship with 0.25, and both fail visibly. |
| 9 | `no-new-benchmark-results` | 40 | 0.25 publishes no new benchmark results. |
| 10 | `native-local-functions` | 3-8, 44-53 | Calor gains local functions (functions declared inside functions), with a new error Calor0211 for unsupported forms. |
| 11 | `local-functions-convert-natively` | 3-6, 194-203 | The converter turns simple C# local functions into Calor local functions; the rest stay C#. |
| 12 | `calor0212-invalid-by-reference-operand` | 7, 60-64 | Bad ref/out/in arguments are now caught by Calor with a clear error. |
| 13 | `by-ref-more-operands-compile` | 65-68 | More kinds of ref/out arguments now compile. |
| 14 | `by-ref-writes-charge-mut` | 69-73 | Writes through ref/out now count as mutations for effect checking, closing a loophole. |
| 15 | `by-ref-null-checks-end-earlier` | 74-77 | Null checks are invalidated sooner when by-reference parameters may share storage. |
| 16 | `convert-keeps-by-ref-identity` | 78-86 | Converted ref/out code keeps writing to the same storage, and out var is lowered safely or left as C#. |
| 17 | `by-ref-shapes-stay-csharp` | 87-93 | By-reference forms Calor cannot express are kept as C#. |
| 18 | `feature-check-by-ref-entries` | 94-97 | The feature-check report now describes ref/out support accurately. |
| 19 | `by-ref-corpus-effect` | 98-105 | On three real libraries, 33 more members stay C#, and 10 more of 364 modules get further through the compiler. |
| 20 | `by-ref-contract-cases-hold` | 107-112 | All 57 ref/out contract cases hold; one conversion limitation stays open. |
| 21 | `interp-overload-selection-kept` | 3-6, 116-121 | Interpolated strings keep calling the same overload after conversion. |
| 22 | `interp-handlers-kept-as-csharp` | 122-128 | Special interpolated-string targets are kept as C# instead of being miscompiled. |
| 23 | `interp-holes-native-subset` | 129-136 | Only simple interpolation holes are converted; others are kept as marked C#. |
| 24 | `interp-hole-evaluation-order` | 137-139 | Interpolation holes now run in the original order. |
| 25 | `interp-holes-analyzed` | 140-145 | Effects inside interpolation holes are now checked, which can turn a warning into an error. |
| 26 | `interp-hole-diagnostic-location` | 146-147 | Errors in interpolation holes point to the right place. |
| 27 | `interp-feature-check-entries` | 148-152 | feature-check lists interpolation support in more detail. |
| 28 | `interp-checked-by-differential-tests` | 154-156 | Interpolation conversion is tested by running both programs and comparing output, also in German and French locales. |
| 29 | `report-label-fixed` | 158-168 | The convert report no longer mislabels automatic rescues as passthrough. |
| 30 | `report-path-trigger-fields` | 169-175 | Conversion reports say which mechanism kept each member as C# and why. |
| 31 | `mcp-module-name-rescue-fixed` | 176-179 | A custom module name no longer breaks the MCP rescue. |
| 32 | `mcp-roundtrip-honours-passthrough` | 180-181 | An MCP option that was ignored is now honoured. |
| 33 | `batch-abort-json` | 182-184 | A batch abort now returns structured JSON. |
| 34 | `surface-defaults-unchanged` | 185-190 | Each conversion surface's defaults are now documented; none changed. |
| 35 | `json-verbose-stdout-single-document` | 207-216 | JSON output stays parseable when --verbose is used. |
| 36 | `assess-coverage-by-ref-descriptions` | 217-223 | Analysis commands describe ref/out support correctly. |
| 37 | `expression-bodied-compound-assignment` | 224-226 | A silent operator loss in conversion is fixed. |
| 38 | `local-shadows-function-in-calls` | 227-231 | Calls now resolve to a local delegate before a same-named function, matching C#. |
| 39 | `double-literals-kept` | 232-236 | The converter no longer turns 2.0 into 2, which changed division results. |
| 40 | `iterator-accessors-preserved` | 237-251 | C# iterator properties are kept as C# instead of producing illegal Calor; one test case could not be measured. |
| 41 | `partial-properties-preserved` | 252-258 | Partial properties are kept as C# instead of breaking the build. |
| 42 | `named-refinement-arithmetic` | 260-272 | Arithmetic on named refinement types now type-checks. |
| 43 | `decimal-arithmetic-type` | 273-278 | The type checker now types decimal arithmetic correctly. |
| 44 | `proof-refers-to-local` | 280-301 | Proofs can now mention simple local variables, under strict rules. |
| 45 | `website-conversion-guide` | 3-8, 303-314 | The website has a new guide on converting C#, checked against the real converter. |
| 46 | `guide-corrections` | 316-323 | Writing the guide found and fixed four converter defects. |
| 47 | `website-examples-checked-in-ci` | 325-344 | Website code examples are now compiled in CI; 49 programs on 96 pages are checked. |
| 48 | `website-example-fixes` | 346-365 | 11 broken website examples were found and fixed, and the refinement pages now say which command runs the obligation solver. |
| 49 | `array-expressions-native-ordered` | 369-386 | Arrays in expressions now convert to Calor and keep C#'s evaluation order. |
| 50 | `generated-names-avoid-collisions` | 387-389 | Converter-generated names no longer clash with user names. |
| 51 | `array-shapes-stay-csharp-reported` | 390-395 | Array forms that cannot be expressed stay C# and are reported. |
| 52 | `arr2d-row-effects-charged` | 396-403 | Effects inside 2D array rows are now checked; array member calls are still unknown. |
| 53 | `trusted-publishing` | 410-416 | Publishing to NuGet uses short-lived tokens, and old workflows cannot publish. |
| 54 | `notes-hash-checked-g014` | 417-418 | The release notes cannot differ from the reviewed ones. |
| 55 | `benchmark-publication-separate-g015` | 419-420 | Benchmark numbers need their own explicit approval to publish. |
| 56 | `release-sdk-pin-no-google-fonts` | 421-422 | Release builds use one pinned SDK and no build-time font download. |
| 57 | `release-runtime-pinned` | 423-426 | Package bytes no longer depend on which .NET runtime the build machine had. |

## Approved readings and the not-measured case

- **R-F6-FEWER-OPTIONS** (amendment 1.5.0, `D-ACCEPT-READINGS`): F6-REPORT-04, 05, 06, 07, 12, 13. A both-options
  cell on a surface with fewer options is measured with every option the surface has; SDK cells are measured on
  the library.
- **R-PERMISSIVE-EFFECTS-NATIVE** (same): F1-REFOUT-08, X-INT-01, F2-INTERP-02, F2-INTERP-07, F5-ARRAY-01,
  F5-ARRAY-05, X-INT-02. A row whose only error without `--permissive-effects` is `Calor0410` counts as native.
- **F4-ITER-04 not measured** (`D-F4-ITER-04-NOT-MEASURED`): no surface can measure it on the candidate. Its READY
  result is `skipped`, and F4-ITER-01, 02, 03, X-INT-03 and DOC-F4-01 cover it; all five passed.

## Known residuals (`knownResiduals` in the record)

| Item | Status | Effect |
|---|---|---|
| #1578 GATE-HEADING-CODE-SPAN-LINK | MAJOR, accepted (`D-ACCEPT-FILE-FREEZE`) | Gate anchor text differs for a code-span link heading; no W1 page uses one |
| #1578 GATE-S004-PARITY-COARSE | MINOR, accepted | Parity theory compares flagged-case sets only |
| GATE-PREVIOUSAFTER-BOOL | MINOR, known | Gate's T005 port accepts `previousAfter: false` where C# rejects it; counts are protected |
| #1573 | accepted residual | Private base-class `@default` makes a converted `out var` fail loudly (`CS0122`); stated in the notes |
| #1574 | pre-existing, 0.27 | Chained assignment double-evaluates outside `out var` lowerings (silent); three smaller items |
| #1564 | pre-existing, 0.26 | In-process converter reads `~/.calor/manifests`; tests use an isolated HOME |
| #1541 | pre-existing, 0.27 | Field/property default collection elements silently dropped; does not contradict the F5 claim (arrays only) |
| #1555 | pre-existing | Hand-written `§ARR` id/type swap; converter output cannot hit it |
| #1548 | open governance | Next release's contract |
| NUGET-POLICY-NOT-VISIBLE, CLASSIFIER-NOT-A-GATE | known limitations | See the candidate README |

## Verification done for this PR

- **R2 gate** (`reviews/r2-gate.txt`). In a scratch clone the generated final record was committed with a
  placeholder approval URL (`pull/1#pullrequestreview-1`) and the `0.25.x` entry set to `adjudicated`.
  `refs/remotes/origin/main` was moved to that commit locally only; nothing was pushed. With the candidate checked
  out, `--expect-head --version 0.25.0` against the real surfaces passed. The surfaces were the notes, packages,
  metadata and website, all clone A of the re-proof, re-hashed equal to `publication-candidates.json`.
  `--benchmark-worktree` after the B2 gate failed with **only `G015`**, as intended.
- **Classifier** (`reviews/classifier.txt`). From a fresh clone at the landing, the PR head and a scratch commit
  adding the post-approval files are both `NOT-INVALIDATED`. The same with the map change added is `INVALIDATED`
  on `eng/release-adjudication-map.json` only. That is why the change goes in the gate-state PR.

## Known CI failure on the final commit (needs your explicit OK)

`ReleaseAdjudicationMapTests.Release025IsMappedToTheFutureA1PathsAndIsNotYetAdjudicated` asserts that
`terminal-record.json` does **not** exist. So the post-approval commit turns `tests (compiler)` red on exactly that
one assertion. On a scratch head with the post-approval files, 1 of 166 release-gate and manifest tests fails, and
it is this one.

The test is a release-state file under `D-A1-RELEASE-STATE-FILES`, and the ruling updates it in the gate-state PR
after this one. Changing it first is not an option. Any non-exempt commit that lands before this PR's merge enters
the candidate-to-adjudication history, and the classifier would report it.

So this PR merges with that single known failure, and the gate-state PR follows immediately. That PR's tests pass
once the record is on main (`reviews/gate-state-tests.txt`). If `tests (compiler)` is a required check, the merge
needs your admin merge.

## Exact steps after approval

1. **Approve** this PR on GitHub (review state Approve). Its URL is
   `https://github.com/juanmicrosoft/calor/pull/<this PR>#pullrequestreview-<id>`.
2. Close #943, #906, #847, #1139, #1132, #1144, #1143, #1428 and #1429.
3. The agent (or you) adds the post-approval commit. Mark the PR ready only after it lands:
   ```bash
   T=docs/plans/evidence/adjudication-1430/tools/make_a1_records.py.txt
   python3 $T . --approval-url PENDING --snapshot-closures   # writes closures.json from gh
   python3 $T . --approval-url '<review URL>'
   python3 $T . --approval-url '<review URL>' --check
   git add docs/plans/evidence && git commit -m "0.25 A1: add the approved READY records and terminal record (#1430)"
   ```
   It adds exactly eight files and changes none. The agent then re-runs the classifier on that commit from a fresh
   clone of the landing.
4. Merge this PR with **Create a merge commit**, accepting the one known failure above. The merge commit is the
   adjudication commit.
5. Compute the identity from the merge commit:
   ```bash
   git fetch origin main
   ADJ=$(git rev-parse <merge commit>)   # full 40-hex SHA on main
   DIG=$(git show "$ADJ:docs/plans/evidence/adjudication-1430/terminal-record.json" | shasum -a 256 | cut -d' ' -f1)
   echo "calor-adjudication:v1:$ADJ:$DIG"
   ```
   The hash is over the blob's exact bytes at that commit, so a later edit makes a different identity.
6. Merge the gate-state PR (`0.25.x` set to `adjudicated`, four tests updated). The gate reads the map from
   protected main, so no identity passes before this.
7. Optional local check in a full, non-shallow clone with `origin/main` fetched and the candidate checked out:
   `python3 scripts/verify_release_adjudication.py --identity "<identity>" --expect-head --version 0.25.0`.
8. Dispatch the release:
   `gh workflow run publish-nuget.yml --ref main -f adjudication_identity="<identity>"`.
   The workflow builds from the candidate, re-runs the gate on every surface, pushes to NuGet through Trusted
   Publishing, creates the `v0.25.0` release with the identity trailer, and dispatches the website deploy. Do not
   dispatch benchmark publication; this record does not approve it.

## Files

- `decisions.json`: the three 2026-10-21 decisions.
- `claim-registry.json`: 57 claims (approved).
- `draft/terminal-record.pending.json`: the record before approval.
- after approval: `closures.json`, `terminal-record.json` and the six `ready/d1d7afde/a1/ready-record.json`.
- `tools/make_a1_records.py.txt`: the generator.
- `reviews/`: Codex rounds and verification, R2 gate output, classifier output.
