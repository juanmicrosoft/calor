# Plan: .NET Ecosystem Effect Manifest Coverage (v4)

**Updated:** 2026-04-16 — incorporates v3 critique: honesty about over-approximation limits, prototype-first approach, Mono.Cecil, async state machines, permissive defaults, and structured type info as prerequisite.

## Context

Calor's effect system currently only covers the BCL (~60 types, ~400 method annotations). Any call to a third-party library resolves as `Unknown`. Four rounds of iteration have shaped this plan:

- **v1-v2:** Static manifests for every library → rejected because IL analysis in isolation resolves only 5-25% for interface-heavy frameworks, and the operational burden (0.5 FTE, 100+ hours review) is unsustainable.
- **v3:** Compile-time analysis of the user's dependency graph → right architecture, but the v3 critique exposed that the "union all implementations" strategy for virtual dispatch produces **over-approximated results** for interface-heavy patterns (ILogger, DbContext, IMediator), making the plan's headline examples misleading.

v4 keeps compile-time analysis as the architecture but is honest about what it delivers, adds a prototype-first approach, and fills the gap for interface-heavy types with **curated interface-level manifests** — a small, targeted set of annotations for the most common framework interfaces.

---

## Honest Assessment: What Compile-Time Analysis Actually Delivers

### Where it works well (concrete call chains)

| Call | Resolution | Confidence |
|------|-----------|-----------|
| `File.ReadAllText(path)` | Direct BCL call → `fs:r` | Definitive |
| `HttpClient.GetAsync(url)` | Concrete type → `net:rw` | Definitive |
| `DbCommand.ExecuteNonQuery()` | Concrete type → `db:w` | Definitive |
| `JsonSerializer.Serialize(obj)` | Concrete, sealed → pure | Definitive |
| Any method chain that reaches BCL leaves through only concrete types | Traced to seed effects | High |

### Where it over-approximates (interface-heavy patterns)

| Call | What union-all produces | What the user expects |
|------|------------------------|----------------------|
| `ILogger.Log()` | `cw` + `fs:w` + `net:w` (union of ALL registered sinks) | Just the effects of their configured sink |
| `DbContext.SaveChanges()` | May work if EF provider chain is narrow — **needs prototype validation** | `db:w` |
| `IMediator.Send()` | Union of ALL handler effects → potentially everything | Just the effects of the specific handler |

The union of all implementations is **sound** (never misses real effects) but potentially **so broad it's useless** for common interface-heavy patterns.

### Where it fails (irreducible dynamic dispatch)

- `IServiceProvider.GetService<T>()` — runtime resolution
- Reflection-based invocation
- Plugin/middleware registration via DI

### The v4 strategy: three resolution tiers

```
Tier A: Concrete call chains → Compile-time IL analysis (high precision)
         Works for BCL, concrete library types, sealed types.
         This is the strength of the compile-time approach.

Tier B: Common framework interfaces → Curated interface-level manifests (medium set)
         ILogger, DbContext, IConfiguration, IMediator, etc.
         ~20-30 interface types annotated at the INTERFACE level, not per-implementation.
         Small enough to maintain manually. Embedded in compiler.

Tier C: User-defined interfaces / DI patterns → User supplemental manifests
         Project-local .calor-effects.json (existing priority 300 system).
         `calor effects suggest` generates templates from unresolvable calls.
```

**Why interface-level manifests (Tier B) work:** For `ILogger.Log()`, the correct annotation at the interface level is `cw` (or a broader "any output effect"). The user doesn't care which sink implementation is involved — they care that logging is an observable side effect. By annotating at the interface level with the **expected** effect (not the union of all implementations), we get useful, correct results without over-approximation.

This is a much smaller maintenance burden than v2's "manifest every library" approach. We're annotating ~20-30 well-known .NET framework interfaces, not 2,800+ methods across 25+ libraries.

---

## Phase 0: Prerequisites

### 0a. Unify the dual effect systems

Same as previous versions — consolidate `EffectChecker.KnownEffects`, `BuiltInEffects.Catalog`, and manifests into a single manifest-based resolution system.

| File | Change |
|------|--------|
| `src/Calor.Compiler/Effects/BuiltInEffects.cs` | Delete after migration |
| `src/Calor.Compiler/Effects/EffectsCatalog.cs` | Delete |
| `src/Calor.Compiler/Effects/EffectChecker.cs` | Remove `KnownEffects`; delegate to `EffectResolver` |
| `src/Calor.Compiler/Effects/EffectResolver.cs` | Remove `EffectsCatalog` dependency |
| `src/Calor.Compiler/Manifests/bcl-*.calor-effects.json` | Absorb entries from `BuiltInEffects.Catalog` |
| `src/Calor.Compiler/Effects/EffectEnforcementPass.cs` | Update constructor |

### 0b. Add structured type info to BoundCallExpression

**This is a prerequisite, not a parallel workstream.** The compile-time analysis needs to know the resolved type at each call site. Currently `BoundCallExpression.Target` is a `string` (e.g., `"Console.WriteLine"`). The binder already resolves types during binding — we need to carry that info through.

Add to `BoundCallExpression`:
```csharp
public string? ResolvedTypeName { get; }     // Fully-qualified, e.g. "System.Console"
public string? MethodName { get; }            // e.g. "WriteLine"
public string[]? ParameterTypes { get; }      // e.g. ["System.String"]
```

This improves both compile-time analysis (precise call site identification) and manifest fallback resolution (less ambiguity from string matching).

| File | Change |
|------|--------|
| `src/Calor.Compiler/Binding/BoundNodes.cs` | Add structured fields to `BoundCallExpression` |
| `src/Calor.Compiler/Binding/Binder.cs` | Populate structured fields during binding |
| `src/Calor.Compiler/Effects/EffectEnforcementPass.cs` | Use structured fields for `EffectResolver` calls |

### 0c. Manifest schema additions

Add optional metadata fields to `EffectManifest` (backward-compatible, `JsonIgnoreCondition.WhenWritingNull`):

```csharp
public string? Library { get; set; }           // NuGet package ID
public string? LibraryVersion { get; set; }    // semver range
public string? GeneratedBy { get; set; }       // tool + version
public string? Confidence { get; set; }        // "verified" | "reviewed" | "inferred"
public string? GeneratedAt { get; set; }       // ISO 8601
```

---

## Phase 0.5: Prototype (Go/No-Go Checkpoint)

**Before committing to the full implementation, validate feasibility with a 1-week vertical prototype.**

### What the prototype does

1. Load a real project's referenced assemblies (ASP.NET Core + EF Core + Serilog — ~100-200 assemblies in transitive closure)
2. Build the interface→implementation type index
3. Trace ONE method: `DbContext.SaveChanges()` through the assembly chain to `DbCommand.ExecuteNonQuery()`
4. Measure: How long does assembly loading + type indexing take?
5. Measure: Does `SaveChanges` resolve to `db:w` through the concrete implementation chain, or does interface dispatch produce over-broad results?
6. Measure: What percentage of the EF Core public API resolves to precise effects vs. over-approximated effects?

### Go/no-go criteria

| Metric | Go | Revisit | No-go |
|--------|:---:|:---:|:---:|
| Assembly loading + type indexing time | <1s | 1-3s (need demand-driven) | >3s |
| `DbContext.SaveChanges()` resolves to `db:w` | Yes | Resolves but over-broad | Cannot trace through |
| % of EF Core methods with precise (not over-broad) effects | >50% | 30-50% | <30% |

**If no-go:** Fall back to a curated manifest approach (v2-style) but with much smaller scope — only the ~100 most commonly called framework methods, not the entire API surface. The compile-time analysis for concrete BCL call chains still has value.

### Implementation

Throwaway prototype — not production code. Use Mono.Cecil for fast iteration:

```csharp
// Prototype: load assemblies, build type index, trace one method
var assemblies = Directory.GetFiles(projectBinDir, "*.dll").Select(AssemblyDefinition.ReadAssembly);
var typeIndex = BuildInterfaceImplementationIndex(assemblies);
var effects = TraceEffects("Microsoft.EntityFrameworkCore.DbContext", "SaveChanges", typeIndex, seedEffects);
```

---

## Phase 1: Cross-Assembly IL Analysis Engine

**Use Mono.Cecil**, not raw `System.Reflection.Metadata`. Cecil provides `MethodDefinition.Body.Instructions` with resolved operands — the call graph builder is ~50 lines instead of ~1,500. The trade-off (extra NuGet dependency, slightly higher memory) is worth it for the 2-3 weeks of development time saved.

**Location:** `src/Calor.Compiler/Effects/Analysis/`

### Key components

| File | Purpose | Est. lines |
|------|---------|-----------|
| `AssemblyGraph.cs` | Loads referenced assemblies via Cecil. Builds cross-assembly type index: for every interface/abstract method, find all concrete implementations across all loaded assemblies. | ~500-800 |
| `ILCallGraphBuilder.cs` | Walks `MethodBody.Instructions` for each method. Extracts call edges from `Call`, `Callvirt`, `Newobj` instructions via `Instruction.Operand` (which is already a resolved `MethodReference`). Handles async state machines (see below). | ~300-500 |
| `TransitiveEffectPropagator.cs` | Tarjan's SCC + fixpoint iteration. Propagates seed effects upward through resolved call chains. Uses same algorithm as `EffectEnforcementPass` but on Cecil's method graph. | ~300-400 |
| `AsyncStateMatchineResolver.cs` | Detects compiler-generated async state machine types (`IAsyncStateMachine` implementors with `<MethodName>d__N` naming). Redirects analysis from the stub `async` method to its `MoveNext()` body. **Critical:** ~50% of modern .NET library methods are async. | ~150-200 |
| `EffectAnalysisResult.cs` | Per-method results: resolved effects, confidence level, resolution path, contributing implementations (for `calor effects explain`). | ~100 |

### Virtual dispatch strategy (refined from v3)

For a `callvirt` to `IFoo.Bar()`:

1. Look up all concrete implementations of `IFoo.Bar()` in the assembly graph
2. **If exactly 1 implementation found:** high-confidence resolution — trace through that implementation
3. **If 2-5 implementations found:** union effects, tag as `"over-approximated"`, record which implementations contributed
4. **If >5 implementations found or interface is in the "framework interface" set (ILogger, IServiceProvider, etc.):** **skip IL analysis for this call**, fall back to interface-level manifest (Tier B)
5. **If 0 implementations found:** return `Unknown`, recommend supplemental manifest

The threshold (>5 implementations) and the "framework interface" set are configurable. The framework interface set is maintained alongside the Tier B manifests.

### Async state machine handling

When the analysis encounters a method with body `{/* generated async stub */}`:
1. Check if the method's type has a nested type implementing `IAsyncStateMachine`
2. If so, find the `MoveNext()` method on that nested type
3. Analyze `MoveNext()` instead of the original method
4. Map effects back to the original async method

This is essential — without it, every `async` method in every library is a dead end.

### Demand-driven analysis (performance)

Don't eagerly analyze all 200 assemblies. Instead:
1. Start from the Calor program's external call sites (from `BoundCallExpression` with structured type info)
2. Load only the assemblies containing those types
3. Trace outward as needed (if method A in assembly X calls method B in assembly Y, load Y on demand)
4. Stop at BCL seed effects (they're the terminal nodes)

This keeps the analysis focused on what's actually needed.

---

## Phase 2: Curated Interface-Level Manifests (Tier B)

**Goal:** Annotate ~20-30 well-known .NET framework interfaces at the interface level, covering the patterns where compile-time analysis over-approximates.

### Why this works

For `ILogger.Log()`, the correct annotation is not the union of all implementations — it's a declaration of what logging means: **observable output**. Whether the output goes to console, file, or network depends on configuration. From the caller's perspective, the effect is "this function logs" — and the existing effect code `cw` (or we could use a broader IO code) captures this.

For `DbContext.SaveChanges()`, if IL analysis can trace through the concrete provider chain, great. If not (and the prototype will tell us), we annotate at the interface level: `db:w`.

### Manifest: `bcl-framework-interfaces.calor-effects.json`

Embedded in the compiler alongside the existing BCL manifests:

```json
{
  "version": "1.0",
  "description": "Effect declarations for common .NET framework interfaces (Tier B)",
  "mappings": [
    {
      "type": "Microsoft.Extensions.Logging.ILogger",
      "methods": {
        "Log": ["cw"],
        "LogInformation": ["cw"],
        "LogWarning": ["cw"],
        "LogError": ["cw"],
        "LogDebug": ["cw"],
        "LogTrace": ["cw"],
        "LogCritical": ["cw"],
        "IsEnabled": []
      }
    },
    {
      "type": "Microsoft.EntityFrameworkCore.DbContext",
      "methods": {
        "SaveChanges": ["db:w"],
        "SaveChangesAsync": ["db:w"],
        "Find": ["db:r"],
        "FindAsync": ["db:r"],
        "Add": ["mut"],
        "Update": ["mut"],
        "Remove": ["mut"],
        "Set": []
      }
    },
    {
      "type": "Microsoft.Extensions.Configuration.IConfiguration",
      "methods": { "*": ["env:r"] }
    },
    {
      "type": "Microsoft.Extensions.DependencyInjection.IServiceProvider",
      "methods": { "GetService": [] }
    }
  ]
}
```

**Size estimate:** ~20-30 interface types, ~200 method entries. Maintains the same JSON format as existing BCL manifests. Reviewed and updated with compiler releases.

### Resolution priority

When both compile-time analysis and a Tier B manifest entry exist for the same call:

- **If compile-time analysis is precise (single implementation, confidence: high):** use analysis result
- **If compile-time analysis is over-approximated (multiple implementations, tagged):** use Tier B manifest
- **If neither resolves:** `Unknown` — suggest supplemental manifest via `calor effects suggest`

This is implemented in `EffectResolver` by checking the `EffectAnalysisResult.IsOverApproximated` flag.

---

## Phase 3: Integration, Caching, and MSBuild Plumbing

### Compilation pipeline changes

Current:
```
Calor Source → Parse → Bind → EffectEnforcementPass → Diagnostics
                                    ↓
                          EffectResolver.Resolve(string, string)
                          → manifests → Unknown
```

New:
```
Calor Source → Parse → Bind → EffectEnforcementPass → Diagnostics
                                    ↓
                          For each external call (with structured type info):
                          1. Check effect analysis cache
                          2. If precise analysis result: use it
                          3. If over-approximated: use Tier B manifest
                          4. If no result: EffectResolver manifest fallback
                          5. If still unknown: report with actionable diagnostic
```

### CompilationOptions changes

```csharp
public class CompilationOptions
{
    // Existing fields...
    public IReadOnlyList<string>? ReferencedAssemblyPaths { get; set; }  // NEW
    public bool EnableCrossAssemblyAnalysis { get; set; } = true;       // NEW — can be disabled for speed
}
```

**Cross-cutting impact:** Every caller of `Program.Compile()` must be evaluated:
| Caller | Change needed |
|--------|--------------|
| `CompileCalor` MSBuild task | Pass `@(Reference)` items as assembly paths |
| `calor` CLI | Resolve from `--reference` flags or infer from project file |
| MCP tools (`CompileTool`, `CheckTool`, etc.) | Pass through if available |
| `SelfTestRunner` | No change (BCL-only tests) |

### Caching

**Cache file:** `obj/calor/effect-analysis.cache`

**Cache key:** Hash of:
- Referenced assembly **content hashes** (MVID, not timestamps — avoids spurious invalidation)
- BCL seed manifest content hash
- Tier B manifest content hash
- Supplemental manifest content hash
- Calor compiler version
- Set of external call sites in the Calor source (if new calls added, partial re-analysis needed)

**Cache behavior:**
- First build: full analysis (~1-2s target, validated by prototype)
- Subsequent builds (code changes only, no new external call sites): cache hit, ~0ms
- After `dotnet restore` that changes assembly content: cache miss, re-analyze
- New Calor source adds calls to previously-unanalyzed types: partial re-analysis for those types only

### Enforcement mode for analysis-derived effects

**Default to permissive (warnings, not errors) for compile-time analysis results.** Over-approximation + strict enforcement = false positive storm that kills user trust.

Users opt into strict mode explicitly:
```bash
calor --input app.calr --strict-effects  # errors for all undeclared effects including analysis-derived
```

Without `--strict-effects`, analysis-derived effects produce `Calor0412` (warning) instead of `Calor0410` (error).

### Dependencies

The compiler gains a dependency on `Mono.Cecil`:
- `Mono.Cecil` NuGet package (~300KB)
- Already targets .NET Standard 2.0 (compatible with net10.0)
- Well-maintained (used by Fody, ILRepack, hundreds of tools)
- The `Calor.Tasks` assembly needs to bundle this or the `Calor.Sdk` package includes it

---

## Phase 4: BCL Seed Manifest Completion + CLI Tooling

### Additional BCL manifests

| Area | Manifest file |
|------|--------------|
| System.Text.Json | `bcl-text-json.calor-effects.json` |
| System.Text.RegularExpressions | `bcl-text-regex.calor-effects.json` |
| System.Collections.Concurrent | `bcl-concurrent.calor-effects.json` |
| System.Security.Cryptography | `bcl-crypto.calor-effects.json` |
| System.Threading (Mutex, Semaphore, Timer, Channel) | `bcl-threading.calor-effects.json` |

### CLI commands

**`calor effects explain <type.method>`** — First-class feature, not a debug aid. Shows exactly how an effect was resolved:

```
$ calor effects explain DbContext.SaveChanges

DbContext.SaveChanges() → db:w
  Resolution: compile-time analysis (precise)
  Path: SaveChanges() → StateManager.SaveChanges() → ... → DbCommand.ExecuteNonQuery() [seed: db:w]

$ calor effects explain ILogger.Log

ILogger.Log() → cw
  Resolution: interface-level manifest (Tier B)
  Source: bcl-framework-interfaces.calor-effects.json
  Reason: 12 implementations found in loaded assemblies (over-approximation threshold exceeded)
  Implementations: Logger<T>, NullLogger, SerilogLogger, ConsoleLogger, DebugLogger, ...
```

**`calor effects suggest --input app.calr`** — Generates supplemental manifest template for unresolvable calls.

**`calor effects analyze --project ./src/MyApp.csproj`** — Runs compile-time analysis standalone and reports: total external calls, resolved precisely, resolved via Tier B manifest, resolved via supplemental manifest, unresolvable. Useful for evaluating coverage before enabling strict mode.

---

## Sprint Sequencing

| Sprint | Weeks | Deliverables |
|--------|-------|-------------|
| **0** | 1-2 | Prerequisites: unify dual effect systems, add structured type info to `BoundCallExpression`, manifest schema additions. |
| **0.5** | 3 | **Prototype (go/no-go checkpoint).** Load real project assemblies with Cecil, build type index, trace `DbContext.SaveChanges()` → `db:w`. Measure time + over-approximation. Report findings. |
| **1** | 4-6 | IL analysis engine: `AssemblyGraph`, `ILCallGraphBuilder`, `TransitiveEffectPropagator`, `AsyncStateMachineResolver`. Use Mono.Cecil. Unit tests with real assemblies. Performance benchmarks. |
| **2** | 7-8 | Tier B interface-level manifests (~20-30 interfaces). Resolution priority logic in `EffectResolver`. Integration with `EffectEnforcementPass`. Permissive mode for analysis-derived effects. |
| **3** | 9-10 | Caching infrastructure. MSBuild integration (`CompileCalor` receives `@(Reference)`, passes to analysis). `CompilationOptions` changes. CLI commands: `calor effects explain`, `calor effects suggest`, `calor effects analyze`. |
| **4** | 11-12 | BCL seed manifest completion. End-to-end testing: Calor programs calling EF Core, ASP.NET Core, Serilog with correct effect resolution. Documentation. |

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **Prototype fails (no-go)** | Fall back to curated manifests for ~100 most commonly called framework methods (Tier B approach only, no compile-time analysis for Tier A). Still better than `Unknown` for everything. |
| **Over-approximation exceeds 30%** | Tier B manifests cover the worst offenders (ILogger, etc.). For the rest, permissive mode means warnings not errors. Users provide supplemental manifests for their specific interfaces. |
| **Performance exceeds 2s** | Demand-driven analysis (only trace from actual call sites). Skip assemblies not in the call subgraph. Cache aggressively. |
| **Async state machines are more complex than expected** | Cecil provides `CustomAttribute` access to detect `[AsyncStateMachine]` attribute, which directly links to the state machine type. Well-documented pattern. |
| **Mono.Cecil dependency causes issues** | Cecil is .NET Standard 2.0, widely used (Fody, ILRepack, Coverlet). Version-pin to avoid surprises. Falls back gracefully if Cecil load fails (disable analysis, warn). |
| **False positives from over-approximation in strict mode** | Strict mode is opt-in. Default is permissive (warnings). `calor effects explain` shows why an effect was attributed. Users can override with supplemental manifests (priority 300). |

---

## What v4 Defers

| Item | Reason | When to revisit |
|------|--------|----------------|
| Type-flow narrowing | Would allow precise resolution for ILogger (track concrete type at call site). Significant compiler complexity. | After 6 months of usage data on Tier B manifests — if users find `cw` for all logging too broad |
| Community manifest repository | Tier B is ~20-30 interfaces maintained by the core team. No community pipeline needed at this scale. | If supplemental manifest patterns become common enough to share |
| Manifest NuGet packages | Compile-time analysis + embedded manifests eliminate the need. | Only if a class of libraries is systematically unresolvable |
| `log` and `msg:*` effect codes | ILogger mapped to `cw` via Tier B manifest. Compile-time analysis resolves concrete sink types naturally. | If users report `cw` is insufficiently granular for logging |
| Auto-update pipeline | No library-specific manifests to update. BCL seeds + Tier B manifests updated with compiler releases. | N/A |

---

## Verification: Phase-Gated with Go/No-Go Criteria

Full verification strategy is documented in:
- `docs/plans/dotnet-ecosystem-effect-manifests-verification.md` — test categories, benchmarks, cache tests
- `docs/plans/verification-strategy-effect-manifests-v3.md` — gate structure, acceptance criteria, post-release monitoring

### Gate structure

```
Phase 0 ──── Regression gate ────────── Go / No-go
                                            │
Sprint 0.5 ── Feasibility spike ──────── Go / Pivot / No-go
                                            │
Phase 1 ──── Resolution + perf gate ──── Go / Caution / No-go
                                            │
Phase 2 ──── Correctness gate ────────── Go / No-go
                                            │
Phase 3 ──── Cache correctness gate ──── Go / No-go
                                            │
Phase 4 ──── Acceptance gate ─────────── Ship / Iterate / Rethink
```

### Phase 0 gate: Unification preserves existing behavior

| Test | Criteria |
|------|----------|
| **Migration completeness** | All ~247 `BuiltInEffects.Catalog` entries produce identical `EffectResolution` through manifest-only resolver |
| **Enforcement regression** | All `Calor.Enforcement.Tests` scenarios produce identical diagnostics (code, message, line, call chain) |
| **Stale reference check** | Grep confirms zero references to `BuiltInEffects`, `EffectsCatalog`, or `EffectChecker.KnownEffects` |
| **Go:** All three pass. **No-go:** Any regression in enforcement tests. Do not proceed with a broken foundation. |

### Sprint 0.5 gate: Feasibility spike

| Metric | Go | Pivot | No-go |
|--------|:---:|:---:|:---:|
| Assembly loading + type indexing time (real EF Core project) | <500ms | 500ms-2s | >2s |
| `DbContext.SaveChanges()` traces to `db:w` | Yes, <5 hops | Yes, 10-20 hops | Cannot trace |
| Over-approximation: run union-all on 5 common interfaces | ≤3 of 5 have >2x over-approx | 4 of 5 | 5 of 5 |
| Memory footprint | <100MB | 100-500MB | >500MB |

Measure over-approximation on: `ILogger.Log()`, `IMediator.Send()`, `DbContext.SaveChanges()`, `IConfiguration.GetValue()`, `IHostBuilder.Build()`. Report # implementations found, unioned effects, human-expected effects, ratio.

**Pivot** means: proceed but revise performance budget or add demand-driven analysis as default.
**No-go** means: fall back to Tier B manifests only (no compile-time analysis for Tier A).

### Phase 1 gate: IL analysis works at scale

| Test | Criteria |
|------|----------|
| **Real-assembly resolution** | `DbContext.SaveChanges()` → `db:w` and `HttpClient.GetAsync()` → `net:rw` using actual NuGet packages |
| **Over-approximation ratio** | <2x for Tier 1-2 libraries (attributed effects ≤ 2× human-expected) |
| **12 unit test categories** | Direct calls, virtual dispatch (1/N/0 impls), cross-assembly chains, constructors, properties, fields, async state machines, generics, depth limits, unloadable assemblies, circular refs |
| **Performance** | Full analysis <2s on 50-assembly project (CI-enforced) |
| **Determinism** | 10 runs with shuffled assembly load order produce identical results |

Unit tests use in-memory compiled test assemblies via `CSharpCompilation.Emit()`. Integration tests use real NuGet packages. Snapshot tests follow existing `Calor.Conversion.Tests` pattern.

### Phase 2 gate: Propagation correctness

| Test | Criteria |
|------|----------|
| **Fixpoint convergence** | <50 iterations for all test cases |
| **Determinism** | Identical results across 10 runs with shuffled load order |
| **Known-answer golden tests** | `SaveChanges` → `db:w` (3-8 hops), `GetAsync` → `net:rw` (2-5 hops), `Serialize(string)` → pure, `Serialize(Stream)` → `fs:w` |

**No-go:** Non-determinism. Different diagnostics on different builds is unacceptable for a compiler.

### Phase 3 gate: Cache correctness

| Test | Criteria |
|------|----------|
| **Round-trip** | Write cache, read cache, results are identical to fresh analysis |
| **Invalidation: new call sites** | New Calor source adding calls to already-cached assemblies → cache still valid |
| **Invalidation: assembly changes** | Package version bump → cache miss, re-analysis |
| **Invalidation: supplemental manifest** | Edit `.calor-effects.json` → cache miss |
| **No spurious invalidation** | `dotnet restore` with identical package content (different timestamps) → cache hit (uses MVID/content hash) |
| **MSBuild incremental build** | Shell-script test: first build analyses, second build (code-only change) hits cache, third build (package change) re-analyses |

**No-go:** Cache too lenient (doesn't invalidate when it should → stale results → wrong diagnostics).

### Phase 4 gate: End-to-end acceptance

| Test | Criteria |
|------|----------|
| **Resolution rate** | EF Core CRUD app >90%, ASP.NET Core controller >75%, Serilog >85%, MediatR+DI >50% |
| **False positives** | Per-scenario `known-false-positives.json`. CI fails on new false positives. |
| **Comparison baseline** | Run same programs with current compiler (no analysis) vs. v4. Delta must be >40 percentage points improvement in resolution rate. |
| **Over-approximation on real programs** | Average function needs ≤3 effect codes. If >3 due to over-approximation, Tier B manifests must cover the worst offenders. |
| **CLI tools** | `calor effects explain` shows resolution path. `calor effects suggest` produces valid JSON. `calor effects analyze` reports resolution rates. |

### Performance budgets (CI-enforced)

| Benchmark | Target |
|-----------|--------|
| Assembly loading (10 assemblies) | <100ms |
| Assembly loading (50 assemblies) | <300ms |
| Assembly loading (200 assemblies) | <800ms |
| IL decoding (single assembly) | <50ms |
| Call graph construction (EF Core chain) | <200ms |
| Effect propagation (EF Core subgraph) | <100ms |
| Full analysis, cold start (50 assemblies) | <2s |
| Full analysis, cache hit | <10ms |
| Partial re-analysis (1 new assembly) | <500ms |

Memory: type index <50MB, call graph <20MB, peak working set <200MB.

### Post-release monitoring

| Signal | What it indicates | Action threshold |
|--------|-------------------|-----------------|
| User supplemental manifest size | Irreducible gap size | >50 entries → consider reintroducing static manifests for specific tiers |
| `Calor0411` frequency | Unresolvable call rate | >15% of external calls → investigate and expand Tier B |
| `calor effects suggest` usage | Gap is real but manageable | High usage + low `Calor0411` = healthy. Low usage + high `Calor0411` = users ignoring the gap |
| Over-approximation trend | Union strategy accumulating noise | Monthly measurement; trending upward → prioritize type-flow narrowing |

### What "Done" looks like

The feature is verified and ready to ship when:

1. **Accuracy:** `DbContext.SaveChanges()` → `db:w` resolves using only BCL seeds + compile-time analysis (no EF Core manifest needed). `ILogger.Information()` → `cw` via Tier B. `File.ReadAllText()` → `fs:r` via BCL seed.
2. **Precision:** Over-approximation ratio <30% on integration scenarios. Average function needs ≤3 effect declarations.
3. **Performance:** Cold analysis <2s on 50-assembly project. Cache hit <10ms. No measurable build-time regression on cache hit.
4. **Resolution improvement:** >40 percentage point improvement over current compiler (no analysis) on real programs.
5. **Graceful degradation:** Unloadable assemblies → warnings. Unresolvable interfaces → actionable `Calor0411`. `calor effects suggest` generates valid template.
6. **No regressions:** All existing tests pass. No new `Unknown` resolutions in previously-resolved scenarios. Deterministic across runs.
7. **Maintainability:** Snapshot tests cover all categories. Performance benchmarks in CI. Cache equivalence check nightly.
