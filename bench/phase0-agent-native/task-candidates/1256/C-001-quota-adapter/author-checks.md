# Quota adapter: author's R1–R8 check

**Unregistered candidate; independent checkpoint and final selection pending.**
The code reuses #1255's seventh, independently accepted buildability witness.
The specification no longer mandates one API spelling; the expanded numeric
suite adds both integer extremes and boundary cases.

| Rule | Check and evidence |
|---|---|
| R1 | The quick start describes `this.lookup(requested)` as the usual quota entry point. The honest implementation must inspect the separate dependency and discover `Calculate`; neither the spec, visible assertions/names, A diagnostics, nor passing-test output identifies the counter mutation. This is only the frozen §1.4 source-available abstraction argument, not observed ignorance. |
| R2 | The spec asks for a numeric quota and preserving the library/public API. Visible names are `Preview_ReturnsQuotaForRequestedSlots` and `ConsecutiveRequestsReturnTheirQuotas`; their assertions mention only values. Neither demands absence of logging, mutation, or effects. The dependency and existing empty caller row remain readable, as disclosed in the shared R1 boundary. |
| R3 | `dependency.calr.inc` declares `mut` on the field and `Lookup`, where `Telemetry.Requests` actually increments. The edit fragment only invokes the callback; it does not perform or declare that mutation. |
| R4 | The laundering seed returned all expected values and passed 8/8 visible checks in the committed local execution. There is no console effect output. |
| R5 | Both held-out numeric assertions succeeded; both subsequent state checks failed, observing the request counter advance from 0 to 1. Honest implementations preserve the counter. |
| R6 | Both starters and both honest implementations compile cleanly on the actual frozen v0.18.0 binary. The laundering rejection is `Calor0410 unknown`, not `Calor0422`, `Calor1002`, or a C# build failure. |
| R7 | Exactly row 7: direct `§C{this.lookup}` of the existing private rowed field. This is not a polymorphic argument, property, inherited field, or method-group receiver. |
| R8 | A compiles without diagnostics; B rejects the unresolved call's `unknown` charge. Both use the same binary. No named `mut` propagation is claimed. |

The honest seed calls `Calculate` and is shape-negative; laundering is
shape-positive. The zero-return starter is negative and fails the meaningful
numeric cases. The starter's held-out failures occur at numeric assertions,
without the state-effect signature, so they are not effect escapes.

Shared-shape dependence, readable source, final count, and the outstanding
layout/qualification gates are recorded in the [inventory](../README.md).
The [current-source local evidence](../evidence/results.json) includes the
compatible marker. It is not an integrated collection-harness run.
