# B2 (#1422) PR 2 review round 2 (Codex, read-only, reasoning effort high)

Command: as in round 1, on the diff at `0365804c`. The prompt also asked the reviewer to verify
the round-1 fixes. It raised no finding against them. Verdict: **REQUEST-CHANGES** (1 MAJOR).

| # | Severity | Finding (summarized) | Response |
|---|---|---|---|
| 1 | MAJOR | Freshness compared `git rev-parse REV:path` object ids, and those ignore the tree-entry mode. Main could replace the contract seal with a symlink whose target string equals the JSON bytes. The blob id would be unchanged, so B2-08 would pass, although the seal on main is unreadable. | `check_inputs_fresh` now compares whole tree entries: `git ls-tree --full-tree REV -- path` gives mode, type, and object id. A directory's tree id already covers the modes of the entries inside it. New test: `test_an_input_replaced_by_a_symlink_with_the_same_bytes_is_refused`. It asserts that the blob id is unchanged on main and that B2-08 names the seal. Mutation P21 (back to object ids only) is killed. |

Suite after the fix: 63 tests OK.
