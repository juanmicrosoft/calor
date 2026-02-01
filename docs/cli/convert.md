---
layout: default
title: convert
parent: CLI Reference
nav_order: 3
permalink: /cli/convert/
---

# opalc convert

Convert a single file between C# and OPAL.

```bash
opalc convert <input> [options]
```

---

## Overview

The `convert` command performs bidirectional conversion between C# and OPAL:

- **C# → OPAL**: Convert `.cs` files to OPAL syntax
- **OPAL → C#**: Convert `.opal` files to generated C#

The conversion direction is automatically detected from the input file extension.

---

## Quick Start

```bash
# Convert C# to OPAL
opalc convert MyService.cs

# Convert OPAL to C#
opalc convert MyService.opal

# Specify output path
opalc convert MyService.cs --output src/MyService.opal

# Include benchmark comparison
opalc convert MyService.cs --benchmark
```

---

## Arguments

| Argument | Required | Description |
|:---------|:---------|:------------|
| `input` | Yes | The source file to convert (`.cs` or `.opal`) |

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
| `MyFile.cs` | `MyFile.opal` |
| `MyFile.opal` | `MyFile.g.cs` |

---

## C# to OPAL Conversion

When converting C# to OPAL, the converter:

1. Parses the C# source code
2. Identifies supported constructs (classes, methods, properties, etc.)
3. Maps C# patterns to OPAL equivalents
4. Generates unique IDs for all structural elements
5. Adds effect declarations based on detected side effects
6. Suggests contracts based on validation patterns

### Supported Constructs

| C# Construct | OPAL Equivalent |
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
Converting MyService.cs → MyService.opal
  Warning: Complex LINQ query at line 42 - manual review recommended
  Warning: Async method at line 78 - converted to sync equivalent

Conversion complete with 2 warnings
```

---

## OPAL to C# Conversion

When converting OPAL to C#, the converter generates idiomatic C# code:

```bash
opalc convert Calculator.opal
```

Output includes:
- Proper C# namespaces and class structures
- Contract enforcement via runtime checks (optional)
- Effect documentation via XML comments
- Generated file header with timestamp

---

## Benchmark Comparison

Use `--benchmark` to see how the OPAL version compares to C#:

```bash
opalc convert PaymentService.cs --benchmark
```

Output:
```
Converting PaymentService.cs → PaymentService.opal

Benchmark Comparison:
┌─────────────────┬────────┬────────┬──────────┐
│ Metric          │ C#     │ OPAL   │ Savings  │
├─────────────────┼────────┼────────┼──────────┤
│ Tokens          │ 1,245  │ 842    │ 32.4%    │
│ Lines           │ 156    │ 98     │ 37.2%    │
│ Characters      │ 4,521  │ 2,891  │ 36.1%    │
└─────────────────┴────────┴────────┴──────────┘

Conversion complete: PaymentService.opal
```

---

## Verbose Output

Use `--verbose` to see detailed conversion progress:

```bash
opalc convert MyService.cs --verbose
```

Output:
```
Converting MyService.cs → MyService.opal

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

Writing output: MyService.opal
Conversion complete with 1 warning
```

---

## Examples

### Basic Conversion

```bash
# Convert a service class
opalc convert src/Services/UserService.cs

# Convert back to C#
opalc convert src/Services/UserService.opal
```

### Batch Conversion with Shell

```bash
# Convert all C# files in a directory
for f in src/Services/*.cs; do
  opalc convert "$f"
done
```

For project-wide conversion, use [`opalc migrate`](/opal/cli/migrate/) instead.

### Integration with Claude Code

After conversion, use Claude to refine the OPAL:

```
/opal

Review the converted file src/Services/UserService.opal and:
1. Add appropriate contracts based on the business logic
2. Verify effect declarations are complete
3. Improve naming of generated IDs if needed
```

---

## Limitations

The converter may not perfectly handle:

- **Complex LINQ expressions** - May need manual adjustment
- **Async/await patterns** - Converted to synchronous equivalents
- **Dynamic types** - Not supported in OPAL
- **Unsafe code** - Not supported in OPAL
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

- [opalc migrate](/opal/cli/migrate/) - Convert entire projects
- [opalc analyze](/opal/cli/analyze/) - Find best conversion candidates
- [opalc benchmark](/opal/cli/benchmark/) - Detailed metrics comparison
- [Adding OPAL to Existing Projects](/opal/guides/adding-opal-to-existing-projects/) - Complete migration guide
