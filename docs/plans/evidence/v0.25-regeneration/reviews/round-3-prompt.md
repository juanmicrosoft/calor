You are an adversarial reviewer for PR #1581 in juanmicrosoft/calor (the current directory is a checkout of the PR branch `milestone-0.25/reproof-48cae911`; `git diff origin/main...HEAD` is the PR). Review read-only.

## What the PR claims

It re-runs, on the frozen 0.25 release candidate `48cae911700ceb0dfc358e00933650445d81604f`, the checks that scope 1.4.0 `acceptancePreconditions` require (`docs/plans/evidence/v0.25-r0-1426/scope.json`: proof-s2-witnesses, provenance-candidate-manifest, provenance-regeneration, provenance-determinism, release-gate). It also writes one READY record per family under `docs/plans/evidence/<family dir>/ready/`, meeting rule S007 (`tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs` ValidateReady, and its port `ready_problems` in `scripts/verify_release_adjudication.py`). The model is 0.24's C2 regeneration 3 (`docs/plans/evidence/c2-1424/regeneration-3/`). Start with `docs/plans/evidence/v0.25-regeneration/regeneration.md`, then `results.json`, `ledger.json`, the six `ready/ready-record.json`, and a sample of `ready/cases/*.json`. The producers are in `docs/plans/evidence/v0.25-regeneration/tools/*.txt` (make_ready.py.txt writes the case and READY records).

Constraints the PR must respect:
- No `src/` change and no change to any frozen input. Only newly added data or text files (.json .jsonl .md .txt .log .trx .csv .gz .sha256, with no dot-segment) under `docs/plans/evidence/v0.25-regeneration/` and the six family `ready/` directories. The V025 classifier in `tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs` diffs every commit, so a later modification of an added file invalidates the candidate.
- Every measurement comes from a fresh clone with an isolated HOME and CALOR_HOME.
- Records must not overclaim. "Independently reviewed" or "independently verified" is forbidden, and so is calling something passed when the evidence does not show it.

## Round 3

This is round 3 of at most 3 (a verification-only pass follows). Rounds 1 and 2 and their dispositions are in
`docs/plans/evidence/v0.25-regeneration/reviews/round-1.md` and `round-2.md`. The commits were rebuilt again:
`b8e6de456` (measurements and READY records), `09efac77f` (review records), `b731cef9f` (narrative, T1 aggregate, R2
dry run), `0ee03c4c7` (classifier record for `b731cef9f`, adding only `classifier/` files).

1. Verify the round-2 dispositions: the classifier record (`classifier/`), and the F6 aggregate measurement
   (`tools/f6_aggregates.py.txt`, `measure/f6-aggregates/f6-aggregates.json`, and the F6-REPORT-05 and F6-REPORT-06
   case records).
2. Re-check the whole PR once more for any record that claims more than its evidence shows: the case records, the
   READY records, `results.json`, `regeneration.md`, `publication-candidates.json`, the R2 dry run, and the classifier
   paths. The two F6 readings that `regeneration.md` flags for the maintainer are disclosed judgement calls. Raise
   them only if a record misstates them.
Report each finding as `[BLOCKING|MAJOR|MINOR|NIT] <file>:<location> — <problem> — <why it matters> — <suggested fix>`. A finding is BLOCKING only if it makes a READY claim, a PASSED row or the candidate's integrity wrong. Finish with exactly one line, `VERDICT: <APPROVE|CHANGES-REQUESTED>`, and a count by severity. Be concrete and do not pad the list.
