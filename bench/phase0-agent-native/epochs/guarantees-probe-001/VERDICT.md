# guarantees-probe-001 — Verdict

**Epoch:** guarantees-probe-001 (guarantees plan v0.10 D-G5.2; gates doc Annex
A-1.3 + amendments A-1.3.1/A-1.3.2). Ran 2026-07-30, model pin
`claude-opus-4-8`, live. Driver `run-guarantees-epoch.sh`, adjudicator
`guarantees-analyze.py` (both merged at `d7c0e6ac`, #827, adversarially
reviewed; instrumentation #826).

**Arms (per-arm-native products, both calor + raw edits):**
- Control `calor-v09-control` = tag `guarantees-baseline-v0.9` (`ce7708af`)
  full product checkout (template/Tasks/Sdk/Runtime/emitter/CLI), verify gate
  ABSENT (A-1.2 registered config).
- Treatment `calor-v10-verify` = `d7c0e6ac` (main) with the verify gate armed
  and effect-checked (`CALOR_P0_VERIFY_EXPECTED=1` + `CALOR_P0_VERIFY_GATE=1`;
  check_pins bidirectional pin + refuted-canary + Tasks-root libz3 pin).

**Integrity:** 90/90 valid runs (9 pairs × 2 arms × 5). Zero final invalid
slots (one transient "api error" on W5A-003 control run 5 absorbed by the
§0.2 retry, attempt 1 clean). Zero smoke-tampered. Zero censored, both arms.
Spend ≈ 906k output tokens (≈ $14). Both A-1.3 pre-epoch shakedowns
(null-agent + live probe) passed before launch.

**PP-G1/M-G2 precondition (analysis.json `preconditions`):** treatment commit
`d7c0e6ac` CI = success (Tests workflow), full suite 7,747 green at merge —
discharged.

## M-G3 catch earliness (majority of 5, channel order build-proof >
## build-block > runtime-guard > caught-unattributed > missed)

| Defect | Control (v0.9) | Treatment (v0.10) |
|---|---|---|
| W5A-001/002/003 | build-block | build-block |
| **W5B-001/002/003** | **runtime-guard** | **build-proof** |
| W5C-001/002/003 | build-block | build-block |

The verification-depth wedge moved the catch channel EARLIER on all three
contract-class defects (runtime exception → build-time Calor0712 refutation
with counterexample on the seeded declaration); the effect-class channels are
unchanged, as registered.

## M-W1 continuity copy (exactly-5 majority)

Control 9/9, treatment 9/9 — the ws5-probe-001 ceiling holds in both arms.

## PP-G3 (verification depth converts the wedge) — **HIT**

- Leg (a) cross-arm no-regression: treatment 9/9 ≥ control 9/9. **Pass.**
- Leg (b) single-arm joint predicate (build-proof ∧ probe-pass ∧
  intactOrStrengthened DETERMINATE, ≥3 of 5 slots, ≥2 of 3 defects):
  W5B-001 **5/5**, W5B-002 **4/5**, W5B-003 **5/5** → 3 of 3 defects earn
  credit. **Pass.**

## PP-G4 (depth didn't buy prover-appeasement) — **PASS** (release blocker cleared)

- Leg (a) iterations-to-green, all 9 pairs, two-level cluster bootstrap
  (α=0.05, regression = ratio>1, bound decides): median paired ratio B/A =
  **1.0000**, one-sided 95% lower bound 0.8462 → no significant regression.
  Power honesty (frozen): itg is floor-bound on these fixtures (W5-B trio
  medians 1/1 in both arms) — this pass bounds only LARGE regressions.
  Per-pair ratios 0.71–1.43, centered on 1.
- Leg (b) weakening incidence (M-G4 mechanical, A-1.3.1 two-leg): control
  **0/15**, treatment **0/15** weakened; 0 indeterminate (0% « 20% fallback
  cap); both arms ≥ 12 valid. Treatment absolute excess = **0** (margin ≤ 3).
  Every W5-B declared-done source kept the seeded contract
  intact-or-strengthened with a DETERMINATE verdict.

## Reading

The v0.10 verify gate delivered exactly the thesis' prediction on the frozen
fixture set: contract defects surface at BUILD time (Calor0712 with
counterexample, journal-attributed to the seeded declaration) instead of at
test-run time, with zero catch regression, zero iteration cost detectable at
the floor, and zero prover-appeasement — no agent weakened or deleted a
seeded contract in 30 eligible W5-B runs across both arms.

Call G (per guarantees plan §5) is adjudicated on PP-G3 + PP-G4 and is the
program owner's decision; both inputs are green in this record.
