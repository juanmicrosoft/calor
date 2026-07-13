# W1-002 — Usage Billing (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

All amounts are integer cents. All arithmetic is 32-bit integer arithmetic;
every division truncates toward zero. No floating point anywhere.

## Usage normalization (shared rule)

Every billing function first normalizes its `units` argument:

- `units < 0` is treated as `0`;
- `units > 100000` is treated as `100000` (the metering ceiling).

Normalization happens **before any arithmetic** on `units`.

## Existing behavior (already implemented in the starting fixture)

`BillBasic(units)` — normalizes `units`, then returns `units * 7 / 10 + 500`
(7 cents per 10 units plus a 500-cent base fee). Guaranteed output range
`[500, 70500]` (declared where the language can express it).

## Task

1. **Refactor:** the normalization rule is currently written inline in
   `BillBasic` and would be duplicated by each new function below. Consolidate
   it into a single shared implementation used by every billing function. All
   of `BillBasic`'s observable behavior — including its declared output-range
   guarantee — must be preserved exactly.
2. **Add** the missing operations:
   1. `BillPro(units)` — normalizes `units`, then charges a 300-cent base fee
      plus tiered usage: the first 1000 units (or all of them, if fewer) cost
      9 cents per 10 units; units beyond the first 1000 cost 8 cents per 10
      units. **Each tier's charge is computed with its own truncating
      division, then the parts are summed.** Guaranteed output range
      `[300, 80400]` (declare where expressible).
   2. `BillCapped(units, cap)` — the Pro price for `units`, but never more
      than `cap`. **Requires `cap >= 0`** (declare where expressible).
      Guaranteed: result is in `[0, cap]` (declare where expressible).
   3. `CheaperPlan(units)` — returns `0` if the Basic price for `units` is
      less than or equal to the Pro price, otherwise `1`. Ties go to Basic.

## Constraints

- Pure computation only: no I/O, no global state, no floating point.
- Do not change `BillBasic`'s observable behavior.
- The normalization ceiling/floor must be applied before multiplying, so that
  extreme inputs (e.g. the maximum 32-bit integer) cannot overflow.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `BillBasic(int) → int`, `BillPro(int) → int`,
`BillCapped(int, int) → int`, `CheaperPlan(int) → int`, reachable through the
arm's `TestShim.cs` (provided by the harness; not editable by the agent).
