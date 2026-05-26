# task-01: Multi-function refactor

## Prompt to the agent

The file `setup/Math.calr` contains a single function `Calculate` that
performs three logically distinct operations: validate the input, scale
it, and compute a running total. Refactor it into three smaller
functions in the same module:

1. `ValidateInput(val: i32) → i32` — returns the validated input or
   throws if it is negative.
2. `ScaleInput(val: i32) → i32` — returns `val * 2`.
3. `RunningTotal(scaled: i32, prior: i32) → i32` — returns
   `scaled + prior`.

Then replace `Calculate`'s body so it calls the three helpers in order.
The signature and contracts of `Calculate` MUST NOT change. All four
functions live in the same module `MathOps`.

## Acceptance

`acceptance.sh` checks:
- `setup/Math.calr` contains four functions named `Calculate`,
  `ValidateInput`, `ScaleInput`, `RunningTotal`.
- `Calculate` body invokes all three helpers via `§C{...}`.
- The compiled C# round-trip is byte-identical to `expected/Math.calr`'s
  compiled output for input `(val=5, prior=0) → 10`.

## Why this stresses the ID system

Phase 1: the agent must locate function-end positionally (no structural
IDs). Phase 2: the three new function names need stable symbol IDs.
This is the classic refactor-extract case at module scope.

## Multi-edit qualifier

≥5 distinct edit locations: one body rewrite + three new function
declarations + module-boundary edits = ≥5 sites in one file.
