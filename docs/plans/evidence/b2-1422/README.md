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

## PR 2: freshness scoped to headline inputs; deterministic headline

**Why.** 0.24 C2 (#1424, PR #1517) ran the gate on the frozen candidate `696ab824` and got
`B2-08`: `tests/Calor.Compiler.Tests/EvidenceContract at HEAD differs from refs/remotes/origin/main`.
C1 (#1508) had added candidate-invalidation tests to that directory after the candidate. Those
files are not benchmark inputs, but PR 1 listed the whole directory. The release flow publishes from
the adjudicated candidate, not from main HEAD (R2: `benchmark.yml` checks out the candidate after
`verify_release_adjudication.py`), so main will always have moved on by publication time. A second
problem: `check_published` read the baseline headline from `origin/main`, so the
`comparableWithPublished`/`comparison` fields, and so the headline's bytes, depended on main's later
state. R2's `--benchmark-worktree` step compares those bytes with the hashes A1 adjudicated at the
candidate, so a main-dependent byte breaks that comparison.

**New rule (B2-08 freshness).** HEAD is the candidate. It may publish while main has moved on, but
only if no headline input differs between HEAD and `origin/main`. Each input's whole tree entry is
compared (`git ls-tree`: mode, type, object id), so a file replaced by a same-bytes symlink differs,
and a path missing on one side differs. The inputs are `INPUT_PATHS`, every file the candidate's
three seals name, and every `calorPath`/`csharpPath` in the candidate's `registration/pairs.json`
(452 files):

| Input | Why it is an input |
|---|---|
| `docs/plans/evidence/b1-1276/registration`, `.../results` | the registered packet and the results packet the headline projects (B1 review records in `.../reviews` are not inputs) |
| `docs/plans/evidence/evidence-contract-1407/contract.json` | method authorizations (B2-06) |
| `.../evidence-contract-1407/sha256.json`, and every file the registration, results, and contract seals name (`docs/plans/b1-1276-benchmark-registration.md`, `docs/plans/v0.24-evidence-contract.md`, `artifact-inventory.json`, ...) | what B2-01 checks (review round 1) |
| `tests/TestData/Benchmarks` and each registered pair file | the corpus the registration names (B2-03) |
| `tests/Calor.Evaluation` | the registered generator (`pair-metrics`, `pair-results`; the metric uses no `src/` code) |
| `EvidenceContract/Benchmark{Registration,Results}{Tests,Validator}.cs`, `EvidenceContractValidator.cs`, `EvidenceContractTests.cs`, `tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj` | B1's validator as the workflow runs it: the two filtered test classes, the partial validator class they call, the helpers they use (`RepoRoot`, `Contract`), and the project that compiles them |
| every `.cs` file under `tests/Calor.Compiler.Tests` that declares a part of `EvidenceContractValidator`, `EvidenceContractTests`, `BenchmarkResultsTests`, or `BenchmarkRegistrationTests`, at HEAD or on main | a new part can rebind an unchanged call to a better overload (verification pass) |
| `scripts/benchmark_publication_gate.py` | the gate that writes the headline |

Not inputs: other files in `EvidenceContract/` (C1's `CandidateInvalidation*.cs`,
`CandidateManifestTests.cs`), `src/` (B2-10 re-runs the generator at the candidate, and the headline
records the candidate's `src` tree hash), build settings (`Directory.Build.props`, `global.json`),
and `benchmark.yml` (a dispatch runs main's copy of the workflow anyway). The sealed contract
document is an input, so an amendment to the contract on main after the candidate refuses
publication of that candidate. This is intended: the amendment may change what B2-06 accepts.
`push.paths` lists every input outside the corpus directory, so a change to one re-runs the gate.

**New rule (B2-07 against main).** The comparison is now with the headline published *at the
candidate* (`HEAD:website/public/data/benchmark-headline.json`). Main is fenced separately. Main's
headline (exact bytes) and its headline stamp-index entries (every entry with `path` = the headline
and `stampPointer` = `/provenance/commit`, in order) must each be unchanged since the candidate, or
be exactly what this run writes (this candidate's publication PR already merged). Anything else
refuses: a newer or different headline, an added or duplicated entry, or an unreadable index. So a
candidate cannot publish over a newer headline on main. This keeps round-1 finding 4 of PR 1
closed. An older checkout cannot avoid the comparison, because a headline on main that the
checkout lacks refuses unless it is byte for byte this run's own output. Other entries in the
stamp index on main do not count.

**Deterministic bytes.** The headline is rendered from the candidate's packet, the candidate's
published headline, and `git` object ids at the candidate. The stamp index is the candidate's
committed index (read with `git show HEAD:`, not from the work tree) plus this entry. Both files are
written as exact UTF-8 bytes (`write_bytes`, LF). Nothing reads the clock or main's later state.
R2 needs no change: `verify_release_adjudication.py --benchmark-worktree` still compares the two
files' sha256 with the adjudicated hashes. Those hashes now stay valid after main moves on.

### Tests (`scripts/test_benchmark_publication_gate.py`, 64 = 50 + 14)

Changed because the rule changed:
- `test_an_older_checkout_is_compared_with_the_headline_on_main` is replaced by
  `test_a_candidate_whose_headline_already_reached_main_rewrites_the_same_bytes`. The old test
  required the bytes to follow main's headline, which is the nondeterminism being removed. The new
  one re-runs the publishing candidate after its headline merged. It must pass, report the comparison
  against HEAD, and write exactly the bytes main already has.
- `WorkflowTests.test_no_dispatch_input_is_interpolated_into_a_shell_script`: the assertion that the
  whole `EvidenceContract` directory is an input becomes three assertions. The directory is *not* an
  input. Every test class in the workflow's validator filter is. Every input matches a `push.paths`
  glob, evaluated with GitHub's `*`/`**` semantics.
- Fixture: it also writes the validator files, an unrelated `CandidateManifestTests.cs`, and a
  sealed contract document outside every `INPUT_PATHS` entry.

New:
- Positive: `test_unrelated_test_files_on_main_after_the_candidate_do_not_block`. This is the C2
  case: C1-style tests added and edited in `EvidenceContract/`, plus `src/` and docs changes, on
  main after the candidate. It passes, and the bytes equal those written with main at the candidate.
  Also `test_other_stamp_index_entries_on_main_do_not_block_or_change_the_bytes`.
- Negative: `test_every_headline_input_changed_on_main_is_refused` covers 16 cases: registration,
  results, contract, contract seal, a sealed document named only by a seal, corpus directory,
  registered pair file, generator, the 6 validator files, the project file, and the gate. Each starts
  from the same candidate. Each must refuse with B2-08 and nothing else, and the case count is
  pinned. Also: `test_a_generator_change_on_main_is_refused`,
  `test_an_input_replaced_by_a_symlink_with_the_same_bytes_is_refused`,
  `test_a_different_headline_published_on_main_after_the_candidate_is_refused` (B2-07),
  `test_a_changed_headline_stamp_entry_on_main_is_refused` (B2-07),
  `test_a_duplicate_headline_stamp_entry_on_main_is_refused` (B2-07),
  `test_an_unreadable_stamp_index_on_main_is_refused` (B2-07, 3 shapes),
  `test_a_registered_pair_file_outside_the_corpus_directory_is_an_input`, and
  `test_a_new_validator_partial_class_file_on_main_is_refused` (verification pass; one file in
  `EvidenceContract/` and one elsewhere in the test project).
- Determinism: `test_two_runs_write_identical_bytes` (a second run over the first run's outputs, and
  a third run from a clean tree) and `test_the_bytes_depend_on_the_committed_stamp_index_not_the_work_tree`.
- Real repository: `RealPacketTests.test_every_headline_input_exists`. Every input exists, so a
  renamed input cannot go silently absent on both sides. The three sealed documents are inputs. The
  input count is pinned (`INPUT_PATHS` and the sealed files, plus 2 x 226). Every input outside the
  corpus directory matches a push path.

### Mutation check (`mutation-check-pr2.txt`)

The script is `mutation-check-pr2.py`. It makes 22 mutations, each disabling or weakening one new
guard, and runs the full suite after each one. All 22 are killed:
- the registered pair paths, the files the seals name, the contract seal, one validator file, files
  declaring a validator partial class, the generator, or the gate itself dropped from the inputs;
- the freshness check disabled, or comparing object ids only so modes are ignored;
- the whole `EvidenceContract` directory made an input again;
- the comparison read from main's headline;
- main's headline fence disabled, or narrowed so it no longer allows the idempotent re-run;
- the stamp-entry fence disabled, comparing the whole index, comparing only the first entry, or
  treating an unreadable index as absent;
- the stamp index read from the work tree;
- a run timestamp added to the headline;
- three `push.paths` triggers removed or broadened.

### Local end-to-end on the real repository (`local-e2e-pr2.md`)

The generator re-run is byte-identical to the committed packet, and B1's validator and R2's
workflow-gate tests pass (79 of 79). With main simulated in local clones, the candidate writes the
same headline (`b2d297d2…`) and stamp index (`8b6c1097…`) in four cases: main at the candidate, a
second run, main with an unrelated `EvidenceContract/` test added (the C2 case), and main with this
candidate's headline already merged. The pre-PR-2 gate refuses the C2 case with B2-08. In the
merged case it writes a different headline (`6cc3cf87…`), which is the nondeterminism removed here.
Main changing the generator or a registered pair file refuses with B2-08.

### Reviews (`reviews/pr2-*.md`)

Codex, read-only, reasoning effort high. Round 1: REQUEST-CHANGES (sealed files were not inputs;
only the first stamp entry was compared). Round 2: REQUEST-CHANGES (object ids ignored file modes).
Round 3: APPROVE. Verification pass (2026-10-08, after a Codex usage-limit wait): REQUEST-CHANGES,
1 MAJOR, no BLOCKING. A new file declaring another part of `EvidenceContractValidator` could rebind
an unchanged call to a better overload, and the README's limit said otherwise. Fixed in
`bca13caf`, with no further review round (`reviews/pr2-verification-pass-codex.md`).

### Limits

- The whole `tests/Calor.Evaluation` project and the whole corpus directory are inputs, so a main
  change there that cannot affect the B1 metric (an LLM-benchmark task file, for example) still
  refuses an older candidate. This errs toward refusing.
- `src/`, `Directory.Build.props`, and `global.json` are not inputs. The B1 metric does not call the
  compiler, and B2-10 re-runs the generator at the candidate.
- Validator partial classes. Some `.cs` files under `tests/Calor.Compiler.Tests` declare a part of
  `EvidenceContractValidator`, `EvidenceContractTests`, `BenchmarkResultsTests`, or
  `BenchmarkRegistrationTests`; `partial_validator_files` finds them with `git grep`. Every such
  file, at HEAD or on main, is an input, so a part added only on main refuses. The match is
  textual. A part declared through unusual formatting is missed, for example a comment between
  `partial` and `class`, or a fully qualified name. A non-partial type cannot change those calls:
  they are static calls on these classes, and extension methods never apply to a static call.
- The end-to-end hashes are for the code commit `bca13caf`. The re-frozen 0.24 candidate gets its
  own hashes, which A1 adjudicates. No workflow was run on GitHub, and nothing was published.
