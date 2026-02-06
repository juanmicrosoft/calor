---
layout: default
title: Home
nav_order: 1
description: "Calor - Coding Agent Language for Optimized Reasoning"
permalink: /
---

# Calor
{: .fs-9 }

Coding Agent Language for Optimized Reasoning
{: .fs-6 .fw-300 }

A programming language designed specifically for AI coding agents, compiling to .NET via C# emission.
{: .fs-5 .fw-300 }

[Get Started](/calor/getting-started/){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/juanmicrosoft/calor){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## Why Calor?

AI coding agents are transforming software development, but they're forced to work with languages designed for humans. This creates a fundamental mismatch.

**AI agents need to understand code semantically** - what it does, what side effects it has, what contracts it upholds - but traditional languages hide this information behind syntax that requires deep semantic analysis to parse.

Calor asks: *What if we designed a language from the ground up for AI agents?*

---

## What Agents See

### Calor - Everything Explicit

```
§F[f002:Square:pub]
  §I[i32:x]
  §O[i32]
  §Q (>= x 0)
  §S (>= result 0)
  §R (* x x)
§/F[f002]
```

**What Calor tells the agent directly:**
- Function ID: `f002` - can reference precisely
- Precondition (`§Q`): `x >= 0`
- Postcondition (`§S`): `result >= 0`
- No side effects (no `§E` declaration)

### C# - Requires Inference

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

**What C# requires the agent to infer:**
- Parse exception patterns to find contracts
- Understand that lack of I/O calls *probably* means no side effects
- Hope line numbers don't change across edits

---

## The Verification Breakthrough

For 50 years, computer scientists have known how to make software more reliable: effect systems, design-by-contract, dependent types. These techniques can **prove** code correctness.

**So why isn't all software written this way?**

Because humans find annotation burden too high. Every verification system that relies on human discipline has failed to achieve mainstream adoption.

**Coding agents change this equation.** When agents write code, annotation cost is zero. They never forget contracts, never skip effects declarations, never cut corners under deadline pressure.

Calor is the first language to leverage this insight:

```
§F{f001:ProcessOrder:pub}
  §I{Order:order}
  §O{bool}
  §E{db}                    // Effect declaration enforced at compile time
  §Q (> order.amount 0)     // Precondition verified at runtime
  §S (!= result null)       // Postcondition verified at runtime

  §C{SaveOrder} order       // OK: SaveOrder has db effect
  §C{SendEmail} order       // COMPILE ERROR: net effect not declared
§/F{f001}
```

The compiler catches effect violations with full call chains. The runtime catches contract violations with function ID and source location. Bugs that would ship to production in traditional languages are **impossible** in Calor.

[Learn more: The Verification Opportunity](/calor/philosophy/the-verification-opportunity/){: .btn .btn-outline }

---

## Benchmark Results

Evaluated across 20 paired Calor/C# programs:

| Category | Calor vs C# | Winner | Interpretation |
|:---------|:-----------|:-------|:---------------|
| Comprehension | **1.33x** | Calor | Explicit structure aids understanding |
| Error Detection | **1.19x** | Calor | Contracts surface invariant violations |
| Edit Precision | **1.15x** | Calor | Unique IDs enable targeted changes |
| Generation Accuracy | 0.94x | C# | Mature tooling, familiar patterns |
| Task Completion | 0.93x | C# | Ecosystem maturity advantage |
| Token Economics | 0.67x | C# | Calor's explicit syntax uses more tokens |
| Information Density | 0.22x | C# | Calor trades density for explicitness |

**Key Finding:** Calor excels where explicitness matters - comprehension, error detection, and edit precision. C# wins on token efficiency, reflecting a fundamental tradeoff: explicit semantics require more tokens but enable better agent reasoning.

[View detailed benchmarks](/calor/benchmarking/results/){: .btn .btn-outline }

---

## Quick Start

```bash
# Clone and build
git clone https://github.com/juanmicrosoft/calor.git
cd calor && dotnet build

# Compile Calor to C#
dotnet run --project src/Calor.Compiler -- \
  --input samples/HelloWorld/hello.calr \
  --output samples/HelloWorld/hello.g.cs

# Run the generated program
dotnet run --project samples/HelloWorld
```

[Full installation guide](/calor/getting-started/installation/){: .btn .btn-outline }

---

## Migration Analysis

Have an existing C# codebase? Use `calor analyze` to find files that would benefit most from Calor:

```bash
# Score C# files for migration potential
calor analyze ./src

# Output:
# === Calor Migration Analysis ===
# Analyzed: 42 files
# Average Score: 34.2/100
#
# Priority Breakdown:
#   Critical (76-100): 2 files
#   High (51-75):      8 files
#   ...
```

The analyzer scores files based on patterns like null handling, error handling, and argument validation that map to Calor features.

[Learn more about analyze](/calor/cli/analyze/){: .btn .btn-outline }

---

## Project Status

- [x] Core compiler (lexer, parser, C# code generation)
- [x] Control flow (for, if/else, while, do-while)
- [x] Type system (Option, Result)
- [x] Contracts (requires, ensures) with runtime enforcement
- [x] Effects declarations with compile-time enforcement
- [x] Interprocedural effect analysis (SCC-based)
- [x] MSBuild SDK integration
- [x] Evaluation framework (7 metrics, 20 benchmarks)
- [ ] Direct IL emission
- [ ] IDE language server

---

## Contributing

Calor is an experiment in language design for AI agents. We welcome contributions, especially:

- Additional benchmark programs
- Metric refinements
- Parser improvements
- Documentation

[Contributing guide](/calor/contributing/){: .btn .btn-outline }
