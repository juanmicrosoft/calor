# C1 #1423 re-freeze verification pass 2 (Codex, run by the coordinator)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given the full
`git diff origin/main...HEAD` and read-only repository access. Under the contract §9 independence deviation
this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

1. **MAJOR — RESOLVED.** [Generator lines 65–68](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ae99f3ac31c8ada4d/docs/plans/evidence/c1-1423/generate_candidate_manifest.py:65) distinguish absence from lookup failure and require successful commit peeling. In-memory controls rejected local/remote exit 128 and a non-commit ref; genuine absence passed.

2. **MINOR — RESOLVED.** Git metadata confirms candidate `696ab824` committed at **2026-10-07T12:51:38Z**, matching the [record](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ae99f3ac31c8ada4d/docs/plans/v0.24-c1-candidate.md:15) and [manifest](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ae99f3ac31c8ada4d/docs/plans/evidence/c1-1423/candidate-manifest.json:11). [Inputs](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ae99f3ac31c8ada4d/docs/plans/evidence/c1-1423/inputs.json:176) correctly retain `12:51:39Z` as GitHub’s merge-event time.

3. **NEW BLOCKING — A matching local tag masks a conflicting remote tag.** At [generator line 70](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ae99f3ac31c8ada4d/docs/plans/evidence/c1-1423/generate_candidate_manifest.py:70), when local peeling succeeds, the condition checks only `tagged == CANDIDATE`; it ignores `remote.stdout`. Reproduced with local `v0.24.0` at `696ab824…` and a successful remote lookup advertising `5ebdbee2fee7fc87d0db187ea81024c3aadcbc54`: `FAILURES == []`, and releasability remained “no conflicting tag.” This violates the rule recorded in both inputs and manifest. Validate the remote target independently, including annotated-tag peeling, and reject conflicting targets.

No files changed. Full regeneration was interrupted; conclusions above use git metadata and direct in-memory controls.

VERDICT: NOT CLEAN

## Dispositions

This pass was run by the coordinating agent on PR head `8dcd541b`; its output is reproduced
above as delivered.

1. MAJOR (tag lookup fails open): resolved, per the pass.
2. MINOR (commit time): resolved, per the pass.
3. **NEW BLOCKING: fixed.** `releasability()` now passes local and remote results to
   `tag_problems()`, which judges them independently. The local tag must be absent (`show-ref` exit 1)
   or present and peel to the candidate. The remote result comes from
   `git ls-remote --tags origin refs/tags/v<version> refs/tags/v<version>^{}`; it must be empty, or its
   peeled entry (`^{}`, or the plain entry for a lightweight tag) must be the candidate. Any lookup
   error, unparsable line, or unexpected ref fails. `tag_self_test()` runs 11 controls (13 after the next pass; see refreeze-verification-3-codex.md) on every
   generator invocation, including:
   - a local tag at the candidate with a conflicting remote tag (fails);
   - a remote annotated tag peeling to the candidate (passes);
   - a remote annotated tag whose tag object is the candidate but which peels elsewhere (fails);
   - lightweight remote tags;
   - local and remote lookup errors and garbage output (fail).

   Mutation check: re-introducing the masking bug, or preferring the unpeeled remote entry, makes
   the self-test fail. `--check` passes on `696ab824`. The record says nothing about the C# tests,
   which are unchanged.
