# Safe delegation M0 v0.23 — R3, R2B′, and R4: `NOT_REACHED`

**Recorded:** 2026-10-01. **Issues:** #1374 (R3), #1375 (R2B′), #1376 (R4).
**Epic:** #1370. **Authority:** @juanmicrosoft, close-out decision of
2026-10-01, under [r0-authorization.md](r0-authorization.md) Section 10 and
[amendment-001-public-proxy.md](amendment-001-public-proxy.md) Section 4.
**Effective:** on the maintainer's merge of the 0.23 close-out PR.
**Machine-readable state:** [gate-state.json](gate-state.json).

## 1. Values

| Gate | Issue | Prerequisites (amendment 001 Section 4) | Prerequisite values | Value |
|---|---|---|---|---|
| R3 | #1374 | R1, R2A′ | R1 `UNAVAILABLE`, R2A′ `UNAVAILABLE` | `NOT_REACHED` |
| R2B′ | #1375 | R1, R2A′, R3 | R1 `UNAVAILABLE`, R2A′ `UNAVAILABLE`, R3 `NOT_REACHED` | `NOT_REACHED` |
| R4 | #1376 | R1, R2A′, R3, R2B′ | R1 `UNAVAILABLE`, R2A′ `UNAVAILABLE`, R3 `NOT_REACHED`, R2B′ `NOT_REACHED` | `NOT_REACHED` |

Terminal records of the blocking gates:

- R1: [r1/clearance-record.md](r1/clearance-record.md), "Maintainer decision
  and terminal value". Round 5 of 5 ended with 2 unresolved BLOCKING
  objections.
- R2A′: [r2a-prime-public-proxy.md](r2a-prime-public-proxy.md) Section 2.2.
  No record conforming to the amended R2A′ definition cleared the protocol.

## 2. Rule applied

The #1370 gate-propagation amendment says only `MET` unlocks a dependent
gate. If a prerequisite is not `MET`, the dependent gate does no
substantive work and closes `NOT_REACHED`, naming every blocking issue and
its terminal value. Amendment 001 Section 4 keeps that rule for the amended
graph. The #1374, #1375, and #1376 sequencing amendments each say the same
for their own prerequisites.

`NOT_REACHED` is an administrative sequencing state. It is not scientific
evidence that a paper boundary cannot be defined, that public task supply is
absent, or that a study cannot be sized.

## 3. Work performed

None. These gates never started:

| Gate | Not produced |
|---|---|
| R3 | No three-arm paper boundary, parity standard, support intersection, or comparator definition |
| R2B′ | No repository draw, supply inspection, eligibility count, or capacity estimate. The R2A′ repository snapshot was not used as input |
| R4 | No estimator, sizing run, or prospective study envelope |

No data was accessed under these gates. They have no local caches and no
partial evidence to retain or delete. No review rounds ran and no spend was
incurred under them ([spend-ledger.md](spend-ledger.md)).

## 4. Original-gate history

The original-definition values recorded by amendment 001 Section 2 are
unchanged: R3-original, R2B-original, and R4-original stay `NOT_REACHED`.

## 5. Reopening

These values are terminal for 0.23. Restarting any of these gates needs a
new, versioned authorization in a later milestone, after its own
prerequisites are `MET`. This record reopens nothing.
