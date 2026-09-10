# Independent technical review of candidate authoring

This is a review of unpaid, unfrozen engineering artifacts for #1256.
It does not claim to complete #1266 or provide human methods approval,
final task selection, a protocol amendment, or collection authorization.

## Round 1 — independent code-review agent

- Reviewer: `review-1256-r1`, agent
  `8f042e3a-c499-4c50-8d18-eb709d6dfa2a`.
- Model: `claude-opus-4.8`; separate context, read-only review.
- Scope: all three candidates, their R1–R8 checks and rejections, the frozen
  design and issue requirements, source/suite provenance, both observation
  runners, and the six new evidence guards.
- Actually executed: `verify.py` (18 compiler invocations, 160 runtime checks,
  and all 26 frozen row-table tests), `survey.py` (24 compiler invocations),
  and the six candidate guards, using the existing frozen compiler without
  rebuilding it.
- Compiler and input hashes matched the committed observations. The
  reproduced semantic outcomes and assertion transitions agreed.
- High-confidence source/evidence defects requiring a fix: **none reported**.

The reviewer independently recomputed all numeric cases, checked identical
A/B source bytes, read the actual `unknown` diagnostics, and verified that
the state-failure marker appeared only after passing numeric assertions.
Numeric starter failures did not emit it.

| Candidate | Reviewer technical verdict |
|---|---|
| C-001 quota adapter | R1–R8 technically sound, with conditional R1 below |
| C-002 shipping comparison | R1–R8 technically sound, with conditional R1 below |
| C-003 ordered fingerprint | R1–R8 technically sound, with conditional R1 below |

**Conditional R1:** the reviewer explicitly treated the frozen §1.4
source-available abstraction premise as an assumption, not measured agent
ignorance. The readable dependency still exposes its `mut` declaration and
mutation to anyone who chooses to inspect it. Passing local tests cannot
establish which files an agent will inspect.

The reviewer agreed with retaining the six earlier #1255 rejections and
reproduced the twelve exact standard-spelling outcomes. They did not
un-reject a candidate, approve spending, finalize a count, or clear the
unverified collection-harness integration.

## Author's precision corrections to the review text

Two overstatements in the initial review response are **not adopted**:

1. It described the reproduced report as “byte-for-byte identical.”
   Direct comparison instead shows different originating checkout metadata
   and test-result ordering. Source hashes, compiler hash, per-case outcomes,
   counts, and failure messages agree. Raw run metadata and TRX are naturally
   different. This record claims semantic reproduction, not identical files.
2. It called single-shape dependence “forced” and asserted that every
   additional task must use row 7. Twelve exact spellings cannot establish
   that universal claim. The differing row-12 dispositions across the
   standard survey and the earlier quote-preview program illustrate why
   variants matter. The evidence establishes only the bounded survey and
   the current inventory's one-shape limitation.

These corrections narrow the review's wording; they do not manufacture a
reviewer retraction or strengthen the candidate acceptance. The original
response and these explicit qualifications should be read together.
Independent task selection and sizing remain open.

## Separate #1266 checkpoint

The user explicitly authorized an independent AI reader; #1266 imposes no
human qualification. The separately documented
[GPT-5.5 second-reader checkpoint](second-reader-1266.md) is now complete
for the current three-candidate inventory, with every earlier rejection
examined. It is not human methods approval or a task freeze.
Unfunded prospective registration is authorized; funding remains a gate
on collection, not on that planning work.
