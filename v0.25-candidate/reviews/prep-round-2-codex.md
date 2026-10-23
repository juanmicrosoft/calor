# 0.25 release prep: Codex round 2

- **Invocation 2 of at most 4** (3 rounds plus 1 verification pass).
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `9c06acca0` (the round-1 fixes), whole diff from `e075f848a`.
- **Verdict:** REQUEST-CHANGES: 1 BLOCKING, 1 MAJOR, 1 MINOR.

Codex also verified: the round-1 escalation record is accurate; the scope, D1 and current T1 seals
recomputed by the generator match their pins; the SDK parsing handles the real jobs; the release
sections are byte-identical and pass G012; the refinement-page corrections match the implementation.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | The required G2 re-run cannot execute: protocol 1.5.0 allows 4 executions, all 4 are used (`c2-1424/regeneration-3/ledger.json`), and `plan_problems()` binds executions after the third to #1537's 0.24 merge. | **Escalated to the maintainer** (a protocol amendment needs approval). Confirmed in the ledger ("executions 4 of maxExecutions 4"). README "Open questions" 4. |
| 2 | MAJOR | Landing detection fails open on an unreadable manifest blob (`cat-file -e` failure reads as "absent"), and `Diff()` accepts failed blob reads. | Fixed: presence is decided by `ls-tree`; an existing manifest whose blob cannot be read is an error; an unreadable blob in any diff is an error. Control: `AnUnreadableManifestBlobIsUndecided`. |
| 3 | MINOR | Neither round-1 control reaches the `rev-list` guards (both fail at `merge-base`). | Fixed: a `GitRun` seam injects a failing command. `AFailedGitCommandIsUndecided` (6 rows) replaces the two deleted-object controls and covers `merge-base` on the target and main sides, both `rev-list` enumerations, `ls-tree` and `diff-tree`. In each row the weakened-classifier commit would otherwise pass as before-landing or go unclassified. |

Also in this commit: the round-1 edit had converted the two corrected website pages from CRLF to
LF (whole-file rewrites); their line endings are restored (3 changed lines each). To stay within the
1,500-line PR ceiling, 4 redundant path rows were dropped and the README and generator compacted;
`ReleaseCandidateInvalidationTests` has 44 cases (test manifest 14537).

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. BLOCKING — docs/plans/evidence/v0.25-candidate/generate_candidate_manifest.py:51: the required G2 re-run cannot execute. Protocol 1.5.0 permits 4 executions; c2-1424/regeneration-3/ledger.json:131 records all 4 consumed. determinism_runner.plan_problems() rejects another execution and binds executions after the third to PR #1537's 0.24 merge. A read-only probe reproduced both refusals. Record this additional freeze blocker and register an approved 0.25 protocol, execution allowance and candidate binding before freezing.

2. MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs:179: landing detection still fails open on an unreadable manifest blob. If the landing's manifest blob is missing while commits and trees remain readable, cat-file -e failure becomes "manifest absent." landing becomes null, so later classifier edits receive BeforeLanding exemptions. Diff() also silently accepts failed blob reads at lines 195–196, allowing NOT-INVALIDATED. Distinguish path absence from object-read failure, return UNDECIDED for unreadable objects, and add a missing-manifest-blob control.

3. MINOR — tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidationTests.cs:163: neither new control exercises the repaired rev-list failure paths. Both missing-commit arrangements fail at merge-base; both tests would still pass with the two rev-list guards removed. Add separate controls that reach each enumeration failure and assert its rev-list error.

The original escalated record is accurate. Scope/D1/current-T1 seals match their pins; SDK parsing handles the real jobs; release sections are byte-identical and pass G012; the refinement-page corrections match the implementation. .NET tests could not run under the read-only filesystem restriction.
```
