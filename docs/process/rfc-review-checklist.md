# RFC Review Checklist

This checklist applies to all RFC-class documents under `docs/plans/`.
The lesson driving this checklist: the v2 → v3 → v4 → v5 trajectory of the
Compact Stable Identifiers RFC caught one *architectural fiction* per review
round — a sentence asserting the compiler does X when, in fact, the source
code did not do X. The checklist exists to catch the next analogous fiction
at PR review time, not at v(N+1).

## For RFC authors

1. **Architectural claim rule.** Every sentence of the form
   "the compiler does X", "the verifier asserts Y", or "the migrator
   guarantees Z" MUST EITHER:
   - cite a file:line in the source tree, OR
   - be explicitly marked `[PROPOSED]` to indicate the behavior is
     proposed but not yet implemented.

2. **Effort estimates with citations.** When estimating effort for an
   audit/refactor task, cite a grep result for the call site count:
   - GOOD: "0.5 day — `IdGenerator.Generate()` has 2 production call sites
     (`Ids/IdAssigner.cs:175`, `:180`)"
   - BAD: "0.5–2 days" (unfounded upper bound)

3. **Measurement claims.** When citing a numeric measurement
   (token counts, runtime, memory), include the script that produces the
   number, the sample size, and (for noisy measurements) a confidence
   interval. Hand-picked samples of N < 20 are not acceptable for headline
   numbers.

## For RFC reviewers

1. **Grep-verify every architectural claim.** Take 30 seconds per
   architectural assertion to run `rg` against the cited `file:line`.
   If the citation is missing OR if grep does not find the asserted
   behavior, add a `[VERIFY]` comment to the RFC PR.

2. **Be the devil's advocate explicitly.** Pair each RFC with two review
   documents:
   - A designer-voice critique (read the RFC charitably, find genuine
     improvements).
   - A devil's-advocate critique (read the RFC adversarially, find the
     assertions that don't hold up).

3. **Verdict convergence is the ship signal.** When both reviewers converge
   on "approve and ship" with only calibration concerns, author one
   calibration revision (capturing the calibrations in-line) and ship. Do
   not iterate further unless a new architectural concern emerges.

4. **Grep-verify artifact-state claims, not just architectural claims.**
   When a review document asserts something about the state of an artifact
   ("§N is missing," "the file truncates at line X," "the directory does
   not exist"), the reviewer MUST grep-verify the claim before including it
   in the review:

   - "§N is missing" requires `rg "^## §N" <file>` showing no match.
   - "File truncates at line X" requires `wc -l <file>` showing line count
     == X AND `tail -1 <file>` showing the truncation.
   - "Directory does not exist" requires `ls <path>` or `Test-Path <path>`
     showing absence.

   If the reviewer's tool truncates the read at some byte threshold, the
   reviewer MUST use a second tool (`rg`, `wc -l`, `tail`,
   `Get-Content -Tail`) to verify the artifact ends where their read ends,
   before concluding "the file is truncated."

   This rule was added v5-impl → v6 after one DA review made a "blocking"
   claim that §8 was missing from a file that demonstrably contains
   §8.1–§8.5 (verified by `rg "^## §8"`).

## For all RFC contributors

- A new RFC version (v(N+1)) is justified when EITHER:
  - A reviewer identifies an architectural fiction (claim not in the
    source), OR
  - A reviewer identifies a missing required artifact (statistical test,
    retry policy, rollback path).
- A new RFC version is NOT justified for:
  - Wording polish,
  - Adding citations to claims that are correct,
  - Reorganizing structure without changing substance.
- Calibration deltas should be captured in the next-version's §0 change
  table; new RFC versions should reference predecessors and document
  deltas only.
