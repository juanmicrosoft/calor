---
layout: default
title: Getting Started
nav_order: 3
has_children: true
permalink: /getting-started/
---

# Getting Started with Calor

This guide will help you install Calor, write your first program, and understand how to integrate Calor with AI coding agents.

---

## Quick Overview

Calor is a programming language that compiles to C# via source-to-source transformation. The workflow is:

```
your_code.calr → Calor Compiler → your_code.g.cs → .NET Build → executable
```

---

## What You'll Learn

1. **[Installation](/calor/getting-started/installation/)** - Set up the Calor compiler
2. **[Hello World](/calor/getting-started/hello-world/)** - Write and run your first Calor program
3. **[Claude Integration](/calor/getting-started/claude-integration/)** - Use Calor with Claude Code (enforced Calor-first)
4. **[Codex Integration](/calor/getting-started/codex-integration/)** - Use Calor with OpenAI Codex CLI

---

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- A terminal or command prompt

---

## Installation

Install the Calor compiler as a global .NET tool:

```bash
dotnet tool install -g calor
```

Or update an existing installation:

```bash
dotnet tool update -g calor
```

---

## Quick Start

Calor integrates into any .NET project. Start with a C# project (new or existing), enable Calor, and from that point forward all new code is written in Calor.

### Step 1: Start with a C# Project

```bash
# Create a new project, or use an existing one
dotnet new console -o MyApp
cd MyApp
```

### Step 2: Enable Calor

```bash
calor init
```

This adds MSBuild integration so `.calr` files compile automatically during `dotnet build`.

### Step 3: (Optional) Enable AI Agent Integration

For Claude Code (with enforced Calor-first via hooks):
```bash
calor init --ai claude
```

For OpenAI Codex CLI (guidance-based):
```bash
calor init --ai codex
```

This adds Calor skills and project documentation that instruct the AI to:
- Write all new code in Calor (not C#)
- Analyze existing C# files before modifying them to determine if they should be converted to Calor first

**Note:** Claude Code enforces Calor-first via hooks (blocks `.cs` creation). Codex CLI is guidance-based only.

### Step 4: Write Calor Code

After init, create `.calr` files and they compile automatically. Create `Program.calr`:

```
§M{m001:MyApp}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §P "Hello from Calor!"
§/F{f001}
§/M{m001}
```

Then build:

```bash
dotnet build
```

See [Hello World](/calor/getting-started/hello-world/) for a detailed explanation of the syntax.

### Step 5: (Optional) Migrate Existing C# Files

For existing C# codebases, analyze which files are good candidates for migration:

```bash
calor assess ./src --top 10
```

Then convert high-scoring files:

```bash
calor convert HighScoreFile.cs
```

See the [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) guide for the complete walkthrough.

---

## What You Get

### Basic Init (`calor init`)

| Component | Description |
|:----------|:------------|
| MSBuild integration | `.calr` files compile automatically during `dotnet build` |
| Generated output | C# files go to `obj/` directory, keeping source tree clean |

### With Claude (`calor init --ai claude`)

| Component | Description |
|:----------|:------------|
| Claude Code skills | `/calor` to write Calor code, `/calor-convert` to convert C# |
| Hook configuration | **Enforces Calor-first** - blocks `.cs` file creation |
| CLAUDE.md | Project guidelines instructing Claude to prefer Calor for new code |

### With Codex (`calor init --ai codex`)

| Component | Description |
|:----------|:------------|
| Codex skills | `$calor` to write Calor code, `$calor-convert` to convert C# |
| AGENTS.md | Project guidelines (guidance-based, not enforced) |

**Note:** Codex CLI does not support hooks, so Calor-first is guidance-based only. Review file extensions after generation.

---

## Manual Installation

For alternative installation methods (global tool only, manual Claude skills setup, or building from source), see the [Installation](/calor/getting-started/installation/) page.

---

## Next Steps

- [Installation](/calor/getting-started/installation/) - Detailed setup instructions
- [Hello World](/calor/getting-started/hello-world/) - Understand the hello world program
- [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) - Complete migration guide
- [Syntax Reference](/calor/syntax-reference/) - Complete language reference
- [CLI Reference](/calor/cli/) - All `calor` commands including migration analysis
