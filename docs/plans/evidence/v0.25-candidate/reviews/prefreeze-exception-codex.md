# 0.25 pre-freeze PR (#1576): EXC-PREFREEZE-GATE verification pass

- **Exception:** EXC-PREFREEZE-GATE (@juanmicrosoft, 2026-10-19): "Exception: fix + 1 pass". One fix and exactly
  one verification-only pass; a BLOCKING or MAJOR finding stops the PR for a maintainer decision.
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `d7fa810e2..5a42d5135` (the exception fix, the post-budget notes text and the gate-fixture fix).
- **Verdict:** NOT-VERIFIED: 1 MAJOR, 1 MINOR. **The PR stops here** under the exception's terms.

## Results

| Item | Result |
|---|---|
| 1. S004 replay | Production replay matches S004's rules and order, and S007 uses its touched list. The parity test does not discriminate every rule (see MINOR) |
| 2. Heading ids | The nested-image fix works, and the gate and the site agree on all 16 vectors and 12 pages. A regression the vectors miss (see MAJOR) |
| 3. First-of-duplicates control | VERIFIED |
| 4. `[0.25.0]` notes on #1545–#1547 and Trusted Publishing | VERIFIED: byte-identical, true against the records, no forbidden wording |
| 5. Gate fixture: no auto-gc | VERIFIED |
| 6. Test manifest 14,606 | VERIFIED |

## Open findings

| Severity | Finding | Fix (not applied) |
|---|---|---|
| MAJOR | `heading_text` splits on code spans before resolving links, so a link whose text is a code span, ``## [`Build`](https://example.com)``, gives `build-https-example-com`; the site (and the previous gate) give `build`. The shared vectors had no such case. | Resolve links and images over the whole heading first, keeping code-span text literal inside link text; add the vector. |
| MINOR | The S004 parity theory compares only flagged-case sets. The "add of an active case" fixture also lacks a valid `requirementSha256`, so another violation flags the same case when the active-add guard is removed; and no control discriminates additions-before-changes. | Compare (case, rule) pairs, give the added copy a valid digest, and add an add-then-change control. |

## Codex output (final message, verbatim)

```text
Checked d7fa810e2..5a42d5135. No files modified.

1. NOT-VERIFIED. Production replay matches the requested S004 rules and order; S007 uses its touched list. The test calls the C# validator and detects removals-before-changes, but does not discriminate every rule: removing the active-add guard or moving changes before additions leaves all subject-set comparisons passing.

2. NOT-VERIFIED. The nested-image fix works. Python and the actual website implementation pass all 16 shared vectors and 12 pages. However, the refactor introduces the heading regression below, which those vectors miss.

3. VERIFIED. An in-memory last-wins mutation removes case F1-REFOUT-03 needs a passed result while retaining the duplicate warning. The tightened assertion therefore fails as required.

4. VERIFIED against repository records and supplied closure timestamps. Both [0.25.0] sections are byte-identical (30,882 bytes). The closure dates, Trusted Publishing workflow, environment restriction and attestation qualification agree with the recorded evidence and checker. The full wording scan finds no forbidden independence claim.

5. VERIFIED. Repository-local gc.auto=0 and maintenance.auto=false disable automatic object maintenance in disposable fixtures. They change no committed inputs, gate checks or assertions.

6. VERIFIED. 14,606 = 14,592 + 14: 13 replay theory rows + 1 heading fact + 1 duplicate-row fact − 1 replaced theory row.

Regressions:

- MAJOR — heading conformance. For ## [`Build`](https://example.com), the site and baseline gate produce build; HEAD produces build-https-example-com. heading_text splits around code spans before resolving links, breaking this previously correct case.
- MINOR — replay-control discrimination. The active-add fixture lacks requirementSha256, so a separate digest violation preserves the same flagged case after removing the active-add guard. Comparing only subject sets masks that missing rule. Additions-before-changes also lacks a discriminating control.

VERDICT: NOT-VERIFIED
```
