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
```

---

## Options

| Option | Short | Required | Description |
|:-------|:------|:---------|:------------|
| `--input` | `-i` | Yes | Input Calor source file |
| `--output` | `-o` | Yes | Output C# file path |
| `--verbose` | `-v` | No | Show detailed compilation output |
| `--verify` | | No | Enable static contract verification with Z3 |
| `--no-cache` | | No | Disable verification result caching |
| `--clear-cache` | | No | Clear verification cache before compiling |
| `--contract-mode` | | No | Contract enforcement mode: off, debug, release (default: debug) |
| `--strict-api` | | No | Require §BREAKING markers for public API changes |
| `--require-docs` | | No | Require documentation on public functions |
| `--enforce-effects` | | No | Enforce effect declarations (default: true) |

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
5. **Write** - Save to the output file

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

  §R (+ x 1)
       ^

Compilation failed with 1 error
```

For machine-readable error output, use [`calor diagnose`](/calor/cli/diagnose/).

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
| `1` | Compilation failed (syntax errors, validation errors) |
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
warning Calor0702: Postcondition may be violated in function 'BadFunction'. Counterexample: x=0, y=1
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
- [calor diagnose](/calor/cli/diagnose/) - Machine-readable diagnostics
- [calor format](/calor/cli/format/) - Format Calor source files
- [Getting Started](/calor/getting-started/) - Installation and first program
