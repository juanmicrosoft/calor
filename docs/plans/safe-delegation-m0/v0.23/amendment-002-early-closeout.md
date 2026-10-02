# Safe delegation M0 v0.23 — Amendment 002: early close-out

**Recorded:** 2026-10-01. **Version:** 002. **Epic:** #1370. **Affects:**
#1373 (R2A′) and #1377 (R5); the trigger in Section 2 applies to every
amended gate. **Authority:** @juanmicrosoft under
[r0-authorization.md](r0-authorization.md) Sections 1 and 5 and
[amendment-001-public-proxy.md](amendment-001-public-proxy.md) Section 8;
effective upon maintainer merge. Before that merge this file is a proposal
and grants nothing. **Machine-readable state:** [gate-state.json](gate-state.json).

## 1. Decision recorded

On 2026-10-01 the maintainer decided to close the 0.23 inquiry before its
deadline (2026-10-29, R0 Section 5). No further substantive work is done
under R0. Only the R5 administrative closeout of R0 Section 10 remains, and
it is performed by the close-out record that introduces this amendment.

Amendment 001 has no rule for a gate that is still open, has no record
meeting its `MET` definition, and has review rounds left when the
maintainer ends the inquiry early. Its `UNAVAILABLE` triggers do not cover
that case. Without a rule, such a gate stays open, and amendment 001
Section 4 lets R5 run only "after every earlier gate is dispositioned". This
amendment closes that gap. It invents no other value.

## 2. Rule added

This rule extends the closure criteria of amendment 001 Section 5 for R1,
R2A′, R3, R2B′, and R4. It replaces none of them.

At an early close-out, each gate whose value is still open (`null`) and
that has no conforming record already cleared by the protocol closes as
follows:

1. If any prerequisite in the amended graph is not `MET`, it closes
   `NOT_REACHED` (unchanged #1370 propagation rule).
2. Otherwise, if no record meeting the gate's `MET` definition in amendment
   001 Section 5 has cleared [review-protocol.md](review-protocol.md), it
   closes `UNAVAILABLE` with the reason "no conforming record was obtained
   before the maintainer's early close-out on <date>". The record states the
   rounds used and the rounds left.

A gate that already holds a value, or that closes under one of amendment
001's own triggers, keeps that value; this rule does not apply to it.
The rule also does not cover an open gate whose conforming record has cleared
the protocol but is not yet merged. That gate keeps the ordinary rule: it is
`MET` only if the maintainer merges it (amendment 001 Section 5). On
2026-10-01 no open gate was in that state, so the case does not arise here.

`UNAVAILABLE` under item 2 is a process result. It says nothing about
whether a conforming record could have been obtained with the rounds and
time that were left.

## 3. Application on 2026-10-01

| Gate | Value before | Rule | Value after |
|---|---|---|---|
| R1 | open | Not this rule: amendment 001's own trigger applies (BLOCKING objections remain after 5 rounds) | `UNAVAILABLE` |
| R2A′ | open | Item 2: the submitted record selects and counts repositories, which amendment 001 excludes from R2A′; 2 of 5 rounds used, 3 left | `UNAVAILABLE` |
| R3, R2B′, R4 | open | Item 1 | `NOT_REACHED` |
| R5 | open | Not covered by Section 2; R5 follows amendment 001 Section 5 and R0 Section 10 (`UNAVAILABLE` or `EXPIRED`) | `UNAVAILABLE` |

## 4. Evidence affected

No gate used the R2A′ repository snapshot or any R1 rule as input. No
downstream supply estimate, sizing, or classification exists.

The R2A′ enumeration counts do exist: 1,387 candidates, 131 walked, 24
eligible, and per-criterion failure and activity counts
([r2a-prime-public-proxy.md](r2a-prime-public-proxy.md) Section 4 and
[r2a-prime-repos.json](r2a-prime-repos.json)). They are retained as
historical, non-binding material and are excluded from the evidence of every
0.23 gate. The R1 draft rules are retained on the same terms. The R5 record
states how a future milestone may reuse either.

## 5. What this amendment does not change

- No value under the `history` key of [gate-state.json](gate-state.json)
  (amendment 001 Section 8).
- No cap, boundary flag, prerequisite, process state, or classification.
- R0 stays `MET`. Under R0 Section 10, the inquiry is complete once R5 is
  dispositioned, and no active authority remains.
- The formal M0 status in [../decision.md](../decision.md) stays
  **UNADJUDICATED**.
