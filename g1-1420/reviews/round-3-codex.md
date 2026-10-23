# G1 (#1420) review round 3 — Codex (cross-family adversarial)

- Reviewed SHA: `7291c29192d733c1a1d334583d5bae9d47d0df68`
- Command and prompt: as in round 1 (`git diff origin/main` on stdin).
- This is the last round allowed by the contract's §9 ceiling (3 review rounds per PR).

## Verbatim output

1. **MAJOR — The matrix does not prove delivery to every registered consumer on every supported RID.** [.github/workflows/test.yml:1324](.github/workflows/test.yml#L1324) builds only Verification and Tasks on the four additional RIDs, leaving **44 test-host × RID cells untested**. [scripts/test_z3_hermetic.py:315](scripts/test_z3_hermetic.py#L315) checks coverage of the aggregate `test-hosts` category, not individual projects. Consequently, [README.md:19](docs/plans/evidence/g1-1420/README.md#L19) marks the “every consumer” residual fixed while lines 44–47 acknowledge missing coverage. **Fix:** run the four guard facts in all 13 registered test projects on every supported RID, require a passing TRX for each project/RID cell, and reject omitted cells. Full suites are unnecessary for these delivery probes.

2. **MINOR — The Tasks TRX guard does not require the task-output deployment test.** [.github/workflows/test.yml:1352](.github/workflows/test.yml#L1352) requires only five passing results; [scripts/test_z3_hermetic.py:118](scripts/test_z3_hermetic.py#L118) checks the minimum count and four shared guard names. An in-memory TRX containing those four facts plus an unrelated passing test was accepted despite omitting `VerifyGate_NativeZ3_DeployedToTasksOutputRoot`. Thus the claimed enforcement can lose the check of the actual Tasks output directory. **Fix:** require that deployment test by its full name, enforce the exact selected inventory, and add a negative control substituting an unrelated passing result.

## Dispositions

1. **MAJOR — accepted, fixed.** `z3-consumer-matrix` now builds every project listed by `scripts/test_z3_hermetic.py --list-test-projects` (the 13 in `eng/test-manifest.json`) and runs each one's `Z3ConsumerGuardTests` on every non-linux-x64 RID. Each project's TRX must contain exactly 4 results, all passing, with all 4 guard facts present (`--check-trx … --exact 4`). With linux-x64's full suites, all 65 test-host × RID cells are exercised. `test_platform_divergences_are_narrow_and_enforced` requires the job to use the project list in the build, run, and check steps. The registry's matrix cells and the closure packet now state this coverage, and the "fewer projects off linux-x64" caveat is gone. A local replica of the run and check steps on osx-arm64 passes for all 13 projects.
2. **MINOR — accepted, fixed.** The Tasks deployment check is a separate TRX that must contain exactly 1 passing result, named `Calor.Tasks.Tests.CompileCalorIntegrationTests.VerifyGate_NativeZ3_DeployedToTasksOutputRoot` (`--exact 1 --no-guard --require <name>`). New negative controls in `test_guard_trx_check_rejects_skips_and_missing_facts`: an unrelated passing result substituted for the required test is rejected, and an extra result beyond an exact count is rejected.

Final BLOCKING count after round 3: 0 open (round 1's one BLOCKING and round 2's one BLOCKING were fixed in the following commits).
