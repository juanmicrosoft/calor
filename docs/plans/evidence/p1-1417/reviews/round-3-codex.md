# Round 3: Codex adversarial review

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` (cross-family), diff vs `origin/main` on stdin.
- Reviewed SHA: `da556f7816b441a38e8254d5149a9c24b7d6969e`.
- Prompt: hostile review against #1417 and contract §6; numbered BLOCKING/MAJOR/MINOR or "No objections".

## Verbatim output

1. **BLOCKING — [DurableProvenance.cs:351](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ac3bb362723d09f44/tests/Calor.Compiler.Tests/Provenance/DurableProvenance.cs:351): A nonexistent phase-1 source commit can become authoritative.** `ReadPhase1` silently omits source and manifest verification whenever `headCommit` does not resolve—even before merge. Set `measuredCommit` and `phase1.headCommit` to an invented 40-hex SHA, stamp the artifact accordingly, and record the branch’s actual input hashes. Pending validation passes. After squash, completion also passes: the landed hashes match, and `ReviewedPhase1` authenticates the same unchecked record. This violates §6’s PR-head identity and verified tree equality. Missing-object tolerance must distinguish a discarded, already-landed phase-1 record from an unverifiable pre-merge claim. No control tests this case.

2. **MAJOR — [LedgerCommitStampTests.cs:181](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ac3bb362723d09f44/tests/Calor.Compiler.Tests/LedgerCommitStampTests.cs:181): The documented write-back command still bypasses round-2 validation.** Its filter runs only `PendingIdentitiesAwaitLandingOrCompleteWithEqualTrees`, which calls `CompleteWriteBack` without validating either the pending entry or completed output. Round-2 finding 4 therefore remains inadequately fixed: a pending entry retaining `resolvableOnMain` still gets written successfully, then fails authoritative validation with `P015`. Likewise, an omitted `src` claim or substituted phase-1 record can be written successfully before the completed verifier rejects it. The new controls exercise `Verify`, not this command. The command must validate inputs and completed outputs before rewriting the index.

## Dispositions

| # | Severity | Disposition | Change |
|---|---|---|---|
| 1 | BLOCKING | Accepted, fixed | A pending identity whose `headCommit` does not resolve now fails (`P013`) unless its artifact has already landed on protected `main`. Before merge, in PR CI, the head is an ancestor of the merge ref, so its trees and manifests are always checked. Only after landing may a squash have discarded it, and by then the reviewed record has been checked by the required `tests (compiler)` job. Control: `UnlandedPhase1MustNameAnExistingHead`, the reviewer's exact shape (an invented 40-hex head with the branch's real input hashes, checked out as the PR would be). It fails with the rule disabled. |
| 2 | MAJOR | Accepted, fixed | `CompleteWriteBack`, which the documented write-back command runs, verifies the pending entry first and the completed entry afterwards. It returns the first finding instead of a completion when either fails, so the command never writes an identity the verifier rejects. That covers a retained `resolvableOnMain`, a missing `src` claim, and a substituted phase-1 record. It also reports `P004` when `origin/main` is absent, instead of "not landed yet". Control: the pending test asserts that the write-back refuses an entry carrying `resolvableOnMain`. Removing only the first validation is still caught by the second; removing both makes the control fail. |

Mutation check after the fixes: with the head-existence rule disabled and the write-back's input
validation removed, `UnlandedPhase1MustNameAnExistingHead` failed (1 of 23). The output validation
still caught the `resolvableOnMain` case, as designed.

Round 3 is the last round allowed by the capacity rule (3 review rounds per PR). Both findings are
fixed in the head that follows the reviewed SHA. No re-review was run after them.

BLOCKING remaining after round 3: 0 by disposition. Codex has not re-reviewed the round-3 fixes.
