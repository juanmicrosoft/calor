---
layout: default
title: CLI Reference
nav_order: 6
has_children: true
permalink: /cli/
---

# CLI Reference

The `calor` command-line tool provides commands for working with Calor code and analyzing C# codebases for migration.

---

## Installation

Install `calor` as a global .NET tool:

```bash
dotnet tool install -g calor
```

Or update an existing installation:

```bash
dotnet tool update -g calor
```

---

## Available Commands

| Command | Description |
|:--------|:------------|
| `calor` (default) | Compile Calor source files to C# |
| [`calor assess`](/calor/cli/assess/) | Score C# files for Calor migration potential |
| [`calor init`](/calor/cli/init/) | Initialize Calor with AI agent support and .csproj integration |
| [`calor convert`](/calor/cli/convert/) | Convert single files between C# and Calor |
| [`calor migrate`](/calor/cli/migrate/) | Migrate entire projects between C# and Calor |
| [`calor ids`](/calor/cli/ids/) | Manage unique identifiers (check, assign, index) |
| [`calor benchmark`](/calor/cli/benchmark/) | Compare Calor vs C# across evaluation metrics |
| [`calor format`](/calor/cli/format/) | Format Calor source files to canonical style |
| [`calor diagnose`](/calor/cli/diagnose/) | Output machine-readable diagnostics for tooling |
| [`calor mcp`](/calor/cli/mcp/) | Start MCP server for AI agent tool integration |
| `calor lsp` | Start Language Server Protocol server for IDE features |
| [`calor hook`](/calor/cli/hook/) | Claude Code hook commands (internal) |

---

## Compilation (Default Command)

Compile Calor source files to C#:

```bash
calor --input file.calr --output file.g.cs
```

### Options

| Option | Description |
|:-------|:------------|
| `--input`, `-i` | Input Calor file path (required) |
| `--output`, `-o` | Output C# file path (required) |
| `--verbose`, `-v` | Show detailed compilation output |

### Example

```bash
# Compile a single file
calor --input src/MyModule.calr --output src/MyModule.g.cs

# Compile with verbose output
calor -v -i src/MyModule.calr -o src/MyModule.g.cs
```

---

## See Also

- [Getting Started](/calor/getting-started/) - Installation and first program
- [Syntax Reference](/calor/syntax-reference/) - Complete language reference
