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

## Two Paths to OPAL

Choose your starting point based on your situation:

### New Project

Get up and running with OPAL in one command:

**macOS/Linux:**
```bash
curl -fsSL https://raw.githubusercontent.com/juanmicrosoft/opal/main/scripts/init-opal.sh | bash
```

**Windows (PowerShell):**
```powershell
irm https://raw.githubusercontent.com/juanmicrosoft/opal/main/scripts/init-opal.ps1 | iex
```

### Existing C# Project

Add OPAL to your existing codebase in two steps:

```bash
# 1. Enable OPAL in your project (adds MSBuild integration)
opalc init

# 2. Analyze your codebase to find migration candidates
opalc analyze ./src --top 10
```

After init, you can:
- Write new `.opal` files that compile automatically during `dotnet build`
- Use `opalc convert` to migrate high-scoring C# files to OPAL

**Optional: Enable Claude Code integration**

```bash
# Add Claude skills for AI-assisted OPAL development
opalc init --ai claude
```

This adds `/opal` and `/opal-convert` skills, plus CLAUDE.md with guidelines that instruct Claude to write new code in OPAL and analyze C# files before modifying them.

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
