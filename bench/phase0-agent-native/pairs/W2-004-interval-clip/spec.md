# W2-004 — Interval Clipping (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

All arithmetic is 32-bit integer arithmetic.

## Data model

A set of intervals is given as two parallel integer arrays `starts` and
`ends` of equal length: interval `i` is the **half-open** range
`[starts[i], ends[i])` — it includes `starts[i]` and excludes `ends[i]`.

Every operation requires `starts` and `ends` to have the same length
(declared where the language can express it; callers must respect it).
Callers additionally guarantee the intervals are well-formed, sorted, and
non-overlapping: `starts[i] < ends[i]`, and `ends[i] <= starts[i+1]`
(consecutive intervals may touch exactly, sharing a boundary point).
Coordinates may be negative.

## Existing behavior (already implemented in the starting fixture)

`TotalCovered(starts, ends)` — the total number of integer points covered,
i.e. the sum of `ends[i] - starts[i]` over all intervals. `0` for no
intervals. The result is never negative (declared where expressible).

## Task: implement the missing operations

1. `ContainsPoint(starts, ends, x)` → boolean — whether `x` lies inside any
   interval. Remember the ranges are half-open: `x == starts[i]` is inside,
   `x == ends[i]` is not (unless it is the start of a later interval).
2. `ClipCovered(starts, ends, lo, hi)` → integer — the number of covered
   integer points that also lie in the half-open clip window `[lo, hi)`.
   Requires `lo <= hi` (declare where expressible). Intervals partially
   overlapping the window contribute only their overlapping part; intervals
   entirely outside contribute nothing. The result is never negative
   (declare where expressible).
3. `GapCount(starts, ends)` → integer — the number of **strictly positive**
   gaps between consecutive intervals: pairs `i, i+1` with
   `starts[i+1] - ends[i] > 0`. Touching intervals (shared boundary) form no
   gap. `0` for zero or one interval. The result is never negative (declare
   where expressible).

## Constraints

- Pure computation only: no I/O, no global state, no floating point.
- Do not change `TotalCovered`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `TotalCovered(int[], int[]) → int`,
`ContainsPoint(int[], int[], int) → bool`,
`ClipCovered(int[], int[], int, int) → int`, `GapCount(int[], int[]) → int`,
reachable through the arm's `TestShim.cs` (provided by the harness; not
editable by the agent).
