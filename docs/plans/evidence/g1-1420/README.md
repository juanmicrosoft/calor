# 0.24 G1 (#1420): hermetic Z3 assets for every consumer — closure evidence

Gate G1 of the frozen 0.24 contract (`docs/plans/v0.24-evidence-contract.md`, contract
1.0.1). This packet maps the contract's G1 closure evidence (§10, `contract.json`
`children[3].closureEvidence`) and the #859 residual recorded in §11 to the change that
closes each item and the check that keeps it closed. It does not amend the contract.

Sections consumed: §3 (inventory: `z3-upstream-pins`, `z3-release-binaries`,
`sdk-consumer-check`), §8 (missingness: skipped is never established), §9 (capacity: 1 PR,
under 1,500 changed lines excluding evidence data, $0), §10 (G1 row), §11 (#859 row).

## The #859 residual, item by item

| §11 residual | Status | Change | Kept closed by |
|---|---|---|---|
| Native propagation uses the evaluation-time `runtimes\**\*` glob plus the `AfterTargets="Build"` `CopyZ3NativeToOutput` side copy | Fixed | `Calor.Compiler.csproj` target `AddZ3AssetsToOutput` creates every Z3 output item at execution time (`BeforeTargets="AssignTargetPaths"`, `DependsOnTargets="ValidateZ3Assets"`), from an explicit RID list. The glob, the `Exists()`-guarded `Microsoft.Z3.dll` item, the compiler side copy, and `Calor.Tasks`' `CopyZ3NativeToTasksOutput` side copy are removed. | `scripts/test_z3_hermetic.py` (`test_compiler_project_creates_z3_items_only_at_execution_time`, `test_tasks_project_has_no_z3_side_copy`); the `test` job step "Late Z3 bootstrap still reaches a referencing test host" |
| Each workflow bootstraps separately (9 of 12 workflow files) | Fixed | One owned composite action, `.github/actions/bootstrap-z3`, replaces all 22 per-job bootstrap and seed steps (19 jobs) plus the `publish` job's z3-binaries download, in 7 workflow files. With the new `z3-consumer-matrix` job, 21 jobs use it. No workflow calls `download-z3.*` directly. | `test_workflows_seed_z3_only_through_the_owned_action`: every job that runs a `dotnet` build/test/run/pack/restore/publish/msbuild command or a package consumer probe uses the action exactly once, before its first `dotnet` command; no other step mentions `download-z3`, `z3-binaries`, `Z3Prover/z3`, `libz3`, or the Z3 source directories, except registered negative controls. |
| Two provenance paths: upstream archives (`z3-upstream-4.15.7.sha256`) for development, the `z3-binaries-4.15.7` release (`.github/z3-binaries-4.15.7.sha256`) for publishing | Fixed (acquisition) | `publish-nuget.yml` `publish` now packs from the same owned bootstrap as every test and consumer job. The bootstrap verifies each upstream archive against `z3-upstream-4.15.7.sha256`, then each extracted output against `.github/z3-binaries-4.15.7.sha256`. Both pins stay; there is one acquisition path. The packed bytes are checked again after pack (`scripts/check-packaged-z3.py`, all supported RIDs, both packages). | `test_publish_packs_the_bootstrapped_assets`, `test_packaged_z3_checker_fails_closed` |
| No RID consumer matrix proves zero silent Z3 skips for every consumer | Fixed | `eng/z3-consumers.json` registers the supported RIDs, the unsupported RIDs with reasons, and every consumer × RID cell. `tests/Shared/Z3ConsumerGuardTests.cs` (4 facts, no skip path) is linked into all 13 projects in `eng/test-manifest.json`. New `test.yml` job `z3-consumer-matrix` runs test hosts on linux-arm64, osx-arm64, win-x64, and win-arm64. `cli-tool-consumer` gains linux-arm64 and win-arm64. Package probes check packaged bytes against the pins. | `test_registered_matrix_covers_every_supported_rid`, `test_every_registered_test_project_links_the_guard` |
| #859's MSBuild-time download | Remains rejected | No change. `ValidateZ3Assets` and every target are checked for `DownloadZ3`, `curl`, `Invoke-WebRequest`, and URLs. | `test_compiler_project_creates_z3_items_only_at_execution_time`; the existing `test_build_project_has_no_network_or_tracked_resource_mutation_targets` |

## G1 closure evidence (contract §10)

**Consumer and RID inventory.** `eng/z3-consumers.json`:

- Supported RIDs: linux-x64, linux-arm64, osx-arm64, win-x64, win-arm64.
- Unsupported, with reasons: osx-x64 (upstream x64-osx archive ships an arm64 binary),
  win-x86 (no .NET 10 SDK; pinned, never copied or packed).
- Consumers and cells:

| Consumer | linux-x64 | linux-arm64 | osx-arm64 | win-x64 | win-arm64 |
|---|---|---|---|---|---|
| Test hosts (13 projects, guard in each) | all 13 (`remaining-tests`, `id-validation`, `performance.yml`, `publish-nuget.yml` `test`) | Verification + Tasks (`z3-consumer-matrix`) | same | same | same |
| Calor.Sdk package | `sdk-package-consumer`, `sdk-consumer` | same | same | same | same |
| calor tool package | `cli-tool-consumer` | same (new) | same | same | same (new) |

- Projects: `projects.entries` lists every non-test project whose `ProjectReference`
  closure reaches Calor.Compiler (12, with a role and coverage or exclusion reason for
  each). `test_every_z3_consuming_project_is_registered` recomputes the closure and
  requires it to equal the manifest's 13 test projects plus these 12.
- Post-publication: `verify-release.yml` (manual, installs the published tool; builds
  nothing) is registered in `postPublicationProbes`.

Off linux-x64, test hosts other than Calor.Verification.Tests and Calor.Tasks.Tests are
not run. Every test host loads Z3 through the same `AddZ3AssetsToOutput` items, and the
two that run cover a direct and a transitive (`Calor.Tasks`) reference; this is recorded
as the registered coverage, not as a claim about the other 11 projects on those RIDs.

**No unowned Z3 seeding workaround.** See the second residual row. The z3-binaries
producer (`build-z3.yml`) and drift monitor (`z3-pin-check.yml`) are registered in
`producersAndMonitors`; neither runs `dotnet` or writes into `src/Calor.Compiler/{z3,runtimes}`.

**No implicit network download.** Builds stay network-free: the `test` job builds and
packs behind a dead proxy (unchanged), and `ValidateZ3Assets` fails on missing or wrong
assets instead of fetching.

**Discriminating fresh-checkout tests.**

| Case | Check | Result before this change | Result after |
|---|---|---|---|
| Absent assets fail before tests | `test` step "Missing Z3 fails before compilation without downloading" (existing) | fails the build | fails the build |
| Wrong asset fails | `test` step "Wrong Z3 asset fails before compilation" (new; corrupts the linux-arm64 native, which the x64 host never loads) | — | fails with `checksum mismatch` |
| Wrong asset in a test host | `Z3ConsumerGuardTests` (hash of output-root and `runtimes/<rid>/native` libz3 and `Microsoft.Z3.dll`) | — | 2 facts fail (local control: native replaced with another RID's bytes) |
| Bootstrap lands after MSBuild evaluation | `scripts/z3-late-bootstrap-probe.proj` + guard, `test` step "Late Z3 bootstrap still reaches a referencing test host" | build succeeds, test host has `Microsoft.Z3.dll` and no libz3, guard 2 of 4 fail (old csproj, local osx-arm64) | 6 natives propagate, guard 4 of 4 pass |
| Unsupported RID | `HostRidIsASupportedZ3Rid` | — | fails on any RID outside the supported set |
| Mislabeled runner | `CALOR_Z3_EXPECTED_RID` in `z3-consumer-matrix` | — | fails on a RID mismatch |
| Package carries wrong/unsupported/root native | `scripts/check-packaged-z3.py` | — | fails (5 synthetic mutations in `test_packaged_z3_checker_fails_closed`) |

## Windows platform divergences (found, not repaired)

The first `z3-consumer-matrix` runs on win-x64 and win-arm64 (run 37020428891) loaded Z3
and passed the guard, but 3 Calor.Verification.Tests cases gave Windows-only results:

- `IntegrationTests.StringInBodyOnly_StillNeverElides`: Windows reports Calor0712
  (postcondition may be violated, counterexample `result=1`) where Linux and macOS report
  the expected `Assumed` demotion. This is a platform-dependent verifier verdict.
- `ContractTranslatorSemanticsVersionGuardTests.TranslatorOutputMatchesCommittedBaseline`:
  translator fixture hash differs on Windows (same `SemanticsVersion`).
- `VerifierRuntimeDifferentialTests.CommittedReportsMatchGeneratedOracle`: the regenerated
  report contains CR bytes on Windows.

These are not Z3-delivery failures, and their repair belongs to the determinism and
soundness gates (#1421, #1135, #1419). They are registered by exact name in
`testHosts.platformDivergences`; the job deselects only those on Windows, and its TRX
check requires the exact remaining count (407 of 410) to pass with zero skips.

## Local results (osx-arm64, this branch)

Recorded in the PR body; CI on the PR is authoritative for the other RIDs.

## Not done here

- The z3-binaries release is still produced by `build-z3.yml` and watched by
  `z3-pin-check.yml`, but nothing builds or packs from it any more. Whether the inventory
  row `z3-release-binaries` should describe it as a pinned mirror is an amendment
  question for the maintainer, not something this gate changes.
- `sdk-consumer-check` still emits no result record (#1410 owns that defect).
- `download-z3.sh`/`.ps1` still fetch the win-x86 archive, which is verified but unused.
