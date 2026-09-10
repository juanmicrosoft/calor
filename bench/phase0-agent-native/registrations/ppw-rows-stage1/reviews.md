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

## Pre-freeze oracle correction: independent review

Reviewer: `oracle-method-review`, independent read-only code-review agent,
model **gpt-5.5**. This remains AI technical review, not experimental
model execution or spending approval.

The old authoring evidence used a numeric assertion before its state
assertion. Inspection and four actual locally compiled regression controls
showed that a wrong value or thrown exception could therefore mask an
actual state violation. Before task freeze or collection, the proposed
final suites split those checks into separate numeric and state cases.
The state observer catches the call exception and still checks the actual
before/after state; numeric tests disclose wrong values and exceptions.
Historical authoring source/evidence is retained, not rewritten.

The reader inspected the new tests and the four raw control records:
three effectful-wrong-value programs and one effectful-throwing program.
Each compiled under the real permissive release compiler; each produced
two genuine state failures alongside two numeric/exception failures.
These are deterministic instrument controls, not agent observations.

**Oracle disposition:** the reader found the split consistent with frozen
R4/R5. R4 requires each canonical laundering solution to pass the entire
visible suite. No frozen text conditions empirical effect escapes on an
otherwise-correct numeric output. Numeric failure alone remains insufficient
for an escape. §9 now explicitly discloses coexisting failures and does not
claim otherwise-correct outputs.

The review also identified two integration findings:

1. The merged instrument's `seeded.clean` admission does not yet accept the
   prepared tasks' explicit `seeded.laundering`/`seeded.honest` roles.
   This is a real task-freeze/collection integration obligation. No legacy
   alias was used to falsely label laundering as honest, and this methods
   amendment does not claim task freeze or final admission.
2. The draft said cache-cold regeneration was already implemented. Corrected
   to an explicit **prospective pre-collection requirement**. Independent
   review and merging of actual enforcement remain required; a local
   draft-helper experiment is not evidence of merged enforcement.

The size, mathematical method, fixed weights, point-estimate off-ramps,
original sections 1–8, and absence of collected data are unchanged.

## Independent remediation review

Reviewer: `prospective-admission-review`, independent read-only code-review
agent, model **claude-sonnet-5**.

**Clean: no significant issue found in the three-file methods follow-up.**
The reader checked the actual untracked test/source structure, current
instrument admission, and tracked prose/JSON. They verified that the
unresolved task-admission mismatch remains disclosed, cold-cache enforcement
is a future requirement rather than a shipped claim, and no task freeze,
epoch pin, funding, or collection authorization is asserted.

This reader inspected source structure rather than re-executing the task
matrix. Their structural R4 assessment is not substituted for observed
visible-suite execution; actual runs remain the task-freeze evidence.
The author separately reran the thirteen mathematical/candidate guards
and the byte comparison of frozen sections 1–8: all passed.
