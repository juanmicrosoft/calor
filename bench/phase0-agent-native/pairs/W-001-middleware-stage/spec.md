# W-001 — Middleware stage (behavioral specification)

This is the only task statement the agent sees. It is arm-neutral: it describes
behavior and a public surface, never language idioms or compiler settings.

## Existing behavior (already implemented in the starting fixture)

- `RunTwice(g)` → integer — calls the no-argument step `g` twice and returns the
  sum of the two results.
- `IPipelineBehavior.Handle(request, next)` → integer — the pipeline contract.
- `RetryBehavior` implements it: `Handle(request, next)` returns `RunTwice(next)`.

## Task: extend the module

1. `Beat()` → integer — a module-level step that prints the word `beat` on its
   own line to the console and returns `1`.
2. `RetryBehavior.Probe()` → integer — runs the `Beat` step twice, through
   `RunTwice`, and returns the sum (`2`). Calling `Probe()` therefore prints
   `beat` twice.
3. `RetryBehavior.Twice()` → integer — returns the same total `Probe()` returns
   (`2`) but is **silent**: calling `Twice()` writes nothing to the console.
   Build it out of the same `RunTwice` machinery rather than hard-coding the
   number; the behaviour is expected to keep the step it runs as part of its
   state.

## Constraints

- Do not change `RunTwice`'s or `Handle`'s observable behavior.
- `Probe()` prints exactly two lines, `beat` and `beat`, and nothing else.
- `Twice()` prints nothing, every time it is called, on a freshly constructed
  `RetryBehavior` and on one whose `Probe()` has already been called.
- Declare effects the way the existing code does.

## If the starter does not build as delivered

It does. Build first, then extend.

## Public surface (pinned; held-out tests bind to it via a fixed shim)

Module function `Beat() → int` and `RunTwice(Func<int>) → int`; class
`RetryBehavior` with a public parameterless constructor and instance methods
`Handle(int, Func<int>) → int`, `Probe() → int`, `Twice() → int`. All reachable
through the harness-provided `TestShim.cs` (not editable by the agent).

## How a run is scored (harness semantics, `run-pair.sh` `extract_metrics`)

At declared-done the harness builds the workspace and runs the held-out suite
silently. `result.json` records `escapedBugs` = the number of held-out tests
that fail and `heldoutPassed` = the number that pass; a state that does not
build counts as **all** tests failing, and `taskSuccess` is true only when
`escapedBugs` is `0`.

Two of the held-out tests watch the console around the operation this spec
calls silent — `Twice_IsSilent_OnFreshBehavior` and `Twice_IsSilent_AfterProbe`.
They are what the measurement reads: a run counts as an **escape** when at least one of them fails *on a
workspace that built*. A declared-done state that does not build is not an
escape; it is reported separately, so failing to finish and hiding an effect
are never added together.

The harness also records, without scoring it, whether the finished source reached
the shape this task describes. That figure is published beside the result; it
affects no run.
