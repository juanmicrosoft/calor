---
layout: default
title: convert
parent: CLI Reference
nav_order: 3
permalink: /cli/convert/
---

# calorc convert

Convert a single file between C# and Calor.

```bash
calorc convert <input> [options]
```

---

## Overview

The `convert` command performs bidirectional conversion between C# and Calor:

- **C# → Calor**: Convert `.cs` files to Calor syntax
- **Calor → C#**: Convert `.calor` files to generated C#

The conversion direction is automatically detected from the input file extension.

---

## Quick Start

```bash
# Convert C# to Calor
calorc convert MyService.cs

# Convert Calor to C#
calorc convert MyService.calor

# Specify output path
calorc convert MyService.cs --output src/MyService.calor

# Include benchmark comparison
calorc convert MyService.cs --benchmark
```

---

## Arguments

| Argument | Required | Description |
|:---------|:---------|:------------|
| `input` | Yes | The source file to convert (`.cs` or `.calor`) |

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--output` | `-o` | Auto-detected | Output file path |
| `--benchmark` | `-b` | `false` | Include benchmark metrics comparison |
| `--verbose` | `-v` | `false` | Enable verbose output |

---

## Auto-Detected Output Paths

If `--output` is not specified:

| Input | Output |
|:------|:-------|
| `MyFile.cs` | `MyFile.calor` |
| `MyFile.calor` | `MyFile.g.cs` |

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
| `namespace` | `§M[id:Name]` module |
| `class` | `§C[id:Name:vis]` class |
| `method` | `§F[id:Name:vis]` function |
| `property` | `§Y[id:Name:vis:type]` property |
| `field` | `§D[id:type:name]` field |
| `if/else if/else` | `§IF[id]...§EI...§EL...§/I[id]` |
| `for` loop | `§L[id:var:from:to:step]` |
| `while` loop | `§W[id]` |
| `try/catch` | Converted to `Result<T,E>` pattern |
| `?.`, `??` | Converted to `Option<T>` pattern |

### Conversion Warnings

The converter reports patterns it can't perfectly translate:

```
Converting MyService.cs → MyService.calor
  Warning: Complex LINQ query at line 42 - manual review recommended
  Warning: Async method at line 78 - converted to sync equivalent

Conversion complete with 2 warnings
```

---

## Calor to C# Conversion

When converting Calor to C#, the converter generates idiomatic C# code:

```bash
calorc convert Calculator.calor
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
calorc convert PaymentService.cs --benchmark
```

Output:
```
Converting PaymentService.cs → PaymentService.calor

Benchmark Comparison:
┌─────────────────┬────────┬────────┬──────────┐
│ Metric          │ C#     │ Calor   │ Savings  │
├─────────────────┼────────┼────────┼──────────┤
│ Tokens          │ 1,245  │ 842    │ 32.4%    │
│ Lines           │ 156    │ 98     │ 37.2%    │
│ Characters      │ 4,521  │ 2,891  │ 36.1%    │
└─────────────────┴────────┴────────┴──────────┘

Conversion complete: PaymentService.calor
```

---

## Verbose Output

Use `--verbose` to see detailed conversion progress:

```bash
calorc convert MyService.cs --verbose
```

Output:
```
Converting MyService.cs → MyService.calor

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

Writing output: MyService.calor
Conversion complete with 1 warning
```

---

## Examples

### Basic Conversion

```bash
# Convert a service class
calorc convert src/Services/UserService.cs

# Convert back to C#
calorc convert src/Services/UserService.calor
```

### Batch Conversion with Shell

```bash
# Convert all C# files in a directory
for f in src/Services/*.cs; do
  calorc convert "$f"
done
```

For project-wide conversion, use [`calorc migrate`](/calor/cli/migrate/) instead.

### Integration with Claude Code

After conversion, use Claude to refine the Calor:

```
/calor

Review the converted file src/Services/UserService.calor and:
1. Add appropriate contracts based on the business logic
2. Verify effect declarations are complete
3. Improve naming of generated IDs if needed
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

- [calorc migrate](/calor/cli/migrate/) - Convert entire projects
- [calorc analyze](/calor/cli/analyze/) - Find best conversion candidates
- [calorc benchmark](/calor/cli/benchmark/) - Detailed metrics comparison
- [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) - Complete migration guide
