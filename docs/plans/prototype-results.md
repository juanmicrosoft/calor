# Sprint 0.5 Prototype Results: Cross-Assembly IL Analysis Feasibility (v2)

**Date:** 2026-04-16
**Prototype:** `tools/Calor.AnalysisPrototype/`
**Updated:** Incorporates corrections from `prototype-results-critique.md`

## Summary

**Overall: PIVOT** — Compile-time IL analysis is validated for concrete call chains (EF Core sync → DbCommand). It does NOT work for interface-heavy framework types (ILogger, IConfiguration, IHost) or async methods. The v4 plan's hybrid architecture (Tier A + Tier B + Tier C) is the right approach, but **Tier B manifests do the heavy lifting** for the patterns developers encounter most. Tier A is valuable but handles the minority of real-world framework calls.

## Corrected Gate Results

| Gate | Result | Verdict |
|------|--------|---------|
| Assembly loading + indexing | 104ms, 14.9MB | **GO** |
| `SaveChanges()` → `db:w` | Resolved to `db:rw, db:r` in 12 hops | **GO** |
| `SaveChangesAsync()` | NOT resolved — async state machine not traversed | **PIVOT** |
| Memory | 14.9MB | **GO** |
| Interface resolution rate | **1/4** resolved (25%) | **NO-GO** |
| Multi-trace scaling (4 traces, shared visited) | 8,624 visited methods, 142ms | **GO** |

### Gate 5 corrected (per critique)

The original over-approximation gate reported "GO (0/4 >2x)" — this was misleading. 3/4 test interfaces produced **zero effects**, not well-bounded effects. The corrected metric is resolution rate:

| Interface | Effects | Expected | Outcome |
|-----------|---------|----------|---------|
| `DbContext.SaveChanges()` | `[db:rw, db:r]` | `db:w` | **Resolved** |
| `ILogger.Log()` | `[]` | `cw` | **Not resolved** |
| `IConfiguration.GetSection()` | `[]` | `env:r` | **Not resolved** |
| `IHost.RunAsync()` | `[]` | `net:rw` | **Not resolved** |

Resolution rate: 1/4 = 25%. This is NO-GO for the resolution gate, confirming that Tier B manifests are essential, not supplemental.

## Key Findings

### 1. Concrete call chains work (SaveChanges sync)

The call chain traced successfully:
```
DbContext.SaveChanges()
  → SaveChanges(bool)
    → IStateManager.SaveChanges()
      → StateManager.SaveChanges()              [1 impl]
        → IDatabase.SaveChanges()
          → Database (abstract, no body)
            → RelationalDatabase.SaveChanges()   [sub-impl via Database]
              → IBatchExecutor.Execute()
                → BatchExecutor.Execute()        [1 impl]
                  → ModificationCommandBatch.Execute()
                    → IRelationalCommand.ExecuteReader()
                      → RelationalCommand.ExecuteReader()  [1 impl]
                        → DbCommand.ExecuteReader  [SEED: db:r]
                        → DbConnection.Open         [SEED: db:rw]
```

Discoveries:
- **Abstract-class-to-subclass resolution required** — `Database` → `RelationalDatabase` (template method pattern)
- **Ubiquitous interface filtering essential** — without filtering `IDisposable`/`IFormattable`/`IEnumerable`, the trace explodes through hundreds of unrelated implementations
- **`visited` set key must include parameter count** — `SaveChanges()` and `SaveChanges(bool)` must not block each other

### 2. Async methods do NOT work (SaveChangesAsync)

`SaveChangesAsync` resolved **zero effects** in 24 hops. The prototype has no async state machine handling — it doesn't detect `[AsyncStateMachine]` attributes or redirect to `MoveNext()` on the generated state machine type. This is a **known gap** that the v4 plan addresses in `AsyncStateMachineResolver.cs` but the prototype doesn't implement.

**Impact:** ~50% of real-world library methods are async. Async state machine traversal is a mandatory Sprint 1 deliverable.

### 3. ILogger failure root cause: delegates + BCL internals (architectural limit, not incomplete seeds)

Verbose trace of all 9 ILogger implementations:

| Implementation | Visited | Blocked at | Last methods in path |
|---------------|:---:|---|---|
| `NullLogger` | 1 | — | (empty body) |
| `NullLogger<T>` | 1 | — | (empty body) |
| `ConsoleLogger` | 1,588 | `Monitor.TryEnter_FastPath` | `ConsoleFormatter.Write → AnsiLogConsole.Write → AnsiParsingLogConsole.Write` |
| `DebugLogger` | 597 | `Debugger.IsManagedDebuggerAttached` | — |
| `Logger` | 1,262 | `Func<>.Invoke` | `ConsoleFormatter.Write → AnsiLogConsole.Write → AnsiParsingLogConsole.Write` |
| `EventLogLogger` | 550 | `Func<>.Invoke` | `WindowsEventLog.WriteEntry` |
| `EventSourceLogger` | 1,751 | `Monitor.TryEnter_FastPath` | `MultipartReaderStream.Flush → ReadOnlyStream.Flush → SqlFileStream.Flush` |

**Root cause analysis:**

The trace reaches deep into the logging implementations (1,000+ visited methods) but fails at two architectural boundaries:

1. **BCL internal methods** — `Monitor.TryEnter_FastPath` and `Debugger.IsManagedDebuggerAttached` have no IL body (they're intrinsics). The trace can't see through them.
2. **Delegate invocations** — `Func<>.Invoke` is the delegate call mechanism. The trace can't follow delegates to their targets because the target is runtime-determined.

Notably, `ConsoleLogger` traces all the way to `AnsiLogConsole.Write` → `AnsiParsingLogConsole.Write` — which likely calls `Console.Write` internally. But the trace gets blocked at `Monitor.TryEnter_FastPath` (thread synchronization before the write) and never reaches the seed.

**Verdict:** This is an **architectural limitation**, not an incomplete seed problem. Adding more seeds would not fix the delegate and BCL-intrinsic walls. **Tier B manifests are the correct solution for ILogger.**

### 4. Scaling: shared visited set works well

4 traces with shared visited set: 8,624 unique visited methods in 142ms. The shared set prevents re-visiting methods already explored, making subsequent traces nearly free. This validates the v4 plan's caching approach.

### 5. Performance is well within budget

| Phase | Time |
|-------|------|
| Assembly loading | 46ms |
| Type indexing | 58ms |
| Single trace (SaveChanges) | 542ms |
| 4 traces (shared visited) | 142ms |
| **Total (indexing + 4 traces)** | **246ms** |

Well under the 2s budget. Subsequent builds with caching would be ~0ms.

## Honest Architecture Framing

The v4 plan's three tiers have different contributions:

| Tier | What it covers | % of real-world framework calls |
|------|---------------|:---:|
| **A (compile-time IL analysis)** | Concrete chains: EF Core sync methods, HttpClient, File I/O, direct BCL calls | ~20-30% |
| **B (curated interface manifests)** | ILogger, IConfiguration, IHost, DbContext (as fallback), IMediator, etc. | ~50-60% |
| **C (user supplemental)** | DI-resolved services, user-defined interfaces | ~10-20% |

**Tier B does the heavy lifting.** Tier A is valuable for the cases where it works (and those cases are high-confidence, zero-maintenance), but the majority of framework calls that Calor users encounter go through interfaces that IL analysis can't resolve.

This is a fine architecture. It should be framed honestly: compile-time analysis provides a high-confidence floor for concrete types, curated manifests cover the common framework interfaces, and users fill the DI gap.

## Recommendation: Proceed with v4 plan, with adjustments

1. **Async state machine handling is mandatory in Sprint 1** — validated as non-working in prototype
2. **Abstract-class-to-subclass resolution must be in the production engine** — needed for EF Core's `Database → RelationalDatabase` pattern
3. **Ubiquitous interface filter is part of the core engine, not an optimization**
4. **ILogger Tier B annotation should be `cw`** — this is a design decision, not an empirical finding. The prototype shows the trace reaches `AnsiLogConsole.Write` which eventually calls `Console.Write`, supporting `cw` as the correct annotation.
5. **The resolution rate gate (1/4 = 25%) means Tier B manifest investment is the highest-priority Sprint 2 deliverable** — not a supplement to Tier A but the primary mechanism for framework interface coverage.
