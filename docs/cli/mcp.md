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
- **Lint** code for agent-optimal format compliance
- **Format** code to canonical style
- **Diagnose** code with machine-readable diagnostics
- **Manage IDs** (check and assign declaration IDs)
- **Assess** C# code for Calor migration potential

The server communicates over stdio using the [Model Context Protocol](https://modelcontextprotocol.io/) specification.

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--stdio` | | `true` | Use standard input/output for communication |
| `--verbose` | `-v` | `false` | Enable verbose output to stderr for debugging |

---

## Available Tools

The MCP server exposes ten tools:

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

### calor_lint

Check Calor source code for agent-optimal format compliance.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code to lint",
  "fix": "boolean - Return auto-fixed code in the response (default: false)"
}
```

**Output:** Parse success status, lint issues with line numbers and messages, and optionally the fixed code.

### calor_format

Format Calor source code to canonical style.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code to format"
}
```

**Output:** Formatted code and whether it changed from the original.

### calor_diagnose

Get machine-readable diagnostics from Calor source code. Includes suggestions and fix information for errors when available.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code to diagnose",
  "options": {
    "strictApi": "boolean - Enable strict API checking (default: false)",
    "requireDocs": "boolean - Require documentation on public functions (default: false)"
  }
}
```

**Output:**
```json
{
  "success": true,
  "errorCount": 1,
  "warningCount": 0,
  "diagnostics": [
    {
      "severity": "error",
      "code": "Calor0106",
      "message": "Unknown operator 'cotains'. Did you mean 'contains'?",
      "line": 1,
      "column": 40,
      "suggestion": "Replace 'cotains' with 'contains'",
      "fix": {
        "description": "Replace 'cotains' with 'contains'",
        "edits": [
          {
            "startLine": 1,
            "startColumn": 40,
            "endLine": 1,
            "endColumn": 47,
            "newText": "contains"
          }
        ]
      }
    }
  ]
}
```

**Diagnostic Fields:**
- `severity` - "error" or "warning"
- `code` - Diagnostic code (e.g., "Calor0106" for invalid operator)
- `message` - Human-readable error message
- `line` - 1-based line number
- `column` - 1-based column number
- `suggestion` - (optional) Brief description of the suggested fix
- `fix` - (optional) Machine-applicable fix with edits

**Fix-Supported Diagnostics:**
- Typos in operators (e.g., "cotains" → "contains")
- Mismatched closing tag IDs
- Undefined variables with similar names in scope
- C# constructs with Calor alternatives (e.g., "nameof" → use string literal)

### calor_ids

Manage Calor declaration IDs. Check for missing, duplicate, or invalid IDs and optionally assign new ones.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code to check/process",
  "action": "string - 'check' validates IDs, 'assign' adds missing IDs (default: 'check')",
  "options": {
    "allowTestIds": "boolean - Allow test IDs (f001, m001) without flagging as issues (default: false)",
    "fixDuplicates": "boolean - When assigning, also fix duplicate IDs (default: false)"
  }
}
```

**Output:** For 'check': ID issues with type, line, kind, name, and message. For 'assign': Modified code and list of assignments.

### calor_assess

Assess C# source code for Calor migration potential. Returns scores across 8 dimensions plus detection of unsupported C# constructs.

**Input Schema:**
```json
{
  "source": "string - C# source code to assess (single file mode)",
  "files": "array - Multiple C# files to assess (multi-file mode), each with 'path' and 'source'",
  "options": {
    "threshold": "integer - Minimum score (0-100) to include in results (default: 0)"
  }
}
```

**Scoring Dimensions:**
- **ContractPotential** (18%): Argument validation, assertions -> contracts
- **EffectPotential** (13%): I/O, network, database calls -> effect declarations
- **NullSafetyPotential** (18%): Nullable types, null checks -> Option&lt;T&gt;
- **ErrorHandlingPotential** (18%): Try/catch, throw -> Result&lt;T,E&gt;
- **PatternMatchPotential** (8%): Switch statements -> exhaustiveness checking
- **ApiComplexityPotential** (13%): Undocumented public APIs
- **AsyncPotential** (6%): async/await, Task&lt;T&gt; returns
- **LinqPotential** (6%): LINQ method usage

**Output:** Summary with average score and priority breakdown, plus per-file details including scores, dimensions, and unsupported constructs.

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
- **calor**: MCP server with tools for compile, verify, analyze, convert, syntax help, lint, format, diagnose, and IDs

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
