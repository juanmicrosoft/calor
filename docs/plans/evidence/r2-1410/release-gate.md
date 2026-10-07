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
  `refs/remotes/origin/main`). It holds the #1408 terminal record at
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
| `issue` | `1408` |
| `outcome` | `MILESTONE-SUCCEEDED` (a `MILESTONE-FAILED` record never passes) |
| `adjudicationIndependence` | `reduced` while the §9 deviation stands |
| `limitation` | the contract's `publishedLimitation`, verbatim |
| `epicIndependentAdjudicationMet` | `false` |
| `contract` | `{version, files}`; `files` equals `sha256.json` `files` at the adjudication commit, and the packet tree is identical at the candidate and the adjudication commit |
| `candidate` | `{commit, version}`; full SHA, an ancestor of the adjudication commit; `Directory.Build.props` at the candidate declares `version` |
| `evidenceManifests` | `[{role, path, sha256}]` with exactly one `candidate-manifest` (#1423) and one `raw-artifact-freeze` (#1424); each blob at the adjudication commit has that SHA-256 and is JSON whose top-level `candidate` is the candidate SHA or `{"commit": <candidate SHA>}` |
| `claimRegistry` | `{path, sha256}` of the #1424 claim registry `{"claims": ["claim:<id>", ...]}` committed at the adjudication commit |
| `adjudications` | exactly one row per subject: every inventory artifact, `gate:#<issue>` for every child but #1408, and every registered claim; no other subject; outcome in the frozen set, never `BLOCKED`, never `SUPPORTED` under the deviation, `HISTORICAL-ONLY` only for a historical-only artifact; `independence = reduced-maintainer-adjudicated`; no inventory artifact still classified `stale` |
| `publication.release-notes.sha256` | hash of the rendered notes (CRLF to LF, trailing whitespace trimmed, one final newline); the GitHub release's tag and title must both be `v<version>` |
| `publication.nuget-packages.files` | `{<file name>: sha256}`; the pushed directory holds exactly these files; a version already on nuget.org must match entry for entry except `.signature.p7s` |
| `publication.release-metadata.files` | `{<file name>: sha256}` of the SBOM and provenance JSON |
| `publication.website.treeSha256` | SHA-256 over sorted `<sha256>  <relative path>` lines of the built tree |
| `publication.benchmark-results.files` | `{<repo path>: sha256}` naming exactly the two #1422 (B2) outputs, `website/public/data/benchmark-headline.json` and `bench/phase0-agent-native/commit-stamp-index.json` (any other key set is `G011`); the changed files in the publishing work tree must be exactly these, with these hashes |

Every publication block is schema-checked on every invocation, whichever surface is publishing.
The gate checks the subset of the §8 predicate that a record can show; the full T001-T003 rules stay
with `EvidenceContractValidator.ValidateTerminalRecord`, which #1408's PR runs. The positive control
proves a gate-valid fixture (with a non-empty claim registry) is also valid there.

## 3. Surfaces

All surfaces publish from the one candidate commit named in the record. Order:
adjudication gate, then release test gates, then NuGet push, then the GitHub release, then release
metadata, then website and benchmark dispatch.

| Surface (§7 id) | Workflow | Gate | Negative controls |
|---|---|---|---|
| GitHub release (`github-release`) | `publish-nuget.yml` `publish` job; created only after the NuGet push. Body = adjudicated notes + identity trailer. The remote tag is read through the API immediately before and after creation and must be absent or at the candidate. An existing release is accepted only if its body verifies; it is never edited | Gate re-run in the job with `--expect-head --nuget-dir --metadata-dir --release-notes`; existing body with `--release-body` | `ReleaseBodyWithoutTheIdentityTrailerFails`, `ReleaseBodyNamingAnotherIdentityFails`, `ReleaseBodyWithEditedNotesFails`, `TagPointingAwayFromTheCandidateFails`, `ReleaseIsCreatedAfterThePackagesAndBeforeDownstreamDispatch` |
| Release notes (`github-release-notes`) | rendered from the candidate `CHANGELOG.md` in the `publish` job | `--release-notes`: hash plus wording scan | `ChangedReleaseNotesFail`, `IndependentClaimInAdjudicatedNotesFails` (3 cases) |
| NuGet packages (`nuget-packages`) | `publish-nuget.yml`: `workflow_dispatch` from `main` only, required `adjudication_identity`, no `release` trigger, no version override; every job checks out the candidate | `adjudication-gate` job first; `publish` job verifies packages, metadata, notes, and package descriptions before attestation, and compares any version already on nuget.org before `dotnet nuget push --skip-duplicate` | `ChangedPackageFails`, `UnadjudicatedExtraPackageFails`, `RegistryPackageWithOtherContentFails`, `IndependentClaimInThePackageDescriptionFails`, `CheckoutOtherThanTheCandidateFails` |
| SBOM / provenance (`release-metadata`) | `publish` job, generated from the packages and stamped with the candidate SHA; verified before attestation; an existing asset must be byte-identical (no `--clobber`), a missing one is uploaded | same job | `ChangedReleaseMetadataFails`, `PublishWorkflowHasNoVersionOverrideAndNoAssetClobber` |
| Website (`website`) | `nextjs-gh-pages.yml`: dispatch from `main` only, required identity, builds the candidate; dispatched by `publish-nuget` | build job: identity + release body, then `--website-dir website/out`; deploy job checks out the candidate and re-verifies the downloaded Pages artifact before `deploy-pages` | `ChangedWebsiteTreeFails`, `IndependentClaimOnTheAdjudicatedWebsiteFails`, `ObfuscatedIndependentClaimOnTheWebsiteFails` (3) |
| Benchmark publication (`benchmark-publication`) | `benchmark.yml`: dispatch from `main` only with the identity, on the candidate; push runs measure only and never publish. The publishing step runs only after #1422's refusal gate (step `methodology`) passed, so nothing publishes without both. The PR branch is built from the candidate and pushed as verified (no `create-pull-request` re-application onto `main`); #1422's stale-PR cleanup stays the job's last step | identity + release body first; `--benchmark-worktree` in the publishing step, immediately before the commit: the changed files must be exactly #1422's two outputs (`benchmark-headline.json` and its `commit-stamp-index.json` entry) with the adjudicated hashes | `UnadjudicatedBenchmarkFileFails`, `ChangedBenchmarkFileFails`, `BenchmarkRecordOtherThanExactlyTheB2OutputsFails` (2) |
| Agent refactoring data (`agent-refactoring-results`) | `benchmark.yml` `agent-refactoring-benchmark`; since #1422 the job is read-only (`contents: read`) and has no commit step, so it publishes nothing (the inventory classifies agent results historical-only) | none needed; any publishing command added to the job without a preceding gate fails the structural control | `EveryPublishingCommandFollowsTheGateInItsJob` |
| Installed-tool check (`installed-tool-verification`) | `verify-release.yml`: dispatch from `main`, required identity; the install matrix checks out the candidate's sample | identity, version, tag, release body | `RequestedVersionOtherThanTheAdjudicatedOneFails`, `MissingRequiredTagFails` |
| Release PR (`release-pr`) | manual, before the candidate freeze. See §6: it cannot consume an identity that does not exist yet | the gate checks the candidate's `Directory.Build.props` version and its rendered notes | `CandidateVersionThatDiffersFromTheBuildPropsFails`, `ChangedReleaseNotesFail` |
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
`G012` forbidden wording; `G013` release body. Exit status 1 on any code; there is no
warning-only mode and no override flag.

## 5. What a workflow gate cannot enforce (maintainer settings)

These are repository settings. R2 changes none of them. Recommended, in priority order:

1. **Credentials that old workflow versions cannot reach.** `workflow_dispatch` accepts any branch
   or tag as the ref, and `gh run rerun` replays an old run's workflow file. An older tag (for
   example `v0.9.0`) still holds an ungated `publish-nuget.yml`, and the current repository-scoped
   `NUGET_API_KEY` is visible to it. Moving publication credentials into environments with a
   `main`-only deployment policy (item 3) removes that authority from every older workflow version.
   Until then, the R2 gate covers the workflow files on `main` only.
2. **Branch protection on `main`:** turn on "Do not allow bypassing the above settings"
   (enforcement currently `non_admins`); add `tests (verification)` (release-critical, carries the
   #1135 oracle) to the required checks. The R2 tests run in `tests (compiler)`, which is already
   required.
3. **NuGet secret scope:** move `NUGET_API_KEY` from repository secrets to an environment (for
   example `nuget-release`) whose deployment policy allows only `main`, and add `environment:` to
   the `publish` job. Today any branch or tag that edits or predates `publish-nuget.yml` can read it.
4. **Tag ruleset:** restrict creation, update, and deletion of `v*` tags to the release workflow
   (or to admins). The workflow reads the remote tag immediately before and after creating the
   release, but only a ruleset closes the remaining window.
5. **`github-pages` environment:** restrict deployments to `main`; every gated deploy now runs from
   `main` and checks out the candidate itself.
6. **Release events:** treat a red `release-audit` run as a release that bypassed the gate.

## 6. Release PR and the contract

§7 inventories the release PR as a surface and §8 condition 7 asks every §7 surface to consume the
identity. The release PR bumps the version and writes the notes before #1423 freezes the candidate,
so it necessarily precedes #1408 and cannot consume an identity that does not exist yet. R2 does not
resolve this by exemption: the gate checks the release PR's products (the candidate's version and
its rendered notes) against the record. Whether that satisfies condition 7, or the release PR row
needs a #1407 amendment, is a maintainer decision.

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
  can publish them; R2 does not weaken the comparison.
- **Notes and site come from the candidate.** Adjudicated wording must already be in the
  candidate's `CHANGELOG.md` and `website/`; a post-adjudication edit changes the candidate
  (§8 stopping rule 4).
- **Benchmark methodology refusal** remains #1422 (B2): its refusal gate runs first and R2's
  publishing step sits after it; **provenance identity resolution** remains
  #1417 (P1). The gate reuses the §6 rules (full SHA on fetched `main`, no shallow skip) and does
  not reimplement either.
