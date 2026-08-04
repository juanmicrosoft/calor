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
| **Expressible defects** (effect/null/index/div — the mechanical arm's own checkers) | PR #856 | **0** | native supply exists and the mechanism is **proven** (100% verification-addressable on real corpus code — Calor0410 differential), but the native int-returning surface on these immutable-leaning libraries is either **not value-asserted** (comparer hashes → NoObservableDefect) or **fails arm-divergently** (comparer ordering → correctly excluded as conversion-confounded) |

## The two genuine positives (banked)

1. **The v0.10 ceiling does NOT persist at real scale.** In the dry-run the C#
   arm shipped genuine bugs on **16.7% of runs (3/18 C#-arm runs, across 2 of 6
   tasks)** — build-clean, visible-suite-green, held-out-failing. Unlike the
   v0.10 authored fixtures (C# 9/9), real code leaves measurable escaped-bug
   headroom. Verification has a real target; the blocker is purely substrate,
   not the absence of headroom. **Caution: this is a dry-run signal, n = 6 tasks
   / 18 C#-arm runs, and the dry-run was not powered for it** — a rate estimate
   is not claimed. What it does establish is *existence*: the escapes are
   individually verified genuine, so the authored-fixture ceiling (C# 9/9,
   nothing escapes) demonstrably does not hold on real code.

2. **The verification-addressable-defect mechanism is proven end-to-end.** On
   real converted Serilog/FluentValidation code, an injected effect-discipline
   defect makes the converted `§E` stay pure while enforcement charges the
   effect → **Calor0410** build signal the C# arm has no equivalent of (100%
   addressability differential on the 3 native candidates). When such a defect
   exists in native code, Calor catches it and C# does not. The problem is task
   *supply*, not the mechanism.

## Investigation record — the expressible-stratum 0-eligible numbers

The claim "mechanism proven, supply starved" rests on a specific measurement, so
the numbers are recorded here rather than left to the narrative.

The stratum's `EffectViolation` operator was broadened to any `int`/`long`-returning
method (a `using`-nested `Directory.*` effect producing a deterministic `+1` return
corruption; it fires `Calor0410` because the converter's `§E`-walker does not charge
the nested effect). That broadening is what moved native supply off zero:

| Quantity | Value |
|---|---|
| Native candidates found on the pinned corpus | **3** (before broadening: 0) |
| Differentially **verification-addressable** (predicted check fires on the mutated conversion, absent on the clean one) | **3 / 3 = 100%** |
| **Eligible** under the D-W4.1 predicate | **0 / 3** |

All three exclusions are clause (b) — the defect exists and the check fires, but the
task is not *adjudicable*:

- **Serilog, `GetHashCode`-shaped surface** → `NoObservableDefect`: no covering
  value-asserting test, so the held-out oracle has nothing to fail on. This is the
  corpus-shape lever in miniature.
- **FluentValidation, comparer `Compare`-shaped surface** → `ArmsDiverge`: the two
  arms fail with different signatures, so the outcome is conversion-confounded and
  the candidate is correctly excluded rather than counted.
- The eligibility predicate's **`RequireIdenticalSignature` guard was deliberately
  NOT relaxed** to admit the arm-divergent candidate. Loosening it would have
  manufactured an eligible task by lowering the bar mid-investigation; the 0 stands
  as measured.

Provenance: the operator, the differential addressability probe, the exclusion
accounting and 132 tests are committed in **PR #856** (`w4-expressible-stratum`) —
that is the reproducible half, and it is what the "100% addressable" claim rests on.
The per-candidate dispositions above are from the investigation run's log and are
not themselves a committed artifact; they are recorded here as the finding, at the
granularity the close-out decision actually used.

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
- **Disclosure — this is a novel route to not-adjudicated.** §6.2's parenthetical
  names two triggers for the not-adjudicated branch: sub-2-project *fidelity*,
  and the D-W4.4 *ceiling* branch. Neither fired — fidelity was adequate on two
  projects, and the ceiling check went the *other* way (it cleared, positive #1).
  The trigger here is a **third route: task-supply starvation** — no eligible
  thesis-testing task exists to run the epoch over. This is legitimate under
  §6.1's general principle (the measurement cannot be made, so no verdict may be
  registered) and it routes identically, but it was **not enumerated in advance**
  and is disclosed as such rather than folded silently into an existing trigger.
- **Bank the reusable machinery** — oracle-hidden bundle runner, task-gen
  (logic + revert + expressible strata), the Calor0410 addressability
  differential — all proven end-to-end; v0.12 inherits a working benchmark the
  moment the substrate matures.
- **Call W** routes to its not-adjudicated branch: the measurement half returns
  to substrate engineering (the three levers + the overlay), and v0.12 leads with
  fidelity + checker breadth + a value-asserted corpus. The other proof points
  are adjudicated on their own tracks and are **not** interchangeable: **PP-A2**
  is a Call W *input* (§6 line 167) and resolves to its pre-committed
  **"demand unproven"** value — no adopter by Call W — routing the adoption half
  to a **maintenance-mode posture** at v0.12 planning; **PP-A1 and PP-W5** are
  the v0.11.0 *release* gates, orthogonal to Call W's shape decision. See the
  Call W record.

The measurement half did its job: it bought, cheaply (~$74 of agent spend total)
and honestly, a decisive answer to "can we measure verification's outcome value
on real code at current maturity" — *not yet, and here are the exact three things
that must move first* — plus the one real positive that the ceiling does not
survive real code.
