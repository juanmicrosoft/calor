# EXC-W1-VERBATIM: one fix for W-V1b plus exactly one Codex verification-only pass

**Decision:** @juanmicrosoft, 2026-10-18, relayed through the coordinating session. W-V1b gets one fix and
exactly one Codex verification-only pass. This is a review-rounds exception for PR #1566 only. It comes
after EXC-W1-EXCERPT and does not extend it. No GitHub comment records the decision.

## The fix

`WebsiteSources_AreExcerptsOfTheFixtures` no longer normalizes what it compares. A `csharp` fence marked
`{/* w1-source: ID */}` must occur **verbatim**, as one contiguous substring of the fixture.

- **Only normalization allowed:** CRLF is converted to LF, and newlines are trimmed at the fence's two ends.
- **No other processing:** no comment stripping, no blank-line splitting, no whitespace changes.
- **Elision:** a line that is exactly `// ...` is an explicit elision. Each part then has to be a verbatim,
  in-order substring. The guide uses no elisions.

To satisfy this, the guide's four C# input blocks (F1, F2, F3, F5) now show the whole fixture text
verbatim. The explanatory inline comments they used to carry are now in the prose.

## Negative controls

Each control is applied to the real guide text, and each must produce a problem:

- **Deleted statement:** `x = x + 1;` removed from `Bump`.
- **Commented-out line:** `// x = x + 1;`.
- **Changed line:** `Twice(in int x) => x * 2` changed to `x * 3`.
- **Added line:** `total = 0;` added after `int total = 1;`.

## Verification pass

See `exc-w1-verbatim-verification.md`.
