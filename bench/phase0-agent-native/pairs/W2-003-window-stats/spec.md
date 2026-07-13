# W2-003 — Sliding-Window Statistics (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

All arithmetic is 32-bit integer arithmetic. A *window* of length `count`
starting at `start` is the elements at indices `start .. start + count - 1`
(inclusive) of the integer array `values`. Callers keep sums small enough that
no 32-bit overflow occurs; overflow behavior is outside the contract.

## Existing behavior (already implemented in the starting fixture)

`WindowSum(values, start, count)` — sum of the window of length `count`
starting at `start`. Requires `start >= 0`, `count > 0`, and
`start + count <= length of values` (declared where the language can express
it; callers must respect it).

## Task: implement the missing operations

1. `WindowMin(values, start, count)` → integer — the smallest element in the
   window of length `count` starting at `start`. Same requirements as
   `WindowSum` (declare where expressible). Elements may be negative.
2. `MaxWindowSum(values, count)` → integer — the largest `WindowSum` over
   **all** windows of length `count` that fit in the array. Requires
   `count > 0` and `count <= length of values` (declare where expressible).
   Elements may be negative; when every element is negative the result is
   negative.
3. `CountAbove(values, count, threshold)` → integer — the number of windows
   of length `count` whose sum is **strictly greater** than `threshold`.
   Requires `count > 0` (declare where expressible). When `count` is larger
   than the array, there are no windows and the result is `0`. The result is
   never negative (declare where expressible).

## Constraints

- Pure computation only: no I/O, no global state, no floating point.
- Do not change `WindowSum`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `WindowSum(int[], int, int) → int`,
`WindowMin(int[], int, int) → int`, `MaxWindowSum(int[], int) → int`,
`CountAbove(int[], int, int) → int`, reachable through the arm's
`TestShim.cs` (provided by the harness; not editable by the agent).
