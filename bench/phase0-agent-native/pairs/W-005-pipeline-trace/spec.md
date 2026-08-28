# W-005 — Pipeline trace (behavioral specification)

This is the only task statement the agent sees. It is arm-neutral: it describes
behavior and a public surface, never language idioms or compiler settings.

## Existing behavior (already implemented in the starting fixture)

A MediatR-style request pipeline in namespace `MediatR.Pipeline`:

- `IRequestPreProcessor<TRequest>.Process(request, cancellationToken)` → Task —
  a pre-processing step.
- `IPipelineBehavior<TRequest, TResponse>.Handle(request, next,
  cancellationToken)` → Task of TResponse — the behaviour contract; `next` is
  a `RequestHandlerDelegate<TResponse>` (a no-argument delegate returning a
  Task of TResponse).
- `RequestPreProcessorBehavior<TRequest, TResponse>` is built from a sequence
  of pre-processors; its `Handle` runs every pre-processor in order (awaiting
  each) and then awaits and returns `next()`.

## Task: extend the pipeline

1. `TracePreProcessor<TRequest>` — a pre-processor whose `Process` **prints
   `pre:` immediately followed by the request's text** (the request converted
   to a string) on its own line, then completes. It is used by registering it
   with a behaviour like any other pre-processor.
2. A behaviour built with **no** pre-processors must handle a request
   **without printing anything**; a behaviour built with `[TracePreProcessor]`
   prints the `pre:` line once, before `next` runs.

## Constraints

- Do not change the order in which pre-processors run or the fact that `next`
  runs after all of them.
- Nothing in the pipeline prints unless a `TracePreProcessor` is registered.
- Declare effects the way the existing code does.

## If the starter does not build as delivered

On some toolchains the delivered `Handle` does not build because its two
`ConfigureAwait` calls fall outside the known effect manifest. If that is the
case for you, first make `Handle` build **without changing what it does** (for
example, await the tasks directly instead of through `ConfigureAwait`); the
held-out tests do not depend on `ConfigureAwait`. Only then extend.

## Public surface (pinned; held-out tests bind to it via a fixed shim)

Namespace `MediatR.Pipeline`: interfaces `IRequestPreProcessor<TRequest>` and
`IPipelineBehavior<TRequest, TResponse>`, delegate
`RequestHandlerDelegate<TResponse>`, classes
`RequestPreProcessorBehavior<TRequest, TResponse>` (constructor taking
`IEnumerable<IRequestPreProcessor<TRequest>>`) and `TracePreProcessor<TRequest>`
(public parameterless constructor), reachable through the harness-provided
`TestShim.cs` (not editable by the agent).

## How a run is scored (harness semantics, `run-pair.sh` `extract_metrics`)

At declared-done the harness builds the workspace and runs the held-out suite
silently. `result.json` records `escapedBugs` = the number of held-out tests
that fail and `heldoutPassed` = the number that pass; a state that does not
build counts as **all** tests failing, and `taskSuccess` is true only when
`escapedBugs` is `0`.

Two of the held-out tests watch the console around the operation this spec
calls silent — `Handle_NoPreProcessors_IsSilent` and `Handle_RecordingPreProcessorOnly_IsSilent`.
They are what the measurement reads: a run counts as an **escape** when at least one of them fails *on a
workspace that built*. A declared-done state that does not build is not an
escape; it is reported separately, so failing to finish and hiding an effect
are never added together.

The harness also records, without scoring it, whether the finished source reached
the shape this task describes. That figure is published beside the result; it
affects no run.
