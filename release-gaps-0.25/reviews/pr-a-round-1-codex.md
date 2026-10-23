# PR A (#1545, #1548), Codex round 1

Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, reviewing
`git diff origin/main...HEAD` at a2d9a8d08. Raw final message: `pr-a-round-1-codex.raw.txt`.

**Verdict: REQUEST-CHANGES** (2 MAJOR, 2 MINOR). Codex found no way past the 0.24.0 override pin.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | Secret listings were read from the first page only, and `total_count` was ignored. A 31st secret could hide `NUGET_API_KEY`. | Fixed. `gh_fetch` reads every page (100 per page). `listed()` returns UNKNOWN unless the entries add up to `total_count`. Tests: `test_listing_short_of_its_total_count_is_unknown`, `test_live_fetch_reads_every_page`. |
| 2 | MAJOR | A mapping-form `environment: {name: nuget-release}` and `secrets['NUGET_RELEASE_API_KEY']` got past both the script and the C# test. | Fixed. The scan is now conservative. Another job fails if its environment mentions `nuget-release`, if its name is an expression, alias, or flow mapping, or if the name is missing. Any mention of the key in any syntax fails, and so does any mention outside a parsed job. The C# test now runs the script, plus 5 C# mutation cases. Python: `test_other_syntaxes_for_the_key_or_environment_are_open` (6 cases). |
| 3 | MINOR | The empty-key guard could be disabled (`continue-on-error`, `if:`) or moved after setup without any test noticing. | Fixed. The guard is now the publish job's first step. The script checks that it is first, has no `if:` and no `continue-on-error`, and exits 1 (`W007`). Test: `test_suppressed_moved_or_conditional_guard_is_open` (4 mutants). |
| 4 | MINOR | Deleting the repository secret does not take the value back from runs already queued. | Fixed in the README, step 5: revoking the key ends that access, and the check cannot verify revocation. |
