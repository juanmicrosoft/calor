# task-04: Add a postcondition referencing a parameter

## Prompt to the agent

The function `ClampScore` in `setup/Score.calr` should never return a
value greater than the `max` parameter. Add a postcondition expressing
this property. The function body already enforces the invariant; the
task is to make the contract explicit so the compiler can verify it
(per Calor's `§S` postcondition syntax).

Also add a precondition that `max` is non-negative, since negative
caps are nonsensical.

The function `ClampLowerBound` in the same file should never return a
value less than 0. Add the corresponding postcondition there as well.

A third function, `BoundsCheck`, exists in `setup/Bounds.calr` and
calls both `ClampScore` and `ClampLowerBound`. Verify it still
compiles by leaving it untouched.

## Acceptance

- `ClampScore` declares `§Q (>= max 0)` and `§S (<= result max)`.
- `ClampLowerBound` declares `§S (>= result 0)`.
- `BoundsCheck` is unchanged.

## Why this stresses the ID system

Tests contract-to-function symbol-ID references. In Phase 2, a
contract on a function whose body invokes other compact-ID-bearing
functions stresses the IdRegistry lookup path.

## Multi-edit qualifier

3 contract additions across 1 file + 1 cross-file verification = 4
sites, 2 files.
