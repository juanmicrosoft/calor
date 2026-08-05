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

**Stated precisely:** with MediatR at 0 only two projects contribute, so "no project > 40%" is
*arithmetically unsatisfiable* — two nonzero shares cannot both be ≤ 40%. It is a consequence of the
total collapsing, not independent evidence. **The load-bearing failure is 8 against 70.**

**Why this is `miss` and not one of the softer values.** A-1.5.2 enumerates the non-hit routes
exhaustively and makes **miss the default for anything unlisted**:

- **`underpowered`** requires supply **≥ 70** but below what the `w4-dryrun-001` variance needs for
  80% power. Supply is 8. Not reached.
- **`not adjudicated` (i)** — the measurement completed, and the eligible set reproduced **identically
  across two independent runs** at the pinned configuration.
- **(ii)** — three projects produced evaluable candidates (MediatR 2, Serilog 14, FluentValidation 10).
  **Disclosed conflict:** `substrate-plan-v0.12.md` §5 still carries an earlier wording of this same
  trigger — *"fewer than 2 projects pass the frozen **fidelity bar**"* — and under **that** wording it
  **would fire**, because **0 of 3** projects reach M-S1's 0.70 (0.469 / 0.400 / 0.532). A-1.5.2's
  parenthetical describes that wording as an "earlier draft"; it is in fact still merged on `main`,
  which is a plan/annex sync defect corrected in the same change as this record. The annex governs
  (plan D-S5.2 delegates the trigger list to A-1.5; §6.1 adjudicates on the A-1.5-registered values).
  **And the call is invariant either way:** §6.2's decision table has a single combined column
  — *"PP-S3 miss / not adjudicated"* — whose (PP-S1 miss) cell is "Venue retired" for both. Nothing
  in this adjudication depends on resolving the conflict.
  Under the strictest reading of "evaluable" (= *eligible*, not merely enumerated) the trigger still
  does not fire: FluentValidation and Serilog both produced eligible tasks, so exactly 2 projects
  qualify and "fewer than 2" is false — the harness records `ProjectsWithEligibleTasks: 2`.
- **(iii)** — eligible tasks did **not** fail the determinism screen; **8/8 passed on both arms**.

No listed route applies, so the registered default governs: **MISS**.

## PP-S1 — **MISS** (diagnostic)

Registered at A-1.6(b) and adjudicated by the census (`s1-census-001`): top-3 causes cover **40.4%**
of the 141 conversion failures against a pre-committed 50% bar, across **33 distinct causes**, 0
unattributed. The 40–53% native fraction is **structural at v0.12 maturity, not a work-list**.

**Corroborated by a second, independent route:** PP-S1 also misses on **M-S1's own frozen bar** —
≥ 0.70 on ≥ 2 of 3 projects, realized **0 of 3**. Two routes agreeing is stronger than one.

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

**A third change also raised supply and is adjudicated here, because an earlier draft of this record
missed it and it is the one that most resembles the failure mode M-S4 exists to catch.** The
arm-failure-signature canonicalization (inside `76f932f3`) moved supply **5 → 8** by taking
`ArmsDiverge` from 3 to **0** — and `ArmsDiverge` is **M-S5's metric, the semantic-fidelity guard**.
It is nonetheless not an M-S4 event: the three exclusions were an *instrument artifact*, not real
divergence — both arms failed the identical test sets, and the arms were being compared positionally
across two differently-ordered runs. The fix joins them by test identity and is pinned by a
regression test. **Disclosed with it:** M-S5's baseline is registered as "the first post-freeze funnel
run", which is the **post-fix** run at `ArmsDiverge` = 0 — i.e. the baseline was set by this change.
That direction is conservative (0 is the strictest possible baseline) and no emitter merge landed, so
the ratchet was never exercised.

**PP-S4 passes VACUOUSLY, and the distinction matters.** M-S4's population is verifiably empty:
**no v0.12 commit touched `src/` at all**, and `FeatureSupport.cs` is untouched since `4d7f5887`
(v0.11). So M-S4 = 0 is checkable from the diff, independent of any instrument.

**But its registered instrument was never built.** `bench/phase0-agent-native/fixtures/d-s1.5/` does
not exist, and neither does the `d-s1.5-fixtures` CI job — A-1.5.7 froze the registry "by location,
schema, and entry point", and two of those three are absent. D-S1.4's `NativeFraction` CI assertion
likewise never landed, so A-1.5.3's `ExcludePatterns` pin remains advisory as it said it would.
Calling the registry "legitimately empty" (an earlier draft's wording) describes a registry that
exists and is empty; it is absent. **The blocker is cleared because nothing required gating, not
because the gate was exercised** — and A-1.5.7 opens by naming exactly this failure ("a blocker with
no measurement path is unfalsifiable"). **Carried to v0.13 as debt: the first `SupportLevel`
promotion re-arms PP-S4 with no CI to enforce it.**

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
  survives this call untouched. **With its own registered caution carried forward, not dropped:**
  PP-G3 is an **authorable-fixture** result, and this plan's own §0.2 records that *the v0.10
  authorable-fixture ceiling does not survive real code*. So the re-scoped claim is evidenced **at
  fixture scale only**. No real-scale evidence for it exists — and A-1.6(a) explains why none was ever
  produced: the real-scale arm never contained Calor.
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

## Why no achievable WS-S2/WS-S3 work reaches the bar — the arithmetic the call rests on

The strongest objection to retiring is that **WS-S2** (checker breadth) and **WS-S3** (a value-asserted
subject) were the two registered levers aimed at exactly the exclusions that killed most candidates —
`NoObservableDefect` took 10 of 26, and addressability ran at 65% — and **neither was ever executed**.

The arithmetic defeats it. After build and `RecoverBuildAsync`, this corpus yields **26 evaluable
candidates**, an 8× collapse from the 203 enumeration ceiling. **Even at 100% eligibility — zero
clause-(a) losses, zero clause-(b) losses, 100% addressable — 26 < 70.** WS-S2 and WS-S3 operate on
the *conversion* of candidates into eligible tasks; they cannot manufacture candidates. The binding
constraint is the 203 → 26 collapse itself, which is converter/pipeline maturity — and PP-S1 = MISS
says that is structural (top-3 = 40.4% across 33 causes, MISS under 9 of 11 bucketings).

Realized funnel yield was **8/203 = 3.9%**, against A-1.5.4's registered sensitivity of "~37 eligible
at 25% attrition". The freeze's own optimistic case was an order of magnitude high.

## Workstream dispositions

Plan §6.2: *"In every branch the program continues; Call S decides shape."* Shape for all four:

- **WS-S1 (fidelity)** — **continues, on its narrowed justification only.** PP-S1 = MISS ends it as a
  ranked-fix workstream, but A-1.6(b) already re-based it: `NativeFraction` is the quality metric of
  the **shipped `calor import` path** and stands alone. It does not continue as epoch supply work.
- **WS-S2 (checker breadth)** — **not executed; closed unstarted.** Its entire rationale was raising
  addressable supply for the retired venue. `D-S2.1` (plain reference null-deref) has independent
  product value and is carried to v0.13 as a candidate, not a commitment.
- **WS-S3 (corpus shape)** — **not executed; closed unstarted.** Same rationale, same fate. The
  `NoObservableDefect` finding it was built to attack is retained as evidence about the corpus.
- **WS-S4 (authored-contract overlay)** — **the consequential one, and it is closed unresolved.** Its
  exit criterion was "mechanism shipped **or** D-S4.2 negative published"; neither happened. It is the
  **only channel connected to the proof-depth headline** (§0.1), so with the venue retired the
  headline claim has no live measurement channel at all. That is stated plainly rather than left for a
  reader to infer, and it is the single largest open question handed to v0.13.

## Re-entry conditions, quantitative

"Retired" must be distinguishable from "abandoned", so the conditions are registered rather than left
qualitative. A successor plan may re-open the real-scale venue when **both** hold:

1. **Supply** — the 203 → 26 build-and-recovery collapse is repaired, **or** the corpus is widened
   enough that ≥ 70 evaluable candidates survive it (on current yield that is roughly 3× more pinned
   subjects, and MediatR contributed 0, so subject *selection* matters as much as count).
2. **Instrument** — a Calor arm that actually invokes the Calor compiler, so the diagnostic reaches
   the agent (A-1.6(a)). Option 1 there remains cheaper than the overlay, because `Calor0410` is an
   enforcement diagnostic requiring no contracts.

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
