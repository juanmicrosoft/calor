# #1421 G2 determinism protocol — adversarial review round 1 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run
from the worktree root with the diff against `origin/main` on stdin (`cases.json` omitted from the
diff but readable on disk). **Reviewed commit:** `aa771215`. **Verdict:** 6 BLOCKING, 9 MAJOR,
0 MINOR — request changes.

No registered case was run while addressing these findings.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Relative `CALOR_DETERMINISM_RECORD_DIR` resolves against the test host's directory, so every cell/artifact would read `Missing` | **Moved to the second G2 PR** with the runner; `workflow.requirements` now requires absolute record paths |
| 2 | BLOCKING | Re-execution of a NON-DETERMINISTIC commit allowed; pooling caller-selected; repairs and `pooledWith` trusted | **Fixed in rules; enforcement in PR 2.** Pooling removed: a commit is executed at most once (`retryPolicy.reexecuteCommit: false`, D007). PR 2's plan must refuse an executed commit and check repairs with git (`workflow.requirements`) |
| 3 | BLOCKING | Budget trusted a hand-written ledger; no reservation; `regeneration-compute` bypass; negative minutes | **Fixed in rules; enforcement in PR 2.** Every `workflow_dispatch` run is charged to `determinism-compute`, counted from the API run inventory with unfinished runs' worst case reserved; the `charge` input is gone; non-negative minutes from job timestamps (`budget.charges`, `executions.ledgerCheck`). #1424's charge is an open question |
| 4 | BLOCKING | Malformed records could decide DETERMINISTIC (presence-only schema, trusted `unregistered`, multiplicity unchecked, duplicate profiles) | **Fixed.** `_record_problem` enforces exact fields, profile order and uniqueness, exact test names and multiplicities, outcome tokens, exact cell and artifact key sets and value forms, and environment-check/status consistency; 13 negative controls |
| 5 | BLOCKING | Records not bound to the candidate commit or run | **Fixed.** `decide` requires the dispatched full SHA and one run id per execution; records from another commit, run, or schema are INVALID |
| 6 | BLOCKING | Rehashing allowed post-hoc changes without an amendment | **Fixed.** D016 validates the packet against the one on `origin/main` in the required `calor-first-guard` job: a change needs a new version and appended entry (earlier entries immutable), and may not remove cases, environments, rows, or attempts |
| 7 | MAJOR | Timeout discarded an existing TRX; per-attempt writes lost earlier observations; invalid attempts' values dropped | **Fixed (values, decider); runner part in PR 2.** `profile_values` keeps every observed value and fills only unobserved ones; the decider compares values from `invalid` attempts; per-profile record writes are a PR 2 requirement |
| 8 | MAJOR | Exit code and TRX run outcome ignored | **Fixed.** New case `invocation:<profile>` (`exit|ResultSummary outcome`, pass `0|Completed`) |
| 9 | MAJOR | Duplicate cell ids kept the last value | **Fixed.** Duplicate, unregistered, or malformed cells are flagged `Malformed` and listed as unregistered (INVALID) |
| 10 | MAJOR | Translator hash only in a failure message; theory rows collapse | **Fixed / limitation.** `TranslatorOutputMatchesCommittedBaseline` writes `translator-fixture` (hash and version) before asserting; registered artifact mapped to its row. Identical theory display names stay a multiset per name: any failure is detected (all are expected `Passed`), but not which row — recorded in `limitations` |
| 11 | MAJOR | Tasks VerifyGate, TaskGenAddressability, SDK canary omitted | **Declared.** `scope.notCovered` lists each with a reason; nothing may cite this protocol for them; adding one is an amendment |
| 12 | MAJOR | `setup-dotnet` may add a floating LTS runtime to the private root | **PR 2 requirement.** An install procedure that installs nothing else; the environment check already rejects any extra runtime (fail closed). The dry run saw exactly 10.0.12 on all five runners |
| 13 | MAJOR | Row-to-test mapping and `publish-nuget.yml` unchecked | **Fixed.** D003 requires each contract row's own test; D014 checks every profile project in `publish-nuget.yml` too |
| 14 | MAJOR | Memory limits not registered | **Fixed.** `memoryGiB` per environment (D004) and `resources.rule`; enforcement in PR 2's environment check |
| 15 | MAJOR | Over the 1,500-line ceiling (counting `freeze_cases.py`) | **Fixed.** Split into the two G2 PRs §9 allows; `freeze_cases.py` removed (procedure in README). This PR: 911 changed lines outside `docs/plans/evidence/` before review fixes of later rounds |
