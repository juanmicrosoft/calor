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
- **Verify** contracts and refinement type obligations using Z3 SMT solver
- **Analyze** code for security vulnerabilities and bugs
- **Convert** C# code to Calor
- **Navigate** code with goto-definition, find-references, and symbol search
- **Get syntax help** for Calor features
- **Lint** and **format** code for agent-optimal compliance
- **Diagnose** code with machine-readable diagnostics
- **Preview edits** and analyze impact before committing changes
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

The MCP server exposes 32 tools organized by category:

### Compilation & Verification

| Tool | Description |
|:-----|:------------|
| `calor_compile` | Compile Calor source to C# |
| `calor_verify` | Verify contracts with Z3 SMT solver |
| `calor_verify_contracts` | Verify contracts (alias with improved discoverability) |
| `calor_typecheck` | Type check source code, returns categorized errors |
| `calor_diagnose` | Get machine-readable diagnostics with fix suggestions |
| `calor_validate_snippet` | Validate code fragments in isolation with context |
| `calor_compile_check_compat` | Verify generated C# is API-compatible with original |

### Code Navigation (LSP-style)

| Tool | Description |
|:-----|:------------|
| `calor_goto_definition` | Find definition of symbol at position |
| `calor_find_references` | Find all usages of a symbol |
| `calor_symbol_info` | Get type, contracts, and signature for a symbol |
| `calor_document_outline` | Get hierarchical structure of a file |
| `calor_find_symbol` | Search symbols by name across files |
| `calor_scope_info` | Get everything in scope at a given position |

### Analysis & Migration

| Tool | Description |
|:-----|:------------|
| `calor_analyze` | Advanced verification (dataflow, taint, bug patterns) |
| `calor_assess` | Score C# files for migration potential |
| `calor_analyze_convertibility` | Analyze C# code for Calor conversion likelihood |
| `calor_convert` | Convert between C# and Calor |
| `calor_impact_analysis` | Compute what would be affected by changing a symbol |
| `calor_call_graph` | Get callers/callees with effect annotations |
| `calor_edit_preview` | Preview effects of an edit before committing |

### Refinement Types & Obligations

| Tool | Description |
|:-----|:------------|
| `calor_obligations` | Generate and verify refinement type obligations |
| `calor_suggest_fixes` | Suggest ranked fix strategies for failed obligations |
| `calor_discover_guards` | Discover Z3-validated guards for failed obligations |
| `calor_suggest_types` | Analyze parameter usage and suggest refinement types |
| `calor_diagnose_refinement` | All-in-one repair tool with ranked patches |

### Code Quality

| Tool | Description |
|:-----|:------------|
| `calor_lint` | Check agent-optimized format issues |
| `calor_format` | Format source to canonical style |

### Syntax & Help

| Tool | Description |
|:-----|:------------|
| `calor_syntax_help` | Get syntax documentation for a feature |
| `calor_syntax_lookup` | Look up Calor syntax for a C# construct |
| `calor_explain_error` | Explain an error code or message with fix patterns |
| `calor_ids` | Check/assign declaration IDs |
| `calor_self_test` | Run compiler self-test against golden files |

---

## Tool Details

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

### calor_verify_contracts

Verify Calor contracts using Z3 SMT solver. Alias for `calor_verify` with improved discoverability.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code to verify",
  "timeout": "integer - Z3 solver timeout per contract in milliseconds (default: 5000)"
}
```

**Output:** Verification results with counterexamples for failed contracts.

### calor_typecheck

Type check Calor source code. Returns type errors with precise locations and categories.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code to type check",
  "filePath": "string - Optional file path for diagnostic messages"
}
```

**Output:**
```json
{
  "success": false,
  "errorCount": 1,
  "warningCount": 0,
  "typeErrors": [
    {
      "code": "Calor0200",
      "message": "Type mismatch: expected i32, got str",
      "line": 5,
      "column": 10,
      "severity": "error",
      "category": "type_mismatch"
    }
  ]
}
```

**Error Categories:** `type_mismatch`, `undefined_reference`, `duplicate_definition`, `invalid_reference`, `other`

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

**Fix-Supported Diagnostics:**
- Typos in operators (e.g., "cotains" → "contains")
- Mismatched closing tag IDs
- Undefined variables with similar names in scope
- C# constructs with Calor alternatives (e.g., "nameof" → use string literal)

### calor_validate_snippet

Validate Calor code fragments in isolation with optional context. Useful for incremental validation during code generation.

**Input Schema:**
```json
{
  "snippet": "string (required) - The Calor code fragment to validate",
  "context": {
    "location": "string - Where snippet appears: expression|statement|function_body|module_body (default: statement)",
    "returnType": "string - Expected return type for containing function",
    "parameters": "array - Variables in scope, each with 'name' and 'type'",
    "surroundingCode": "string - Code that precedes the snippet"
  },
  "options": {
    "lexerOnly": "boolean - Stop after token validation only (default: false)",
    "showTokens": "boolean - Include token stream in output (default: false)"
  }
}
```

**Output:** Validation results with diagnostics. When `lexerOnly` is true, only token-level errors are reported.

### calor_compile_check_compat

Verify generated C# is API-compatible with original. Checks namespace preservation, enum values, and attribute emission.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code to compile and check",
  "expectedNamespace": "string - Expected namespace in generated code",
  "expectedPatterns": "array of strings - Patterns that must appear in generated code",
  "forbiddenPatterns": "array of strings - Patterns that must NOT appear in generated code"
}
```

**Output:** Compatibility verdict with matched/unmatched patterns.

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

### calor_analyze_convertibility

Analyze how likely C# code is to successfully convert to Calor. Combines static analysis of unsupported constructs with an actual conversion attempt to produce a practical score (0–100) with blocker details.

**Input Schema:**
```json
{
  "source": "string (required) - C# source code to analyze",
  "options": {
    "quick": "boolean - Stage 1 only: static analysis without conversion attempt (default: false)"
  }
}
```

**Output:** Convertibility score, blocker list, and conversion attempt results (unless `quick` mode).

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

### calor_goto_definition

Find the definition of a symbol at a given position. Provide either `source` for inline code or `filePath` to read from file.

**Input Schema:**
```json
{
  "source": "string - Calor source code (use this OR filePath)",
  "filePath": "string - Path to a .calr file (use this OR source)",
  "line": "integer (required) - Line number (1-based)",
  "column": "integer (required) - Column number (1-based)"
}
```

**Output:** Definition location with file path, line, column, symbol name, kind, and source preview.

### calor_find_references

Find all references to a symbol. Supports lookup by position, symbol ID, or symbol name.

**Input Schema:**
```json
{
  "source": "string - Calor source code (use this OR filePath)",
  "filePath": "string - Path to a .calr file (use this OR source)",
  "symbolId": "string - Calor unique ID (e.g., 'f001')",
  "symbolName": "string - Name of the symbol to find",
  "line": "integer - Line number (1-based)",
  "column": "integer - Column number (1-based)",
  "includeDefinition": "boolean - Include definition location in results (default: true)",
  "groupByKind": "boolean - Group results by kind (default: false)"
}
```

**Output:** List of references with line, column, kind (definition, call_site, type_usage, reference), and context preview.

### calor_symbol_info

Get type information, contracts, and documentation for a symbol at a given position.

**Input Schema:**
```json
{
  "source": "string - Calor source code (use this OR filePath)",
  "filePath": "string - Path to a .calr file (use this OR source)",
  "line": "integer (required) - Line number (1-based)",
  "column": "integer (required) - Column number (1-based)"
}
```

**Output:** Structured information including type, kind, contracts (preconditions/postconditions), effects, and a formatted signature.

### calor_document_outline

Get a structured outline of all symbols in a Calor source file. Returns a hierarchical tree of modules, classes, functions, methods, fields, etc.

**Input Schema:**
```json
{
  "source": "string - Calor source code (use this OR filePath)",
  "filePath": "string - Path to a .calr file (use this OR source)",
  "includeDetails": "boolean - Include parameter types and contracts (default: true)"
}
```

**Output:** Hierarchical symbol tree with name, kind, line, detail, and children. Includes summary counts by symbol type.

### calor_find_symbol

Search for symbols by name in Calor source files. Can search in inline source, a single file, or a directory of .calr files.

**Input Schema:**
```json
{
  "query": "string (required) - Symbol name (case-insensitive partial match)",
  "source": "string - Calor source code to search in",
  "filePath": "string - Path to a .calr file to search in",
  "directory": "string - Directory to search for .calr files (recursive)",
  "kind": "string - Filter by kind: function|class|interface|enum|method|field|property",
  "limit": "integer - Maximum results (default: 50)"
}
```

**Output:** Matching symbols with name, kind, location, and file path.

### calor_scope_info

Get everything in scope at a given position. Returns enclosing function/class/module, local variables, parameters, available functions, active contracts, and valid insertion points.

**Input Schema:**
```json
{
  "source": "string - Calor source code (use this OR filePath)",
  "filePath": "string - Path to a .calr file (use this OR source)",
  "line": "integer (required) - Line number (1-based)",
  "column": "integer (required) - Column number (1-based)",
  "includeTypes": "boolean - Include type information for variables (default: true)",
  "includeContracts": "boolean - Include active contracts in scope (default: true)"
}
```

**Output:** Enclosing context, variables in scope with types, available functions, active contracts, and valid insertion points.

### calor_impact_analysis

Compute what would be affected by changing a symbol. Returns direct and transitive impacts through call chains, contract dependencies, and effect chain implications.

**Input Schema:**
```json
{
  "source": "string - Calor source code (use this OR filePath)",
  "filePath": "string - Path to a .calr file (use this OR source)",
  "symbolId": "string - Calor unique ID (e.g., 'f001')",
  "line": "integer - Line number (1-based)",
  "column": "integer - Column number (1-based)",
  "changeType": "string - Type of change: signature|type|rename|delete|contract (default: signature)",
  "depth": "integer - Maximum depth for transitive analysis (1-5, default: 3)"
}
```

**Output:** Direct and transitive impact lists with affected symbols, impact type, and severity.

### calor_call_graph

Get callers and/or callees of a function with effect annotations. Detects recursive cycles via strongly connected components.

**Input Schema:**
```json
{
  "source": "string - Calor source code (use this OR filePath)",
  "filePath": "string - Path to a .calr file (use this OR source)",
  "symbolId": "string - Calor unique ID (e.g., 'f001')",
  "line": "integer - Line number (1-based)",
  "column": "integer - Column number (1-based)",
  "direction": "string - Which direction: callers|callees|both (default: both)",
  "depth": "integer - Maximum traversal depth (1-5, default: 1)",
  "includeEffects": "boolean - Include effect annotations (default: true)"
}
```

**Output:** Call graph nodes with function ID, name, effects, and edges. Includes cycle detection for recursive functions.

### calor_edit_preview

Preview the effects of an edit before committing it. Given original and modified Calor source, reports compilation errors, contract violations, effect inconsistencies, and dangling references.

**Input Schema:**
```json
{
  "originalSource": "string (required) - Original Calor source code before the edit",
  "modifiedSource": "string (required) - Modified Calor source code after the edit",
  "checks": "array of strings - Which checks to run: compile|contracts|effects|references (default: all)"
}
```

**Output:** Verdict (`safe`, `safe_with_warnings`, or `breaking`) with details of any issues found.

### calor_obligations

Generate and verify refinement type obligations using Z3.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code",
  "timeout": "integer - Z3 solver timeout in milliseconds (default: 5000)",
  "function_id": "string - Filter to specific function"
}
```

**Output:** Summary with counts by status (discharged, failed, timeout, boundary), plus per-obligation details including kind, status, counterexample (for failed), and suggested fix.

### calor_suggest_fixes

Suggest ranked fix strategies for failed obligations.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code",
  "obligation_id": "string - Filter to specific obligation"
}
```

**Output:** Array of fixes, each with strategy (add_precondition, add_guard, refine_parameter, etc.), confidence level, and a Calor code template.

### calor_discover_guards

Discover guards that would discharge failed obligations, validated by Z3.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code",
  "obligation_id": "string - Specific obligation to target"
}
```

**Output:** Array of guards ranked by confidence, each with the Calor expression, insertion kind (precondition, if_guard, assert), and whether Z3 validated it.

### calor_suggest_types

Analyze parameter usage patterns and suggest refinement types.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code",
  "function_id": "string - Filter to specific function"
}
```

**Output:** Array of suggestions with parameter name, detected usage pattern (used_as_divisor, used_as_index, compared_geq_zero), suggested predicate, confidence, and ready-to-use Calor syntax.

### calor_diagnose_refinement

All-in-one repair tool — combines obligations, guards, and type suggestions into ranked patches.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code",
  "policy": "string - Obligation policy: 'default', 'strict', or 'permissive' (default: 'default')"
}
```

**Output:** Obligation summary, plus an array of patches. Each patch includes the obligation it addresses, the policy action, patch kind (precondition, if_guard, refine_parameter), Calor code to insert, confidence, and which obligation IDs it discharges.

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

### calor_syntax_help

Get syntax documentation for a specific Calor feature.

**Input Schema:**
```json
{
  "feature": "string (required) - Feature name (e.g., 'async', 'contracts', 'effects', 'loops', 'collections')"
}
```

**Output:** Relevant syntax documentation and examples.

### calor_syntax_lookup

Look up Calor syntax for a C# construct. Supports fuzzy matching.

**Input Schema:**
```json
{
  "query": "string (required) - C# construct to look up (e.g., 'object instantiation', 'for loop', 'async method', 'try catch')"
}
```

**Output:** Matching Calor syntax with examples.

### calor_explain_error

Explain a Calor error code or message. Returns the relevant common mistake pattern with the correct fix.

**Input Schema:**
```json
{
  "error": "string (required) - Error code (e.g., 'CALOR0042') or error message text"
}
```

**Output:** Common mistake pattern match with description, correct syntax example, and fix instructions.

### calor_ids

Manage Calor declaration IDs. Check for missing, duplicate, or invalid IDs and optionally assign new ones.

**Input Schema:**
```json
{
  "source": "string (required) - Calor source code to check/process",
  "action": "string - 'check' validates IDs, 'assign' adds missing IDs (default: 'check')",
  "options": {
    "allowTestIds": "boolean - Allow test IDs (f001, m001) without flagging (default: false)",
    "fixDuplicates": "boolean - When assigning, also fix duplicate IDs (default: false)"
  }
}
```

**Output:** For 'check': ID issues with type, line, kind, name, and message. For 'assign': Modified code and list of assignments.

### calor_self_test

Run compiler self-test: compiles embedded reference .calr files and diffs output against golden .cs files.

**Input Schema:**
```json
{
  "scenario": "string - Run only a specific scenario by name (e.g., '01_hello_world')"
}
```

**Output:** Test results with pass/fail per scenario and diff output for failures.

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
- **calor**: MCP server with all 32 tools

### Gemini CLI (via calor init)

When you run `calor init --ai gemini`, the MCP server is automatically configured in `.gemini/settings.json` alongside hooks:

```json
{
  "mcpServers": {
    "calor": {
      "command": "calor",
      "args": ["mcp", "--stdio"]
    }
  },
  "hooks": {
    "BeforeTool": [
      {
        "matcher": "write_file|replace",
        "hooks": [
          {
            "name": "calor-validate-write",
            "type": "command",
            "command": "calor hook validate-write --format gemini $TOOL_INPUT"
          }
        ]
      }
    ]
  }
}
```

Note: Unlike Claude Code, Gemini CLI's MCP config does not require a `type` field, and configuration is project-level (`.gemini/settings.json`) rather than user-level.

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
- [Gemini Integration](/calor/getting-started/gemini-integration/) - Using Calor with Google Gemini CLI
- [Model Context Protocol](https://modelcontextprotocol.io/) - MCP specification
