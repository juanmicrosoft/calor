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
| `.codex/skills/calor/SKILL.md` | Teaches Codex Calor syntax for writing new code |
| `.codex/skills/calor-convert/SKILL.md` | Teaches Codex how to convert C# to Calor |
| `AGENTS.md` | Project documentation with Calor-first guidelines |

You can run this command again anytime to update the Calor documentation section in AGENTS.md without losing your custom content.

---

## Important: Guidance-Based Enforcement

Unlike Claude Code which uses hooks to enforce Calor-first development, **Codex CLI does not support hooks**. This means:

- Calor-first development is **guidance-based only**
- Codex *should* follow the instructions in AGENTS.md and create `.calr` files
- However, enforcement is not automatic - Codex may occasionally create `.cs` files
- Always review file extensions after code generation
- Use `calor assess` to find any unconverted `.cs` files

---

## Available Skills

### The `$calor` Skill

When working with Codex CLI in an Calor-initialized project, use the `$calor` command to activate Calor-aware code generation.

**Example prompts:**

```
$calor

Write a function that calculates compound interest with:
- Preconditions: principal > 0, rate >= 0, years > 0
- Postcondition: result >= principal
- Effects: pure (no side effects)
```

```
$calor

Create a UserService class with methods for:
- GetUserById (returns Option<User>)
- CreateUser (returns Result<User, ValidationError>)
- DeleteUser (effects: database write)
```

### The `$calor-convert` Skill

Use `$calor-convert` to convert existing C# code to Calor:

```
$calor-convert

Convert this C# class to Calor:

public class Calculator
{
    public int Add(int a, int b) => a + b;

    public int Divide(int a, int b)
    {
        if (b == 0) throw new ArgumentException("Cannot divide by zero");
        return a / b;
    }
}
```

Codex will:
1. Convert the class structure to Calor syntax
2. Add appropriate contracts (e.g., `§Q (!= b 0)` for the divide precondition)
3. Generate unique IDs for all structural elements
4. Declare effects based on detected side effects

---

## Skill Capabilities

The Calor skills teach Codex:

### Syntax Knowledge

- All Calor structure tags (`§M`, `§F`, `§C`, etc.)
- Lisp-style expressions: `(+ a b)`, `(== x 0)`, `(% i 15)`
- Arrow syntax conditionals: `§IF{id} condition → action`
- Type system: `i32`, `f64`, `str`, `bool`, `Option<T>`, `Result<T,E>`, arrays

### Best Practices

- Unique ID generation (`m001`, `f001`, `c001`, etc.)
- Contract placement (`§Q` preconditions, `§S` postconditions)
- Effect declarations (`§E{db:rw,net:rw,cw}`)
- Proper structure nesting and closing tags

### Code Patterns

- Error handling with `Result<T,E>`
- Null safety with `Option<T>`
- Iteration patterns (for, while, do-while)
- Class definitions with fields, properties, methods

---

## Codex vs Claude Code Comparison

| Feature | Claude Code | Codex CLI |
|:--------|:------------|:----------|
| Skills directory | `.claude/skills/` | `.codex/skills/<name>/` |
| Skill file format | `calor.md` | `SKILL.md` with YAML frontmatter |
| Project instructions | `CLAUDE.md` | `AGENTS.md` |
| Skill invocation | `/calor` | `$calor` |
| Calor-first enforcement | **Hooks (enforced)** | **Guidance only** |
| Blocks `.cs` creation | Yes | No |

---

## Example Prompts

### Generate a Function

**Prompt:**
```
$calor

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
  §/I{if1}
§/F{f001}
§/M{m001}
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
§/F{f001}
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
$calor

I need to implement [feature description].

The requirements are:
- [requirement 1]
- [requirement 2]

Please create the Calor code with appropriate contracts and effects.
```

### Converting Existing Code

```
$calor-convert

Convert src/Services/PaymentService.cs to Calor, adding:
- Contracts based on the validation logic
- Effect declarations for database and network calls
```

### Verifying Calor-First Compliance

Since Codex doesn't enforce Calor-first automatically, periodically check for unconverted files:

```bash
# Find C# files that might need conversion
calor assess ./src --top 10

# Check for any new .cs files that should be .calr
find . -name "*.cs" -not -name "*.g.cs" -not -path "./obj/*"
```

---

## Best Practices

1. **Review generated files** - Always check that Codex created `.calr` files, not `.cs`
2. **Use explicit instructions** - Be specific about wanting Calor output
3. **Include skill reference** - Start prompts with `$calor` or `$calor-convert`
4. **Run analysis regularly** - Use `calor assess` to find migration candidates
5. **Convert promptly** - If Codex creates a `.cs` file, convert it immediately

---

## Troubleshooting

### Codex Creates `.cs` Files Instead of `.calr`

This can happen since enforcement is guidance-based. Solutions:

1. Be more explicit: "Create this as an Calor file (`.calr`), not C#"
2. Start your prompt with `$calor` to activate the skill
3. Convert the file: `calor convert filename.cs`

### Skills Not Recognized

Ensure you've run `calor init --ai codex` and the skill files exist:

```bash
ls -la .codex/skills/calor/SKILL.md
ls -la .codex/skills/calor-convert/SKILL.md
```

---

## Next Steps

- [Syntax Reference](/calor/syntax-reference/) - Complete language reference
- [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) - Migration guide
- [calor init](/calor/cli/init/) - Full init command documentation
- [Claude Integration](/calor/getting-started/claude-integration/) - Alternative with enforced Calor-first
