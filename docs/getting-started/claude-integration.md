---
layout: default
title: Claude Integration
parent: Getting Started
nav_order: 3
---

# Using OPAL with Claude and AI Agents

OPAL is designed specifically for AI coding agents. This guide explains how to use OPAL with Claude and other AI assistants.

---

## Quick Setup

Initialize your project for Claude Code with a single command:

```bash
opalc init --ai claude
```

This creates:

| File | Purpose |
|:-----|:--------|
| `.claude/skills/opal.md` | Teaches Claude OPAL v2+ syntax for writing new code |
| `.claude/skills/opal-convert.md` | Teaches Claude how to convert C# to OPAL |
| `CLAUDE.md` | Project documentation with OPAL reference and conventions |

You can run this command again anytime to update the OPAL documentation section in CLAUDE.md without losing your custom content.

---

## Available Skills

### The `/opal` Skill

When working with Claude Code in an OPAL-initialized project, use the `/opal` command to activate OPAL-aware code generation.

**Example prompts:**

```
/opal

Write a function that calculates compound interest with:
- Preconditions: principal > 0, rate >= 0, years > 0
- Postcondition: result >= principal
- Effects: pure (no side effects)
```

```
/opal

Create a UserService class with methods for:
- GetUserById (returns Option<User>)
- CreateUser (returns Result<User, ValidationError>)
- DeleteUser (effects: database write)
```

### The `/opal-convert` Skill

Use `/opal-convert` to convert existing C# code to OPAL:

```
/opal-convert

Convert this C# class to OPAL:

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

Claude will:
1. Convert the class structure to OPAL syntax
2. Add appropriate contracts (e.g., `§Q (!= b 0)` for the divide precondition)
3. Generate unique IDs for all structural elements
4. Declare effects based on detected side effects

---

## Skill Capabilities

The OPAL skills teach Claude:

### Syntax Knowledge

- All OPAL v2+ structure tags (`§M`, `§F`, `§C`, etc.)
- Lisp-style expressions: `(+ a b)`, `(== x 0)`, `(% i 15)`
- Arrow syntax conditionals: `§IF{id} condition → action`
- Type system: `i32`, `f64`, `str`, `bool`, `Option<T>`, `Result<T,E>`, arrays

### Best Practices

- Unique ID generation (`m001`, `f001`, `c001`, etc.)
- Contract placement (`§Q` preconditions, `§S` postconditions)
- Effect declarations (`§E[db,net,cw]`)
- Proper structure nesting and closing tags

### Code Patterns

- Error handling with `Result<T,E>`
- Null safety with `Option<T>`
- Iteration patterns (for, while, do-while)
- Class definitions with fields, properties, methods

---

## Teaching Claude OPAL

If you're using Claude outside of Claude Code, you can teach it OPAL by including the syntax reference in your prompt:

### Minimal Prompt

```
I'm working with OPAL, a language for AI agents that compiles to C#.

Key syntax:
- §M[id:Name] / §/M[id] - Module
- §F[id:Name:vis] / §/F[id] - Function (pub/pri)
- §I[type:name] - Input parameter
- §O[type] - Output type
- §E[cw,fr,net] - Effects (console write, file read, network)
- §Q condition - Requires (precondition)
- §S condition - Ensures (postcondition)
- §L[id:var:from:to:step] / §/L[id] - Loop
- §IF[id] cond → action §EI cond → action §EL → action §/I[id] - Conditional
- §P expr - Print
- §R expr - Return
- (+ a b), (* a b), (== a b) - Lisp-style expressions

Example:
§M[m001:FizzBuzz]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §L[for1:i:1:100:1]
    §IF[if1] (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I[if1]
  §/L[for1]
§/F[f001]
§/M[m001]

Please write OPAL code for: [your request]
```

---

## Why OPAL Works Well with Agents

### 1. Unambiguous Structure

Traditional code:
```
// Which brace closes what?
if (x > 0) {
    for (int i = 0; i < n; i++) {
        if (y > 0) {
            // ...
        }
    }
}
```

OPAL code:
```
§IF[if1] (> x 0)
  §L[for1:i:0:n:1]
    §IF[if2] (> y 0)
      // ...
    §/I[if2]
  §/L[for1]
§/I[if1]
```

Every scope has explicit open and close tags with matching IDs. No ambiguity.

### 2. Precise Edit Targeting

Tell Claude: "Change the loop `for1` to start at 1 instead of 0"

```
// Before
§L[for1:i:0:n:1]

// After
§L[for1:i:1:n:1]
```

The ID `for1` uniquely identifies the target. No confusion with other loops.

### 3. Explicit Contracts for Verification

```
§F[f001:CalculateInterest:pub]
  §I[f64:principal]
  §I[f64:rate]
  §I[i32:years]
  §O[f64]
  §Q (> principal 0)
  §Q (>= rate 0)
  §Q (> years 0)
  §S (>= result principal)
  §R (* principal (** (+ 1 rate) years))
§/F[f001]
```

Claude can verify:
- All preconditions are satisfied at call sites
- The postcondition is mathematically correct
- No undeclared side effects

### 4. Effects for Side Effect Analysis

```
§F[f001:ProcessOrder:pub]
  §I[Order:order]
  §O[bool]
  §E[db,net,cw]  // Explicit effects
  // ...
§/F[f001]
```

Claude knows immediately that this function:
- Writes to database (`db`)
- Makes network calls (`net`)
- Writes to console (`cw`)

No need to analyze the implementation.

---

## Example Prompts

### Generate a Function

**Prompt:**
```
Write an OPAL function that calculates factorial with a precondition
that n >= 0 and postcondition that result >= 1
```

**Response:**
```
§M[m001:Math]
§F[f001:Factorial:pub]
  §I[i32:n]
  §O[i32]
  §Q (>= n 0)
  §S (>= result 1)
  §IF[if1] (<= n 1) → §R 1
  §EL → §R (* n §C[Factorial] §A (- n 1) §/C)
  §/I[if1]
§/F[f001]
§/M[m001]
```

### Find Bugs

**Prompt:**
```
Is there a bug in this OPAL code?

§F[f001:Divide:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §R (/ a b)
§/F[f001]
```

**Response:**
```
Yes - missing precondition. Add:

§Q (!= b 0)

Otherwise division by zero is possible.
```

### Refactor Code

**Prompt:**
```
Refactor this to extract the calculation into a separate function:

§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §B[x] (* 5 5)
  §P x
§/F[f001]
```

**Response:**
```
§F[f001:Square:pri]
  §I[i32:n]
  §O[i32]
  §R (* n n)
§/F[f001]

§F[f002:Main:pub]
  §O[void]
  §E[cw]
  §B[x] §C[Square] §A 5 §/C
  §P x
§/F[f002]
```

---

## Best Practices

1. **Always include IDs** - They enable precise references
2. **Declare effects** - Even if empty (`§E[]`)
3. **Write contracts** - They serve as executable documentation
4. **Use closing tags** - They prevent scope ambiguity
5. **Use V2 syntax** - `(+ a b)` not legacy verbose syntax

---

## Workflow Tips

### Starting a New Feature

```
/opal

I need to implement [feature description].

The requirements are:
- [requirement 1]
- [requirement 2]

Please create the OPAL code with appropriate contracts and effects.
```

### Converting Existing Code

```
/opal-convert

Convert src/Services/PaymentService.cs to OPAL, adding:
- Contracts based on the validation logic
- Effect declarations for database and network calls
```

### Refactoring OPAL

Reference specific elements by their IDs:

```
In PaymentService.opal:
- Extract the validation logic from f002 into a new private function
- Add a postcondition to f001 ensuring the result is positive
- Rename loop l001 to something more descriptive
```

### Debugging with Claude

```
Review OrderService.opal and identify:
1. Any missing preconditions that could cause runtime errors
2. Functions that should be marked pure but have undeclared effects
3. Opportunities to use Result<T,E> instead of exceptions
```

---

## IDE Integration

While OPAL skills work in any Claude Code session, you'll have the best experience with proper editor support:

### VS Code

1. Install the OPAL extension (if available)
2. Open your initialized project
3. Use `/opal` and `/opal-convert` commands

### Terminal

Claude Code works from any terminal:

```bash
cd my-opal-project
claude
```

Then use the skills as normal.

---

## Next Steps

- [Syntax Reference](/opal/syntax-reference/) - Complete language reference
- [Adding OPAL to Existing Projects](/opal/guides/adding-opal-to-existing-projects/) - Migration guide
- [opalc init](/opal/cli/init/) - Full init command documentation
- [Benchmarking](/opal/benchmarking/) - See how OPAL compares to C#
