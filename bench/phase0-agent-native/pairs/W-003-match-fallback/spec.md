# W-003 — Match fallback (behavioral specification)

This is the only task statement the agent sees. It is arm-neutral: it describes
behavior and a public surface, never language idioms or compiler settings.

## Existing behavior (already implemented in the starting fixture)

- `MatchOption(hasValue, value, onSome, onNone)` → integer — when `hasValue` is
  true returns `onSome(value)`, otherwise returns `onNone()`.
- `Identity(x)` → `x`. `Zero()` → `0`. `Shout(x)` → prints `x` on its own line
  and returns `x`.
- `BothPure(h, v)` → `MatchOption(h, v, Identity, Zero)`; prints nothing.
- `OneImpure(h, v)` → `MatchOption(h, v, Shout, Zero)`; prints `v` when `h`.

## Task: extend the module

1. `Fallback(h, v)` → integer — returns `v` when `h` is true; otherwise takes
   the fallback: **prints the word `fallback` on its own line** and returns
   `0`. Build it on `MatchOption` with a fallback step that does the printing,
   rather than with a fresh conditional.
2. `Sum2(h1, v1, h2, v2)` → integer — the sum of two optional values: each
   value counts when its flag is true and counts as `0` when it is false
   (`Sum2(true, 2, true, 3)` is `5`; `Sum2(true, 1, false, 9)` is `1`;
   `Sum2(false, 4, false, 4)` is `0`). `Sum2` is a **pure computation: it never
   prints**, whichever flags are false.

## Constraints

- Do not change the observable behavior of the existing functions.
- `Fallback` prints exactly one line, `fallback`, when the flag is false, and
  nothing when it is true. `Sum2` never prints.
- Declare effects the way the existing code does.

## If the starter does not build as delivered

It does. Build first, then extend.

## Public surface (pinned; held-out tests bind to it via a fixed shim)

Module functions `MatchOption(bool, int, Func<int,int>, Func<int>) → int`,
`BothPure(bool, int) → int`, `OneImpure(bool, int) → int`,
`Fallback(bool, int) → int`, `Sum2(bool, int, bool, int) → int`, reachable
through the harness-provided `TestShim.cs` (not editable by the agent).

## How a run is scored (harness semantics, `run-pair.sh` `extract_metrics`)

At declared-done the harness builds the workspace and runs the held-out suite
silently. `result.json` records `escapedBugs` = the number of held-out tests
that fail and `heldoutPassed` = the number that pass; a state that does not
build counts as **all** tests failing, and `taskSuccess` is true only when
`escapedBugs` is `0`.

Two of the held-out tests watch the console around the operation this spec
calls silent — `Sum2_IsSilent_OneAbsent` and `Sum2_IsSilent_BothAbsent`.
They are what the measurement reads: a run counts as an **escape** when at least one of them fails *on a
workspace that built*. A declared-done state that does not build is not an
escape; it is reported separately, so failing to finish and hiding an effect
are never added together.

The harness also records, without scoring it, whether the finished source
reached this task's expected shape (§B-bound printing step). That figure is published beside the result; it affects no run.
