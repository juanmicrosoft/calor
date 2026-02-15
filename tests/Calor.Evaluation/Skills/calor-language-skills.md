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

#### String Operations

**IMPORTANT: Use these operations for string manipulation. Do NOT invent syntax.**

```calor
// Type conversion - CRITICAL for building strings from numbers
(str x)           // Convert ANY value to string: (str 42) → "42"

// String concatenation - use for building formatted strings
(concat a b c)    // Concatenate strings: (concat "Hello" " " "World") → "Hello World"

// Format strings - printf-style with {0}, {1} placeholders
(fmt "{0}:{1}" type id)  // Format: (fmt "User:{0}" 123) → "User:123"

// String queries
(len s)           // Length: (len "hello") → 5
(contains s t)    // Contains: (contains "hello" "ell") → true
(starts s t)      // Starts with: (starts "hello" "he") → true
(ends s t)        // Ends with: (ends "hello" "lo") → true
(indexof s t)     // Index of FIRST occurrence: (indexof "hello" "l") → 2
                  // NOTE: Only takes 2 args. No startIndex parameter!
(isempty s)       // Is null or empty: (isempty "" ) → true
(equals s t)      // String equality: (equals "hi" "hi") → true

// NOTE: No < or > comparison on strings! Use (equals) for equality only.
// For lexicographic comparison, compare character codes manually.

// String transforms
(upper s)         // Uppercase: (upper "hello") → "HELLO"
(lower s)         // Lowercase: (lower "HELLO") → "hello"
(trim s)          // Trim whitespace: (trim "  hi  ") → "hi"
(substr s i n)    // Substring: (substr "hello" 1 3) → "ell"
(substr s i)      // Substring to end: (substr "hello" 2) → "llo"
(replace s a b)   // Replace: (replace "hello" "l" "L") → "heLLo"

// Character operations
(char-at s i)     // Character at index: (char-at "hello" 0) → 'h'
```

#### Character Operations

**CRITICAL: Calor does NOT support single-quoted character literals.**

```calor
// WRONG - single quotes cause "Unexpected character '''" error
(== c '0')        // ❌ ERROR - single quotes not supported
(== c '-')        // ❌ ERROR - single quotes not supported
```

**Instead, use character classification predicates or numeric code comparisons:**

```calor
// Character classification - check what type of character
(is-digit c)        // true if c is '0'-'9'
(is-letter c)       // true if c is a letter (any language)
(is-whitespace c)   // true if c is space, tab, newline, etc.
(is-upper c)        // true if c is uppercase letter
(is-lower c)        // true if c is lowercase letter

// Character extraction and conversion
(char-at s i)       // Get character at index i from string s
(char-code c)       // Get Unicode code point: (char-code c) where c='A' → 65
(char-from-code n)  // Create char from code: (char-from-code 65) → 'A'
(char-upper c)      // Convert to uppercase
(char-lower c)      // Convert to lowercase

// Compare characters using numeric codes
(== (char-code c) 48)   // c == '0' (code 48)
(== (char-code c) 45)   // c == '-' (code 45)
(>= (char-code c) 65)   // c >= 'A' (code 65)
(<= (char-code c) 90)   // c <= 'Z' (code 90)
```

**Common Character Codes Reference:**
| Character | Code | Character | Code |
|-----------|------|-----------|------|
| `'0'` | 48 | `'9'` | 57 |
| `'A'` | 65 | `'Z'` | 90 |
| `'a'` | 97 | `'z'` | 122 |
| `'-'` | 45 | `'_'` | 95 |
| `' '` (space) | 32 | `'.'` | 46 |

**Character Operation Examples:**

```calor
// Check if character is a digit - PREFERRED way
§IF{if1} (is-digit c) → §R true
§/I{if1}

// Check if character is '0' using code comparison
§IF{if2} (== (char-code c) 48) → §R true
§/I{if2}

// Check if character is in range '0'-'9' using codes
§B{code} (char-code c)
§IF{if3} (&& (>= code 48) (<= code 57))
  §R true
§/I{if3}

// Iterate through string checking each character
§B{i} 0
§WH{wh1} (< i (len s))
  §B{c} (char-at s i)
  §IF{if4} (is-digit c)
    // process digit
  §/I{if4}
  §ASSIGN i (+ i 1)
§/WH{wh1}
```

**Common Patterns for Building Formatted Strings:**

```calor
// Pattern 1: Using concat with str for number conversion
// Task: Return "{type}:{id}"
§R (concat type ":" (str id))

// Pattern 2: Using fmt for complex formatting
// Task: Return "TOKEN-{userId}-{sequence}"
§R (fmt "TOKEN-{0}-{1}" userId sequence)

// Pattern 3: Zero-padded numbers using fmt with format specifier
// Task: Return "{prefix}-{sequence:D6}" (6-digit zero-padded)
§R (fmt "{0}-{1:D6}" prefix sequence)

// Pattern 4: Building report strings
// Task: Return "Report: {title} (Generated: {timestamp})"
§R (fmt "Report: {0} (Generated: {1})" title timestamp)
```

**CRITICAL: Do NOT invent string operations.** Use only the operations listed above.
- Do NOT use `ToString` - use `(str x)` instead
- Do NOT use `§CONCAT` or `§TOSTR` - these do not exist
- Do NOT use `String.Format` - use `(fmt ...)` instead

#### Array Operations

**IMPORTANT: Array syntax is different from C#. Do NOT use C#-style array syntax.**

```calor
// Array Types (use square brackets BEFORE the element type)
[i32]       // int[] - array of integers
[str]       // string[] - array of strings
[[i32]]     // int[][] - jagged array

// WRONG: C#-style type syntax
// i32[]      ❌ ERROR - don't put brackets after type
```

**Array Creation:**
```calor
// Sized array with literal
§B{[i32]:arr} §ARR{a001:i32:5}              // Create int[5]

// Sized array with variable
§B{[i32]:arr} §ARR{a001:i32:n}              // Create int[n]

// Sized array with expression
§B{[i32]:result} §ARR{a001:i32:(len data)}  // Create int[data.Length]

// Initialized array
§B{[i32]:arr} §ARR{a001:i32} 1 2 3 §/ARR{a001}  // Create [1, 2, 3]
```

**Array Element Access:**
```calor
// Access element at index - use §IDX (NO braces around array name)
§B{elem} §IDX arr 0                         // arr[0]
§B{elem} §IDX arr i                         // arr[i]
§B{last} §IDX arr §^ 1                      // arr[^1] - last element

// WRONG: These do NOT work
// §IDX{arr} i      ❌ ERROR - braces cause parsing issues
// (get arr i)      ❌ ERROR - not valid
// (at arr i)       ❌ ERROR - not valid
// arr[i]           ❌ ERROR - no C# indexer syntax
```

**Array Element Assignment (for mutable collections):**
```calor
// Set element - use §SETIDX
§SETIDX{arr} i newValue                     // arr[i] = newValue

// WRONG: This does NOT work
// §ASSIGN arr i value    ❌ ERROR - wrong syntax
```

**Array Length:**
```calor
// Get array length - use §LEN or (len)
§B{size} §LEN arr                           // arr.Length
§B{size} (len arr)                          // arr.Length (alternative)
```

**Complete Array Example:**
```calor
§F{f001:SumArray:pub}
  §I{[i32]:nums}
  §O{i32}
  §B{sum} 0
  §B{i} 0
  §WH{wh1} (< i (len nums))
    // IMPORTANT: Get element into a binding first, THEN use in expression
    §B{val} §IDX nums i
    §ASSIGN sum (+ sum val)
    §ASSIGN i (+ i 1)
  §/WH{wh1}
  §R sum
§/F{f001}
```

**CRITICAL: Don't mix tag-style (§IDX) inside Lisp expressions:**
```calor
// WRONG: §IDX inside Lisp expression
§ASSIGN sum (+ sum §IDX data i)     // ❌ ERROR - can't nest §IDX in (...)

// CORRECT: Use a binding first
§B{val} §IDX data i
§ASSIGN sum (+ sum val)             // ✓ Works - val is a simple identifier
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

**§IF as Expression (Conditional/Ternary):**

§IF can be used as an expression to return a value (like C#'s ternary `?:`):

```calor
// Syntax: §IF{id} condition → thenValue §EL → elseValue §/I{id}
§B{x} §IF{if1} (< n 0) → (- 0 n) §EL → n §/I{if1}
// Compiles to: var x = (n < 0) ? (0 - n) : n;

// Conditional assignment - minimum of two values
§B{minLen} §IF{if1} (<= lenA lenB) → lenA §EL → lenB §/I{if1}
// Compiles to: var minLen = (lenA <= lenB) ? lenA : lenB;
```

**IMPORTANT:** Both `→ thenValue` and `§EL → elseValue` are required for IF expressions.

**Alternative - Use explicit branches for complex logic:**
```calor
// For complex logic with multiple statements, use block syntax
§IF{if1} (< n 0)
  §B{x} (- 0 n)
§EL
  §B{x} n
§/I{if1}
// Now use x
```

**COMMON MISTAKE - Arrow syntax with statements after:**
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

9. **Use proper string operations**: For string manipulation, use `(str x)` for conversion, `(concat ...)` for joining, `(fmt ...)` for formatting. NEVER invent syntax like `ToString`, `§CONCAT`, or `§TOSTR`.

## String Operation Examples

These examples show correct patterns for common string tasks:

### Building Cache Keys
```calor
// Task: Return "{type}:{id}"
§M{m001:CacheModule}
§F{f001:BuildCacheKey:pub}
  §I{str:type}
  §I{i32:id}
  §O{str}
  §R (concat type ":" (str id))
§/F{f001}
§/M{m001}
```

### Generating Formatted Tokens
```calor
// Task: Return "TOKEN-{userId}-{sequence}"
§M{m001:TokenModule}
§F{f001:GenerateToken:pub}
  §I{i32:userId}
  §I{i32:sequence}
  §O{str}
  §R (fmt "TOKEN-{0}-{1}" userId sequence)
§/F{f001}
§/M{m001}
```

### Zero-Padded IDs
```calor
// Task: Return "{prefix}-{sequence:D6}" (6-digit zero-padded)
§M{m001:IdModule}
§F{f001:GenerateId:pub}
  §I{str:prefix}
  §I{i32:sequence}
  §O{str}
  §R (fmt "{0}-{1:D6}" prefix sequence)
§/F{f001}
§/M{m001}
```

### Report with Timestamp
```calor
// Task: Return "Report: {title} (Generated: {timestamp})"
§M{m001:ReportModule}
§F{f001:GenerateReport:pub}
  §I{str:title}
  §I{i64:timestamp}
  §O{str}
  §R (fmt "Report: {0} (Generated: {1})" title timestamp)
§/F{f001}
§/M{m001}
```

### Simple Integer Parsing (with default)
```calor
// Task: Parse string to int, return 0 if invalid
// Note: For complex parsing, keep it simple - use available operations
§M{m001:ParseModule}
§F{f001:ParseInt:pub}
  §I{str:s}
  §O{i32}
  §IF{if1} (isempty s) → §R 0
  §/I{if1}
  // For basic cases, return 0 as default for invalid input
  // Complex parsing with character iteration requires more elaborate logic
  §R 0
§/F{f001}
§/M{m001}
```

## Common Mistakes to Avoid

### String Operation Mistakes

**WRONG - Inventing syntax:**
```calor
// WRONG: ToString doesn't exist
§R (+ type ":" (ToString id))

// WRONG: §CONCAT doesn't exist
§R §CONCAT "TOKEN-" §TOSTR userId "-" §TOSTR sequence

// WRONG: §STR_LEN doesn't exist
§B{length} (§STR_LEN s)
```

**CORRECT - Using actual Calor syntax:**
```calor
// CORRECT: Use (str x) to convert to string
§R (concat type ":" (str id))

// CORRECT: Use (fmt ...) for formatted strings
§R (fmt "TOKEN-{0}-{1}" userId sequence)

// CORRECT: Use (len s) for string length
§B{length} (len s)
```

**WRONG - indexof with startIndex:**
```calor
// WRONG: indexof only takes 2 arguments
§B{next} (indexof s "@" (+ first 1))  // ❌ ERROR - 3 args not supported
```

**CORRECT - Use substr + indexof:**
```calor
// CORRECT: Search from offset using substr
§B{first} (indexof s "@")
§B{rest} (substr s (+ first 1))
§B{next} (indexof rest "@")           // ✓ Search in substring
```

### Character Literal Mistakes

**WRONG - Single-quoted character literals (cause "Unexpected character '''" error):**
```calor
// WRONG: Single quotes are NOT supported
§IF{if1} (== c '0')             // ❌ ERROR
§IF{if2} (== c '-')             // ❌ ERROR
§IF{if3} (&& (>= c '0') (<= c '9'))  // ❌ ERROR
```

**CORRECT - Use character predicates or code comparisons:**
```calor
// CORRECT: Use (is-digit c) for digit check
§IF{if1} (is-digit c)           // ✓ Check if digit

// CORRECT: Use (char-code c) with numeric values
§IF{if2} (== (char-code c) 45)  // ✓ Check if c == '-' (code 45)

// CORRECT: Use codes for range check
§B{code} (char-code c)
§IF{if3} (&& (>= code 48) (<= code 57))  // ✓ Check if '0'-'9'
```

### Array Syntax Mistakes

**WRONG - C#-style array syntax:**
```calor
// WRONG: Type with brackets after
§I{i32[]:items}                 // ❌ ERROR - wrong type syntax

// WRONG: C#-style element access
§B{x} (get arr i)               // ❌ ERROR - 'get' doesn't exist
§B{x} (at arr i)                // ❌ ERROR - 'at' doesn't exist
§B{x} arr[i]                    // ❌ ERROR - no C# indexer

// WRONG: Element assignment
§ASSIGN arr i value             // ❌ ERROR - can't assign like this

// WRONG: Array copy
§B{copy} (copy arr)             // ❌ ERROR - 'copy' doesn't exist
```

**CORRECT - Calor array syntax:**
```calor
// CORRECT: Type with brackets before
§I{[i32]:items}                 // ✓ Array of i32

// CORRECT: Element access with §IDX (NO braces)
§B{x} §IDX arr i                // ✓ arr[i]

// CORRECT: Element assignment with §SETIDX
§SETIDX{arr} i value            // ✓ arr[i] = value

// CORRECT: Array length
§B{n} (len arr)                 // ✓ arr.Length
§B{n} §LEN arr                  // ✓ arr.Length (alternative)
```

**WRONG - Mixing tag-style §IDX inside Lisp expressions:**
```calor
// WRONG: Can't nest §IDX inside (...)
§ASSIGN sum (+ sum §IDX data i)     // ❌ ERROR - tag inside Lisp expr

// WRONG: Can't use §IDX directly in operators
§R (^ hash §IDX arr j)              // ❌ ERROR - tag inside Lisp expr
```

**CORRECT - Use a binding first:**
```calor
// CORRECT: Extract to binding, then use in expression
§B{val} §IDX data i
§ASSIGN sum (+ sum val)             // ✓ val is a simple identifier

§B{elem} §IDX arr j
§R (^ hash elem)                    // ✓ elem is a simple identifier
```

### If-Expression Patterns (Ternary/Conditional)

**§IF as Expression - NOW SUPPORTED:**
```calor
// IF expression for conditional values (compiles to C# ternary)
§B{absX} §IF{if1} (< x 0) → (- 0 x) §EL → x §/I{if1}
// Compiles to: var absX = (x < 0) ? (0 - x) : x;

// IF expression in return
§R §IF{if1} (< a b) → (- 0 1) §EL → 1 §/I{if1}
// Compiles to: return (a < b) ? (0 - 1) : 1;
```

**Alternative - Block syntax for complex logic:**
```calor
// When you need multiple statements, use block syntax
§B{absX} 0
§IF{if1} (< x 0)
  §ASSIGN absX (- 0 x)
§EL
  §ASSIGN absX x
§/I{if1}

// Or arrow syntax with statements
§IF{if1} (< a b) → §R (- 0 1)
§EL → §R 1
§/I{if1}
```

### String Comparison Mistakes

**WRONG - Using < or > on strings:**
```calor
// WRONG: Relational operators don't work on strings
§IF{if1} (< a b) → §R (- 0 1)       // ❌ ERROR - can't compare strings with <
§IF{if2} (> a b) → §R 1             // ❌ ERROR - can't compare strings with >
```

**CORRECT - Use string operations or character codes:**
```calor
// CORRECT: Use (equals) for equality
§IF{if1} (equals a b) → §R 0
§/I{if1}

// For lexicographic comparison, compare character by character
// using (char-at) and (char-code)
```
