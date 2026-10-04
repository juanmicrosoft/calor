# 0.24 B2 (#1422): benchmark publication refuses incomparable results

**Gate:** B2. **Epic:** #1409. **Contract:** v0.24 evidence contract 1.1.1, sections 5, 6, 7, and 10 (B2).
**Inputs:** B1 registration and results (`docs/plans/evidence/b1-1276/`, generator
`b1-1276-results-generator-v1`), P1 durable provenance (`tests/Calor.Compiler.Tests/Provenance/`).

## What changed

- `scripts/benchmark_publication_gate.py` is the refusal gate. It re-checks the B1 packet and writes
  the only publishable headline, `website/public/data/benchmark-headline.json`, as a projection of
  `results.json` (it computes no number), plus a complete durable identity for it in
  `bench/phase0-agent-native/commit-stamp-index.json`. It has no override option.
- Method changes. The accepted method is the B1 registration (merge commit `f0e0eb68…`). Any other
  comparability key, or another registration, needs a `contract.json` amendment-log entry with a
  structured `benchmarkMethodAuthorization` {`comparabilityKeySha256`,
  `supersedesComparabilityKeySha256`: [...], `registrationCommit`?}. It must be present on `main`
  before the first-parent commit that landed the current `results.json`, and still present and not
  withdrawn. A hash in prose authorizes nothing. Across keys, no delta is printed and the headline
  is labeled `comparableWithPublished: false`.
- `.github/workflows/benchmark.yml`, benchmark job: `fetch-depth: 0`; `concurrency:
  benchmark-publication` (no cancel); `statistical_runs` passes through a validated environment
  variable (the same text as R2 #1475). The legacy static, statistical, and LLM outputs are uploaded
  as diagnostics, then discarded (`git reset --hard`, `git clean -fd`). B1's own C# validator
  (`BenchmarkResultsTests`, `BenchmarkRegistrationTests`) runs, then the registered generator
  (`pair-metrics` twice, `pair-results`), then the gate under `set -euo pipefail` with no pipe, as
  step `methodology`. The PR step keeps its default `success()` condition. The last step,
  `if: failure()`, closes every open `benchmark-results*` PR (paginated; every close attempted).
  `push.paths` includes every gate input. `allow_weaker_methodology` and the methodology checker's
  `--allow-weaker` are removed.
- Agent refactoring job: `contents: read`; no commit or push step; no `continue-on-error`; a
  missing pass rate fails the job instead of writing `passRate: 0`.
- Website: each component that shows a pre-0.24 benchmark number carries `HistoricalMethodLabel`
  ("Historical, not comparable under the 0.24 method"). No number is deleted or rewritten, and this
  PR renders no B1 number on the website.

## Refusals and their tests (`scripts/test_benchmark_publication_gate.py`, 50 tests)

| Code | Refuses when | Test(s) |
|---|---|---|
| B2-01 | a sealed file differs; a results file (including an oracle run) is unsealed; the manifest's oracle hashes do not match the oracle runs | `test_seal_mismatch_is_refused`, `test_an_unsealed_oracle_run_is_refused`, `test_an_oracle_run_that_the_manifest_does_not_name_is_refused` |
| B2-02 | the registration is not the accepted one, is not on `origin/main`, or changed since | `test_registration_changed_after_its_merge_is_refused`, `test_registration_commit_not_on_main_is_refused`, `test_a_reregistration_needs_a_prior_amendment` |
| B2-03 | a registered pair's file differs from its registered SHA-256 | `test_changed_pair_content_blocks_the_headline` |
| B2-04 | a manifest row's registered fields changed, or the manifest hashes disagree | `test_changed_pair_manifest_row_is_refused` |
| B2-05 | an included pair is not EQUIVALENT; an EQUIVALENT benchmarks pair is left out; counts do not follow from the manifest | `test_included_unclassified_pair_blocks_the_headline`, `test_equivalent_pair_left_out_of_the_population_is_refused`, `test_counts_that_do_not_follow_from_the_manifest_are_refused` |
| B2-06 | the key differs from the registration without a prior authorization (reduced run count, changed metric set, changed aggregation) | `test_reduced_run_count_is_a_method_change`, `test_changed_metric_set_is_a_method_change`, `test_changed_aggregation_without_an_amendment_is_refused`, `test_amendment_for_another_key_does_not_authorize`, `test_a_hash_mentioned_in_prose_does_not_authorize`, `test_an_amendment_in_the_same_commit_as_the_results_is_not_prior`, `test_amendment_and_results_merged_together_from_one_branch_are_not_prior`, `test_a_withdrawn_amendment_does_not_authorize`, `test_an_amendment_withdrawn_after_the_results_no_longer_authorizes` |
| B2-07 | the headline on `origin/main` has another key and no prior authorization supersedes it (exact list member); same key, different numbers; a published headline without a valid key | `test_amended_key_that_does_not_name_the_published_key_is_refused`, `test_supersession_must_be_an_exact_list_member`, `test_same_key_different_numbers_is_refused`, `test_published_headline_without_a_key_is_refused` |
| B2-08 | provenance commit is short, not HEAD, not on fetched `origin/main`, the clone is shallow, or an input differs from `origin/main`'s (freshness) | `test_short_sha_provenance_is_refused`, `test_commit_not_on_main_is_refused`, `test_shallow_clone_is_refused`, `test_an_older_checkout_with_different_inputs_is_refused` |
| B2-09 | any file other than the headline and the stamp index differs from HEAD | `test_any_other_changed_file_is_refused`, `test_without_the_restore_step_a_diagnostic_file_is_refused` |
| B2-10 | the regenerated packet is missing or differs byte for byte | `test_regeneration_that_differs_is_refused` |
| B2-11 | the packet lacks the registered population, sampling unit, interval label, or the no-correctness label | `test_label_that_does_not_deny_correctness_is_refused` |

Positive paths: `test_consistent_packet_writes_one_headline_with_limits_and_provenance`,
`test_same_key_regeneration_is_comparable`, `test_an_older_checkout_is_compared_with_the_headline_on_main`,
and `test_method_change_by_prior_amendment_is_published_as_incomparable` (labeled INCOMPARABLE, no delta).

Workflow tests execute the workflow's own `run:` scripts with `bash -e` (GitHub's default shell)
against a fixture repository with an origin: a refusal exits nonzero and writes no report output;
the restore step discards diagnostics and the gate then passes; the cleanup step (with a mocked
`gh`) closes every listed PR, fails when listing fails, and attempts every close before failing.
Structural tests: every step that opens a PR, commits, or pushes follows the gate with no
`always()`/`failure()`/`cancelled()`; cleanup is the job's last step; no dispatch input is
interpolated into a script or bypasses the gate; no push targets `main`; every gate input is a push
path; the agent job reads ANSI-colored output and fails on missing output. `RealPacketTests` runs the
content checks on the committed B1 packet (17 included of 217 sent to the oracle, 226 registered).
The suite runs in CI in the `calor-first-guard` job (`.github/workflows/test.yml`, full history).

## Mutation check

`mutation-check.txt`: 47 mutations, one per guard (each gate check disabled or weakened, the step's
`pipefail` removed, `|| true` added, `continue-on-error`, `if: always()` on the PR step, a bypass
input, an interpolated input, the agent `|| echo "0"` fallback, an agent push, the restore step
emptied, a push before the gate, cleanup before publication, cleanup listing swallowed, a close
failure ignored, concurrency cancelling). All 47 are killed.

## Local end-to-end runs

`local-e2e.md`: the registered generator re-run is byte-identical; B1's validator passes (58); the
gate passes on the real repository (in a clone where the branch head stands in for the merged
commit) and writes the headline; P1's verifier passes (23) against the index entry the gate wrote.

## Interaction with R2 (#1410)

B2 owns refusal; R2 owns the adjudication identity, the release trigger, and the publication step,
and merges last. B2 keeps step id `methodology` and its `report` output, which R2's publication
step reads, and inserts its steps before the publication step, so R2's gate sits after B2's
refusal. The `statistical_runs` hardening uses R2's exact text. Expected rebase conflicts for R2,
each resolved by keeping B2's text: the checkout `fetch-depth` comment, the removed methodology step
(R2 had moved `allow_weaker_methodology` into an env var), the `Create PR` step R2 replaces (keep
the cleanup step after R2's replacement, as the last step), and the agent job's commit step (B2
deletes it; R2 had gated it). R2's `--benchmark-worktree` allow-list must admit the gate's two
outputs, `website/public/data/benchmark-headline.json` and
`bench/phase0-agent-native/commit-stamp-index.json`.

## Limits

- Regeneration re-runs `pair-metrics` and `pair-results` on the committed oracle outputs; it does not
  re-run the pair oracle (about 30 minutes from a clean checkout of the registration merge commit).
  B1's validator re-derives the reconciliation from both raw oracle runs instead.
- `create-pull-request` itself is not executed in tests; its position and condition are tested.
- Freshness compares the gate's inputs at HEAD with `origin/main` at the moment of the gate step.
  A change merged to `main` after that step re-runs the workflow through `push.paths`.
- No workflow was run on GitHub, no PR was opened by the workflow, and nothing was published.
  `PR #1226` (a pre-B2 publication PR) is not touched by this change; the next failed run's cleanup
  would close it, or the maintainer can.
