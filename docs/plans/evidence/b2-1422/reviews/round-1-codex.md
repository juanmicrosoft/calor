# B2 (#1422) review round 1 (Codex, read-only, reasoning effort high)

Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" "<prompt>" < <git diff origin/main...HEAD>`.
Verdict: **REQUEST-CHANGES** (4 BLOCKING, 3 MAJOR).

| # | Severity | Finding (summarized) | Response |
|---|---|---|---|
| 1 | BLOCKING | `statistical_runs` was interpolated into shell in two steps, so a `$(...)` value could run with write credentials before the gate. | Both steps take `RUNS` from the environment and validate `^[1-9][0-9]{0,3}$`, the same text as R2 #1475. `test_supply_chain.py` updated as R2 does. New test: no `${{ inputs.* }}` inside any `run:` script. |
| 2 | BLOCKING | Amendment authorization was a substring match; a rejected or prose mention of a hash authorized a method change. The "prior" test committed amendment and results together. | Authorization is a structured `benchmarkMethodAuthorization` object in a non-withdrawn amendment-log entry, read from `contract.json` at the parent of the commit that last changed `results.json`. Tests: prose mention, same-commit amendment, withdrawn amendment, other key; all refused. |
| 3 | BLOCKING | Cleanup swallowed a failed `gh pr list` inside `for $(...)`, had no pagination, ran only after gate failure; no concurrency. | Listing captured in a variable under `set -euo pipefail`, `--limit 1000`, `if: failure()`; job-level `concurrency: benchmark-publication`, `cancel-in-progress: false`. Executed with a mocked `gh`: closes every listed PR; fails when listing fails. |
| 4 | BLOCKING | The published baseline was read from HEAD, so an older checkout could evade comparison. | Baseline is `refs/remotes/origin/main:website/public/data/benchmark-headline.json`. Test: an older checkout is compared with main's headline. |
| 5 | MAJOR | Registration immutability trusted the manifest's `registrationCommit`. | Accepted registrations are the B1 merge commit `f0e0eb68…` (constant in the gate) plus a `registrationCommit` named by a prior authorization. Test: a re-registration without an amendment is refused. |
| 6 | MAJOR | Only four results files had to be sealed; oracle evidence was not validated. | Every results file must be sealed; the manifest's oracle hashes must match the oracle runs; the workflow runs B1's validator (`BenchmarkResultsTests`, `BenchmarkRegistrationTests`, 58 tests, passing locally) before regeneration. Tests for an unsealed oracle run and a swapped oracle run. |
| 7 | MAJOR | Regeneration and cleanup/publication were not exercised. | Cleanup is now executed (mocked `gh`). Regeneration needs the .NET build; it is run in the workflow and was run locally byte-identical (`local-e2e.md`). The real-packet test no longer compares the results directory with itself. Creation of the PR itself (`peter-evans/create-pull-request`) is not executed in tests; the structural test requires it to follow the gate with a `success()` condition. |
| - | wording | "Fails closed" overstated. | CHANGELOG and PR body now list the refusal conditions and say adjudication is a separate gate (#1410). |

The reviewer found the separate `methodology` step and its `report` output compatible with R2, and no TSX defect.
