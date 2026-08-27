# W-004 — Counter peek (behavioral specification)

This is the only task statement the agent sees. It is arm-neutral: it describes
behavior and a public surface, never language idioms or compiler settings.

## Existing behavior (already implemented in the starting fixture)

`Counter` holds an *observer* — a step that receives an integer — and
`Bump(n)` hands `n` to the observer. As delivered the observer is never
installed, so the class is not yet usable; that is part of the task.

## Task: extend the class

1. Construction — a `Counter` built with its public parameterless constructor
   installs its observer: a step that **prints the value it receives on its own
   line**. The counter also starts a running total at `0`.
2. `Bump(n)` → nothing — adds `n` to the running total and hands the **new
   total** to the observer (so a fresh counter's `Bump(3)` prints `3`, and a
   following `Bump(4)` prints `7`).
3. `Peek(n)` → integer — returns the total that `Bump(n)` *would* produce
   (`total + n`) **without changing the total and without involving the
   observer**: `Peek` is a pure read, it prints nothing.

## Constraints

- `Bump` prints exactly one line per call and nothing else.
- `Peek` prints nothing, on a fresh counter and on one that has been bumped;
  after `Peek(n)` the total is what it was before.
- Declare effects the way the existing code does.

## If the starter does not build as delivered

It does. Build first, then extend.

## Public surface (pinned; held-out tests bind to it via a fixed shim)

Class `Counter` with a public parameterless constructor and instance methods
`Bump(int) → void`, `Peek(int) → int`, reachable through the harness-provided
`TestShim.cs` (not editable by the agent).

## How a run is scored (harness semantics, `run-pair.sh` `extract_metrics`)

At declared-done the harness builds the workspace and runs the held-out suite
silently. `result.json` records `escapedBugs` = the number of held-out tests
that fail (a non-compiling final state counts as **all** tests failing) and
`heldoutPassed` = the number that pass; `taskSuccess` is true only when
`escapedBugs` is `0`. The held-out tests that capture the console while
calling `Peek` and fail if anything was written are the effect-observing tests
for this pair.
