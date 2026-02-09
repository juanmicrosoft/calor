---
layout: default
title: Gemini Integration
parent: Getting Started
nav_order: 5
---

# Using Calor with Google Gemini CLI

This guide explains how to use Calor with Google Gemini CLI. For other AI integrations, see [Claude Integration](/calor/getting-started/claude-integration/) or [Codex Integration](/calor/getting-started/codex-integration/).

---

## Quick Setup

Initialize your project for Gemini CLI with a single command:

```bash
calor init --ai gemini
```

This creates:

| File | Purpose |
|:-----|:--------|
| `.gemini/skills/calor/SKILL.md` | Teaches Gemini Calor syntax for writing new code |
| `.gemini/skills/calor-convert/SKILL.md` | Teaches Gemini how to convert C# to Calor |
| `.gemini/settings.json` | **Hook configuration** - enforces Calor-first development |
| `GEMINI.md` | Project documentation with Calor-first guidelines |

You can run this command again anytime to update the Calor documentation section in GEMINI.md without losing your custom content.

---

## Calor-First Enforcement

Unlike Codex CLI, **Gemini CLI supports hooks** (as of v0.26.0+). This means Calor-first development is **enforced**, not just guided.

When Gemini tries to create a `.cs` file, the hook blocks the operation:

```json
{
  "decision": "deny",
  "reason": "BLOCKED: Cannot create C# file 'MyClass.cs'",
  "systemMessage": "This is an Calor-first project. Create an .calr file instead: MyClass.calr\n\nUse @calor skill for Calor syntax help."
}
```

Gemini will then automatically retry with an `.calr` file.

**Allowed file types:**
- `.calr` files (Calor source code)
- `.g.cs` files (generated C# from Calor)
- Files in `obj/` directory (build artifacts)

---

## Available Skills

### The `@calor` Skill

When working with Gemini CLI in an Calor-initialized project, use the `@calor` command to activate Calor-aware code generation.

**Example prompts:**

```
@calor

Write a function that calculates compound interest with:
- Preconditions: principal > 0, rate >= 0, years > 0
- Postcondition: result >= principal
- Effects: pure (no side effects)
```

```
@calor

Create a UserService class with methods for:
- GetUserById (returns Option<User>)
- CreateUser (returns Result<User, ValidationError>)
- DeleteUser (effects: database write)
```

### The `@calor-convert` Skill

Use `@calor-convert` to convert existing C# code to Calor:

```
@calor-convert

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

Gemini will:
1. Convert the class structure to Calor syntax
2. Add appropriate contracts (e.g., `§Q (!= b 0)` for the divide precondition)
3. Generate unique IDs for all structural elements
4. Declare effects based on detected side effects

---

## Skill Capabilities

The Calor skills teach Gemini:

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

## Gemini vs Claude Code vs Codex Comparison

| Feature | Claude Code | Gemini CLI | Codex CLI |
|:--------|:------------|:-----------|:----------|
| Skills directory | `.claude/skills/` | `.gemini/skills/<name>/` | `.codex/skills/<name>/` |
| Skill file format | `calor.md` | `SKILL.md` with YAML | `SKILL.md` with YAML |
| Project instructions | `CLAUDE.md` | `GEMINI.md` | `AGENTS.md` |
| Skill invocation | `/calor` | `@calor` | `$calor` |
| Calor-first enforcement | **Hooks (enforced)** | **Hooks (enforced)** | Guidance only |
| Blocks `.cs` creation | Yes | Yes | No |
| Hook mechanism | `PreToolUse` | `BeforeTool` | N/A |

---

## Example Prompts

### Generate a Function

**Prompt:**
```
@calor

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

### Create a Service Class

**Prompt:**
```
@calor

Create a PaymentService class with:
- A ProcessPayment method that takes amount (f64) and returns Result<PaymentResult, PaymentError>
- Precondition: amount > 0
- Effects: database write, network call
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
@calor

I need to implement [feature description].

The requirements are:
- [requirement 1]
- [requirement 2]

Please create the Calor code with appropriate contracts and effects.
```

### Converting Existing Code

```
@calor-convert

Convert src/Services/PaymentService.cs to Calor, adding:
- Contracts based on the validation logic
- Effect declarations for database and network calls
```

### Verifying the Hook is Working

Test that the hook blocks `.cs` creation:

```bash
# Ask Gemini to create a C# file
gemini "Create a new utility class called StringHelper in StringHelper.cs"

# The hook should block and suggest StringHelper.calr instead
```

---

## Best Practices

1. **Trust the enforcement** - Unlike Codex, Gemini CLI will actually block `.cs` creation
2. **Use explicit instructions** - Be specific about contracts and effects you want
3. **Include skill reference** - Start prompts with `@calor` or `@calor-convert`
4. **Review contracts** - Verify generated contracts match your requirements
5. **Check effects** - Ensure effect declarations are accurate

---

## Troubleshooting

### Hook Not Blocking Files

Verify the settings file exists and has the correct content:

```bash
cat .gemini/settings.json
```

Expected content includes:
```json
{
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

### "calor: command not found"

Ensure the Calor compiler is installed and in your PATH:

```bash
dotnet tool install -g calor
export PATH="$PATH:$HOME/.dotnet/tools"
```

### Skills Not Recognized

Ensure you've run `calor init --ai gemini` and the skill files exist:

```bash
ls -la .gemini/skills/calor/SKILL.md
ls -la .gemini/skills/calor-convert/SKILL.md
```

---

## Next Steps

- [Syntax Reference](/calor/syntax-reference/) - Complete language reference
- [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) - Migration guide
- [calor init](/calor/cli/init/) - Full init command documentation
- [Claude Integration](/calor/getting-started/claude-integration/) - Alternative with Claude Code
- [Codex Integration](/calor/getting-started/codex-integration/) - Alternative with OpenAI Codex CLI
