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
§L{var:from:to:step}
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

C# conversion uses this range form for `int` loops with an expression initializer,
a constant bound, and a nonzero constant `++`, `--`, `+=`, or `-=` step when the
loop variable is not modified or exposed by reference in the body and the final
increment cannot overflow. The initializer still runs once, even for an empty
range. Other C# `for` loops retain exact condition reevaluation and overflow
behavior through the existing `§WH` lowering or C# preservation; they are not
silently treated as snapshot ranges.

### Examples

**Count 1 to 10:**
```
§L{i:1:10:1}
  §P i
```

**Count down:**
```
§L{i:10:1:-1}
  §P i
```

**Count by 2s:**
```
§L{i:0:100:2}
  §P i
```

**Using variable bounds:**
```
§L{i:1:n:1}
  §P i
```

**Using expressions:**
```
§L{i:0:(- n 1):1}
  §P i
```

### While Loop Syntax

```
§WH condition
  // body
```

| Part | Description |
|:-----|:------------|
| `id` | Unique loop identifier |
| `condition` | Boolean expression evaluated before each iteration |

### While Loop Examples

**Simple countdown:**
```
§B{~i} 10
§WH (> i 0)
  §P i
  §ASSIGN i (- i 1)
```

**Read until done:**
```
§B{~running} true
§WH running
  §B{input} §C{Console.ReadLine} §/C
  §IF (== input "quit")
    §ASSIGN running false
```

### Do-While Loop Syntax

```
§DO
  // body (executes at least once)
§/DO (condition)
```

| Part | Description |
|:-----|:------------|
| `id` | Unique loop identifier |
| `condition` | Boolean expression evaluated after each iteration |

The condition is placed at the end to match the semantics: the body always executes at least once, then the condition is checked.

The loop variable must be declared MUTABLE (`§B{~name:type}`) and updated with `§ASSIGN` inside the body. Re-binding it with a second `§B` inside the loop shadows the outer one, which is `Calor0255` (C# forbids it, CS0136), and the condition on the closer would then reference a variable the generated code cannot see.

`§/DO (condition)` is **required**. It is retained — unlike the structural closers removed in Phase 4d — precisely because it carries the condition, and there is nowhere else to put it. The id must match the opening `§DO`; a mismatch is reported as a mismatched-id error.

### Do-While Loop Examples

**Execute at least once:**
```
§B{~i} 0
§DO
  §P i
  §ASSIGN i (+ i 1)
§/DO (< i INT:10)
```

**Menu loop (always show menu first):**
```
§B{~choice:i32} 0
§DO
  §P "1. Option A"
  §P "2. Option B"
  §P "3. Exit"
  §ASSIGN choice §C{ReadChoice} §/C
§/DO (!= choice INT:3)
```

**Retry until success:**
```
§B{~success:bool} false
§DO
  §ASSIGN success §C{TryOperation} §/C
§/DO (! success)
```

---

## Dictionary Iteration

Use `§EACHKV` to iterate over key-value pairs in a dictionary.

### Syntax

```
§EACHKV{keyVar:valueVar} dictName
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

§EACHKV{name:age} ages
  §P name
  §P age
```

**Sum all values:**
```
§B{~total} 0
§EACHKV{k:v} scores
  §ASSIGN total (+ total v)
```

**Conditional processing:**
```
§EACHKV{key:val} data
  §IF (> val 100)
    §P key
```

### Comparison with §EACH

| Loop Type | Use Case |
|:----------|:---------|
| `§L{var:from:to:step}` | Numeric ranges |
| `§EACH{id:var} collection` | Lists, arrays, sets |
| `§EACHKV{k:v} dict` | Dictionaries (key-value pairs) |

---

## Conditionals

### Indented Branches

For simple single-action branches:

```
§IF condition
  action
§EI condition
  action
§EL
  action
```

### Multiple Statements

For complex branches:

```
§IF condition
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
| `§EI` | Else-if (optional, can repeat) at same column as `§IF` |
| `§EL` | Else (optional, at most one) at same column as `§IF` |
| `§IF cond` | Explicit ID form (optional, for tooling references) |

---

## Conditional Examples

### Simple If

```
§IF (> x 0)
  §P "positive"
```

### If-Else

```
§IF (> x 0)
  §P "positive"
§EL
  §P "not positive"
```

### If-ElseIf-Else

```
§IF (> x 0)
  §P "positive"
§EI (< x 0)
  §P "negative"
§EL
  §P "zero"
```

### Single Line with Multiple Branches

```
§IF (== (% i 15) 0)
  §P "FizzBuzz"
§EI (== (% i 3) 0)
  §P "Fizz"
§EI (== (% i 5) 0)
  §P "Buzz"
§EL
  §P i
```

### Nested Conditionals

```
§IF (> x 0)
  §IF (< x 100)
    §P "between 0 and 100"
  §EL
    §P "100 or greater"
```

---

## FizzBuzz Complete Example

```
§M{FizzBuzz}
  §F{Main:pub}
    §O{void}
    §E{cw}
    §L{i:1:100:1}
      §IF (== (% i 15) 0)
        §P "FizzBuzz"
      §EI (== (% i 3) 0)
        §P "Fizz"
      §EI (== (% i 5) 0)
        §P "Buzz"
      §EL
        §P i
```

---

## Loop with Conditional

```
§M{Example}
  §F{PrintEvens:pub}
    §I{i32:n}
    §O{void}
    §E{cw}
    §Q (> n 0)
    §L{i:1:n:1}
      §IF (== (% i 2) 0)
        §P i
```

---

## Early Return

Use conditionals with return for early exit:

```
§F{Factorial:pub}
  §I{i32:n}
  §O{i32}
  §Q (>= n 0)
  §IF (<= n 1)
    §R 1
  §EL
    §R (* n §C{Factorial} §A (- n 1) §/C)
```

---

## Iterators and Yield

Use `§YIELD expression` to produce the next iterator value and `§YBRK` to stop
iteration:

```
§F{Count:pub}
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
§W expression
  §K pattern1
    §R result1
  §K pattern2
    §R result2
  §K _
    §R default
```

| Part | Description |
|:-----|:------------|
| `§W expr` | Switch expression (cases indent below) |
| `expression` | Value to match against |
| `§K` | Case keyword (indented under `§W`) |
| `pattern` | Pattern to match |
| `→` | Arrow to result (single expression) |
| `_` | Wildcard (matches anything) |
| `§W expr` | Explicit ID form (optional) |

### Literal Patterns

Match exact values:

```
§B{day} §W dayNum
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
§B{grade} §W score
  §K §PREL{gte} 90 → "A"
  §K §PREL{gte} 80 → "B"
  §K §PREL{gte} 70 → "C"
  §K §PREL{gte} 60 → "D"
  §K _ → "F"
```

### Variable Patterns with Guards (`§VAR`, `§WHEN`)

Capture the matched value and add conditions:

```
§B{desc} §W value
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

### Nullable Values and Exceptions

Use the existing null-coalescing expression `(?? value fallback)` for a missing
nullable value. Handle failures with `§TR` / `§CA`, not success/error patterns.
See [Types](types.md#failure-uses-exceptions) for a complete throwing function.

### Statement Match Blocks

Use a statement-position match for cases with multiple statements. Indentation
delimits each block:

```
§W x
  §K 1
    §R "one"
  §K 2
    §P "matched two"         // Block syntax (multiple statements)
    §R "two"
  §K _
    §R "other"
```

An expression-position match, such as `§R §W x` or a binding initializer,
supports arrow arms and blocks containing exactly one `§R value`. Other block
shapes produce the fatal diagnostic `Calor1006`: the compiler does not discard
statements or substitute `default`. This restriction also applies inside lambdas
and when optional type checking is disabled.

### Complete Example

```
§M{HttpStatus}
  §F{GetStatusMessage:pub}
    §I{i32:code}
    §O{str}
    §R §W code
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

## Conditional Bodies

Every `§IF`, `§EI`, and `§EL` statement uses an indented body, even for one
statement. Arrow statement bodies are rejected. Arrows still separate a match
expression's pattern from its result.

---

## Next

- [Contracts](/calor/syntax-reference/contracts/) - Preconditions and postconditions
