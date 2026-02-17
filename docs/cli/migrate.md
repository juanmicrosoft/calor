---
layout: default
title: migrate
parent: CLI Reference
nav_order: 4
permalink: /cli/migrate/
---

# calor migrate

Migrate an entire project between C# and Calor.

```bash
calor migrate <path> [options]
```

---

## Overview

The `migrate` command converts all applicable files in a project or directory. It:

- Scans for convertible files
- Creates a migration plan
- Converts files in parallel (optional)
- Generates detailed reports
- Handles errors gracefully without stopping

Use this for bulk conversion of entire codebases.

---

## Quick Start

```bash
# Preview migration (dry run)
calor migrate ./src --dry-run

# Migrate C# to Calor
calor migrate ./src

# Migrate with report
calor migrate ./src --report migration-report.md

# Migrate Calor back to C#
calor migrate ./src --direction calor-to-cs
```

---

## Arguments

| Argument | Required | Description |
|:---------|:---------|:------------|
| `path` | Yes | Project directory or `.csproj` file to migrate |

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--dry-run` | `-n` | `false` | Preview changes without writing files |
| `--benchmark` | `-b` | `false` | Include before/after metrics comparison |
| `--direction` | `-d` | `cs-to-calor` | Migration direction |
| `--parallel` | `-p` | `true` | Run conversions in parallel |
| `--report` | `-r` | None | Save migration report to file (`.md` or `.json`) |
| `--verbose` | `-v` | `false` | Enable verbose output |

### Direction Values

| Value | Aliases | Description |
|:------|:--------|:------------|
| `cs-to-calor` | `csharp-to-calor`, `c#-to-calor` | Convert C# files to Calor |
| `calor-to-cs` | `calor-to-csharp`, `calor-to-c#` | Convert Calor files to C# |

---

## Migration Plan

Before converting, the command analyzes your codebase and creates a plan:

```bash
calor migrate ./src --dry-run
```

Output:
```
=== Migration Plan ===

Direction: C# → Calor
Source: ./src

Files to Convert:
  ✓ 24 files fully convertible
  ⚠ 8 files partially convertible (will have warnings)
  ✗ 3 files skipped (unsupported constructs)

Skipped Files:
  src/Generated/ApiClient.cs (generated code)
  src/Legacy/OldModule.cs (unsafe code)
  src/Interop/NativeWrapper.cs (P/Invoke)

Estimated Issues: 12 warnings across 8 files

Run without --dry-run to execute migration.
```

---

## Migration Progress

During migration, you'll see progress updates:

```
Migrating ./src (C# → Calor)

[████████████████████████████████] 32/32 files

Results:
  ✓ 24 files converted successfully
  ⚠ 5 files converted with warnings
  ✗ 3 files failed (see errors below)

Errors:
  src/Complex/HardFile.cs: Unsupported construct at line 142

Migration complete in 2.3s
```

With `--verbose`, each file is logged individually:

```
[1/32] Converting src/Services/UserService.cs... ✓
[2/32] Converting src/Services/OrderService.cs... ✓ (2 warnings)
[3/32] Converting src/Services/PaymentService.cs... ✓
...
```

---

## Benchmark Summary

Use `--benchmark` to see aggregate metrics:

```bash
calor migrate ./src --benchmark
```

Output includes:
```
=== Benchmark Summary ===

┌─────────────────┬────────────┬────────────┬──────────┐
│ Metric          │ Before     │ After      │ Change   │
├─────────────────┼────────────┼────────────┼──────────┤
│ Total Tokens    │ 45,230     │ 28,140     │ -37.8%   │
│ Total Lines     │ 3,456      │ 2,189      │ -36.7%   │
│ Total Files     │ 32         │ 32         │ 0        │
│ Avg Tokens/File │ 1,413      │ 879        │ -37.8%   │
└─────────────────┴────────────┴────────────┴──────────┘

Token Savings: 17,090 tokens (37.8% reduction)
```

---

## Migration Reports

Generate detailed reports for documentation:

### Markdown Report

```bash
calor migrate ./src --report migration-report.md
```

Creates a human-readable report with:
- Summary statistics
- Per-file conversion status
- Warnings and errors
- Benchmark comparisons (if `--benchmark`)

### JSON Report

```bash
calor migrate ./src --report migration-report.json
```

Creates a machine-readable report for processing:

```json
{
  "version": "1.0",
  "migratedAt": "2025-01-15T10:30:00Z",
  "direction": "cs-to-calor",
  "sourcePath": "./src",
  "summary": {
    "totalFiles": 32,
    "successful": 24,
    "withWarnings": 5,
    "failed": 3,
    "durationMs": 2340
  },
  "files": [
    {
      "source": "src/Services/UserService.cs",
      "output": "src/Services/UserService.calr",
      "status": "success",
      "warnings": [],
      "benchmark": {
        "tokensBefore": 1245,
        "tokensAfter": 842,
        "savings": 0.324
      }
    }
  ],
  "errors": [
    {
      "file": "src/Complex/HardFile.cs",
      "message": "Unsupported construct at line 142",
      "line": 142
    }
  ]
}
```

---

## Parallel Processing

By default, files are converted in parallel for speed. Disable for debugging:

```bash
# Sequential processing
calor migrate ./src --parallel false --verbose
```

---

## Skipped Files

The migrate command automatically skips:

- **Generated files**: `*.g.cs`, `*.generated.cs`, `*.Designer.cs`
- **Build artifacts**: `obj/`, `bin/`
- **Already converted**: Files that already have a corresponding `.calr` or `.g.cs`

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | All files migrated successfully |
| `1` | Some files failed or had warnings |
| `2` | Error - invalid arguments, directory not found, etc. |

---

## Examples

### Preview Before Migrating

```bash
# See what would be converted
calor migrate ./src --dry-run

# See detailed plan
calor migrate ./src --dry-run --verbose
```

### Migrate with Full Reporting

```bash
# Complete migration with benchmark and report
calor migrate ./src --benchmark --report migration.md --verbose
```

### Migrate Specific Project

```bash
# Migrate a specific .csproj
calor migrate ./src/MyProject/MyProject.csproj
```

### Reverse Migration

```bash
# Convert Calor back to C# (e.g., for debugging)
calor migrate ./src --direction calor-to-cs
```

### CI/CD Integration

```bash
# In CI: verify all files can be converted
calor migrate ./src --dry-run
if [ $? -ne 0 ]; then
  echo "Migration issues detected"
  exit 1
fi
```

---

## Best Practices

1. **Always dry-run first** - Preview the migration plan before executing
2. **Commit before migrating** - Ensure you can revert if needed
3. **Use reports** - Generate reports for documentation and review
4. **Review warnings** - Check files with warnings for correct conversion
5. **Test after migration** - Run your test suite to verify functionality

---

## See Also

- [calor convert](/calor/cli/convert/) - Convert single files
- [calor assess](/calor/cli/assess/) - Find migration candidates
- [calor benchmark](/calor/cli/benchmark/) - Detailed metrics comparison
- [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) - Complete migration guide
