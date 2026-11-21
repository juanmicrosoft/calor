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

Passing a `.csproj` uses its evaluated MSBuild `Compile` items, configuration,
target framework, symbols, references, aliases, and parse options. `Compile
Remove` entries are excluded, while explicitly evaluated linked `Compile`
items remain included even when their physical path is under another project.
Passing a directory instead selects loose-directory mode: files are discovered
recursively without adopting a nearby project, and nested project directories
are excluded. Output-path collisions (for
example, `Foo.cs` and `Foo.csx` both mapping to `Foo.calr`) fail before writes.
For project mode, generated C# is validated with its converted siblings and
effective project references/options. Failures escalate native conversion through
member, type, and exact whole-file C# preservation, then validation runs again.
Buildable discovered inputs are written without needing a passthrough flag.
One malformed or unwritable input does not discard unrelated valid outputs.
Loose directories can lack references: exact preserved sources are still written,
with failed compilation reported as `unverified`, not a passed build.
Writing, native fraction, project compilation, and test execution are distinct results.

### Opt-in native namespace splitting

`calor migrate ./src --split-namespaces` can split a C# file containing distinct
top-level namespace blocks into `File.namespace1.calr`, `File.namespace2.calr`,
and so on, in source order. Each file uses the existing `module M N block`
header: `M` remains a file label, and `N` is the original CLR namespace.
There are no namespace blocks in the converted Calor.

For a single named namespace, the same opt-in can convert resolved local imports
without splitting the file. The output remains `File.calr`, `IsSplit` is false in
the API, and no `namespaceIndex` is assigned. `namespace using` retains local
ordinary, alias and static imports; compilation-unit and global imports stay
outside the C# namespace. Since 0.30 this single-namespace case no longer needs
the opt-in: `calor convert` and default `calor migrate` use it too, and fall back
to whole-file C# preservation when it cannot be certified.
Normal effect and nullability checks still apply; resolving an import does not
prove that a call is pure. A scoped projection that changes declared types or
nullable annotations stays as whole-file C#, with an explicit refusal reason.

The bounded multi-namespace subset excludes repeated or nested namespaces, nested types,
scoped imports, alias/global imports, directives, compilation-unit attributes,
extern aliases, inheritance, and nonliteral field/property initializers.
Ordinary compilation-unit imports keep their original scope in each output.
The complete original Roslyn tree and evaluated sibling/reference context remain
the authority during conversion; missing or invalid source context is refused.
Every output must remain native and compile, and the complete generated C# set
is validated with the original project context. This does not add general
constructed-generic member substitution.

Unsupported topologies retain the ordinary single-file preservation boundary.
If an otherwise split-safe input cannot produce the complete native set, the
project migration fails without committing partial outputs. Any conversion,
collision, cancellation or validation failure before commit prevents the entire
opt-in batch from being written. Output, receipt and lock paths participate in
collision checks. Existing single-output files are not deleted or replaced by
split outputs; choose a fresh destination instead.
Refresh also refuses obsolete split files outside the new output set, rather
than leaving their declarations active. Constructor bodies and initialization
must pass the existing effect checks; an unproved constructor is not native
split success.

The report contains one result row per output, with the original `sourcePath`
and zero-based `namespaceIndex`. `summary.sourceFiles` counts distinct inputs;
`summary.totalFiles` counts result rows. Native split rows still carry the
existing Partial namespace-topology registry warning; no registry support level
is promoted.

Library callers opt in with `CSharpToCalorConverter.ConvertProject(source,
sourceFile, cancellationToken)`. Its ordered `Outputs` contain `FileName` and
`Conversion` (the ordinary `ConversionResult`). Check `IsSplit` and each output's
losses before claiming native splitting. Existing `Convert` and `ConvertFileAsync`
remain single-result APIs with unchanged preservation/refusal behavior.

### Safe refresh of generated Calor

Lossless C# migration saves `<output>.conversion.json` beside each generated
file. This receipt retains the original source, supplied sibling trees, parse
options, reference aliases and observed input hashes. Whole-file preservation
keeps the actual conversion context.

Migration can refresh an unchanged generated file when its original C# changes.
It refuses edited output and missing, corrupt or unassociated receipts; keep
those files and choose a new destination. It also checks for changed inputs and
generation bases before committing. Cooperating writers hold generation locks.
Dry runs and cancellation or validation refusal before commit write neither
output nor receipt.

Calor outputs and declaration identities commit together, but receipts are
separate writes. If a final receipt cannot be saved, migration reports an error
and `outputWritten: true` for the committed file. Its output path and loss
disposition remain available. Automatic refresh stays blocked for a mismatched
pair; the error does not mean the output was unchanged.

Receipts detect generation changes, not converter authenticity or native
support. They do not capture the complete evaluated-project, generator or
effect-manifest closure. Lossy partial-class merging and build/LSP/cache
validation do not use these receipts yet.

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--dry-run` | `-n` | `false` | Preview changes without writing files |
| `--benchmark` | `-b` | `false` | Include before/after metrics comparison |
| `--direction` | `-d` | `cs-to-calor` | Migration direction |
| `--parallel` | `-p` | `true` | Run conversions in parallel |
| `--split-namespaces` | — | `false` | Opt in to one native file per split-safe top-level namespace; requires lossless, all-branches migration. |
| `--report` | `-r` | None | Save migration report to file (`.md` or `.json`) |
| `--verbose` | `-v` | `false` | Enable verbose output |
| `--define` | — | — | Add a conditional-compilation symbol. |
| `--configuration` | — | `Debug` | MSBuild configuration used to resolve parse options and symbols. |
| `--framework` | — | — | Select one target framework. Without selection, configuration-sensitive files are preserved exactly and checked under each target; symbols are never unioned. |
| `--language-version` | — | Project value | Override the C# language version. |
| `--documentation-mode` | — | `parse` | Roslyn documentation mode: `none`, `parse`, or `diagnose`. |
| `--feature` | — | — | Roslyn parse feature in `key=value` form. |

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

### Default-path CI measurement

The `roundtrip-verification (default)` CI job also runs default `calor migrate`
on isolated copies of the pinned MediatR, Serilog and FluentValidation source
directories. The `default-migrate-reports` artifact contains the CLI JSON
envelopes, logs, and a combined JSON/Markdown summary:

- Genuinely failed files, converted-but-blocked files, and files actually written.
- Fully native written files divided by all discovered input files. Passthrough
  and unwritten conversions do not count as native output.
- Written passthrough units grouped by reason, plus discovery exclusions.
- Explicit build/test status. These source-only copies are not buildable
  projects, so both statuses are `not-run`, never `passed`.

This report complements the existing project-context round-trip harness; it
does not replace or relax its build, test, native-conversion or coverage gates.
The source-directory measurement does not establish that a migration builds.
Until the preservation fallback ladder is implemented, a failed file can still
block other converted files from being written.

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
