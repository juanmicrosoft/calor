# P1 (#1417) closure evidence: durable provenance identity

Gate P1 of the v0.24 evidence contract (`docs/plans/v0.24-evidence-contract.md`, FROZEN 1.0.1).
It consumes §6 (durable provenance identity), §3 (inventory rows `ledger-provenance-index`,
`benchmark-provenance`, `benchmark-results`, `test-manifest`), and the §11 retrospective row for
#1159. It does not amend the contract. Inventory classifications are unchanged; reclassification
follows the contract's `reclassificationRule` (an amendment naming the repairing PR).

## What changed

| Piece | File |
|---|---|
| Identity index, schema 2 | `bench/phase0-agent-native/commit-stamp-index.json` |
| Verifier (test-side) | `tests/Calor.Compiler.Tests/Provenance/DurableProvenance.cs` |
| Repository checks | `tests/Calor.Compiler.Tests/LedgerCommitStampTests.cs` |
| Discriminating controls | `tests/Calor.Compiler.Tests/Provenance/DurableProvenanceTests.cs` |
| Explicit fetch of protected main and tags | `.github/workflows/test.yml` (`quality-ratchets`, `remaining-tests`), `.github/workflows/publish-nuget.yml` (`test`, `release-quality`) |
| Full-SHA benchmark stamp | `tests/Calor.Evaluation/Benchmarks/BenchmarkRunner.cs` |
| Skip-site removal, counts | `eng/test-manifest.json` |
| Release checklist row | `.claude/skills/create-release/SKILL.md` |

Every index entry keeps its historical stamp (`measuredCommit`) exactly as written. Beside it, the
entry gains `durableIdentity {status, commit, resolvedVia, treeHashes, inputContentHashes}`. The
#1199 fields (`resolvableOnMain`, `basis`, `note`) are unchanged, and `resolvableOnMain` must equal
`durableIdentity.commit` (`P015`). No ledger and no website data file is modified.

## §6 rules and where each is enforced

| §6 rule | Enforcement (`DurableProvenance`) | Finding |
|---|---|---|
| Full 40-hex SHA | `VerifyEntry` | `P002` |
| Ancestor of fetched `refs/remotes/origin/main` | `VerifyOnMain` | `P005` |
| `HEAD`, `refs/pull/N/merge`, branches, tags rejected | `VerifyOnMain` | `P003` |
| Trees verified with `git rev-parse`; the whole tree `"/"` is required | `VerifyComplete`, `CheckTrees` | `P007` |
| Manifest SHA-256 verified | `CheckContents` | `P008` |
| Identical-tree claim must include `src` and is verified at both commits | `VerifyIdenticalTree` | `P009` |
| Two-phase identity: phase 1 never authoritative; write-back keeps phase 1 and re-verifies trees, manifests, and the landing | `VerifyPending`, `CompleteWriteBack`, `VerifyWriteBack` | `P013` |
| Shallow clone fails, not skips | `Verify` | `P006` |
| Unfetched clone fails | `VerifyOnMain` | `P004` |
| History: stamps unchanged, identities beside them | `EveryIndexEntryStillMatchesItsArtifactsOwnStamp` | — |

**Tags.** §6 allows "the target of an immutable release tag". This repository's `v*` tags are
lightweight and unprotected (contract §2 records `v0.22.0` as lightweight). Git alone cannot show
that such a tag is immutable, so the verifier accepts only protected `main`. That is stricter than
§6, never weaker. No recorded identity needs a tag.

**Whole tree.** `treeHashes["/"]` is the durable commit's root tree. It covers every
repository-resident input: corpus gitlinks, `.calr` fixtures, and configuration. Named entries
(`src`, `bench/corpus`, configuration files) make the claimed inputs explicit.

The other bases are checked too:

- `measured-on-top-of` must name the stamped base (`P010`). It must also name a `landing`
  commit. That commit must be on `main`, descend from the base, hold the current artifact bytes
  (which its first parent lacks), and have the recorded `src` tree.
- `landing-commit-only` must name a commit that changed the artifact (`P011`).
- `unique-prefix-expansion`, for the website's short stamps, requires the stamp to be a unique
  7–39 hex prefix of the durable commit in this clone (`P012`).

## Acceptance (#1417)

**A fresh clone resolves every authoritative identity after squash merge.** See `fresh-clone.log`.
A `git clone` of GitHub with no submodules, checked out at this branch, resolves all 11 indexed
identities from `refs/remotes/origin/main`: 5 ledgers and 6 published benchmark stamps. The #1159
orphan `3bb2601e0c…` is absent from that clone. Its ledger's identity is the landing commit
`82a7c653`, verified as the commit that added the ledger. Durable commits reached `main` both
through squash merges (`82a7c653`, #1145) and through merge commits (`b732c4ab` via #1346).

`SquashMergeWriteBackResolvesFromAFreshClone` reproduces the #1159 shape end to end:

1. The ledger is measured and stamped on a branch, squash-merged, and the branch deleted.
2. The measured commit is absent from a fresh clone.
3. The pending identity is not authoritative.
4. The write-back finds the squash commit, and the completed identity verifies.

`MergeCommitWriteBackNamesTheMergeCommit` covers the merge-commit route.

**A branch-only replacement, a false identical-tree claim, and a shallow-clone skip each fail
discriminating tests.**

| Case | Test | Code |
|---|---|---|
| Branch-only commit, even when it is `HEAD` | `BranchOnlyCommitIsRejectedEvenWhenItIsHead` | `P005` |
| `refs/pull/N/merge` commit; `HEAD`, PR ref, or branch as `resolvedVia` | `PullRequestMergeRefIsNotProtectedMain` | `P005`, `P003` |
| A branch-only commit behind a `v*` tag | `ShortShasAndTagsAreRejected` | `P003` |
| False identical `src` tree; a claim without `src`; an unrecorded claim | `FalseIdenticalTreeClaimIsRejected`, `TrueIdenticalTreeClaimVerifiesAtBothCommits` | `P009` |
| Identity without the whole tree | `ClaimedTreeAndManifestHashesAreReRead` | `P007` |
| Shallow clone | `ShallowCloneFailsInsteadOfSkipping` | `P006` |
| Clone without `origin/main` | `CloneWithoutFetchedMainFails` | `P004` |
| Merge changed a measured tree or manifest; forged completion, with or without a rewritten phase 1; completion naming a non-landing commit | `WriteBackFailsClosedWhenTheMergeChangedAMeasuredInput`, `SquashMergeWriteBackResolvesFromAFreshClone` | `P013` |
| Artifact changed after write-back; phase 1 without `src`; pending entry carrying `resolvableOnMain` (also refused by the write-back) | `MergeCommitWriteBackNamesTheMergeCommit`, `PendingIdentityIsNeverAuthoritativeAndMustDescribeTheCommittedArtifact` | `P013` |
| Unlanded phase 1 naming a head that does not exist | `UnlandedPhase1MustNameAnExistingHead` | `P013` |
| Measured-on-top-of without, or with a wrong, landing | `MeasuredOnTopOfMustNameTheLandingOfTheRepair` | `P010` |

Each control asserts its code and that no other code fired. The controls were also run against
deliberately broken verifiers:

- **Before the first review**, `HEAD` reachability, a shallow pass-through, and an identical-tree
  check at the measured commit only were reintroduced together. 4 of 20 tests failed.
- **After review round 1**, five more defects were reintroduced together: an optional whole tree,
  accepted `v*` tags, an identical-tree claim without `src`, no phase-1 manifest comparison, and no
  landing check on write-back. 4 of 22 tests failed.
- Turning off only the `src` requirement failed `FalseIdenticalTreeClaimIsRejected`.
- **After review round 2**, four more defects were reintroduced together: no comparison with the
  committed pending record, no `src` requirement in phase 1, no current-artifact check on a
  completed write-back, and `resolvableOnMain` allowed on a pending entry. 4 of 22 tests failed.
- **After review round 3**, the check that an unlanded phase-1 head exists was disabled and the
  write-back's input validation removed. `UnlandedPhase1MustNameAnExistingHead` failed (1 of 23).
  The write-back refusal of an invalid entry is enforced twice, by validating the pending entry and
  the completed one, so removing only the first is still caught by the second.

The real verifier passes all 23. `fresh-clone.log` repeats the shallow and unfetched cases on the
real repository: the test fails with `P006` and `P004`. Nothing is skipped.

**Historical stamps remain immutable and are linked to separately verified durable identities.**
No `measuredCommit` and no published stamp changed. `EveryIndexEntryStillMatchesItsArtifactsOwnStamp`
binds each index entry to the stamp in its file. The coverage tests
(`EveryLedgerThatStampsAMeasurementIsIndexed`, `EveryPublishedBenchmarkStampIsIndexed`) stop a new
stamp from bypassing the index.

## Identities recorded

| Stamp (as written) | Basis | Durable commit (on `main`) |
|---|---|---|
| `calor0425-corpus-ledger.json` `a00f4a52…` | measured-on-top-of, landing `2a79dfdf…` | `a00f4a525da7a88e784436fb75259b9606ca242b` |
| `higher-order-demand-ledger.json` `b732c4ab…` | identical-src-tree | `b732c4abf8b78e2327f565886e6366e2d64644ea` |
| `effect-rows-probe-ledger.json` `72d060f4…` | identical-src-tree (`src` `b6ae250a…` at both) | `bb5bbdb478979abc006972b4a87028d3a6c5fb0a` |
| `effect-resolver-key-ledger.json` `b2c43561…` | identical-src-tree | `b2c435610c3aa34accc0f28f66d5eca834d7a129` |
| `effect-rows-benefit-ledger.json` `3bb2601e0c…` (absent everywhere) | landing-commit-only | `82a7c653cbf1ea2f6231e38cc328c74a34e6589b` |
| `benchmark-results.json#/commit` `c2a8816d` | unique-prefix-expansion | `c2a8816d1bd3864e70966120c9849e8f8d988cb2` |
| `benchmark-provenance.json#/sourceCommit` `c2a8816d` | unique-prefix-expansion | `c2a8816d1bd3864e70966120c9849e8f8d988cb2` |
| `benchmark-provenance.json#/agentTasks/sourceCommit` `107462e` | unique-prefix-expansion | `107462e3b760aee49522677e1a45a080db241661` |
| `benchmark-provenance.json#/agentRefactoring/sourceCommit` `580e189` | unique-prefix-expansion | `580e18932b279a2eaf0641f3272a708af7bc4303` |
| `agent-benchmark-results.json#/commit` `107462e` | unique-prefix-expansion | `107462e3b760aee49522677e1a45a080db241661` |
| `agent-refactoring-results.json#/commit` `580e189` | unique-prefix-expansion | `580e18932b279a2eaf0641f3272a708af7bc4303` |

**`calor0425-corpus-ledger.json`.** The numbers were measured with uncommitted repairs on top of
`a00f4a52`, and that working tree was never recorded. The durable identity therefore names the
base, as the #1199 note always said. The new `landing` record names `2a79dfdf…`. That commit is on
the #1461 branch and reachable from `main` through merge `84e3cf12`. It committed this exact ledger
together with the repairs; its first parent `8e0a7e1d` has the same `src` tree as `a00f4a52`. The
merge commit `84e3cf12` also satisfies the landing rule, but it carries a later fix (`eed448fa`).
`2a79dfdf` is the closer pin. Even so, equality of the measured working tree with `2a79dfdf:src` is
inferred, not verified. The index says so.

**`effect-rows-probe-ledger.json`.** The measured commit `72d060f4` is not on `main`. A fresh
clone gets it through tag `ppe1-adjudication-0.15.0` and branch `release/v0.15.0`, which lets the
identical-tree claim be re-checked. If that object ever becomes unreachable, the claim fails
(`P009`) instead of passing on trust. The durable identity itself (`bb5bbdb4`, on `main`) does not
depend on that tag.

**Website stamps.** A prefix expansion identifies a commit. It does not show the published numbers
came from a clean checkout of that commit, and it does not make them valid. Those files stay
`stale` in the inventory (#1276, #1422).

## Two-phase identity (write-back)

This PR introduces no pending identity: every durable commit above is already on `main`. It needs no
write-back, whether it is merged by squash or by merge commit. For later measurements on a branch,
such as #1422's benchmark publication:

1. **In the PR**, commit the artifact together with an index entry
   `durableIdentity {status: "pending", phase1 {headCommit (= measuredCommit), pr, treeHashes,
   inputContentHashes, artifactSha256}}` with basis `post-merge-write-back` and no
   `resolvableOnMain`. The verifier then:
   - checks the record's shape, including that `treeHashes` claims `src` and not `"/"`;
   - checks the committed artifact against `artifactSha256`;
   - checks the trees and manifests at the head. The head must exist until the artifact has
     landed on `main`; only after that may a squash have discarded it.

   A pending identity is never authoritative.
2. **After merge**, run `git fetch origin main`, then
   `CALOR_PROVENANCE_WRITEBACK=1 dotnet test tests/Calor.Compiler.Tests --filter "FullyQualifiedName~LedgerCommitStampTests.PendingIdentities"`,
   and open the write-back PR. The run finds the first-parent commit on `main` that landed the
   artifact, re-verifies every phase-1 tree and manifest there, and copies the phase-1 record under
   `writeBack`. It runs the verifier on the pending entry before, and on the completed entry after,
   and writes nothing that the verifier rejects. If any input differs, the run fails and the numbers are re-measured; it never
   records a weaker identity on its own.

   Every later run checks four things. The `writeBack` copy must equal the pending entry as
   committed in the index *at the landing commit*: that is the reviewed record, and it cannot be
   edited afterwards. Every phase-1 input must still match the landed commit. The landed commit
   must be the one that landed the artifact. The committed artifact must still be those bytes.

## Residuals from §11 (#1159 row)

| Residual | Status |
|---|---|
| Reachability tested against `HEAD` (PR merge ref) | Fixed. Only fetched `origin/main` counts |
| Shallow clone skips | Fixed. It fails (`P006`); the runtime skip site is removed from `eng/test-manifest.json` |
| `identical-src-tree` asserted, not verified | Fixed. Verified at both commits (`P009`) |
| Reversed fetch-depth comment | Fixed. The manifest comment is removed with its skip site, and the `quality-ratchets` comment is corrected |
| Short SHA `c2a8816d` in benchmark files | Historical stamps unchanged, with durable identities beside them. The producer (`BenchmarkRunner`) now writes full SHAs. Regenerating and publishing those files is #1422 |
| No post-merge write-back | Fixed as a reviewed two-phase protocol with a fail-closed completion. The write-back is a PR, not an automated push to `main` |

## Not done here

- No CI run was possible locally. The workflow steps were checked by YAML parse and by running the
  same `git fetch` refspecs with `--dry-run`.
- Branch-protection settings are not changed. `tests (compiler)`, which runs these tests, is already
  a required check on `main`.
- The test does not freeze the list of historical stamps. A PR that changed a ledger's stamp and its
  index entry together would pass; that is review's job, as it is for any re-measurement.
- Review rounds are recorded in `reviews/`.
