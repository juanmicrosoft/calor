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

Preserved C# retains its source text and behavior. Generated files use UTF-8.
UTF-16/UTF-32 inputs therefore do **not** retain their original encoding or bytes.
Exact-byte controls cover preserved UTF-8 sources, including BOMs, CRLF, and EOF comments.
Malformed input encodings fail explicitly rather than using replacement text.

When no C# namespace supplies a module label, the converter derives one from the
filename and normalizes it to a valid Calor name. This file label does not change
CLR namespaces, type names, or static containers.
The API's `ConversionOptions.ModuleName` overrides only this file label.
Original global and named CLR namespace owners remain unchanged.

### Compilation context

Conversion and generated-output validation default to an implicit `System`
import, applied equally to original and emitted C#. Use `--no-implicit-system`
for standalone validation. JSON output records the choice as `data.implicitSystem`.
No other SDK namespace is implied. Preserved text and namespace-local imports
are not rewritten, and preserved code remains non-native.

The downstream C# project must supply the same context. Calor SDK/init projects
add the real `Using` item; compiling a saved `.g.cs` elsewhere does not inherit
the CLI's validation context. Short-name ambiguity reports `CS0104`; use an alias
or a fully qualified name. See [compilation-context imports](../semantics/dotnet-backend.md#compilation-context-imports)
for API defaults, project opt-out and editor settings.

Lossless conversion can use the native file-header field `nullable oblivious`
when the resolved original declarations have disabled nullable annotations.
It retains explicit nullable annotations, including nested array and generic
annotations, instead of replacing unknown reference types with nullable types.
The converter admits this replacement only after strict compilation of the
actual emitted/reparsed Calor, C# compilation, and an exact comparison of declared
owner/type/nullability metadata. Already faithful native output is retained.
Mixed `#nullable` contexts, unresolved source context, or an uncertified native
replacement retain the preservation boundary; raw C# is not native recovery.
Conversion options and the default compilation context do not change.

Static calls on source generic types use the selected constructed method signature,
not a receiver name with its generic arguments removed. Namespace and alias lookup,
arity, constraints, overloads, and nullable annotations must still resolve.
Calls charge both the selected method and its declaring type's initialization
effects. An unproved initializer remains unknown; this rule grants no new library
effect permissions.

For directly invoked delegate parameters, conversion can synthesize existing
effect placeholders (`<! callback0>`) instead of assuming callbacks are pure.
Each parameter gets its own row variable; invocation charges that row and the
effects of evaluating its arguments. Effect binders erase without changing CLR
signatures or generic arity. Returned lambdas receive a concrete row only when
strict native inference verifies every return, including conditional branches.
Unknown or assumed facts cannot establish a concrete return contract.
Aliases, stored or captured callbacks, non-lambda returns, and dispatch contracts
are outside this synthesis unit and retain their existing fail-closed behavior.
These contracts do not establish whole-program equivalence.

### Generated output and refresh

C#→Calor conversion saves an adjacent `<output>.conversion.json` receipt containing
the original C# source, parse options, observed inputs and exact generated Calor.
Keep this receipt with its output; it contains the original source, not just hashes.
Generated Calor validation is mandatory before either file is written.

An unchanged generated file can be refreshed from its original C# path. Edited
outputs, missing receipts and outputs associated with another original path are
refused without overwriting either file. Keep those files and convert to a new
destination. A neighboring `.conversion.lock` serializes cooperating conversions.

Output and receipt are not one atomic transaction. If the receipt cannot be saved
after output commits, the command reports the written output and exits with an
error. A missing or mismatched receipt blocks automatic refresh. Receipts detect
edit conflicts and associate original context; they do not authenticate converter
origin or prove that a preservation reason is still necessary.

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
| `--no-implicit-system` | — | `false` | Disable the System-only context import for original and generated C#. |
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
| `--explicit-call-closers` | — | `false` | Legacy compatibility option; current native calls use parentheses and never emit closing tags. |
| `--format` | — | `text` | Output format: `text` or `json` (envelope document on stdout) — see [JSON Output](#json-output---format-json). No `-f` short alias |

---

## JSON Output (`--format json`)

With `--format json` stdout carries exactly one
[envelope document](/calor/cli/envelope-schema/) — all human-oriented status
moves to stderr, and a document is emitted on every path (success, failure,
timeout, crash). Exit codes are unchanged.

```json
{
  "version": "2.0",
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
  accounting: every converter-owned C# interop preservation, TODO fallback, dropped
  construct, and stripped preprocessor directive, each with
  `kind`, `feature`, `file`, `line`, and `description`. `lossCount: 0` means
  the output is fully native Calor. Interop preservation is lossless but not
  native; the CLI does not allow substitutions or drops. In text mode a conversion with losses
  prints a located loss summary instead of the `✓ Conversion successful` line.
- `data.benchmark` — present with `--benchmark`: token/line/character counts
  before and after, reduction percentages, and `compactnessRatio`. The legacy
  `advantageRatio` alias remains in schema v2.0 for compatibility.

If `--output` is not specified:

| Input | Output |
|:------|:-------|
| `MyFile.cs` | `MyFile.calr` |
| `MyFile.calr` | `MyFile.g.cs` |

---

## C# to Calor Conversion

C# static classes convert to **named modules**, using a nested `module`
declaration and `func` members. Their C# names are unchanged: a module
converted from `Example.Utilities` still emits `Example.Utilities`, not a new
wrapper type. Nested modules retain their containing type. Static fields,
properties, constructors, attributes, generic parameters and constraints retain
their member metadata.

Extension functions keep their `this` parameter and must belong to a non-nested,
non-generic module, as required by C#. Shapes outside native conversion support,
including by-reference extension receivers, static events, unsafe type declarations and
attributes on generic type parameters, remain explicitly reported C#
interop rather than losing part of their signature. Modules and classes cannot
share a name and generic arity in the same namespace or containing type
(`Calor0201`). Native static containers use named modules, not a static class
modifier. Ordinary non-static classes may still declare static members.
The first `module` labels the file and carries its optional namespace; it is
not itself the static container.

When converting C# to Calor, the converter:

1. Parses the C# source code
2. Identifies supported constructs (classes, methods, properties, etc.)
3. Maps C# patterns to Calor equivalents
4. Maintains declaration identity in the project `.calor/ids.json` sidecar
5. Adds effect declarations based on detected side effects
6. Reports conversion issues and preservation paths for review

The contract is **lossless**: try native conversion, then preserve unsupported
code at a complete member, type, or whole-file boundary. Failed generated-Calor
or C# validation escalates preservation rather than discarding a buildable input.
Whole-file passthrough reproduces the original C# without a generated preamble.
Reports retain the reasons for escalation and never count passthrough as native.

Object and collection initializers convert natively only at positions where their
evaluation order is preserved. An unsupported position, such as the receiver in
`new Box { Value = 2 }.Value`, preserves the complete containing member, not an
opaque expression inside a native method. The reported interop feature and raw
payload identify that boundary. Initializers inside a preserved type or whole
file are not native. A preserved callback body grants no native effect contract.

Local collection bindings (`let` or `var`) are not class members. They produce
`Calor0100`; the parser consumes the invalid binding and retains later fields,
methods and types for diagnostics. Use a `field` or `property` initializer instead.

Writing and building are separate outcomes. Exact original C# can be written
when a standalone file lacks its project references; that result is
`unverified`, with `roundTrip: "failed"` and exit `2`, not a passed build.
Use the original project references and configuration to verify it. Malformed,
missing, unreadable inputs and write failures remain errors. The destructive
`--lossy` and `--select-active-preprocessor-branch-lossy` options were removed.

### Conditional compilation and compiler directives

C# → Calor conversion preserves conditional compilation by default. Every
`#if`/`#elif`/`#else` branch is represented by native `pp` branches, including
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
| One supported namespace scope | Optional namespace after the file's `module` label |
| Static class | Nested named `module` |
| `class` | `class Name` with native visibility/modifiers |
| `method` | `func Name (Type:parameter) → ReturnType` |
| `property` | `property Type:Name` with indented accessors |
| `field` | `field Type:name` |
| `if/else if/else` | Native indented branches (end at dedent) |
| `for` loop | Reevaluated `for i = start; condition; i = next`; inclusive snapshot ranges are a separate form |
| `while` loop | `while condition` |
| `try/catch` | Native `try` / `catch Type:name` |
| `?.`, `??` | Nullable references and null-conditional / null-coalescing expressions |

### Conversion Warnings

The converter reports preserved constructs and actual validation failures.
It does not replace asynchronous execution with synchronous execution.
For example, an iterator accessor retains its original C# member and is
reported as preservation rather than native iterator-accessor support:

```
Converting MyService.cs → MyService.calr
  [iterator-accessor] Original C# accessor preserved
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
- Ordinary CLR calls after compile-time effect checks
- A generated-file marker (not a timestamp or semantics-version attestation)

---

## Benchmark Comparison

Use `--benchmark` to see how the Calor version compares to C#:

```bash
calor convert PaymentService.cs --benchmark
```

Illustrative counts, not a current measured score:
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

Illustrative progress, not an exact CLI transcript:
```
Converting MyService.cs → MyService.calr

Parsing C# source...
  Found: 1 namespace, 2 classes, 8 methods, 3 properties

Converting constructs:
  [OK] Class: MyService → c001
  [OK] Method: ProcessOrder → f001
  [OK] Method: ValidateInput → f002
  [OK] Method: FetchDataAsync → f003 (async behavior retained)
  [OK] Property: IsEnabled → y001
  ...

Detecting effects:
  f001: db, net (database write, HTTP call detected)
  f002: (pure)
  f003: net (HTTP call detected)

Reviewing contracts:
  Keep validation behavior; add requires input != null only after checking the API contract.

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
3. Use identity-aware tools for declaration renames; retain the sidecar
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
- **Async/await patterns** - Preserve their carrier and scheduling behavior;
  unsupported shapes remain C# rather than becoming synchronous
- **Dynamic and unsafe code** - Check the feature registry and actual conversion
  report; a native node does not prove complete analysis or effect coverage
- **Preprocessor directives** - Retain branches and directive scope as described above

Review the warnings and manually adjust as needed.

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | Conversion completed; read `conversionPaths.outcome` to distinguish native from preserved output |
| `1` | Invalid or missing input, timeout, filesystem failure, or any refused conversion (`outcome: "refused"`; nothing written) |
| `2` | Exact original C# was preserved and written, but standalone compilation failed (`outcome: "unverified"`). Validate with the original project context. |

---

## See Also

- [calor migrate](/calor/cli/migrate/) - Convert entire projects
- [calor assess](/calor/cli/assess/) - Find best conversion candidates
- [calor benchmark](/calor/cli/benchmark/) - Detailed metrics comparison
- [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) - Complete migration guide
