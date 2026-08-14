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
```

| Part | Description |
|:-----|:------------|
| `id` | Unique loop identifier |
| `var` | Loop variable name |
| `from` | Starting value (inclusive) |
| `to` | Ending value (inclusive) |
| `step` | Increment per iteration |

The `from`, `to`, and `step` expressions are each evaluated exactly once, before
the loop starts. The evaluated sign of `step` selects ascending (`<=`) or
descending (`>=`) comparison. A zero step throws
`ArgumentOutOfRangeException` before the first iteration. Integral advancement
is checked: reaching a numeric minimum or maximum endpoint terminates normally
instead of wrapping around into an unbounded loop.

When `step` is omitted, Calor advances with a checked increment of the evaluated
induction value itself. This keeps the implicit `+1` in the loop variable's
integral type (`i8` through `u64`) and avoids mixed-type `+=` or narrowing.

### Examples

**Count 1 to 10:**
```
§L{for1:i:1:10:1}
  §P i
```

**Count down:**
```
§L{for1:i:10:1:-1}
  §P i
```

**Count by 2s:**
```
§L{for1:i:0:100:2}
  §P i
```

**Using variable bounds:**
```
§L{for1:i:1:n:1}
  §P i
```

**Using expressions:**
```
§L{for1:i:0:(- n 1):1}
  §P i
```

### While Loop Syntax

```
§WH{id} condition
  // body
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
```

**Read until done:**
```
§B{running} true
§WH{while1} running
  §B{input} §C{Console.ReadLine} §/C
  §IF{if1} (== input "quit")
    §ASSIGN running false
```

### Do-While Loop Syntax

```
§DO{id}
  // body (executes at least once)
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
```

**Menu loop (always show menu first):**
```
§B{choice} 0
§DO{do1}
  §P "1. Option A"
  §P "2. Option B"
  §P "3. Exit"
  §B{choice} §C{ReadChoice} §/C
```

**Retry until success:**
```
§B{success} false
§DO{do1}
  §B{success} §C{TryOperation} §/C
```

---

## Dictionary Iteration

Use `§EACHKV` to iterate over key-value pairs in a dictionary.

### Syntax

```
§EACHKV{id:keyVar:valueVar} dictName
  // body uses keyVar and valueVar
```

| Part | Description |
|:-----|:------------|
| `id` | Unique loop identifier |
| `keyVar` | Variable name for the current key |
| `valueVar` | Variable name for the current value |
| `dictName` | Name of the dictionary to iterate |

### Examples

**Print all entries:**
```
§DICT{ages:str:i32}
  §KV "alice" 30
  §KV "bob" 25

§EACHKV{e1:name:age} ages
  §P name
  §P age
```

**Sum all values:**
```
§B{total} 0
§EACHKV{e1:k:v} scores
  §ASSIGN total (+ total v)
```

**Conditional processing:**
```
§EACHKV{e1:key:val} data
  §IF{if1} (> val 100)
    §P key
```

### Comparison with §EACH

| Loop Type | Use Case |
|:----------|:---------|
| `§L{id:var:from:to:step}` | Numeric ranges |
| `§EACH{id:var} collection` | Lists, arrays, sets |
| `§EACHKV{id:k:v} dict` | Dictionaries (key-value pairs) |

---

## Conditionals

### Single Line (Arrow Syntax)

For simple single-action branches:

```
§IF{id} condition → action
§EI condition → action
§EL → action
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
```

### Parts

| Part | Description |
|:-----|:------------|
| `§IF cond` | If statement (block body indents below) |
| `condition` | Boolean expression |
| `→` | Arrow separator (single-line inline form) |
| `§EI` | Else-if (optional, can repeat) at same column as `§IF` |
| `§EL` | Else (optional, at most one) at same column as `§IF` |
| `§IF{id} cond` | Explicit ID form (optional, for tooling references) |

---

## Conditional Examples

### Simple If

```
§IF{if1} (> x 0) → §P "positive"
```

### If-Else

```
§IF{if1} (> x 0)
  §P "positive"
§EL
  §P "not positive"
```

### If-ElseIf-Else

```
§IF{if1} (> x 0)
  §P "positive"
§EI (< x 0)
  §P "negative"
§EL
  §P "zero"
```

### Single Line with Multiple Branches

```
§IF{if1} (== (% i 15) 0) → §P "FizzBuzz"
§EI (== (% i 3) 0) → §P "Fizz"
§EI (== (% i 5) 0) → §P "Buzz"
§EL → §P i
```

### Nested Conditionals

```
§IF{if1} (> x 0)
  §IF{if2} (< x 100)
    §P "between 0 and 100"
  §EL
    §P "100 or greater"
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
```

---

## Iterators and Yield

Use `§YIELD expression` to produce the next iterator value and `§YBRK` to stop
iteration:

```
§F{f001:Count:pub}
  §O{i32}
  §YIELD 1
  §YIELD 2
  §YBRK
```

`§YIELD` always requires an expression. A valueless `§YIELD` is rejected; it
does not mean `yield break`. Use `§YBRK` explicitly.

Yield detection is structural across nested statement containers such as
matches, loops, `using`, and synchronization blocks. A yield inside a nested
lambda belongs to the lambda and is rejected rather than turning the enclosing
function into an iterator. Calor also rejects C#-illegal suspension points:
value-yields in catch, unsafe, fixed, or try-with-catch regions; every yield in
finally; and all yields in constructors, property/indexer accessors, event
accessors, operators, and async callables. Property and indexer iterators are
fail-closed until Calor defines their return-type and deferred-execution
semantics.

Iterator functions and methods may only use value parameters. Parameters marked
`ref`, `in`, or `out` are rejected before C# emission. This applies equally to
generic, static, and async callable shapes; yields inside nested lambdas or local
functions do not turn the enclosing callable into an iterator.

---

## Pattern Matching

Pattern matching provides concise multi-way branching with C# switch expression semantics.

### Switch Expression Syntax

```
§W{id} expression
  §K pattern1 → result1
  §K pattern2 → result2
  §K _ → default
```

| Part | Description |
|:-----|:------------|
| `§W expr` | Switch expression (cases indent below) |
| `expression` | Value to match against |
| `§K` | Case keyword (indented under `§W`) |
| `pattern` | Pattern to match |
| `→` | Arrow to result (single expression) |
| `_` | Wildcard (matches anything) |
| `§W{id} expr` | Explicit ID form (optional) |

### Literal Patterns

Match exact values:

```
§B{day} §W{sw1} dayNum
  §K 0 → "Sunday"
  §K 1 → "Monday"
  §K 2 → "Tuesday"
  §K _ → "Other"
```

### Relational Patterns (`§PREL`)

Match value ranges using relational operators:

| Syntax | Meaning | C# Equivalent |
|:-------|:--------|:--------------|
| `§PREL{gte} value` | Greater than or equal | `>= value` |
| `§PREL{gt} value` | Greater than | `> value` |
| `§PREL{lte} value` | Less than or equal | `<= value` |
| `§PREL{lt} value` | Less than | `< value` |

**Example - Grade calculation:**
```
§B{grade} §W{sw1} score
  §K §PREL{gte} 90 → "A"
  §K §PREL{gte} 80 → "B"
  §K §PREL{gte} 70 → "C"
  §K §PREL{gte} 60 → "D"
  §K _ → "F"
```

### Variable Patterns with Guards (`§VAR`, `§WHEN`)

Capture the matched value and add conditions:

```
§B{desc} §W{sw1} value
  §K §VAR{n} §WHEN (> n 100) → "large positive"
  §K §VAR{n} §WHEN (> n 0) → "small positive"
  §K 0 → "zero"
  §K §VAR{n} §WHEN (> n -100) → "small negative"
  §K _ → "large negative"
```

| Part | Description |
|:-----|:------------|
| `§VAR{name}` | Captures value into variable `name` |
| `§WHEN condition` | Guard condition (pattern matches only if true) |

### Option Patterns (`§SM`, `§NN`)

Match Option types:

```
§R §W{sw1} maybeValue
  §K §SM §VAR{v} → v        // Some(v) - extract value
  §K §NN → 0                 // None - default
```

### Result Patterns (`§OK`, `§ERR`)

Match Result types:

```
§R §W{sw1} result
  §K §OK §VAR{v} → (+ "Success: " v)
  §K §ERR §VAR{e} → (+ "Error: " e)
```

### Block Syntax (`§/K`)

For cases with multiple statements, use block syntax:

```
§W{sw1} x
  §K 1 → "one"              // Arrow syntax (single expression)
  §K 2
    §P "matched two"         // Block syntax (multiple statements)
    §R "two"
    §/K
  §K _ → "other"
```

### Complete Example

```
§M{m001:HttpStatus}
  §F{f001:GetStatusMessage:pub}
    §I{i32:code}
    §O{str}
    §R §W{sw1} code
      §K 200 → "OK"
      §K 201 → "Created"
      §K 400 → "Bad Request"
      §K 404 → "Not Found"
      §K 500 → "Server Error"
      §K _ → "Unknown Status"
```

---

## Why Explicit Loop IDs?

1. **Precise targeting** - "Modify loop for1" is unambiguous
2. **Verification** - Compiler uses indentation to determine loop boundaries
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
