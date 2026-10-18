# Release prep 0.25, PR 1 (#1567): Codex reviews

**Budget:** at most 3 rounds plus 1 verification pass. **Used:** 2 invocations (round 1, round 2).
Each invocation: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt on
stdin. Raw final messages: `pr1-round-1-codex.raw.txt`, `pr1-round-2-codex.raw.txt`.

## Round 1 (reviewing a9c884526): REQUEST-CHANGES, 3 MAJOR

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | The map was read from beside the script. The publishing jobs check out the candidate, which is frozen before its A1 record and map change land, so later surfaces would read a stale `not-yet-adjudicated` map and fail. | **Fixed** (8151fb0a4). Both scripts read the map from fetched `refs/remotes/origin/main`, which every run already requires (`G002`). The `benchmark.yml` copy step was reverted. Fixtures commit the map into their throwaway main. `release-gate.md` §9 records the choice and why not the adjudication commit (0.24.0's predates the map). |
| 2 | MAJOR | Only collection tags were rejected. A scalar tag (`!!null "${{ secrets.NUGET_RELEASE_API_KEY }}"`) erases the text the checks look for. | **Fixed.** Any explicit tag on any scalar or collection is `W012`. `test_explicit_yaml_tags_are_open` covers 13 forms, including `!!null`, `!!binary`, a tagged key, `!`, `!!map`, and `!!seq`, plus an untagged control that passes. |
| 3 | MAJOR | The map loader accepted duplicate JSON members (`"status"` twice, last wins). | **Fixed.** `object_pairs_hook` rejects duplicates (`G016`). New malformed-map rows: duplicate `status`, duplicate packet `format`, invalid JSON, and a map missing from main. |

## Round 2 (reviewing 8151fb0a4): APPROVE

No actionable findings. Codex confirmed the round-1 fixes, the 0.24.0 simulation (`G006` + 9
`G008` + `G009`, override PASS), fail-closed 0.25 entries, the documentation of main-tip map
semantics, and that no existing assertion changed.

No verification pass was needed: round 2 approved with no findings to verify.
