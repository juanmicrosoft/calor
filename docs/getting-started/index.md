---
layout: default
title: Getting Started
nav_order: 3
has_children: true
permalink: /getting-started/
---

# Getting Started with OPAL

This guide will help you install OPAL, write your first program, and understand how to integrate OPAL with AI coding agents.

---

## Quick Overview

OPAL is a programming language that compiles to C# via source-to-source transformation. The workflow is:

```
your_code.opal → OPAL Compiler → your_code.g.cs → .NET Build → executable
```

---

## What You'll Learn

1. **[Installation](/opal/getting-started/installation/)** - Set up the OPAL compiler
2. **[Hello World](/opal/getting-started/hello-world/)** - Write and run your first OPAL program
3. **[Claude Integration](/opal/getting-started/claude-integration/)** - Use OPAL with AI coding agents

---

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- A terminal or command prompt

---

## Installation

Install the OPAL compiler as a global .NET tool:

```bash
dotnet tool install -g opalc --version 0.1.2
```

Or update an existing installation:

```bash
dotnet tool update -g opalc
```

---

## Quick Start

OPAL integrates into any .NET project. Start with a C# project (new or existing), enable OPAL, and from that point forward all new code is written in OPAL.

### Step 1: Start with a C# Project

```bash
# Create a new project, or use an existing one
dotnet new console -o MyApp
cd MyApp
```

### Step 2: Enable OPAL

```bash
opalc init
```

This adds MSBuild integration so `.opal` files compile automatically during `dotnet build`.

### Step 3: (Optional) Enable AI Agent Integration

```bash
opalc init --ai claude
```

This adds `/opal` and `/opal-convert` skills, plus CLAUDE.md with guidelines that instruct Claude to:
- Write all new code in OPAL (not C#)
- Analyze existing C# files before modifying them to determine if they should be converted to OPAL first

### Step 4: Write OPAL Code

After init, create `.opal` files and they compile automatically:

```bash
# Create your first OPAL file
echo 'namespace MyApp { §M Main() { Console.WriteLine("Hello from OPAL!") } }' > Program.opal

# Build runs OPAL compilation automatically
dotnet build
```

### Step 5: (Optional) Migrate Existing C# Files

For existing C# codebases, analyze which files are good candidates for migration:

```bash
opalc analyze ./src --top 10
```

Then convert high-scoring files:

```bash
opalc convert HighScoreFile.cs
```

See the [Adding OPAL to Existing Projects](/opal/guides/adding-opal-to-existing-projects/) guide for the complete walkthrough.

---

## What You Get

### Basic Init (`opalc init`)

| Component | Description |
|:----------|:------------|
| MSBuild integration | `.opal` files compile automatically during `dotnet build` |
| Generated output | C# files go to `obj/` directory, keeping source tree clean |

### With Claude (`opalc init --ai claude`)

| Component | Description |
|:----------|:------------|
| Claude Code skills | `/opal` to write OPAL code, `/opal-convert` to convert C# |
| CLAUDE.md | Project guidelines instructing Claude to prefer OPAL for new code |

---

## Manual Installation

For alternative installation methods (global tool only, manual Claude skills setup, or building from source), see the [Installation](/opal/getting-started/installation/) page.

---

## Next Steps

- [Installation](/opal/getting-started/installation/) - Detailed setup instructions
- [Hello World](/opal/getting-started/hello-world/) - Understand the hello world program
- [Adding OPAL to Existing Projects](/opal/guides/adding-opal-to-existing-projects/) - Complete migration guide
- [Syntax Reference](/opal/syntax-reference/) - Complete language reference
- [CLI Reference](/opal/cli/) - All `opalc` commands including migration analysis
