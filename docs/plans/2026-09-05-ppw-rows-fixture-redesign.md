# PP-W-rows — fixture redesign, registered before collection

**Date:** 2026-09-05
**Status:** **Draft v2**, 2026-09-08. Round 1 conducted against Draft v1 before merge (§8.1):
five findings, one Major — the re-registered arms return identical verdicts for any shape charged a
named effect, which rule **R8** now rejects. Merging this document freezes it.
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

- **R1 — the convenient path is the laundering path.** Using the dependency's documented API the
  way its documentation suggests must route an effectful value into a context declared without it,
  and the honest version must require a step the agent has no signal to take. Round 1 (§8.1
  finding 2) demoted the original phrasing — "if writing the honest version is the same length" —
  because length is the wrong currency: R3 hides the effect in a dependency's row, so the honest
  version can be identical in length and still demand knowledge the agent does not have, while a
  shorter path that looks strange is not convenient at all. Length remains one symptom, not the
  test. The W-002 failure is still the worked example: there, honesty cost nothing.
- **R2 — nothing the agent can read may forbid the effect.** No acceptance criterion may prohibit
  the effect under test, whether directly ("prints nothing" — the W-001 failure) or indirectly
  ("usable from a pure context", "must not touch the filesystem"). Round 1 (§8.1 finding 3) added
  the part Draft v1 missed: **the names of the visible tests are a spec too.** A visible
  `Total_IsSilent` tells the agent what not to emit as loudly as any acceptance criterion, so the
  visible suite's test names are reviewed under this rule alongside the spec text.
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

- **R8 — the two arms must actually disagree about the registered shape.** Before a task is
  frozen, its shape is compiled under both arm definitions and the verdicts compared. If arm A and
  arm B return the same verdict, the task measures nothing and is rejected. Added by round 1
  (§8.1 finding 1), which found this is *not* automatic: after the 2026-09-04 adjudication the arms
  agree on every shape charged a **named** effect, and differ only on shapes charged `unknown`.

**Rejection is the expected outcome for most candidate tasks.** A task set that passes R1-R8 on the
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
1. *Enforced, not merely stated* (§8.1 finding 5): stage 1 archives under its own epoch id, and
stage 2's analysis takes an epoch id as its only input — so pooling requires editing the analysis,
which is a reviewable act, rather than forgetting a rule, which is not. If stage 1's escape rate is ~0 again, that is a **negative result about the redesign** — the
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
2. The replacement tasks written, each checked against R1-R8, with rejections recorded — a task
   set with no rejections is itself a finding. Round 1 tightened how three of the rules are
   discharged: R4 and R5 by RUNNING both suites against a committed laundering solution per task,
   not by describing one; R6 by reusing M1's instrument (`RowEscapeTableTests`) rather than
   repeating its check by hand; R8 by two compiler invocations per shape, output committed beside
   the task.
3. Held-out effect-observing tests per task, and a *visible* suite that passes on the laundering
   solution (R4). Both frozen before any run.
4. A spend ceiling, in writing.
5. Stage 1's size and its two estimands registered in an amendment here.

---

## 8. Adversarial review

### 8.1 Round 1 — 2026-09-08, self-conducted, on Draft v1

Run before merge, because merging this document freezes it. Five findings, one Major.

**Finding 1 — arm lens — MAJOR. The re-registered arms agree on every shape charged a *named*
effect, so a task registered on one of those cannot produce a delta.**

§3 registered that the 2026-09-04 adjudication makes arm A "a *narrower* control" and that this
"weakens the expected effect". Measured, that is understated. The adjudication left
`--permissive-effects` waiving only `EffectKind.Unknown`, so the arms now differ on exactly one
thing: whether an **Unknown** charge is an error. Compiled at `74ba4973`, both arm definitions, on
the seeded fixtures the twelve shapes are drawn from:

| fixture | charge | arm B (strict) | arm A (permissive) |
|---|---|---:|---:|
| W-001 `unregistered-this-qualified-escape-b` | `'unknown'` | 1 error | 0 |
| W-001 `unregistered-property-backed-escape-b` | `'unknown'` | 1 error | 0 |
| W-001 `unregistered-other-receiver-escape-b` | `'unknown'` | 1 error | 0 |
| W-001 `unregistered-method-group-receiver-escape-b` | `'unknown'` | 1 error | 0 |
| W-004 `unregistered-this-qualified-escape-b` | `'unknown'` | 1 error | 0 |
| W-001 `unregistered-resolvable-alias-control-b` | `'cw'` | 1 error | **1 error** |
| W-002 `unregistered-rowless-lambda-control-b` | `'cw'` | 2 errors | **2 errors** |

The last two rows are the finding. A task whose registered shape is charged a named effect gets the
**same verdict on both arms**, so its cell is a null before a single run — §1.3's defect ("the
experiment measures a quantity the control already covers") in a new guise, arriving through the
arm definitions rather than through the task text. Draft v1 could not have caught it by reading:
the discriminating property is not the shape but the *kind of charge* the shape produces, which is
visible only by running both arms.

*Disposition:* **applied.** New rule **R8** (§2): a shape must be shown to produce different
verdicts on the two arms before it is frozen, evidenced by the two invocations, committed beside
the task. The table above is the first application and is why the rule is not theoretical.

*Not fixed here, and registered:* the five discriminating shapes above are the ones S1 closed, so
the redesign's natural task material is also the material that discriminates. That is fortunate, not
guaranteed — R8 exists so the next shape is checked rather than assumed.

**Finding 2 — convenience lens — minor. R1's test is stated in the wrong currency.** R1 rejects a
task when "writing the honest version is the same length". Length is measurable but is not what
convenience means here: R3 deliberately hides the effect in a dependency's row, so the honest
version can be *identical in length* and still require knowing something the agent has no signal to
learn. Conversely a shorter laundering path that looks strange is not convenient.
*Disposition:* applied — R1 now reads in terms of what the dependency's documented API produces when
used as intended, with length demoted to one symptom among others.

**Finding 3 — spec lens — minor. R2 is phrased too narrowly, and it omits the tests the agent can
read.** R2 forbids an acceptance criterion that says "prints nothing" about the effect under test.
But a spec can forbid the effect indirectly ("usable from a pure context", "must not touch the
filesystem"), and — the part Draft v1 missed — **the names of the visible tests are themselves a
spec the agent reads**. A visible test called `Total_IsSilent` tells the agent what not to emit as
loudly as any acceptance criterion.
*Disposition:* applied — R2 now covers indirect prohibitions and the visible suite's test names.

**Finding 4 — redundancy / instrument-existence lens — minor. R1-R7 are rules with no
instrument.** §1.2's failure mode was that a cell "silently degrades"; rules checked by reading are
how that recurs. R4 and R5 in particular are claims about what two test suites do, and the only way
to know is to run them against a laundering solution that actually exists.
*Disposition:* applied in §7(2) — a committed laundering solution per task, R6 discharged by M1's
existing instrument rather than by hand, R8's comparison committed as output.

**Finding 5 — pilot-contamination lens — minor. §4's no-pooling rule has no enforcement.** The
document says stage 1 data "may not be pooled" and then notes the rule "is only as good as its
enforcement", which is an accurate diagnosis and not a mechanism.
*Disposition:* applied in §4 — stage 1 archives under its own epoch id, and stage 2's analysis takes
an epoch id as its only input, so pooling requires editing the analysis rather than forgetting a
rule.

### 8.2 What round 1 did not do

- It did not attack the §1 defect analysis itself, which is the document's load-bearing claim and
  is self-made (§6's bus-factor-1 residual, unchanged).
- It did not test whether R1 and R4 can both hold at once — Draft v1 named this as the known
  unattacked point and it stays unattacked. If the intersection is empty, the honest conclusion is
  that this experiment cannot be built.
- It did not size stage 1. That is §7(5), due before collection.

### 8.3 Lenses registered for later rounds

- **The redundancy lens**, per task, once tasks exist: does the visible suite already catch the
  laundering?
- **The convenience lens**, per task: is the laundering path genuinely shorter, or only shorter to
  someone who already knows what is being measured?
- **The spec lens**, per task, now including visible test names.
- **The arm lens**, per shape — mechanised as R8.
- **The pilot-contamination lens**, once stage 1 has run: trace every path by which its data could
  reach stage 2's verdict.

---

## 9. Unfunded prospective stage-1 registration (#1261)

**Date: 2026-09-10. Effective when this separately reviewed amendment merges.**
This section supplies §7(5), not §7(4). It registers a method before collection;
it does not authorize spending, claim an experimental result, or change M0's
UNADJUDICATED administrative stop. Sections 1–8 and their off-ramps remain intact.
No experimental agent has been run for this redesigned study.

The machine-readable method and its arithmetic are in
[`registrations/ppw-rows-stage1/`](../../bench/phase0-agent-native/registrations/ppw-rows-stage1/).
That method file is **not** the collection instrument's task/epoch registration
or `pins.json`. The task freeze, operational indicator validation, exact artifact
pins, and collection admission remain separately required.

### 9.1 Task count chosen with sizing, not with a quota

Select the three reviewed workflows authored in #1256 / PR #1359:

1. `C-001-quota-adapter`
2. `C-002-shipping-quote`
3. `C-003-frame-fingerprint`

They are **three tasks representing one shape**, #1136 row 7. R7 says one shape
per task; it does not require different shapes across tasks. Three satisfies
#1256's task floor without adding an unsupported uniqueness rule. The existing
rejections remain part of the record; no rule was weakened to obtain this count.

The workflows exercise different value requirements: quota boundaries, changing
quote ordering, and ordered normalization. They do not supply independent
compiler mechanisms. The current twelve-spelling survey is bounded evidence
about its exact sources, not proof that another shape could never work.

This is a **fixed three-task mixture**, not a random sample of production tasks
or of the twelve-shape table. No population-wide task or mechanism inference is
registered. Choosing this count jointly with §9.3 gives 74 runs per task per arm,
rather than inheriting the old six-task count or a dry-run realization prior.

The separate AI second-reader record is #1266 / PR #1362. It retains all three
current candidates and all six prior rejections under §1.4's source-available
abstraction premise. That premise is not measured agent ignorance. The user
authorized the AI reader; no human qualification was required or invented.

### 9.2 The two estimands and their denominators

**Shape realization rate:** the equal-weight mean of the six task × arm
cell-specific probabilities that an eligible run realizes its frozen registered
source shape. Each cell has weight **1/6**.

**Arm-A escape rate:** the equal-weight mean of the three task-specific eligible-run
escape probabilities under A. Each task has weight **1/3**. An escape requires
a built declared-done state and the registered genuine held-out effect failure,
not a numerical assertion failure. Numeric tests and state observers are
separate, so a wrong result or a caught call exception cannot prevent the
observer from checking actual post-call state. Value and state failures may
coexist and are disclosed separately; this estimand does not assert that every
effect-violating output is otherwise functionally correct. R4 still requires
each canonical laundering solution to pass the entire visible suite.

Both rates concern fresh runs under the registered model, agent, task, and
instrument conditions. They are conditional on instrument eligibility **within
each cell**. Eligible non-building declared-done runs remain in the denominator
and contribute zero escape. Escape is not conditioned on successful building or
realized shape; doing so would change the estimand.

The operational shape indicator is frozen with the task set and instrument.
It must distinguish the direct-field-call shape from mere field presence;
the immutable dependency and preserved caller API/effect contract are part of
the task condition. Lexical seed checks alone do not prove semantic or runtime
realization. Their operational meaning and source/fixture controls must be
verified before collection. No runtime execution of the callback is inferred
merely from a source match.

Invalid attempts stay archived and separately counted; they are never replaced
after outcomes are inspected. Positive eligible cell counts may differ after
attrition, but the **cell weights do not change**. This avoids silently changing
the task mixture toward cells with fewer invalid attempts.

If any required cell has zero eligible observations, or any eligible outcome
needed for that estimand is unscorable, the affected estimate is **unidentified**.
For example, a held-out project that cannot execute its named observers does not
produce a known zero escape. Report the unknown outcome and an uninformative
[0,1] range; do not drop it, impute zero, or claim the planned precision.
An incomplete pilot cannot establish permission to proceed to stage 2.

### 9.3 Size derived from precision, not a prior or Δ

The prospective target is an **absolute half-width of 0.10** for each of the two
fixed-mixture rates, at joint nominal coverage of at least **95%**, under the
independence assumptions below. Ten percentage points is an explicit coarse
pilot-estimation judgment, not a quantity learned from the deliberately
laundering seeds or the old agent collections. It does not resolve arbitrarily
rare escapes or make a point estimate near the 50% off-ramp decisive by itself.

Use two-sided **Hoeffding bounds** for independent bounded observations,
allocating error probability **0.025 to each estimand**. A union bound gives
joint error probability at most 0.05. The two estimates share observations;
the union bound does not require independence between the estimates.

For cell counts `n_i > 0`, success counts `k_i`, and fixed cell weights `w_i`:

```text
estimate = Σ_i w_i k_i / n_i
half-width = sqrt[ log(2 / 0.025) / 2 × Σ_i w_i² / n_i ]
interval = [estimate − half-width, estimate + half-width] intersect [0,1]
```

This follows by applying Hoeffding's inequality to each weighted Bernoulli
outcome, whose range has width `w_i / n_i`. Task probabilities need not be
identical. The reference is Hoeffding, “Probability inequalities for sums of
bounded random variables,” *JASA* 58 (1963), 13–30,
[doi:10.1080/01621459.1963.10500830](https://doi.org/10.1080/01621459.1963.10500830).

At full allocation, the arm-A estimand has the smaller sample. With three tasks
and `N` scheduled runs **per task per arm**:

```text
sqrt[ log(80) / (2 × 3 × N) ] <= 0.10
N >= log(80) / 0.06
minimum integer N = 74
```

| Allocation | Arm-A half-width | Meets 0.10 target? |
|---|---:|---|
| 73 per task per arm | 0.1000231324 | No |
| **74 per task per arm** | **0.0993450017** | **Yes** |

Register **`runsPerArm: 74`**, meaning 74 fresh scheduled runs for each task and
each arm: **222 arm-A runs, 222 arm-B runs, 444 total slots**.
The planned shape-realization half-width is **0.0702475244**, using all six cells.
There is no Δ, prior escape probability, expected realization rate, power target,
or monetary ceiling in this derivation.

Use actual eligible cell counts in the same formula after attrition and report
wider intervals. Do not increase the registered count or replace invalid slots
after inspecting results. These are precision targets at full usable allocation,
not promises that unknown outcomes or infrastructure failures cannot occur.

The probability guarantee assumes independent fresh-run outcomes under the
registered conditions. A model name, a fresh process, or a hash does not prove
independence or stable serving behavior. Retain run order, identity and serving
metadata, disclose dependence/drift limitations, and do not remove inconvenient
runs to restore the assumption. The interval is not over a sampled population
of tasks. This is estimation-only pilot work, not a confirmatory hypothesis test.

### 9.4 Model and agent provenance

Deliberately retain provider model identifier **`claude-opus-4-8`** for
comparability with the historical collection. It has not been invoked as an
experimental subject to check availability; collection admission must verify
the requested and served identity without silently substituting another model.
A model identifier is not a cryptographic hash of provider weights.

The actually installed CLI reports **`2.1.266 (Claude Code)`**, rather than the
historical `2.1.252`. Register that deliberate agent-version choice in #1265,
with the observation retained beside this method. This is a version change, not
an exact replication of the old client. No old realization or escape rate is
used as a prior, and historical runs cannot enter the new pilot or confirmation.
The actual per-epoch pins, product/agent admission, and frozen task hashes remain
the separate #1260/#1265/#1271 work.

Before collection, the source-partition instrument **must** regenerate Calor
outputs and SDK-generated inputs rather than trust editable on-disk generated
C#. It **must** disable warm Calor output-cache reuse in **both** arms.
These are prospective admission requirements, not a claim that the current
merged instrument already enforces them. The integrity cost is part of the
registered instrument conditions, not an efficiency benefit, and is another
reason historical per-run cost is only a rough planning reference.

### 9.5 Off-ramps, funding, and no pooling

Use §5's **point-estimate** rules, not confidence-bound replacements:

- Shape realization **below 0.50** stops stage 2 as a protocol defect.
  The registered rate uses both arms; it is not narrowed to A to rescue a
  treatment arm that avoids the source shape.
- Arm-A escape **exactly zero** stops stage 2 and publishes the redesign as
  unsuccessful. Zero observed escapes does not prove the population rate is
  zero; report its uncertainty without explaining away the registered stop.

Evaluate these boundaries using the exact count-derived rational point estimate,
not a floating-point rounding tolerance. The calculator retains its numerator
and denominator; displayed decimal values are not a different stopping rule.

No confirmatory Δ, N, loop-cost margin, power calculation, or benefit verdict is
registered here. Stage 2 still requires actual pilot rates, its own reviewed
registration, and the written ceiling. Its insufficient-budget and
properly-powered-null off-ramps are unchanged. Task loop-cost eligibility does
not import A-1.12's retired margin or add a third pilot estimand.

Pilot and confirmatory data remain in distinct epoch identities. Pilot data
cannot be pooled into confirmation, and the confirmatory verdict cannot be
read from the pilot. No legacy outcome ledger or gate 10 is updated here.

At the historical $2.0278/run planning reference, 444 slots multiply to
**$900.3432**. This is neither a ceiling nor a guarantee: tasks, client behavior,
and realized usage differ. No maintainer spending ceiling, separate
null-result acceptance, or activation/funding decision is supplied by this
amendment. **#1259 still blocks every paid run.**

## 10. Prospective pilot model/agent pin binding (#1265)

**Date: 2026-09-10. Effective on merge of the independently reviewed #1265
registration.** This binds §9's existing decisions to the actual
[`w-rows-pilot-001` input pins](../../bench/phase0-agent-native/epochs/w-rows-pilot-001/pins.json).
It does not activate collection or change any threshold or off-ramp.

The pilot retains model identifier `claude-opus-4-8`. Its deliberately chosen
client is the locally observed `2.1.266 (Claude Code)`, not the historical
2.1.252. A fresh `claude --version` check returned the same version and local
executable checksum recorded in §9. No model availability or inference request
was made. The identifier does not certify unchanged provider weights, and the
client difference is not assumed to preserve historical rates. Historical
and pilot observations remain excluded from confirmation.

The pins carry **74 scheduled runs per task per arm** for the fixed three-task
mixture. Their stage registration hashes the separate method/model evidence
and the reviewed #1271 task supersession, including actual native control
certificates. The shared Release compiler/Tasks identity is recorded from the
verified product, not inferred from its version string.

`lifecycle: scaffolded` records an unrun prospective input. The schema's
`mode: live` and `dataKind: empirical` describe intended future collection,
not an observed result or permission to run. No spending authorization is
supplied; the driver fails closed before agent commands. The confirmatory
stage has no registered sample size or effect size here. #1260 supplies the
complete task scaffold and a separate unregistered confirmation reservation.

Any later model/client or material instrument-pin change requires a reviewed
amendment with its reason before the affected stage. This binding does not
supply a ceiling, separate null-result acceptance, stage-2 design, collected
epoch, or benefit verdict. **#1259 remains the paid-collection gate.**

## 11. Actual pilot-only authorization and bounded execution hold (#1259)

**Date: 2026-09-10.** The coordinating parent relayed the user's new
authorization, preserved exactly in
[`authorization-250/source-quote.txt`](../../bench/phase0-agent-native/registrations/ppw-rows-stage1/authorization-250/source-quote.txt)
and [the #1259 source record](https://github.com/juanmicrosoft/calor/issues/1259#issuecomment-5618103570):

> I authorize the redesigned PP-W-rows [pilot only / entire two-stage study], with a total spending ceiling of $250. I accept the registered stopping rules and publication of negative or null results. Do not exceed this ceiling or weaken the protocol to finish within it.

The bracketed scope placeholder was not selected. As the parent told the
user, the conservative interpretation is **pilot only**, with **$250 USD
total**, not per task, arm, scheduled slot or invocation. No stage-2 budget
or automatic continuation is inferred. The record supplies the separate
narrower-study financial decision and stopping-rule/negative/null-publication
acceptance. It does not alter M0's historical UNADJUDICATED disposition.

The [machine-readable receipt and assessment](../../bench/phase0-agent-native/registrations/ppw-rows-stage1/authorization-250/)
distinguish financial approval from operational admission. Sections 1–10
remain byte-for-byte historical records. In particular, the original
unfunded method/model/epoch files retain their hashes; their old funding
strings do not negate this subsequent approval.

The unchanged pilot requires **444 scheduled runs**. The historical
**$2.0278/run** planning reference multiplies to **$900.3432**. The approved
ceiling permits an all-in mean of at most **$250/444 = $0.563063…** per
scheduled slot, including all chargeable failed/interrupted/retried work.
This is an aggregate constraint, not a per-run truncation allocation.
**The historical estimate is not a cost lower bound or proof that the
redesigned pilot cannot cost less.**

No experimental call is admitted until #1378 provides trustworthy reviewed
enforcement/accounting, any execution-pin changes are coherently registered,
and a defensible plan can support the **full unchanged pilot** within $250.
Current evidence does not establish that plan. Nonsecret control-capability
evidence does not establish an authoritative hard bound; official documentation
describes local dollar figures as estimates, not authoritative billing. Subscription access
is not reinterpreted as zero study cost.

Only the project decision and necessary nonsecret capabilities/provenance are
retained. No account profile, raw authentication status, unrelated billing
records or credentials belong in this artifact. Shared global billing settings
must not be changed to force feasibility.

The present operational assessment is **BUDGET_NOT_RUN** because a defensible
complete-pilot plan within the bound has not been established. Reassess it
against #1378's actual reviewed enforcement contract; do not convert an
estimate into a claim of impossibility. N, model/client, task validity and
stopping rules cannot be weakened to fit the cap. Starving runs so they fail
is not a valid negative finding.

This is not a not-buildable result (the genuine Exit A remains), an observed
pilot null/negative, or the separately registered stage-2
**UNDERPOWERED-CARRIED** off-ramp. No stage-2 N/Δ or outcome is supplied.
#1267, #1262 and #1254 remain open until their actual requirements are met.
