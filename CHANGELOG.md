# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

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
