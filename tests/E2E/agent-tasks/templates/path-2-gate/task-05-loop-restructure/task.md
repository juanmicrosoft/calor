# task-05: Restructure nested 3-level loop into a helper function

## Prompt to the agent

The function `SumGrid` in `setup/Grid.calr` contains a nested 3-level
loop computing the sum of all elements in a 3D virtual grid
`(x, y, z)` where each dimension is bounded by `size`. Extract the
innermost two loops (the `y`/`z` planes) into a helper function
`SumPlane(x: i32, size: i32) → i32` and have `SumGrid` call that
helper inside its single remaining outer loop.

The result of `SumGrid(size)` must be unchanged.

## Acceptance

- A new function `SumPlane` exists with signature
  `(x: i32, size: i32) → i32`.
- `SumGrid` contains exactly one `§L{...}` loop.
- `SumGrid` invokes `§C{SumPlane}` inside its loop body.

## Why this stresses the ID system

Structural restructure: tests that without structural IDs, the agent
can still navigate by name and position. The agent must understand
where a `§L` block ends to extract it.

## Multi-edit qualifier

1 new function + 1 body rewrite + 2 loop deletions = 4 sites in 1
file. Meets the "≥5 distinct locations within a file" relaxation only
loosely; consider this borderline if PR-0c finds it too easy.
