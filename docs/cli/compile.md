---
layout: default
title: compile (default)
parent: CLI Reference
nav_order: 0
permalink: /cli/compile/
---

# calor (compile)

Compile Calor source files to C#.

```bash
calor --input <file.calr> --output <file.cs>
```

---

## Overview

The default `calor` command (when no subcommand is specified) compiles Calor source files to C#. This is the core functionality of the Calor compiler.

---

## Quick Start

```bash
# Compile a single file
calor --input MyModule.calr --output MyModule.g.cs

# Short form
calor -i MyModule.calr -o MyModule.g.cs

# With verbose output
calor -v -i MyModule.calr -o MyModule.g.cs

# Compile a project together; write one .g.cs beside each input
calor -i Library.calr -i App.calr --strict-effects --no-cache
```

Repeat `-i` for every source file in a batch. Omit `-o` for a multi-file
invocation: one explicit output path would be shared by all inputs.
Calls to another file's named static module use its declared CLR namespace
and owner, for example `Contracts.Arithmetic.Pick(41)`. Binding selects the
actual source overload in the caller's namespace and import context, and
effect checking charges that declaration's row along with argument effects.
The project index uses the same source selections for call edges.
Ambiguous, inaccessible, or invalid calls remain unresolved; they do not
borrow another source or metadata contract. Foreign instance methods and
owners with static initialization are not closed by this source-call path.
Refused calls retain receiver evaluation before argument evaluation. Known
receiver and argument effects remain enforced under `--permissive-effects`.
Explicit `ref` and `out` arguments also invalidate pre-call nullability
narrowing, even when the foreign call remains unresolved.

---

## Options

| Option | Short | Required | Description |
|:-------|:------|:---------|:------------|
| `--input` | `-i` | Yes | Input Calor source file |
| `--output` | `-o` | Yes | Output C# file path |
| `--reference` | `-r` | No | Assembly reference used by Roslyn validation; repeat for multiple project or package assemblies |
| `--verbose` | `-v` | No | Show detailed compilation output |
| `--verify` | | No | Enable static contract verification with Z3 |
| `--analyze` | | No | Enable static analysis (dataflow, bug patterns, taint tracking) |
| `--all-findings` | | No | Include unverified bug-pattern hints (`Calor0940`–`Calor0944`) in addition to verified findings; incomplete typed analysis is always surfaced as `Calor0929` (requires `--analyze`) |
| `--permissive-effects` | | No | Suppress unknown-effect warnings (recommended for converted code) |
| `--cache` | | No | Enable the incremental-build cache — see below (opt-in) |
| `--no-cache` | | No | Disable caching (verification results and incremental builds; overrides `--cache`) |
| `--clear-cache` | | No | Clear caches before compiling (verification cache and `.calor-build-state.json`) |
| `--contract-mode` | | No | Contract enforcement mode: off, debug, release (default: debug) |
| `--strict-api` | | No | Require constrained `/// breaking version "description"` metadata for public API changes |
| `--require-docs` | | No | Require documentation on public functions |
| `--enforce-effects` | | No | Enforce effect declarations (default: true) |
| `--no-enforce-effects` | | No | Disable effect enforcement (opt out of the v0.11 default-on behavior) |
| `--no-type-check` | | No | Disable Calor semantic type checking. Generated C# is still validated by Roslyn before success is reported |
| `--transpile-only` | | No | **Unsafe:** emit C# without Calor type checking or Roslyn validation. Output is not cached and the CLI reports `Unsafe transpilation output written`, not compilation success |

---

## Incremental builds (`--cache`, opt-in)

With `--cache`, the compiler persists `.calor-build-state.json` at the common
ancestor of the inputs (next to the generated `.g.cs` files) and skips files
that are provably unchanged: same source content hash, same recorded output
content hash, options/compiler/effect-manifests unchanged, and a cached
per-module effect summary available so cross-module effect enforcement still
runs over skipped modules.

For multi-file builds, changes to emitted cross-file targets or source paths
invalidate the project cache. Other source-only edits can preserve unchanged
outputs only after a conservative independence check against both the old and
current companion declarations. Unproven cases, including conditional branches,
recompile rather than reuse a potentially stale output. Native top-level
functions may share the compiler-generated `GlobalModule`; reuse still checks
both the old and current companion member names. Authored partial types, fields,
and static constructors do not qualify for that shared-owner exception.
Validation records for warning-bearing files can support that check, but those
files themselves recompile so their diagnostics are reported again.

```bash
calor --input a.calr --input b.calr --cache    # first run: compiles both
calor --input a.calr --input b.calr --cache    # second run: skips both
```

**Output-shape difference:** a skipped file prints
`Up-to-date (cached): <path>.g.cs` instead of
`Compilation successful: <path>.g.cs`. Tools that scrape per-file success
lines should account for both, or pass `--no-cache`.

Caching is **opt-in** for plain compiles; `calor watch` always caches
(see [watch](/calor/cli/watch/)). `--no-cache` is the explicit off switch and
overrides `--cache`; `--clear-cache` deletes the state file first. Add
`.calor-build-state.json` to `.gitignore` (`calor init` does this for you).
Only diagnostic-clean, Roslyn-valid files are cached, so warnings reappear on
every run and invalid generated output can never become a cache hit. A failed
compile removes any prior output for the failed file, so downstream C# globs
cannot consume stale generated code.

---

## Validated compilation and unsafe transpilation

Normal compilation guarantees that all generated C# from the invocation
compiles successfully with Roslyn. For multi-file input, Calor validates every
generated syntax tree together so cross-file symbols resolve, using the
framework, `Calor.Runtime`, and project references supplied by the invoking
CLI or MSBuild surface. Roslyn errors are reported as `Calor1002` diagnostics
and mapped back to `.calr` locations through generated `#line` directives.

`--no-type-check` disables only Calor's semantic type checker; it does not
disable the generated-output guarantee. Use `--transpile-only` only for
syntax/emitter inspection of incomplete source. This mode is explicitly
unsafe, does not populate the incremental cache, and does not emit the normal
success message.

For MSBuild projects, validation also includes existing `@(Compile)` C# files
and honors `AllowUnsafeBlocks`, `OutputType`, `DefineConstants`, and
`LangVersion`, including whether `ImplicitUsings` is enabled.
`<CalorTranspileOnly>true</CalorTranspileOnly>` provides the same unsafe
behavior as the CLI; the default is `false`.

Effect checking also covers formatting performed by interpolation. A numeric
hole can invoke a custom `CurrentCulture.NumberFormat` getter even when evaluating
the number has no effects. The compiler does not prove that dispatch pure, so it charges
Unknown (`Calor0411`, and `Calor0410` in a pure function), including format and
alignment variants. String, character, Boolean, and null holes remain supported
when the Roslyn binding image proves their provider-independent types or null
values. Custom-object, generic, and unresolved holes do not receive a purity
grant. Effects from evaluating every runtime hole are still charged.

The same boundary applies to Roslyn-selected built-in string concatenation,
including numeric operands on either side of `+`, chained concatenation, and
converted `+=` expressions. Standard boxing does not hide a numeric operand's
formatting dispatch. String, character, Boolean, and proven null operands remain
supported; custom-object, generic, and unresolved formatting remains Unknown.
Numeric addition is not string formatting. User-defined operators and operand
conversions retain their own effect charges, as do operand evaluation,
assignment targets, and bound source initialization. This includes bare
static-field reads and explicit static-constructor entry without a field read.
Implicit static initializers are also checked at their type declaration, against
the same intrinsic mutation/allocation contract as explicit constructors.
`BeforeFieldInit` permits eager CLR initialization; an empty callback trace from
one method call is not a purity proof. Uncontracted formatting is refused even
when the type has no explicit static constructor and its method reads no fields.
String interpolation in initializers uses the same bound hole-type rule as
interpolation in method bodies. Proven string, char, Boolean and null holes are
supported; operand and alignment operations retain their effect checks.

---

## Output Convention

The recommended convention for generated C# files is the `.g.cs` extension:

```
MyModule.calr → MyModule.g.cs
```

This indicates "generated C#" and helps distinguish Calor-generated code from hand-written C#.

---

## Compilation Process

The compiler performs these steps:

1. **Parse** - Read and parse the Calor source file
2. **Validate** - Check syntax and semantic correctness
3. **Transform** - Convert Calor AST to C# AST
4. **Generate** - Emit formatted C# source code
5. **Roslyn validate** - Compile all generated trees together with project references
6. **Write** - Save validated output to the output file

---

## Verbose Output

Use `--verbose` to see compilation details:

```bash
calor -v -i Calculator.calr -o Calculator.g.cs
```

Output:
```
Compiling Calculator.calr...
  Parsing: OK
  Validating: OK
  Modules: 1
  Functions: 3
  Classes: 0
  Lines of Calor: 24
  Lines of C#: 42
Output: Calculator.g.cs
Compilation successful
```

---

## Error Reporting

When compilation fails, errors are reported with file location:

```
Error in Calculator.calr:12:5
  Undefined variable 'x' in expression

  return x + 1
         ^

Compilation failed with 1 error
```

For machine-readable diagnostics, use the `calor_check` MCP tool (`action: "diagnose"`).

---

## Integration with MSBuild

For automatic compilation during `dotnet build`, use [`calor init`](/calor/cli/init/) to set up MSBuild integration. This eliminates the need to run `calor` manually.

After initialization:

```bash
# Calor files compile automatically
dotnet build
```

---

## Batch Compilation

To compile multiple files, use shell scripting:

```bash
# Compile all .calr files in a directory
for f in src/*.calr; do
  calor -i "$f" -o "${f%.calr}.g.cs"
done
```

Or use the MSBuild integration which handles this automatically.

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | Compilation successful |
| `1` | Compilation failed (Calor errors or invalid generated C#) |
| `2` | Error (file not found, invalid arguments) |

---

## Examples

### Compile and Run

```bash
# Compile Calor to C#
calor -i Program.calr -o Program.g.cs

# Build and run with .NET
dotnet run
```

### Compile to Specific Directory

```bash
# Output to build directory
calor -i src/MyModule.calr -o build/generated/MyModule.g.cs
```

### Watch Mode (using external tools)

```bash
# Using fswatch (macOS)
fswatch -o src/*.calr | xargs -n1 -I{} calor -i src/MyModule.calr -o src/MyModule.g.cs

# Using inotifywait (Linux)
while inotifywait -e modify src/*.calr; do
  calor -i src/MyModule.calr -o src/MyModule.g.cs
done
```

---

## Static Contract Verification

Use `--verify` to enable static contract verification with the Z3 SMT solver:

```bash
calor -i MyModule.calr -o MyModule.g.cs --verify
```

When enabled, the compiler uses the Z3 theorem prover to statically verify contracts:

- **Proven contracts**: Runtime checks are replaced with comments, improving performance
- **Disproven contracts**: A warning is emitted showing a counterexample
- **Unproven contracts**: Runtime checks are kept (Z3 timeout or complexity limit reached)
- **Unsupported contracts**: Runtime checks are kept (contracts with function calls, strings, etc.)

Example output:
```
Contract verification complete: 3 proven, 1 unproven, 1 potentially violated, 0 unsupported
warning Calor0712: Postcondition may be violated in function 'BadFunction'. Counterexample: x=0, y=1
```

For proven contracts, the generated C# includes:
```csharp
// PROVEN: Postcondition statically verified: (result >= 0)
```

For more details, see [Static Verification](/calor/philosophy/static-verification/).

---

## Verification Caching

When using `--verify`, the compiler caches Z3 verification results to avoid redundant SMT solver invocations on subsequent compilations. This can dramatically improve compile times for projects with stable contracts.

### How It Works

- **Cache key**: SHA256 hash of the contract expression and parameter types
- **Cache location**: `~/.calor/cache/z3/` (user-level) or `.calor/verification-cache/` (project-level)
- **Cache invalidation**: Automatic when contract expression changes

### Cache Options

```bash
# Default: caching enabled
calor -i MyModule.calr -o MyModule.g.cs --verify

# Disable caching (useful for CI or debugging)
calor -i MyModule.calr -o MyModule.g.cs --verify --no-cache

# Clear cache before verification
calor -i MyModule.calr -o MyModule.g.cs --verify --clear-cache
```

### Performance Impact

First compilation runs Z3 for each contract (can take seconds per complex contract). Subsequent compilations with unchanged contracts return cached results in milliseconds.

---

## See Also

- [calor init](/calor/cli/init/) - Set up automatic compilation with MSBuild
- [calor format](/calor/cli/format/) - Format Calor source files
- [Getting Started](/calor/getting-started/) - Installation and first program
