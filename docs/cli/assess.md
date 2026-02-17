---
layout: default
title: assess
parent: CLI Reference
nav_order: 1
permalink: /cli/assess/
redirect_from:
  - /cli/analyze/
---

# calor assess

Score C# files for Calor migration potential.

```bash
calor assess <path> [options]
```

> **Note:** The command `calor analyze` is supported as an alias for backwards compatibility.

---

## Overview

The `assess` command scans a C# codebase and scores each file based on how much it would benefit from Calor's features. It detects patterns like argument validation, null handling, error handling, async/await usage, and LINQ patterns that map directly to Calor language constructs.

Use this command to:

- **Prioritize migration efforts** - Focus on files that benefit most from Calor's contracts and effects
- **Understand your codebase** - See which patterns are most common across your project
- **Generate reports** - Export analysis in JSON or SARIF format for tooling integration

---

## Quick Start

```bash
# Assess current directory
calor assess .

# Assess with detailed breakdown
calor assess ./src --verbose

# Export as JSON for processing
calor assess ./src --format json --output assessment.json
```

---

## Scoring Dimensions

Each file is scored across eight dimensions that correspond to Calor language features:

| Dimension | Weight | What It Detects | Calor Feature |
|:----------|:------:|:----------------|:-------------|
| **ContractPotential** | 18% | Argument validation, `ArgumentException` throws, range checks | `§Q`/`§S` contracts |
| **NullSafetyPotential** | 18% | Nullable types, `?.`, `??`, null checks | `Option<T>` |
| **ErrorHandlingPotential** | 18% | Try/catch blocks, throw statements | `Result<T,E>` |
| **EffectPotential** | 13% | File I/O, network calls, database access, console | `§E` effect declarations |
| **ApiComplexityPotential** | 13% | Undocumented public APIs | Calor metadata requirements |
| **PatternMatchPotential** | 8% | Switch statements/expressions | Exhaustiveness checking |
| **AsyncPotential** | 6% | `async`/`await`, `Task<T>`, `CancellationToken`, `ConfigureAwait` | Calor async model |
| **LinqPotential** | 6% | LINQ methods (`.Where()`, `.Select()`) and query syntax | Calor collection patterns |

The total score (0-100) is the weighted sum of individual dimension scores.

---

## Priority Bands

Files are categorized into priority bands based on their total score:

| Priority | Score Range | Meaning |
|:---------|:------------|:--------|
| **Critical** | 76-100 | Excellent migration candidate - high density of patterns that Calor improves |
| **High** | 51-75 | Good migration candidate - significant benefit from Calor features |
| **Medium** | 26-50 | Some benefit from migration - moderate pattern density |
| **Low** | 0-25 | Minimal benefit - few patterns that Calor addresses |

---

## Command Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--format` | `-f` | `text` | Output format: `text`, `json`, or `sarif` |
| `--output` | `-o` | stdout | Write output to file instead of stdout |
| `--threshold` | `-t` | `0` | Minimum score to include (0-100) |
| `--top` | `-n` | `20` | Number of top files to show |
| `--verbose` | `-v` | `false` | Show detailed per-file breakdown |

---

## Output Formats

### Text (Default)

Human-readable summary with ASCII bar charts:

```
=== Calor Migration Assessment ===

Analyzed: 42 files
Skipped: 8 files (generated/errors)
Average Score: 34.2/100

Priority Breakdown:
  Critical (76-100): 2 files
  High (51-75):      8 files
  Medium (26-50):    18 files
  Low (0-25):        14 files

Average Scores by Dimension:
  ErrorHandlingPotential   45.2 |#########
  ContractPotential        38.1 |#######
  NullSafetyPotential      35.6 |#######
  EffectPotential          28.4 |#####
  AsyncPotential           24.5 |####
  ApiComplexityPotential   22.1 |####
  LinqPotential            15.8 |###
  PatternMatchPotential    12.3 |##

Top 20 Files for Migration:
--------------------------------------------------------------------------------
 82/100 [Critical]  src/Services/PaymentProcessor.cs
 78/100 [Critical]  src/Services/OrderService.cs
 65/100 [High]      src/Repositories/UserRepository.cs
...
```

With `--verbose`, each file shows dimension breakdown:

```
 82/100 [Critical]  src/Services/PaymentProcessor.cs
         ErrorHandlingPotential: 95 (12 patterns)
         ContractPotential: 88 (8 patterns)
         EffectPotential: 75 (6 patterns)
         NullSafetyPotential: 62 (15 patterns)
```

### JSON

Machine-readable format for processing:

```bash
calor assess ./src --format json --output assessment.json
```

```json
{
  "version": "1.0",
  "analyzedAt": "2025-01-15T10:30:00Z",
  "rootPath": "/path/to/src",
  "durationMs": 1234,
  "summary": {
    "totalFiles": 42,
    "skippedFiles": 8,
    "averageScore": 34.2,
    "priorityBreakdown": {
      "critical": 2,
      "high": 8,
      "medium": 18,
      "low": 14
    },
    "averagesByDimension": {
      "ContractPotential": 38.1,
      "EffectPotential": 28.4,
      "NullSafetyPotential": 35.6,
      "ErrorHandlingPotential": 45.2,
      "PatternMatchPotential": 12.3,
      "ApiComplexityPotential": 22.1,
      "AsyncPotential": 24.5,
      "LinqPotential": 15.8
    }
  },
  "files": [
    {
      "path": "Services/PaymentProcessor.cs",
      "score": 82.3,
      "priority": "critical",
      "lineCount": 245,
      "methodCount": 12,
      "typeCount": 1,
      "dimensions": {
        "ContractPotential": {
          "score": 88.0,
          "weight": 0.18,
          "patternCount": 8,
          "examples": ["throw new ArgumentNullException(...)", "if (...) throw validation"]
        }
      }
    }
  ]
}
```

### SARIF

[SARIF](https://sarifweb.azurewebsites.net/) (Static Analysis Results Interchange Format) for IDE and CI/CD integration:

```bash
calor assess ./src --format sarif --output assessment.sarif
```

SARIF output integrates with:
- **VS Code** - SARIF Viewer extension
- **GitHub** - Code scanning alerts
- **Azure DevOps** - Build results
- **Other tools** - Any SARIF-compatible viewer

Each scoring dimension becomes a SARIF rule (e.g., `Calor-ContractPotential`), and findings appear as diagnostics in your IDE.

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | Success - no high-priority files found |
| `1` | Success - high or critical priority files found |
| `2` | Error - invalid arguments, directory not found, etc. |

Use exit code `1` in CI/CD to flag codebases with high migration potential:

```bash
# Fail CI if high-priority migration candidates exist
calor assess ./src --threshold 51
if [ $? -eq 1 ]; then
  echo "High-priority Calor migration candidates found"
fi
```

---

## Practical Examples

### Find Top Migration Candidates

```bash
# Show top 10 files scoring above 50
calor assess ./src --threshold 50 --top 10
```

### CI/CD Integration

```yaml
# GitHub Actions example
- name: Assess Calor migration potential
  run: |
    calor assess ./src --format sarif --output calor-assessment.sarif

- name: Upload SARIF
  uses: github/codeql-action/upload-sarif@v2
  with:
    sarif_file: calor-assessment.sarif
```

### Generate Migration Report

```bash
# Full JSON report for documentation
calor assess . --format json --output migration-report.json

# Parse with jq to find critical files
cat migration-report.json | jq '.files[] | select(.priority == "critical") | .path'
```

### Verbose Assessment of Specific Area

```bash
# Deep dive into a specific directory
calor assess ./src/Services --verbose --top 50
```

---

## Skipped Files

The analyzer automatically skips:

- **Generated files**: `*.g.cs`, `*.generated.cs`, `*.Designer.cs`
- **Build directories**: `obj/`, `bin/`
- **Version control**: `.git/`
- **Dependencies**: `node_modules/`
- **Files with parse errors**

Skipped files are reported in the summary but don't affect scoring.

---

## See Also

- [Getting Started](/calor/getting-started/) - Install Calor and write your first program
- [Syntax Reference](/calor/syntax-reference/) - Complete language reference
- [Contracts](/calor/syntax-reference/contracts/) - Learn about `§Q`/`§S` contracts
- [Effects](/calor/syntax-reference/effects/) - Learn about `§E` effect declarations
