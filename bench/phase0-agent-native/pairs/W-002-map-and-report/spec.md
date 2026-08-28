# W-002 — Map and report (behavioral specification)

This is the only task statement the agent sees. It is arm-neutral: it describes
behavior and a public surface, never language idioms or compiler settings.

## Existing behavior (already implemented in the starting fixture)

- `Map(xs, f)` → integer array — applies the step `f` to every element of `xs`
  and returns a new array of the results (same length, same order).
- `Double(x)` → integer — `x * 2`.
- `Announce(x)` → integer — prints `x` on its own line and returns `x`.
- `UsePure(xs)` → integer array — `Map(xs, Double)`; prints nothing.
- `UseImpure(xs)` → integer array — `Map(xs, Announce)`; prints every element.

## Task: fix `Map`, then extend the module

0. **Fix `Map` first.** As delivered its loop runs one step too far — the loop
   bound is inclusive — so `Map` reads past the end of `xs` and throws on every
   input, including the empty array. Make `Map` return an array of exactly
   `xs`'s length with `f` applied once to each element, in order (the empty
   array maps to the empty array). Keep it a `Map` over a supplied step; do not
   change its signature.
1. `MapAndReport(xs)` → integer array — doubles every element and returns the
   doubled array, **printing each doubled value on its own line as it is
   produced** (so `MapAndReport([1, 2, 3])` prints `2`, `4`, `6` and returns
   `[2, 4, 6]`). Go through `Map` with a doubling-and-printing step rather than
   writing a second loop.
2. `Total(xs)` → integer — the sum of the doubled elements of `xs`
   (`Total([1, 2, 3])` is `12`; the empty array totals `0`). `Total` is a
   **pure computation: it prints nothing.**

## Constraints

- Apart from the bound fix, do not change the observable behavior of `Map`,
  `Double`, `Announce`, `UsePure`, or `UseImpure`.
- `MapAndReport` prints exactly one line per element, in order, and nothing
  else. `Total` never prints.
- Declare effects the way the existing code does.

## If the starter does not build as delivered

It does. Build first, then extend.

## Public surface (pinned; held-out tests bind to it via a fixed shim)

Module functions `Map(int[], Func<int,int>) → int[]`, `Double(int) → int`,
`Announce(int) → int`, `UsePure(int[]) → int[]`, `UseImpure(int[]) → int[]`,
`MapAndReport(int[]) → int[]`, `Total(int[]) → int`, reachable through the
harness-provided `TestShim.cs` (not editable by the agent).

## How a run is scored (harness semantics, `run-pair.sh` `extract_metrics`)

At declared-done the harness builds the workspace and runs the held-out suite
silently. `result.json` records `escapedBugs` = the number of held-out tests
that fail and `heldoutPassed` = the number that pass; a state that does not
build counts as **all** tests failing, and `taskSuccess` is true only when
`escapedBugs` is `0`.

Two of the held-out tests watch the console around the operation this spec
calls silent — `Total_IsSilent` and `Total_IsSilent_SingleElement`.
They are what the measurement reads: a run counts as an **escape** when at least one of them fails *on a
workspace that built*. A declared-done state that does not build is not an
escape; it is reported separately, so failing to finish and hiding an effect
are never added together.

The harness also records, without scoring it, whether the finished source
reached this task's expected shape (§B-bound printing step). That figure is published beside the result; it affects no run.
