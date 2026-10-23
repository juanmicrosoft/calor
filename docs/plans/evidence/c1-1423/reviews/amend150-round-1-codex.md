# Amendment 1.5.0 prep (PR #1537), review round 1 (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given
`git diff origin/main...HEAD` at `df1d335f`. It had read-only repository access, with #1535 and
the archives branch fetched. Under the contract §9 independence deviation, this is an adversarial
tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

- **MAJOR — scripts/determinism_protocol.py:822:** Setting `res = None` discards real observations from the cut profile. The archived cut retained all 1,170 cells and three artifact hashes. A differing observed verdict or digest there is silently hidden: I reproduced `INCOMPLETE` instead of `NON-DETERMINISTIC` with an observed `Failed` against other contributions' `Passed`. **Fix:** exclude synthesized timeout values and the harness-timeout invocation, retain observed values for disagreement detection, and keep establishing contributions at zero. Add controls for partially observed tests, theory rows, cells, and artifacts.

- **MAJOR — scripts/determinism_runner.py:149:** A commit subject does not authenticate PR #1537's merge commit. A single-parent or off-main commit named `Merge pull request #1537 from arbitrary/fake` passes the guard; no ancestry, topology, or authoritative merge-SHA check occurs. **Fix:** resolve the actual merge SHA from trusted PR metadata or protected-main history, require the dispatched SHA to equal it, and fail closed when resolution fails. Add a matching-subject/wrong-commit negative control.

- **MAJOR — scripts/determinism_protocol.py:807:** The elapsed-time predicate disagrees with the runner. `run_profile()` detects a cut before reading results, but records `seconds` afterward. I reproduced a genuine cut at 898.9 seconds under a 900-second process timeout; 0.7 seconds of result processing produced `seconds=899.6`, failed this predicate, and returned `NON-DETERMINISTIC` through filled values. **Fix:** record the timeout cause explicitly, or use the same captured subprocess duration for classification and recording. Add a runner-to-decider boundary control.

Main's D001–D016 validator passes. Both seals and exception hashes verify, and the archived replay reproduces the reported counts.

## Dispositions

All three MAJORs are fixed.

1. **Observed values are kept.** For a harness-cut invocation, the decider now leaves out only what
   the harness filled in:
   - the invocation value;
   - every `Missing`, `Timeout`, or `Crash` fill;
   - `Malformed` cells (a cells file cut mid-write).

   Everything else is compared: test outcomes, cells, and artifact hashes. The attempt stays
   invalid, so it never establishes agreement. A theory row that was only partly observed is
   checked as a sub-multiset of the value the complete observations agree on.

   New controls: `test_harness_cut_keeps_every_value_it_observed` (one each for a test, a cell, and
   an artifact, each `DISAGREE`) and `test_a_partly_observed_theory_row_is_checked_as_part_of_the_full_value`.

   Residual, documented: if the kill truncated an artifact mid-write, that artifact reads
   `DISAGREE`. That fails closed.
2. **The merge commit comes from GitHub's PR record.** `pr_merge_commit` reads the PR from the same
   GitHub API as the run inventory. It returns `merge_commit_sha` only when the PR is merged into
   `main`, and `None` on any failure. The plan step requires the dispatched SHA to equal it.

   Controls: a wrong SHA, a short SHA, garbage, and an unresolved merge are each refused. A record
   that is unmerged, on another branch, empty, or unreadable gives `None`.
3. **One duration, used by both sides.** The runner now records the subprocess duration before it
   reads any result. It decides the cut from that recorded value with `dp.cut_by_deadline`, the same
   function the decider applies.

   Boundary control: `test_the_recorded_duration_is_the_one_the_cut_was_decided_on` simulates a kill
   1.2 s before the process timeout followed by 0.9 s of result processing. The record reads as a
   cut on both sides.

**Replay re-run under the revised rule:** still `INCOMPLETE`, with 0 `DISAGREE` and the 470
`DISAGREE` cases moved to `INCOMPLETE`. The cut invocation's observed cells and artifacts are
compared and agree. The protocol controls pass (62 tests), and both main's validator and this
tree's validator report "protocol valid".
