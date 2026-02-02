---
layout: default
title: Installation
parent: Getting Started
nav_order: 1
---

# Installation

This guide covers installing the OPAL compiler and setting up your development environment.

---

## Global Tool Install

Install the OPAL compiler as a global .NET tool:

```bash
dotnet tool install -g opalc
```

After installation, you can compile OPAL files from anywhere:

```bash
opalc --input program.opal --output program.g.cs
```

To update to the latest version:

```bash
dotnet tool update -g opalc
```

---

## AI Agent Integration

OPAL provides first-class support for AI coding agents. The `opalc init` command sets up your project for AI-assisted development.

### Supported AI Agents

| Agent | Flag | What Gets Created |
|:------|:-----|:------------------|
| Claude Code | `--ai claude` | Skills in `.claude/skills/`, `CLAUDE.md` project docs |
| OpenAI Codex | `--ai codex` | Codex-optimized configuration |
| Google Gemini | `--ai gemini` | Gemini-optimized configuration |
| GitHub Copilot | `--ai github` | Copilot-optimized configuration |

### Claude Code Integration

Initialize your project for Claude Code:

```bash
opalc init --ai claude
```

This creates:

| File | Purpose |
|:-----|:--------|
| `.claude/skills/opal/SKILL.md` | OPAL code writing skill with YAML frontmatter |
| `.claude/skills/opal-convert/SKILL.md` | C# to OPAL conversion skill |
| `CLAUDE.md` | Project documentation (creates new or updates OPAL section) |

### Claude Code Commands

After initialization, use these commands in Claude Code:

| Command | Description |
|:--------|:------------|
| `/opal` | Write new OPAL code with Claude's assistance |
| `/opal-convert` | Convert existing C# code to OPAL syntax |

### Re-running Init

You can run `opalc init --ai claude` multiple times safely:

- **CLAUDE.md** - Updates only the OPAL section, preserving your custom content
- **Skills files** - Updates to latest version (prompts to confirm or use `--force`)
- **.csproj targets** - Skips if already present (idempotent)

### MSBuild Integration

The `init` command also adds MSBuild targets to your `.csproj` file for automatic OPAL compilation:

```bash
# Specify project explicitly
opalc init --ai claude --project MyApp.csproj

# Auto-detect single .csproj
opalc init --ai claude
```

After initialization, OPAL files compile automatically during `dotnet build`:

```
dotnet build
# → Compiles all .opal files to obj/<config>/<tfm>/opal/*.g.cs
# → Includes generated C# in compilation
```

See [`opalc init`](/opal/cli/init/) for complete documentation.

---

## Prerequisites

Before installing OPAL, ensure you have:

| Requirement | Version | Check Command |
|:------------|:--------|:--------------|
| .NET SDK | 8.0+ | `dotnet --version` |
| Git | Any recent | `git --version` |

### Installing .NET SDK

**macOS (Homebrew):**
```bash
brew install dotnet-sdk
```

**Windows:**
Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0)

**Linux (Ubuntu/Debian):**
```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

---

## Clone and Build (For Contributors)

If you want to contribute to OPAL or build from source:

```bash
# Clone the repository
git clone https://github.com/juanmicrosoft/opal.git
cd opal

# Build the entire solution
dotnet build

# Verify the build
dotnet run --project src/Opal.Compiler -- --help
```

Expected output:
```
OPAL Compiler - Optimized Programming for Agent Language

Usage:
  opalc --input <file.opal> --output <file.cs>

Options:
  --input, -i    Input OPAL source file
  --output, -o   Output C# file
  --help, -h     Show this help message
```

---

## Project Structure

```
opal/
├── src/
│   └── Opal.Compiler/      # The OPAL compiler
├── samples/
│   └── HelloWorld/         # Sample OPAL program
├── tests/
│   ├── E2E/                # End-to-end tests
│   └── Opal.Evaluation/    # Evaluation framework
└── docs/                   # This documentation
```

---

## Compiling OPAL Files

Basic usage:

```bash
dotnet run --project src/Opal.Compiler -- \
  --input path/to/your/program.opal \
  --output path/to/output/program.g.cs
```

The `.g.cs` extension is a convention indicating "generated C#".

---

## Running Generated Code

After compilation, you need a C# project to run the generated code:

### Option 1: Use the HelloWorld Sample

```bash
# Compile your OPAL file
dotnet run --project src/Opal.Compiler -- \
  --input your-program.opal \
  --output samples/HelloWorld/your-program.g.cs

# Run it (requires modifying HelloWorld.csproj or including the file)
dotnet run --project samples/HelloWorld
```

### Option 2: Create a New Project

```bash
# Create a new console project
dotnet new console -o MyOpalProgram
cd MyOpalProgram

# Compile OPAL to the project directory
dotnet run --project ../src/Opal.Compiler -- \
  --input ../my-code.opal \
  --output my-code.g.cs

# Run the program
dotnet run
```

---

## Running Tests

### E2E Tests

```bash
# macOS/Linux
./tests/E2E/run-tests.sh

# Windows
.\tests\E2E\run-tests.ps1
```

### Evaluation Framework

```bash
# Run the evaluation
dotnet run --project tests/Opal.Evaluation -- --output report.json

# Generate markdown report
dotnet run --project tests/Opal.Evaluation -- --output report.md --format markdown
```

---

## Troubleshooting

### Build Errors

**"SDK not found":**
```bash
# Verify .NET is installed
dotnet --info

# If missing, install .NET 8.0 SDK
```

**"Project not found":**
```bash
# Ensure you're in the opal directory
pwd  # Should show .../opal

# List available projects
ls src/
```

### Runtime Errors

**"Main method not found":**
- Ensure your OPAL code has a public `Main` function:
  ```
  §F[f001:Main:pub]
    §O[void]
    // ...
  §/F[f001]
  ```

---

## Next Steps

- [Hello World](/opal/getting-started/hello-world/) - Understand OPAL syntax
- [Syntax Reference](/opal/syntax-reference/) - Complete language reference
