# Phase 2 Measurement Results

**Generated:** 2026-05-29T02:21:59Z
**Analysis seed:** 42
**Total runs:** 900
**Arms present:** A, B, C
**Tasks present:** 30
**Models present:** claude-sonnet-4-6
**Bonferroni-corrected alpha':** 0.0125

## Ship/no-ship decision: **DO NOT SHIP Phase 2 — revert per RFC §11**

Decision rule: all four RFC §10.3 kill criteria must be PASS.

---

## Criterion 1
**Criterion 1: Success-rate non-inferiority (Arm C vs Arm A)**

- Pairs (task × seed): 300
- Discordant pairs (A succ / C fail): 16
- Discordant pairs (A fail / C succ): 13
- Arm A success rate: 0.840
- Arm C success rate: 0.830
- McNemar p-value: 0.710347
- Pass condition: McNemar p > 0.0125 (non-inferiority)
- **Result: PASS**

## Criterion 2
**Criterion 2: Identity-preservation non-regression (Arm C vs Arm A)**

- Pairs (task × seed): 300
- Arm A identity-preservation errors: 1486.0
- Arm C identity-preservation errors: 1499.0
- Wilcoxon p-value: 0.734325
- Pass condition: Wilcoxon p > 0.0125 (non-regression) OR Arm C errors <= Arm A errors
- **Result: PASS**

## Criterion 3
**Criterion 3: Material agent benefit on turn-count OR token-count (Arm C vs Arm A)**

### Branch: turn_count
- Pairs: 300
- Median Arm A: 5.000
- Median Arm C: 5.500
- Median reduction: -10.00%
- Wilcoxon p-value: 0.641804
- |Cliff's δ|: 0.0075 (sign: A<C)
- Branch result: FAIL

### Branch: total_output_tokens
- Pairs: 300
- Median Arm A: 1656.000
- Median Arm C: 1676.500
- Median reduction: -1.24%
- Wilcoxon p-value: 0.617016
- |Cliff's δ|: 0.0045 (sign: A<C)
- Branch result: FAIL

- Pass condition: (turn_count median reduction >= 10% AND Wilcoxon p < 0.0125 AND |Cliff's δ| >= 0.33) OR (total_output_tokens median reduction >= 15% AND Wilcoxon p < 0.0125 AND |Cliff's δ| >= 0.33)
- Winning branch: <none>
- **Result: FAIL**

## Criterion 4
**Criterion 4: Phase 2 distinguishable from Phase 1 (Arm C vs Arm B) on criterion 3's winning metric**

- Note: Criterion 3 did not pass; reporting both metrics.
- Turn-count Wilcoxon p-value: 0.433156
- Token-count Wilcoxon p-value: 0.387695
- **Result: FAIL**

---

## Summary table

| # | Criterion | Result |
|---|-----------|--------|
| 1 | Success-rate non-inferiority | PASS |
| 2 | Identity-preservation non-regression | PASS |
| 3 | Material agent benefit (turn or token) | FAIL |
| 4 | Phase 2 distinguishable from Phase 1 | FAIL |

**Decision: DO NOT SHIP Phase 2 — revert per RFC §11**

Per RFC §16.E item 8, this document is committed regardless of
pass/fail.
