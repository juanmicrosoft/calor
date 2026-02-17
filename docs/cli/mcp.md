---
layout: default
title: mcp
parent: CLI Reference
nav_order: 11
permalink: /cli/mcp/
---

# calor mcp

Start the Calor MCP (Model Context Protocol) server for AI coding agents.

```bash
calor mcp [options]
```

---

## Overview

The `mcp` command starts an MCP server that exposes Calor compiler capabilities as tools for AI coding agents like Claude. This enables agents to:

- **Compile** Calor code to C# directly
- **Verify** contracts using Z3 SMT solver
- **Analyze** code for security vulnerabilities and bugs
- **Convert** C# code to Calor
- **Get syntax help** for Calor features

The server communicates over stdio using the [Model Context Protocol](https://modelcontextprotocol.io/) specification.

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--stdio` | | `true` | Use standard input/output for communication |
| `--verbose` | `-v` | `false` | Enable verbose output to stderr for debugging |

---

## Available Tools

The MCP server exposes five tools:

### calor_compile

Compile Calor source code to C#.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code",
  "filePath": "string - File path for diagnostics",
  "options": {
    "verify": "boolean - Run Z3 contract verification",
    "analyze": "boolean - Run security/bug analysis",
    "contractMode": "string - off|debug|release"
  }
}
```

**Output:** Generated C# code and diagnostics array.

### calor_verify

Verify Calor contracts using Z3 SMT solver.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code",
  "timeout": "integer - Z3 timeout in milliseconds (default: 5000)"
}
```

**Output:** Verification summary with per-function results and counterexamples for failed contracts.

### calor_analyze

Analyze Calor code for security vulnerabilities and bug patterns.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code",
  "options": {
    "enableDataflow": "boolean - Enable dataflow analysis (default: true)",
    "enableBugPatterns": "boolean - Enable bug pattern detection (default: true)",
    "enableTaintAnalysis": "boolean - Enable taint analysis (default: true)"
  }
}
```

**Output:** Security vulnerabilities, bug patterns, and dataflow issues.

### calor_convert

Convert C# source code to Calor.

**Input Schema:**
```json
{
  "source": "string (required) - C# source code to convert",
  "moduleName": "string - Module name for output"
}
```

**Output:** Generated Calor code and conversion statistics.

### calor_syntax_help

Get syntax documentation for a specific Calor feature.

**Input Schema:**
```json
{
  "feature": "string (required) - Feature name (e.g., 'async', 'contracts', 'effects', 'loops', 'collections')"
}
```

**Output:** Relevant syntax documentation and examples.

---

## Configuration

### Claude Desktop

Add to `~/Library/Application Support/Claude/claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "calor": {
      "command": "calor",
      "args": ["mcp", "--stdio"]
    }
  }
}
```

### Claude Code (via calor init)

When you run `calor init --ai claude`, the MCP server is automatically configured in `.claude/settings.json`:

```json
{
  "mcpServers": {
    "calor-lsp": {
      "command": "calor",
      "args": ["lsp"]
    },
    "calor": {
      "command": "calor",
      "args": ["mcp", "--stdio"]
    }
  }
}
```

This configures two servers:
- **calor-lsp**: Language server for IDE features (diagnostics, hover, go-to-definition)
- **calor**: MCP server with tools for compile, verify, analyze, convert, and syntax help

### VS Code

Add to `.vscode/mcp.json`:

```json
{
  "servers": {
    "calor": {
      "command": "calor",
      "args": ["mcp", "--stdio"]
    }
  }
}
```

---

## Manual Testing

Test the server with a direct request:

```bash
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}' | \
  { msg=$(cat); len=${#msg}; printf "Content-Length: %d\r\n\r\n%s" "$len" "$msg"; } | \
  calor mcp --stdio
```

---

## Protocol Details

The server implements [MCP 2024-11-05](https://spec.modelcontextprotocol.io/) with:

- **Transport**: stdio with Content-Length headers
- **Methods**: `initialize`, `initialized`, `tools/list`, `tools/call`, `ping`
- **Capabilities**: `tools` (listChanged: false)

---

## Environment Variables

| Variable | Description |
|:---------|:------------|
| `CALOR_SKILL_FILE` | Override the skill file path for `calor_syntax_help` |

---

## See Also

- [calor init](/calor/cli/init/) - Initialize project with MCP server configuration
- [Claude Integration](/calor/getting-started/claude-integration/) - Using Calor with Claude Code
- [Model Context Protocol](https://modelcontextprotocol.io/) - MCP specification
