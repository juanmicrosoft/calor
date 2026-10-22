# 0.25 R0 post-A1 readiness write-back (scope 1.6.0): Codex review round 1

- **Reviewer:** OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`)
- **Reviewed:** commit `e29b33992` (PR #1627, branch `milestone-0.25/r0-1426-writeback`) against `origin/main` `24bc55533`
- **Date:** 2026-10-22
- **Verdict:** **APPROVE**, no findings

## What the reviewer checked

These are read-only checks it ran with Python against the repository.

- **Historical gate:** the gate's `check_scope_packet` and `check_terminal_1426`
  (`scripts/verify_release_adjudication.py`) run on the packet at the adjudication commit with #1430's terminal record.
  Result: no errors.
- **Current scope.json:** `denominator_problems` and `preconditions_problems` report none.
- **READY records:** for F1 to F6, the gate's `ready_problems` (its S007 port) on each written-back readiness reports none.
- **D1 seal:** recomputed independently as `3743c1ff…`, equal to the new pin in `RefOutContractTests`.
- **Unintended changes:** none. Against `origin/main`, families in `scope.json`, `register.json` and `records.json`
  differ only in readiness, status, `readyNeeds` and `readyRecord`.

## Reviewer summary (verbatim)

> All six families' S007 fields exactly match their A1 READY records. Paths, hashes, candidate, #1626 and ACCEPTED
> outcome are consistent.
> S004–S008, version bumps, mirrors, seals and manifest +2 are consistent. Test changes preserve the relevant guards.
> G005's "never READY" rule is acceptable: the gate reads the adjudication-commit packet, whose hashes match, rather
> than main's write-back.

The reviewer did not run xUnit, because the read-only sandbox does not allow writing build output.

## Disposition

- **PR number (found by the author, not the reviewer):** the reviewed commit recorded `pr: 1626` in scope amendment
  1.6.0, T1 register 1.0.4, D1 1.0.8 and three docs. Issue #1626 turned out to be an unrelated PR opened earlier the
  same day, and this PR is #1627. The follow-up commit changes every reference to 1627. No validator reads the number
  beyond `pr > 0`, so no seal changes. The verification pass checks this fix.
