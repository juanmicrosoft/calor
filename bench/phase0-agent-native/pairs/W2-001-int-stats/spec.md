# W2-001 — Integer Statistics (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

All arithmetic is 32-bit integer arithmetic; division truncates toward zero.

## Existing behavior (already implemented in the starting fixture)

1. `Min(values)` — returns the smallest element of the integer array `values`.
2. `Max(values)` — returns the largest element of the integer array `values`.

Both require a non-empty array (callers must respect this; it is enforced
where your language can express it).

## Task: implement the missing operations

1. `Sum(values)` → integer — sum of all elements. **Edge case:** the sum of an
   empty array is `0`. (Callers keep totals small enough that no 32-bit
   overflow occurs; overflow behavior is outside the contract.)
2. `Mean(values)` → integer — arithmetic mean, i.e. `Sum(values)` divided by
   the number of elements using truncating integer division (toward zero;
   e.g. the mean of `-3` and `-4` is `-3`). **Requires a non-empty array** —
   declare this requirement where your language can express it; callers must
   respect it.
3. `Clamp(value, lo, hi)` → integer — `lo` if `value < lo`, `hi` if
   `value > hi`, otherwise `value`. **Requires `lo <= hi`** (declare where
   expressible). The result is guaranteed to lie in `[lo, hi]` — declare this
   guarantee where your language can express it.

## Constraints

- Pure computation only: no I/O, no global state, no floating point.
- Do not change `Min` or `Max`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `Min(int[]) → int`, `Max(int[]) → int`, `Sum(int[]) → int`,
`Mean(int[]) → int`, `Clamp(int, int, int) → int`, reachable through the
arm's `TestShim.cs` (provided by the harness; not editable by the agent).
