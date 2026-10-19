# 0.24 R2 release gate (#1410)

**Gate:** R2. **Epic:** #1409. **Consumes:** #1407 contract 1.0.1 (FROZEN), §6 provenance identity,
§7 release-path inventory, §8 terminal success predicate, §9 independence deviation.
**Produces for:** #1423 (candidate), #1424 (unpublished candidate hashes), #1408 (terminal record).

This record defines the one adjudication identity that every public release surface consumes, the
fields of the #1408 terminal record that the gate reads, and the workflow gates. It changes no
contract rule. It does not publish, tag, or deploy anything.

**Delivery.** Two stacked PRs, to stay within the §9 PR-size ceiling: PR 1 adds the verifier
(`scripts/verify_release_adjudication.py`), its negative controls (`ReleaseAdjudicationGateTests`), and
this record; PR 2 wires every workflow to it and adds the structural controls
(`ReleaseWorkflowGateTests`). PR 1 alone changes no workflow.

## 1. The adjudication identity

```
calor-adjudication:v1:<adjudication commit, 40 hex>:<terminal record SHA-256, 64 hex>
```

- The **adjudication commit** is a full SHA on protected `main` (an ancestor of fetched
  `refs/remotes/origin/main`). It holds the release's terminal record at the path the
  adjudication map gives that release (§9). For 0.24.0 that is the #1408 record at
  `docs/plans/evidence/adjudication-1408/terminal-record.json`.
- The **record hash** is SHA-256 over the record blob's exact bytes at that commit (no
  normalization). A changed record is a different identity.
- Git objects are immutable, so the identity names one record forever. A clone that is shallow or
  has not fetched `main` fails the gate; it never skips (§6 "Shallow clones").

## 2. Terminal record fields the gate reads

#1408 writes the record. R2 fixes only the fields the gate consumes; #1408 may add fields.

| Field | Rule |
|---|---|
| `schema` | `calor.adjudication-terminal-record/1` |
| `issue` | the release's A1 issue in the map (§9); `1408` for 0.24.0 |
| `outcome` | `MILESTONE-SUCCEEDED` (a `MILESTONE-FAILED` record never passes) |
| `adjudicationIndependence` | `reduced` while the §9 deviation stands |
| `limitation` | the contract's `publishedLimitation`, verbatim |
| `epicIndependentAdjudicationMet` | `false` |
| `contract` | `{version, files}`; `files` equals `sha256.json` `files` at the adjudication commit, and the packet tree is identical at the candidate and the adjudication commit |
| `candidate` | `{commit, version}`; full SHA, an ancestor of the adjudication commit; `Directory.Build.props` at the candidate declares `version`; since #1567 `version` must be in the map (`G009`) and map to the release whose record path holds the record (`G016`) |
| `evidenceManifests` | `[{role, path, sha256}]` with exactly one `candidate-manifest` (#1423) and one `raw-artifact-freeze` (#1424); each blob at the adjudication commit has that SHA-256 and is JSON whose top-level `candidate` is the candidate SHA or `{"commit": <candidate SHA>}` |
| `claimRegistry` | `{path, sha256}` of the claim registry `{"claims": ["claim:<id>", ...]}` committed at the adjudication commit; since #1567 `path` must be the map's `claimRegistry` for the release (0.24.0: #1424's `c2-1424/regeneration-3/claim-registry.json`) |
| `adjudications` | exactly one row per subject: every inventory artifact, `gate:#<issue>` for every child but #1408, and every registered claim; no other subject; outcome in the frozen set, never `BLOCKED`, never `SUPPORTED` under the deviation, `HISTORICAL-ONLY` only for a historical-only artifact; `independence = reduced-maintainer-adjudicated`; no inventory artifact still classified `stale` |
| `publication.release-notes.sha256` | hash of the rendered notes (CRLF to LF, trailing whitespace trimmed, one final newline); the GitHub release's tag and title must both be `v<version>`. Since 0.25 (#1546) every run also renders the candidate's `CHANGELOG.md` `## [<version>]` section itself and requires this hash and a clean wording scan (`G014`, `G012`) |
| `publication.nuget-packages.files` | `{<file name>: sha256}`; the pushed directory holds exactly these files; a version already on nuget.org must match entry for entry except `.signature.p7s` |
| `publication.release-metadata.files` | `{<file name>: sha256}` of the SBOM and provenance JSON |
| `publication.website.treeSha256` | SHA-256 over sorted `<sha256>  <relative path>` lines of the built tree |
| `publication.benchmark-results.files` | `{<repo path>: sha256}` naming exactly the two #1422 (B2) outputs, `website/public/data/benchmark-headline.json` and `bench/phase0-agent-native/commit-stamp-index.json` (any other key set is `G011`); the changed files in the publishing work tree must be exactly these, with these hashes. Since 0.25 (#1547) `approved: true` (the JSON literal) is also required to publish them (`G015`); without it the hashes are adjudicated but nothing may publish them |

Every publication block is schema-checked on every invocation, whichever surface is publishing.
The gate checks the subset of the §8 predicate that a record can show; the full T001-T003 rules stay
with `EvidenceContractValidator.ValidateTerminalRecord`, which #1408's PR runs. The positive control
proves a gate-valid fixture (with a non-empty claim registry) is also valid there.

## 3. Surfaces

All surfaces publish from the one candidate commit named in the record. Order:
adjudication gate, then release test gates, then NuGet push, then the GitHub release, then release
metadata, then website dispatch. Benchmark publication is not on the release path (0.25, #1547):
it is a separate maintainer dispatch, after the release, that the record must also approve.

| Surface (§7 id) | Workflow | Gate | Negative controls |
|---|---|---|---|
| GitHub release (`github-release`) | `publish-nuget.yml` `publish` job; created only after the NuGet push. Body = adjudicated notes + identity trailer. The remote tag is read through the API immediately before and after creation and must be absent or at the candidate. An existing release is accepted only if its body verifies; it is never edited | Gate re-run in the job with `--expect-head --nuget-dir --metadata-dir --release-notes`; existing body with `--release-body` | `ReleaseBodyWithoutTheIdentityTrailerFails`, `ReleaseBodyNamingAnotherIdentityFails`, `ReleaseBodyWithEditedNotesFails`, `TagPointingAwayFromTheCandidateFails`, `ReleaseIsCreatedAfterThePackagesAndBeforeDownstreamDispatch` |
| Release notes (`github-release-notes`) | rendered from the candidate `CHANGELOG.md` in the `publish` job | `--release-notes`: hash plus wording scan | `ChangedReleaseNotesFail`, `IndependentClaimInAdjudicatedNotesFails` (3 cases) |
| NuGet packages (`nuget-packages`) | `publish-nuget.yml`: `workflow_dispatch` from `main` only, required `adjudication_identity`, no `release` trigger, no version override; every job checks out the candidate | `adjudication-gate` job first; `publish` job verifies packages, metadata, notes, and package descriptions before attestation, and compares any version already on nuget.org before `dotnet nuget push --skip-duplicate` | `ChangedPackageFails`, `UnadjudicatedExtraPackageFails`, `RegistryPackageWithOtherContentFails`, `IndependentClaimInThePackageDescriptionFails`, `CheckoutOtherThanTheCandidateFails` |
| SBOM / provenance (`release-metadata`) | `publish` job, generated from the packages and stamped with the candidate SHA; verified before attestation; an existing asset must be byte-identical (no `--clobber`), a missing one is uploaded | same job | `ChangedReleaseMetadataFails`, `PublishWorkflowHasNoVersionOverrideAndNoAssetClobber` |
| Website (`website`) | `nextjs-gh-pages.yml`: dispatch from `main` only, required identity, builds the candidate; dispatched by `publish-nuget` | build job: identity + release body, then `--website-dir website/out`; deploy job checks out the candidate and re-verifies the downloaded Pages artifact before `deploy-pages` | `ChangedWebsiteTreeFails`, `IndependentClaimOnTheAdjudicatedWebsiteFails`, `ObfuscatedIndependentClaimOnTheWebsiteFails` (3) |
| Benchmark publication (`benchmark-publication`) | `benchmark.yml`: dispatch from `main` only with the identity, on the candidate, and (0.25, #1547) only with `publish_results: true`, a separate maintainer action the release path never takes; push runs and other dispatches measure only and never publish. The publishing step runs only after #1422's refusal gate (step `methodology`) passed, so nothing publishes without both. The PR branch is built from the candidate and pushed as verified (no `create-pull-request` re-application onto `main`); #1422's stale-PR cleanup stays the job's last step | identity + release body first; `--benchmark-worktree` in the publishing step, immediately before the commit: the record must approve publication (`G015`), and the changed files must be exactly #1422's two outputs (`benchmark-headline.json` and its `commit-stamp-index.json` entry) with the adjudicated hashes | `UnadjudicatedBenchmarkFileFails`, `ChangedBenchmarkFileFails`, `BenchmarkRecordOtherThanExactlyTheB2OutputsFails` (2), `BenchmarkPublicationWithoutTheRecordsApprovalFails` (3), `BenchmarkPublicationIsASeparateExplicitDispatch`, `ReleasePathKeepsTheTestGatesAndNeverDispatchesBenchmarkPublication` |
| Agent refactoring data (`agent-refactoring-results`) | `benchmark.yml` `agent-refactoring-benchmark`; since #1422 the job is read-only (`contents: read`) and has no commit step, so it publishes nothing (the inventory classifies agent results historical-only) | none needed; any publishing command added to the job without a preceding gate fails the structural control | `EveryPublishingCommandFollowsTheGateInItsJob` |
| Installed-tool check (`installed-tool-verification`) | `verify-release.yml`: dispatch from `main`, required identity; the install matrix checks out the candidate's sample | identity, version, tag, release body | `RequestedVersionOtherThanTheAdjudicatedOneFails`, `MissingRequiredTagFails` |
| Release PR (`release-pr`) | 0.25 (#1546): not a publication surface. It is the candidate-preparation PR, merged before the freeze; its products are bytes of the candidate. See §6 | every run, on every surface, checks the candidate's `Directory.Build.props` version (`G009`) and renders the candidate's `CHANGELOG.md` section, which must be the adjudicated notes and pass the wording scan (`G014`, `G012`) | `CandidateVersionThatDiffersFromTheBuildPropsFails`, `CandidateChangelogThatIsNotTheAdjudicatedNotesFails` (2), `CandidateWithoutTheVersionsChangelogSectionFails` (2), `IndependentClaimInTheCandidateChangelogFailsWithoutAnySurfaceOption` |
| Hand-made release (bypass detector) | `release-audit.yml` on every `release` event; the only workflow that listens to them | body must end with an identity that verifies against its own tag and notes; fails red otherwise; changes nothing | `OnlyTheAuditListensToReleaseEvents` |

Identity-level controls that apply to every surface: `MissingOrMalformedIdentityFails` (4 cases),
`CommitWithoutARecordFails`, `MismatchedRecordHashFails`, `RecordWithAnotherSchemaFails`,
`AdjudicationCommitNotOnMainFails`, `MissingMainRefFails`, `ShallowCloneFailsInsteadOfSkipping`,
`RecordContractHashesThatDifferFromThePacketFail`, `ContractChangedBetweenCandidateAndAdjudicationFails`,
`UnfrozenContractFails`, `FailedMilestoneFails`, `IndependenceDeviationFieldsAreRequired` (4),
`NonReleasableRowOutcomeFails` (4), `RowWithoutReducedIndependenceFails`, `MissingRequiredSubjectFails`,
`DuplicateSubjectFails`, `InventoryThatStillHasStaleArtifactsFails`,
`CandidateThatIsAbsentFails`, `DivergentCandidateThatIsNotAnAncestorFails`,
`ChangedEvidenceManifestHashFails`, `RecordWithoutEvidenceManifestsFails`,
`MissingEvidenceManifestRoleFails` (2), `EvidenceManifestThatDoesNotNameTheCandidateFails`,
`UnregisteredClaimSubjectFails`, `OmittedRegisteredClaimFails`, `BlockedRegisteredClaimFails`,
`ChangedClaimRegistryHashFails`, `RecordWithoutASurfaceFails`,
`EmptyPublicationBlockFailsWithoutAnySurfaceOption` (5).

**Wording.** No surface may call 0.24 evidence "independently adjudicated" or "independently
verified" (§9). The gate scans release notes, the release body, website text files, and changed
benchmark text files, and every text entry of each package (`.nuspec`, the README nuget.org renders) for `independently adjudicated|verified`
and `independent adjudication|verification`. Each text is scanned as written, with HTML entities
decoded, with markup replaced by a space and removed outright, and with Markdown emphasis removed.
JSON files are also scanned as their decoded string values, through the same decoding and markup removal. A phrase is allowed only directly after
`not`, `no`, or `without`, as in the required limitation, and only when that negation is literal in
the source: a negation that appears only after markup is removed (for example a hidden `not `) fails.

**Time of check and time of use.** Each publishing job re-runs the gate against the bytes it
publishes, in the same job, before the publishing step: the `.nupkg` files, metadata, and notes
before attestation and push, the packages nuget.org actually serves (polled after the push and
compared before the release is created), the remote tag around release creation (only a confirmed
404 counts as absent; any other API failure stops the job), the downloaded Pages artifact
before `deploy-pages`, and the work tree immediately before the benchmark commit. The identity names immutable git objects, so a later push
to `main` cannot change what an identity means.

## 4. Codes

`G000` gate could not complete; `G001` malformed identity; `G002` shallow clone or `main` not
fetched; `G003` adjudication commit missing or off `main`; `G004` record missing, hash mismatch,
invalid, or wrong schema; `G005` contract not frozen, packet hashes wrong, or contract changed;
`G006` not `MILESTONE-SUCCEEDED`; `G007` independence fields; `G008` adjudication rows;
`G009` candidate, checkout, version, or tag; `G010` evidence manifests; `G011` surface bytes;
`G012` forbidden wording; `G013` release body; `G014` (0.25) the candidate's `CHANGELOG.md` section
is missing or is not the adjudicated notes; `G015` (0.25) benchmark publication without the
record's `approved: true`; `G016` (#1567) the adjudication map is missing or malformed, the
release is `not-yet-adjudicated`, its contract-packet format is one the gate cannot check, or the
record sits at another release's path. A version that is not in the map (the record's or
`--version`) is a version failure, `G009`. Exit status 1 on any code; there is no
warning-only mode and no override flag.

## 5. What a workflow gate cannot enforce (maintainer settings)

These are repository settings. R2 changes none of them. Recommended, in priority order:

1. **Credentials that old workflow versions cannot reach.** `workflow_dispatch` accepts any branch
   or tag as the ref, and `gh run rerun` replays an old run's workflow file. An older tag (for
   example `v0.9.0`) still holds an ungated `publish-nuget.yml`, and the current repository-scoped
   `NUGET_API_KEY` is visible to it. Moving publication credentials into environments with a
   `main`-only deployment policy (item 3) removes that authority from every older workflow version.
   Until then, the R2 gate covers the workflow files on `main` only.
   **0.25 (#1545), revised 2026-10-19: nuget.org Trusted Publishing, no stored key.** The
   `publish` job keeps `environment: nuget-release` and `id-token: write`. Its first step is the
   official `NuGet/login` action, pinned to commit `8d196754b4036150537f80ac539e15c2f1028841`
   (v1.2.0), with `user: juanmicrosoft`. The action exchanges the job's OIDC token for a one-hour
   key. Step 2 stops the job if no key came back, before it packs, attests, or pushes.
   - The nuget.org policy `calor-release` names repository `juanmicrosoft/calor`, workflow file
     `publish-nuget.yml`, and environment `nuget-release`.
   - nuget.org matches the workflow file name from `job_workflow_ref` and ignores the ref. An
     older tag's `publish-nuget.yml` names no environment, so its token is refused.
   - A non-`main` ref that names the environment is stopped by GitHub's `main`-only deployment
     rule, with admin bypass off, before the job runs. The environment rule is therefore
     load-bearing.
   - The repository `NUGET_API_KEY` is deleted, the old key is revoked, and no environment holds a
     NuGet secret.
   - `scripts/check_release_credentials.py --live` (read-only) checks the workflow and the GitHub
     settings. One setting cannot be read this way: the Actions workflow permissions answer
     HTTP 403 to the maintainer's `gh` login. For it, the check reads a dated maintainer
     attestation and labels the result ATTESTED.
   - Steps and the 2026-10-19 state: `docs/plans/evidence/release-gaps-0.25/README.md`.
2. **Branch protection on `main`:** turn on "Do not allow bypassing the above settings"
   (enforcement currently `non_admins`); add `tests (verification)` (release-critical, carries the
   #1135 oracle) to the required checks. The R2 tests run in `tests (compiler)`, which is already
   required.
3. **NuGet secret scope:** move `NUGET_API_KEY` from repository secrets to an environment (for
   example `nuget-release`) whose deployment policy allows only `main`, and add `environment:` to
   the `publish` job. Today any branch or tag that edits or predates `publish-nuget.yml` can read it.
   (0.25, #1545: `environment:` added; since 2026-10-19 no NuGet secret exists at all, and
   publishing uses Trusted Publishing; see item 1.)
4. **Tag ruleset:** restrict creation, update, and deletion of `v*` tags to the release workflow
   (or to admins). The workflow reads the remote tag immediately before and after creating the
   release, but only a ruleset closes the remaining window.
5. **`github-pages` environment:** restrict deployments to `main`; every gated deploy now runs from
   `main` and checks out the candidate itself.
6. **Release events:** treat a red `release-audit` run as a release that bypassed the gate.

## 6. Release PR and the contract

§7 inventories the release PR as a surface and §8 condition 7 asks every §7 surface to consume the
identity. The release PR bumps the version and writes the notes before #1423 freezes the candidate,
so it necessarily precedes #1408 and cannot consume an identity that does not exist yet. 0.24's #1408
did not waive this (AD-1), and condition 7 failed on it.

**0.25 resolution (#1546): the release PR is dropped as a publication surface.** Of the two options
(freeze first and generate the PR afterwards, or drop the surface), dropping it is the simpler sound
one:

- *Freezing first does not work.* The candidate must already declare the version and carry the
  notes (`G009`, and the notes hash), so a PR generated after the freeze would change the candidate.
- *The release PR publishes nothing.* It pushes no package, creates no tag or release, and deploys
  nothing. It is an ordinary reviewed change to `main`, like any other candidate commit.
- *Its products are candidate bytes, and the identity names the candidate.* There are two: the
  `Directory.Build.props` version and the `CHANGELOG.md` section. Since 0.25 the gate checks both
  on every run, whichever surface runs it and whether or not `--release-notes` is passed. The
  version is checked by `G009`. The section must hash to `publication.release-notes.sha256` and
  pass the wording scan (`G014`, `G012`). A release PR whose notes differ from what #1408
  adjudicated therefore blocks every surface.
- *Its text is not release text.* The PR body is not the release notes and must not restate notes
  or benchmark numbers. `.claude/skills/create-release/SKILL.md` step 4 gives the body to use.
  What users read comes only from the gated surfaces.

The 0.24 contract (#1407) is frozen and is not amended here. The 0.25 contract must say the same
thing: its §7 inventory either lists no `release-pr` surface or classifies it as candidate
preparation checked through `G009`/`G014`. That is a maintainer decision for the 0.25 contract.

## 7. No maintenance-release path

The contract defines no maintenance or non-0.24 release path. §0 authorizes no release or package
publication, and §7 says publication happens "only after #1408 records `MILESTONE-SUCCEEDED`,
through the #1410 gate". R2 therefore adds none. Until #1408 succeeds, the gate blocks NuGet,
GitHub releases, website deployments (including documentation-only updates), benchmark PRs, and the
installed-tool check. A maintenance path would need a #1407 amendment first.

## 8. Open items for later gates

- **Byte equality.** The gate compares the packages, metadata, site, and benchmark files built in
  the release run with the hashes #1424 recorded. Where a build is not byte-reproducible (the
  benchmark generator stamps the run time, for example), the gate fails closed. #1423/#1424 must
  either make these builds reproducible or retain the candidate bytes durably so the release run
  can publish them; R2 does not weaken the comparison. B2's headline also embeds its comparison
  with the headline already on `main`, so the adjudicated headline hash must be computed against
  `main` as it will be at publication, and a re-dispatch after that PR merges fails closed
  (`reviews/refresh-codex.md`).
- **Notes and site come from the candidate.** Adjudicated wording must already be in the
  candidate's `CHANGELOG.md` and `website/`; a post-adjudication edit changes the candidate
  (§8 stopping rule 4).
- **Benchmark methodology refusal** remains #1422 (B2): its refusal gate runs first and R2's
  publishing step sits after it; **provenance identity resolution** remains
  #1417 (P1). The gate reuses the §6 rules (full SHA on fetched `main`, no shallow skip) and does
  not reimplement either.

## 9. The release adjudication map (#1567)

Until #1567 the gate hard-coded three 0.24 values: the record path, `issue == 1408`, and the
#1407 packet. A 0.25 record (#1430) could not pass without editing those constants, and a new
record at the 1408 path would also have broken the override's terminal-record hash check. The
values now live in `eng/release-adjudication-map.json`, a committed, reviewed map keyed by release
version. Each entry names:

| Field | Meaning |
|---|---|
| `version` | `MAJOR.MINOR.PATCH` (exact) or `MAJOR.MINOR.x` (every patch of that minor, prereleases included); keys may not overlap |
| `status` | `adjudicated` or `not-yet-adjudicated`; only `adjudicated` can pass |
| `a1Issue` | the A1 issue whose terminal record governs the release; the record's `issue` must equal it, and its `gate:#<issue>` row is the one not required |
| `terminalRecord` | the record's path; paths are unique across entries |
| `contractPacket` | `{issue, format, dir, contract, inventory, seal}`: the frozen contract the record is adjudicated under, and its hash seal. The gate checks format `calor.evidence-contract-packet/1407` only; any other is `G016` until a reviewed change teaches it |
| `claimRegistry` | the claim-registry path the record must name |
| `maintainerOverride` | 0.24.0 only (any other entry is rejected): the override record `scripts/verify_maintainer_override.py` reads |

**Resolution.** The identity carries no version, so the gate finds the record by bytes: of the
map's record paths at the adjudication commit, exactly one blob must hash to the identity's
SHA-256 (`G004` otherwise). That entry must be `adjudicated` with a supported packet format
(`G016`). The record's `candidate.version` must then be in the map (`G009`) and resolve to the same
entry (`G016`). A `--version` that is not in the map also fails (`G009`). Every later check reads
its paths and issue numbers from the entry.

**Where the map is read.** Both scripts read the map from fetched protected main
(`refs/remotes/origin/main:eng/release-adjudication-map.json`), the ref every run already
requires (`G002`). They never read it from the work tree, and no flag names another map. A map
that is missing at main, is not valid JSON, repeats a JSON member, or breaks any rule above fails
`G016` (the override check: `O006`).

Main, not the candidate or the adjudication commit, because:

- *Not the candidate.* The publishing jobs check out the candidate. The candidate is frozen
  before its A1 record lands, so its copy of the map still says `not-yet-adjudicated`. A gate that
  read the work tree would refuse every surface after the first (Codex round 1, #1567).
- *Not the adjudication commit.* 0.24.0's adjudication commit predates the map. It would have no
  entry to resolve.

So a reviewed change to the map on main changes which records the gate accepts, exactly as a
reviewed change to the gate script does. The identity still names immutable record bytes.

**Entries today.**

- **0.24.0**: `adjudicated`, #1408, the committed MILESTONE-FAILED record, the #1407 packet and its
  `sha256.json` seal, #1424's regeneration-3 claim registry, and the maintainer override. Every
  0.24.0 identity still fails exactly as before (`G006`, `G008`, `G009` on main). The override
  check still passes for v0.24.0; its version lock (`OVERRIDE_VERSION`, O014) stays in the script.
- **0.25.x**: `not-yet-adjudicated`, #1430, record
  `docs/plans/evidence/adjudication-1430/terminal-record.json`, registry
  `docs/plans/evidence/adjudication-1430/claim-registry.json`, and the frozen 0.25 R0 scope packet
  (#1426, `scope.json`, sealed by its `evidenceSeal` block). Every 0.25 identity fails `G016`.
  Which contract governs the 0.25 release is the #1548 maintainer decision; the R0 packet is named
  provisionally. To release 0.25, the PR that lands the #1430 record also sets `status` to
  `adjudicated` and either names a 1407-format packet or adds a reviewed check for the new format.

Tests: `ReleaseAdjudicationMapTests` (a version missing from the map, an entry pointing at a
missing record, a record at another release's path, a claim registry other than the map's, 0.24.0
still resolving, 0.25 failing until adjudicated and passing once a map marks it adjudicated, and
twelve malformed maps), plus `test_explicit_yaml_tags_are_open` for the credential check. Fixture
changes, with no existing assertion changed: the `GateRepo` claim-registry path is now 0.24.0's
real one, because the map binds it; and `GateRepo` and `OverrideRepo` commit the committed map
into the throwaway repository's main, because the scripts read it there.
