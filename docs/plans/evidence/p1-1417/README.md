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
| Explicit fetch of protected main and `v*` tags | `.github/workflows/test.yml` (`quality-ratchets`, `remaining-tests`), `.github/workflows/publish-nuget.yml` (`test`, `release-quality`) |
| Full-SHA benchmark stamp | `tests/Calor.Evaluation/Benchmarks/BenchmarkRunner.cs` |
| Skip-site removal, counts | `eng/test-manifest.json` |
| Release checklist row | `.claude/skills/create-release/SKILL.md` |

Every index entry keeps its historical stamp (`measuredCommit`) exactly as written and gains
`durableIdentity {status, commit, resolvedVia, treeHashes, inputContentHashes}` beside it. The
#1199 fields (`resolvableOnMain`, `basis`, `note`) are unchanged; `resolvableOnMain` must equal
`durableIdentity.commit` (`P015`). No ledger and no website data file is modified.

## §6 rules and where each is enforced

| §6 rule | Enforcement | Finding |
|---|---|---|
| Full 40-hex SHA | `DurableProvenance.VerifyEntry` | `P002` |
| Ancestor of fetched `refs/remotes/origin/main`, or the target of a `refs/tags/vX.Y.Z` tag | `VerifyDurability` | `P005` |
| `HEAD`, `refs/pull/N/merge`, branches, non-release tags rejected | `VerifyDurability` | `P003` |
| Tree hashes verified with `git rev-parse <commit>:<path>` | `VerifyTreeHashes` | `P007` |
| Manifest SHA-256 verified | `VerifyContentHashes` | `P008` |
| Identical-tree claim verified at both commits | `VerifyIdenticalTree` | `P009` |
| Two-phase identity; phase 1 never authoritative; write-back re-verifies trees | `VerifyPending`, `CompleteWriteBack`, `VerifyWriteBack` | `P013` |
| Shallow clone fails, not skips | `Verify` | `P006` |
| Unfetched clone fails | `VerifyDurability` | `P004` |
| History: stamps unchanged, identities beside them | `EveryIndexEntryStillMatchesItsArtifactsOwnStamp` | — |

The other bases are checked too: `measured-on-top-of` must name the stamped base (`P010`);
`landing-commit-only` must name a commit that changed the artifact (`P011`); the new
`unique-prefix-expansion` basis, for the website's short stamps, requires the stamp to be a
unique 7–39 hex prefix of the durable commit in this clone (`P012`).

## Acceptance (#1417)

**A fresh clone resolves every authoritative identity after squash merge.** `fresh-clone.log`:
a `git clone` of GitHub with no submodules, checked out at this branch, resolves all 11 indexed
identities (5 ledgers, 6 published benchmark stamps) from `refs/remotes/origin/main`. The #1159
orphan `3bb2601e0c…` is absent from that clone; its ledger's identity is the landing commit
`82a7c653`, verified as the commit that added the ledger. Several durable commits came to `main`
through squash merges (for example `82a7c653`, #1145) and others through merge commits (for
example `b732c4ab` via #1346). `SquashMergeWriteBackResolvesFromAFreshClone` reproduces the #1159
shape end to end: measured on a branch, squash-merged, branch deleted, measured commit absent from
a fresh clone, pending identity not authoritative, write-back finds the squash commit, completed
identity verifies. `MergeCommitWriteBackNamesTheMergeCommit` covers the merge-commit route.

**A branch-only replacement, a false identical-tree claim, and a shallow-clone skip each fail
discriminating tests.**

| Case | Test | Code |
|---|---|---|
| Branch-only commit, even when it is `HEAD` | `BranchOnlyCommitIsRejectedEvenWhenItIsHead` | `P005` |
| `refs/pull/N/merge` commit; `HEAD`/PR-ref/branch as `resolvedVia` | `PullRequestMergeRefIsNotProtectedMain` | `P005`, `P003` |
| False identical-tree claim; unrecorded claim | `FalseIdenticalTreeClaimIsRejected`, `TrueIdenticalTreeClaimVerifiesAtBothCommits` | `P009` |
| Shallow clone | `ShallowCloneFailsInsteadOfSkipping` | `P006` |
| Clone without `origin/main` | `CloneWithoutFetchedMainFails` | `P004` |
| Merge changed a measured tree | `WriteBackFailsClosedWhenTheMergeChangedAMeasuredTree` | `P013` |

Each control asserts its code and that no other code fired. They were also checked against
deliberately broken verifiers before commit. Reverting to `HEAD` reachability, letting a shallow
clone through, or checking an identical-tree claim only at the measured commit made
`BranchOnlyCommitIsRejectedEvenWhenItIsHead`, `PullRequestMergeRefIsNotProtectedMain`,
`ShallowCloneFailsInsteadOfSkipping`, and `FalseIdenticalTreeClaimIsRejected` fail (4 of 20); the
real verifier passes all 20. `fresh-clone.log` repeats the shallow and unfetched cases on the real
repository: the test fails with `P006` and `P004` respectively. Nothing is skipped.

**Historical stamps remain immutable and are linked to separately verified durable identities.**
No `measuredCommit` and no published stamp changed. `EveryIndexEntryStillMatchesItsArtifactsOwnStamp`
binds each index entry to the stamp in its file; the coverage tests
(`EveryLedgerThatStampsAMeasurementIsIndexed`, `EveryPublishedBenchmarkStampIsIndexed`) stop a
new stamp from bypassing the index.

## Identities recorded

| Stamp (as written) | Basis | Durable commit (on `main`) |
|---|---|---|
| `calor0425-corpus-ledger.json` `a00f4a52…` | measured-on-top-of | `a00f4a525da7a88e784436fb75259b9606ca242b` |
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

`effect-rows-probe-ledger.json`'s measured commit `72d060f4` is not on `main`. A fresh clone gets
it through tag `ppe1-adjudication-0.15.0` and branch `release/v0.15.0`, which lets the
identical-tree claim be re-checked. If that object ever becomes unreachable, the claim fails
(`P009`) instead of passing on trust. The durable identity itself (`bb5bbdb4`, `main`) does not
depend on that tag.

A prefix expansion identifies a commit. It does not show the published numbers came from a clean
checkout of that commit, and it does not make them valid. Those files stay `stale` in the
inventory (#1276, #1422).

## Two-phase identity (write-back)

This PR introduces no pending identity: every durable commit above is already on `main`. So it
needs no write-back, whether it is merged by squash or by merge commit. For later measurements on
a branch, such as #1422's benchmark publication:

1. In the PR, record `durableIdentity {status: "pending", phase1 {headCommit (= measuredCommit),
   pr, treeHashes, inputContentHashes, artifactSha256}}` with basis `post-merge-write-back`. The
   verifier checks its shape, checks the committed artifact against `artifactSha256`, and, while
   the head exists, checks the trees at the head. A pending identity is never authoritative.
2. After merge, run `git fetch origin main` and
   `CALOR_PROVENANCE_WRITEBACK=1 dotnet test tests/Calor.Compiler.Tests --filter "FullyQualifiedName~LedgerCommitStampTests.PendingIdentities"`,
   then open the write-back PR. The run finds the first-parent commit on `main` that landed the
   artifact and re-verifies every phase-1 tree there. If a tree differs, it fails, and the numbers
   are re-measured; it never records a weaker identity on its own.

## Residuals from §11 (#1159 row)

| Residual | Status |
|---|---|
| Reachability tested against `HEAD` (PR merge ref) | Fixed: only fetched `origin/main` or a release-tag target |
| Shallow clone skips | Fixed: fails (`P006`); runtime skip site removed from `eng/test-manifest.json` |
| `identical-src-tree` asserted, not verified | Fixed: verified at both commits (`P009`) |
| Reversed fetch-depth comment | Fixed: the manifest comment is removed with its skip site; the `quality-ratchets` comment is corrected |
| Short SHA `c2a8816d` in benchmark files | The historical stamps are unchanged, with durable identities recorded beside them. The producer (`BenchmarkRunner`) now writes full SHAs. Regenerating and publishing those files is #1422 |
| No post-merge write-back | Fixed as a reviewed two-phase protocol with a fail-closed completion. It is a manual write-back PR, not an automated push to `main` |

## Not done here

- No CI run was possible locally; the workflow steps were checked by YAML parse and by running the
  same `git fetch` refspecs with `--dry-run`.
- Branch-protection settings are not changed. `tests (compiler)`, which runs these tests, is
  already a required check on `main`.
- The test does not freeze the list of historical stamps. A PR that changed a ledger's stamp and
  its index entry together would pass; that is review's job, as it is for a re-measurement.
