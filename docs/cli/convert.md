---
layout: default
title: convert
parent: CLI Reference
nav_order: 3
permalink: /cli/convert/
---

# calor convert

Convert a single file between C# and Calor.

```bash
calor convert <input> [options]
```

---

## Overview

The `convert` command performs bidirectional conversion between C# and Calor:

- **C# → Calor**: Convert `.cs` files to Calor syntax
- **Calor → C#**: Convert `.calr` files to generated C#

The conversion direction is automatically detected from the input file extension.

---

## Quick Start

```bash
# Convert C# to Calor
calor convert MyService.cs

# Convert Calor to C#
calor convert MyService.calr

# Specify output path
calor convert MyService.cs --output src/MyService.calr

# Include benchmark comparison
calor convert MyService.cs --benchmark
```

---

## Arguments

| Argument | Required | Description |
|:---------|:---------|:------------|
| `input` | Yes | The source file to convert (`.cs` or `.calr`) |

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--output` | `-o` | Auto-detected | Output file path |
| `--benchmark` | `-b` | `false` | Include benchmark metrics comparison |
| `--verbose` | `-v` | `false` | Enable verbose output |
| `--explicit-call-closers` | — | `false` | Emit explicit `§/C` for every `§C` call (v0.6.0-compatible output). Use when regenerating `.calr` files intended to parse on v0.6.0 toolchains. By default v0.6.1 elides `§/C` for zero-arg calls. |
| `--format` | — | `text` | Output format: `text` or `json` (envelope document on stdout) — see [JSON Output](#json-output---format-json). No `-f` short alias |

---

## JSON Output (`--format json`)

With `--format json` stdout carries exactly one
[envelope document](/calor/cli/envelope-schema/) — all human-oriented status
moves to stderr, and a document is emitted on every path (success, failure,
timeout, crash). Exit codes are unchanged.

```json
{
  "version": "1.1",
  "command": "convert",
  "diagnostics": [
    { "code": "Calor1343", "message": "[local-functions] Local functions are not supported…",
      "severity": "warning",
      "location": { "file": "/abs/Sample.cs", "line": 12, "column": 5, "length": 0 } }
  ],
  "summary": { "total": 1, "errors": 0, "warnings": 1, "info": 0 },
  "data": {
    "direction": "csharp-to-calor",
    "inputPath": "/abs/Sample.cs",
    "outputPath": "/abs/Sample.calr",
    "success": true,
    "unsupportedFeatureCount": 1,
    "featureCounts": { "local-functions": 1 },
    "validated": false
  }
}
```

- `diagnostics[]` — conversion issues (`Calor1343`, severity mirrors the
  issue, message prefixed with the feature name when known), `--validate`
  parse errors in the generated output (`Calor1344`, warning — the output was
  still written, with `data.validated` / `data.validationErrorCount` set), and
  command-level failures (`Calor1345`: input not found, unknown file type,
  timeout, crash). Converting Calor → C#, compiler diagnostics appear with
  their own codes.
- `data.direction` — `csharp-to-calor` | `calor-to-csharp`.
- `data.benchmark` — present with `--benchmark`: token/line/character counts
  before and after, reduction percentages, and the advantage ratio.

If `--output` is not specified:

| Input | Output |
|:------|:-------|
| `MyFile.cs` | `MyFile.calr` |
| `MyFile.calr` | `MyFile.g.cs` |

---

## C# to Calor Conversion

When converting C# to Calor, the converter:

1. Parses the C# source code
2. Identifies supported constructs (classes, methods, properties, etc.)
3. Maps C# patterns to Calor equivalents
4. Generates unique IDs for all structural elements
5. Adds effect declarations based on detected side effects
6. Suggests contracts based on validation patterns

### Supported Constructs

| C# Construct | Calor Equivalent |
|:-------------|:----------------|
| `namespace` | `§M{id:Name}` module |
| `class` | `§CL{id:Name:vis}` class |
| `method` | `§F{id:Name:vis}` function |
| `property` | `§PROP{id:Name:vis:type}` property |
| `field` | `§FLD{id:type:name}` field |
| `if/else if/else` | `§IF{id}` / `§EI` / `§EL` branches (end at dedent) |
| `for` loop | `§L{id:var:from:to:step}` |
| `while` loop | `§WH{id}` |
| `try/catch` | Converted to `Result<T,E>` pattern |
| `?.`, `??` | Converted to `Option<T>` pattern |

### Conversion Warnings

The converter reports patterns it can't perfectly translate:

```
Converting MyService.cs → MyService.calr
  Warning: Complex LINQ query at line 42 - manual review recommended
  Warning: Async method at line 78 - converted to sync equivalent

Conversion complete with 2 warnings
```

---

## Calor to C# Conversion

When converting Calor to C#, the converter generates idiomatic C# code:

```bash
calor convert Calculator.calr
```

Output includes:
- Proper C# namespaces and class structures
- Contract enforcement via runtime checks (optional)
- Effect documentation via XML comments
- Generated file header with timestamp

---

## Benchmark Comparison

Use `--benchmark` to see how the Calor version compares to C#:

```bash
calor convert PaymentService.cs --benchmark
```

Output:
```
Converting PaymentService.cs → PaymentService.calr

Benchmark Comparison:
┌─────────────────┬────────┬────────┬──────────┐
│ Metric          │ C#     │ Calor   │ Savings  │
├─────────────────┼────────┼────────┼──────────┤
│ Tokens          │ 1,245  │ 842    │ 32.4%    │
│ Lines           │ 156    │ 98     │ 37.2%    │
│ Characters      │ 4,521  │ 2,891  │ 36.1%    │
└─────────────────┴────────┴────────┴──────────┘

Conversion complete: PaymentService.calr
```

---

## Verbose Output

Use `--verbose` to see detailed conversion progress:

```bash
calor convert MyService.cs --verbose
```

Output:
```
Converting MyService.cs → MyService.calr

Parsing C# source...
  Found: 1 namespace, 2 classes, 8 methods, 3 properties

Converting constructs:
  [OK] Class: MyService → c001
  [OK] Method: ProcessOrder → f001
  [OK] Method: ValidateInput → f002
  [WARN] Method: FetchDataAsync → f003 (async converted to sync)
  [OK] Property: IsEnabled → y001
  ...

Detecting effects:
  f001: db, net (database write, HTTP call detected)
  f002: (pure)
  f003: net (HTTP call detected)

Generating contracts:
  f002: Added §Q (!= input null) from null check at line 24

Writing output: MyService.calr
Conversion complete with 1 warning
```

---

## Examples

### Basic Conversion

```bash
# Convert a service class
calor convert src/Services/UserService.cs

# Convert back to C#
calor convert src/Services/UserService.calr
```

### Batch Conversion with Shell

```bash
# Convert all C# files in a directory
for f in src/Services/*.cs; do
  calor convert "$f"
done
```

For project-wide conversion, use [`calor migrate`](/calor/cli/migrate/) instead.

### Integration with Claude Code

After conversion, use Claude to refine the Calor:

```
/calor

Review the converted file src/Services/UserService.calr and:
1. Add appropriate contracts based on the business logic
2. Verify effect declarations are complete
3. Improve naming of generated IDs if needed
```

---

## File Coexistence (.cs and .calr)

After converting a `.cs` file to `.calr`, both files will exist in your project. When you compile the `.calr` file, Calor generates a `.g.cs` file. If the original `.cs` file is still included in compilation, you will get **CS0101 duplicate type** errors because both files define the same types.

### Resolution strategies

**1. Exclude originals from compilation (recommended for incremental migration)**

Add the original `.cs` files to your `.csproj` exclusion list:

```xml
<ItemGroup>
  <Compile Remove="MyService.cs" />
</ItemGroup>
```

**2. Move originals to a reference directory**

```bash
mkdir -p .csharp-originals
mv MyService.cs .csharp-originals/
```

This preserves the originals for reference while removing them from compilation.

**3. Delete originals after verification**

Once you've verified the Calor version roundtrips correctly:

```bash
# Verify roundtrip first
calor convert MyService.calr -o /tmp/MyService.check.cs
diff MyService.cs /tmp/MyService.check.cs

# If satisfied, remove the original
rm MyService.cs
```

---

## Limitations

The converter may not perfectly handle:

- **Complex LINQ expressions** - May need manual adjustment
- **Async/await patterns** - Converted to synchronous equivalents
- **Dynamic types** - Not supported in Calor
- **Unsafe code** - Not supported in Calor
- **Preprocessor directives** - Ignored during conversion

Review the warnings and manually adjust as needed.

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | Conversion successful |
| `1` | Conversion completed with warnings |
| `2` | Error - file not found, parse error, etc. |

---

## See Also

- [calor migrate](/calor/cli/migrate/) - Convert entire projects
- [calor assess](/calor/cli/assess/) - Find best conversion candidates
- [calor benchmark](/calor/cli/benchmark/) - Detailed metrics comparison
- [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) - Complete migration guide
