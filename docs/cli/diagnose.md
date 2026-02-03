---
layout: default
title: diagnose
parent: CLI Reference
nav_order: 7
permalink: /cli/diagnose/
---

# calorc diagnose

Output machine-readable diagnostics for Calor files.

```bash
calorc diagnose <files...> [options]
```

---

## Overview

The `diagnose` command analyzes Calor files and outputs diagnostics in formats suitable for tooling integration:

- **Text** - Human-readable plain text
- **JSON** - Machine-readable structured data
- **SARIF** - Static Analysis Results Interchange Format for IDE/CI integration

Use this for automated fix workflows, CI/CD pipelines, and editor integrations.

---

## Quick Start

```bash
# Diagnose a single file (text output)
calorc diagnose MyModule.calor

# JSON output for processing
calorc diagnose MyModule.calor --format json

# SARIF output for IDE integration
calorc diagnose src/*.calor --format sarif --output diagnostics.sarif

# Enable strict checking
calorc diagnose MyModule.calor --strict-api --require-docs
```

---

## Arguments

| Argument | Required | Description |
|:---------|:---------|:------------|
| `files` | Yes | One or more Calor source files to diagnose |

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--format` | `-f` | `text` | Output format: `text`, `json`, or `sarif` |
| `--output` | `-o` | stdout | Output file (stdout if not specified) |
| `--strict-api` | | `false` | Enable strict API checking |
| `--require-docs` | | `false` | Require documentation on public functions |

---

## Output Formats

### Text (Default)

Human-readable diagnostics:

```bash
calorc diagnose Calculator.calor
```

Output:
```
Calculator.calor:12:5: error: Undefined variable 'x'
  §R (+ x 1)
       ^

Calculator.calor:8:3: warning: Function 'Calculate' has no effect declaration
  Consider adding §E[] for pure functions

Calculator.calor:15:3: info: Unused variable 'temp'
  §B[temp] 42

Summary: 1 error, 1 warning, 1 info
```

### JSON

Machine-readable format for automated processing:

```bash
calorc diagnose Calculator.calor --format json
```

Output:
```json
{
  "version": "1.0",
  "files": [
    {
      "path": "Calculator.calor",
      "diagnostics": [
        {
          "severity": "error",
          "code": "Calor001",
          "message": "Undefined variable 'x'",
          "location": {
            "line": 12,
            "column": 5,
            "endLine": 12,
            "endColumn": 6
          },
          "source": "§R (+ x 1)",
          "suggestions": [
            {
              "message": "Did you mean 'a'?",
              "replacement": "a"
            }
          ]
        },
        {
          "severity": "warning",
          "code": "Calor002",
          "message": "Function 'Calculate' has no effect declaration",
          "location": {
            "line": 8,
            "column": 3
          },
          "suggestions": [
            {
              "message": "Add effect declaration for pure function",
              "replacement": "  §E[]"
            }
          ]
        }
      ]
    }
  ],
  "summary": {
    "errors": 1,
    "warnings": 1,
    "info": 1
  }
}
```

### SARIF

[SARIF](https://sarifweb.azurewebsites.net/) format for IDE and CI/CD integration:

```bash
calorc diagnose src/*.calor --format sarif --output diagnostics.sarif
```

SARIF output integrates with:
- **VS Code** - SARIF Viewer extension
- **GitHub** - Code scanning alerts
- **Azure DevOps** - Build results
- **JetBrains IDEs** - Qodana integration

---

## Diagnostic Codes

| Code | Severity | Description |
|:-----|:---------|:------------|
| `Calor001` | Error | Undefined variable |
| `Calor002` | Warning | Missing effect declaration |
| `Calor003` | Warning | Unused variable |
| `Calor004` | Error | Type mismatch |
| `Calor005` | Error | Invalid ID reference |
| `Calor006` | Warning | Missing contract |
| `Calor007` | Info | Redundant parentheses |
| `Calor008` | Error | Unclosed structure tag |
| `Calor009` | Warning | Undocumented public API |
| `Calor010` | Error | Duplicate ID |

---

## Strict Mode

### Strict API (`--strict-api`)

Enforces stricter API rules:

- All public functions must have explicit return types
- All parameters must have explicit types
- No implicit any types

```bash
calorc diagnose MyModule.calor --strict-api
```

### Require Docs (`--require-docs`)

Requires documentation on public APIs:

```bash
calorc diagnose MyModule.calor --require-docs
```

Error:
```
MyModule.calor:5:1: error: Public function 'ProcessOrder' missing documentation
  Add documentation comment before function declaration
```

---

## Processing Multiple Files

Diagnostics from all files are aggregated:

```bash
calorc diagnose src/*.calor --format json
```

The output includes diagnostics from all files in a single report.

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | No errors (warnings and info are OK) |
| `1` | One or more errors found |
| `2` | Processing error (file not found, invalid Calor, etc.) |

---

## AI Agent Integration

The `diagnose` command is designed for AI agent workflows:

### Automated Fix Loop

```bash
# 1. Get diagnostics in JSON
calorc diagnose MyModule.calor --format json > diagnostics.json

# 2. AI agent reads diagnostics and applies fixes

# 3. Re-run diagnostics to verify
calorc diagnose MyModule.calor --format json
```

### Claude Code Integration

In Claude Code, use the diagnostics to guide fixes:

```
Run calorc diagnose on MyModule.calor and fix any errors
```

Claude will:
1. Run the diagnose command
2. Parse the output
3. Apply fixes based on suggestions
4. Verify the fixes

---

## CI/CD Integration

### GitHub Actions

```yaml
- name: Check Calor diagnostics
  run: |
    calorc diagnose src/**/*.calor --format sarif --output calor.sarif

- name: Upload SARIF
  uses: github/codeql-action/upload-sarif@v2
  with:
    sarif_file: calor.sarif
```

### Azure Pipelines

```yaml
- script: |
    calorc diagnose src/**/*.calor --format sarif --output $(Build.ArtifactStagingDirectory)/calor.sarif
  displayName: 'Run Calor diagnostics'

- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: $(Build.ArtifactStagingDirectory)/calor.sarif
    artifactName: CodeAnalysis
```

---

## Examples

### Quick Check

```bash
# Check for errors only
calorc diagnose MyModule.calor
if [ $? -ne 0 ]; then
  echo "Errors found, fix before committing"
  exit 1
fi
```

### Full Analysis Report

```bash
# Generate comprehensive report
calorc diagnose src/*.calor \
  --strict-api \
  --require-docs \
  --format json \
  --output analysis-report.json
```

### Pre-Commit Hook

```bash
#!/bin/bash
# .git/hooks/pre-commit

Calor_FILES=$(git diff --cached --name-only --diff-filter=ACM | grep '\.calor$')

if [ -n "$Calor_FILES" ]; then
  echo "$Calor_FILES" | xargs calorc diagnose
  if [ $? -ne 0 ]; then
    echo "Calor diagnostics found errors. Fix before committing."
    exit 1
  fi
fi
```

---

## See Also

- [calorc format](/calor/cli/format/) - Format Calor source files
- [calorc compile](/calor/cli/compile/) - Compile with error reporting
- [calorc analyze](/calor/cli/analyze/) - Analyze C# for migration potential
