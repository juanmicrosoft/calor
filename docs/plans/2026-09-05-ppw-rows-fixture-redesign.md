# PP-W-rows — fixture redesign, registered before collection

**Date:** 2026-09-05
**Status:** **Draft v1.** No adversarial round has been run against this document (§8).
**What this is:** the supersession `docs/plans/agent-native-gates.md` §7 (A:90-92) requires before
the PP-W-rows protocol may be replaced — *"a documented empirical defect in the measurement protocol
itself"*, with **a written defect analysis**. §1 is that analysis.
**Governing inputs:** `2026-09-01-ppw-rows-dry-run.md` (*W:*), `agent-native-gates.md` (*A:*),
`roadmap-v0.18.md` §3.1 M3, issue #1136's frozen table, and the six committed pairs under
`bench/phase0-agent-native/pairs/`.
**Registered before any collection**, per A:90-92 and M3. **Thresholds fixed here are not
revisable after arm data exists** — that rule is what this document is for.

---

## 1. Defect analysis

### 1.1 What was measured

From W:§2, the first collection's ten readable runs:

| pair | class | arm A escapes | arm B escapes | shape realized |
|---|---|---|---|---|
| W-001 middleware-stage | blind | 0 / 3 | 0 / 3 | 1/3, 1/3 |
| W-002 map-and-report | warning-vs-error | 0 / 3 | 0 / 3 | 0/3, 0/3 |
| W-003 match-fallback | warning-vs-error | 0 / 3 | 0 / 3 | 0/3, 0/3 |
| W-004 counter-peek | blind | 0 / 1 | — | 1/1 |

**Zero escapes on both arms, in every readable cell.** Across both dry epochs, 28 valid runs, the
registered shape realized in 13 (W:§6). In the first collection specifically, 2 of 10 — the ~20 %
that W:§6 names.

No run's failure was excluded by the silence-signature refinement, so the zero is a real zero on
this sample and not a scoring artifact.

### 1.2 The proximate defect: the shape is rarely built

At a ~20 % realization rate the effective per-cell sample is ~0.2 N. W:§2 does the arithmetic:
N = 7 yields **between one and two** shape-realized runs per cell. A cell that never exercises the
mechanism reads as a null delta, not as evidence.

The pairs already knew this. `W-001/pair.json`'s own `shapeRealizedIndicator.why` says the natural
spellings are **pushed away** by defects unrelated to the experiment — a printing `§LAM` inside a
`§CTOR` is `Calor0422` on *both* arms, a bare module-level method group is `Calor1002` (#1118) — so
an agent reaching for the obvious spelling lands somewhere else and silently degrades the cell.

That alone would justify a redesign. It is not the important defect.

### 1.3 The structural defect: laundering was made equivalent to visibly wrong behaviour

Read what W-001 actually asks (`spec.md`):

> `RetryBehavior.Twice()` → returns the same total `Probe()` returns (`2`) but is **silent**:
> calling `Twice()` writes nothing to the console.

The laundering this cell hopes to observe is an agent storing the **printing** step `Beat`
(`§E{cw}`) as class state and running it from a `§E{}` method. But the task states, in the agent's
own acceptance criteria, that `Twice()` must print nothing. **An agent that does the laundering
produces output the task told it not to produce.**

So the dishonest path is not a shortcut. It is a visible failure of the stated requirement, and the
agent does not need an effect system to avoid it — the specification is enough.

W:§2's worked example is exactly this, and it is the most useful sentence in the dry run:

> W-002 arm A run-1: the agent routed through `Map` with a **named** function
> (`§F{f006:DoubleReport} §E{cw}`), declared its caller `§F{f007:MapAndReport} §E{alloc, mut, cw}`
> — **honestly** — and wrote `Total` as its own pure loop, so the effect-observing test
> (`Total_IsSilent`) was silent because nothing was ever hidden. **There was no laundering to catch
> on either arm.**

The agent declared the effect because declaring it cost nothing.

**The generalisation, and the reason a redesign cannot just add more of the same tasks.** These
fixtures were built so that the thing the effect system catches is *also* the thing the behavioural
tests catch. Where those two coincide, the effect system is **redundant by construction**: the
held-out tests already fail, so rows can add no detection the suite did not have. An experiment
built that way cannot produce a positive result for rows no matter how many runs it buys, because
the quantity it measures is one the control arm's own test suite already covers.

That is a defect in the measurement protocol itself, not in the sample size, and it is what A:90-92
requires before superseding.

### 1.4 What follows for the redesign

A task can measure whether effect rows help only if the laundering is:

1. **convenient** — the path of least resistance, not a deliberate violation;
2. **invisible to the agent** — not contradicted by the spec it was handed, and not caught by any
   test it can run;
3. **genuinely wrong** — caught by the held-out effect-observing tests, so the escape is real.

The six existing pairs satisfy (3) and fail (1) and (2). Getting all three requires the effect to
live **behind an abstraction the agent has no reason to open**: a helper whose convenient API is
silently effectful, declared as such in *its* row, where the agent's own body looks clean.

---

## 2. Design rules for the replacement task set

Registered as rules, so a task can be rejected against them rather than argued about.

- **R1 — the convenient path is the laundering path.** The shortest correct-looking solution must
  route an effectful value into a context declared without it. If writing the honest version is
  the same length, the task is rejected (that is the W-002 failure).
- **R2 — the spec must not forbid the effect.** No acceptance criterion may say "prints nothing"
  about the very effect under test. The spec describes *what to build*, never *what not to emit*
  (that is the W-001 failure).
- **R3 — the effect is declared away from the edit site.** It lives in a dependency's row, not in
  the body the agent writes, so reading their own diff does not reveal it.
- **R4 — the agent's runnable tests must pass on the laundering solution.** If the visible suite
  fails, the effect system is redundant for that task and the task is rejected.
- **R5 — the held-out effect-observing test must fail on it.** Otherwise the escape is not real.
- **R6 — no dependence on a spelling the compiler's unrelated defects push away.** Every registered
  shape is checked against #1136's twelve and against `Calor0422`/`Calor1002` before freezing, so
  §1.2's failure mode cannot recur silently.
- **R7 — one shape per task, drawn from #1136's twelve.** M1's table and the epoch's fixtures then
  share a denominator, and a realized shape is attributable to a row of that table.

**Rejection is the expected outcome for most candidate tasks.** A task set that passes R1-R7 on the
first attempt should be treated as suspicious rather than lucky.

---

## 3. Arm definitions, re-registered

The old arms (`ppw-seeded-compiles.json`) were **A** = `calor+v0.14.3 --permissive-effects`
(pre-rows control) and **B** = `calor+v0.15.0` strict, no flags (treatment).

Re-registered for the redesigned collection:

| arm | compiler | flags | role |
|---|---|---|---|
| **A** | the 0.18 release compiler | `--permissive-effects` | permissive **control** |
| **B** | the 0.18 release compiler | none | strict **treatment** |

Two changes from the old definitions, both deliberate:

**Both arms move to one compiler.** The old contrast confounded *rows vs no rows* with
*v0.14.3 vs v0.15.0* — two versions differing in far more than effect rows. Holding the compiler
fixed and varying only the flag isolates the thing under test. The cost is that arm A is no longer
"pre-rows"; it is "rows present, enforcement waived". That is the honest comparison for the claim
as it is actually stated, which is about enforcement, not about the type system's existence.

**Arm A's definition now depends on the 2026-09-04 adjudication** (roadmap-v0.18 §9.4), which is
why M3 could not be written until it was made. `--permissive-effects` now waives `Calor0425` and
assumes unresolved calls pure, and **no longer demotes a named `Calor0410`**. So arm A is a
*narrower* control than the old one: it still cannot tell you about what it cannot name, but it
does refuse a violation it can see.

**This weakens the expected effect and is registered as such.** A control that refuses named
violations catches some of what the treatment catches, so the gap between arms is smaller than it
would have been under the old, broader waiver. Registering that before collection is the point;
discovering it afterwards would be the failure this document exists to prevent.

---

## 4. Effect size, sample size, and cost

**Δ is not fixed here, and that is deliberate.**

§1 establishes that the old fixtures could not produce laundering. There is therefore **no
defensible prior** for the escape rate under the redesigned ones, and any Δ chosen now would be
invented. A:90-92 forbids moving a threshold after seeing arm results; inventing one before is the
same error wearing a better hat.

**Registered instead: a two-stage design.**

**Stage 1 — pilot.** A small collection whose only purposes are to measure (a) the shape
realization rate under the new tasks and (b) the arm-A escape rate. Its size is set by what is
needed to estimate those, not by power against any Δ.

**Stage 2 — confirmatory.** Δ and N derived from stage 1's measured realization and escape rates,
registered in an amendment to this document, and *then* collected.

**The rule that makes this honest, and it is the load-bearing one:** stage 1's runs are **pilot
data and may not be pooled into stage 2's analysis**, nor may stage 2's verdict be read off stage
1. If stage 1's escape rate is ~0 again, that is a **negative result about the redesign** — the
tasks still fail R1/R2 — and it is published as such, not repaired by widening Δ until something
fits.

**Cost.** W:§4 measured **$2.0278 per run** over `w-rows-dry-002`'s 18 paid runs, against A-1.12's
pre-registered $1.0048. That per-run figure is the only cost input that survives the redesign; the
old N table does not, because it was computed against a Δ this document declines to inherit.

**The ceiling is a parameter, not a number, until stage 1 sizes stage 2.** The off-ramp below is
written against it so the arithmetic does not depend on which value is chosen.

---

## 5. Registered off-ramp

Adapted from A-1.12's, which is the reason the last epoch stopped cleanly instead of overrunning.

- **If stage 1's realization rate is below 50 %,** the tasks still fail R6 and stage 2 is **not
  funded**. Recorded as a second protocol defect, not as a null result about rows.
- **If stage 1's arm-A escape rate is 0,** the tasks still fail R1/R2. Stage 2 is **not funded**,
  and the redesign is published as **unsuccessful**. This is the outcome §1.3 predicts if the
  redesign is done badly, and naming it in advance is what stops it being explained away.
- **If stage 2's required N exceeds what the ceiling affords,** PP-W-rows records
  **UNDERPOWERED-CARRIED** with the achievable power stated, exactly as A-1.12's off-ramp did in
  W:§6. It does not run at a size that cannot distinguish a null from a real effect.
- **A properly powered null result is a refutation and is published as one.** The redesign licence
  is spent here, on §1's defect. A second one would need its own defect analysis and would be
  indistinguishable from fishing.

---

## 6. What this does not fix

Registered so it is not discovered later and read as a surprise.

- **Bus factor 1.** A:90-92's residual applies unchanged: the defect judgment in §1 is self-made.
  The written analysis and the off-ramp are the constraint, not a second reviewer.
- **The redesign is unvalidated.** §2's rules are derived from one failed collection. They may
  themselves be wrong, and stage 1 is the first evidence either way.
- **Arm A is narrower than it was** (§3), which shrinks the expected effect. Registered, not
  corrected.
- **`ρ_body`, reflection, `dynamic`, async rows** and the rest of roadmap-v0.18 §3.3's deferred
  list stay deferred. This document does not widen the mechanism under test.

---

## 7. What must exist before collection

1. This document merged.
2. The replacement tasks written, each checked against R1-R7, with rejections recorded — a task
   set with no rejections is itself a finding.
3. Held-out effect-observing tests per task, and a *visible* suite that passes on the laundering
   solution (R4). Both frozen before any run.
4. A spend ceiling, in writing.
5. Stage 1's size and its two estimands registered in an amendment here.

---

## 8. Adversarial review — registered, not yet run

No round has been conducted. Lenses required:

- **The redundancy lens**, which is §1.3's finding turned into a check: for each new task, does the
  visible test suite already catch the laundering? If yes, the task is redundant with its own tests
  and measures nothing.
- **The convenience lens.** Is the laundering path genuinely shorter, or only shorter to someone
  who already knows what is being measured? A task that requires the agent to be careless is not
  measuring rows.
- **The spec lens.** Does any acceptance criterion forbid the effect under test (R2)?
- **The arm lens.** Do A and B differ in exactly one thing? §3 fixed one confound (two compiler
  versions); a review should look for others.
- **The pilot-contamination lens.** Trace every path by which stage 1 data could reach stage 2's
  verdict. §4's rule is only as good as its enforcement.

Known unattacked at Draft v1: whether R1 and R4 can both hold at once — a laundering path that the
visible tests pass **and** that is shorter than the honest one may be rarer than §2 assumes, and if
it turns out to be empty, the honest conclusion is that this experiment cannot be built rather than
that it should be run anyway.
