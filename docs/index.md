---
layout: default
title: Home
nav_order: 1
description: "OPAL - Optimized Programming for Agent Language"
permalink: /
---

# OPAL
{: .fs-9 }

Optimized Programming for Agent Language
{: .fs-6 .fw-300 }

A programming language designed specifically for AI coding agents, compiling to .NET via C# emission.
{: .fs-5 .fw-300 }

[Get Started](/opal/getting-started/){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/juanmicrosoft/opal){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## Why OPAL?

AI coding agents are transforming software development, but they're forced to work with languages designed for humans. This creates a fundamental mismatch.

**AI agents need to understand code semantically** - what it does, what side effects it has, what contracts it upholds - but traditional languages hide this information behind syntax that requires deep semantic analysis to parse.

OPAL asks: *What if we designed a language from the ground up for AI agents?*

---

## What Agents See

### OPAL - Everything Explicit

```
§F[f002:Square:pub]
  §I[i32:x]
  §O[i32]
  §Q (>= x 0)
  §S (>= result 0)
  §R (* x x)
§/F[f002]
```

**What OPAL tells the agent directly:**
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

## Benchmark Results

Evaluated across 20 paired OPAL/C# programs using V2 compact syntax:

| Category | OPAL vs C# | Winner | Interpretation |
|:---------|:-----------|:-------|:---------------|
| Comprehension | **1.33x** | OPAL | Explicit structure aids understanding |
| Error Detection | **1.19x** | OPAL | Contracts surface invariant violations |
| Edit Precision | **1.15x** | OPAL | Unique IDs enable targeted changes |
| Generation Accuracy | 0.94x | C# | Mature tooling, familiar patterns |
| Task Completion | 0.93x | C# | Ecosystem maturity advantage |
| Token Economics | 0.67x | C# | OPAL's explicit syntax uses more tokens |
| Information Density | 0.22x | C# | OPAL trades density for explicitness |

**Key Finding:** OPAL excels where explicitness matters - comprehension, error detection, and edit precision. C# wins on token efficiency, reflecting a fundamental tradeoff: explicit semantics require more tokens but enable better agent reasoning.

[View detailed benchmarks](/opal/benchmarking/results/){: .btn .btn-outline }

---

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

[Full installation guide](/opal/getting-started/installation/){: .btn .btn-outline }

---

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

---

## Contributing

OPAL is an experiment in language design for AI agents. We welcome contributions, especially:

- Additional benchmark programs
- Metric refinements
- Parser improvements
- Documentation

[Contributing guide](/opal/contributing/){: .btn .btn-outline }
