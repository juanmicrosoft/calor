# W-006 — Map doubler (behavioral specification)

This is the only task statement the agent sees. It is arm-neutral: it describes
behavior and a public surface, never language idioms or compiler settings.

## Existing behavior (already implemented in the starting fixture)

- `Map(xs, f)` → integer array — applies the step `f` to every element of `xs`
  and returns a new array of the results (same length, same order).
- `Double(x)` → `x * 2`. `Announce(x)` → prints `x` on its own line, returns `x`.
- `UsePure(xs)` → `Map(xs, Double)`; prints nothing.
- `UseImpure(xs)` → `Map(xs, Announce)`; prints every element.

## Task: fix `Map`, then add a class

**Fix `Map` first.** As delivered its loop runs one step too far — the loop
bound is inclusive — so `Map` reads past the end of `xs` and throws on every
input, including the empty array. Make `Map` return an array of exactly `xs`'s
length with `f` applied once to each element, in order (the empty array maps
to the empty array). Keep it a `Map` over a supplied step; do not change its
signature.

Then add a class `Doubler` (public parameterless constructor). A `Doubler` owns its
*stage* — a stored integer-to-integer step it applies to arrays through `Map`
— and exposes two operations:

1. `Loud(xs)` → integer array — applies the stage once to every element,
   **printing each result on its own line as it is produced**, and returns the
   array: `Loud([1, 2])` prints `2` and `4` and returns `[2, 4]`.
2. `Twice(xs)` → integer array — applies the stage **twice over** (the result
   of the first pass is fed through the stage again, via `Map` both times) and
   returns the array: `Twice([1, 2])` returns `[4, 8]`. `Twice` is **silent**:
   it prints nothing, however many elements `xs` has.

The stage doubles: both operations are built on doubling steps.

## Constraints

- Apart from the bound fix, do not change the observable behavior of the
  existing module functions.
- `Loud` prints exactly one line per element, in order, and nothing else.
  `Twice` never prints.
- Declare effects the way the existing code does.

## If the starter does not build as delivered

It does. Build first, then extend.

## Public surface (pinned; held-out tests bind to it via a fixed shim)

Module functions `Map`, `Double`, `Announce`, `UsePure`, `UseImpure` as above;
class `Doubler` with a public parameterless constructor and instance methods
`Loud(int[]) → int[]`, `Twice(int[]) → int[]`, reachable through the
harness-provided `TestShim.cs` (not editable by the agent).

## How a run is scored (harness semantics, `run-pair.sh` `extract_metrics`)

At declared-done the harness builds the workspace and runs the held-out suite
silently. `result.json` records `escapedBugs` = the number of held-out tests
that fail (a non-compiling final state counts as **all** tests failing) and
`heldoutPassed` = the number that pass; `taskSuccess` is true only when
`escapedBugs` is `0`. The held-out tests that capture the console while
calling `Twice` and fail if anything was written are the effect-observing
tests for this pair.
