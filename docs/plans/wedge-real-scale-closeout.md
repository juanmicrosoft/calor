# WS-W4/W5 Real-Scale Measurement — Close-Out Finding

**Status: CONCLUDED (2026-08-04). Verdict: real-scale PP-W2 is NOT ADJUDICATED —
the measurement is not viable at v0.11 maturity for structural, evidenced
reasons. This is the wedge plan's pre-committed §6.2 "not-adjudicated" branch,
published as a decisive program input, not a quiet failure.**

This closes the v0.11 measurement half (WS-W4 build + the W5 real-scale epoch).
The adoption half (WS-W1/W2/W3) shipped and is unaffected. Maintainer-approved
2026-08-04 after the feasibility investigation below.

## What was attempted, and what each attempt proved

The real-scale benchmark ran two arms — `csharp` (idiomatic original) vs `calor`
(machine-converted + verification) — on real OSS (Serilog, FluentValidation,
MediatR, pinned) with injected/reverted defects and a hidden held-out oracle
(`w4-dryrun-001`, runner PR #853). Three thesis-testing task sources were built
and measured; all three are supply-starved on this corpus:

| Task source | Built | Live eligible | Why |
|---|---|---|---|
| **Logic mutations** (arithmetic/off-by-one/boundary) | Slice C | yields tasks | but the mechanical Calor arm has **no verification signal** for logic bugs → measures the conversion penalty, not the thesis (dry-run: confounded 2/2/2 tie) |
| **Revert real bug-fixes** (gold standard) | PR #852 | **0** | the cleanly-separable single-source reverts land in **non-native** files (native∩separable ≈ ∅ on this corpus) |
| **Expressible defects** (effect/null/index/div — the mechanical arm's own checkers) | PR (this) | **0** | native supply exists and the mechanism is **proven** (100% verification-addressable on real corpus code — Calor0410 differential), but the native int-returning surface on these immutable-leaning libraries is either **not value-asserted** (comparer hashes → NoObservableDefect) or **fails arm-divergently** (comparer ordering → correctly excluded as conversion-confounded) |

## The two genuine positives (banked)

1. **The v0.10 ceiling does NOT persist at real scale.** In the dry-run the C#
   arm shipped genuine bugs on **16.7% of runs (2/6 tasks)** — build-clean,
   visible-suite-green, held-out-failing. Unlike the v0.10 authored fixtures
   (C# 9/9), real code leaves measurable escaped-bug headroom. Verification has
   a real target; the blocker is purely substrate, not the absence of headroom.

2. **The verification-addressable-defect mechanism is proven end-to-end.** On
   real converted Serilog/FluentValidation code, an injected effect-discipline
   defect makes the converted `§E` stay pure while enforcement charges the
   effect → **Calor0410** build signal the C# arm has no equivalent of (100%
   addressability differential on the 3 native candidates). When such a defect
   exists in native code, Calor catches it and C# does not. The problem is task
   *supply*, not the mechanism.

## The three structural levers (v0.12), now precisely characterized

1. **Converter fidelity (~40–53% native).** Half the real code converts to
   interop/reverts and is excluded; the interesting defect sites (stateful,
   guard-bearing) are disproportionately in the excluded half.
2. **Checker breadth.** The bug-pattern checkers key on narrow shapes — null-deref
   models `Option.unwrap`, not plain reference null; index-OOB keys on specific
   accessor forms converted code rarely produces. Few real defects are
   expressible.
3. **Corpus shape.** Immutable-leaning logging/validation libraries have little
   value-asserted stateful native surface. A value-asserted numeric/collection
   corpus is needed — MediatR already yielded 0.

Plus the parallel track: the **authored-contract overlay** (a deterministic
mechanism to re-apply `§Q`/`§S` to each per-task conversion) — the D-W4.5 arm-ii
channel that does not exist and is the only way to test proof-depth on defect
classes the mechanical checkers can't reach.

## Disposition

- **Run no epoch.** There are no eligible thesis-testing tasks; a mechanical-only
  logic-bug epoch would measure the conversion penalty at power that cannot
  detect the registered effect (§6.1 / dry-run VERDICT.md). PP-W2 → **not
  adjudicated** (A-1.4 registration, additive).
- **Bank the reusable machinery** — oracle-hidden bundle runner, task-gen
  (logic + revert + expressible strata), the Calor0410 addressability
  differential — all proven end-to-end; v0.12 inherits a working benchmark the
  moment the substrate matures.
- **Call W** routes to its not-adjudicated branch: the measurement half returns
  to substrate engineering (the three levers + the overlay), the adoption pair
  (PP-A1/PP-A2/PP-W5 release gates) is adjudicated separately, and v0.12 leads
  with fidelity + checker breadth + a value-asserted corpus. See the Call W
  record.

The measurement half did its job: it bought, cheaply (~$74 of agent spend total)
and honestly, a decisive answer to "can we measure verification's outcome value
on real code at current maturity" — *not yet, and here are the exact three things
that must move first* — plus the one real positive that the ceiling does not
survive real code.
