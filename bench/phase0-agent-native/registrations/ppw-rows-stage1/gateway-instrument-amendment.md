# Prospective request-gateway amendment (#1406)

This amendment changes execution instrumentation, not the frozen task set,
model/client, estimands, precision calculation or scientific stopping rules.
It takes effect only through the independently reviewed #1406 merge. It does
not report observations or authorize an invocation by itself.

## Replaced implementation condition

The earlier spending implementation required every invocation's worst-case
liability multiplied by all 444 slots to fit the ceiling. That was an added
implementation condition, not a requirement of the frozen protocol. Original
sections 4-5, 7 and 11 require cost planning, a written ceiling, a defensible
full-pilot forecast and trustworthy enforcement. They do not guarantee
completion despite every possible infrastructure failure or prescribe a
completion probability.

The replacement is aggregate request admission. Before opening any provider
connection, reserve the full verified input-context liability plus the requested
absolute output maximum, using the most expensive admitted token categories,
fast mode and residency multiplier. No per-run starvation threshold is added.
Only validated complete provider usage releases unused reservation. Unknown,
failed, interrupted or ambiguous charges retain their whole reservation.
Every retry, concurrent request and background helper request needs a separate
atomic reservation. Unpriced operations, server compaction and fallback are
rejected before forwarding; CLI estimates never reconcile money.

## Conditions under original sections 9.2, 9.4 and 10

- Keep the three frozen shape-7 tasks, 74 scheduled runs per task per arm,
  444 slots, exact Opus 4.8 model and retained client 2.1.266.
- Both arms load the identical frozen compiler/Tasks/Runtime bytes. Only
  `--permissive-effects` differs. Source generation and Calor outputs stay cold.
- Visit run ordinals 1 through 74; within each ordinal visit the frozen task
  list in order, and within each task visit A then B. Start no replacement
  slot and do not reorder after observing outcomes or remaining money.
- Preserve the existing prompt's 10 build/test cycles, external client timeout,
  arm-specific diagnostics, raw edit mechanism and starting task contents.
  Do not use bare mode, safe mode, tools-none or an alternate model to obtain
  admission. Disable auto-update for the retained invocation only.
- Isolate the complete runner, including generated-code builds and tests.
  Permit only its capability-protected gateway endpoint, not arbitrary
  loopback, external networking, outside signals or gateway control writes.
  Kernel checks are executed; a JSON capability assertion is insufficient.
- Gateway-mode tests use the real existing xUnit v2 engine in-process instead
  of VSTest's TCP testhost. The framework is still 2.9.2 and the utility is the
  one bundled by adapter 2.8.2. Theories, fixtures, async behavior and named
  outcomes remain; testhost isolation, VSTest collectors and unsupported
  presentation/filter options are not silently emulated. Errors are explicit.
  The prebuilt test host, framework dependencies and Runtime are source/byte
  bound and read-only to the experimental descendants.

## Interruption, budget and financial lineage

The latest written ceiling is $1,000 total for the experiment, superseding
$500 and the original $250. These are not additive allowances and do not
multiply by arm, task, run or stage. Preserve the original receipt, exact
intermediate quote, inherited acceptance of negative/null publication and
registered stopping rules, and all accumulated charges and unknown liabilities.

The ledger is anchored to one canonical Git common directory and fixed relative
path shared by all worktrees/output roots. It has one immutable active scope.
There is no automatic resume, reset, expired reservation, replayed completed
slot or new ledger on retry. Any later operational amendment must carry every
settled and unresolved liability forward; it cannot refresh the allowance.

`INCOMPLETE_BUDGET`, unknown charge, policy refusal or infrastructure interruption
preserves attempted, interrupted and unstarted slots. Such a collection has
`complete: false` and `verdict: null`. Do not exclude/replace attempts, impute
zeros, analyze the observed prefix as the pilot, apply a scientific stopping
verdict, or call it a null. `UNDERPOWERED-CARRIED` remains a stage-2 disposition.

The fixed 444-slot plan still needs a defensible prospective forecast. A safe
aggregate controller does not promise that all 444 slots will fit. Stage 2
requires the actual pilot, prospective power/sample-size registration and
affordability within the remaining shared ceiling. Neither this amendment nor
the financial increase activates stage 2 or a release.

## Evidence and historical preservation

The original eleven-artifact pins, later inactive fifteen-artifact projection,
all prior analysis manifests and the dated post-guard assessment remain
byte-for-byte historical records. New source, pricing, runtime, authorization,
plan and empirical execution-profile references are additive supersessions.
The current analysis selector must audit all requests, replay concurrent
liabilities, verify all 444 ordered slot completions and reject incomplete,
mixed-stage, mixed-source or cross-epoch archives without changing arithmetic.

Engineering null-agent and mock-provider runs are not experimental observations.
Native no-forward probes use saved authentication only in client memory and
never persist credentials, account identifiers, auth payloads or request bodies.
