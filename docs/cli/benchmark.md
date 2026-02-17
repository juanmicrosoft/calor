---
layout: default
title: benchmark
parent: CLI Reference
nav_order: 5
permalink: /cli/benchmark/
---

# calor benchmark

Compare Calor vs C# across evaluation metrics.

```bash
calor benchmark [project] [options]
```

---

## Overview

The `benchmark` command measures and compares Calor against C# across seven evaluation categories designed to assess AI agent effectiveness:

1. **Token Economics** - Token count and density
2. **Generation Accuracy** - Code correctness
3. **Comprehension** - Understandability
4. **Edit Precision** - Targeted modification accuracy
5. **Error Detection** - Bug identification
6. **Information Density** - Meaning per token
7. **Task Completion** - End-to-end success

Use this to quantify Calor's advantages for AI-assisted development.

---

## Quick Start

```bash
# Compare two files
calor benchmark --calor Calculator.calr --csharp Calculator.cs

# Benchmark entire project
calor benchmark ./src

# Quick token-only comparison
calor benchmark --calor file.calr --csharp file.cs --quick

# Generate markdown report
calor benchmark ./src --format markdown --output report.md
```

---

## Arguments

| Argument | Required | Description |
|:---------|:---------|:------------|
| `project` | No | Project directory to benchmark (finds paired files) |

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--calor` | | None | Calor file to benchmark |
| `--csharp`, `--cs` | | None | C# file to benchmark |
| `--category` | `-c` | All | Filter by category |
| `--format` | `-f` | `console` | Output format: `console`, `markdown`, `json` |
| `--output` | `-o` | stdout | Save results to file |
| `--verbose` | `-v` | `false` | Show detailed per-metric breakdown |
| `--quick` | `-q` | `false` | Quick token-only benchmark |

### Category Values

- `TokenEconomics`
- `GenerationAccuracy`
- `Comprehension`
- `EditPrecision`
- `ErrorDetection`
- `InformationDensity`
- `TaskCompletion`

---

## File-Level Benchmark

Compare a specific Calor file against its C# equivalent:

```bash
calor benchmark --calor PaymentService.calr --csharp PaymentService.cs
```

Output:
```
=== Calor vs C# Benchmark ===

Files:
  Calor: PaymentService.calr
  C#:   PaymentService.cs

Results:
┌─────────────────────┬────────┬────────┬───────────┐
│ Category            │ Calor   │ C#     │ Advantage │
├─────────────────────┼────────┼────────┼───────────┤
│ Token Economics     │ 82.4   │ 58.2   │ 1.42x     │
│ Generation Accuracy │ 91.2   │ 76.5   │ 1.19x     │
│ Comprehension       │ 88.6   │ 71.3   │ 1.24x     │
│ Edit Precision      │ 94.1   │ 68.2   │ 1.38x     │
│ Error Detection     │ 86.3   │ 72.1   │ 1.20x     │
│ Information Density │ 79.5   │ 61.4   │ 1.29x     │
│ Task Completion     │ 89.8   │ 74.6   │ 1.20x     │
├─────────────────────┼────────┼────────┼───────────┤
│ Overall             │ 87.4   │ 68.9   │ 1.27x     │
└─────────────────────┴────────┴────────┴───────────┘

Calor shows 1.27x overall advantage for AI agent tasks.
```

---

## Project-Level Benchmark

Benchmark all paired files in a project:

```bash
calor benchmark ./src
```

The command finds files with matching base names (e.g., `UserService.calr` and `UserService.cs`) and benchmarks each pair.

Output:
```
=== Project Benchmark ===

Directory: ./src
Paired Files: 12

Aggregate Results:
┌─────────────────────┬────────┬────────┬───────────┐
│ Category            │ Calor   │ C#     │ Advantage │
├─────────────────────┼────────┼────────┼───────────┤
│ Token Economics     │ 84.2   │ 56.8   │ 1.48x     │
│ ...                 │ ...    │ ...    │ ...       │
└─────────────────────┴────────┴────────┴───────────┘

Per-File Breakdown:
  PaymentService: 1.42x advantage
  OrderService:   1.38x advantage
  UserService:    1.31x advantage
  ...
```

---

## Quick Benchmark

For fast token/line comparison without the full 7-metric evaluation:

```bash
calor benchmark --calor file.calr --csharp file.cs --quick
```

Output:
```
=== Quick Benchmark ===

┌─────────────────┬────────┬────────┬──────────┐
│ Metric          │ Calor   │ C#     │ Savings  │
├─────────────────┼────────┼────────┼──────────┤
│ Tokens          │ 842    │ 1,245  │ 32.4%    │
│ Lines           │ 98     │ 156    │ 37.2%    │
│ Characters      │ 2,891  │ 4,521  │ 36.1%    │
└─────────────────┴────────┴────────┴──────────┘
```

---

## Verbose Output

Use `--verbose` for detailed metric breakdown:

```bash
calor benchmark --calor file.calr --csharp file.cs --verbose
```

Shows individual metrics within each category:

```
Token Economics (Score: 82.4 vs 58.2)
  Token Count:     842 vs 1,245 (32.4% savings)
  Token Density:   0.89 vs 0.62 (meaning per token)
  Context Fit:     94% vs 78% (fits in 8K context)

Generation Accuracy (Score: 91.2 vs 76.5)
  Syntax Correctness: 100% vs 95%
  Semantic Match:     92% vs 78%
  First-Try Success:  88% vs 65%

...
```

---

## Output Formats

### Console (Default)

Human-readable tables and summaries in the terminal.

### Markdown

```bash
calor benchmark ./src --format markdown --output benchmark.md
```

Creates a markdown report suitable for documentation or GitHub.

### JSON

```bash
calor benchmark ./src --format json --output benchmark.json
```

Creates machine-readable output:

```json
{
  "version": "1.0",
  "benchmarkedAt": "2025-01-15T10:30:00Z",
  "mode": "project",
  "path": "./src",
  "pairedFiles": 12,
  "results": {
    "aggregate": {
      "calor": 87.4,
      "csharp": 68.9,
      "advantage": 1.27
    },
    "categories": {
      "TokenEconomics": {
        "calor": 84.2,
        "csharp": 56.8,
        "advantage": 1.48
      }
    },
    "files": [
      {
        "name": "PaymentService",
        "calorPath": "src/PaymentService.calr",
        "csharpPath": "src/PaymentService.cs",
        "advantage": 1.42
      }
    ]
  }
}
```

---

## Single Category

Focus on a specific evaluation category:

```bash
calor benchmark --calor file.calr --csharp file.cs --category TokenEconomics
```

---

## Evaluation Categories Explained

### Token Economics

Measures token efficiency:
- **Token count** - Raw number of tokens
- **Token density** - Semantic meaning per token
- **Context efficiency** - How well code fits in LLM context windows

### Generation Accuracy

Measures code generation quality:
- **Syntax correctness** - Valid code on first generation
- **Semantic match** - Code does what was requested
- **First-try success** - No need for corrections

### Comprehension

Measures how well AI understands the code:
- **Structure recognition** - Identifying functions, classes, etc.
- **Flow understanding** - Control flow analysis
- **Intent extraction** - Understanding purpose

### Edit Precision

Measures targeted modification accuracy:
- **Target identification** - Finding the right location
- **Minimal change** - Not affecting unrelated code
- **Preservation** - Maintaining existing behavior

### Error Detection

Measures bug-finding ability:
- **Bug identification** - Finding issues
- **Root cause analysis** - Understanding why
- **Fix suggestion quality** - Proposing solutions

### Information Density

Measures meaning per token:
- **Redundancy** - Repeated information
- **Boilerplate ratio** - Ceremony vs. logic
- **Signal-to-noise** - Useful vs. structural tokens

### Task Completion

Measures end-to-end success:
- **Task understanding** - Grasping requirements
- **Implementation quality** - Meeting requirements
- **Iteration count** - Attempts needed

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | Benchmark completed successfully |
| `1` | Benchmark completed but Calor showed no advantage |
| `2` | Error - files not found, invalid arguments, etc. |

---

## See Also

- [calor assess](/calor/cli/assess/) - Score files for migration potential
- [calor convert](/calor/cli/convert/) - Convert files with benchmark option
- [Benchmarking](/calor/benchmarking/) - Detailed methodology documentation
- [Results](/calor/benchmarking/results/) - Published benchmark results
