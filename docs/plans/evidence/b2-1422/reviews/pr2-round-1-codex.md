# B2 (#1422) PR 2 review round 1 (Codex, read-only, reasoning effort high)

Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" "<prompt>" < <git diff origin/main...HEAD>`
(diff at `e2ec148a`). The prompt asked for paths that publish a stale or incomparable headline,
input sets that are too narrow or too broad, nondeterministic bytes, and broken R2 integration.
Verdict: **REQUEST-CHANGES** (1 MAJOR, 1 MINOR).

| # | Severity | Finding (summarized) | Response |
|---|---|---|---|
| 1 | MAJOR | The freshness set omits files `check_seals` (B2-01) reads: the contract seal itself, `docs/plans/b1-1276-benchmark-registration.md`, `docs/plans/v0.24-evidence-contract.md`, and `artifact-inventory.json`. Main could change one, and the candidate would still pass on its older copy. | `CONTRACT_SEAL` joins `INPUT_PATHS`, and `input_paths` adds every file that the candidate's three seals (`SEALS`) name. `push.paths` gains the two sealed documents. Tests: the every-input test gains the contract seal and a sealed document outside every `INPUT_PATHS` entry (16 cases). `RealPacketTests.test_every_headline_input_exists` requires the three named files to be inputs, pins the count, and requires every non-corpus input to match a push path. Mutations P16, P17, and P20 are killed. |
| 2 | MINOR | `stamp_entry` returned the first matching entry, so a second headline entry appended on main passed. An unreadable index collapsed to "absent". | `stamp_entries` returns every headline entry (and every malformed entry) in order. An index that is not an object with a `publicationStamps` list becomes the marker `<unreadable>`, which equals no readable index. Tests: `test_a_duplicate_headline_stamp_entry_on_main_is_refused` and `test_an_unreadable_stamp_index_on_main_is_refused` (3 shapes). Mutations P18 and P19 are killed. |

Suite after the fixes: 62 tests OK.
