# 0.25 release prep: Codex round 3 (last adversarial round)

- **Invocation 3 of at most 4** (3 rounds plus 1 verification pass).
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `b5bda71f8` (the round-2 fixes), whole diff from `e075f848a`.
- **Verdict:** REQUEST-CHANGES: 2 MINOR. No BLOCKING or MAJOR.

Codex also verified: all six injected-command rows need their guards; 14,493 + 44 = 14,537 was
correct; the notes are byte-identical, compile as MDX, pass G012 and match the website expectations;
the generator's self-tests and packet and seal checks pass; the four escalations are recorded
accurately, and it found no further freeze blocker.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MINOR | Phase detection used `git.IsAncestor()`, which turns a `merge-base` error into "after the landing" and bypasses the `GitRun` seam. | Fixed: the phase check uses the runner; exit 0 is before the landing, 1 after, anything else an error (`UNDECIDED`). New theory row `phase` (fails only that call). |
| 2 | MINOR | `AnUnreadableManifestBlobIsUndecided` did not test either blob guard alone. | Fixed: `AnUnreadableBlobIsAnError` (2 rows) calls `FindLanding` directly for an unreadable manifest blob, and checks the diff guard with an unreadable added downstream record under an intact landing. |

`ReleaseCandidateInvalidationTests` now has 46 cases (test manifest 14539).

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. MINOR — tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs:141. Phase detection still calls git.IsAncestor(), which collapses every nonzero merge-base exit into false and bypasses GitRun. An error therefore becomes AfterLanding; an exempt downstream addition can still produce NOT-INVALIDATED, contradicting "any git failure gives UNDECIDED." Use the injected runner, distinguish exits 0/1 from errors, and add a control for this ancestry check.

2. MINOR — tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidationTests.cs:196. AnUnreadableManifestBlobIsUndecided does not independently protect either new blob guard. Removing the landing guard still passes because Diff() encounters the missing manifest blob; removing the diff guard still passes because landing detection records the error. Isolate landing detection with a direct FindLanding() assertion, and test diff reading using a missing non-manifest blob with an intact landing.

Verified: all six injected-command rows require their guards by inspection; 14,493 + 44 = 14,537 is correct. Notes are byte-identical, compile as MDX, pass G012, and match the updated website expectations. Generator self-tests and packet/seal checks pass. The four escalations are accurately recorded; no additional freeze blocker found.
```
