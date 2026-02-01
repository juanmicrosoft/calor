---
layout: default
title: diagnose
parent: CLI Reference
nav_order: 7
permalink: /cli/diagnose/
---

# opalc diagnose

Output machine-readable diagnostics for OPAL files.

```bash
opalc diagnose <files...> [options]
```

---

## Overview

The `diagnose` command analyzes OPAL files and outputs diagnostics in formats suitable for tooling integration:

- **Text** - Human-readable plain text
- **JSON** - Machine-readable structured data
- **SARIF** - Static Analysis Results Interchange Format for IDE/CI integration

Use this for automated fix workflows, CI/CD pipelines, and editor integrations.

---

## Quick Start

```bash
# Diagnose a single file (text output)
opalc diagnose MyModule.opal

# JSON output for processing
opalc diagnose MyModule.opal --format json

# SARIF output for IDE integration
opalc diagnose src/*.opal --format sarif --output diagnostics.sarif

# Enable strict checking
opalc diagnose MyModule.opal --strict-api --require-docs
```

---

## Arguments

| Argument | Required | Description |
|:---------|:---------|:------------|
| `files` | Yes | One or more OPAL source files to diagnose |

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
opalc diagnose Calculator.opal
```

Output:
```
Calculator.opal:12:5: error: Undefined variable 'x'
  §R (+ x 1)
       ^

Calculator.opal:8:3: warning: Function 'Calculate' has no effect declaration
  Consider adding §E[] for pure functions

Calculator.opal:15:3: info: Unused variable 'temp'
  §B[temp] 42

Summary: 1 error, 1 warning, 1 info
```

### JSON

Machine-readable format for automated processing:

```bash
opalc diagnose Calculator.opal --format json
```

Output:
```json
{
  "version": "1.0",
  "files": [
    {
      "path": "Calculator.opal",
      "diagnostics": [
        {
          "severity": "error",
          "code": "OPAL001",
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
          "code": "OPAL002",
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
opalc diagnose src/*.opal --format sarif --output diagnostics.sarif
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
| `OPAL001` | Error | Undefined variable |
| `OPAL002` | Warning | Missing effect declaration |
| `OPAL003` | Warning | Unused variable |
| `OPAL004` | Error | Type mismatch |
| `OPAL005` | Error | Invalid ID reference |
| `OPAL006` | Warning | Missing contract |
| `OPAL007` | Info | Redundant parentheses |
| `OPAL008` | Error | Unclosed structure tag |
| `OPAL009` | Warning | Undocumented public API |
| `OPAL010` | Error | Duplicate ID |

---

## Strict Mode

### Strict API (`--strict-api`)

Enforces stricter API rules:

- All public functions must have explicit return types
- All parameters must have explicit types
- No implicit any types

```bash
opalc diagnose MyModule.opal --strict-api
```

### Require Docs (`--require-docs`)

Requires documentation on public APIs:

```bash
opalc diagnose MyModule.opal --require-docs
```

Error:
```
MyModule.opal:5:1: error: Public function 'ProcessOrder' missing documentation
  Add documentation comment before function declaration
```

---

## Processing Multiple Files

Diagnostics from all files are aggregated:

```bash
opalc diagnose src/*.opal --format json
```

The output includes diagnostics from all files in a single report.

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | No errors (warnings and info are OK) |
| `1` | One or more errors found |
| `2` | Processing error (file not found, invalid OPAL, etc.) |

---

## AI Agent Integration

The `diagnose` command is designed for AI agent workflows:

### Automated Fix Loop

```bash
# 1. Get diagnostics in JSON
opalc diagnose MyModule.opal --format json > diagnostics.json

# 2. AI agent reads diagnostics and applies fixes

# 3. Re-run diagnostics to verify
opalc diagnose MyModule.opal --format json
```

### Claude Code Integration

In Claude Code, use the diagnostics to guide fixes:

```
Run opalc diagnose on MyModule.opal and fix any errors
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
- name: Check OPAL diagnostics
  run: |
    opalc diagnose src/**/*.opal --format sarif --output opal.sarif

- name: Upload SARIF
  uses: github/codeql-action/upload-sarif@v2
  with:
    sarif_file: opal.sarif
```

### Azure Pipelines

```yaml
- script: |
    opalc diagnose src/**/*.opal --format sarif --output $(Build.ArtifactStagingDirectory)/opal.sarif
  displayName: 'Run OPAL diagnostics'

- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: $(Build.ArtifactStagingDirectory)/opal.sarif
    artifactName: CodeAnalysis
```

---

## Examples

### Quick Check

```bash
# Check for errors only
opalc diagnose MyModule.opal
if [ $? -ne 0 ]; then
  echo "Errors found, fix before committing"
  exit 1
fi
```

### Full Analysis Report

```bash
# Generate comprehensive report
opalc diagnose src/*.opal \
  --strict-api \
  --require-docs \
  --format json \
  --output analysis-report.json
```

### Pre-Commit Hook

```bash
#!/bin/bash
# .git/hooks/pre-commit

OPAL_FILES=$(git diff --cached --name-only --diff-filter=ACM | grep '\.opal$')

if [ -n "$OPAL_FILES" ]; then
  echo "$OPAL_FILES" | xargs opalc diagnose
  if [ $? -ne 0 ]; then
    echo "OPAL diagnostics found errors. Fix before committing."
    exit 1
  fi
fi
```

---

## See Also

- [opalc format](/opal/cli/format/) - Format OPAL source files
- [opalc compile](/opal/cli/compile/) - Compile with error reporting
- [opalc analyze](/opal/cli/analyze/) - Analyze C# for migration potential
