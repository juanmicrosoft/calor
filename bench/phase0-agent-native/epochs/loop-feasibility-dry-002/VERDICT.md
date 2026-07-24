# loop-feasibility-dry-002 — D4.5 feasibility verdict

35/35 runs complete (7 non-retired pairs × 5 runs, calor arm, pinned
claude-opus-4-8 — enforced, not just recorded). All journals loop-telemetry/2
schema-valid. Success 35/35, escaped bugs 0, censoring 0. Total cost $35.28
(authorized envelope ~$49–67).

## Findings against the pre-stated decision rules (loop-m2-baseline.md)

1. **Iterations-to-green is FLOOR-BOUND, not merely underpowered.** Pooled
   median = 1; 94% of runs finish at the 1-iteration floor (mean 1.09, max 2).
   A 15% reduction from a floor value is unobservable in an integer metric at
   ANY N. Rule fires: PP-L5's primary measure moves to **tokens-to-green**.
2. **Tokens-to-green is decidable at exactly the proposed threshold.** Median
   7,689 output tokens/run (mean 10,309); simulation MDE at 80% power under
   the gates-doc §6.1-style paired-ratio bootstrap is **~15% at n = 7 pairs ×
   5 runs/arm** (power curve: 10%→0.57, 15%→0.83, 20%→0.96). M5 must run at
   least this N per arm for the threshold to be adjudicable.
3. **M-L3 (diagnostic actionability) has too few events**: 3 qualifying
   failing-iterations across the whole epoch (all 3 followed by an edit to a
   named node, for what it's worth). Below the pre-stated 20-event floor →
   **PP-L4 is reported-not-adjudicated at M5**. This is itself a finding: on
   these fixtures the baseline loop rarely fails after its first edit, so
   diagnostic-steering claims cannot be measured at authorable-fixture scale —
   consistent with the strategy doc's §9 pattern.

## Threshold-freeze proposal (D4.4 annex, needs sign-off)

- **PP-L5 (frozen)**: ≥15% relative reduction in **median per-pair
  tokens-to-green**, arm A (loop-baseline-ws1) vs arm B (baseline+WS2/WS3),
  simultaneous epoch, ≥7 pairs × ≥5 runs/arm, gates-doc §6.1 adjudication.
  Iterations-to-green remains recorded (observational) but does not gate.
- **PP-L4**: reported-not-adjudicated (M-L3 event floor unmet at this scale).
- **PP-L1** unchanged (toolchain metric, D3.3 fixture, not epoch-dependent).

Method note: MDE is a design-stage simulation estimate (empirical run
resampling, synthetic treatment scaling, 400 sims × 200-resample cluster
bootstrap over pairs, one-sided 95%); analysis.json carries the full curve.
