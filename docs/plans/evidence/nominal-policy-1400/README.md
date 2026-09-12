# D1: nominal Oblivious policy evidence

**Issue:** [#1400](https://github.com/juanmicrosoft/calor/issues/1400), under
[#1082](https://github.com/juanmicrosoft/calor/issues/1082).
**PR:** [#1450](https://github.com/juanmicrosoft/calor/pull/1450).
**Status:** bounded measurement continuation; **no policy decision adopted**.

The parent accepted a current-candidate measurement/migration-feasibility
continuation on 2026-09-12T03:36:50-04:00. The historical evidence is useful,
but its 149 source observations, 384 heuristic targets and unavailable candidate
rejection denominator do not satisfy the current policy-decision gate. The
historical deferral proposal is not an adopted decision or permission to remove
Stage B from the full 0.22 scope.

## Current continuation

[Measurement and source-artifact design](current-candidate/PLAN.md) specifies
the actual-current versus isolated conservative-shadow comparison, resolved
identity/reference/map evidence, denominators, migration controls and review
gates. This is **design only**: no new behavioral patch or measurement has run.

Behavioral work is blocked until the parent supplies accepted merge pins for
both N2 #1381 and N3 #1382. The issue branch will then normal-merge actual main,
retain its measured ancestors and record new immutable candidates. Unmerged
candidate APIs or evidence are not imported as production facts.

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
continuation requires two fresh non-author final-head reviews, actual CI and
the parent's policy adjudication. This plan is not that acceptance.

Stage A remains independently gated; #1444 remains required and #1402 must
rerun its eventual implemented candidate. No production converter takeover,
shipping routing change, suppression, release, research, guard lift, merge or
closure is authorized.
