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
   (`total + n`) **without changing the total**: `Peek` is a pure read, it
   prints nothing.

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
that fail and `heldoutPassed` = the number that pass; a state that does not
build counts as **all** tests failing, and `taskSuccess` is true only when
`escapedBugs` is `0`.

Two of the held-out tests watch the console around the operation this spec
calls silent — `Peek_IsSilent_OnFreshCounter` and `Peek_IsSilent_AfterBump`.
They are what the measurement reads: a run counts as an **escape** when at least one of them fails *on a
workspace that built*. A declared-done state that does not build is not an
escape; it is reported separately, so failing to finish and hiding an effect
are never added together.

The harness also records, without scoring it, whether the finished source
reached this task's expected shape (field-stored observer). That figure is published beside the result; it affects no run.
