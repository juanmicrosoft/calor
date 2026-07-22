---
layout: default
title: Codex Integration
parent: Getting Started
nav_order: 4
---

# Using Calor with OpenAI Codex CLI

This guide explains how to use Calor with OpenAI Codex CLI. For Claude Code integration, see [Claude Integration](/calor/getting-started/claude-integration/).

---

## Quick Setup

Initialize your project for Codex CLI with a single command:

```bash
calor init --ai codex
```

This creates:

| File | Purpose |
|:-----|:--------|
| `.codex/config.toml` | MCP server configuration for Calor tools |
| `.codex/hooks.json` | Codex-native write validation and post-write Calor lint hooks |
| `AGENTS.md` | Project documentation with Calor-first guidelines |

You can run this command again anytime to update the Calor documentation section in AGENTS.md without losing your custom content.

---

## MCP Server Integration

The init command also configures an **MCP (Model Context Protocol) server** that gives Codex direct access to the Calor compiler. This enables Codex to:

- **Type check** code and get semantic errors
- **Verify contracts** using the Z3 SMT solver
- **Analyze** code for bugs and migration potential
- **Convert** between C# and Calor

### How It Works

When you open the project in Codex CLI, the MCP server starts automatically based on the `.codex/config.toml` configuration:

```toml
# BEGIN CalorC MCP SECTION - DO NOT EDIT
[mcp_servers.calor]
command = "calor"
args = ["mcp", "--stdio"]
# END CalorC MCP SECTION
```

Codex discovers `.codex/hooks.json` in trusted projects. On first use, run `/hooks`, review the generated commands, and trust them. The `PreToolUse` hook validates C# destinations submitted through covered file-edit tools, while the `PostToolUse` hook lints changed `.calr` files. The hook consumes Codex's JSON stdin envelope directly; it does not use Claude Code's `$TOOL_INPUT` environment-variable schema. Codex releases may route specialized file-change tools outside the lifecycle-hook path; run the smoke test in `bench/phase0-agent-native/CODEX-SMOKE.md` and keep CI verification enabled.

### Available Tools

| Tool | Purpose |
|:-----|:--------|
| `calor_typecheck` | Semantic type checking with error categorization |
| `calor_verify_contracts` | Z3-based contract verification |
| `calor_compile` | Compile Calor source to C# |
| `calor_analyze` | Advanced bug detection |
| `calor_convert` | Convert between C# and Calor |
| `calor_format` | Format source to canonical style |
| `calor_lint` | Check agent-optimized format issues |

See [`calor mcp`](/calor/cli/mcp/) for the complete list of 19 available tools.

---

## Enforcement

Codex supports project lifecycle hooks. Calor configures a `PreToolUse` hook for
`apply_patch`/`Edit`/`Write` and a `PostToolUse` hook that lints changed `.calr`
files. Project hooks run only after the repository and exact hook definitions
are trusted; use `/hooks` to review their status.
The write hook is a guardrail, not a complete security boundary. It validates
rename destinations and every destination in multi-file patches. Shell commands
and specialized tool paths can fall outside tool-hook coverage, so CI should
still run `calor assess`, builds, and tests. A smoke run must confirm that the
Codex tool path in use actually invokes both lifecycle hooks.

---

## Project Guidance
`calor init --ai codex` writes Calor authoring and validation rules into the
managed section of `AGENTS.md`. Codex loads this guidance automatically; the
initializer does not generate project-local Codex skill files.

The generated guidance covers:

- All Calor structure tags (`§M`, `§F`, `§C`, etc.)
- Lisp-style expressions: `(+ a b)`, `(== x 0)`, `(% i 15)`
- Arrow syntax conditionals: `§IF{id} condition → action`
- Type system: `i32`, `f64`, `str`, `bool`, `Option<T>`, `Result<T,E>`, arrays
- Unique ID generation (`m001`, `f001`, `c001`, etc.)
- Contract placement (`§Q` preconditions, `§S` postconditions)
- Effect declarations (`§E{db:rw,net:rw,cw}`)
- Proper indentation and block nesting
- Error handling with `Result<T,E>`
- Null safety with `Option<T>`
- Iteration patterns (for, while, do-while)
- Class definitions with fields, properties, methods

---

## Codex vs Claude Code Comparison

| Feature | Claude Code | Codex CLI |
|:--------|:------------|:----------|
| Project instructions | `CLAUDE.md` | `AGENTS.md` |
| MCP Tools | Yes | Yes |
| Calor-first enforcement | Hooks | Hooks for covered tools + `AGENTS.md` |
| Blocks `.cs` file edits | Yes | Yes, for covered file-edit tools |

---

## Example Prompts

### Generate a Function

**Prompt:**
```
Write an Calor function that calculates factorial with a precondition
that n >= 0 and postcondition that result >= 1
```

**Expected Response:**
```
§M{m001:Math}
  §F{f001:Factorial:pub}
    §I{i32:n}
    §O{i32}
    §Q (>= n 0)
    §S (>= result 1)
    §IF{if1} (<= n 1) → §R 1
    §EL → §R (* n §C{Factorial} §A (- n 1) §/C)
```

### Find Bugs

**Prompt:**
```
Is there a bug in this Calor code?

§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
```

**Expected Response:**
```
Yes - missing precondition. Add:

§Q (!= b 0)

Otherwise division by zero is possible.
```

---

## Workflow Tips

### Starting a New Feature

```
I need to implement [feature description].

The requirements are:
- [requirement 1]
- [requirement 2]

Please create the Calor code with appropriate contracts and effects.
```

### Converting Existing Code

```
Convert src/Services/PaymentService.cs to Calor, adding:
- Contracts based on the validation logic
- Effect declarations for database and network calls
```

### Verifying Calor-First Compliance

Hooks catch covered Codex file edits, while these checks provide repository-wide verification:

```bash
# Repository/CI backstop for C# added beside a .calr source
bash scripts/check-calor-first-diff.sh --working-tree

# Optional local wrapper: audit after each Codex session
bash scripts/codex-with-calor-check.sh codex exec --json
```

The guard fails for every new `.cs` path, including root-level and newly-created
directories. Existing tracked C# is grandfathered for compiler/runtime
maintenance but is protected by CODEOWNERS review. Generated output must use a
path already present in the protected base-branch `.calor-csharp-allowlist`; an
allowlist added by the same PR is not accepted, and filename suffixes such as
`.g.cs` are not trusted. CI also runs behavioral tests for these cases,
including deleted-source replacements. This remains necessary because Codex
may route specialized file changes outside lifecycle-hook coverage.

```bash
# Find C# files that might need conversion
calor assess ./src --top 10

# Check for any new .cs files that should be .calr
find . -name "*.cs" -not -name "*.g.cs" -not -path "./obj/*"
```

---

## Best Practices

1. **Use MCP tools** - Leverage MCP tools for compilation and verification
2. **Trust and inspect hooks** - Use `/hooks` after initialization or hook changes
3. **Use explicit instructions** - Be specific about wanting Calor output
4. **Keep guidance current** - Re-run init to update the managed `AGENTS.md` section
5. **Run analysis regularly** - Use `calor assess` to find migration candidates
6. **Convert promptly** - If Codex creates a `.cs` file, convert it immediately

---

## Troubleshooting

### Codex Creates `.cs` Files Instead of `.calr`

This can happen through a shell command, an uncovered tool path, or disabled/untrusted hooks. Solutions:

1. Be more explicit: "Create this as an Calor file (`.calr`), not C#"
2. Run `/hooks` and verify both generated Calor hooks are enabled and trusted.
3. Convert the file: `calor convert filename.cs`.

### Hooks Not Running

Ensure initialization created the hook file, then review it in Codex:

```bash
cat .codex/hooks.json
# In Codex CLI: /hooks
```

---

## Next Steps

- [Syntax Reference](/calor/syntax-reference/) - Complete language reference
- [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) - Migration guide
- [calor init](/calor/cli/init/) - Full init command documentation
- [calor mcp](/calor/cli/mcp/) - MCP server tool documentation
- [Claude Integration](/calor/getting-started/claude-integration/) - Alternative with enforced Calor-first
