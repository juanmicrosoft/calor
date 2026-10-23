# D1: nominal Oblivious policy evidence

**Issue:** [#1400](https://github.com/juanmicrosoft/calor/issues/1400), under
[#1082](https://github.com/juanmicrosoft/calor/issues/1082).
**PR:** [#1450](https://github.com/juanmicrosoft/calor/pull/1450).
**Status:** completed current-candidate measurement; scoped revision proposed
for parent adjudication.

The parent accepted a current-candidate measurement/migration-feasibility
continuation on 2026-09-12T03:36:50-04:00. The historical evidence is useful,
but its 149 source observations, 384 heuristic targets and unavailable candidate
rejection denominator do not satisfy the current policy-decision gate. The
historical deferral proposal is not an adopted decision or permission to remove
Stage B from the full 0.22 scope.

## Current continuation

[Measurement and source-artifact design](current-candidate/PLAN.md) specifies
the actual-current, Annotated-only, and conservative comparison. The completed
[2026-09-12 run](current-candidate/runs/2026-09-12-8f9891a1/README.md) preserves
the full reduced row sets, raw reports, attempts, controls, migration evidence,
and deterministic manifest.

The parent supplied accepted N2 `63220eab` and N3/main `8f9891a1` and explicitly
started measurement on 2026-09-12T05:52:23-04:00. The issue branch normal-merged
that actual main as `791e4f44`, retaining its measured ancestors. The non-shipping [compiler](current-candidate/source/compiler/README.md) and
[migration](current-candidate/source/migration/README.md) source archives were
pinned before execution. No future-child or unmerged behavior is assumed.

The result proposes a specifically bounded revision: treat identity-proven
nominal Oblivious values from the actual private metadata profile as possibly
null at the measured direct native-return boundary. Initialization and selected
method-input behavior for those producers, and source-declaration `None`,
remain withheld until #1401 preserves evaluated nullable context and
declaration identity and #1402 adds direct boundary controls and reruns the
implemented candidate.

Across 289 configured files per mode, 177 were attempted, 6 remained configured
exclusions, and 106 Serilog files were not reached after the same restore
failure in all three configurations. Annotated-only enforcement newly rejected
one MediatR file. Conservative widening added no corpus rejection because the
attempted captures contained no identity-proven nominal Oblivious source row.
Two bounded actual private-metadata controls did reject only under the
conservative policy. FluentValidation's nullable-disabled source inventory
contains 874 relevant `None` value-declaration rows, but current conversion
does not preserve their source contract, so they are not relabelled as
compiler fallout.

The fixture-specific migration candidate faithfully represents its selected
nullable-disabled declarations as nullable Calor references and preserves null,
identity, construction, and downstream exception behavior. It is not a
corpus-wide converter implementation and retains an unrelated `Calor0200`
limitation.

Capacity was [recorded before behavior](https://github.com/juanmicrosoft/calor/issues/1400#issuecomment-5644506295)
and in N0's current D1 row: Copilot / GPT-6 Astra,
`ada009ab-5eb3-41ac-82cc-68fa9b4e158a`, `nominal-policy-1400`, one bounded M
AI implementation/measurement/classification continuation. Provisional
checkpoint and two-review target: **2026-09-12, conditional on dependency
availability**. This is not human staffing, engineer-day equivalence or a
delivery guarantee.

## Immutable historical checkpoint

The complete former evidence tree is preserved byte-for-byte under
[`historical/pre-t1/`](historical/pre-t1/). Its
[398-line README](historical/pre-t1/README.md), data manifest, all 60 compressed
artifacts, source archives and reproduction scripts retain their original
contents and source hashes. No old count or defect is restamped as post-repair.

Historical relative links and reproduction paths refer to the original tree.
The [original rendered README at `a0ed611e`](https://github.com/juanmicrosoft/calor/blob/a0ed611ed1af723d9c0628e56ca2bee14e2dd0d8/docs/plans/evidence/nominal-policy-1400/README.md)
retains that layout; its reproduction instructions pin the separate pre-T1
checkout `c2684b461cdbe170f5d64ab45383abefa8f1f729`.
Measurements remain attributed to `39348d8c` / `d6061f9c`, not `a0ed611e` or
the continuation.

The prior integrated-head reviews and CI cover `a0ed611e` only. The completed
continuation still requires two fresh non-author final-head reviews, actual CI,
and the parent's policy adjudication. The proposal is not yet the adopted
decision.

Stage A remains independently gated; #1444 remains required and #1402 must
rerun its eventual implemented candidate. No production converter takeover,
shipping routing change, suppression, release, research, guard lift, merge or
closure is authorized.
