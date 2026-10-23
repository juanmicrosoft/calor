# 0.24 G3 (#1135): verifier oracle determinism — results packet

Gate G3 of the frozen 0.24 contract (`docs/plans/v0.24-evidence-contract.md`). It consumes the
#1421 protocol (`docs/plans/evidence/g2-1421/protocol.json`). Closure evidence (§10, `contract.json`
`children`): the repaired oracle executed under the protocol on every registered platform, every
attempt retained, agreement under the frozen rule, every `determinismRows` entry resolved.

**Status: not closed.** One execution has run. It was `NON-DETERMINISTIC` and incomplete because
of two defects in the execution machinery. No determinism row is resolved.

## Pull requests

| PR | Content |
|---|---|
| #1492 (merged, `0142438f`) | Repairs: per-check fresh Z3 contexts (GC-dependent id recycling, the #1135 flip), UTF-8 string literals on Windows, LF-only oracle reports and translator fixture, `USERPROFILE`-honoring user-level root; protocol amendment 1.2.0 (the home probe checks that root). Reviews: `reviews/round-*.md`, `reviews/verification-pass*.md` |
| This PR | Execution 1's records and ledger; protocol amendment 1.3.0 (machinery repairs below). Reviews: `reviews/pr2-*.md` |

## Execution 1: `g3-exec-1`

| Field | Value |
|---|---|
| Run | [37177387101](https://github.com/juanmicrosoft/calor/actions/runs/37177387101), `workflow_dispatch` on `main`, run attempt 1, no job re-run |
| Commit | `0142438f42278021d95c6169dfe397a7f63eea3c` (#1492 merge) |
| Protocol | 1.2.0 (`protocolSha256` `3e1c3d9e…`) |
| Runner-minutes | 388 (plan 1, decide 1, attempt jobs 22–46) |
| Verdict | **`NON-DETERMINISTIC`**, `complete: false` |
| Classes | 1,580 `DISAGREE`, 557 `INCOMPLETE`, 0 `AGREE-PASS`, 0 `AGREE-FAIL` |
| Attempts | 53 completed, 7 invalid (harness deadline), 90 missing (never uploaded) |
| Determinism rows | all four `OPEN` |

Files: `executions/g3-exec-1/result.json` (the decider's output, unchanged) and
`executions/g3-exec-1/attempts.tar.gz` (every retained attempt record and `env.json`: 60
attempts of the four Windows jobs). SHA-256 values are in `ledger.json`. The raw TRX files and
per-invocation directories stay in the run's Actions artifacts (90 days) and are not committed.

### What went wrong (machinery, not the verifier)

1. **No Linux or macOS record exists.** linux-x64, linux-arm64, and osx-arm64 (6 jobs) ran every
   attempt (`run-job` exited 0), but `actions/upload-artifact` rejected each output directory:
   NuGet had written HTTP-cache files named with `:` into the isolated homes inside it. The
   rejection uploads nothing, so the decider filled 90 attempts as missing. The 1.1.0 machinery
   already did this (reproduced locally with main's runner before #1492); no execution had run
   before.
2. **Windows ran out of job time.** Each Windows job spent 1.2 to 3.0 minutes before its first
   attempt, then about 3.1 minutes per attempt (median 156 s `verification-full` plus 29 s
   `oracle-isolated`; up to 201 s and 43 s). Fifteen attempts did not fit in the 45 minutes the
   harness allows under a 50-minute timeout. One to three late attempts per job were cut or not
   started. A cut invocation keeps what it observed (all four cut `verification-full` invocations
   kept the passing translator-fixture value, and three kept passing cells and report hashes) and
   records `Timeout` for the rest, and the decider compares invalid attempts. **All 1,580
   `DISAGREE` cases come from those harness-generated `Timeout` and `Missing` values differing
   from the retained observations**; `artifact:translator-fixture` is `INCOMPLETE`, not
   `DISAGREE`.

### What Windows observed (not established; recorded)

Among the 53 completed Windows attempts (win-x64 26, win-arm64 27), every observed value equals
the pass value:

- Every registered Calor.Verification.Tests name is `Passed`, including
  `IntegrationTests.StringInBodyOnly_StillNeverElides` (the G1 Windows `Calor0712` row).
- All 1,170 cells have one value per cell, including `case-000307` (the #1135 flip).
- Both regenerated reports equal the committed bytes: JSON `6e86b43a…`, Markdown `428edcf8…`
  (the G1 CR-bytes row).
- The translator fixture equals the committed `hash|SemanticsVersion` (`f79711fd…`; the G1
  fixture-hash row).
- Every completed invocation is `0|Completed`. No attempt was an `environment-violation`; the
  amendment-1.2.0 home probe passed on both Windows platforms.

This bounds nothing under the protocol: the execution is not `DETERMINISTIC`, and Linux and macOS
contributed no value.

## Amendment 1.3.0 (this PR)

See `docs/plans/evidence/g2-1421/README.md`. Isolated homes are removed before upload (and
`fill-missing` fails if a path the upload rejects remains); the probe builds beside the output
directory; win-x64 and win-arm64 job timeouts are 75 minutes; `maxDispatchedControlRuns` is 0
so 3 × 655 = 1,965 fits the 2,000-minute ceiling. The decider's handling of harness-cut
invocations is unchanged and recorded as a known inconsistency.

## Next execution and where its records go

The protocol requires a version to be merged before it governs an execution (`freeze`;
#1421 acceptance), so execution 2 cannot run on this PR's head. After this PR merges, execution 2
is dispatched once on the merge commit. Its records go into C2's (#1424) regeneration evidence,
not a third G3 pull request (maintainer decision 2026-10-04: G3 stays within its 2 PRs).

S2 (#1413) repairs (#1494–#1498) change the verifier and the committed oracle reports after
execution 2's commit. Execution 2 is G3 closure evidence for its own commit only; the
candidate-time protocol run is C2's (charged to `regeneration-compute`, contract amendment 1.2.0).

## Budget

13 (G2 dry run) + 388 (g3-exec-1) = **401 of 2,000** runner-minutes; 1 of 3 executions; 0
dispatched control runs. Worst case of the two executions left: 401 + 2 × 655 = 1,711.
