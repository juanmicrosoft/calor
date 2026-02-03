---
layout: default
title: Control Flow
parent: Syntax Reference
nav_order: 4
---

# Control Flow

Calor provides loops and conditionals with explicit structure.

---

## Loops

### For Loop Syntax

```
§L{id:var:from:to:step}
  // body
§/L{id}
```

| Part | Description |
|:-----|:------------|
| `id` | Unique loop identifier |
| `var` | Loop variable name |
| `from` | Starting value (inclusive) |
| `to` | Ending value (inclusive) |
| `step` | Increment per iteration |

### Examples

**Count 1 to 10:**
```
§L{for1:i:1:10:1}
  §P i
§/L{for1}
```

**Count down:**
```
§L{for1:i:10:1:-1}
  §P i
§/L{for1}
```

**Count by 2s:**
```
§L{for1:i:0:100:2}
  §P i
§/L{for1}
```

**Using variable bounds:**
```
§L{for1:i:1:n:1}
  §P i
§/L{for1}
```

**Using expressions:**
```
§L{for1:i:0:(- n 1):1}
  §P i
§/L{for1}
```

### While Loop Syntax

```
§WH{id} condition
  // body
§/WH{id}
```

| Part | Description |
|:-----|:------------|
| `id` | Unique loop identifier |
| `condition` | Boolean expression evaluated before each iteration |

### While Loop Examples

**Simple countdown:**
```
§B{i} 10
§WH{while1} (> i 0)
  §P i
  §ASSIGN i (- i 1)
§/WH{while1}
```

**Read until done:**
```
§B{running} true
§WH{while1} running
  §B{input} §C{Console.ReadLine} §/C
  §IF{if1} (== input "quit")
    §ASSIGN running false
  §/I{if1}
§/WH{while1}
```

### Do-While Loop Syntax

```
§DO{id}
  // body (executes at least once)
§/DO{id} condition
```

| Part | Description |
|:-----|:------------|
| `id` | Unique loop identifier |
| `condition` | Boolean expression evaluated after each iteration |

The condition is placed at the end to match the semantics: the body always executes at least once, then the condition is checked.

### Do-While Loop Examples

**Execute at least once:**
```
§B{i} 0
§DO{do1}
  §P i
  §ASSIGN i (+ i 1)
§/DO{do1} (< i 5)
```

**Menu loop (always show menu first):**
```
§B{choice} 0
§DO{do1}
  §P "1. Option A"
  §P "2. Option B"
  §P "3. Exit"
  §B{choice} §C{ReadChoice} §/C
§/DO{do1} (!= choice 3)
```

**Retry until success:**
```
§B{success} false
§DO{do1}
  §B{success} §C{TryOperation} §/C
§/DO{do1} (! success)
```

---

## Conditionals

### Single Line (Arrow Syntax)

For simple single-action branches:

```
§IF{id} condition → action
§EI condition → action
§EL → action
§/I{id}
```

### Multi-Line (Block Syntax)

For complex branches:

```
§IF{id} condition
  // multiple statements
§EI condition
  // multiple statements
§EL
  // multiple statements
§/I{id}
```

### Parts

| Part | Description |
|:-----|:------------|
| `§IF{id}` | If statement with unique ID |
| `condition` | Boolean expression |
| `→` | Arrow separator (single-line only) |
| `§EI` | Else-if (optional, can repeat) |
| `§EL` | Else (optional, at most one) |
| `§/I{id}` | Closing tag (ID must match) |

---

## Conditional Examples

### Simple If

```
§IF{if1} (> x 0) → §P "positive"
§/I{if1}
```

### If-Else

```
§IF{if1} (> x 0)
  §P "positive"
§EL
  §P "not positive"
§/I{if1}
```

### If-ElseIf-Else

```
§IF{if1} (> x 0)
  §P "positive"
§EI (< x 0)
  §P "negative"
§EL
  §P "zero"
§/I{if1}
```

### Single Line with Multiple Branches

```
§IF{if1} (== (% i 15) 0) → §P "FizzBuzz"
§EI (== (% i 3) 0) → §P "Fizz"
§EI (== (% i 5) 0) → §P "Buzz"
§EL → §P i
§/I{if1}
```

### Nested Conditionals

```
§IF{if1} (> x 0)
  §IF{if2} (< x 100)
    §P "between 0 and 100"
  §EL
    §P "100 or greater"
  §/I{if2}
§/I{if1}
```

---

## FizzBuzz Complete Example

```
§M{m001:FizzBuzz}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §L{for1:i:1:100:1}
    §IF{if1} (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I{if1}
  §/L{for1}
§/F{f001}
§/M{m001}
```

---

## Loop with Conditional

```
§M{m001:Example}
§F{f001:PrintEvens:pub}
  §I{i32:n}
  §O{void}
  §E{cw}
  §Q (> n 0)
  §L{for1:i:1:n:1}
    §IF{if1} (== (% i 2) 0)
      §P i
    §/I{if1}
  §/L{for1}
§/F{f001}
§/M{m001}
```

---

## Early Return

Use conditionals with return for early exit:

```
§F{f001:Factorial:pub}
  §I{i32:n}
  §O{i32}
  §Q (>= n 0)
  §IF{if1} (<= n 1) → §R 1
  §EL → §R (* n §C{Factorial} §A (- n 1) §/C)
  §/I{if1}
§/F{f001}
```

---

## Why Explicit Loop IDs?

1. **Precise targeting** - "Modify loop for1" is unambiguous
2. **Verification** - Compiler checks matching `§L` and `§/L`
3. **Agent-friendly** - Easy to identify loop boundaries
4. **Refactoring safe** - IDs survive code movement

---

## Why Arrow Syntax?

The arrow `→` provides:

1. **Single-line clarity** - `§IF cond → action` is compact
2. **Readable flow** - Condition "leads to" action
3. **Consistent pattern** - Same syntax for if, elseif, else

---

## Next

- [Contracts](/calor/syntax-reference/contracts/) - Preconditions and postconditions
