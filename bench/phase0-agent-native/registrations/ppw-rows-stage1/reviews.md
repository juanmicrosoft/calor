# Independent prospective-method review

Reviewer: `methods-review-1261-r1`, independent read-only code-review agent
`3cdd2154-ac13-422a-9c17-02e9de04938f`, model **claude-opus-4.8**.
This is AI technical/methods review, not human credentialing, funding,
experimental model execution, or evidence of benefit.

## Disposition

**Clean: no significant methods or code defect found.** The reviewer
concluded that the amendment legitimately supplies the unfunded prospective
stage-1 size/two-estimand registration required by #1261. They found no
protocol barrier requiring funding or an extra human qualification before
that registration.

## What the reviewer actually verified

- The governing document change is append-only. Frozen sections 1–8 remain
  byte-for-byte identical, confirmed by comparing the original and staged
  prefix hashes.
- Executed `precision.py` and all seven mathematical tests: **7 passed**.
- Independently recomputed every sizing number: `log(80)`, minimal N=74,
  the N=73 failure, both N=74 half-widths, the allocated error probability,
  and the historical-cost multiplication.
- Verified the weighted Hoeffding derivation: each outcome's bounded range
  is `w_i/n_i`, giving summed squared ranges `Σ_i w_i²/n_i`.
  The arm-A estimator determines the larger required allocation.
- Verified exact rational boundary handling, clipping, malformed-count
  rejection, fixed cell weights after attrition, and unscorable/zero-cell
  handling without zero imputation.
- Cross-checked the task identities, one-shape scope, release commit,
  model/agent provenance, and JSON/document/calculator consistency.

No task/compiler observation matrix or experimental model was executed in
this methods review. The synthetic counts in mathematical tests remain
unit-test inputs, not pilot observations.

## Explicitly retained qualifications

1. Coverage concerns the fixed task/arm mixture and assumes independent
   fresh-run outcomes. Model identity or a fresh process does not prove
   that assumption. No task-population confidence statement is made.
2. The prospective shape statistic is an equal-cell-weight mean. The
   historical pooled rate agrees with it at full balanced allocation but
   can differ after attrition. This choice is stated before data; §5 did
   not specify aggregation weights. It is not silently replaced with a
   control-only statistic that avoids treatment-arm shape avoidance.
3. Each returned band has 97.5% nominal coverage under its assumptions;
   the two-estimand family has at least 95% by a union bound.
4. The ten-percentage-point target is coarse pilot precision, not a
   rare-event guarantee or a power calculation.
5. The method supplies §7(5), not the spend ceiling in §7(4). Actual task
   freeze, operational indicator verification, admission/pins, and
   collection remain separate. No stage-2 Δ/N, margin, or verdict is
   invented, and the frozen off-ramps are not weakened.
