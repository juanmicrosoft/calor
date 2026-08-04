# Call W — the v0.11 → v0.12 Gate

**Decision: PROCEED to v0.12 with the measurement half returning to substrate
engineering. Shape: v0.12 leads with converter fidelity + checker breadth + a
value-asserted corpus (the real-scale levers) and the authored-contract overlay,
while the adoption push continues and the Call 3 door stays closed.**
Maintainer-approved 2026-08-04. This is the wedge plan §6 Call W, adjudicated on
PP-W2 + PP-A2, with PP-A1/PP-W5 as orthogonal release gates. **Inputs as
registered: PP-W2 = not adjudicated; PP-A2 = "demand unproven"** (its
pre-committed miss value — adoption half to maintenance-mode posture). The
v0.11.0 release itself is **folded forward into v0.12** — no v0.11.0 tag; see the
release section below.

## Inputs

**PP-W2 (the science weight) = NOT ADJUDICATED** — the four-valued input's
not-adjudicated branch (§6.2), registered at A-1.4 tranche 2. Real-scale cannot
measure verification's outcome value at v0.11 maturity: every thesis-testing
task source is supply-starved on the pinned corpus (see
`wedge-real-scale-closeout.md`). Per §6.1, this routes the measurement half back
to substrate engineering, finding published, door closed — exactly as
pre-committed. **The one positive:** the ceiling-recurrence check cleared (C# arm
escaped 16.7% at real scale — **dry-run signal, n = 6 tasks / 18 C#-arm runs, not
powered for it; an existence result, not a registered escape rate**) — the v0.10
ceiling does not survive real code, so
the venue has genuine headroom and the blocker is substrate, not a dead thesis.

**PP-A2 (the go-to-market weight) = "DEMAND UNPROVEN"** — the plan's
pre-committed miss wording (§4 PP-A2 row), registered at its pre-committed value
and not softened. The criterion was **a named adopter by Call W** (strategy §1.3
conditions: ≥1 non-maintainer reviewer, agreed in writing). Call W is here and no
adopter is secured, so PP-A2 resolves now — it is a Call W *input* (§6 line 167),
not a release gate, and it does not get to stay pending because the answer is
unwelcome. No adopter-deadline rebase is claimed.

The adoption *surface* did ship and is real (WS-W1/W2/W3: consumable SDK, honest
conversion, effect soundness, `calor import` / review-packet / eject / playbook).
What is unproven is *demand*, which produces no benchmark evidence either way.
Per the pre-committed disposition this routes the **adoption half to a
maintenance-mode posture** at v0.12 planning: keep the shipped surface working,
correct, and documented; do not fund new adoption *depth* ahead of demand. The
measurement half is unaffected. The **Call 3 crossing** (a named adopter) remains
the maintainer's reserved one-way door.

## Shape decision for v0.12

With PP-W2 not-adjudicated and PP-A2 at "demand unproven", Call W decides
*shape*, per the Call 2 / Call G pattern — the program continues, and every
branch keeps it moving:

1. **Measurement half → substrate engineering.** v0.12 leads with the three
   precisely-characterized levers, because they gate every future real-scale
   measurement:
   - **Converter fidelity** — raise the ~40–53% native fraction (the reverted /
     failed-conversion surface is the addressable engineering; #847 faithful
     local-function emission is one landed pointer).
   - **Checker breadth** — model plain reference null-deref (not only
     `Option.unwrap`), broaden index-OOB / div-by-zero to the shapes converted
     code actually produces.
   - **A value-asserted corpus** — add a numeric/collection/stateful library
     whose native surface is exercised by value-asserting tests (the current
     immutable-leaning corpus is not).
   - **Parallel track:** the authored-contract overlay (deterministic `§Q`/`§S`
     re-application per task) — the arm-ii proof-depth channel that does not
     exist.
   The benchmark machinery (oracle-hidden runner, three task strata, the
   Calor0410 addressability differential) is proven and banked; it re-runs the
   moment the substrate clears.

2. **Adoption half → maintenance-mode posture; Call 3 stays closed.** This is
   PP-A2's pre-committed routing, not a discretionary choice. The product surface
   is real and usable, so v0.12 keeps it working, correct, and documented — bug
   fixes, honest docs, CI gates — but funds **no new adoption depth** ahead of
   demand. Any v0.12 adoption effort is evidence-led demand-building, not a
   committed external dependency. The Call 3 crossing (a named adopter) remains
   the maintainer's reserved one-way door, and crossing it is what would take the
   adoption half back off maintenance-mode.

3. **v0.12 does NOT lead with an outcome-advantage claim.** PP-W2 produced no
   outcome-level verdict, so v0.12 marketing/positioning re-scopes to what is
   evidenced: verification's value is *earliness / attribution / cost* and (once
   the substrate clears) a testable catch-rate hypothesis — not a demonstrated
   catch-rate advantage. The honest headline for v0.11 is "the ceiling does not
   survive real code, and here are the three things that must move to measure
   what comes next."

## v0.11.0 release: FOLDED FORWARD (maintainer decision, 2026-08-04)

Call W decides v0.12 *shape*; it does not by itself ship v0.11.0. Per §6 the
release gates are **PP-W5** (strictness-parity: did the WS-W2 batch tax the loop
— a neutral-N1 v0.10.0-control vs v0.11-treatment epoch, gate frozen at A-1.4
tranche 1 / #839) and **PP-A1** (CI adoption gates). PP-W5 requires its own
spend-authorized epoch.

This was surfaced to the maintainer as a separate decision, and the decision is:
**no tagged v0.11.0 — the v0.11 work folds forward into v0.12.** Rationale: with
the measurement half concluding not-adjudicated, a v0.11.0 release would ship the
adoption half alone, and PP-A2 has just routed that half to maintenance-mode; a
release cycle plus a spend-authorized epoch buys no decision. **PP-W5 stays
frozen and unadjudicated** and is run once against the v0.12 substrate work, where
the strictness-parity question has a materially different treatment arm to be
measured against. **PP-A1 likewise carries forward** as a v0.12 release gate.

**Precise statement of the drift against frozen row A-1.4 tranche 1** (an earlier
draft said flatly that "#839 is untouched," which is true of the row's text and
misleading about its meaning). Three parts, stated separately:
- **Arm definition — no drift.** The row freezes `treatment = main at epoch time`.
  "v0.11 + v0.12 substrate" *is* main at epoch time, so the arm is consistent with
  the freeze by its letter and needs no supersession.
- **Schedule — drift.** The row says "by plan §3 the parity epoch runs at W5."
  There is no W5 in v0.12; the epoch is deferred past it.
- **Attribution scope — drift.** The row says it adjudicates the
  "**v0.11**-toolchain-vs-v0.10.0 release question," and its pre-committed on-fail
  isolation recipe (`v0.10.0 + WS-W2-only`) was built for a two-source attribution
  problem. A two-release treatment makes it three-source, and the recipe cannot
  separate v0.11 from v0.12 work.

None of this is corrected by silence. The v0.12 plan registers an **additive**
annex note (D-S5.5) restating the adjudicated question, extending the isolation
ladder to two steps, and recording that the 1.25 margin was calibrated for a
one-release delta and is not re-derived — so a fail becomes more likely and a pass
correspondingly stronger.

**On §6.2's "the adoption pair still adjudicates" clause.** The wedge plan's
not-adjudicated branch pre-commits that the adoption pair still adjudicates in
exactly this branch. **PP-A2 does adjudicate here** ("demand unproven", above).
**PP-A1 does not — it is deferred to v0.12, and that deferral is disclosed rather
than inherited.** The reading applied is that PP-A1's adjudication is bound to the
release it gates, and since the maintainer's decision above cancels the v0.11.0
release, PP-A1 moves with it. That is a narrower reading of "the adoption pair"
than the clause's plain text, so it is recorded as a **disclosed departure**, not
as compliance. PP-A1 requires no epoch — it is a CI gate — so it can be
adjudicated at any time before the v0.12.0 release, and nothing about this
deferral makes it easier to pass.

Consequences to hold: the shipped v0.11 adoption surface lives on `main`
untagged, so v0.12's release notes and CHANGELOG must cover the v0.11 range as
well; and the eventual PP-W5 epoch's treatment arm is now "v0.11 + v0.12
substrate", not "v0.11", which the epoch's arm definition must state explicitly
rather than inherit.

## What v0.11 bought (the honest ledger)

A shipped, usable adoption surface; a proven, reusable real-scale benchmark
harness; the decisive, cheap ($74 of agent spend) finding that real-scale
verification-outcome measurement is not yet viable *and exactly why*; and the one
genuine positive that the v0.10 ceiling does not persist at real scale. The
program continues, better-aimed, into v0.12.
