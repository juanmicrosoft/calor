# Call W — the v0.11 → v0.12 Gate

**Decision: PROCEED to v0.12 with the measurement half returning to substrate
engineering. Shape: v0.12 leads with converter fidelity + checker breadth + a
value-asserted corpus (the real-scale levers) and the authored-contract overlay,
while the adoption push continues and the Call 3 door stays closed.**
Maintainer-approved 2026-08-04. This is the wedge plan §6 Call W, adjudicated on
PP-W2 + PP-A2, with PP-A1/PP-W5 as orthogonal v0.11.0 release gates.

## Inputs

**PP-W2 (the science weight) = NOT ADJUDICATED** — the four-valued input's
not-adjudicated branch (§6.2), registered at A-1.4 tranche 2. Real-scale cannot
measure verification's outcome value at v0.11 maturity: every thesis-testing
task source is supply-starved on the pinned corpus (see
`wedge-real-scale-closeout.md`). Per §6.1, this routes the measurement half back
to substrate engineering, finding published, door closed — exactly as
pre-committed. **The one positive:** the ceiling-recurrence check cleared (C# arm
escaped 16.7% at real scale) — the v0.10 ceiling does not survive real code, so
the venue has genuine headroom and the blocker is substrate, not a dead thesis.

**PP-A2 (the go-to-market weight) = PENDING** — the adoption *surface* shipped
(WS-W1/W2/W3: consumable SDK, honest conversion, effect soundness, `calor
import` / review-packet / eject / playbook), but the go-to-market *outcome* — a
named external adopter — is the **Call 3 crossing**, a one-way door reserved for
the maintainer. No adopter is secured, so PP-A2 does not yet resolve.

## Shape decision for v0.12

With PP-W2 not-adjudicated and PP-A2 pending, Call W decides *shape*, per the
Call 2 / Call G pattern — the program continues, and every branch keeps it
moving:

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

2. **Adoption half → continue; Call 3 stays closed.** The product surface is
   real and usable. The Call 3 crossing (a named adopter) remains the
   maintainer's reserved one-way door; until then, v0.12's adoption work is
   evidence-led demand-building, not a committed external dependency.

3. **v0.12 does NOT lead with an outcome-advantage claim.** PP-W2 produced no
   outcome-level verdict, so v0.12 marketing/positioning re-scopes to what is
   evidenced: verification's value is *earliness / attribution / cost* and (once
   the substrate clears) a testable catch-rate hypothesis — not a demonstrated
   catch-rate advantage. The honest headline for v0.11 is "the ceiling does not
   survive real code, and here are the three things that must move to measure
   what comes next."

## Remaining v0.11.0 release gates (orthogonal to this shape decision)

Call W decides v0.12 *shape*; it does not by itself ship v0.11.0. Per §6 the
release gates are **PP-W5** (strictness-parity: did the WS-W2 batch tax the loop
— a neutral-N1 v0.10.0-control vs v0.11-treatment epoch, gate frozen at A-1.4
tranche 1 / #839) and **PP-A1** (CI adoption gates). PP-W5 requires its own
spend-authorized epoch. This is a **separate maintainer decision** and is not
bundled into Call W. Options: run the PP-W5 parity epoch to ship v0.11.0 as a
released version, or fold the v0.11 work forward into v0.12 without a tagged
v0.11.0 release. Surfaced to the maintainer as the next gate.

## What v0.11 bought (the honest ledger)

A shipped, usable adoption surface; a proven, reusable real-scale benchmark
harness; the decisive, cheap ($74 of agent spend) finding that real-scale
verification-outcome measurement is not yet viable *and exactly why*; and the one
genuine positive that the v0.10 ceiling does not persist at real scale. The
program continues, better-aimed, into v0.12.
