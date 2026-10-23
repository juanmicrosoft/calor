# A1 capacity audit: terminal condition 9 (#1408)

**Condition 9 (contract §8):** "No §9 ceiling was exceeded without a merged amendment raising it."
**Result: does not hold.** The terminal record therefore proposes `MILESTONE-FAILED` (decision AD-4).

Method: read-only audit at `origin/main` `12200dd7`, using GitHub's API and the committed review
records. All paths below are relative to `docs/plans/evidence/`. The contract's ceilings and
exceptions are in `evidence-contract-1407/contract.json` `authorityCapacity.capacity`.

## How a review round is counted

The ceiling is "Review rounds per PR: 3; then the PR is closed and re-scoped". The contract's own
amendments count a Codex pass after round 3 that **requested changes and was followed by a fix** as
an overrun that needs a registered exception:

- 1.3.0 (#1496): "rounds 1–3, then two verification-only passes" that each led to a fix is "an
  overrun of 2". It adds: "No other PR gets an overrun allowance."
- 1.3.1 (#1502, #1503): rounds 1–3 plus one verification pass that requested changes needed an
  exception before one more change was allowed.

The only review-rounds exceptions are #1496 (5), #1502 (6), and #1503 (5).

## Overruns with no amendment

| PR | Passes after round 3 that requested changes and were followed by a fix | Records |
|---|---|---|
| **#1508 (C1 PR 1)** | **4**: the verification pass (MAJOR, test fix), then `refreeze-verification` (MAJOR), `-2` (NEW BLOCKING), and `-3` (MAJOR; "fixed afterwards and not re-reviewed"). `-4` was clean. 8 passes in all; the PR was not closed and re-scoped | `c1-1423/reviews/round-1..3-codex.md`, `verification-pass-codex.md`, `refreeze-verification{,-2,-3,-4}-codex.md` |
| **#1479 (G2 PR 1)** | **2**: the verification pass (NOT CLEAN), then a second pass (2 NEW BLOCKING), each followed by a fix; the third pass was clean. 6 passes | `g2-1421/reviews/round-1..3-codex.md`, `verification-pass{,-2,-3}-codex.md` |
| #1473 (B1) | 1 (NEW BLOCKING, fixed). Amendment 1.1.0 raised only its PR size | `b1-1276/reviews/verification-pass-codex.md` |
| #1480 (S1) | 1 (two items fixed in `e7cbce77`, not re-reviewed) | `s1-1311/reviews/round-verification.json` |
| #1474/#1475 (R2) | 1 ("accepted and fixed") | `r2-1410/reviews/verification-pass-codex.md` |
| #1499 (S2 record) | 1 (BLOCKING + MAJOR, partly closed by a test change) | `s2-1413/reviews/dispositions/verification-codex.md` |
| #1527 (B2 PR 2) | 1 (MAJOR, fixed in `bca13caf`) | `b2-1422/reviews/pr2-verification-pass-codex.md` |
| #1507 (amendment 1.3.2) | 1 (MINOR, fixed) | `evidence-contract-1407/reviews/amendment-1.3.2-report-totals-correction-verification-pass-codex.md` |

The first two rows are decisive on their own. Some records read the budget as "three rounds and one
verification pass" (for example `c1-1423/reviews/verification-pass-codex.md`). Even under that looser
reading, #1508 (8 passes, 4 change-requesting passes after round 3) and #1479 (6 passes, 2) exceed it.

## Ceilings that are not recorded

These are not decisive, but they cannot be shown to hold either:

- **ordinary-ci (15,000 runner-minutes).** No record measures it; the contract notes that its
  250-minute-per-push basis is an estimate. The audit summed job wall-clock time from the Actions API
  for runs after the R0 merge (2026-10-02T13:10:12Z), excluding determinism dispatches. It found
  about 29,500 job-minutes on 0.24 PR branches across 523 `pull_request` runs, plus about 6,500 on
  `main` pushes. The figure includes cancelled runs, and which workflows are "ordinary PR checks"
  was never defined. The script was run once and its output was not retained, so treat it as an
  indication only.
- **maintainer-review-total (40 h) and maintainer-review-per-pr (2 h).** Nothing records them.
- **paid-spend (0 USD).** S1 records $0. No record addresses the cost basis of the agents and
  Codex runs.

## Interpretation-dependent items (not relied on)

- **agent-prs-per-gate.**
  - #1424 opened 3 PRs (#1517, #1535 unmerged, #1542) against 2. Amendment 1.5.0 raised only
    `regenerations`. 2 merged.
  - R0 #1407 merged 11 PRs if amendment PRs count.
  - #1533 was reassigned by maintainer decision PREP-PR-ACCOUNTING, not by an amendment.
- **pr-size.** These depend on the evidence-data exclusion:
  - #1491: 1,818 counted lines. It is not a gate PR.
  - #1472: 1,762, or 1,493 with P1's own exclusion.
  - #1475: 2,029 as a stacked diff, about 856 incremental.
  - #1481: 3,310, or 1,498 with the Tier 2 expectations file excluded.
  - #1466: 2,604, before the ceilings were accepted.
- **#1502 exception condition 6.** A merge that resolved 3 `src/` conflicts was accepted by
  maintainer decision Q11 (`DEV-R-NUM-MERGE-MECHANICS`), not by an amendment.

## Ceilings shown within bounds

| Ceiling | Recorded use |
|---|---|
| `s1-generated-cases` | 3,008 of 3,008 |
| `s1-compute` | 0 CI minutes, under 2 machine-hours |
| `s1-timebox` | Met |
| `determinism-compute` | 1,608 of 3,200 |
| `regeneration-compute` | 323, 573, and 583 of 1,500 per regeneration |
| `regenerations` | 3 of 3 |
| `s2-repairs` | 7 of 7 |
| `s2-repair-size` | At most 591 |
| `milestone-timebox` | Day 8 of 56 |

## Why this cannot be repaired inside 0.24

- §9 consequence 4: from the #1424 raw-artifact freeze until #1408 closes, amendment authority is
  not exercised. An amendment in that window invalidates the affected evidence and returns to
  #1423.
- Amendment 1.5.0: a further failure after regeneration 3 is terminal.
- §8: #1408 "does not add, drop, or weaken a condition".
