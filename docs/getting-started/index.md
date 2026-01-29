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

## Quick Start

Get up and running with OPAL in one command:

**macOS/Linux:**
```bash
curl -fsSL https://raw.githubusercontent.com/juanmicrosoft/opal/main/scripts/init-opal.sh | bash
```

**Windows (PowerShell):**
```powershell
irm https://raw.githubusercontent.com/juanmicrosoft/opal/main/scripts/init-opal.ps1 | iex
```

---

## What You Get

The init script sets up everything you need:

| Component | Description |
|:----------|:------------|
| `opalc` global tool | Compile OPAL to C# from anywhere: `opalc --input file.opal --output file.g.cs` |
| Claude Code skills | Use `/opal` to write OPAL code and `/opal-convert` to convert C# to OPAL |
| Sample project | A ready-to-run OPAL project to explore |

---

## Manual Installation

For alternative installation methods (global tool only, manual Claude skills setup, or building from source), see the [Installation](/opal/getting-started/installation/) page.

---

## Next Steps

- [Installation](/opal/getting-started/installation/) - Detailed setup instructions
- [Hello World](/opal/getting-started/hello-world/) - Understand the hello world program
- [Syntax Reference](/opal/syntax-reference/) - Complete language reference
