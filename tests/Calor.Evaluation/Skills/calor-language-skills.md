# Calor Language Skills

You are writing code in **Calor**, a programming language designed for AI coding agents. Calor compiles to C# and emphasizes explicit contracts, effects, and unambiguous syntax.

## Core Philosophy

Calor is designed for:
- **Correctness**: First-class contracts (preconditions/postconditions) make requirements explicit
- **Clarity**: No operator precedence ambiguity - all expressions use prefix notation
- **Verifiability**: Constraints are machine-checkable, not just documentation

## Contract-First Methodology

**Before writing implementation, extract and express constraints as contracts.** This is your primary tool for ensuring correctness.

### Step 1: Identify Input Constraints (Preconditions)

Read the requirement and find ALL constraints on inputs:
- "must be positive" → `§Q (> n 0)`
- "must not be zero" → `§Q (!= n 0)`
- "X must not exceed Y" → `§Q (<= x y)`
- Any division or modulo operation → `§Q (!= divisor 0)`

### Step 2: Identify Output Guarantees (Postconditions)

Find what the function promises about its result:
- "result is never negative" → `§S (>= result 0)`
- "result is at least 1" → `§S (>= result 1)`
- "result is bounded by input" → `§S (<= result n)`

### Step 3: Write Contracts BEFORE Implementation

The contracts become your specification. **If you can't satisfy a postcondition, your implementation is wrong.**

```calor
// FIRST: Write the contracts
§F{f001:MyFunction:pub}
  §I{i32:n}
  §O{i32}
  §Q (> n 0)           // Input constraint from requirement
  §S (>= result 0)     // Output guarantee from requirement
  // THEN: Implement logic that satisfies the contracts
  §R ...
§/F{f001}
```

## Why Contracts Give You an Advantage

Unlike C# where validation is optional and easily forgotten:

1. **Contracts are verified at compile time** - Z3 SMT solver proves them when possible
2. **Contracts always execute at runtime** - ContractViolationException with precise location
3. **Contracts document your understanding** - If the contract is wrong, you misunderstood the requirement
4. **Contracts catch bugs early** - Invalid inputs fail immediately, not deep in logic

Example: A division function without `§Q (!= b 0)` will produce a compile-time warning (Calor0920).

## Self-Verification with Contracts

Use contracts to verify your implementation:

- **If you can't make a postcondition true** → Your implementation is wrong
- **If a precondition seems impossible** → You misunderstood the requirement
- **If contracts conflict** → The requirement has contradictions

Think of contracts as executable test cases that run on every call.

## When to Use Calor Features

### Contracts (Preconditions and Postconditions)

Use contracts to express requirements and guarantees mentioned in the task:

| Requirement Pattern | Calor Contract |
|---------------------|----------------|
| "must be positive" | `§Q (> n 0)` |
| "must be non-negative" | `§Q (>= n 0)` |
| "must not be zero" | `§Q (!= n 0)` |
| "must be between X and Y (inclusive)" | `§Q (>= n X)` and `§Q (<= n Y)` |
| "must be even" | `§Q (== (% n 2) 0)` |
| "must be odd" | `§Q (!= (% n 2) 0)` |
| "X must not exceed Y" | `§Q (<= x y)` |
| "X must be less than Y" | `§Q (< x y)` |
| division or modulo by Y | Always `§Q (!= y 0)` |
| "result is never negative" | `§S (>= result 0)` |
| "result is always positive" | `§S (> result 0)` |
| "result is at least 1" | `§S (>= result 1)` |
| "result is bounded by input" | `§S (<= result n)` |
| "result is within range [min, max]" | `§S (>= result min)` and `§S (<= result max)` |

**Preconditions (`§Q`)** express what callers must guarantee.
**Postconditions (`§S`)** express what the function guarantees to return.

### Syntax Quick Reference

#### Function Structure
```calor
§F{id:FunctionName:pub}
  §I{type:paramName}     // Input parameter
  §O{returnType}         // Return type
  §Q (condition)         // Precondition (0 or more)
  §S (condition)         // Postcondition (0 or more)
  // body
  §R expression          // Return
§/F{id}
```

#### Types
| Calor | Meaning |
|-------|---------|
| `i32` | 32-bit integer |
| `i64` | 64-bit integer |
| `bool` | Boolean |
| `str` | String |
| `void` | No return value |

#### Expressions (Prefix Notation)
```calor
(+ a b)       // a + b
(- a b)       // a - b
(* a b)       // a * b
(/ a b)       // a / b
(% a b)       // a % b (modulo)

(== a b)      // a == b
(!= a b)      // a != b
(< a b)       // a < b
(<= a b)      // a <= b
(> a b)       // a > b
(>= a b)      // a >= b

(&& a b)      // a AND b
(|| a b)      // a OR b
(! a)         // NOT a
```

#### Control Flow

**IMPORTANT: Arrow Syntax vs Block Syntax**

Calor has two styles for if statements. Choosing the wrong one causes compilation errors.

**Arrow syntax (`→`) - SINGLE STATEMENT ONLY:**
```calor
§IF{id} (condition) → §R value1
§EI (condition2) → §R value2
§EL → §R value3
§/I{id}
```
- Use ONLY when each branch has exactly ONE statement
- The statement must immediately follow the arrow
- Always end with `§/I{id}`

**Block syntax - MULTIPLE STATEMENTS:**
```calor
§IF{id} (condition)
  §R value1
§EL
  // multiple statements allowed here
  §B{x} 5
  §R x
§/I{id}
```
- Use when ANY branch needs more than one statement
- No arrow after the condition
- Statements go on following lines

**COMMON MISTAKE - Do NOT do this:**
```calor
// WRONG - arrow syntax cannot have statements after it on separate lines
§IF{if1} (== x 0) → §R false
§ASSIGN y (+ y 1)    // ERROR: parser expects §/I or §EI here
§/I{if1}
```

**CORRECT - use block syntax when you need multiple statements:**
```calor
§IF{if1} (== x 0)
  §R false
§/I{if1}
§ASSIGN y (+ y 1)    // Now this is outside the if, which is correct
```

**Or if both statements should be conditional:**
```calor
§IF{if1} (== x 0)
  §R false
§EL
  §ASSIGN y (+ y 1)
§/I{if1}
```

**For Loop:**
```calor
§L{id:var:start:end:step}
  // body (var goes from start to end inclusive)
§/L{id}
```

**While Loop:**
```calor
§WH{id} (condition)
  // body - use block-style ifs inside loops
§/WH{id}
```

**Nested Control Flow Pattern:**
When you have a loop with conditional logic inside, use block-style for inner ifs:
```calor
§WH{wh1} (condition)
  §IF{if1} (check)
    §R result
  §/I{if1}
  §ASSIGN i (+ i 1)
§/WH{wh1}
```

#### Bindings and Assignment
```calor
§B{varName} expression    // Create binding: varName = expression
§ASSIGN varName expr      // Update existing binding
```

#### Return
```calor
§R expression             // Return the expression's value
```

## Complete Examples

### Simple Function (no constraints)
```calor
§F{f001:Square:pub}
  §I{i32:n}
  §O{i32}
  §R (* n n)
§/F{f001}
```

### Function with Precondition
```calor
§F{f001:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
§/F{f001}
```

### Function with Postcondition
```calor
§F{f001:Abs:pub}
  §I{i32:n}
  §O{i32}
  §S (>= result 0)
  §IF{if1} (< n 0) → §R (- 0 n)
  §EL → §R n
  §/I{if1}
§/F{f001}
```

### Function with Both Pre and Postconditions
```calor
§F{f001:Clamp:pub}
  §I{i32:value}
  §I{i32:min}
  §I{i32:max}
  §O{i32}
  §Q (<= min max)
  §S (>= result min)
  §S (<= result max)
  §IF{if1} (< value min) → §R min
  §EI (> value max) → §R max
  §EL → §R value
  §/I{if1}
§/F{f001}
```

### Recursive Function
```calor
§F{f001:Factorial:pub}
  §I{i32:n}
  §O{i32}
  §Q (>= n 0)
  §S (>= result 1)
  §IF{if1} (<= n 1) → §R 1
  §EL → §R (* n §C{Factorial} §A (- n 1) §/C)
  §/I{if1}
§/F{f001}
```

### Loop-based Function
```calor
§F{f001:Power:pub}
  §I{i32:base}
  §I{i32:exp}
  §O{i32}
  §Q (>= exp 0)
  §B{result} 1
  §L{for1:i:1:exp:1}
    §ASSIGN result (* result base)
  §/L{for1}
  §R result
§/F{f001}
```

### Loop with Conditional (Block-Style)
When a loop contains an if statement, always use block-style for the inner if:
```calor
§F{f001:IsPrime:pub}
  §I{i32:n}
  §O{bool}
  §Q (> n 0)
  §IF{if1} (<= n 1) → §R false
  §EI (== n 2) → §R true
  §EI (== (% n 2) 0) → §R false
  §/I{if1}
  §B{i} 3
  §WH{wh1} (<= (* i i) n)
    §IF{if2} (== (% n i) 0)
      §R false
    §/I{if2}
    §ASSIGN i (+ i 2)
  §/WH{wh1}
  §R true
§/F{f001}
```

### Compound Contracts

For complex requirements, combine multiple contracts:

**Absolute value** - result is non-negative and equals n or -n:
```calor
§F{f001:Abs:pub}
  §I{i32:n}
  §O{i32}
  §S (>= result 0)
  §S (|| (== result n) (== result (- 0 n)))
  §IF{if1} (< n 0) → §R (- 0 n)
  §EL → §R n
  §/I{if1}
§/F{f001}
```

**Factorial** - input non-negative, result at least 1:
```calor
§F{f001:Factorial:pub}
  §I{i32:n}
  §O{i32}
  §Q (>= n 0)
  §S (>= result 1)
  §IF{if1} (<= n 1) → §R 1
  §EL → §R (* n §C{Factorial} §A (- n 1) §/C)
  §/I{if1}
§/F{f001}
```

**Clamp to range** - valid range required, result bounded:
```calor
§F{f001:Clamp:pub}
  §I{i32:value}
  §I{i32:min}
  §I{i32:max}
  §O{i32}
  §Q (<= min max)
  §S (>= result min)
  §S (<= result max)
  §IF{if1} (< value min) → §R min
  §EI (> value max) → §R max
  §EL → §R value
  §/I{if1}
§/F{f001}
```

**Sum of range** - start <= end, result non-negative for non-negative inputs:
```calor
§F{f001:SumRange:pub}
  §I{i32:start}
  §I{i32:end}
  §O{i32}
  §Q (<= start end)
  §B{sum} 0
  §L{for1:i:start:end:1}
    §ASSIGN sum (+ sum i)
  §/L{for1}
  §R sum
§/F{f001}
```

## Guidelines for Writing Calor

1. **WRITE CONTRACTS FIRST**: Before implementing logic, extract ALL constraints from the requirement:
   - Read the requirement carefully for words like "must", "only", "never", "always", "at least", "at most"
   - Write `§Q` preconditions for every input constraint
   - Write `§S` postconditions for every output guarantee
   - Any division or modulo → add `§Q (!= divisor 0)`
   - THEN implement logic that satisfies the contracts

2. **Translate constraints to contracts**: When the task says "must only accept X" or "X must not be Y", use `§Q` preconditions. When it says "result is always X", use `§S` postconditions.

3. **Use prefix notation consistently**: Write `(+ a b)` not `a + b`. Nest expressions: `(+ 1 (* 2 3))` for `1 + 2 * 3`.

4. **Choose appropriate IDs**: Use meaningful IDs like `f001`, `if1`, `for1` for structures.

5. **Keep functions focused**: One function per task, with clear inputs, outputs, and contracts.

6. **Arrow vs Block syntax for if statements**:
   - Use arrow syntax (`→`) ONLY for single-statement branches
   - Use block syntax (no arrow) when you need multiple statements
   - Inside loops, prefer block-style for inner if statements
   - When in doubt, use block syntax - it always works

7. **Close all structures**: Every `§IF{id}` needs `§/I{id}`, every `§WH{id}` needs `§/WH{id}`, etc. The closing ID must match the opening ID.

8. **Use contracts to verify your work**: If a postcondition fails at runtime, your implementation is incorrect. Contracts are your self-checking mechanism.
