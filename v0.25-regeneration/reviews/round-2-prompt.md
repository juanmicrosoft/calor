You are an adversarial reviewer for PR #1581 in juanmicrosoft/calor (the current directory is a checkout of the PR branch `milestone-0.25/reproof-48cae911`; `git diff origin/main...HEAD` is the PR). Review read-only.

## What the PR claims

It re-runs, on the frozen 0.25 release candidate `48cae911700ceb0dfc358e00933650445d81604f`, the checks that scope 1.4.0 `acceptancePreconditions` require (`docs/plans/evidence/v0.25-r0-1426/scope.json`: proof-s2-witnesses, provenance-candidate-manifest, provenance-regeneration, provenance-determinism, release-gate). It also writes one READY record per family under `docs/plans/evidence/<family dir>/ready/`, meeting rule S007 (`tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs` ValidateReady, and its port `ready_problems` in `scripts/verify_release_adjudication.py`). The model is 0.24's C2 regeneration 3 (`docs/plans/evidence/c2-1424/regeneration-3/`). Start with `docs/plans/evidence/v0.25-regeneration/regeneration.md`, then `results.json`, `ledger.json`, the six `ready/ready-record.json`, and a sample of `ready/cases/*.json`. The producers are in `docs/plans/evidence/v0.25-regeneration/tools/*.txt` (make_ready.py.txt writes the case and READY records).

Constraints the PR must respect:
- No `src/` change and no change to any frozen input. Only newly added data or text files (.json .jsonl .md .txt .log .trx .csv .gz .sha256, with no dot-segment) under `docs/plans/evidence/v0.25-regeneration/` and the six family `ready/` directories. The V025 classifier in `tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs` diffs every commit, so a later modification of an added file invalidates the candidate.
- Every measurement comes from a fresh clone with an isolated HOME and CALOR_HOME.
- Records must not overclaim. "Independently reviewed" or "independently verified" is forbidden, and so is calling something passed when the evidence does not show it.

## Round 2

This is round 2 of at most 3. Round 1's findings and their dispositions are in
`docs/plans/evidence/v0.25-regeneration/reviews/round-1.md`. The PR's commits were rebuilt (the classifier forbids
modifying an added file): `4dae3be25` (measurements and READY records), `e53cfc481` (round-1 record), `0f83a3e5a`
(narrative, T1 aggregate, R2 dry run). The classifier record for the final head lands in a last commit that adds only
`classifier/` files.

1. Verify each round-1 disposition. Is the F2 change right? Is the F6 per-cell map (`F6_CELLS` in
   `tools/make_ready.py.txt`; per-cell `cellEvidence` in `docs/plans/evidence/v0.25-f6-1144/ready/cases/*.json`)
   now justified cell by cell against the tests' actual bodies
   (`tests/Calor.Compiler.Tests/ConversionReporting/ConversionPathReportingTests.cs`) and the W1 transcripts
   (`docs/plans/evidence/v0.25-w1-1429/transcripts/`)? Treat the two readings flagged for the maintainer (surfaces
   that do not expose both options; SDK measured on the library) as disclosed judgement calls: raise them only if the
   record misstates them, or if a cell does not even do what its own reading says.
2. Repeat round 1's checks (passed cases, S007/ready_problems, re-proof statuses, F4, R2 dry run, classifier paths)
   on the rebuilt commits, and look for anything new.
Report each finding as `[BLOCKING|MAJOR|MINOR|NIT] <file>:<location> — <problem> — <why it matters> — <suggested fix>`. A finding is BLOCKING only if it makes a READY claim, a PASSED row or the candidate's integrity wrong. Finish with exactly one line, `VERDICT: <APPROVE|CHANGES-REQUESTED>`, and a count by severity. Be concrete and do not pad the list.
