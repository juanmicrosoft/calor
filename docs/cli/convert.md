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
| `input` | Yes | The source file to convert (`.cs`, `.csx`, or `.calr`) |

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--output` | `-o` | Auto-detected | Output file path |
| `--benchmark` | `-b` | `false` | Include benchmark metrics comparison |
| `--verbose` | `-v` | `false` | Enable verbose output |
| `--lossy` | — | `false` | Explicitly allow reported substitutions or drops. Without this flag, unsupported code is preserved verbatim and both generated Calor and round-tripped C# must validate before the destination is replaced. |
| `--select-active-preprocessor-branch-lossy` | — | `false` | Explicitly lossy: evaluate conditional compilation through Roslyn and retain only the active branch. This implies lossy fidelity. |
| `--define` | — | — | Add a conditional-compilation symbol. Repeat or provide multiple values. |
| `--configuration` | — | — | Configuration recorded in conversion metadata. |
| `--framework` | — | — | Target framework recorded in conversion metadata. |
| `--language-version` | — | `preview` | C# language version used by Roslyn. |
| `--documentation-mode` | — | `parse` | Roslyn documentation mode: `none`, `parse`, or `diagnose`. |
| `--source-kind` | — | `regular` | Roslyn source kind: `regular` or `script`. Script units use whole-unit passthrough, including `#r` and `#load`. |
| `--feature` | — | — | Roslyn parse feature in `key=value` form. Repeat for multiple features. |
| `--reference` | — | — | Metadata reference path, optionally `path=alias1,alias2` for extern aliases. |
| `--validate` | — | — | Compatibility flag. Generated Calor validation is now mandatory in every mode. |
| `--passthrough` | — | `false` | Preserve unconvertible members as raw C# interop blocks. Lossless mode already preserves unsupported descendants at the nearest complete boundary. |
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
    "fidelity": "lossless",
    "unsupportedFeatureCount": 1,
    "featureCounts": { "local-functions": 1 },
    "validated": true
  }
}
```

- `diagnostics[]` — conversion issues (`Calor1343`, severity mirrors the
  issue, message prefixed with the feature name when known), generated-output
  parse errors (`Calor1344`, error — the destination remains unchanged), and
  command-level failures (`Calor1345`: input not found, unknown file type,
  timeout, crash). Converting Calor → C#, compiler diagnostics appear with
  their own codes.
- `data.direction` — `csharp-to-calor` | `calor-to-csharp`.
- `data.lossCount` / `data.losses[]` — C# → Calor structured semantic-loss
  accounting: every §CSHARP interop preservation, TODO fallback, dropped
  construct, and stripped preprocessor directive, each with
  `kind`, `feature`, `file`, `line`, and `description`. `lossCount: 0` means
  the output is fully native Calor. Interop preservation is lossless but not
  native; substitutions and drops require `--lossy`. In text mode a conversion with losses
  prints a located loss summary instead of the `✓ Conversion successful` line.
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

The default contract is **lossless**: unsupported descendants bubble to the
nearest complete member or type and are preserved verbatim as C# interop.
Success means the emitted Calor parses and compiles, and its generated C#
compiles through Roslyn. Output files are replaced atomically only after these
checks pass. `--lossy` permits substitutions or drops, but every occurrence is
reported with its source location.

### Conditional compilation and compiler directives

C# → Calor conversion preserves conditional compilation by default. Every
`#if`/`#elif`/`#else` branch is represented as explicit `§PP` AST, including
conditional usings, declarations and partial types, class/struct members, and
method statements. Conversion API callers can supply `CSharpParseOptions`,
additional defined symbols, and a configuration name; these are recorded in
`ConversionResult.Metadata`.

The API's `SelectActiveBranchLossy` mode is an explicit lossy opt-in. It asks
Roslyn to evaluate conditions with the supplied symbols, keeps only the active
branch, and records every removed conditional directive as a conversion loss.
Inactive `#nullable`, `#pragma`, `#warning`, `#error`, `#line`, `#define`, and
`#undef` directives are removed individually with distinct losses; active
directives remain effective.

Compiler-affecting directives (`#nullable`, `#pragma`, `#warning`, `#error`,
and `#line`) are preserved verbatim in source order. They remain explicit raw
interop boundaries in the loss ledger. A preserved `#error` correctly makes
round-trip compilation fail while retaining the directive in returned output.
Conditional placements without a structural Calor model (top-level statements,
expression fragments, accessors, lambdas, and similar contexts) preserve their
complete enclosing C# boundary as scoped interop rather than selecting a branch.
Conditional top-level statements use true compilation-unit passthrough: generated
headers, implicit usings, namespaces, and dependency injection are not prepended.
Namespace-wrapped interop is likewise emitted at compilation-unit scope without
string-based namespace stripping.
Compilation-unit attributes, extern aliases, active `#line` mappings, and C#
scripts are conservatively preserved through whole-unit passthrough. Generated
`#nullable enable` is omitted whenever the source carries an explicit nullable
directive.
`.csx` files route automatically to C# → Calor script conversion. Script
validation uses Roslyn script compilation with real metadata/source resolvers;
missing `#r` or `#load` targets are errors rather than silently accepted.

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
