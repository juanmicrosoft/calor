# Critical Review, Round 2 — Roadmap v0.20 "Earn the Language", Draft v2

**Target:** `docs/plans/roadmap-v0.20.md`, Draft v2 (803 lines; working tree on
`plan/v0.20-safe-delegation`, 2026-09-08)
**Prior round:** `roadmap-v0.20-critique.md` (against `b63b50f6`, 473 lines)
**Reviewer:** Claude (adversarial read, second round; still no independent human round)
**Stance:** Draft v2 is a materially better document than Draft v1 and a *more honest* one:
it now states in its own §6.3 that "the full comparison is not justified for scheduling under
the proposed ceiling." Two of the three rejections in §11 are correct and I withdraw the
corresponding recommendations. The remaining problems are structural consequences of the
revision itself: M0 as written cannot produce the output the plan needs from it (a deadlock
between "size the unchanged gates" and "only M1 may change them"); the plan over-counts its own
multiplicity burden; the historical task frame caps n independently of money; and it is no
longer clear what software v0.20 ships. **Approve M0 only with the conditional-output form in R1
below; do not approve M1–M6 in this form.**

---

## The one-line judgment

> v1 hid the infeasibility at M4. v2 moves it to M0 and says it out loud — good — but then
> instructs M0 to size only the *unchanged* gates and forbids M0 from proposing a margin, so the
> desk screen's only legal output is "stop." A screen that can only say no is a decision already
> taken. Either let M0 map the margin/capacity frontier, or skip M0 and record NOT FEASIBLE today.

---

## 1. Disposition of round-1 items — where I agree, where I push back

### Accepted rejections (I withdraw these)

**R2 — "demote safety to a reported bound."** §11 rejects this: a descriptive safety result
cannot support an *adoption* pass for something called *safe* delegation. That is right, and my
round-1 wording ("let the economic and completion gates carry the inference") would have licensed
exactly the claim-without-evidence the plan exists to prevent. Withdrawn. The consequence is
handled below (§2, S1): if safety cannot be bounded, the honest outcome is NOT FEASIBLE, and the
plan should say what it does *then*.

**M7 / R6 — import the hardness ceiling.** §11 rejects a C#-failure quota for a cost-primary
study, and §5.1 now adds the correct replacement: "Equal 100% completion does not prevent a
meaningful cost difference." That is true — `agent-native-gates.md` §3.6 exists because its
primary was *escaped bugs*, which is blind at a 100% ceiling; a cost primary is not. Withdrawn.
The adopted parts (determinism evidence, configuration canaries, wasted-iteration
instrumentation) are the right subset.

**R1 — fund the trust-boundary half automatically.** §11 rejects automatic funding and routes it
through a separately scoped exploratory proposal (§7.1). The stated worry — "a back door to
another release of mechanisms without value evidence" — is the correct worry for this repository,
whose history is release after release of mechanism. I accept the rejection with one residual
(§2, S6): the false-established search in §4.4 is *audit*, not mechanism, and if M0 stops it must
be routed somewhere explicit (v0.19 or the exploratory option), not dropped.

### Partial acceptances I consider adequate

**M2 / R3 — dollars vs. human minutes.** v2 keeps total economic cost as primary, adds human
minutes as a named secondary, separates cash / list-rate / subscription / hours, uses
role-specific rates, requires a preregistered rate range, and requires the economic pass to hold
at the *least favorable* registered setup/rate combination (§6.1). That is stronger than what I
asked for. Adequate. One residual (S5): the band widths are now a lever with a conservative
direction, and the text does not say who sets them.

**C2 — adoption governance.** §3's new "Research participation is not production adoption"
subsection, the research-agreement contents, the Call 3 disposition requirement, and the
separately-approved production path are all correct. §11's pushback — "historical lack of an
adopter is not evidence about current recruitment without an update" — is fair: absence of a
result is not evidence of a failed search if no search was run. I withdraw the "failed for nine
releases" framing. What remains (S4): the plan should then state whether any recruitment has
been attempted since Call W, because M1's "missing demand stops" is uninformative otherwise.

### Accepted items, verified

C1 (M0 sequencing), C3 (decomposition exposed; worksheet required; week numbers removed), M1
(setup bands, pessimistic-combination pass), M3 (75% historical floor, cluster counting,
12-month frame), M4 (design-first B sign-off, stated hypothesized effect), M5 (familiarity as
registered limitation, syntax-vs-logic repair classification), M6 (matrix-wide regression rule
incl. #1187/#1188, false-established search with the *corrected* oracle — violated claimed
property while established, not merely changed behavior; that correction is better than what I
wrote), M8 (task-cluster formula), M9 (cost dictionary), m1–m3. I checked the §6.3 arithmetic:
`1 − 0.05^(1/n)` and the six-way column are correct to the stated precision, 299 and 477 are the
right crossover points, and the 3,600-per-arm figure reproduces. The §7.4 reuse table names files
that exist (`harness-capture.py`, `run-pair.sh`, `loop-telemetry-schema.md`, the held-out runner
inside `run-pair.sh`).

---

## 2. New findings against Draft v2

Severity as before: **Critical** invalidates the plan as written; **Major** a gate or milestone
will not do what the text says; **Minor** wording.

### S1 — M0 is deadlocked: it must size the *unchanged* gates, may not propose a margin, and the unchanged safety gate is already shown infeasible in the same section (Critical)

§6 (revised): "M0 first sizes these thresholds as written. M1 may approve them or propose a
separately reviewed change on business grounds." §7.3: M1 starts only after "M0 credible."
§6.3: the +1 pp safety gate needs ≥ 299 independent tasks with zero events (≥ 477 under the
six-way illustration), against a capacity of 40–80 triplets; and §5.1 now caps n by the
adopter's 12-month history (S2 below).

So under the rules as written:

1. M0 may only size the gates as written.
2. The gates as written cannot be met at any capacity the ceiling affords — the plan's own table
   shows this.
3. Therefore M0's only permitted disposition is "no credible route" → stop before M1.
4. The one mechanism that could change the outcome (a business-grounds margin change) lives in
   M1, which never starts.

This is not a hypothetical; it is the plan's arithmetic applied to the plan's control flow.
§11's next-round question 1 ("does M0 have a credible, inexpensive way to rule out the full
comparison?") has the answer *yes — it already has, in §6.3* — and the interesting question is
the one the plan does not let M0 answer: **what margin and what capacity would make the
comparison feasible, and is any such point one an adopter could accept?**

**Fix (R1).** Give M0 a conditional-output form. For each candidate safety margin
δ ∈ {1, 2, 5, 10} pp and each capacity in triplets {40, 80, 150, 300}, M0 reports which of the
five §6.2 gates are decidable under conservative scenarios. The memo's disposition is then one
of: (a) NOT FEASIBLE at every δ the decision owner would defend on business grounds; (b)
FEASIBLE ONLY AT δ ≥ X and capacity ≥ Y, referred to M1 for a *separately reviewed* margin
decision. Keep the rule that M0 cannot *approve* a margin change; let it *map* one. Without this,
run no M0 and record NOT FEASIBLE today — that costs zero person-days and is equally honest.

### S2 — The historical task frame caps n at roughly the adopter's annual request count, independent of budget; the plan should say so (Major)

§5.1: ≥ 75% of primary task weight on distinct replayed historical requests; the preceding
12-month history is the frame; "if that frame cannot supply the powered design, stop rather than
fill it with synthetic variants." §6.1: the adopter must expect ≥ 50 requests in 12 months.

An adopter meeting the §6.1 demand bar therefore supplies on the order of 50 historical requests,
before eligibility screening. With the 75% floor, n_max ≈ 50 / 0.75 ≈ 67 task clusters — pilot
and final combined, since §5.1 requires disjoint clusters. At n ≈ 50–60 final, the zero-event
single-rate upper bound is ~5–6% (§6.3 table), five times the margin. **No amount of reviewer
hours or compute changes this.** Money can buy capacity; it cannot buy a year of history.

This is the strongest argument in the plan for NOT FEASIBLE at 1 pp and it is currently implicit,
spread across three sections. Put it in §6.3 as a third illustration, next to the capacity one,
and have M0's memo carry a "task-supply ceiling" row.

### S3 — The plan over-counts its multiplicity burden; the six-way column is the wrong illustration for an all-gates-must-pass decision (Major, but it *helps* the plan)

§6.2: "Bounds are one-sided with simultaneous 95% coverage across the registered gate
quantities." §6.3 then illustrates with a six-way Bonferroni column reaching 1% at 477.

For a decision that requires *every* gate to pass (§6.2, §6.3 "each positive decision requires
its full conjunction"), the relevant construction is an intersection-union test: each component
tested at level α yields an overall level-α test, with no per-quantity adjustment (Berger 1982).
The maximum type-I error is attained with one component at its null boundary and the rest deep
in the alternative — and is still ≤ α. Simultaneous coverage across all gate quantities is
therefore *more conservative than the plan's own error target requires*, and the 477 figure
overstates the safety burden; 299 is the right zero-event crossover for one conjunction.

Where multiplicity *does* bite is across the **positive decision branches**: LANGUAGE EARNS
CONTINUATION and WORKFLOW VALUE are two distinct positive claims, neither excluded under the
null, so "any false positive investment claim ≤ 5%" (§6.3) needs adjustment across those two
routes (α/2 each, or a hierarchical procedure that uses §8's precedence), not across the
fourteen-odd registered quantities. The registered count is also not six: two comparators × two
denominators for safety, three completion quantities, two economic, plus the B-vs-A set.

**Fix.** Rewrite the coverage sentence in §6.2 as: per-gate one-sided 95% bounds, conjunction
per branch (IUT), with error control across the two positive branches. Replace the six-way
column with a two-way one (`1 − 0.025^(1/n)`, which reaches 1% at 368). This does not rescue
feasibility — 299–368 triplets is still 4–5× capacity and 5–7× task supply — but M0 should carry
the right number.

### S4 — M0 can still be a self-review with a foregone conclusion; make the stop rule mechanical (Major)

M0 is a maintainer desk exercise deciding whether to stop the maintainer's own flagship
experiment. In this repository the historical bias runs toward over-stringency rather than
optimism, but the safeguard should not depend on that. Two cheap fixes:

- Preregister M0's stop rule as arithmetic, not judgment: e.g., "if the smallest n at which every
  §6.2 gate is decidable under the *most favorable registered* scenario exceeds min(capacity,
  task-supply), disposition is NOT FEASIBLE." Then the memo computes; it does not decide.
- Have the independent reviewer named in the front matter counter-sign the M0 memo. Two days of
  maintainer work plus an hour of review is cheap insurance on the one decision that gates
  everything else.

Also (from C2's residual): the memo should state whether any adopter search has been run since
Call W (2026-08-04) and its funnel, so that "DEMAND UNPROVEN" at M1 means *searched and not
found*, not *never asked*.

### S5 — Setup/rate bands are a conservative-direction lever with no named owner (Major, cheap)

§6.1: bands are frozen "from documented estimates before observing comparative pilot outcomes,"
the pass must hold at the least favorable combination, and out-of-band setup blocks a pass. Good.
But narrow bands make passing easier and the text does not say who sets the width or what the
floor is. Require the bands to be proposed by the maintainer and *approved* by the independent
reviewer (or the adopter's engineering lead), with a minimum width tied to the documented
estimate's own uncertainty (e.g., ±50% of the point estimate unless a tighter band is justified in
writing). Same for the cost-dictionary classification (§6.1 table): the maintainer should not be
the sole classifier of what counts as "adopter training in normal use" vs "research-only
orientation."

### S6 — If M0 stops, the false-established search has no home (Major)

§4.4 budgets "a separate search for false-established results over the complete supported
matrix." That work has value independent of any comparison: the compiler *currently publishes*
Proven verdicts to users, today's audit found four soundness findings (#1183–#1186), and
website issue #1213 (W11) asks the site to "qualify safety guarantees." If M0 stops, §7.1's
exploratory option *may* pick this up ("a mechanism probe can find false guarantees") but nothing
requires it. State explicitly: on an M0 stop, the matrix-wide false-established search is either
(a) transferred to epic #1182 under v0.19 as a T-series finding, or (b) the first candidate for
the §7.1 exploratory proposal. It should not be the thing that quietly disappears with the
experiment.

### S7 — What does v0.20 ship? (Major)

v1 had a shape: an experiment and a decision. v2's most likely path — by its own §6.3
disposition and S1–S2 above — is M0 → NOT FEASIBLE → possibly an exploratory proposal. That is a
two-day memo and a decision record. Versions are for shipped software; a release whose content is
a stop memo either (a) should not occupy a version number (record the decision in
`docs/plans/` and let v0.20 be whatever code ships next), or (b) needs the roadmap to say what
code ships alongside — e.g., the exploratory artifact, or nothing beyond v0.19 carry-over. The
DoD (§10) permits "a documented stop," which is right; it does not say what is *released*. Pick
(a) or (b).

### S8 — The exploratory option's "friction study" inherits the self-review problem (Minor)

§7.1 allows "a friction study [that] can estimate human minutes and repair costs" on "an in-house
example." If the human is the maintainer, the minutes are not an estimate an adopter can use, and
they will be quoted later regardless of the EXPLORATORY ONLY label. Require ≥ 1 non-maintainer
participant for any human-time figure the exploration reports, or label maintainer-only figures
as "author time," never "review time."

### S9 — Arm B sketch: one missing component (Minor)

§5's M0 sketch of B — property-based testing, runtime guards, shared acceptance service — is what
an informed .NET team would use (next-round question 3: yes). Add the fourth thing such a team
would reach for: a banned-API / Roslyn analyzer confining the decision module to the same
restricted subset arm C is held to (no I/O, no host mutation, no non-deterministic calls). It is
cheap, it is what "restricted patterns" means in practice, and without it B's effect story is
runtime-only while C's is static — which is the difference being measured, so it should be
present in B's *design* rather than discovered as an omission in the credibility review.

### S10 — Wording (Minor)

- §1 line 22–24 now correctly says "half" means an upper bound ≤ 0.50. Good. Consider adding the
  one-sentence consequence: "at the sample sizes the ceiling affords, this requires a point
  estimate well below 0.50," so a reader does not need §6.3 to understand §1.
- §6.3 "Six-way Bonferroni illustration" — rename per S3 or drop.
- §11's disposition table is good practice and should become the convention for every roadmap
  revision in `docs/plans/`.

---

## 3. Answers to §11's next-round questions

1. **Does M0 have a credible, inexpensive way to rule out the full comparison?** Yes — §6.3 has
   already done it for the unchanged gates, and Newcombe's paired method 10 (or Tango's score
   interval) gives closed-form zero-discordant intervals, so no simulation is needed to confirm.
   The problem is the opposite one: M0 is not allowed to do anything *but* rule it out (S1).
2. **Is any route to the unchanged joint gates compatible with supply and capacity?** No.
   Capacity gives 40–80 triplets; task supply gives ~50–67 clusters (S2); the 1 pp gate needs
   ≥ 299 under the most favorable assumption and the correct multiplicity count (S3). Not close.
3. **Does B reflect what an informed adopter would use?** Yes, with the analyzer addition (S9).
4. **Can any allocation, band, weighting, or branch manufacture a win?** The remaining lever is
   band width (S5). Cost-dictionary movement is blocked after outcomes (§6.1); variant weighting
   is frozen before collection (§5.1, §6.1); branch precedence is fixed (§8). With S5 closed, I
   find no route.
5. **Are research consent, negative-outcome support, and Call 3 boundaries explicit enough?** Yes
   for the adopter. Add the S4 requirement that "demand unproven" means "searched."
6. **Is the exploratory option bounded?** Yes on claims and gates. Add S8 (non-maintainer time)
   and S6 (an explicit home for the false-established search).

---

## 4. Recommendations, ranked

**R1 (blocking).** Reform M0's mandate to the conditional-output form in S1: a margin × capacity
× task-supply frontier, with a mechanical stop rule (S4) and independent counter-signature. If
the decision owner would not defend any margin above 1 pp on business grounds, skip M0 and record
NOT FEASIBLE now — same honesty, zero cost.

**R2.** Correct the multiplicity construction (S3): per-gate IUT, error control across the two
positive branches. Replace the six-way column.

**R3.** Add the task-supply ceiling to §6.3 as a first-class illustration (S2).

**R4.** Name owners and floors for the setup/rate bands and the cost dictionary (S5).

**R5.** Decide what v0.20 releases (S7), and where the false-established search lives if the
comparison stops (S6).

**R6.** Minor: S8, S9, S10.

---

## 5. What I would say to the decision owner in one paragraph

Draft v2 did the hard thing: it wrote the infeasibility into the plan instead of discovering it
in week five. Now finish the thought. As written, M0 is a two-day exercise whose only legal
answer is the one §6.3 already gives, because M0 may not touch the margin and M1 — the only
place the margin can move — never opens. Either let M0 draw the margin/capacity/supply frontier
so that M1 can have an honest business conversation about what "safe enough" costs to prove, or
record NOT FEASIBLE today and spend the two days on the one piece of §4.4 that is worth doing
regardless of the comparison: finding out whether the compiler's current Proven verdicts are
true. The rest of the document is ready to be the reference design it has become.
