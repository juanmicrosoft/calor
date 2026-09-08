# Critical Review — Roadmap v0.20 "Earn the Language"

**Target:** `docs/plans/roadmap-v0.20.md` (as of commit `b63b50f6`, branch `plan/v0.20-safe-delegation`)
**Date:** 2026-09-08
**Reviewer:** Claude (adversarial read, one round; no independent human round has been run on this document)
**Stance:** The protocol discipline is the best in the repository's history. The plan is nevertheless
**not ready to approve as a roadmap**, because its own arithmetic already determines its outcome
(NOT FEASIBLE at M4), it depends on an external adopter the project has failed to secure across nine
releases, and its resource envelope is under-decomposed by roughly 1.5–2×. **Recommend: split it.**
Fund the trust-boundary / acceptance-service half now (cheap, in-house, high information); gate the
three-arm economic comparison on a one-page sizing calculation and a signed adopter, both done
*before* any of M2–M4 is scheduled.

---

## The one-line judgment

> This document knows exactly what an honest experiment would require, writes it all down, and then
> proposes to spend up to five of eight weeks and ~20 of 30 person-days discovering — at M4 — that
> the safety gate it fixed in §6.2 cannot be powered under the ceiling it fixed in §7. That
> discovery costs one page of arithmetic today. Do the arithmetic first, then decide what v0.20 is.

---

## What the plan gets right (and should keep, verbatim, in any revision)

These are not courtesy credits. Each is a specific improvement over `roadmap-v0.18.md` and
`agent-native-gates.md`, and each should survive whatever restructuring follows.

- **Arm B exists.** §1 and §5 refuse to let "checking rules helps" stand in for "a new language helps."
  This is the single largest methodological gap in every earlier Calor experiment (all were Calor vs
  ordinary C#), and closing it is the reason this roadmap is worth revising rather than discarding.
- **Authority separation with an isolated rebuild (§4.1).** The acceptance service rebuilding from
  approved inputs, hash-binding evidence to toolchain/deps/config, and keeping trusted checks out of
  the agent-controlled patch is the correct threat model for agent-driven experiments. Most published
  agent benchmarks get this wrong.
- **"Not established" is not "safe" (§4.3).** Preserving distinct machine-readable reasons under one
  honest label, and forbidding "the application is safe," is exactly right.
- **Escalation is a cost, not evidence (§4.3).** Human rescue counted at full price, and pre-review
  outcomes retained as secondary evidence, prevents the most common laundering route.
- **Power in both directions (§6.3).** Requiring ≥80% probability of the correct positive decision
  in the *workflow-only* scenario — the outcome the project does not want — is rare and admirable.
  "A design powered only for a Calor win is insufficient" should be quoted in every future roadmap.
- **Infinite cost for zero acceptances; undefined ≠ zero (§6.1).** Correct, and correctly refused as
  a "missing observation."
- **Precommitted outcome table with INVALID first (§8)** and "failure to pass is not equivalence."
- **No optional stopping, no patch-and-keep (§6.4).** The replacement-epoch rule is the right one.
- **Sunk-cost separation (§6.1).** Reporting prototype R&D apart from adoption economics, *and*
  reporting the total, closes the "cheap subsequent executions" trick.
- **§6.3 mandates an executable adjudicator.** This matches the discipline that made
  `bench/phase0-agent-native/ppw-analyze.py --dry-run` refuse to emit a verdict; keep it.

The remaining ~40% of the document is where the problems live.

---

## Findings

Severity: **Critical** = invalidates the plan as written or its budget; **Major** = a gate or
metric will not do what the text says; **Minor** = wording/consistency.

### C1 — The safety gate is already NOT FEASIBLE under the stated ceiling; the plan defers finding this out to week 5 (Critical)

§6.2 (line 322) requires the one-sided 95% upper bound on the Calor-minus-C# serious-defect
difference to be ≤ +1 pp, against *each* C# arm, on *both* denominators, with simultaneous
coverage across all registered quantities (§6.2, "Bounds are one-sided with simultaneous 95%
coverage"). §6.3 (line 371) then concedes this "can require hundreds or thousands of independent
observations." §7 caps non-maintainer time at 80 hours *total* for specification, comparator
review, task adjudication, and timed review.

The arithmetic nobody wrote down:

- **Best case (zero defects observed in every arm).** The rule of three gives a one-sided 95% upper
  bound of ≈ 3/n on the Calor rate; the difference bound is no tighter. 3/n ≤ 0.01 ⇒ **n ≥ 300
  paired tasks.** Simultaneous coverage across six safety quantities (2 comparators × 2
  denominators, plus the B-vs-A decision) pushes this toward 450–600.
- **Realistic case (≈3% defect rate in each arm, true difference 0).** Unpaired non-inferiority on a
  difference of proportions: n ≈ (1.645 + 0.84)² · 2·0.03·0.97 / 0.01² ≈ **3,600 tasks per arm.**
  Pairing helps only to the extent defects are correlated across arms, which is unknown.
- **What 80 hours buys.** Subtract specification (~10 h), comparator credibility review (~4 h),
  held-out suite authoring and severity adjudication (~15–20 h), and training three reviewers
  (~6 h). Roughly 40 h remain for timed review. At 20 min per slot (already thin for a business-rule
  change with evidence) that is ~120 slots = **~40 paired tasks across pilot *and* final** — and
  §6.3 requires the pilot to be disjoint. Confirmatory n ≈ 30.

**Gap: ≥ 8× in the most favorable case, > 100× in the realistic one.** Since §6.3 forbids widening
the margin after the pilot, and §6.2 pins it at +1 pp, the plan's own precedence rules land on
**NOT FEASIBLE at M4** with near certainty. The project has been here before at a smaller scale:
`2026-09-01-ppw-rows-dry-run.md` §4 found N = 9 needed for 80% power against a ceiling that
afforded N = 3, and armed UNDERPOWERED — *after* the dry-run spend. v0.20 proposes to repeat that
pattern with a 40× larger budget and external humans on the hook.

**Fix.** Move the sizing calculation from M4 to *before M1 approval* — a one-page desk exercise
with the base rates assumed in §6.3 — and present the adopter with a margin that is both
acceptable *and* measurable at the affordable n. If no such margin exists, say so in the roadmap
and restructure (see Recommendation R1). Also: with n ≈ 30, the safety comparison can only ever be
a *reported upper bound*, not a pass/fail non-inferiority test. Register it as such.

### C2 — The plan rests on an adopter the project has failed to find for nine releases, and crosses a governed one-way door without invoking the governance (Critical)

§3 requires a non-maintainer adopter with recurring refund-rule changes; §6.1 (line 294) requires
they expect ≥ 50 relevant requests in 12 months; §6.2's "Usable adoption" gate (line 324) requires
that adopter to install, integrate, change, and hand off *in their real refund path*. §5.2 further
requires ≥ 3 qualified non-maintainer reviewers, a non-implementing evaluator, a C#-experienced
non-maintainer to vet arm B, and (front matter) an independent reviewer for final interpretation.
That is **five to seven distinct external roles, recruited in Week 1.**

The record:

- `call-w-adjudication.md` (2026-08-04): **PP-A2 = "DEMAND UNPROVEN."** No named adopter; the
  adoption half was routed to maintenance-mode posture by pre-commitment.
- `agent-native-strategy.md:69`: "bus factor 1, no external users."
- `roadmap-v0.18.md` front matter: "**No independent round has been run**." Every adversarial
  review in `docs/plans/` to date has been self-conducted.
- `agent-native-strategy.md:73` and `loop-plan-v0.9.md:185`: a named external adopter is **Call 3**,
  "the maintainer's reserved one-way door," because it "creates obligations that outlive a program
  decision: migration paths, compatibility promises, maintenance expectations, reputational
  exposure to adopters if later killed."

v0.20 makes crossing Call 3 a Week-1 deliverable of M1 and never mentions Call 3, the obligations
it creates, or what the adopter is told about the ~three of four outcomes in §8 that end in
"maintenance mode" or "stop." Asking a real team to put a pre-1.0, bus-factor-1 language into a
refund decision path *as the substrate for an experiment that may conclude the language should be
abandoned* is a governance decision, not a milestone task.

**Fix.** (a) Route M1 explicitly through the Call 3 sign-off in `agent-native-strategy.md` §1.3.
(b) Pre-commit the adopter promise for the negative outcomes — the "eject to C# losslessly" feature
(`agent-native-strategy.md:69` mitigation (a)) is the natural one; name it and its test status.
(c) Sequence recruitment *after* the C1 sizing calculation so that a scarce first-adopter
relationship is not spent on a design that M4 would have declared infeasible anyway.

### C3 — The resource envelope does not survive its own decomposition (Critical)

§7: 30 engineering person-days over 8 weeks. Decomposing the plan's own deliverables:

| Item | Source | Realistic days |
|---|---|---|
| M1: domain, demand evidence, readiness matrix, pricing, authorizations | §7 | 3 |
| M2: arm B prototype (capped) | §7 line 400 | 5 |
| M2: arm C integration (capped) | §7 line 400 | 5 |
| M2: protected acceptance service — isolated rebuild, hash binding, file-surface enforcement, stale-evidence detection, bypass resistance | §4.1, §4.4 | **8–12** (uncapped, unbudgeted) |
| M2: mechanism suite — bad implementations, bypass probes, stale-proof reuse, discriminating regressions for #1183–#1185, #1189 | §4.4, §5.1 | 3–5 |
| M3: sampling frame, held-out suite with determinism validation, severity rubric, reviewer protocol | §5.1, §5.2 | 5 |
| M4: pilot execution, joint-power simulation with coverage checks for sparse/zero-event cells and reviewer clustering, freeze | §6.3 | 5 |
| M5: collection operations, replacement-execution adjudication | §5.2 | 4 |
| M6: reproducible report, crosswalk to existing gates, independent interpretation | §8, §9 | 3 |
| **Total** | | **41–47** |

Against 30. The largest single item — the acceptance service — is the one with no allowance at
all. It is also the item on which the trust-boundary gate (§6.2 row 4) depends, and that gate is
pass/fail: one confirmed false-established result voids the release. Unbudgeted work on a
pass/fail dependency is where schedules die.

Also: the 8-week clock has no start date. §7 says M1 "can begin after v0.19's release
disposition." `Directory.Build.props:3` reads `0.17.0`; v0.18's gate 15 is "armed and undischarged"
and its M3 sizing is still due (`roadmap-v0.18.md` front matter); **no `roadmap-v0.19.md` exists**;
epic #1182 carries 16 open issues and epic #1202 carries 15, all opened today. Weekly milestones
for a project that starts two unwritten releases from now are not a schedule; they are a shape.

**Fix.** Re-cost with the acceptance service and mechanism suite as first-class line items, or
narrow M2 (see R1). Replace "Week N" with "M1 + N weeks" and state the v0.19 exit criteria that
start the clock.

### M1 — The primary metric contains an n = 1 term that can decide the outcome and is outside the interval procedure (Major)

§6.1 line 283: `(adopter setup cost / 50 + mean operating cost per assigned slot) / fraction
correctly accepted`. Operating cost per slot is measured ~30 times per arm and gets a CI. Adopter
setup cost is measured **once** per arm. If Calor setup is 40 hours and C# setup is 2 hours at the
same rate, the amortized term is $80 vs $4 per change (at $100/h) — comparable to or larger than
the operating cost of a slot. The pass/fail ratio can therefore be moved by a single unreplicated
number with no uncertainty attached, and §6.3's joint error-control language never says how that
uncertainty propagates.

**Fix.** Either (a) treat setup as a preregistered fixed constant with a sensitivity band published
alongside the primary, and require the gate to pass at the *pessimistic* end of the band; or (b)
make operating cost per correct acceptance the inferential primary and report the amortized total
as the business-decision number with its own stated uncertainty. Do not leave it implicit.

### M2 — The metric mixes real hours and notional dollars at 1:1, so the "cost" result is really a review-time result wearing a cost costume (Major)

§6.1 converts human hours at "the same preapproved rate across arms" and adds API/compute dollars.
Two problems:

1. Token dollars here are list-rate accounting on a subscription
   (`2026-09-01-ppw-rows-dry-run.md:15-25` documents this precisely for the earlier epochs). Prior
   per-run cost was ~$2. Even at 10× that for a longer repair loop, tokens are ~$20/slot; twenty
   minutes of reviewer time at any professional rate is more. **Human time will dominate the
   numerator in every arm.** The primary outcome is then "which arm needs fewer reviewer minutes per
   correct acceptance," and the hourly rate is a free parameter that scales both arms equally —
   fine for the *ratio*, but it means the dollar framing adds nothing and the API cost adds only
   noise.
2. One rate for reviewers, evaluators, and maintainer engineering is wrong in both directions.

**Fix.** Make *human minutes per correctly accepted change* the inferential primary; report dollars
as a derived business number with the rate as an explicit, sensitivity-tested input. This is also
more honest about what the experiment can actually observe.

### M3 — Task supply and the amortization horizon are the same finite population, and the plan never reconciles them (Major)

§6.1 requires the adopter to expect ≥ 50 relevant requests in 12 months. §5.1 wants confirmatory
tasks with "real task provenance," a disjoint pilot pool (§6.3), held-out regression and property
cases, and §2 row 6 says "synthetic mechanism wins alone cannot pass." §7 collects everything in
Weeks 6–7.

A stream of ~1 request/week cannot supply ~30 confirmatory + ~8 pilot + held-out tasks in a
two-week window. In practice the pool will be "independently authored requests grounded in the
adopter's recorded work" (§5.1's escape hatch) — authored fixtures with provenance flavor. That is
closer to the synthetic work §2 says cannot pass than the text admits.

**Fix.** Preregister the expected mix — e.g., ≥ N replayed historical changes (which come with a
free oracle: the adopter's actual subsequent implementation and any incident record) plus ≤ M
authored variants — and put a floor on the real fraction. Report eligible-vs-excluded across the
whole 12-month frame, as §5.1 already requires.

### M4 — Arm B is the load-bearing control and gets five days from the party whose language is on trial (Major)

§5 defines B as "analyzers, verification tools, restricted patterns, and/or runtime enforcement"
plus the same authority separation as C, built in ≤ 5 engineering days (§7), vetted by one
C#-experienced non-maintainer. Arm C is a compiler with ~nine releases of investment. The plan's
mitigations (equal allowance, sunk-cost logged separately, "not ready" if B is not credible) are
the right instincts but leave the asymmetry intact: the primary metric is *operating* cost, which
rewards the polished arm regardless of how sunk cost is reported.

Two things make this worse. First, "credible" is a judgment call made by people with a stake in
the result. Second, a genuinely strong B is cheap and obvious — C# + property-based tests
(CsCheck/FsCheck) + runtime guards + the *shared* acceptance service — and if B is allowed the
same restricted subset as C (§5 says it is), B's prior of matching C on completion and safety is
high. Against a strong B, the ≤ 0.50 cost-ratio gate requires Calor to be ~2.5–3× cheaper at n ≈ 30
(the point estimate must sit well below 0.5 for the upper bound to clear it). Nothing in the
evidence base supports a prior that large: PP-W2 was never adjudicated, the real-scale ceiling
check was n = 6 (`call-w-adjudication.md`), and v0.18's effect-row claim is narrower and unfinished.

**Fix.** Preregister B's design *before* C's integration work starts, and have the non-maintainer
reviewer sign the design, not the result. State the plausible true effect size the maintainer is
betting on and show it in the §6.3 power scenarios. If the honest prior is "we would be surprised
by ≤ 0.50," say so — that is a reason to run a cheaper study first, not a reason to hide it.

### M5 — Model prior-knowledge asymmetry is the largest confound and is not named (Major)

§5.2 pins the model and §5 says "equal information does not require identical syntax." True, but
C# has enormous presence in every model's training data and Calor has effectively none. This is
the single largest determinant of agent iteration count and repair-loop cost, and it cuts against
Calor. A TARGET NOT MET outcome (§8) is therefore evidence about *today's models with today's Calor
exposure* — §8's last paragraph gestures at this but the TARGET NOT MET action ("Stop broad
safe-delegation feature expansion") does not carry the caveat.

**Fix.** Name it as a registered threat. Add a secondary measure that isolates it (e.g., iteration
count on syntax/compile errors vs. logic errors per arm, which the existing gates already
distinguish under "`.g.cs` dead-end rate" and wasted-iteration definitions in
`agent-native-gates.md` §2). Carry the caveat into the §8 action text.

### M6 — Trust-boundary entry criterion is too thin given what the audit found today (Major)

§4.4 carries forward regressions for #1183–#1185 and #1189 "plus any other findings touching the
chosen subset." Epic #1182 has 16 issues. Of the ones the plan does *not* name: #1187 (N1,
expression/pattern grouping lost in C# emission) and #1188 (N2, statements dropped from
expression-match block arms) are squarely inside the §3 subset ("bounded integer calculations,
branches"); #1186 (S4, inherited interface contracts not preserved) is a declared-guarantee defect.
The audit's headline is that "a 'Proven' contract verdict do[es] not yet reliably establish the
promised semantics" — i.e., the base rate of false-established results is currently nonzero, and
the trust-boundary gate is pass/fail on exactly that.

**Fix.** Make the M2 entry criterion explicit: every #1182 issue whose construct is in the M1
matrix has a discriminating regression that fails on the pre-fix compiler, *plus* a budgeted
adversarial "false-established hunt" over the whole supported matrix (mutation of accepted
programs; check that no mutation keeps a Proven verdict while changing held-out behavior). One
hit voids the release, so this is the cheapest insurance in the plan.

### M7 — No hardness/ceiling screen on the task pool; the completion gate can be blind (Major)

§5.1 says "do not make tasks harder merely because strong C# succeeds." `agent-native-gates.md`
§3.6 exists because the wave-1 dry run found both arms at ~100% success, a state where no
completion advantage is detectable at any n. v0.20 imports neither the ceiling check nor the
difficulty band (§3.1). These are not in tension: pin the hardness screen on arm A before freeze,
as the existing protocol does, and forbid post-freeze changes.

### M8 — Denominator wording is inconsistent between slot-level and task-level (Major, cheap)

§6.1 line 283 divides by "fraction of assigned slots correctly accepted" and line 286 says "use
equal task weights; average repeated runs within task before aggregation." The unit of analysis is
tasks; the formula says slots. §5.2's replacement-execution rule ("never add denominator entries")
is also written against slots. Pick one — tasks, with slot-level costs averaged within task — and
rewrite the formula and the replacement rule against it.

### M9 — Reviewer-training and specification amortization are unpinned levers (Major, cheap)

§6.1 amortizes "specification, integration, onboarding, and training" over 50; §5.2 says reviewer
training is "charged." Whether reviewer training goes into the /50 term or into per-slot operating
cost changes the primary by a large fraction, and it is not fixed until M4. Pin it in M1 with the
other thresholds.

### m1 — The plain-language question overstates what passing requires (Minor)

§1 asks for "half the total cost." With one-sided 95% upper bounds at n ≈ 30, the point estimate
must be ~0.3–0.4 to pass. Either say "at most half, with the upper bound, so in practice about a
third" or accept that the headline and the gate are describing different bets.

### m2 — Registration path and harness reuse are asserted, not named (Minor)

§9 proposes `bench/safe-delegation/v0.20/`; the append-only registry, epoch layout, `pins.json`,
and analyzers live under `bench/phase0-agent-native/`. §7 says "reuse what meets the protocol" but
names nothing. M2's PR is required to name integration points (§7) — good — but the roadmap should
already say which of `harness-capture.py`, `ppw-analyze.py`, the epoch layout, and the held-out
runner are reused vs replaced, because that is a week of the 30 days.

### m3 — Solver timeouts fail arm C slots by construction; make the pin explicit (Minor)

§4.3 correctly classes timeouts as "not established," and §5.2 correctly does not treat them as
infrastructure failures. Consequence: Z3 wall-clock is a direct cost to arm C and to no other arm.
That is fair, but the timeout value, hardware, and warm/cold cache policy for the *solver* should
be in the §5.2 pin list by name, not implied by "solver pins."

---

## Recommendations

**R1 — Split v0.20 into two gated halves.**

- **v0.20a: Trust boundary and protected acceptance (self-fundable, no external humans).** §4 in
  full, the mechanism suite, the false-established hunt (M6), the construct/property matrix, and a
  *documented case study* of one refund-style module on the maintainer's own substrate with
  honest, non-comparative reporting. Every deliverable here is pass/fail or descriptive, needs no
  power calculation, and is a prerequisite for any comparison. Information per dollar is highest
  here. It also directly answers §2 row 4 ("can we trust the acceptance boundary?") — the one
  question whose negative answer invalidates everything else.
- **v0.20b: The three-arm comparison**, entered only when (i) the one-page sizing calculation
  (C1) shows a gate set that is both acceptable to a named adopter and measurable at the
  affordable n, (ii) Call 3 has been crossed with the negative-outcome promise written (C2), and
  (iii) arm B's design is signed (M4). Until all three hold, v0.20b is a registered *intention*
  with a NOT FEASIBLE / DEMAND UNPROVEN label already attached, and it costs nothing.

**R2 — Do the sizing arithmetic before approving anything.** One page, using §6.3's own assumed
rates, for every §6.2 gate at n ∈ {20, 30, 50, 100}. Publish it in the roadmap. If the safety gate
cannot be a pass/fail test at any affordable n — and C1 says it cannot — register it as a reported
bound with a preregistered "would-have-failed-at" threshold, and let the economic and completion
gates carry the inference.

**R3 — Make human minutes per correct acceptance the primary; dollars derived (M2), with the setup
term handled by sensitivity band (M1).**

**R4 — Reorder M1 so the cheapest kills fire first:** sizing → B design sign-off → adopter search
(with Call 3 governance) → thresholds. Right now the order asks external people to commit before
the plan knows whether an achievable design exists.

**R5 — Re-cost §7 with the acceptance service and mechanism suite as line items (C3), and anchor
the clock to v0.19's exit criteria, which need writing.**

**R6 — Import from `agent-native-gates.md` rather than re-deriving:** the hardness ceiling (§3.6),
difficulty band (§3.1), determinism-evidence commit rule (§3.7), wasted-iteration definition (§2),
and the invalid-run config check (§0.2). Each is a paragraph the v0.20 protocol currently lacks.

---

## What I would say to the decision owner in one paragraph

You have written the experiment you would need in order to believe a Calor advantage. That is
genuinely valuable and should be kept as the reference design. But as a *v0.20 roadmap* it commits
30 person-days and a first-adopter relationship to a procedure whose most likely registered outcome
— NOT FEASIBLE at M4, on the safety margin, under the reviewer-hour ceiling — is computable now.
Compute it now. Then fund the half of the plan that does not depend on the answer (the trust
boundary and acceptance service, which the audit opened today shows is needed regardless), and
hold the comparison until an adopter, a signed arm-B design, and a measurable gate set exist.
Reaching "stop this bet" by a five-week detour is not more rigorous than reaching it by a one-page
calculation; it is just more expensive.
