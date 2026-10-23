# #1421 G2 PR 2 (execution machinery) — adversarial review round 1 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run
from the worktree root with `git diff origin/main...HEAD` on stdin. **Reviewed commit:** `33f1f25a`
(PR #1486). **Verdict:** 6 BLOCKING, 15 MAJOR, 1 MINOR — request changes.

No registered case was run and no control run was dispatched while addressing these findings.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `observe` shadowed its `out` parameter; every env-check crashed | **Fixed.** Helper renamed; `test_environment_check_records_its_observations` runs `cmd_env_check` end to end with a fake `dotnet` |
| 2 | BLOCKING | The validator imported the runner, so main's extracted validator would crash after merge and import untrusted code | **Fixed.** `workflow_problems` and `judge` live in `determinism_protocol.py`; the validator imports nothing from the runner |
| 3 | BLOCKING | Whitespace splitting of `git diff --name-only` let a docs-only change pass as a code repair | **Fixed.** `git diff --name-only -z`, split on NUL; control includes `docs/fix notes.md` |
| 4 | BLOCKING | Records bound only the validator, not the runner or workflow | **Fixed.** `harnessSha256` is `harness_digest` over the validator, runner, and workflow (decider and runner); recorded in `recordFormats.harnessSha256` |
| 5 | BLOCKING | A copied workflow could call the unchanged runner outside the inventory | **Fixed.** Every command requires this workflow's `GITHUB_WORKFLOW_REF`; plan requires the current run in the inventory under its own title; D013 rejects any other workflow that calls the runner |
| 6 | BLOCKING | Deleting a run erased history | **Mitigated.** Plan refuses when a ledger run is missing from the inventory, uses the larger of ledger and API minutes, and keeps ledgered executions as executed; `ledger-check` reconciles both ways. Residual: a run deleted before the ledger records it (`workflow.residual`) |
| 7 | MAJOR | Workflow check accepted removed `needs`, replaced commands, masked status | **Fixed.** Exact job keys, step names and order, runner command or action per step, conditions, and no `||` anywhere outside expressions; controls for each |
| 8 | MAJOR | Comments, anchors, flow mappings bypassed `fail-fast` | **Fixed.** Strategy compared line for line; trailing comments, anchors, aliases, tags, merge keys, flow mappings, and document markers are rejected |
| 9 | MAJOR | Inventory completeness failed open | **Fixed.** `total_count` required, duplicates and short reads fail closed, current run required |
| 10 | MAJOR | Minutes undercharged malformed jobs; unfinished runs reserved only the worst case | **Fixed.** Reversed or unfinished jobs in a finished run fail closed; unfinished runs count measured minutes plus the worst case |
| 11 | MAJOR | Control limit ignored unstarted dispatches | **Fixed.** Every dispatched control run counts |
| 12 | MAJOR | Charged work starts before the budget check; re-runs reached setup | **Partly fixed.** Every job's first step refuses a re-run before any action. A plan job's own minutes cannot be avoided; recorded as residual |
| 13 | MAJOR | Checkout, env-check, and bootstrap used the real home | **Fixed.** The first step of every job moves `HOME`, `USERPROFILE`, `DOTNET_CLI_HOME`, the global git config, and NuGet packages under the runner temp directory |
| 14 | MAJOR | `setup-dotnet@v4` installs a floating LTS runtime first | **Fixed (deviation recorded).** `install-sdk` runs setup-dotnet's bundled install script, pinned by commit and SHA-256, with only the SDK version |
| 15 | MAJOR | Home probe used PowerShell's runtime and failed open | **Fixed.** The probe is a file-based app on the pinned runtime; any failure is `False`, which is a violation on every OS. The Windows known-folder behaviour is a recorded limitation |
| 16 | BLOCKING | The decider trusted an empty violations list | **Fixed (decider amendment).** `_record_problem` re-judges the observations and rejects `calorCacheExisted` true; seven new INVALID controls |
| 17 | MAJOR | Private-root containment was a substring test | **Fixed.** Parsed listing paths compared by path components (`ntpath` for Windows, case-insensitive) |
| 18 | MAJOR | No tree check between build and first attempt; git errors read as clean | **Fixed.** `run_job` checks before the first invocation (a modified tree is an environment-violation); git failures are dirty |
| 19 | MAJOR | The step timeout did not reserve cleanup time | **Fixed.** `run_job` stops starting and cuts invocations 5 minutes before the job timeout, measured from the job's first step |
| 20 | MAJOR | Exceptions while reading record files lost TRX observations | **Fixed.** TRX values are kept and the invocation is `invalid`; a malformed TRX is a crash value |
| 21 | MINOR | Generated failure records were inconsistent with the decider | **Fixed.** Every generated status is checked against `_record_problem` in the controls |
| 22 | MAJOR | Several controls did not isolate their guard | **Fixed.** Re-run controls use otherwise valid inputs and match the refusal text; new controls for env-check, mocked API pagination, multi-profile intermediate writes, deadline, and existing-record preservation |

Also merged `origin/main` (contract amendment 1.2.0). Its #1424 charge rule cannot be applied by
`plan` yet (the #1423 manifest and #1424 ledger do not exist), so plan over-counts; recorded in
`workflow.deviations`.
