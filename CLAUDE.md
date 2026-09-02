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

### Run Tests Locally

Three lanes, ordered fastest to slowest. See the [Corpus Submodules](#corpus-submodules-round-trip-harness)
section below for **which** projects touch `bench/corpus/`.

**First-time setup (once per clone).** `Calor.Verification.Tests` links
against `Microsoft.Z3` native binaries that are gitignored per platform.
Any build that touches the compiler project hard-errors with
`missing Z3 asset` until the download script runs:

```bash
src/Calor.Compiler/scripts/download-z3.sh    # or .ps1 on Windows
```

**Fast unit-test lane** (seconds; no submodules required):

```bash
dotnet test tests/Calor.Compiler.Tests/     # lexer/parser/emitter/analysis
dotnet test tests/Calor.Conversion.Tests/   # C# → Calor snapshots
dotnet test tests/Calor.Semantics.Tests/    # binding/semantic analysis
dotnet test tests/Calor.Verification.Tests/ # Z3 contract verification
dotnet test tests/Calor.Enforcement.Tests/  # effects/taint
```

Other corpus-independent projects are also fast-lane candidates —
`Calor.LanguageServer.Tests`, `Calor.Ids.Tests`, `Calor.Tasks.Tests`,
`Calor.ILAnalysis.Tests`, `Calor.Experimental.Tests`, `Calor.Performance.Tests`
— the five above are the routinely-touched core.

`tests/Calor.Compiler.Tests/Binding/BinderIncompleteRatchetTests.cs` will
silently skip on a bare clone (it uses `Skip.IfNot` when `bench/corpus/`
is empty); everything else runs green without submodules. To iterate on a
single class, use the CLAUDE-standard filter:
`dotnet test --filter "FullyQualifiedName~ClassName"`.

**Full-corpus lane** (adds ~500 MB and a few minutes; needed to unskip
`BinderIncompleteRatchetTests`):

```bash
git submodule update --init
dotnet test tests/Calor.Compiler.Tests/    # scope to the project that needs corpus
```

`dotnet test` at repo root is much broader — it sweeps every test project
the solution references (LSP, tasks, IL analysis, evaluation, etc.), which
takes noticeably longer than the fast lane and covers surfaces most PRs
don't need. Prefer scoped `dotnet test tests/<Project>/` invocations, or
run everything at once when preparing a release.

**Round-trip harness lane** (slowest — ~30 min end-to-end; matches the
`roundtrip-verification` CI job):

```bash
git submodule update --init
dotnet run --project tools/Calor.RoundTrip.Harness -- run --all
```

`run --all` iterates every project in `ProjectConfigs.KnownProjects`
(`Synthetic`, `Synthetic2`, `MediatR`, `Serilog`, `FluentValidation`)
against the vendored `bench/corpus/` corpus. Pass a single project name
(`run MediatR`) to shrink the loop while debugging a specific regression.

### Corpus Submodules (round-trip harness)

`.gitmodules` pins three real-world C# corpora under `bench/corpus/`:
`MediatR`, `serilog`, and `FluentValidation`. `git clone` does **not**
auto-init them — CI opts them in per job (`grep -n submodules:
.github/workflows/test.yml` to find the current call sites; the `test`,
`roundtrip-verification`, and the `compiler` shard of `remaining-tests` are
the jobs that need them today).

Init them on a fresh clone (needed for the round-trip harness under
`tools/Calor.RoundTrip.Harness/`, the `roundtrip-verification` CI job when
run locally, or the `corpus-binder-ratchet` leg):

```bash
git submodule update --init   # first clone
git submodule update          # refresh pinned SHAs after main advances
```

The three pinned submodules are flat (no nested submodules of their own),
so `--recursive` is unnecessary here. CI passes it as a safe default.

The three corpora add ~500 MB to a fresh clone; skip them if you are only
running the unit-test lane.

**Which tests need submodules?** Only the round-trip harness and
`BinderIncompleteRatchetTests`
(`tests/Calor.Compiler.Tests/Binding/BinderIncompleteRatchetTests.cs`) read
from `bench/corpus/`. `BinderIncompleteRatchetTests` uses
`Skip.IfNot(subjects.All(Directory.Exists), "corpus submodules not
initialized")` to skip cleanly on a bare clone, so running
`dotnet test tests/Calor.Compiler.Tests/` is safe without submodules —
you will silently skip that one class. `Calor.Conversion.Tests`,
`Calor.Semantics.Tests`, `Calor.Verification.Tests`,
`Calor.Enforcement.Tests`, and `Calor.Evaluation` do not touch
`bench/corpus/` at all.

Dev/CI parity: corpus tests can be green in CI while failing locally on a
fresh clone. `git submodule update --init` on the outer repo is the fix.

Origin: 2026-08-18 test-suite audit, finding F8 / recommendation R4
(`docs/plans/2026-08-18-test-suite-audit.md`).

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

## Writing for the public surface

`CHANGELOG.md`, `website/content/**`, and GitHub release notes are written **as if
speaking to a computer science college student**. Internal artifacts — `docs/plans/`,
`docs/design/`, PR bodies, review records, code comments — stay technical and precise.

An earlier version of this rule said "a high schooler must understand it", and it
backfired: avoiding every technical term produced *longer, harder* prose. "The step that
works out what every name refers to" is worse than **name binding**. Circumlocution is the
thing to avoid, not vocabulary.

- **Use the standard term** when one exists: binding, parser, AST, diagnostic, type
  checker, submodule, CI. Do not paraphrase it away.
- **Explain Calor's own vocabulary** briefly on first use — effect rows, `Calor0410`,
  `§PROP`, the row charge, laundering, a ledger. One clause, not a paragraph.
- **Short sentences, active voice, lead with what changed.** Split any sentence carrying
  three subordinate clauses.
- **Numbers, not adjectives**: "40 of 60 modules" beats "most modules".
- Simpler language never means vaguer claims. Say plainly when a result is unproven,
  underpowered, or an upper bound.

Test: would a second-year CS student skim the entry and know what changed and whether it
affects them?

## Benchmarks

Benchmarks live in `benchmarks/`. When writing or modifying benchmarks, ensure they are **not biased towards Calor** — benchmarks must be fair and representative comparisons.

## Dependencies

- **Microsoft.CodeAnalysis.CSharp 5.3.0** — Roslyn, for C# parsing in the migration pipeline (supports C# 14)
- **System.CommandLine 2.0.0-beta4** — CLI argument parsing
- **Z3 4.15.7** — SMT solver for contract verification (custom ARM64 build)
