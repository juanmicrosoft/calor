# Plan: `calor effects suggest` CLI Command (v4)

**Updated:** 2026-04-20 — v3 + variable type resolution in v1, constructor/property detection, agent workflow, performance note.

## Context

When Calor code calls .NET types not covered by the built-in manifests, the compiler emits `Calor0411` (unknown external call) diagnostics. The `calor effects suggest` command analyzes the source AST, filters out internal calls, checks each external call against the manifest resolver, and generates a manifest template for unresolved types.

## Usage

```bash
calor effects suggest --input mycode.calr [--project .] [--output .calor-effects.suggested.json] [--merge] [--json]
```

- `--input / -i` (required): Calor source file(s) to analyze (accepts multiple)
- `--project / -p` (optional): Project directory for manifest loading context
- `--solution / -s` (optional): Solution directory
- `--output / -o` (optional): Output file path (default: `.calor-effects.suggested.json`)
- `--merge` (optional): Merge into existing manifest instead of writing separate file. Mutually exclusive with `--json`.
- `--json` (optional): Output raw JSON to stdout. Mutually exclusive with `--merge`.

Exit codes: 0 = success (suggestions written or all resolved), 1 = parse error, 2 = file write error.

## Agent Workflow

This command is designed for AI agents as the primary consumer. The integration loop:

1. Agent generates Calor code that calls .NET libraries
2. Compilation produces `Calor0411` (unknown external call) errors
3. Agent runs `calor effects suggest --input app.calr --json`
4. Agent parses the JSON output — each unresolved type/method is listed with empty `[]` effects
5. Agent fills in effects based on API documentation, source analysis, or convention (e.g., methods named `Save*` → `db:w`, methods named `Log*` → `cw`)
6. Agent writes the completed manifest to `.calor-effects.json`
7. Recompile — `Calor0411` errors gone

The `--json` flag produces machine-parseable output for step 3-4. Human users use the default console output with hints.

## Implementation

### Core pipeline (5 steps)

Inline the Lexer/Parser directly (same pattern as MCP `StructureTool`), don't use `Program.Compile()`:

```
Step 1: Lex + Parse → ModuleNode (per file)
Step 2: CallGraphAnalysis.Build(module) → internal function map (per file)
Step 3: ExternalCallCollector.Collect(module) → all (typeName, methodName, callKind) tuples
Step 4: Filter: remove internal calls (check against function map), then resolve remaining against EffectResolver → keep only Unknown
Step 5: Expand short names, group by type, output
```

For multiple files: union the function maps across all modules before filtering (step 4), so `a.calr`'s internal functions are recognized when processing `b.calr`'s calls.

### Step 1: Parse

```csharp
var diagnostics = new DiagnosticBag();
var lexer = new Lexer(source, filePath);
var tokens = lexer.Lex();
if (diagnostics.HasErrors) { /* report and exit 1 */ }
var parser = new Parser(tokens, diagnostics);
var module = parser.ParseModule();
if (diagnostics.HasErrors) { /* report and exit 1 */ }
```

### Step 2: Build internal function map

```csharp
var callGraph = CallGraphAnalysis.Build(module);
// callGraph.FunctionNameToId — top-level functions
// callGraph.MethodNameToIds — class methods
```

### Step 3: Collect external calls

Use the shared `ExternalCallCollector` (extracted from `InteropEffectCoverageCalculator`). Extended to walk:
- `module.Functions[*].Body`
- `module.Classes[*].Methods[*].Body`
- `module.Classes[*].Constructors[*].Body`

Each collected call is tagged with its kind:
```csharp
enum CallKind { Method, Constructor, Getter, Setter }
record CollectedCall(string TypeName, string MethodName, CallKind Kind);
```

**Call kind detection from AST nodes:**
- `CallStatementNode` / `CallExpressionNode` → `CallKind.Method`
- `NewExpressionNode` (§NEW) → `CallKind.Constructor` (type = `NewExpressionNode.TypeName`)
- Call targets ending in `.get_*` pattern → `CallKind.Getter` (strip `get_` prefix for property name)
- Call targets ending in `.set_*` pattern → `CallKind.Setter` (strip `set_` prefix for property name)
- For cases where the AST doesn't distinguish (e.g., property access looks like a method call), default to `CallKind.Method` — the user can move entries to the correct field during review

**Variable type resolution (in v1, not deferred):**

Extract `ResolveVariableType` from `EffectEnforcementPass` (lines 695-730) into the shared `ExternalCallCollector`. When collecting calls from a function body, first scan `BindStatementNode` entries for `NewExpressionNode` initializers to build a `variableName → typeName` map:

```csharp
// §B{client} §NEW{HttpClient} → "client" maps to "HttpClient"
if (bind.Initializer is NewExpressionNode newExpr)
    variableTypeMap[bind.Name] = MapShortTypeNameToFullName(newExpr.TypeName);
```

When a call target's type part (e.g., `client` in `client.GetAsync`) matches a variable in the map, substitute the resolved type before emitting. This converts `("client", "GetAsync")` → `("System.Net.Http.HttpClient", "GetAsync")`.

This reuses the same logic already in the enforcement pass — extracting it into the shared collector means both the enforcement pass and the suggest command resolve variable types consistently.

### Step 4: Filter internal calls + resolve

```csharp
var unresolvedExternal = allCalls
    .Where(call => !IsInternalCall(call.Target, callGraph.FunctionNameToId, callGraph.MethodNameToIds))
    .Where(call => resolver.Resolve(call.TypeName, call.MethodName).Status == EffectResolutionStatus.Unknown)
    .Distinct()
    .ToList();
```

`IsInternalCall` mirrors `FindInternalFunctionByName` from the enforcement pass: check bare name against `FunctionNameToId`, for dotted names extract the method part and check `MethodNameToIds`.

### Step 5: Build manifest and output

- Expand short names via `MapShortTypeNameToFullName()`
- Flag likely variable names (lowercase, not in type mapping) with console warning
- Group by type, place in correct `TypeMapping` field (`Methods`, `Constructors`, `Getters`, `Setters`) based on `CallKind`
- Empty `[]` for each entry — user fills in effects

### Merge mode (`--merge`)

When merging into an existing manifest:
- Load existing manifest
- Preserve ALL fields: `Version`, `Description`, `Confidence`, `GeneratedBy`, `GeneratedAt`, `Library`, `LibraryVersion`, `NamespaceDefaults`
- Merge `Mappings` at method level:
  - Existing methods with non-empty effects → never overwritten
  - Existing methods with `[]` → preserved
  - New methods → added with `[]`
  - New types → added as new `TypeMapping`
- Never remove entries (additive only)

### Console output

```
Analyzing mycode.calr...
Found 4 unresolved external calls across 2 types:

  MyApp.Services.OrderService
    ProcessOrder    → []  (fill in effects)
    CancelOrder     → []  (fill in effects)

  client  (⚠ likely a variable name — replace with the actual type)
    GetAsync        → []  (fill in effects)
    SetAsync        → []  (fill in effects)

Written to .calor-effects.suggested.json

Hint: Common effects are cw (console), fs:r/fs:w (file), db:r/db:w (database),
      net:r/net:w (network), mut (mutation), rand (random), time (system time).
      Use [] for pure methods with no side effects.
```

When all calls resolve: `"All external calls are resolved. No supplemental manifest needed."` — no file written.

## Performance

The pipeline is lightweight — no binding, enforcement, or codegen:
- Lex + Parse: ~10ms per 1,000-line file (existing benchmark)
- `CallGraphAnalysis.Build`: single AST walk, O(functions + methods)
- `ExternalCallCollector.Collect`: single AST walk, O(statements)
- `EffectResolver.Resolve` per call: O(1) dictionary lookup (cached)
- Variable type map construction: O(bind statements) per function

For a 10,000-line file with 500 external calls: expected <200ms total. No performance gate needed — this is inherently fast because it skips the expensive phases (SCC fixpoint, Z3 verification, codegen).

## Files to modify

| File | Change |
|------|--------|
| `src/Calor.Compiler/Commands/EffectsCommand.cs` | Add `CreateSuggestCommand()`, `ExecuteSuggest()`, `BuildTypeMappings()`, merge logic |
| `src/Calor.Compiler/Effects/ExternalCallCollector.cs` (new) | Shared AST call collector with class/constructor walking, call kind tagging |
| `src/Calor.Compiler/Evaluation/Metrics/InteropEffectCoverageCalculator.cs` | Replace inline collector with shared `ExternalCallCollector` |
| `tests/Calor.Enforcement.Tests/EffectsSuggestTests.cs` (new) | Tests |

## Tests

| Test | What it verifies |
|------|-----------------|
| `BuildTypeMappings_GroupsByType` | [(A, foo), (A, bar), (B, baz)] → 2 TypeMappings |
| `BuildTypeMappings_DeduplicatesCalls` | Same (type, method) 3x → 1 entry |
| `BuildTypeMappings_ExpandsShortNames` | "Console" → "System.Console" |
| `BuildTypeMappings_PlacesConstructorsCorrectly` | Constructor calls → `Constructors` field, not `Methods` |
| `InternalClassMethod_NotIncludedInSuggestions` | §CLS with method + external call → only external appears |
| `InternalFunction_NotIncludedInSuggestions` | §F internal function called → not in output |
| `VariableType_ResolvedViaNewExpression` | §B{client} §NEW{HttpClient} + §C{client.GetAsync} → type = "System.Net.Http.HttpClient" |
| `VariableName_UnresolvableFlagged` | Variable without §NEW (e.g., DI-injected) → warning flag |
| `Merge_PreservesExistingEffects` | Existing `["db:w"]` not overwritten |
| `Merge_AddsNewMethods` | New method added, existing untouched |
| `Merge_PreservesAllMetadata` | Description, Confidence, NamespaceDefaults preserved |
| `AllResolved_NoOutput` | All calls resolve → message, no file |
| `ParseErrors_ExitCode1` | Invalid source → exit 1, no manifest |
| `MultipleFiles_UnionFunctionMaps` | Internal func in file A recognized when processing file B |

## Verification

1. Create Calor file with internal class + external calls → only external calls in output
2. Run `calor effects suggest --input test.calr` → valid `.calor-effects.suggested.json`
3. Copy to `.calor-effects.json`, fill in effects, re-compile → zero Calor0411
4. Run `calor effects validate` on generated manifest → pass
5. Test `--merge` → existing entries preserved
6. Test no-external-calls file → "all resolved" message
7. Test parse-error file → exit code 1
8. All existing tests pass
