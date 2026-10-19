# EXC-W1-EXCERPT: one fix for W-V1 plus exactly one Codex verification-only pass

**Decision:** @juanmicrosoft, 2026-10-18, relayed through the coordinating session: "W-V1 gets one fix
plus exactly one Codex verification-only pass." This is a review-rounds exception for PR #1566 only. It
allows one fix and one extra verification pass beyond the R0 §9 budget of 3 rounds plus 1 verification
pass. No GitHub comment records the decision.

## The fix

`WebsiteSources_AreExcerptsOfTheFixtures` used to accept any ordered subsequence of fixture lines. A page
could delete a statement from a shown member and still pass (W-V1, `verification.md`).

The new rule:

- A marked `csharp` fence is split into chunks at blank lines.
- Each chunk must equal a **contiguous** run of the fixture's non-blank lines.
- The chunks must occur in order.
- Leading whitespace and a trailing `//` comment on a page line are ignored.
- A page may leave out whole chunks, such as a `using` directive or an unshown member. It cannot drop,
  add or change a line inside a shown chunk.

The test has two negative controls, and each must produce a problem:

- the earlier semantic edit, `Twice(in int x) => x * 2` changed to `x * 3`;
- the new one, deleting `x = x + 1;` from the shown `Bump`. This is the W-V1 counterexample.

All four required markers (F1, F2, F3, F5) pass on the current pages.

## Verification pass

See `exc-w1-excerpt-verification.md`.
