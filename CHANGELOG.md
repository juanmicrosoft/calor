# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [0.4.7] - 2026-04-20

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Static analysis for class members** — The `--analyze` flag now examines methods, constructors, property accessors, operators, indexers, and event accessors (previously only top-level functions were analyzed)
- **Verification-gated reporting** — `--analyze` only reports proven findings by default (Z3-confirmed or constant analysis); use `--all-findings` for lower-confidence results
- **Taint hop-count tracking** — Taint analysis tracks propagation steps; single-hop parameter-to-sink flows filtered by default to reduce false positives
- **Bug pattern detection in class members** — Division by zero, null dereference, integer overflow, off-by-one, path traversal, command injection, and SQL injection detection now covers all class member bodies
- **ScopeRestorer RAII pattern** — Eliminates scope corruption risk from 14+ manual try/finally blocks in the Binder
- **Arity-aware overload resolution** — `Scope.LookupByArity` resolves correct overload by argument count, preventing wrong return types from flowing into Z3
- **Static context enforcement** — `this` expression not bindable in static methods and operators
- **Nested class scope isolation** — Inner classes don't inherit outer class fields
- **Constructor initializer binding** — `: base()`/`: this()` arguments visible to bug pattern checkers
- **BoundConditionalExpression** — Ternary expressions preserve all three branches for analysis (was returning only the true branch)
- **33 new unit tests** for class member binding, scope, overloads, dataflow, and end-to-end analysis
- **New `--all-findings` CLI flag** for showing all analysis findings including inconclusive results
- **New documentation page** (`/cli/static-analysis/`) documenting the analysis pipeline, finding types, and real-world results

### Fixed
- **False positive elimination** — Unhandled expression types (cast, array length, indexer, etc.) return opaque expressions instead of `BoundIntLiteral(0)`, eliminating the entire class of false division-by-zero reports
- **DEC literal misparse** — Decimal literals (`DEC:100`) now bind to `BoundFloatLiteral` instead of falling to zero-literal fallback
- **Assignment LHS not counted as use** — `x = 1` no longer reports `x` as "used before write" in dataflow analysis
- **Multi-statement sync blocks** — Lock bodies now preserved for analysis (was dropping all statements)
- **this.field shadowing** — `this.field` resolves from class scope, not method scope (prevents parameter shadowing field)
- **Throw-to-catch CFG edges** — Throw statements inside try blocks now flow to catch blocks instead of function exit
- **Using exception path** — Using statements modeled as try/finally with dispose on exception path
- **DeclaredEffects pass-through** — `VerificationAnalysisPass` now passes function effects to `TaintAnalysis` (was missing)

### Validated
- **47 open-source projects scanned** — 23 verified findings across 8 projects, 27 projects clean (zero findings), ~90% true positive rate
- **Real findings**: ILSpy null dereferences, FluentFTP path traversal, ASP.NET Core path traversal, Mapster unsafe unwraps, Avalonia nullable unwraps

## [0.4.6] - 2026-04-18

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Effect system: .NET framework manifests** — Tier B effect manifests for 30+ common .NET framework interfaces (ILogger, DbContext, IConfiguration, HttpClient, ControllerBase, etc.) enabling the compiler to enforce correct effect declarations when Calor code calls framework types
- **Effect system: ecosystem library manifests** — Manifests for Serilog, Newtonsoft.Json, Dapper, MediatR, AutoMapper, FluentValidation, Polly
- **Effect system: BCL manifest expansion** — New manifests for System.Text.Json, Regex, Concurrent collections, Crypto types
- **Effect system: variable type resolution** — Enforcement pass resolves instance method calls via §NEW initializer tracking (e.g., §B{r} §NEW{Random} → r.Next resolves to rand)
- **Effect system: structured type info** — BoundCallExpression now carries ResolvedTypeName and ResolvedMethodName from the binder
- **Effect system: centralized type mapping** — MapShortTypeNameToFullName with 65+ type name mappings across BCL, framework, and ecosystem types
- 95 new enforcement tests (210 total)

### Fixed
- **Effect system: unified resolver** — Consolidated three parallel effect systems (BuiltInEffects, EffectsCatalog, EffectChecker.KnownEffects) into a single manifest-based resolver
- **Parser: compound effect codes** — Fixed §E{db:r,cw,env:r} silently mis-parsing the third compound code when colon-delimited effects are chained with commas
- **EffectCodes.ToCompact: missing mappings** — Added environment_read→env:r, database_write→db:w, heap_write→mut and other internal-to-surface code conversions
- **Enforcement: collection mutations** — Added CollectionPushNode, DictionaryPutNode, CollectionRemoveNode, etc. to the enforcement pass (→ mut effect)
- **Converter: effect declaration format** — Fixed converter emitting internal values (environment_read) instead of surface codes (env:r) in §E declarations

### Removed
- `BuiltInEffects.cs` — ~204 hardcoded entries migrated to manifest JSON files
- `EffectsCatalog.cs` — Intermediate layer removed; EffectResolver handles all resolution
- `EffectChecker` class — Legacy checker replaced by EffectEnforcementPass; shared types moved to EffectTypes.cs

## [0.4.5] - 2026-04-14

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **Phase 11-12 — 49→0 conversion failures** — Achieved 100% compilation success across 38,932 .calr files (13,831 roslyn + 25,101 dotnet). Key fixes: iterative or-pattern parsing for stack overflow prevention, lambda multi-line format for FallbackCommentNode, §CS{} raw C# fallback for unconvertible call targets, HasEndNewBeforeEndCall nesting depth tracking, missing Lisp expression tokens, PLIST REST attribute consumption, TypeMapper array bracket normalization, hex→decimal integer emission, literal keyword escaping, empty array conversion, tuple support in Lisp arguments, PascalCase operator recovery, positional type patterns, bracket depth tracking in ParseValue, dotted reference raw call handling

## [0.4.4] - 2026-04-10

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **Phase 6A — WHERE constraints** — Normalize multiline Roslyn type names in WHERE constraints; add `?`, `*`, `[]`, `.` handling in ParseValue generic loops; strip `@` from ANON/NEW property names; strip `!` null-forgiving from target strings; sanitize backtick from module names
- **Phase 6B+C — WHERE dot-nested** — Handle `Type<T>.NestedType` in WHERE constraint parser; fix ANON implicit property names; strip `global::` from enum values; add HSET hoisting
- **Phase 6D — ulong literals** — Add ulong fallback for integers > long.MaxValue; fix `§VAR{}` detection in tuple pattern arms
- **Phase 6E — array ID mismatch** — Empty arrays emit with explicit size 0; fix match expression multi-line indentation; simplify `delegate*` types to `nint` in attribute blocks

## [0.4.3] - 2026-04-08

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **51-project conversion campaign** — Phase 3-5 fixes addressing ~80 additional conversion failures across array ID mismatches, dictionary hoisting, enum parsing, empty method bodies, generic calls in statement position, and §ARR2D dimension hoisting
- **Enum cast/paren ambiguity** — Parenthesized hex enum values like `(0x0001)` no longer misinterpreted as type casts
- **Collection nodes in match arms** — List, dictionary, and set creation in switch expression arms now use block syntax
- **Call statement argument hoisting** — Complex arguments with section markers are hoisted in statement-level calls

## [0.4.2] - 2026-04-02

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Changes

## [0.4.1] - 2026-03-15

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **List pattern conversion** — Full C# → Calor round-trip support for list/slice patterns (`[var a, ..var rest, var b]`) with correct slice position tracking (start/middle/end) via `SliceIndex` on `ListPatternNode`
- **UTF-8 string literals** — Round-trip support for `"hello"u8` via `IsUtf8` property on `StringLiteralNode`, with lexer/parser/emitter changes

### Fixed
- **Slice position correctness** — `[var first, .., var last]` now correctly preserves the slice position instead of always appending at end; bare `..` emits without spurious `var _` binding
- **Unknown feature default** — `FeatureSupport.GetSupportLevel` now returns `NotSupported` for unregistered features, preventing silent suppression of blockers in `MigrationAnalyzer`
- **PostConversionFixer CRLF handling** — Orphaned closing tag regex now handles Windows `\r\n` line endings correctly
- **SelfTest span offset consistency** — Input line endings normalized before compilation so span offsets match golden files across platforms
- **ClaudeInitializer test isolation** — All test instances now use `ClaudeJsonPathOverride` to prevent race conditions writing to `~/.claude.json`
- **Parser u8 stripping** — Defensive stripping of `u8` suffix from string literal values if lexer includes it

## [0.4.0] - 2026-03-09

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Copilot instructions** — Ported Claude `/confidence` and `/create-release` skills to `.github/instructions/` for GitHub Copilot compatibility
- **MCP cancellation token propagation** — All MCP tool `ExecuteAsync` methods now accept and propagate `CancellationToken` for proper request cancellation

### Fixed
- **§ERR fallback emission** — Unsupported C# constructs now emit parseable `§ERR "TODO: ..."` tokens instead of unparseable `§ERR{...}` brace format
- **Named argument round-trip** — Named arguments in converter output now use correct `name: value` syntax that parses back cleanly
- **Unicode escape sequences** — `\Uxxxxxxxx` 8-digit Unicode escapes now handled correctly in string literals
- **Ternary decomposition** — Ternary expressions (`a ? b : c`) now decompose to `§IF` expression form instead of statement form, fixing 26+ Calor0104 errors across real-world codebases
- **Doc comment carriage return leaks** — `\r` characters stripped from XML doc comments during conversion, preventing broken `//` comment prefixes
- **§ markers in Lisp expressions** — Binary/unary operations with §-containing operands (calls, ternaries) now hoist to temp vars, preventing Calor0114 parse errors inside `(op arg1 arg2)` expressions
- **Empty §ASSIGN for collections** — Collection creation (List, Dict, Set, Array) as assignment RHS now emits the collection block with the target name directly, instead of empty `§ASSIGN` statements

### Converter Quality Improvements
- **Newtonsoft.Json**: 54.0% → **100%** compile rate (240 files)
- **Humanizer**: 86.1% → **99%** compile rate (100-file sample)
- **PowerShell**: All 14 reported blockers resolved; 200-file sample at **100%** clean conversion

## [0.3.8] - 2026-03-05

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **§SYNC lock statement** — Native `§SYNC{id} (expr) ... §/SYNC{id}` syntax compiling to `lock(expr) { body }` with full round-trip support; follows complete AST node checklist (token, lexer, AST, 5 visitors, parser, converter)
- **Verbatim identifier mapping** — C# `@keyword` identifiers mapped to backtick syntax (`` `keyword` ``) in Calor via `EscapeCalorIdentifier` helper at 8+ emission points; round-trips correctly to `@keyword` in C# output
- **Conditional usings in §PP** — `§U` directives inside `§PP` preprocessor blocks at module level; `TypePreprocessorBlockNode` extended with `Usings` property for both active and disabled preprocessor branches
- **MCP tool consolidation** — 34 MCP tools consolidated to 13 focused tools (`calor_help`, `calor_navigate`, `calor_structure`, `calor_check`, `calor_fix`, `calor_migrate`, `calor_refine`, `calor_batch`); improves discoverability and reduces tool selection confusion
- **`calor_fix` auto-repair tool** — New MCP tool that diagnoses and auto-applies fixes for common Calor compilation errors
- **`calor_migrate` tool** — Unified migration workflow combining convert, validate, and fix in a single tool
- **Primary constructor synthesis** — Primary constructor parameters converted to fields with proper constructor initialization
- **Tuple type and expression parsing** — Full support for C# tuple types `(int, string)` and tuple literals in converter
- **Event accessor bodies** — `add`/`remove` accessor bodies in event definitions now converted and emitted correctly
- **Nested delegate support** — `§DEL` delegate definitions inside class bodies
- **Goto case/default** — `goto case` and `goto default` converted to `§GOTO{CASE:value}` / `§GOTO{DEFAULT}` with documentation in MCP

### Fixed
- **String interpolation lexing** — Brace-depth tracking prevents premature close on `{` inside interpolated strings
- **Null coalescing operator** — `??` operator properly supported in converter and emitter
- **Null-conditional access** — `?.` chains correctly decomposed during conversion
- **Nullable lambda parameters** — `Func<int?>` and nullable types in lambda signatures emit correctly
- **Unsigned numeric literals** — `0u`, `0UL` etc. parsed and emitted correctly
- **Operator precedence** — Fixed parenthesization in complex expressions during conversion
- **Target-typed new** — `new()` infers type from context instead of emitting `NEW{object}`
- **MCP memory pressure** — Wait-and-retry with backoff instead of immediate rejection; concurrency scaled with CPU count
- **Feature discoverability** — MCP tool output now includes feature support status and workarounds inline

## [0.3.7] - 2026-03-02

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **String interpolation with embedded calls** — CalorEmitter now uses function-call syntax inside `${...}` instead of `§C` tags which were treated as literal text by the parser; fixes 29-file Humanizer conversion blocker
- **OperatorOverloadNode parameter emission** — CSharpEmitter now uses `Visit(p)` for operator overload parameters, preserving ref/out/in/params modifiers
- **Interpolation-safe expression emission** — `NewExpression`, `AwaitExpression`, and `ArrayAccess` now emit C#-style syntax inside `${...}` interpolation contexts instead of `§`-prefixed section markers

### Added
- **Batch conversion validation** — `calor_batch_convert` MCP tool now supports `validate` parameter that parses and compiles each converted file, catching false-positive successes
- **C#-to-Calor conversion guide** — Skills documentation now includes common conversion patterns (interpolation, ternary, ref/out, chained calls) for agent guidance
- **Ternary expression syntax entry** — `calor-syntax-documentation.json` now includes `(? condition trueValue falseValue)` with examples
- **3 new conversion test snapshots** — InterpolationWithMethodCall (12-01, round-trip verified), RefOutParameters (12-02), OperatorOverloadWithModifiers (12-03, round-trip verified)

## [0.3.6] - 2026-03-01

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **Complete C# keyword sanitization** — `SanitizeIdentifier()` expanded from 27 to 50+ reserved keywords (`as`, `is`, `in`, `event`, `lock`, `delegate`, `checked`, `yield`, `out`, `ref`, `volatile`, `abstract`, `override`, `sealed`, `virtual`, `async`, `await`, `typeof`, `sizeof`, `unchecked`, `unsafe`, `fixed`, `foreach`, `goto`, `throw`, `try`, `catch`, `finally`, `explicit`, `implicit`, `extern`, `operator`, `params`, `readonly`, `stackalloc`, `const`, `var`, `dynamic`, `nameof`, `when`); prevents invalid C# in 5–15% of converted files
- **Call expression leading dot** — `§C{.Method}` now correctly emits `this.Method()` instead of invalid `.Method()`
- **Converter module ID consistency** — Module ID is always `m001` instead of inconsistent IDs like `m044` caused by shared counter increment during child node conversion
- **Interop block namespace duplication** — Use `ToString()` instead of `ToFullString()` for nodes inside namespaces to prevent namespace trivia bleeding into interop blocks
- **Switch enum value prefix** — Heuristic to detect enum type from qualified case labels and qualify bare identifiers (from `using static`) in switch expressions and statements

### Added
- **Batch convert chunking** — `calor_batch_convert` MCP tool now supports `maxFiles`, `offset`, `directoryFilter`, and `skipConverted` parameters for converting large projects in manageable chunks
- **Compile tool batch mode** — `calor_compile` MCP tool now accepts `files` (string array) and `projectPath` (directory) for batch compilation in a single call instead of 200+ individual calls
- **Diagnose tool auto-apply** — `calor_diagnose` MCP tool now supports `apply` parameter to automatically apply fix edits and return `fixedSource` alongside diagnostics, eliminating one round-trip per diagnostic cycle
- **CSharp minimize tool** — New `calor_csharp_minimize` MCP tool analyzes `§CSHARP` interop blocks and suggests which constructs could be native Calor, using Roslyn parsing and FeatureSupport registry cross-reference
- **Volatile keyword support** — `volatile` modifier is now fully supported for fields: `MethodModifiers.Volatile` flag, converter detection, parser recognition (`volatile`/`vol`), emitter output; `FeatureSupport` updated from `NotSupported` to `Full`

## [0.3.5] - 2026-02-27

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Relational/compound `is` patterns** — `x is > 5 or < 3` now converts to equivalent boolean expressions `(|| (> x 5) (< x 3))` instead of falling back to §CSHARP; supports `and`, `or`, `not`, parenthesized, and nested pattern combinations
- **Type-level preprocessor blocks** — `#if`-wrapped entire type declarations (class, interface, enum) at module level are now converted to `§PP` blocks; handles disabled branches where Roslyn excludes types from the syntax tree
- **Enum visibility modifiers** — Enums now support `public`, `internal`, `private`, `protected` visibility via `§EN{id:Name:vis}` syntax instead of hardcoded `public`
- **Nested type declarations** — Classes, structs, records, interfaces, and enums defined inside other types are now parsed, converted, and emitted correctly
- **Extended dictionary initializer support** — `SortedDictionary`, `ConcurrentDictionary`, `FrozenDictionary`, `ImmutableDictionary`, and `ImmutableSortedDictionary` now use the same initializer conversion as `Dictionary`
- **5 new conversion snapshot tests** — Relational patterns, internal enums, nested types, preprocessor-wrapped types, and dictionary initializers
- **Feature registry entries** — Added `dictionary-initializer`, `list-initializer`, `hashset-initializer`, `nested-type` to FeatureSupport; updated `relational-pattern` and `compound-pattern` from NotSupported to Full

## [0.3.4] - 2026-02-26

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Preprocessor directive conversion** — C# `#if`/`#elif`/`#else`/`#endif` directives are now converted to Calor `§PP` blocks by extracting Roslyn trivia; handles nested `#if` and `#elif` chains as nested `§PP` nodes
- **MCP preprocessor discoverability** — `FeatureSupport` entry, `SyntaxHelpTool` aliases, `AGENTS.md` template row, and `calor-language-skills.md` section for `§PP`
- **Refinement types** — `§RTYPE{id:Name:base} (predicate)` for named refinement types, inline `§I{type:param} | (predicate)` on parameters, `§PROOF{id:desc} (expr)` for proof obligations, and `#` self-reference in predicates
- **Obligation engine** — Z3-powered verification pipeline: obligation generation, assume-negate-check solving, guard discovery, and configurable policies (default, strict, permissive)
- **5 MCP agent guidance tools** — `calor_obligations` (verify obligations), `calor_suggest_types` (detect parameters needing refinements), `calor_discover_guards` (Z3-validated fix suggestions), `calor_suggest_fixes` (ranked fix strategies), `calor_diagnose_refinement` (all-in-one repair loop)
- **Obligation policy** — Configurable per-status actions (Ignore, WarnOnly, WarnAndGuard, AlwaysGuard, Error) with three built-in policies
- **101 new tests** — Refinement type parsing, obligation solving, guard discovery, MCP tool integration, and Z3 self-reference resolution

### Fixed
- **Lock/checked body ordering** — Comment annotations now correctly appear before body statements instead of after
- **Non-standard for-loop fallback** — Multi-variable declarations and expression initializers now emit in correct order; multi-incrementor patterns detected as non-standard

## [0.3.3] - 2026-02-25

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **PostConversionFixer** — Auto-fix 6 known invalid converter output patterns: orphaned closing tags, unmatched parentheses, comma leaks, generic `<T>` in Lisp position, inline `§ERR`/`§LAM` extraction, missing IF `→` arrow (#474)
- **`calor_convert_validated` MCP tool** — Single-call pipeline chaining convert → auto-fix → diagnose → compat-check with stage-based error reporting (#474)
- **Blocker classification** — `calor_analyze_convertibility` now classifies blockers as `language_unsupported` vs `converter_not_implemented` with summary counts (#474)
- **Complex composed examples** — 5 real-world examples in calor-language-skills.md (3 generated by the converter from real C# input, all parser-validated) (#474)

### Fixed
- **CommaLeaks false-positive** — Fix regex that was stripping commas from inline signatures, breaking valid converter output (#474)
- **Converter auto-fix integration** — ConvertTool now attempts PostConversionFixer before reporting parse errors, recovering from known converter bugs (#474)

## [0.3.2] - 2026-02-24

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.31x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins)
  - ErrorDetection: 1.83x (Calor wins)
  - EditPrecision: 1.39x (Calor wins)
  - RefactoringStability: 1.30x (Calor wins)
  - Correctness: 1.30x (Calor wins)
- **Programs Tested**: 207

### Added
- **Compact syntax Phase 1** — Auto-props, optional IDs, and inline signatures reduce Calor boilerplate (#445)
- **Default parameter values** — Emit and parse default parameter values in Calor syntax (#460)
- **6 language gap features** — Address 6 C# constructs from tracking issue #325: unsafe/fixed/stackalloc blocks, tuple types, multi-dimensional arrays, Parallel LINQ, COM interop fallback, Span<T> (#457)
- **Bitwise attribute expressions** — Full support for bitwise OR (`|`), AND (`&`), complement (`~`), and parenthesized expressions in attribute arguments (#449, #453)
- **Expanded benchmark suite** — Grow from 40 to 207 programs across 14 categories (#452)
- **Return type inference for `new()`** — Infer target type for `new()` in local functions and async methods (#466)
- **EdgeCaseCoverageAnalyzer** — New analyzer for edge case coverage and correctness estimation (#442)
- **MCP tools for edit precision** — Add call graph analysis tools for refactoring impact (#446)
- **2.0x comprehension ratio** — Proportional metrics and LLM evaluation reach 2.0x AI comprehension ratio (#447)

### Fixed
- **Ternary throw hoisting** — Hoist ternary throw expressions to guard statements (#459)
- **Option<T>/Result<T,E> converter** — Per-member fallback for `ConvertStruct` and `InferTargetType` for return context (#458)
- **Null-coalescing throw** — Convert `?? throw` to if-null-throw guard instead of `§ERR` (#451)
- **Non-throwable literal wrapping** — Wrap non-throwable literals in `System.Exception` for `§TH` codegen (#450)
- **CalorFormatter coverage** — Handle all 23 missing expression types in `FormatExpression` (#464)
- **Self-referential runtime reference** — Prevent `Calor.Runtime` from referencing itself; document dotted module names (#463)
- **Dotted-name round-trip** — Document and test dotted-name round-trip behavior (#462)
- **Constructor overloading** — Close Challenge 8; constructor overloading was already supported (#456)
- **Benchmark structure scoring** — Remove artificial parameters dependency in `CalculateCalorStructureScore` (#454)
- **License attribution** — Fix website footer to show Apache 2.0 instead of MIT (#443)

## [0.3.1] - 2026-02-23

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.27x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.70x (Calor wins, large effect d=1.39)
  - ErrorDetection: 1.65x (Calor wins, large effect d=1.62)
  - EditPrecision: 1.37x (Calor wins, large effect d=11.80)
  - RefactoringStability: 1.37x (Calor wins, large effect d=4.36)
  - Correctness: 1.26x (Calor wins, large effect d=1.06)
- **Programs Tested**: 40

### Added
- **Proportional comprehension scoring** — Replace boolean presence checks with log2 diminishing returns formula; files with more contracts/effects now score proportionally higher
- **Contract-depth and effect-specificity scoring** — Bonus for pre+post contract completeness, effect specificity (comma-separated effects), and matched open/close ID pairs
- **LLM-based comprehension evaluation** — Claude API integration with LLM-as-judge scoring via `--llm` flag; loads curated questions, falls back to structural generation
- **`calor_explain_error` MCP tool** — Matches compiler errors to 10 common mistake patterns with fix examples and correct syntax
- **DiagnoseTool error guidance** — Enriches diagnostics with `commonMistake` field when compiler has no specific fix suggestion
- **Expanded question bank** — 105 comprehension questions across all 36 benchmark programs (up from 13 across 4)
- **Pre-compiled regexes** — All comprehension scoring regexes compiled at class load time for 250+ program scalability
- **CI LLM comprehension workflow** — GitHub Actions step runs LLM evaluation with `ANTHROPIC_API_KEY` secret

## [0.3.0] - 2026-02-22

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.20x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - ErrorDetection: 1.65x (Calor wins, large effect)
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **C# interop blocks** — `§CSHARP{...}§/CSHARP` syntax for embedding raw C# at module/class scope, enabling incremental migration of unsupported constructs
- **Interop conversion mode** — Converter wraps unsupported members in `§CSHARP` blocks instead of TODO comments, producing `.calr` files that round-trip to valid C#
- **Convertibility analysis tool** — `calor_analyze_convertibility` MCP tool and `calor analyze-convertibility` CLI command for assessing C# file migration readiness
- **Round-trip test harness** — Automated C# → Calor → C# pipeline with test result comparison for validating conversion fidelity
- **Bug detection improvements** — Off-by-one checker and precondition suggester for enhanced static analysis
- **Contract inference pass** — Automatic inference of contracts from code patterns
- **Migrate workflow enhancements** — Analyze and verify phases added to `calor migrate` command
- **Syntax help telemetry** — Track which syntax features agents query most to prioritize documentation

### Fixed
- **Agent benchmark docs** — Improved CLAUDE.md syntax reference fixing 12 failing benchmark tasks across 8 categories (86.5% → 100% pass rate): while loops, switch/pattern matching, events, implication operator (`->` not `implies`), async return types, StringBuilder operators, block lambdas, multi-effect declarations
- **async-004 task prompt** — Fixed misleading "network read effect" to "network effect" (HttpClient needs `net:rw`)

## [0.2.9] - 2026-02-21

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **Unsupported feature telemetry** — Track unsupported C# constructs (goto, unsafe, etc.) in Application Insights during conversion, enabling data-driven prioritization of converter improvements
- **Pattern combinators** — `not`, `or`, and `and` pattern combinators and negated type patterns in C# converter
- **Collection spread-only conversion** — Spread expressions and fluent chain-on-new hoisting in converter
- **Required modifier and partial methods** — Support for `required` property modifier and partial method declarations
- **Delegate emission** — Delegate types, parameter attributes, and generic interface overloads in converter
- **Named arguments and tuple literals** — Named arguments, tuple literals, getter-only properties, and verbatim strings
- **Primary constructor parameters** — C# 12 primary constructors converted to readonly fields
- **`notnull` generic constraint** — Support for `notnull` constraint and static lambda conversion
- **Permissive effect inference** — New mode for converted code to avoid strict effect enforcement on generated output

### Fixed
- **Converter**: null-coalescing `??` → conditional (not arithmetic), declaration pattern variable binding, `out var` support, method groups, explicit interface implementations, target-typed new inference, cast-then-call chains, `protected internal`, `unchecked` blocks, default parameters, chained assignments, `typeof`, `lock`, lambda assignment, expression-bodied constructors, `int.MaxValue`, `ValueTask`, empty `[]`, static properties
- **Diagnostics**: Broke monolithic `Calor0100` (UnexpectedToken) into 6 specific error codes for clearer error messages
- **Parser**: `§HAS`/`§IDX`/`§LEN`/`§CNT` inside lisp expressions, tuple deconstruction, generic static access, variance modifiers, interface type params
- **Converter hoisting**: Chain bindings hoisted before `if` conditions, `§NEW` args hoisted to temp vars

## [0.2.8] - 2026-02-21

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **C# to Calor Conversion Campaign** — Converted 30 C# sample projects, producing 54 recommendations and 18 merged fixes
- **Cross-class method call effect inference** — Dotted call targets like `_calculator.Add` now resolve to internal functions for effect propagation, with name collision detection via multi-map
- **Local function support in converter** — C# local functions are hoisted to module-level `§F` functions during conversion
- **`§HAS`/`§IDX`/`§CNT`/`§LEN` inside lisp expressions** — Collection operations can now appear as arguments in prefix expressions like `(+ val §IDX arr 1)`
- **LINQ extension method effect recognition** — Common LINQ methods (Where, Select, OrderBy, ToList, etc.) recognized as pure in effect system
- **Async I/O and Math functions in effect catalog** — `TextWriter.WriteLineAsync`, `StreamReader.ReadLineAsync`, `Math.Floor/Clamp/Sin/Round/Log` added to known effects
- **`§PROP` inside `§IFACE`** — Interface properties now emit correctly instead of being treated as methods
- **Tuple deconstruction conversion** — `(_a, _b) = (x, y)` converts to individual `§ASSIGN` statements
- **Line comment and char literal support in lexer** — `//` comments and single-quoted char literals no longer crash the lexer

### Fixed
- **Emitter**: `default:` instead of `case _:` for wildcard switch, read-only properties emit `{ get; }`, `@` prefix removed from `this`/`base`/keywords, namespace dots preserved in type names, decimal type bind attribute parsing
- **Converter**: `nameof()` → string literal, `string.Empty` → `""`, postfix/prefix increment → `§ASSIGN (+ var 1)`, `§MT` instead of `§SIG` for interface methods, `§FLD` instead of `§DICT`/`§LIST` for collection fields, `@`-prefixed C# identifiers stripped

## [0.2.7] - 2026-02-19

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **Class-level visibility preservation** — `internal class Program` no longer round-trips to `public class Program`; visibility flows through the full AST→converter→parser→emitter pipeline
- **Effect inference in converter** — The C#→Calor converter now auto-infers side effects from method bodies (e.g., `Console.WriteLine` → `§E{cw}`, `throw` → `§E{throw}`) instead of requiring manual annotation
- **Shared EffectCodes utility** — `EffectCodes.ToCompact()` centralizes effect category/value → compact code mapping
- **LINQ query syntax support** — `from`/`where`/`select`/`orderby`/`group by`/`join` expressions
- **LINQ method chain decomposition** — Chains like `.Where().Select().ToList()` are decomposed into sequential Calor statements
- **Type operators** — `is`, `as`, `cast` type checking and conversion operators
- **7 missing language features** — decimal literals, array/object initializers, anonymous types, extension methods, yield return, partial classes, operator overloads
- **`§USE` syntax** — New using directive format with `--validate-codegen` flag
- **`CalorCompilerOverride` MSBuild property** — Override compiler path in build
- **`calor self-test` CLI command** — Automated compiler self-test via CLI and MCP tool

### Fixed
- **Converter fidelity** — const arrays, built-in method chains, mutable binding `~` prefix, bare array initializers, multi-element `§ARR` arrays, float literal decimal points, complex string interpolation expressions
- **Effect enforcement** — Resolved `§F` vs `§MT` inconsistency for LINQ calls and method-level effect checking
- **Code generation** — struct support, static fields, global namespace, increment/decrement operators, class inheritance, static class modifier, readonly struct identity, operator overloads, `§IDX` codegen, generics in inheritance, attribute unquoting, `#nullable enable`
- **Parser/emitter** — `§EACH` index support, `§CAST` error improvements, partial class modifier emission, stale static class comment, double-slash error message, `§EACH` syntax docs
- **Init/tooling** — `.proj` file support, git root resolution for MCP, atomic writes for `~/.claude.json`, MCP tools in agent templates

## [0.2.6] - 2026-02-18

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **MCP server documentation** - Comprehensive documentation for `calor mcp` command with all 19 tools
- **LSP-style MCP navigation tools** - `calor_goto_definition`, `calor_find_references`, `calor_symbol_info`, `calor_document_outline`, `calor_find_symbol`
- **Semantic analysis MCP tools** - `calor_typecheck` for type checking with error categorization, `calor_verify_contracts` for Z3 contract verification

### Fixed
- MCP server now writes configuration to `~/.claude.json` per-project section instead of `.mcp.json`
- MCP server uses newline-delimited JSON (NDJSON) instead of Content-Length framing

## [0.2.5] - 2026-02-17

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **Ask Calor GPT integration** - Custom GPT link added to website header, footer, and dedicated homepage section with analytics tracking
- **MCP Server tools** - New `calor_assess` tool for C# migration analysis, plus `lint`, `format`, `diagnose`, and `ids` tools for AI agent integration
- **Hero section update** - New video and messaging on website homepage

### Fixed
- CI workflow: removed weekly schedule trigger, now runs all benchmarks on release with human-readable metric names
- Website: tied benchmark results now display with salmon color for clarity
- Evaluation: removed bias in effect discipline and correctness benchmarks
- Evaluation: consolidated benchmark metrics and integrated Safety/EffectDiscipline

## [0.2.4] - 2026-02-16

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.07x (Calor leads)
- **Metrics**: Calor wins 8, C# wins 4
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
  - ContractVerification, EffectSoundness, InteropEffectCoverage: Calor-only features (C# has no equivalent)
- **Programs Tested**: 40

### Added
- Effect Discipline benchmark measuring side effect management (40 tasks across 4 categories)
- Safety benchmark measuring contract enforcement quality

### Fixed
- Fixed array type conversion in benchmark test harness (JSON deserializes as `object[]` but methods need typed arrays)
- Fixed format string interpolation in compiler (`"${0}"` no longer incorrectly treated as C# interpolation)
- Added documentation that `abs`, `max`, `min`, `sqrt`, `pow` operators don't exist in Calor
- Fixed 21 benchmark test files that had invalid syntax (recursive functions, data structures, design patterns)
- Fixed InformationDensity calculator using outdated square bracket patterns instead of curly braces
- All 40 benchmark files now compile successfully

## [0.2.3] - 2026-02-12

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.84x (C# leads on token economics)
- **Metrics**: Calor wins 7, C# wins 4
- **Highlights**:
  - Comprehension: 1.57x (Calor wins, large effect)
  - ErrorDetection: 1.51x (Calor wins, large effect)
  - RefactoringStability: 1.50x (Calor wins, large effect)
  - EditPrecision: 1.38x (Calor wins, large effect)
  - ContractVerification, EffectSoundness, InteropEffectCoverage: Calor-only features (C# has no equivalent)
- **Programs Tested**: 36

### Added
- **Platform-specific VS Code extension bundles** - Each platform (Windows x64/ARM64, macOS x64/ARM64, Linux x64/ARM64) gets its own VSIX with bundled language server binary (~40 MB each)
- **Bundled language server discovery** - Extension automatically uses bundled `calor-lsp` binary, no separate installation needed
- **Enum extension methods** - `§EEXT{id:EnumName}` for defining extension methods on enums
- **Shorter enum syntax** - `§EN` as shorthand for `§ENUM` (legacy syntax still supported)

### Changed
- Enum definitions now use `§EN{id:name}` instead of `§ENUM{id:name}` (both are accepted for backwards compatibility)
- CI workflow now builds 6 platform-specific VSIX packages in parallel

### Fixed
- Benchmark framework now correctly counts Calor-only metrics (ContractVerification, EffectSoundness, InteropEffectCoverage) as Calor wins instead of ties

## [0.2.2] - 2026-02-10

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.84x (C# leads on token economics)
- **Metrics**: Calor wins 7, C# wins 4
- **Highlights**:
  - Comprehension: 1.57x (Calor wins)
  - ErrorDetection: 1.51x (Calor wins)
  - RefactoringStability: 1.50x (Calor wins)
  - EditPrecision: 1.38x (Calor wins)
  - ContractVerification, EffectSoundness, InteropEffectCoverage: Calor-exclusive features (not available in C#)
- **Programs Tested**: 36

### Added
- **Collection operations with semantic type checking** - `§LIST`, `§DICT`, `§HSET` literals with `§PUSH`, `§PUT`, `§SETIDX`, `§HAS`, `§CNT` operations
- **Pattern matching with arrow syntax** - `§W`/`§K` switch expressions with relational patterns (`§PREL`), variable patterns (`§VAR`), guards (`§WHEN`)
- **Async/await support** - `§AF`/`§AMT` for async functions/methods, `§AWAIT` expression with ConfigureAwait support
- **Lambda expressions** - Inline `(x) → expr` and block `§LAM`/`§/LAM` syntax with async support
- **Delegate definitions** - `§DEL`/`§/DEL` for custom delegate types with effect tracking
- **Event support** - `§EVT` for event definitions, `§SUB`/`§UNSUB` for subscribe/unsubscribe
- **Dictionary iteration** - `§EACHKV` for iterating key-value pairs

### Fixed
- Z3 SMT solver contract inheritance verification gaps
- Type checker for angle bracket generic syntax

## [0.2.1] - 2026-02-08

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.80x (C# leads overall)
- **Metrics**: Calor wins 4, C# wins 4
- **Highlights**:
  - ErrorDetection: 1.55x (Calor wins, large effect)
  - Comprehension: 1.49x (Calor wins, large effect)
  - RefactoringStability: 1.49x (Calor wins, large effect)
  - EditPrecision: 1.36x (Calor wins, large effect)
- **Programs Tested**: 28

### Added
- **Z3 static contract verification** - Prove contracts at compile time with `--verify` flag; proven contracts can have runtime checks elided
- **Manifest-based effect resolution for .NET interop** - Layered resolution from built-in BCL manifests, user manifests, and namespace defaults
- **Granular effect taxonomy** - `fs:r/fs:w`, `net:r/net:w`, `db:r/db:w`, `env:r/env:w` with subtyping (`rw` encompasses `r` and `w`)
- **New CLI commands**: `calor effects resolve`, `calor effects validate`, `calor effects list`
- New CatchBugs component on homepage showing interprocedural effect analysis with compiler error demo

### Changed
- Homepage restructured from 9 to 7 sections for better focus
- Hero updated with value-oriented messaging ("When AI writes your code, the language should catch the bugs")
- CodeComparison updated with ULID-based stable identifiers
- FeatureGrid updated with impact statements and "Learn more" links for all cards
- BenchmarkChart reframed as "Where Explicit Semantics Pay Off"
- QuickStart now includes descriptions under each command
- ProjectStatus now compact with chip-based milestones

### Removed
- Story section from homepage
- CompetitivePositioning section from homepage
- VSCodeExtension section from homepage

## [0.2.0] - 2026-02-07

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.80x (C# leads overall)
- **Metrics**: Calor wins 4, C# wins 4
- **Highlights**:
  - ErrorDetection: 1.55x (Calor wins, large effect)
  - Comprehension: 1.49x (Calor wins, large effect)
  - RefactoringStability: 1.49x (Calor wins, large effect)
  - EditPrecision: 1.36x (Calor wins, large effect)
- **Programs Tested**: 28

### Fixed
- Benchmark calculators now use correct curly brace syntax `{` for Calor patterns instead of square brackets `[`
- This fix enables proper detection of Calor language constructs in RefactoringStability, Comprehension, ErrorDetection, and EditPrecision metrics

## [0.1.9] - 2026-02-06

### Changed
- Documentation updated to remove v1/v2 version references
- Fixed invalid tokens in documentation to match current lexer (§SM, §NN, §CL, §MT, §IV, §TH)

## [0.1.8] - 2026-02-05

### Added
- New documentation page: "The Verification Opportunity" explaining why effects and contracts enforcement is a key value proposition
- "Learn more" links on landing page feature cards for Contracts and Effects

## [0.1.7] - 2026-02-05

### Added
- `calor lint` command for formatting and linting Calor files
- Comprehensive linter regression test suite

### Changed
- **Project renamed from OPAL to Calor**
  - Language name: Calor (was OPAL)
  - CLI tool: `calor` (was `opalc`)
  - File extension: `.calr` (was `.opal`)
  - NuGet packages: `calor`, `Calor.Tasks`, `Calor.Sdk`
- New tagline: "Coding Agent Language for Optimized Reasoning"
- Added project logo
- Enhanced warning messages for non-Claude AI agents (Codex, GitHub Copilot) to clearly indicate they cannot enforce Calor-first development

### Fixed
- Claude skills directory structure now uses correct `SKILL.md` format

## [0.1.4] - 2025-02-03

### Added
- **Multi-AI support**: Added support for GitHub Copilot, OpenAI Codex, and Google Gemini CLI
  - `calor init --ai github` for GitHub Copilot
  - `calor init --ai codex` for OpenAI Codex
  - `calor init --ai gemini` for Google Gemini
- **Solution-level initialization**: `calor init` now works on solution folders, initializing all projects
- Enum support for C# to Calor conversion
- Support for explicit enum values and underlying types
- Calor syntax: `§ENUM{id:Name}` and `§ENUM{id:Name:underlyingType}`
- Type mappings for DateTime, Guid, and read-only collections (ReadList, ReadDict)
- Comprehensive NuGet package metadata (authors, tags, repository URL, license)
- CHANGELOG.md for tracking version history

### Changed
- Renamed to "Coding Agent Language for Optimized Reasoning"
- Documentation links now point to https://juanrivera.github.io/calor
- Updated documentation to reflect current feature support status
- Fixed Claude skills directory structure to match Codex/Gemini pattern

### Fixed
- Clarified that `calor init` should be run in a folder with a C# project or solution

## [0.1.3] - Previous Release
- Claude Code hooks for Calor-first enforcement
- Initial AI integration with Claude

## [0.1.0] - Initial Release
- Initial public release of Calor compiler
