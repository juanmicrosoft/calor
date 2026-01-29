# OPAL — Optimized Programming for Agent Logic

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

## Design Principles

| Principle | Implementation | Agent Benefit |
|-----------|----------------|---------------|
| **Explicit over implicit** | Effects declared with `§E[cw,fr,net]` | Know side effects without reading implementation |
| **Contracts are code** | First-class `§Q` (requires) and `§S` (ensures) | Generate tests from specs, verify correctness |
| **Everything has an ID** | `§F[f001:Main]`, `§L[l001:i:1:100:1]` | Precise references that survive refactoring |
| **Unambiguous structure** | Matched tags `§F[]...§/F[]` | Parse without semantic analysis |
| **Machine-readable semantics** | Lisp-style operators `(+ a b)` | Symbolic manipulation without text parsing |

## Measuring Success

We evaluate OPAL against C# across 7 categories designed to measure what matters for AI coding agents:

| Category | What It Measures | Why It Matters |
|----------|------------------|----------------|
| **Comprehension** | Structural clarity, semantic extractability | Can agents understand code without deep analysis? |
| **Error Detection** | Bug identification, contract violation detection | Can agents find issues using explicit semantics? |
| **Edit Precision** | Targeting accuracy, change isolation | Can agents make precise edits using unique IDs? |
| **Generation Accuracy** | Compilation success, structural correctness | Can agents produce valid code? |
| **Task Completion** | End-to-end success rates | Can agents complete full tasks? |
| **Token Economics** | Tokens required to represent logic | How much context window does code consume? |
| **Information Density** | Semantic elements per token | How much meaning per token? |

### Benchmark Results

Evaluated across 20 paired OPAL/C# programs (100% compilation success for both) using V2 compact syntax:

| Category | OPAL vs C# | Winner | Interpretation |
|----------|------------|--------|----------------|
| Comprehension | **1.33x** | OPAL | Explicit structure aids understanding |
| Error Detection | **1.19x** | OPAL | Contracts surface invariant violations |
| Edit Precision | **1.15x** | OPAL | Unique IDs enable targeted changes |
| Generation Accuracy | 0.94x | C# | Mature tooling, familiar patterns |
| Task Completion | 0.93x | C# | Ecosystem maturity advantage |
| Token Economics | 0.67x | C# | OPAL's explicit syntax uses more tokens |
| Information Density | 0.22x | C# | OPAL trades density for explicitness |

**Key Finding:** OPAL excels where explicitness matters — comprehension, error detection, and edit precision. C# wins on token efficiency, reflecting a fundamental tradeoff: explicit semantics require more tokens but enable better agent reasoning.

*Note: V2 syntax uses Lisp-style expressions `(+ a b)` instead of verbose `§OP[kind=add] §REF[name=a] §REF[name=b]`, improving token economics by ~40% compared to V1.*

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

## Side-by-Side: What Agents See

### Function with Contracts

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

### Control Flow with Effects

**OPAL** — Loop bounds and effects explicit:
```
§F[f001:PrintRange:pub]
  §I[i32:n]
  §O[void]
  §E[cw]
  §L[for1:i:1:n:1]
    §P i
  §/L[for1]
§/F[f001]
```

**What the agent knows without analysis:**
- Iterates from 1 to n (loop bounds in syntax)
- Side effect: `cw` (console write) — nothing else
- `§P` is the built-in print alias for `Console.WriteLine`
- Can calculate iteration count symbolically

## Quick Start

```bash
# Clone and build
git clone https://github.com/juanmicrosoft/opal.git
cd opal && dotnet build

# Compile OPAL to C#
dotnet run --project src/Opal.Compiler -- \
  --input samples/HelloWorld/hello.opal \
  --output samples/HelloWorld/hello.g.cs

# Run the generated program
dotnet run --project samples/HelloWorld
```

## Syntax Reference

| Element | Syntax | Example |
|---------|--------|---------|
| Module | `§M[id:name]` | `§M[m001:Calculator]` |
| Function | `§F[id:name:visibility]` | `§F[f001:Add:pub]` |
| Input | `§I[type:name]` | `§I[i32:x]` |
| Output | `§O[type]` | `§O[i32]` |
| Effects | `§E[codes]` | `§E[cw,fr,net]` |
| Requires | `§Q expr` | `§Q (>= x 0)` |
| Ensures | `§S expr` | `§S (>= result 0)` |
| Loop | `§L[id:var:from:to:step]` | `§L[l1:i:1:100:1]` |
| If/ElseIf/Else | `§IF...§EI...§EL` | `§IF (> x 0) → §R x §EL → §R 0` |
| Call | `§C[target]...§/C` | `§C[Math.Max] §A 1 §A 2 §/C` |
| Print | `§P expr` | `§P "Hello"` |
| Return | `§R expr` | `§R (+ a b)` |
| Operations | `(op args...)` | `(+ a b)`, `(== x 0)`, `(% n 2)` |
| Close tag | `§/X[id]` | `§/F[f001]` |

**Effect codes:** `cw` (console write), `cr` (console read), `fw` (file write), `fr` (file read), `net` (network), `db` (database)

**Operators:** `+`, `-`, `*`, `/`, `%` (arithmetic), `==`, `!=`, `<`, `<=`, `>`, `>=` (comparison), `&&`, `||` (logical)

## Running the Evaluation

```bash
# Run the evaluation framework
dotnet run --project tests/Opal.Evaluation -- --output report.json

# Generate markdown report
dotnet run --project tests/Opal.Evaluation -- --output report.md --format markdown
```

## Project Status

- [x] Core compiler (lexer, parser, C# code generation)
- [x] Control flow (for, if/else, while)
- [x] Type system (Option, Result)
- [x] Contracts (requires, ensures)
- [x] Effects declarations
- [x] MSBuild SDK integration
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
