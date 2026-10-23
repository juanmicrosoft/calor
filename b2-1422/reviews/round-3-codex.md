# B2 (#1422) review round 3 (Codex, read-only, reasoning effort high)

Verdict: **REQUEST-CHANGES** (1 BLOCKING, 1 MAJOR). The reviewer confirmed as addressed: exact-list
supersession, the first-parent authorization lookup, current withdrawal filtering, final-step
cleanup, pagination, and attempting every close.

| # | Severity | Finding (summarized) | Response |
|---|---|---|---|
| 1 | BLOCKING | The freshness fence omitted `tests/Calor.Compiler.Tests/EvidenceContract`, whose validators the workflow runs; an older checkout could use an older validator. | Added to `FRESHNESS_PATHS`. Test asserts the workflow runs `BenchmarkResultsTests` and that the validator directory is fenced. |
| 2 | MAJOR | Push paths omitted the gate's inputs, so a merged withdrawal or packet change need not re-run the gate and its cleanup. | `push.paths` adds the B1 packet, the contract packet, the validators, and the gate script. Test: every freshness path is a push path. |

The reviewer also probed a case where `results.json` is reverted to earlier bytes after an amendment
merged; the landing found is the reverting commit, and the amendment did merge before it. This was
not raised as a finding and is unchanged.
