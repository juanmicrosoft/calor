# CLAUDE.md — Calor Compiler

Calor is a DSL designed for AI agents that compiles to C# on .NET 10. The compiler lives in `src/Calor.Compiler/` and is packaged as the `calor` global tool. Version is tracked in `Directory.Build.props` (check there for the current version; do not trust a number written in docs).

## Build & Test

```bash
dotnet build          # Build all projects
dotnet test           # Run all tests
dotnet test --filter "FullyQualifiedName~ClassName"  # Run specific tests
```

- .NET 10 SDK required (pinned in `global.json` to 10.0.100, rollForward: latestMinor)
- `TreatWarningsAsErrors` is enabled globally — fix all warnings before committing
- GPG signing workaround (1Password agent): `git -c commit.gpgsign=false commit -m "message"`

### Test Projects

| Project | What it covers |
|---------|---------------|
| `Calor.Compiler.Tests` | Lexer, parser, emitter, analysis unit tests |
| `Calor.Conversion.Tests` | C# → Calor snapshot-based conversion tests |
| `Calor.Evaluation` | Runtime evaluation and execution |
| `Calor.Semantics.Tests` | Binding and semantic analysis |
| `Calor.Verification.Tests` | Z3 contract verification |
| `Calor.Enforcement.Tests` | Effect enforcement and taint analysis |

Tests use **xUnit**. Conversion tests are snapshot-based (golden files in `TestData/`).

## Architecture — Compilation Pipeline

### Calor → C# (compilation)

```
Source → Lexer → Tokens → Parser → AST → Binder → BoundTree → Analysis → CSharpEmitter → C#
                                                      ↓
                                              Bug patterns, contracts,
                                              effects, verification (Z3)
```

### C# → Calor (migration)

```
C# Source → Roslyn Parse → SyntaxTree → RoslynSyntaxVisitor → AST → CalorEmitter → Calor
                                              ↓
                                     Unsupported features →
                                     CSharpInteropBlockNode (raw C# preserved)
```

### Key Files (with approximate line counts)

| File | Lines | Role |
|------|-------|------|
| `Parsing/Lexer.cs` | 1,400 | Tokenizer — section markers, typed literals, keywords |
| `Parsing/Parser.cs` | 8,900 | Recursive descent parser → AST |
| `Ast/AstNode.cs` | 480 | IAstVisitor / IAstVisitor\<T\> interfaces (~236 methods each) |
| `Ast/*.cs` | 29 files | AST node classes organized by feature |
| `Binding/Binder.cs` | 520 | Two-pass binding: symbol registration + body binding |
| `Binding/BoundNodes.cs` | 500 | Bound tree nodes, VariableSymbol, FunctionSymbol |
| `CodeGen/CSharpEmitter.cs` | 4,600 | IAstVisitor\<string\> — generates C# from AST |
| `Migration/RoslynSyntaxVisitor.cs` | 6,500 | CSharpSyntaxWalker — converts C# → Calor AST |
| `Migration/CalorEmitter.cs` | 2,800 | IAstVisitor\<string\> — generates Calor from AST |
| `Migration/FeatureSupport.cs` | 810 | Feature support registry for C# → Calor migration |
| `Ids/IdScanner.cs` | 330 | IAstVisitor — scans/validates node IDs |
| `Verification/ExpressionSimplifier.cs` | 1,400 | IAstVisitor\<T\> — simplifies expressions for Z3 |
| `Analysis/BugPatterns/Patterns/` | dir | Checkers: div-by-zero, null-deref, off-by-one, overflow, index-OOB |
| `Diagnostics/Diagnostic.cs` | — | All diagnostic codes (Calor0001–Calor1399) |

All paths relative to `src/Calor.Compiler/`.

## Calor Syntax Quick Reference

Block structure is **indentation-only** (2 spaces per level, Python-style). **Never
write structural closer tags** — the main block closers (`§/M`, `§/F`, `§/L`, `§/I`,
`§/W`, `§/WH`, `§/CL`, `§/MT`, `§/IFACE`, and others) raise a hard error (`Calor0830`);
a few remaining closer forms are still tolerated by the parser but always optional.
The only closers you should ever write are `§/C` (call argument lists) and `§/LAM`
(block lambdas).

```
§M{id:Name}                Module
§F{id:name:vis} (T:x) -> R  Function with inline signature
§B{name:type}              Immutable binding
§B{~name:type}             Mutable binding
§L{id:var:from:to:step}    For loop
§IF{id} (cond)             If (body indented)
§EI (cond)                  ElseIf (at parent column)
§EL                         Else (at parent column)
§C{object.method} §A arg §/C  Method call with argument
§E{codes}                  Effects (§E{} = pure)
§Q (expr)                  Precondition
§S (expr)                  Postcondition
§IV (expr)                 Invariant
```

Example (current syntax — see `samples/FizzBuzz/fizzbuzz.calr`; fenced
` ```calor ` blocks starting with `§M` are parse-checked by `calor self-check docs`):

```calor
§M{m001:FizzBuzz}
  §F{f001:Main:pub} () -> void
    §E{cw}
    §L{for1:i:1:100:1}
      §IF{if1} (== (% i 15) 0)
        §P "FizzBuzz"
      §EI (== (% i 3) 0)
        §P "Fizz"
      §EL
        §P i
```

**Typed literals:** `INT:42`, `STR:"hello"`, `BOOL:true`, `FLOAT:3.14`

## Adding New AST Nodes — Checklist

1. **Node class** in `Ast/` with `Accept(IAstVisitor)` and `Accept<T>(IAstVisitor<T>)` methods
2. **Visitor interfaces** — add `Visit` methods to both `IAstVisitor` and `IAstVisitor<T>` in `Ast/AstNode.cs`
3. **All visitors** — implement in every IAstVisitor implementer:
   - `CodeGen/CSharpEmitter.cs` (C# generation)
   - `Migration/CalorEmitter.cs` (Calor generation)
   - `Ids/IdScanner.cs` (ID scanning)
   - `Verification/ExpressionSimplifier.cs` (Z3 simplification)
4. **Lexer** — add token kind in `Parsing/Token.cs` (update `IsKeyword` range), add keyword to dictionary in `Parsing/Lexer.cs`
5. **Parser** — add to `ParsePrimaryExpression` switch + `IsExpressionStart()` (for expressions) or `ParseStatement` dispatch (for statements)
6. **C# converter** — add switch cases and conversion methods in `Migration/RoslynSyntaxVisitor.cs`
7. **Feature registry** — add entry in `Migration/FeatureSupport.cs`

### Critical Parser Patterns

- **`ParseAttributes()`** already splits on `:` into `_pos0`, `_pos1`, etc. — **never re-split** `attrs["_pos0"]` on `:`
- **`IsExpressionStart()`** must include all new expression token kinds — otherwise binding initializers silently fail to parse
- **Closing tags with IDs** (e.g. `§/UNSAFE{u1}`) need `ParseAttributes()` after `Advance()` to consume the ID block
- **`ParseValue()`** handles `*` for pointer types and `[,]` for multi-dimensional array types

## Key Conventions

- **Visitor pattern everywhere** — every AST node has `Accept` methods; all tree operations implement `IAstVisitor` or `IAstVisitor<T>`
- **CSharpInteropBlockNode** — when `RoslynSyntaxVisitor` encounters unsupported C# features, it wraps the raw C# in this node (preserves code verbatim with metadata about the unsupported feature)
- **MemberPreprocessorBlockNode** — wraps class members in conditional `#if` blocks; supports `§PP{CONDITION}` ... `§/PP{CONDITION}` syntax with chained else branches
- **VariableSymbol.IsParameter** — distinguishes function parameters from locals; used by analysis passes
- **BoundCallExpression.Target** is a `string` — `NullDereferenceChecker` checks for `.unwrap` suffix
- **Option\<T\> and Result\<T,E\>** are valid generic types in Calor's type system
- **Diagnostic codes** — Calor0001–0099 (lexer), 0100–0199 (parser), 0200–0299 (semantic), 0300–0399 (contracts), 0400–0499 (effects), 0500–0599 (patterns), 0600–0699 (API strictness), 0700–0799 (semantics version + contract verification results), 0800–0899 (ID validation), 0900–0999 (dataflow/bug patterns/taint), 1000–1099 (codegen/interop), 1100–1199 (refinements/obligations), 1200–1299 (experimental), 1300–1399 (CLI: lint findings and command-level errors)

## Project Layout

```
src/
  Calor.Compiler/        Core compiler, CLI tool (calor)
  Calor.Runtime/         Runtime support for generated C#
  Calor.LanguageServer/  LSP server for IDE integration
  Calor.Sdk/             Public SDK for programmatic compilation
  Calor.Tasks/           MSBuild task integration
tests/
  Calor.Compiler.Tests/  Compiler unit + integration tests
  Calor.Conversion.Tests/ C# ↔ Calor snapshot tests
  Calor.Evaluation/      Runtime evaluation tests
  Calor.Semantics.Tests/ Semantic analysis tests
  Calor.Verification.Tests/ Z3 verification tests
  Calor.Enforcement.Tests/  Effect enforcement tests
  TestData/              Golden files for snapshot testing
tools/
  Calor.RoundTrip.Harness/ Round-trip verification tool
samples/                 Example Calor programs
docs/                    Syntax reference, guides, philosophy
editors/                 VSCode extension
```

## Benchmarks

Benchmarks live in `benchmarks/`. When writing or modifying benchmarks, ensure they are **not biased towards Calor** — benchmarks must be fair and representative comparisons.

## Dependencies

- **Microsoft.CodeAnalysis.CSharp 5.3.0** — Roslyn, for C# parsing in the migration pipeline (supports C# 14)
- **System.CommandLine 2.0.0-beta4** — CLI argument parsing
- **Z3 4.15.7** — SMT solver for contract verification (custom ARM64 build)
