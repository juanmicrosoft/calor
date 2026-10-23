# Fix #1485 — Codex adversarial review, round 1

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, diff `origin/main...HEAD` on stdin.
- Outcome: the session hit the Codex usage limit before it wrote a final verdict.
  It had already run probes against the patched compiler. The findings below come
  from those probe outputs and its interim notes. No verdict was recorded.

## Findings and dispositions

| # | Finding (repro) | Severity | Disposition |
|---|---|---|---|
| 1 | Statement-level `§PP{DEBUG}` with no indented body and no `§/PP` swallowed the next same-column `§P` into `#if DEBUG`. | major (silent) | Fixed. `ParsePreprocessorDirective` now uses block ownership for the `§PP` and `§PPE` bodies. Tests: `EmptyPreprocessorBlock_DoesNotSwallowSameColumnSibling`, `EmptyPreprocessorBlock_InsideIf_DoesNotStealOuterElse`. |
| 2 | Flat closer form whose first statement sits on the opener's line (`§UNSAFE{u1} §P "first"` / `§P "second"` / `§/UNSAFE{u1}`) was rejected: `BeginBlockBody` treated same-line content as indent form. | major (regression) | Fixed. Same-line content now still gets the closer scan; the scan skips the opener's line and returns "not flat" only when the first later line is deeper. Test: `FlatCloserForm_BodyStartingOnOpenerLine_ContinuesUntilCloser`. |
| 3 | Empty `§UNSAFE{u1}` followed by a nested same-kind block with an id-less closer (`§UNSAFE{u2}` … `§/UNSAFE`) was misjudged as flat, so `u1` swallowed `u2` and the next statement. | major (silent) | Fixed. The scan counts same-kind openers at the opener's column; an id-less closer closes the innermost of those first. Test: `EmptyBlock_FollowedByNestedSameKindBlockWithIdlessCloser_IsNotFlat`. |
| 4 | Block lambda without `§/LAM` (`§B{a:Action} §LAM{lam1}` then `§P` / `§R`) swallowed every following statement. | major (silent) | Fixed with ownership. A lambda whose `§/LAM` closer follows keeps run-until-closer, because the converter emits block-lambda bodies at the opener line's column with the closer at a shallower one (`ConverterReachTests` C1/N2/Cluster1 caught the first attempt). Test: `BlockLambda_WithoutCloser_DoesNotSwallowFollowingStatements`. |
| 5 | Empty `§GET` followed by a same-column `§P` inside `§PROP` was parsed into the getter. Empty accessors also consumed the enclosing member's Dedent. | minor (invalid input accepted) | Fixed. Property and event accessors use ownership and consume only their own Dedent. Test: `EmptyGetter_DoesNotStealPropertyDedent`. |
| 6 | `§W{w1} x` with `§K` cases at the match's own column is accepted. | minor | Kept. Cases at the `§W` column parse as cases, and the match ends at the next same-column statement. Previously this layout was rejected or silently took the parent's Dedent. No committed program uses it. |
| 7 | Empty `§CTOR` followed by a same-column member `§PP`/`§CSHARP` reports `Calor0100`. | minor (loud, pre-existing) | Not changed. Pre-existing loud error (`IsClassMemberOpener` does not list `Preprocessor`/`CSharpInterop`). Extending it is column-sensitive and out of scope. |
| 8 | Probes for interface end, an empty nested `§IFACE`, event accessors, and auto-properties before a sibling class. | — | Correct; no change. |
