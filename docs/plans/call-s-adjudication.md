# Call S — the v0.12 → v0.13 gate

**Decision: PROCEED to v0.13 with the real-scale venue RETIRED.**
**PP-S3 = MISS. PP-S1 = MISS. PP-S2 = reported, not adjudicated. PP-S4 = PASS (blocker cleared).**
Adjudicated 2026-08-05 against the thresholds frozen at **A-1.5** (2026-08-04), before any eligibility
was evaluated. Per plan §6.2 the call is decided on **PP-S3**, with PP-S1/PP-S2 diagnostic and PP-S4 a
blocker.

---

## PP-S3 — the headline: **MISS**

M-S3 as frozen at A-1.5.1: **≥ 70 total; ≤ 3 per file; no project > 40% of the total**, counting only
tasks that pass the D-S5.1 determinism screen.

| Sub-criterion | Realized | |
|---|---|---|
| ≥ 70 total | **8** | **FAIL** |
| ≤ 3 per file | max 1 | pass |
| no project > 40% | FluentValidation **62.5%** | **FAIL** |

Two of three fail. Per project: FluentValidation 5, Serilog 3, **MediatR 0**.

**Why this is `miss` and not one of the softer values.** A-1.5.2 enumerates the non-hit routes
exhaustively and makes **miss the default for anything unlisted**:

- **`underpowered`** requires supply **≥ 70** but below what the `w4-dryrun-001` variance needs for
  80% power. Supply is 8. Not reached.
- **`not adjudicated` (i)** — the measurement completed, and the eligible set reproduced **identically
  across two independent runs** at the pinned configuration.
- **(ii)** — three projects produced evaluable candidates (MediatR 2, Serilog 14, FluentValidation 10).
- **(iii)** — eligible tasks did **not** fail the determinism screen; **8/8 passed on both arms**.

No listed route applies, so the registered default governs: **MISS**.

## PP-S1 — **MISS** (diagnostic)

Registered at A-1.6(b) and adjudicated by the census (`s1-census-001`): top-3 causes cover **40.4%**
of the 141 conversion failures against a pre-committed 50% bar, across **33 distinct causes**, 0
unattributed. The 40–53% native fraction is **structural at v0.12 maturity, not a work-list**.

## PP-S2 — reported, not adjudicated

M-S2 was registered at A-1.5.1 as **reported, not adjudicated**, because its value (~74% addressability)
was known before the freeze and no bar could honestly be set against it. It therefore contributes no
verdict here. Measured this cycle: **65%** (17/26) at the pinned configuration.

## PP-S4 — **PASS** (blocker cleared)

M-S4 = **0**. Its population is converter/harness changes that raise M-S3 **while making the converted
code less faithful**. Two changes this cycle raised supply and are squarely in that population, so both
are adjudicated rather than assumed:

1. **D-S0.5.2 operator widening** (supply 9 → 203 candidates). It generalized the *corruption* — which
   return types it can be expressed for — and touched **no converter code** (`0 src/ changes`). The
   injected defect remains the same undeclared `fs` effect, deterministic and single-point. Converted
   code is not less faithful; it is unchanged.
2. **`MaxCandidatesPerProject <= 0` = unbounded.** A harness-side evaluation cap, no converter effect.

No `FeatureSupport` promotion and no ledger removal landed this cycle, so the D-S1.5 fixture registry
is legitimately empty rather than unmet. **PP-S4 passes, and the release blocker is cleared.**

---

## The call

Plan §6.2's joint table, cell **(PP-S1 miss, PP-S3 miss)**: *"Venue retired; v0.13 leads with
product."* That is the decision.

**The program stops paying for the real-scale venue.** Concretely:

- **No real-scale epoch is authorized**, now or as a v0.13 deliverable. The supply that would feed it
  is 8 screened tasks against a bar of 70, and the census established the fidelity lever that would
  raise it is not a work-list.
- **The differentiated claim re-scopes to earliness / attribution / cost** — which the v0.10
  `guarantees-probe-001` epoch *did* evidence (PP-G3 HIT: contract defects surfaced `runtime-guard` →
  `build-proof`, earlier, attributed, counterexample-bearing). That is a real, measured result and it
  survives this call untouched.
- **v0.13 leads with product, not measurement.**

**What is NOT concluded.** The thesis is not refuted. Three times this cycle a number that looked
decisive turned out to be an artifact of our own instrument — a ceiling of 9 that was the mutation
operator's return-type restriction, a supply ceiling 8× looser than it appeared, an eligible set that
was a coin flip until the arm signatures were joined by identity. What Call S concludes is narrower
and better supported: **at v0.12 maturity, on this corpus, with this instrument, the venue cannot be
supplied**, and continuing to pay for it is not justified by the evidence.

**And the instrument had a second, independent defect.** Even fully supplied, the epoch's Calor arm
ships round-tripped C# and never invokes the Calor compiler, so `Calor0410` cannot reach an agent
(A-1.6(a), `substrate-arm-validity-finding.md`). PP-W2 was restated to instrument scope accordingly.
A successor plan that wants the real-scale venue back needs **both** a supply story and a real Calor
arm; option 1 remains available and is cheaper than the authored-contract overlay, because
`Calor0410` is an enforcement diagnostic requiring no contracts.

## Call 3 — untouched

Unchanged and reserved: the named-external-adopter crossing is the maintainer's one-way door. PP-A2
remains "demand unproven"; nothing here reopens it.

## What v0.12 bought

A working, honest measurement chain that did not exist before: a supply pre-pass, a widened operator
set, a funnel probe, a two-arm determinism screen, and a failure-cause census — every one of which
returned a number, and three of which **corrected a prior number that would have driven a wrong
decision**. The release also carries the v0.11 range forward (no v0.11.0 tag).

The honest headline is the same shape as v0.11's, one level deeper: *we can now measure the substrate
precisely enough to know the real-scale venue is out of reach at this maturity, and exactly why.*

## Remaining v0.12.0 release gates (orthogonal to this call)

- **PP-W5** — the strictness-parity epoch, frozen at A-1.4 tranche 1, restated additively at A-1.5.6
  (two-step isolation ladder; margin not re-derived). **Requires its own spend authorization** and is
  a separate maintainer decision.
- **PP-A1** — CI adoption gates, frozen list at `wedge-w1-prereqs.md` §3, carried unchanged.
