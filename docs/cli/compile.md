---
layout: default
title: compile (default)
parent: CLI Reference
nav_order: 0
permalink: /cli/compile/
---

# opalc (compile)

Compile OPAL source files to C#.

```bash
opalc --input <file.opal> --output <file.cs>
```

---

## Overview

The default `opalc` command (when no subcommand is specified) compiles OPAL source files to C#. This is the core functionality of the OPAL compiler.

---

## Quick Start

```bash
# Compile a single file
opalc --input MyModule.opal --output MyModule.g.cs

# Short form
opalc -i MyModule.opal -o MyModule.g.cs

# With verbose output
opalc -v -i MyModule.opal -o MyModule.g.cs
```

---

## Options

| Option | Short | Required | Description |
|:-------|:------|:---------|:------------|
| `--input` | `-i` | Yes | Input OPAL source file |
| `--output` | `-o` | Yes | Output C# file path |
| `--verbose` | `-v` | No | Show detailed compilation output |

---

## Output Convention

The recommended convention for generated C# files is the `.g.cs` extension:

```
MyModule.opal → MyModule.g.cs
```

This indicates "generated C#" and helps distinguish OPAL-generated code from hand-written C#.

---

## Compilation Process

The compiler performs these steps:

1. **Parse** - Read and parse the OPAL source file
2. **Validate** - Check syntax and semantic correctness
3. **Transform** - Convert OPAL AST to C# AST
4. **Generate** - Emit formatted C# source code
5. **Write** - Save to the output file

---

## Verbose Output

Use `--verbose` to see compilation details:

```bash
opalc -v -i Calculator.opal -o Calculator.g.cs
```

Output:
```
Compiling Calculator.opal...
  Parsing: OK
  Validating: OK
  Modules: 1
  Functions: 3
  Classes: 0
  Lines of OPAL: 24
  Lines of C#: 42
Output: Calculator.g.cs
Compilation successful
```

---

## Error Reporting

When compilation fails, errors are reported with file location:

```
Error in Calculator.opal:12:5
  Undefined variable 'x' in expression

  §R (+ x 1)
       ^

Compilation failed with 1 error
```

For machine-readable error output, use [`opalc diagnose`](/opal/cli/diagnose/).

---

## Integration with MSBuild

For automatic compilation during `dotnet build`, use [`opalc init`](/opal/cli/init/) to set up MSBuild integration. This eliminates the need to run `opalc` manually.

After initialization:

```bash
# OPAL files compile automatically
dotnet build
```

---

## Batch Compilation

To compile multiple files, use shell scripting:

```bash
# Compile all .opal files in a directory
for f in src/*.opal; do
  opalc -i "$f" -o "${f%.opal}.g.cs"
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
# Compile OPAL to C#
opalc -i Program.opal -o Program.g.cs

# Build and run with .NET
dotnet run
```

### Compile to Specific Directory

```bash
# Output to build directory
opalc -i src/MyModule.opal -o build/generated/MyModule.g.cs
```

### Watch Mode (using external tools)

```bash
# Using fswatch (macOS)
fswatch -o src/*.opal | xargs -n1 -I{} opalc -i src/MyModule.opal -o src/MyModule.g.cs

# Using inotifywait (Linux)
while inotifywait -e modify src/*.opal; do
  opalc -i src/MyModule.opal -o src/MyModule.g.cs
done
```

---

## See Also

- [opalc init](/opal/cli/init/) - Set up automatic compilation with MSBuild
- [opalc diagnose](/opal/cli/diagnose/) - Machine-readable diagnostics
- [opalc format](/opal/cli/format/) - Format OPAL source files
- [Getting Started](/opal/getting-started/) - Installation and first program
