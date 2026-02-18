# Calor Language Skills

You are writing code in **Calor**, a programming language designed for AI coding agents. Calor compiles to C# and emphasizes explicit contracts, effects, and unambiguous syntax.

## Core Philosophy

Calor is designed for:
- **Correctness**: First-class contracts (preconditions/postconditions) make requirements explicit
- **Clarity**: No operator precedence ambiguity - all expressions use prefix notation
- **Verifiability**: Constraints are machine-checkable, not just documentation

## Semantic Guarantees

Calor has formal semantics (v1.0.0) that differ from C#. **Do not assume C# behavior.**

| Rule | Calor Behavior |
|------|----------------|
| Evaluation Order | Strictly left-to-right for all expressions |
| Short-Circuit | `&&`/`||` always short-circuit |
| Scoping | Lexical with shadowing; inner scope does NOT mutate outer |
| Integer Overflow | TRAP by default (throws `OverflowException`) |
| Type Coercion | Explicit for narrowing; implicit only for widening |
| Contracts | `§Q` before body, `§S` after body |

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

### Null-Safety Pattern for Reference Types

**CRITICAL: When working with arrays or strings, always check for null BEFORE checking length or other properties.**

The `(len arr)` operation will throw `NullReferenceException` if `arr` is null. Contracts are evaluated in order, so place null checks first:

```calor
// WRONG - If arr is null, (len arr) throws NullReferenceException BEFORE the contract can fail
§Q (> (len arr) 0)              // ❌ Crashes on null input

// CORRECT - Check null first, then length
§Q (!= arr null)                // ✓ First: reject null
§Q (> (len arr) 0)              // ✓ Then: check length (safe because arr is not null)
```

**Common patterns for reference type validation:**

| Requirement | Contracts (in order) |
|-------------|---------------------|
| "array must not be null" | `§Q (!= arr null)` |
| "array must not be empty" | `§Q (!= arr null)` then `§Q (> (len arr) 0)` |
| "array must have at least N elements" | `§Q (!= arr null)` then `§Q (>= (len arr) N)` |
| "string must not be null or empty" | `§Q (!= s null)` then `§Q (> (len s) 0)` |
| "string must not be null" | `§Q (!= s null)` |

**Example - Array Max function:**
```calor
§F{f001:Max:pub}
  §I{[i32]:arr}
  §O{i32}
  §Q (!= arr null)              // First: array must not be null
  §Q (> (len arr) 0)            // Then: array must have elements
  §B{max} §IDX arr 0
  §B{i} 1
  §WH{wh1} (< i (len arr))
    §B{current} §IDX arr i
    §IF{if1} (> current max)
      §ASSIGN max current
    §/I{if1}
    §ASSIGN i (+ i 1)
  §/WH{wh1}
  §R max
§/F{f001}
```

**Why this matters:** Without the null check, passing `null` causes a runtime crash (`NullReferenceException`) instead of a clean contract violation (`ContractViolationException`). The null check ensures invalid inputs are rejected with a proper error message.

## Syntax Quick Reference

### Structure Tags

```
§M{id:Name}           Module (namespace)
§F{id:Name:vis}       Function (pub|pri)
§I{type:name}         Input parameter
§O{type}              Output/return type
§E{effects}           Side effects: cw,cr,fs:r,fs:w,net:rw,db:rw
§U{namespace}         Using directive
§/M{id} §/F{id}       Close tags (ID must match)
```

### Types

```
i32, i64, f32, f64    Numbers
u8, u16, u32, u64     Unsigned integers
str, bool, void       String, boolean, unit
?T                    Option<T> (nullable)
T!E                   Result<T,E> (fallible)
[T]                   Array of T (e.g., [u8], [i32], [str])
[[T]]                 Nested array (e.g., [[i32]] for int[][])

datetime              DateTime
datetimeoffset        DateTimeOffset
timespan              TimeSpan
date                  DateOnly
time                  TimeOnly
guid                  Guid

List<T>               List<T>
Dict<K,V>             Dictionary<K,V>
ReadList<T>           IReadOnlyList<T>
ReadDict<K,V>         IReadOnlyDictionary<K,V>
```

### Numeric Literals and Constants

**IMPORTANT: For extreme integer values, use typed literals:**
```calor
INT:-2147483648    // int.MinValue (-2^31)
INT:2147483647     // int.MaxValue (2^31 - 1)
```

**WRONG - Don't try to construct MinValue with arithmetic:**
```calor
(- 0 2147483648)   // ❌ ERROR - 2147483648 exceeds i32 range
```

**CORRECT - Use typed literal directly:**
```calor
§R INT:-2147483648  // ✓ Returns int.MinValue
```

### Expressions (Prefix Notation)

```calor
(+ a b)       // a + b (also string concatenation)
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

### Unavailable Operators - Use IF Expressions Instead

**CRITICAL: The following operators do NOT exist in Calor:**
- `(abs x)` - absolute value
- `(max a b)` - maximum
- `(min a b)` - minimum
- `(sqrt x)` - square root
- `(pow a b)` - power

**Use IF expressions to implement these:**
```calor
// Absolute value - NO (abs x) operator!
§B{absVal} §IF{if1} (< x 0) → (- 0 x) §EL → x §/I{if1}

// Maximum - NO (max a b) operator!
§B{maxVal} §IF{if1} (> a b) → a §EL → b §/I{if1}

// Minimum - NO (min a b) operator!
§B{minVal} §IF{if1} (< a b) → a §EL → b §/I{if1}
```

### String Operations

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

### Character Operations

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
| `'='` | 61 | `'+'` | 43 |
| `'/'` | 47 | | |

**Getting character constants (without single quotes):**
```calor
// Use char-from-code to create character values
§B{equalChar} (char-from-code 61)    // '='
§B{plusChar} (char-from-code 43)     // '+'

// Or extract from a string
§B{equalChar} (char-at "=" 0)        // '='
```

### Array Operations

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
§B{last} §IDX arr (- (len arr) 1)           // arr[arr.Length - 1] - last element

// WRONG: These do NOT work
// §IDX{arr} i      ❌ ERROR - braces cause parsing issues
// §IDX arr §^ 1    ❌ ERROR - §^ syntax not supported
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

**CRITICAL: Don't mix tag-style (§IDX) inside Lisp expressions or contracts:**
```calor
// WRONG: §IDX inside Lisp expression
§ASSIGN sum (+ sum §IDX data i)     // ❌ ERROR - can't nest §IDX in (...)

// WRONG: §IDX inside contract
§S (== result §IDX arr 0)           // ❌ ERROR - can't nest §IDX in contracts

// CORRECT: Use a binding first
§B{val} §IDX data i
§ASSIGN sum (+ sum val)             // ✓ Works - val is a simple identifier

// CORRECT: For contracts referencing array elements, use simple postconditions
§S (>= result 0)                    // ✓ Simple value constraint
```

### Control Flow

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

**Do-While Loop:**
```calor
§DO{id}
  ...body (executes at least once)...
§/DO{id} condition
```

**Break and Continue:**
```calor
§BK                       // Break out of loop
§CN                       // Continue to next iteration
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

### Bindings and Assignment

```calor
§B{varName} expression    // Create binding: varName = expression
§B{type:varName} expr     // Create binding with explicit type
§ASSIGN varName expr      // Update existing binding
```

**CRITICAL: §B declares a NEW variable. Use §ASSIGN to update existing variables.**
```calor
§B{k} (% rng n)              // First use: declare k
§ASSIGN k (+ k offset)       // Update: use §ASSIGN, not §B

// WRONG - variable redeclaration error:
§B{k} (% rng n)
§B{k} (abs k)                // ERROR: k already defined in this scope
```

### Return

```calor
§R expression             // Return the expression's value
```

### Statements

```calor
§P expr               // Print line (Console.WriteLine)
§Pf expr              // Print without newline (Console.Write)
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

## String Operation Examples

These examples show correct patterns for common string tasks:

### Building Cache Keys

Task: Return `"{type}:{id}"`

```calor
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

Task: Return `"TOKEN-{userId}-{sequence}"`

```calor
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

Task: Return `"{prefix}-{sequence:D6}"` (6-digit zero-padded)

```calor
§M{m001:IdModule}
§F{f001:GenerateId:pub}
  §I{str:prefix}
  §I{i32:sequence}
  §O{str}
  §R (fmt "{0}-{1:D6}" prefix sequence)
§/F{f001}
§/M{m001}
```

## Collections

### List

```calor
§LIST{name:elementType}   // Create and initialize a list
  value1
  value2
§/LIST{name}

§PUSH{listName} value     // Add to end of list
§INS{listName} index val  // Insert at index
§SETIDX{listName} idx val // Set element at index
§REM{listName} value      // Remove first occurrence
§CLR{listName}            // Clear all elements
§CNT{listName}            // Get count
§HAS{listName} value      // Check if contains
```

### Dictionary

```calor
§DICT{name:keyType:valType}  // Create dictionary
  §KV key1 value1
  §KV key2 value2
§/DICT{name}

§PUT{dictName} key value     // Add or update entry
§REM{dictName} key           // Remove by key
§HAS{dictName} key           // Check if key exists
§CLR{dictName}               // Clear all entries
```

### HashSet

```calor
§HSET{name:elementType}   // Create hash set
  value1
  value2
§/HSET{name}

§PUSH{setName} value      // Add to set
§REM{setName} value       // Remove from set
§HAS{setName} value       // Check membership
§CLR{setName}             // Clear all elements
```

### Iterating Collections

```calor
§EACH{id:var} collection      // Foreach (type inferred)
§EACH{id:var:type} collection  // Foreach (explicit type)
  ...body...
§/EACH{id}

§EACHKV{id:k:v} dict      // Foreach over dictionary key-values
  ...body...
§/EACHKV{id}
```

## Async/Await

Async functions and methods use `§AF` and `§AMT` tags:

```calor
§AF{id:Name:vis}          // Async function (returns Task<T>)
§/AF{id}                  // End async function
§AMT{id:Name:vis}         // Async method (returns Task<T>)
§/AMT{id}                 // End async method
§AWAIT expr               // Await an async operation
§AWAIT{false} expr        // Await with ConfigureAwait(false)
```

### Template: Async Function

```calor
§M{m001:AsyncDemo}
§AF{f001:FetchDataAsync:pub}
  §I{str:url}
  §O{str}
  §B{str:result} §AWAIT §C{client.GetStringAsync} §A url §/C
  §R result
§/AF{f001}
§/M{m001}
```

## Exception Handling

```calor
§TR{id}                   // Try block
  ...try body...
§CA{ExceptionType:varName} // Catch clause
  ...catch body...
§CA                       // Catch-all (no type)
  ...catch body...
§FI                       // Finally block
  ...finally body...
§/TR{id}                  // End try block

§TH "message"             // Throw new Exception
§TH expr                  // Throw expression
§RT                       // Rethrow (inside catch)

§CA{Type:var} §WHEN cond  // Exception filter
```

### Template: Try/Catch/Finally

```calor
§M{m001:ErrorHandling}
§F{f001:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §TR{t1}
    §R (/ a b)
  §CA{DivideByZeroException:ex}
    §P "Division by zero!"
    §R 0
  §FI
    §P "Cleanup complete"
  §/TR{t1}
§/F{f001}
§/M{m001}
```

## Generics

### Generic Functions and Classes

Type parameters use `<T>` suffix syntax after tag attributes:

```calor
§F{id:Name:pub}<T>            // Generic function with one type param
§F{id:Name:pub}<T, U>         // Generic function with two type params
§CL{id:Name}<T>               // Generic class
§IFACE{id:Name}<T>            // Generic interface
§MT{id:Name:vis}<T>           // Generic method
```

### Type Constraints (§WHERE)

```calor
§WHERE T : class              // Reference type constraint
§WHERE T : struct             // Value type constraint
§WHERE T : new()              // Parameterless constructor
§WHERE T : IComparable<T>     // Interface constraint
§WHERE T : class, IDisposable // Multiple constraints
```

### Template: Generic Repository

```calor
§M{m001:GenericDemo}
§CL{c001:Repository:pub}<T>
  §WHERE T : class
  §FLD{List<T>:_items:pri}

  §CTOR{ct001:pub}
    §ASSIGN _items §NEW{List<T>}
  §/CTOR{ct001}

  §MT{m001:Add:pub}
    §I{T:item}
    §O{void}
    §C{_items.Add} §A item §/C
  §/MT{m001}

  §MT{m002:GetAll:pub}
    §O{IReadOnlyList<T>}
    §R _items
  §/MT{m002}
§/CL{c001}
§/M{m001}
```

## Classes and Interfaces

### Class Definition

```calor
§CL{id:Name:modifiers}    // Class definition
  // modifiers: abs, seal, pub, pri
§EXT{BaseClass}           // Extends (class inheritance)
§IMPL{InterfaceName}      // Implements (interface)
```

### Interface Definition

```calor
§IFACE{id:Name}           // Interface definition
  §MT{id:MethodName}      // Method signature
    §I{type:param}        // Parameters
    §O{returnType}        // Return type
  §/MT{id}
§/IFACE{id}
```

### Method Modifiers

```calor
§VR                       // Virtual method modifier
§OV                       // Override method modifier
§AB                       // Abstract method modifier
§SD                       // Sealed method modifier
```

### Template: Class Inheritance

```calor
§M{m001:Shapes}
§CL{c001:Shape:pub abs}
  §MT{mt001:Area:pub:abs}
    §O{f64}
  §/MT{mt001}
§/CL{c001}

§CL{c002:Circle:pub}
  §EXT{Shape}
  §FLD{f64:radius:pri}
  §MT{mt001:Area:pub:over}
    §O{f64}
    §R (* 3.14159 (* radius radius))
  §/MT{mt001}
§/CL{c002}
§/M{m001}
```

## Constructors

```calor
§CTOR{id:visibility}      // Constructor
  §I{type:param}          // Parameters
  §BASE §A arg §/BASE     // Call base constructor
  §THIS §A arg §/THIS     // Call this constructor
  §ASSIGN target value    // Field assignment
§/CTOR{id}
```

### Template: Constructor with Base Call

```calor
§M{m001:Animals}
§CL{c001:Animal:pub}
  §FLD{str:_name:pro}
  §CTOR{ctor001:pub}
    §I{str:name}
    §ASSIGN §THIS._name name
  §/CTOR{ctor001}
§/CL{c001}

§CL{c002:Dog:pub}
  §EXT{Animal}
  §FLD{str:_breed:pri}
  §CTOR{ctor001:pub}
    §I{str:name}
    §I{str:breed}
    §BASE §A name §/BASE
    §ASSIGN §THIS._breed breed
  §/CTOR{ctor001}
§/CL{c002}
§/M{m001}
```

## Switch/Match Expressions

```calor
§W{id} target             // Match expression start
§K pattern → expr         // Case with arrow syntax
§K pattern                // Case with block body
  ...body...
§/K                       // End case
§/W{id}                   // End match
```

### Template: Switch Expression

```calor
§M{m001:Grades}
§F{f001:GetGrade:pub}
  §I{i32:score}
  §O{str}
  §R §W{sw1} score
    §K §PREL{gte} 90 → "A"
    §K §PREL{gte} 80 → "B"
    §K §PREL{gte} 70 → "C"
    §K §PREL{gte} 60 → "D"
    §K _ → "F"
  §/W{sw1}
§/F{f001}
§/M{m001}
```

### Guard Clauses with WHEN

```calor
§W{sw1} x
  §K §VAR{n} §WHEN (> n 100) → "large"
  §K §VAR{n} §WHEN (< n 0) → "negative"
  §K 0 → "zero"
  §K _ → "normal"
§/W{sw1}
```

## Enums

```calor
§EN{id:Name}              // Simple enum
  Red
  Green
  Blue
§/EN{id}

§EN{id:Name:underlyingType}  // Enum with underlying type
  Ok = 200
  NotFound = 404
  Error = 500
§/EN{id}
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

## Common Mistakes to Avoid

### Null-Check Ordering Mistakes (CRITICAL)

**WRONG - Checking length without null check first:**
```calor
// WRONG: If arr is null, this throws NullReferenceException, not ContractViolationException
§F{f001:Max:pub}
  §I{[i32]:arr}
  §O{i32}
  §Q (> (len arr) 0)            // ❌ Crashes if arr is null!
  // ...
§/F{f001}
```

**CORRECT - Always check null BEFORE length:**
```calor
// CORRECT: Null check first, then length check
§F{f001:Max:pub}
  §I{[i32]:arr}
  §O{i32}
  §Q (!= arr null)              // ✓ First: reject null
  §Q (> (len arr) 0)            // ✓ Then: check length (safe now)
  // ...
§/F{f001}
```

**Why this matters:** Contracts are evaluated in order. If you call `(len arr)` when `arr` is null, you get a crash instead of a proper contract violation. Always guard reference types with `(!= x null)` before accessing their properties.

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

### Variable Redeclaration Mistakes

**WRONG - Using §B to update an existing variable:**
```calor
// WRONG: Can't redeclare a variable in the same scope
§B{k} (% rng n)                 // First use: declares k
§B{k} (+ k 1)                   // ❌ ERROR - 'k' already defined

// WRONG: Redeclaring in conditional
§B{result} 0
§IF{if1} (< n 0)
  §B{result} (- 0 n)            // ❌ ERROR - 'result' already exists
§/I{if1}
```

**CORRECT - Use §ASSIGN to update existing variables:**
```calor
// CORRECT: §B to declare, §ASSIGN to update
§B{k} (% rng n)                 // Declare k
§ASSIGN k (+ k 1)               // ✓ Update k

// CORRECT: Update in conditional
§B{result} 0
§IF{if1} (< n 0)
  §ASSIGN result (- 0 n)        // ✓ Update existing result
§/I{if1}
```

### Character Literal Mistakes

**WRONG - Single-quoted character literals (cause "Unexpected character '''" error):**
```calor
// WRONG: Single quotes are NOT supported
§IF{if1} (== c '0')             // ❌ ERROR
§IF{if2} (== c '-')             // ❌ ERROR
§IF{if3} (&& (>= c '0') (<= c '9'))  // ❌ ERROR
§B{pad} '='                     // ❌ ERROR - can't use single quotes
```

**CORRECT - Use character predicates, codes, or string extraction:**
```calor
// CORRECT: Use (is-digit c) for digit check
§IF{if1} (is-digit c)           // ✓ Check if digit

// CORRECT: Use (char-code c) with numeric values
§IF{if2} (== (char-code c) 45)  // ✓ Check if c == '-' (code 45)

// CORRECT: Use codes for range check
§B{code} (char-code c)
§IF{if3} (&& (>= code 48) (<= code 57))  // ✓ Check if '0'-'9'

// CORRECT: Get character constant using char-from-code
§B{equalChar} (char-from-code 61)   // ✓ '=' character (code 61)
§B{plusChar} (char-from-code 43)    // ✓ '+' character (code 43)

// CORRECT: Get character from string
§B{equalChar} (char-at "=" 0)       // ✓ '=' from string
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

### String Comparison Mistakes

**WRONG - Using < or > on strings:**
```calor
// WRONG: Relational operators don't work on strings
§IF{if1} (< a b) → §R (- 0 1)       // ❌ ERROR - can't compare strings with <
§IF{if2} (> a b) → §R 1             // ❌ ERROR - can't compare strings with >
```

**CORRECT - Use string operations or character codes:**
```calor
§IF{if1} (equals a b) → §R 0
§/I{if1}
```

For lexicographic comparison, compare character by character using `(char-at)` and `(char-code)`.

## Records and Union Types

```
§D{Name} (type:field, ...)   Record (data class)
§/D{Name}                    End record (if block form)

§T{Name}                     Type/Union definition
  §V{VariantName}            Variant case
  §V{VariantName} (fields)   Variant with data
§/T{Name}                    End type
```

### Template: Result Type

```calor
§T{Result}
  §V{Ok} (i32:value)
  §V{Err} (str:message)
§/T{Result}
```

## Properties

```
§PROP{id:Name:type:vis}   Property definition
  §GET{vis}               Getter
    §R expression
  §/GET
  §SET                    Setter
    §ASSIGN _field value
  §/SET
  §INIT                   Init-only setter (C# 9+)
§/PROP{id}

§FLD{type:name:vis}       Field definition
§DEFAULT                  Default value expression
```

## Lambdas and Delegates

### Delegate Definition

```
§DEL{id:Name:vis}         Delegate type declaration
  §I{type:param}
  §O{returnType}
§/DEL{id}
```

### Lambda Expressions

```
§LAM{id:param:type}       Single-param lambda
§LAM{id:p1:t1:p2:t2}      Multi-param lambda
  ...body...
§/LAM{id}                 Closing tag required
```

## Events

```
§EVT{id:Name:vis:DelegateType}  Event declaration
§SUB{target.Event} handler      Subscribe (+=)
§UNSUB{target.Event} handler    Unsubscribe (-=)
```

## String Interpolation

```
§INTERP                   Start interpolated string
  "text {expr} more text"
§/INTERP                  End interpolation
```

## Modern Operators

```
§?? left right            Null coalescing: left ?? right
§?. target member         Null conditional: target?.member
§RANGE start end          Range: start..end
§^ n                      Index from end: ^n
§EXP expr                 Expression (for complex expressions)
§DEFAULT                  Default value (default(T))
```

### With Expression (Records)

```
§WITH{source}             Create copy with modifications
  PropertyName = newValue
§/WITH
```

## Option/Result Types

```
§SM value             Some(value)
§NN{type=T}           None of type T
§OK value             Ok(value)
§ERR "message"        Err(message)
```

## Switch/Match (Additional)

Alternative alias `§SW` is available for `§W`:

```
§SW{id} target            Same as §W{id} target
§/SW{id}                  Same as §/W{id}
```

## Pattern Matching Enhancements

### Relational Patterns

```
§PREL{op} value           Relational pattern
  op: gte, lte, gt, lt    Maps to >=, <=, >, <
```

### Variable Pattern

```
§VAR x                    Captures value into variable x
```

### Property Pattern

```
§PPROP{Type}              Match type with properties
  PropertyName = pattern
§/PPROP
```

### Positional Pattern

```
§PPOS{Type}               Match type with positional values
  pattern1
  pattern2
§/PPOS
```

### List Pattern

```
§PLIST                    Match list structure
  pattern1
  pattern2
  §REST                   Rest of list (..)
§/PLIST
```

### Property Match Pattern

```
§PMATCH{property} pattern    Match property value inline
```

## Explicit Body Markers

```
§BODY                 Start function body (optional)
...statements...
§END_BODY             End function body (optional)
```

## Enum Extensions

```
§ENUM{id:Name}            Enum (legacy alias for §EN)
§/ENUM{id}                End enum (legacy)

§EEXT{id:EnumName}        Extension methods for enum
  §F{f001:MethodName:pub}
    §I{EnumType:self}     First param becomes 'this'
    §O{returnType}
  §/F{f001}
§/EEXT{id}
```

## Extended Features: Metadata

### Issue Tracking

```
§TD "description"            Todo marker
§TD{id:category:priority}    Todo with metadata
§FX "description"            Fixme marker (bug to fix)
§FX{id:category:priority}    Fixme with metadata
§HK "description"            Hack marker (workaround)
§HK{id} "description"        Hack with id
```

### Inline Examples

```
§EX expr → expected          Inline example/test
§EX{id:msg:"desc"} expr → expected   Example with metadata
```

### Dependencies

```
§US{target1, target2}        Uses (dependencies)
§/US                         End uses block
§UB{caller1, caller2}        Used by (reverse dependencies)
§/UB                         End used by block
```

### Versioning and Stability

```
§SN{1.0.0}                   Since (version introduced)
§DP{since:1.0.0}             Deprecated marker
§BR{1.5.0} "description"     Breaking change marker
§XP                          Experimental (unstable API)
§SB                          Stable (API guaranteed stable)
```

### Complexity Contracts

```
§CX{time:O(n)}               Time complexity
§CX{time:O(n):space:O(1)}    Time and space complexity
```

### Context and Decisions

```
§CT                          Context section start
§/CT                         End context

§DC{id}                      Decision record
  §CHOSEN option             Chosen option
  §REJECTED option           Rejected alternative
  §REASON "explanation"      Rationale
§/DC{id}                     End decision
```

### Visibility Sections

```
§VS                          Visible section (for agents/users)
§/VS                         End visible

§HD                          Hidden section (implementation details)
§/HD                         End hidden

§FC target                   Focus marker (highlight importance)
```

### Agent Authorship

```
§AU{agent:agent-id}          Author marker
§TASK{PROJ-123} "description"  Task reference
§DATE{2024-01-15}            Date marker
§LK{agent:id:expires:time}   Lock (multi-agent editing)
```

### Property Testing

```
§PT predicate                Property test
```

### File References

```
§FILE{path/to/file.cs}       Reference external file
```

### Field and Invariant

```
§FL{type:name}               Field definition (in types)
§IV condition                Invariant (must always hold)
§IV{msg:"error"} condition   Invariant with message
```

### Collection Access

```
§KEY expr                    Get dictionary key
§VAL expr                    Get dictionary value
```

### Type Casting

```
§AS{targetType} expr      Safe cast (as operator)
§CAST{targetType} expr    Explicit cast
```

### Where Clause (Legacy)

```
§WR T : constraint           Where clause (legacy, prefer §WHERE)
```

### Async Modifier

```
§ASYNC                    Async modifier (standalone)
```

### Adding to Collections

```
§ADD{collection} item     Add item to collection
```
