# OPAL — Optimized Programming for Agents Language

A programming language designed specifically for AI coding agents, compiling to .NET via C# emission.

## Why OPAL Exists

AI coding agents are transforming software development, but they're forced to work with languages designed for humans. This creates a fundamental mismatch:

**AI agents need to understand code semantically** — what it does, what side effects it has, what contracts it upholds — but traditional languages hide this information behind syntax that requires deep semantic analysis to parse.

OPAL asks: *What if we designed a language from the ground up for AI agents?*

### The Core Insight

When an AI agent reads code, it needs answers to specific questions:
- What does this function **do**? (not just how it's implemented)
- What are the **side effects**? (I/O, state mutations, network calls)
- What **constraints** must hold? (preconditions, postconditions)
- How do I **precisely reference** this code element across edits?
- Where does this **scope end**?

Traditional languages make agents *infer* these answers through complex analysis. OPAL makes them *explicit* in the syntax.

## What Makes OPAL Different

| Principle | How OPAL Implements It | Agent Benefit |
|-----------|------------------------|---------------|
| **Explicit over implicit** | Effects declared with `§E[cw,fr,net]` | Know side effects without reading implementation |
| **Contracts are code** | First-class `§Q` (requires) and `§S` (ensures) | Generate tests from specs, verify correctness |
| **Everything has an ID** | `§F[f001:Main]`, `§L[l001:i:1:100:1]` | Precise references that survive refactoring |
| **Unambiguous structure** | Matched tags `§F[]...§/F[]` | Parse without semantic analysis |
| **Machine-readable semantics** | Lisp-style operators `(+ a b)` | Symbolic manipulation without text parsing |

### Side-by-Side: What Agents See

**OPAL** — Everything explicit:
```
§F[f002:Square:pub]
  §I[i32:x]
  §O[i32]
  §Q (>= x 0)
  §S (>= result 0)
  §R (* x x)
§/F[f002]
```

**C#** — Contracts buried in implementation:
```csharp
public static int Square(int x)
{
    if (!(x >= 0))
        throw new ArgumentException("Precondition failed");
    var result = x * x;
    if (!(result >= 0))
        throw new InvalidOperationException("Postcondition failed");
    return result;
}
```

**What OPAL tells the agent directly:**
- Function ID: `f002`, can reference precisely
- Precondition (`§Q`): `x >= 0`
- Postcondition (`§S`): `result >= 0`
- No side effects (no `§E` declaration)

**What C# requires the agent to infer:**
- Parse exception patterns to find contracts
- Understand that lack of I/O calls *probably* means no side effects
- Hope line numbers don't change across edits

## The Tradeoff

OPAL deliberately trades token efficiency for semantic explicitness:

```
C#:   return a + b;    // 4 tokens, implicit semantics
OPAL: §R (+ a b)       // Explicit Lisp-style operations
```

This tradeoff pays off when:
- Agents need to **reason** about code behavior
- Agents need to **detect** contract violations
- Agents need to **edit** specific code elements precisely
- Code correctness matters more than brevity

### Benchmark Results

Evaluated across 20 paired OPAL/C# programs using V2 compact syntax:

| Category | OPAL vs C# | Winner | Why |
|----------|------------|--------|-----|
| Comprehension | **1.33x** | OPAL | Explicit structure aids understanding |
| Error Detection | **1.19x** | OPAL | Contracts surface invariant violations |
| Edit Precision | **1.15x** | OPAL | Unique IDs enable targeted changes |
| Token Economics | 0.67x | C# | OPAL's explicit syntax uses more tokens |

**Key Finding:** OPAL excels where explicitness matters — comprehension, error detection, and edit precision. C# wins on token efficiency, reflecting a fundamental tradeoff: explicit semantics require more tokens but enable better agent reasoning.

## Quick Start

```bash
# Install the compiler
dotnet tool install -g opalc

# Initialize for Claude Code (run in a folder with a C# project or solution)
opalc init --ai claude

# Compile OPAL to C#
opalc --input program.opal --output program.g.cs
```

### Your First OPAL Program

```
§M[m001:Hello]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §P "Hello from OPAL!"
§/F[f001]
§/M[m001]
```

Save as `hello.opal`, then:

```bash
opalc --input hello.opal --output hello.g.cs
```

### Building from Source

```bash
git clone https://github.com/juanmicrosoft/opal.git
cd opal && dotnet build

# Run the sample
dotnet run --project src/Opal.Compiler -- \
  --input samples/HelloWorld/hello.opal \
  --output samples/HelloWorld/hello.g.cs
dotnet run --project samples/HelloWorld
```

## Documentation

- **[Syntax Reference](https://juanmicrosoft.github.io/opal/syntax-reference/)** — Complete language reference
- **[Getting Started](https://juanmicrosoft.github.io/opal/getting-started/)** — Installation, hello world, Claude integration
- **[Benchmarking](https://juanmicrosoft.github.io/opal/benchmarking/)** — How we measure OPAL vs C#

## Project Status

- [x] Core compiler (lexer, parser, C# code generation)
- [x] Control flow (for, if/else, while)
- [x] Type system (Option, Result)
- [x] Contracts (requires, ensures)
- [x] Effects declarations
- [x] MSBuild SDK integration
- [x] AI agent initialization (`opalc init`)
- [x] Evaluation framework (7 metrics, 20 benchmarks)
- [ ] Direct IL emission
- [ ] IDE language server

## Contributing

OPAL is an experiment in language design for AI agents. We welcome contributions, especially:
- Additional benchmark programs
- Metric refinements
- Parser improvements
- Documentation

See the evaluation framework in `tests/Opal.Evaluation/` for how we measure progress.
