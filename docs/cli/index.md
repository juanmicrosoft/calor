---
layout: default
title: CLI Reference
nav_order: 6
has_children: true
permalink: /cli/
---

# CLI Reference

The `opalc` command-line tool provides commands for working with OPAL code and analyzing C# codebases for migration.

---

## Installation

Install `opalc` as a global .NET tool:

```bash
dotnet tool install --global Opal.Compiler
```

Or run the quick-start script which installs everything:

```bash
curl -fsSL https://raw.githubusercontent.com/juanmicrosoft/opal/main/scripts/init-opal.sh | bash
```

---

## Available Commands

| Command | Description |
|:--------|:------------|
| `opalc` (default) | Compile OPAL source files to C# |
| [`opalc analyze`](/opal/cli/analyze/) | Score C# files for OPAL migration potential |
| [`opalc init`](/opal/cli/init/) | Initialize OPAL with AI agent support and .csproj integration |
| [`opalc convert`](/opal/cli/convert/) | Convert single files between C# and OPAL |
| [`opalc migrate`](/opal/cli/migrate/) | Migrate entire projects between C# and OPAL |
| [`opalc benchmark`](/opal/cli/benchmark/) | Compare OPAL vs C# across evaluation metrics |
| [`opalc format`](/opal/cli/format/) | Format OPAL source files to canonical style |
| [`opalc diagnose`](/opal/cli/diagnose/) | Output machine-readable diagnostics for tooling |

---

## Compilation (Default Command)

Compile OPAL source files to C#:

```bash
opalc --input file.opal --output file.g.cs
```

### Options

| Option | Description |
|:-------|:------------|
| `--input`, `-i` | Input OPAL file path (required) |
| `--output`, `-o` | Output C# file path (required) |
| `--verbose`, `-v` | Show detailed compilation output |

### Example

```bash
# Compile a single file
opalc --input src/MyModule.opal --output src/MyModule.g.cs

# Compile with verbose output
opalc -v -i src/MyModule.opal -o src/MyModule.g.cs
```

---

## See Also

- [Getting Started](/opal/getting-started/) - Installation and first program
- [Syntax Reference](/opal/syntax-reference/) - Complete language reference
