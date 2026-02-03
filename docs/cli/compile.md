---
layout: default
title: compile (default)
parent: CLI Reference
nav_order: 0
permalink: /cli/compile/
---

# calorc (compile)

Compile Calor source files to C#.

```bash
calorc --input <file.calor> --output <file.cs>
```

---

## Overview

The default `calorc` command (when no subcommand is specified) compiles Calor source files to C#. This is the core functionality of the Calor compiler.

---

## Quick Start

```bash
# Compile a single file
calorc --input MyModule.calor --output MyModule.g.cs

# Short form
calorc -i MyModule.calor -o MyModule.g.cs

# With verbose output
calorc -v -i MyModule.calor -o MyModule.g.cs
```

---

## Options

| Option | Short | Required | Description |
|:-------|:------|:---------|:------------|
| `--input` | `-i` | Yes | Input Calor source file |
| `--output` | `-o` | Yes | Output C# file path |
| `--verbose` | `-v` | No | Show detailed compilation output |

---

## Output Convention

The recommended convention for generated C# files is the `.g.cs` extension:

```
MyModule.calor → MyModule.g.cs
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
calorc -v -i Calculator.calor -o Calculator.g.cs
```

Output:
```
Compiling Calculator.calor...
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
Error in Calculator.calor:12:5
  Undefined variable 'x' in expression

  §R (+ x 1)
       ^

Compilation failed with 1 error
```

For machine-readable error output, use [`calorc diagnose`](/calor/cli/diagnose/).

---

## Integration with MSBuild

For automatic compilation during `dotnet build`, use [`calorc init`](/calor/cli/init/) to set up MSBuild integration. This eliminates the need to run `calorc` manually.

After initialization:

```bash
# Calor files compile automatically
dotnet build
```

---

## Batch Compilation

To compile multiple files, use shell scripting:

```bash
# Compile all .calor files in a directory
for f in src/*.calor; do
  calorc -i "$f" -o "${f%.calor}.g.cs"
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
calorc -i Program.calor -o Program.g.cs

# Build and run with .NET
dotnet run
```

### Compile to Specific Directory

```bash
# Output to build directory
calorc -i src/MyModule.calor -o build/generated/MyModule.g.cs
```

### Watch Mode (using external tools)

```bash
# Using fswatch (macOS)
fswatch -o src/*.calor | xargs -n1 -I{} calorc -i src/MyModule.calor -o src/MyModule.g.cs

# Using inotifywait (Linux)
while inotifywait -e modify src/*.calor; do
  calorc -i src/MyModule.calor -o src/MyModule.g.cs
done
```

---

## See Also

- [calorc init](/calor/cli/init/) - Set up automatic compilation with MSBuild
- [calorc diagnose](/calor/cli/diagnose/) - Machine-readable diagnostics
- [calorc format](/calor/cli/format/) - Format Calor source files
- [Getting Started](/calor/getting-started/) - Installation and first program
