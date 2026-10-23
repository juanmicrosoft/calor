# Amendment 1.5.0 prep (PR #1537), review round 2 (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given
`git diff origin/main...HEAD` at `7b9fca7d`, with read-only repository access. Under the contract
§9 independence deviation, this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

- **MAJOR — scripts/determinism_protocol.py:346:** The elapsed-time heuristic still misclassifies genuine job-deadline cuts. I reproduced `run_job()` selecting a 899.5-second deadline for a 900-second process timeout. `run_profile()` records `timeout`; the attempt's reason becomes "the job deadline was reached before this invocation." The decider recognizes no cut and reports `NON-DETERMINISTIC` through filled values (1,643 `DISAGREE`). Process termination delays can widen this failure window. **Fix:** record which timeout bound actually triggered termination and use that cause for classification, rather than elapsed duration. Add runner-to-decider controls for a deadline 0.5 seconds before the process timeout and delayed termination.

- **MINOR — docs/plans/evidence/g2-1421/protocol.json:561:** The active `limitations` list still says harness-cut Timeout values are compared and produce `DISAGREE`. Every 1.5.0 result inherits this statement, contradicting the amended rule and README. **Fix:** explicitly label it as behavior before 1.5.0, retain the historical results, and re-seal the packet.

## Dispositions

1. **MAJOR: fixed. The cause comes from the bound, not from the measured time.**
   - **The rule.** `run_profile` gives each invocation `min(process timeout, time left before the job
     deadline)`. A timeout counts as a job-deadline cut exactly when the deadline was the smaller of
     the two. The measured duration plays no part.
   - **How the cause reaches the record.** `run_profile` returns the cause in a private `_cut` key.
     `run_job` removes that key before writing any record, and when the cause is a cut it records the
     harness-cut reason. The decider identifies a cut from the reason, the `invalid` status, the
     `timeout|` invocation, and the profile being last. `cut_by_deadline` (the duration test) is
     removed.
   - **Controls.**
     - `test_a_cut_is_decided_by_the_bound_that_ended_the_invocation` covers four cases, each with
       0.9 s of slow result processing: a deadline 0.5 s before the process timeout with a kill 2 s
       late (a cut); an ordinary cut; the process-timeout bound with a late end (a timeout); and the
       process-timeout bound with an early end (a timeout).
     - `test_a_deadline_cut_is_recorded_with_the_harness_cut_reason` checks that `run_job` records
       the reason, stops the remaining attempts, and never writes `_cut`.
2. **MINOR: fixed.** The limitation now reads "Before amendment 1.5.0 (1.3.0 and 1.4.0): …". It
   keeps g3-exec-1 and c2-1424-regen-2 as decided, and points to `runPlan.harnessCutRule` for 1.5.0.
   The packet is re-sealed.

**Checks after the fix.** The replay is unchanged: `INCOMPLETE`, with 0 `DISAGREE` and the
470 `DISAGREE` cases moved to `INCOMPLETE`. The protocol controls pass (63 tests), and both main's
validator and this tree's validator report "protocol valid".
