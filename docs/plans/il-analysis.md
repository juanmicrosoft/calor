# Plan: Compile-Time IL Analysis for Effect Resolution (v3)

**Version:** 3 (revised after two critique rounds + gap analysis)

## Context

Calor's effect enforcement currently resolves external .NET method effects via static JSON manifests only. When a method isn't covered by manifests, it returns `Unknown` (triggering Calor0411 in strict mode). This means user code calling a data-access layer DLL that transitively calls `DbContext.SaveChanges()` gets no `db:w` effect resolution unless every intermediate method is manually cataloged.

A prototype (`docs/plans/prototype-results.md`) validated that IL analysis can trace `SaveChanges()` → `db:w` through 12 hops in 142ms. It confirmed async state machine traversal is mandatory (0% resolution without it) and interface-heavy types (ILogger, IConfiguration) should use Tier B manifests instead.

**Goal:** Add opt-in compile-time IL analysis that reads referenced .NET assemblies, builds a call graph from IL opcodes, seeds effects from manifests, and propagates them backward — filling the gap between manifests and Unknown.

---

## Approach

Use `System.Reflection.Metadata` (already in .NET SDK — no extra deps). IL analysis sits in `EffectResolver` **after all manifest layers** (including namespace defaults) but before returning Unknown. Feature is opt-in via `<CalorEnableILAnalysis>true</CalorEnableILAnalysis>`.

```
EffectResolver resolution pipeline:
  1. Specific method in manifest
  2. Method name in manifest
  3. Wildcard in manifest
  4. Type default effects
  5. Namespace defaults
  6. ► IL analysis fallback (NEW)
  7. Unknown
```

Manifests always win — IL analysis only fills the gap between namespace-default coverage and Unknown.

---

## Reference Assembly vs. Implementation Assembly Resolution

NuGet packages and BCL types ship **reference assemblies** (`ref/` folders) with method signatures but **no method bodies**. `PEReader` will find empty method bodies for these — the analyzer would resolve zero effects if it reads ref assemblies. This is the single most critical integration issue.

### Detection

Check for `System.Runtime.CompilerServices.ReferenceAssemblyAttribute` in the assembly's custom attributes. If present, it's a ref assembly with no usable IL.

### Resolution Strategy

For each assembly path from `@(ReferencePath)`:

1. **Load PE header and check for `ReferenceAssemblyAttribute`.**
2. **If implementation assembly (no attribute):** Use directly — has full method bodies. This covers project references (`<ProjectReference>`) and local DLLs.
3. **If reference assembly:** Resolve the corresponding implementation assembly via **`{project}.deps.json`**:
   - After `dotnet build` runs, `{OutputPath}/{AssemblyName}.deps.json` contains the full dependency graph with both `compile` and `runtime` sections for every package.
   - The `runtime` section maps each library to its implementation assembly path relative to the NuGet cache.
   - **Algorithm:** Read `{ProjectDirectory}/obj/{Configuration}/{TFM}/{AssemblyName}.deps.json` (the intermediate output) or the project's `project.assets.json`. For each ref assembly, look up its package ID in the `libraries` section, then find the `runtime` asset path. Combine with the NuGet global packages folder (`$(NuGetPackageRoot)`, typically `~/.nuget/packages/`) to get the absolute path to the implementation assembly.
   - **For BCL / runtime pack assemblies** (e.g., `System.Data.Common.dll`): These come from the targeting pack (`ref/` only). The implementation lives in the shared runtime at `{dotnet root}/shared/Microsoft.NETCore.App/{version}/`. Resolve via `$(NetCoreTargetingPackRoot)` or `RuntimeFrameworkVersion`. Pass the **runtime directory path** as an additional MSBuild property (`CalorRuntimeDirectory`) so the task can search it.
   - **Fallback:** If the implementation assembly can't be found, log Calor0415 and skip — let manifests or Unknown handle methods from it.
4. **Post-resolution validation:** Sample a non-abstract, non-extern method in the loaded assembly and verify it has an IL body. If still bodiless, treat as unresolvable.

### MSBuild Integration

Use **`@(ReferencePath)`** (not `@(Reference)`) — populated by `ResolveAssemblyReferences` with resolved file paths.

```xml
<PropertyGroup>
  <!-- Detect runtime directory for BCL implementation assembly resolution -->
  <_CalorRuntimeDir Condition="'$(CalorEnableILAnalysis)' == 'true'">$(NetCoreTargetingPackRoot)\..\..\..\shared\Microsoft.NETCore.App\$(BundledNETCoreAppTargetFrameworkVersion)\</_CalorRuntimeDir>
</PropertyGroup>

<CompileCalor
    SourceFiles="@(CalorCompile)"
    OutputDirectory="$(CalorOutputDirectory)"
    ProjectDirectory="$(MSBuildProjectDirectory)"
    Verbose="$(CalorVerbose)"
    EnableILAnalysis="$(CalorEnableILAnalysis)"
    ReferencedAssemblies="@(ReferencePath)"
    RuntimeDirectory="$(_CalorRuntimeDir)"
    NuGetPackageRoot="$(NuGetPackageRoot)"
    DepsFilePath="$(ProjectDepsFilePath)">
```

Add `DependsOnTargets="ResolveAssemblyReferences"` to the `CompileCalorFiles` target only when IL analysis is enabled:

```xml
<PropertyGroup>
  <_CompileCalorDependsOn Condition="'$(CalorEnableILAnalysis)' == 'true'">ResolveAssemblyReferences</_CompileCalorDependsOn>
</PropertyGroup>

<Target Name="CompileCalorFiles"
        BeforeTargets="BeforeCompile"
        DependsOnTargets="$(_CompileCalorDependsOn)"
        Condition="'@(CalorCompile)' != ''">
```

### Test: Real NuGet-resolved Assembly

A mandatory integration test loads `System.Data.Common.dll` from `@(ReferencePath)` the way MSBuild resolves it, detects it as a ref assembly, resolves the implementation from the runtime directory, and extracts `DbCommand.ExecuteNonQuery()` call edges. This test prevents shipping a feature that works on hand-built fixtures and resolves nothing in production.

---

## Analyzer Caching Across Files (Task-Level Shared State)

### Problem

`CompileCalor.Execute()` compiles each `.calr` file independently via `Program.Compile(source, path, false)` (CompileCalor.cs:195). Each call creates a new `EffectEnforcementPass` and `EffectResolver`. With IL analysis, this would construct a new `ILEffectAnalyzer` + load all assemblies per file — 100 files × 50 assemblies = 5,000 loads.

### Solution: `CompilationContext` shared object

Introduce a new `CompilationContext` class that holds shared, reusable state across file compilations:

```csharp
// New class in src/Calor.Compiler/CompilationContext.cs
public sealed class CompilationContext : IDisposable
{
    /// <summary>
    /// Pre-built effect resolver with IL analyzer attached.
    /// Reused across all file compilations in a single build.
    /// </summary>
    public EffectResolver? SharedEffectResolver { get; init; }

    public void Dispose()
    {
        (SharedEffectResolver as IDisposable)?.Dispose();
    }
}
```

This follows the existing pattern where `CompilationOptions` holds result objects via `internal set` (e.g., `VerificationResults`, `ObligationResults`), but separates "shared live objects" from "configuration" — `CompilationContext` holds stateful services, `CompilationOptions` holds value-type config.

**`CompilationOptions` change:** Add `CompilationContext? Context { get; init; }` — a single optional field, not a live analyzer.

**`Program.Compile` change:** When `options.Context?.SharedEffectResolver` is non-null, use it instead of constructing a new `EffectResolver`. The enforcement pass constructor already accepts `EffectResolver? resolver` — just pass the shared one.

**`CompileCalor.Execute()` change:**

```csharp
// Before the per-file loop:
CompilationContext? context = null;
if (EnableILAnalysis && ReferencedAssemblies.Length > 0)
{
    var assemblyPaths = ReferencedAssemblies
        .Select(item => item.GetMetadata("FullPath"))
        .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
        .ToList();

    var resolver = new EffectResolver();
    resolver.Initialize(ProjectDirectory);

    var ilAnalyzer = new ILEffectAnalyzer(assemblyPaths, resolver,
        runtimeDirectory: RuntimeDirectory,
        nugetPackageRoot: NuGetPackageRoot,
        depsFilePath: DepsFilePath);
    var sharedResolver = new EffectResolver(ilAnalyzer: ilAnalyzer);
    sharedResolver.Initialize(ProjectDirectory);

    context = new CompilationContext { SharedEffectResolver = sharedResolver };
}

// In the per-file loop:
var options = new CompilationOptions
{
    EnforceEffects = true,
    ProjectDirectory = ProjectDirectory,
    Context = context,
    // ... other options
};
var result = Program.Compile(source, inputPath, options);

// After the loop:
context?.Dispose();
```

This gives O(1) assembly loads per build, not O(N×M).

### Empty `@(ReferencePath)` Handling

When `CalorEnableILAnalysis=true` but `@(ReferencePath)` is empty (pure Calor project with no NuGet/project references), the task skips analyzer construction entirely — `context` remains null, and compilation proceeds without IL analysis. No error, no warning. Add a test for this.

---

## Method Identity: Full Signature Keys

`MethodKey(TypeName, MethodName, ParameterCount)` is insufficient — same-arity overloads are common in .NET (`Add(int)` vs `Add(string)`, `ref`/`out` variants, generic instantiations). The analyzer uses full parameter type signatures:

```csharp
public readonly record struct MethodKey(
    string TypeName,       // Fully qualified, e.g. "System.Data.Common.DbCommand"
    string MethodName,     // e.g. "ExecuteNonQuery"
    string ParameterSig)   // Serialized parameter types, e.g. "(System.String,System.Int32)"
{
    public static MethodKey FromDefinition(MetadataReader reader, MethodDefinitionHandle handle);
    public static MethodKey FromReference(MetadataReader reader, MemberReferenceHandle handle);

    /// Name-only key for fallback lookups when caller lacks parameter types.
    public (string TypeName, string MethodName) NameKey => (TypeName, MethodName);
}
```

SRM provides `MethodSignature<T>` via `SignatureDecoder` — decode parameter types into a canonical string form. For generic type parameters, use positional names (`!0`, `!!0`) to avoid instantiation-specific keys.

### Fallback for Name-Only Lookups

The effect enforcement pass currently calls `_resolver.Resolve(typeName, methodName)` **without parameter types** (confirmed at EffectEnforcementPass.cs:566). The `BoundCallExpression` has `ResolvedParameterTypes` but the enforcement pass operates on string-based call targets, not bound nodes.

**For this version:** When the IL analyzer receives a name-only query (no `ParameterSig`), it unions effects across all overloads of `(TypeName, MethodName)`. This is an explicit, documented over-approximation. It is sound (never under-reports) but may over-report for methods with overloads that have different effects.

**Follow-up:** Thread `BoundCallExpression.ResolvedParameterTypes` through `InferFromCallTarget` → `EffectResolver.Resolve()` for overload-precise resolution. This is a pre-existing limitation that becomes more valuable to fix with IL analysis, but is not blocking.

---

## Assembly Loading: Two-Phase (Eager Type Scan, Lazy Body Load)

The plan says "assemblies loaded lazily on demand" but this requires knowing *which assembly* contains a given type before loading it — a chicken-and-egg problem.

**Solution: Two-phase loading.**

**Phase 1 (Eager, cheap):** On `AssemblyIndex` construction, open each assembly's PE header and scan its `TypeDefinition` table to build a type→assembly mapping. This reads only the metadata tables (type names, flags), not method bodies. For a typical assembly, this is ~1-5ms — scanning 50 assemblies costs ~50-250ms total.

**Phase 2 (Lazy, per-method):** When `FindMethod()` is called and a specific method body is needed, load the full `MethodBodyBlock` on demand. The `PEReader` stays open (it memory-maps the file), but method body decoding only happens when needed.

This means all 50 assemblies get their type tables scanned at startup, but only the 10-15 assemblies that contain types actually referenced by the Calor program have their method bodies decoded.

---

## Three-State Resolution (Soundness)

IL analysis must distinguish between "method is pure" and "analysis was incomplete":

```csharp
public enum ILResolutionStatus
{
    /// Analysis completed; all callees resolved. Effects are precise.
    Resolved,

    /// Analysis completed; all callees resolved, no effects found.
    ResolvedPure,

    /// Analysis was cut short. Effects are UNKNOWN, not empty.
    Incomplete
}
```

**Propagation rule:** If any callee in a method's transitive closure is `Incomplete`, the method itself is `Incomplete`. Only methods whose complete call graph was fully analyzed can be `Resolved` or `ResolvedPure`.

`ILEffectAnalyzer.TryResolve()` returns:
- `EffectResolution(Resolved, effects, source)` for `Resolved`
- `EffectResolution(PureExplicit, Empty, source)` for `ResolvedPure`
- `null` for `Incomplete` — falls through to Unknown

This prevents the silent false-purity bug.

---

## IL Opcode Coverage

### Tracked as call-graph edges:
| Opcode | Hex | Handling |
|--------|-----|----------|
| `call` | 0x28 | Direct call → edge to callee |
| `callvirt` | 0x6F | Virtual call → edge to callee (virtual dispatch resolution) |
| `newobj` | 0x73 | Constructor call → edge to `.ctor` |
| `ldftn` | 0xFE 0x06 | Function pointer load → edge from enclosing method to target (delegate creation) |
| `ldvirtftn` | 0xFE 0x07 | Virtual function pointer load → edge with virtual dispatch |

### Tracked as analysis boundaries (mark method Incomplete):
| Opcode | Hex | Handling |
|--------|-----|----------|
| `calli` | 0x29 | Indirect call via function pointer — target unknown → Incomplete |

The `ldftn`/`ldvirtftn` handling is critical for delegate-based patterns: `Task.Run(() => repo.Save())`, `Parallel.ForEach(items, item => Process(item))`, LINQ lambdas.

---

## State Machine Redirection

### Async (`[AsyncStateMachine]`)
1. Read `CustomAttribute` rows on the `MethodDefinition`.
2. Decode the attribute constructor — check if it's `AsyncStateMachineAttribute`.
3. **Read the `Type` argument directly from the attribute blob** — authoritative.
4. Find `MoveNext()` on that type and analyze it instead of the stub.
5. **No name-pattern fallback.** If attribute is absent/undecodable → Incomplete.

### Iterator (`[IteratorStateMachine]`)
Same approach: read `IteratorStateMachineAttribute`, redirect to `MoveNext()`. Handles `yield`-based methods.

---

## New Files: `src/Calor.Compiler/Effects/IL/`

### `ILAnalysisOptions.cs` (~60 lines)
`MaxDepth` (50), `MaxVirtualImplementations` (5), `MaxVisitedMethods` (10,000), `SkipInterfaces`, `UbiquitousInterfaces`.

### `AssemblyIndex.cs` (~700 lines)
Two-phase loading. Implements `IDisposable`. Type→assembly mapping with `(AssemblyName, TypeName)` keys. Interface implementation index. Abstract subtype index. Ref assembly detection + `deps.json`-based impl resolution. Runtime directory fallback for BCL types. `BadImageFormatException` catch on all SRM calls.

### `ILCallGraphBuilder.cs` (~600 lines)
IL decoding via `MethodBodyBlock.GetILReader()` + `BlobReader`. Extracts `call`, `callvirt`, `newobj`, `ldftn`, `ldvirtftn`. Marks `calli` as boundary. Full-signature `MethodKey` from `SignatureDecoder`. Handles `MethodSpecificationHandle` (generics).

### `StateMachineResolver.cs` (~250 lines)
`[AsyncStateMachine]` + `[IteratorStateMachine]`. Attribute-blob Type argument decoding. No name-pattern fallback.

### `TransitiveEffectPropagator.cs` (~400 lines)

**Phase 1: Graph construction** (demand-driven BFS)
```
worklist = Queue<MethodKey>(entryPoints), sorted by MethodKey.ToString()
visited  = HashSet<MethodKey>()
seeds    = Dict<MethodKey, EffectSet>()
incomplete = HashSet<MethodKey>()

while worklist not empty:
    m = dequeue
    if visited contains m: continue
    if |visited| > MaxVisitedMethods:
        emit Calor0416 naming m; incomplete.add(m); continue
    visited.add(m)

    resolution = manifestResolver.Resolve(m.TypeName, m.MethodName, m.ParamTypes)
    if resolution.Status != Unknown:
        seeds[m] = resolution.Effects; continue

    location = assemblyIndex.FindMethod(m)
    if location == null or !location.HasBody:
        incomplete.add(m); continue

    moveNext = stateMachineResolver.Redirect(location)
    if moveNext != null: location = moveNext

    (edges, hasCalli) = callGraphBuilder.ExtractCallEdges(location)
    if hasCalli: incomplete.add(m)
    forwardEdges[m] = edges

    for each edge in edges (sorted by Callee.ToString()):
        if edge.IsVirtual:
            if SkipInterfaces or UbiquitousInterfaces: continue
            impls = assemblyIndex.GetImplementations(edge.Callee)
            if impls.Count > MaxVirtualImplementations:
                incomplete.add(edge.Callee); continue
            for each impl: worklist.enqueue(impl.Key)
        else:
            worklist.enqueue(edge.Callee)
```

**Phase 2: Propagation** (Tarjan SCC + fixpoint)
```
Compute SCCs via Tarjan on forward edges.
Process in reverse topological order. Sort SCC members by MethodKey.

For single-method SCC:
  effects(m) = Union(effects of all callees)
  If any callee Incomplete → m is Incomplete

For multi-method SCC:
  Fixpoint: effects(m) = Union(all callee effects including peers)
  Convergence: monotone union over finite EffectSet lattice
  Bound: O(|SCC| × |lattice height|) — assert, not budget
```

### `ILEffectAnalyzer.cs` (~300 lines)
Orchestrator. Owns `AssemblyIndex`. `IDisposable`. In-memory cache keyed by full `MethodKey`. Name-only lookup unions all overloads.

### `CompilationContext.cs` (~30 lines)
Holds `SharedEffectResolver`. `IDisposable`. Constructed once per `CompileCalor.Execute()`, reused across all file compilations.

---

## Files to Modify

### `src/Calor.Compiler/Effects/EffectResolver.cs`
- **Constructor injection**: accept optional `ILEffectAnalyzer?` parameter.
- IL fallback in **all four** internal resolution methods (method, getter, setter, constructor), after namespace defaults, before returning Unknown.

### `src/Calor.Compiler/Effects/EffectEnforcementPass.cs`
- Accept `referencedAssemblyPaths` and `enableILAnalysis` parameters.
- When `options.Context?.SharedEffectResolver` is provided, use it directly instead of constructing a new resolver.
- Implement `IDisposable` to clean up analyzer resources.

### `src/Calor.Compiler/Program.cs`
- `CompilationOptions`: add `EnableILAnalysis` (bool), `ReferencedAssemblyPaths` (IReadOnlyList<string>?), `Context` (CompilationContext?).
- In enforcement pass construction: if `options.Context?.SharedEffectResolver` exists, pass it as the resolver. Otherwise construct fresh (existing behavior).

### `src/Calor.Compiler/Diagnostics/Diagnostic.cs`
- `Calor0414` — ILResolvedEffect (informational, verbose only)
- `Calor0415` — ILAnalysisFallback (informational)
- `Calor0416` — ILAnalysisBudgetExhausted (warning)

### `src/Calor.Tasks/CompileCalor.cs`
- New properties: `ITaskItem[] ReferencedAssemblies`, `bool EnableILAnalysis`, `string RuntimeDirectory`, `string NuGetPackageRoot`, `string DepsFilePath`.
- Before file loop: construct `CompilationContext` with shared `EffectResolver` + `ILEffectAnalyzer`.
- In file loop: pass `context` via `CompilationOptions.Context`.
- After file loop: `context?.Dispose()`.

### `src/Calor.Sdk/Sdk/Sdk.props`
- `<CalorEnableILAnalysis>false</CalorEnableILAnalysis>` default.

### `src/Calor.Sdk/Sdk/Sdk.targets`
- Pass `EnableILAnalysis`, `ReferencedAssemblies="@(ReferencePath)"`, `RuntimeDirectory`, `NuGetPackageRoot`, `DepsFilePath` to task.
- Conditional `DependsOnTargets="ResolveAssemblyReferences"`.

---

## Severity Story for IL-Resolved Effects

IL-resolved effects are **best-effort with known imprecision**. They must not silently become hard errors.

**Policy:**
- **Calor0410 (ForbiddenEffect):** Fires as normal regardless of resolution source.
- **Calor0414 (ILResolvedEffect):** Informational (verbose only) — tells user effect was discovered via IL.
- **Calor0411 (UnknownExternalCall):** Still fires for calls where IL analysis returned Incomplete. IL analysis reduces the count, never adds new errors.

---

## Incremental Build Cache Interaction

When IL analysis is enabled:
- `EnableILAnalysis` must be in `optionsHash`.
- A fingerprint of `@(ReferencePath)` (sorted paths + file sizes) must be in global invalidation check.
- **Follow-up change** to the incremental compilation feature — document in the IL analysis PR.

---

## Determinism

All processing is deterministic:
- **BFS:** FIFO queue; call targets sorted by `MethodKey.ToString()` before enqueuing.
- **Tarjan:** Type definitions in metadata token order; assemblies sorted by name.
- **Fixpoint:** SCC members in sorted `MethodKey` order.

---

## Test Strategy

### Test Assemblies (`tests/Calor.ILAnalysis.Tests/TestAssemblies/`)
8 small class library projects compiled to DLLs:
- **TestAssembly.DataAccess**: `Repo.Save()` → `DbCommand.ExecuteNonQuery()` chain
- **TestAssembly.Async**: async state machine with DB call
- **TestAssembly.Iterator**: `yield`-based method with effectful operations
- **TestAssembly.VirtualDispatch**: `IStore.Save()` with `SqlStore` + `FileStore`
- **TestAssembly.Delegates**: `Parallel.ForEach(items, item => repo.Save(item))` via ldftn
- **TestAssembly.DeepChain**: 20-hop chain to seed
- **TestAssembly.Circular**: Mutually recursive methods
- **TestAssembly.Overloads**: Same-arity overloads with different effects

### Test Scenarios (~32 tests)

| # | Category | Scenario | Expected |
|---|----------|----------|----------|
| 1 | Core | Direct call → seed | `db:w` |
| 2 | Core | Transitive 3-hop chain | `db:w` |
| 3 | Core | Pure method (complete graph) | `ResolvedPure` |
| 4 | Async | `[AsyncStateMachine]` → MoveNext | `db:w` |
| 5 | Async | Missing attribute (obfuscated) | `Incomplete` |
| 6 | Iterator | `[IteratorStateMachine]` → MoveNext | Resolved |
| 7 | Virtual | 1 implementation | `db:w` |
| 8 | Virtual | 2 implementations (union) | `db:w, fs:w` |
| 9 | Virtual | >threshold implementations | `Incomplete` |
| 10 | Virtual | SkipInterfaces (ILogger) | `null` |
| 11 | Virtual | UbiquitousInterfaces (IDisposable) | `null` |
| 12 | Delegate | ldftn → delegate creation | `db:w` |
| 13 | Delegate | calli instruction | `Incomplete` |
| 14 | Identity | Same-arity overloads distinguished | Correct effects per overload |
| 15 | Identity | Explicit interface impl | Distinguished |
| 16 | Identity | Generic method (`List<T>.Add`) | Resolved |
| 17 | Identity | Name-only lookup (no params) | Union of overloads |
| 18 | Assembly | Ref assembly detected | `Incomplete`, not false pure |
| 19 | Assembly | Ref→impl via deps.json | Loads impl, resolves effects |
| 20 | Assembly | BCL ref → runtime dir impl | Loads impl from shared runtime |
| 21 | Assembly | Real NuGet-resolved assembly (EFCore) | Resolves `SaveChanges` chain |
| 22 | Assembly | Malformed PE | Logged + skipped, no crash |
| 23 | Assembly | Unknown assembly / type | `null` gracefully |
| 24 | Assembly | Type name collision (2 assemblies) | Assembly-scoped resolution |
| 25 | Boundary | P/Invoke (no body) | `Incomplete` |
| 26 | Boundary | Depth limit exceeded | `Incomplete` + Calor0415 |
| 27 | Boundary | Budget exhausted (>10K methods) | `Incomplete` + Calor0416 |
| 28 | Soundness | Incomplete callee → Incomplete caller | Not false pure |
| 29 | Soundness | Circular A→B→A | Fixpoint converges (assert) |
| 30 | Integration | Full Calor compile with IL analysis | `db:w` propagated, no Calor0411 |
| 31 | Integration | Analyzer reuse across 10 files | PEReader constructed once |
| 32 | Integration | Empty `@(ReferencePath)` | No-op, no error |

---

## Implementation Order

1. **`ILAnalysisOptions.cs`** — config
2. **`MethodKey`** — full-signature identity with SRM decoders
3. **`AssemblyIndex.cs`** — two-phase loading, ref→impl resolution, type indexes, `IDisposable`
4. **`ILCallGraphBuilder.cs`** — IL decoding, all 6 opcodes, token resolution
5. **`StateMachineResolver.cs`** — async + iterator attribute decoding
6. **`TransitiveEffectPropagator.cs`** — BFS + Tarjan + fixpoint, three-state, deterministic
7. **`ILEffectAnalyzer.cs`** — orchestrator, cache, `IDisposable`
8. **`CompilationContext.cs`** — shared state container
9. **Modify `EffectResolver.cs`** — constructor injection, IL fallback in all 4 paths
10. **Modify `EffectEnforcementPass.cs`** — shared resolver support, `IDisposable`
11. **Modify `Program.cs`** — `CompilationOptions` changes
12. **Modify `Diagnostic.cs`** — Calor0414, 0415, 0416
13. **Test assemblies** — 8 fixture DLLs
14. **Unit + integration tests** — 32 tests
15. **Modify `CompileCalor.cs`** — task-level caching, new properties
16. **Modify `Sdk.props/targets`** — `@(ReferencePath)`, runtime dir, deps file, conditional depends

## Verification

1. `dotnet build` — 0 warnings, 0 errors
2. `dotnet test tests/Calor.ILAnalysis.Tests` — all 32 tests pass
3. `dotnet test` — no regression in existing tests
4. **Ref assembly test (test #21):** Real NuGet-resolved `Microsoft.EntityFrameworkCore.dll` → detect ref → resolve impl → trace `SaveChanges` chain
5. **Delegate test (test #12):** `Parallel.ForEach(items, item => repo.Save(item))` → `db:w`
6. **Soundness test (test #28):** Incomplete traces produce `null`, not false purity
7. **Performance benchmark:** Load realistic closure (EFCore + DI + Serilog resolved via MSBuild). Budget: type-table scan < 250ms, total analysis < 2s. If exceeded, profile before shipping.
8. **Manual E2E:** Calor project with `<CalorEnableILAnalysis>true</CalorEnableILAnalysis>` referencing a DAL DLL → `db:w` propagates through `UserRepository.Save()`
9. **Run the validation benchmark** (see below) and include results in PR description.

---

## Success Metrics and Objective Measurement

### Metric 1: Unknown Call Resolution Rate (Primary)

The core value proposition: how many previously-Unknown external calls does IL analysis resolve?

```
Resolution improvement = (Unknown_before - Unknown_after) / Unknown_before × 100%
```

**How to measure:** Add a `--diagnostics-summary` output to `Program.Compile` that reports effect resolution statistics: total external calls, manifest-resolved, IL-resolved, remaining Unknown. Compare runs with `EnableILAnalysis=false` vs `true`.

**Target corpus** (run against all of these):
| Project | Why it matters |
|---------|---------------|
| `dotnet new webapi` + EF Core + 3-layer (Controller→Service→Repository→DbContext) | Typical data access; concrete call chains |
| `dotnet new worker` + background job processor calling DB | Async-heavy; validates state machine resolution |
| Converted C# project from the conversion pipeline (if available) | Real-world Calor code |

**Expected outcome:** **40-60% reduction in Unknown calls** for concrete-call-chain-heavy projects. Interface-heavy projects (heavy DI, MediatR pipelines) will see lower improvement — those are covered by Tier B manifests.

**PR must include:** Resolution rate for each corpus project, before and after.

### Metric 2: False Purity Rate (Soundness — Zero Tolerance)

IL analysis must never declare a method pure when it has effects.

```
False purity rate = IL-resolved-as-pure methods that actually have effects / total IL-resolved methods
```

**How to measure:** For the test corpus, manually audit 50-100 IL-resolved methods. Compare IL-resolved effects against actual method behavior (code review of the referenced assembly source).

**Target:** **0%.** The three-state resolution guarantees this — Incomplete never reports pure. Any false purity is a bug, not a tuning issue.

### Metric 3: Over-Approximation Rate (Acceptable Imprecision)

IL analysis may over-report effects (e.g., virtual dispatch union includes effects from implementations the runtime doesn't hit).

```
Over-approximation rate = methods with over-reported effects / total IL-resolved methods with effects
```

**How to measure:** For the test corpus, compare IL-resolved effects against the ground truth (manual audit or runtime instrumentation).

**Target:** **< 15%.** Over-approximation is safe (user adds the declaration, or narrows with a manifest). Under-approximation is a bug.

### Metric 4: Build Time Overhead

```
IL analysis overhead = build_time(EnableILAnalysis=true) - build_time(EnableILAnalysis=false)
```

**How to measure:** Run `dotnet build` 10 iterations each way on the test corpus, report median.

**Target:**
| Project size | Budget |
|-------------|--------|
| < 50 `.calr` files | < 500ms overhead |
| 50-200 `.calr` files | < 2s overhead |

Analyzer is constructed once per build (not per file), so overhead scales with reference-closure size, not file count.

### Metric 5: Manifest Authoring Reduction

The practical developer benefit: fewer hand-written manifest entries needed.

```
Manifest lines saved = manifest_entries_to_eliminate_all_Calor0411(without IL) - manifest_entries_to_eliminate_all_Calor0411(with IL)
```

**How to measure:** For the test corpus, count manifest entries needed to eliminate all Calor0411 warnings, with and without IL analysis.

**Expected outcome:** For a project with a concrete data-access layer (repositories wrapping EF Core), IL analysis eliminates the need for manifests covering repository methods entirely — only leaf methods (DbCommand, DbContext) need manifests, which are already built-in. **50-100 fewer manifest entries** for a medium project.

### What We Should NOT Claim

- "Eliminates the need for manifests." — False. Interfaces, delegates, P/Invoke, and BCL internals are IL analysis boundaries.
- "Complete effect coverage." — False. Incomplete traces fall through to Unknown.
- "Zero build time impact." — False. Assembly loading has measurable cost. Opt-in for this reason.

---

## Validation Benchmark (`bench/ILAnalysisBench`)

Ships in the same PR. Creates a realistic test project and measures all 5 metrics.

### Structure

```
bench/ILAnalysisBench/
  ILAnalysisBench.csproj        Console app
  Program.cs                     Benchmark runner
  fixtures/
    WebApiProject/               dotnet new webapi + EF Core + 3-layer arch
      Controllers/UserController.calr
      Services/UserService.calr
      Repositories/UserRepository.calr
```

### What it does

1. **Setup:** Creates a temp project with `.calr` files that call a repository layer backed by EF Core. References `Microsoft.EntityFrameworkCore` and `System.Data.Common` via NuGet.
2. **Baseline run:** Compile all `.calr` files with `EnableILAnalysis=false`. Record: total external calls, Unknown count, build time.
3. **IL analysis run:** Compile with `EnableILAnalysis=true`. Record: total external calls, manifest-resolved count, IL-resolved count, remaining Unknown count, build time.
4. **Report:**

```
IL Analysis Validation Benchmark
================================

Corpus: WebApi + EF Core (3-layer)
  .calr files:       12
  Referenced assemblies: 47
  External calls:     83

Resolution Rates:
  Manifest-only:  34/83 resolved (41%), 49 Unknown
  With IL analysis: 62/83 resolved (75%), 21 Unknown
  IL-resolved:     28 additional methods
  Improvement:     57% reduction in Unknown calls

Soundness:
  IL-resolved pure:    14 methods
  IL-resolved effects: 28 methods
  False purity:        0 (audited 42/42)

Build Time (median of 10 runs):
  Without IL: 1,240ms
  With IL:    1,680ms
  Overhead:   440ms

Manifest Reduction:
  Entries needed without IL: 49
  Entries needed with IL:    21
  Saved:                     28 entries
```

5. **Acceptance criteria:**
   - Resolution improvement > 30% (conservative floor)
   - False purity = 0
   - Build overhead < 2s
   - Over-approximation rate < 15%

If any criterion fails, the implementation must be revised before merging. These numbers go in the PR description as the validated baseline.
