# W1 (#1429) Codex verification-only pass (invocation 4 of 4; review budget spent)

**Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt: verify the
round-3 dispositions at `e681be0b3` and check the round-3 fix commit for new BLOCKING or MAJOR issues.
**Verdict:** REQUEST-CHANGES. Reduced independence (R0 §9).

| Item | Codex | Evidence |
|---|---|---|
| R3-1 C# input bound to fixture | NOT-VERIFIED | The markers and the `x * 3` negative control work, but the ordered-subsequence rule lets the page omit fixture lines. Deleting `x = x + 1;` from the shown `Bump` (so `Run()` would return 42, not 44), or deleting the shown handler call, still passes |
| R3-2 hole bounds | VERIFIED | Matches `IsNativeHoleCall`/`IsNativeHoleArgument` and the verbatim-format preservation; `InterpolationTargetTypingTests` covers the shapes |
| R3-3 README base | VERIFIED | `0b9568b3f` is `origin/main` and the merge base; nothing under `src/`, `website/` or `CHANGELOG.md` changed between the two bases |

**Open MAJOR (W-V1):** `WebsiteSources_AreExcerptsOfTheFixtures` allows arbitrary fixture lines to be
skipped, so a page can delete a statement from a shown C# member without failing. Suggested fix:
compare complete selected members (or contiguous declared excerpts), ignoring only comment trivia, and
add a statement-deletion negative control.

**Status:** not fixed. The PR's review budget (3 rounds plus 1 verification pass, R0 §9) is spent, and
the W1 instructions say to stop and report when a MAJOR is still open. A fix needs a maintainer
decision: a capacity exception for one more fix plus one verification pass, or acceptance of the
residual. The pages' C# blocks are, today, line-for-line excerpts of the fixtures (the check passes),
so no page currently misstates its input; the gap is that the test would not catch a future deletion.
