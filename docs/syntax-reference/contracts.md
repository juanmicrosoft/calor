---
layout: default
title: Contracts
parent: Syntax Reference
nav_order: 5
---

# Contracts

Contracts are first-class citizens in Calor. They define what a function requires (preconditions) and guarantees (postconditions).

---

## Preconditions (`§Q`)

Preconditions specify what must be true when calling a function.

### Syntax

```
§Q condition
§Q{message="error text"} condition
```

### Examples

```
§Q (>= x 0)                              // x must be non-negative
§Q (!= divisor 0)                        // divisor must not be zero
§Q{message="Age must be positive"} (> age 0)
```

### Multiple Preconditions

Functions can have multiple preconditions:

```
§F{f001:CreateUser:pub}
  §I{str:name}
  §I{i32:age}
  §O{User}
  §Q (!= name "")                        // name not empty
  §Q (> (len name) 2)                    // name at least 3 chars
  §Q (>= age 0)                          // age non-negative
  §Q (<= age 150)                        // age reasonable
  // ...
§/F{f001}
```

---

## Postconditions (`§S`)

Postconditions specify what the function guarantees when it returns.

### Syntax

```
§S condition
§S{message="error text"} condition
```

### The `result` Variable

In postconditions, `result` refers to the return value:

```
§S (>= result 0)                         // result is non-negative
§S (!= result null)                      // result is not null
§S (== result (* x x))                   // result equals x squared
```

### Examples

```
§F{f001:Abs:pub}
  §I{i32:x}
  §O{i32}
  §S (>= result 0)                       // absolute value is non-negative
  §IF{if1} (>= x 0) → §R x
  §EL → §R (- 0 x)
  §/I{if1}
§/F{f001}
```

---

## Complete Contract Example

```
§F{f001:Divide:pub}
  §I{i32:dividend}
  §I{i32:divisor}
  §O{i32}

  // Preconditions
  §Q (!= divisor 0)                      // can't divide by zero
  §Q{message="Dividend must be non-negative"} (>= dividend 0)

  // Postconditions
  §S (>= result 0)                       // result is non-negative
  §S (<= result dividend)                // result doesn't exceed dividend

  §R (/ dividend divisor)
§/F{f001}
```

---

## Contract Patterns

### Range Validation

```
§F{f001:Clamp:pub}
  §I{i32:value}
  §I{i32:min}
  §I{i32:max}
  §O{i32}
  §Q (<= min max)                        // valid range
  §S (>= result min)                     // result at least min
  §S (<= result max)                     // result at most max
  // ...
§/F{f001}
```

### Non-Empty Collection

```
§F{f001:First:pub}
  §I{List:items}
  §O{?Item}
  §Q (> (count items) 0)                 // list not empty
  §S (!= result null)                    // result exists
  // ...
§/F{f001}
```

### State Preservation

```
§F{f001:Increment:pub}
  §I{i32:x}
  §O{i32}
  §S (== result (+ x 1))                 // result is exactly x + 1
  §R (+ x 1)
§/F{f001}
```

### Balance Transfers

```
§F{f001:Transfer:pub}
  §I{Account:from}
  §I{Account:to}
  §I{i32:amount}
  §O{void}
  §E{db:rw}
  §Q (> amount 0)                        // positive amount
  §Q (>= from.balance amount)            // sufficient funds
  // Balance conservation would be expressed if Calor supported old_ values
  // ...
§/F{f001}
```

---

## Quantified Contracts

Calor supports universal (`forall`) and existential (`exists`) quantifiers for expressing properties about collections and ranges.

### Universal Quantification (`forall`)

Express that a property holds for all elements in a range:

```
§Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (>= arr{i} INT:0)))
```

This reads: "For all integers i, if i is in range [0, n), then arr[i] is non-negative."

**Syntax:**
```
(forall ((var type)) body)
(forall ((var1 type1) (var2 type2)) body)  // Multiple variables
```

### Existential Quantification (`exists`)

Express that at least one element satisfies a property:

```
§Q (exists ((i i32)) (&& (>= i INT:0) (< i n) (== arr{i} target)))
```

This reads: "There exists an integer i in range [0, n) where arr[i] equals target."

**Syntax:**
```
(exists ((var type)) body)
```

### Implication (`->`)

Use implication to guard quantified expressions:

```
§Q (-> (> n INT:0) (exists ((i i32)) (&& (>= i INT:0) (< i n) (> arr{i} INT:0))))
```

This reads: "If n > 0, then there exists a positive element in the array."

**Syntax:**
```
(-> antecedent consequent)
```

Implication `p -> q` is equivalent to `!p || q`.

### Complete Examples

#### All Array Elements Non-Negative

```
§F{f001:ProcessArray:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (>= arr{i} INT:0)))
  §R true
§/F{f001}
```

#### Array Contains Target (Search Precondition)

```
§F{f001:BinarySearch:pub}
  §I{i32:n}
  §I{i32:target}
  §O{i32}
  §Q (exists ((i i32)) (&& (>= i INT:0) (< i n) (== arr{i} target)))
  §S (>= result INT:0)
  §S (< result n)
  // ...
§/F{f001}
```

#### Matrix Symmetry (Multiple Variables)

```
§F{f001:CheckSymmetric:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((i i32) (j i32))
       (-> (&& (>= i INT:0) (< i n) (>= j INT:0) (< j n))
           (== matrix{i}{j} matrix{j}{i})))
  §R true
§/F{f001}
```

### Runtime vs Static Verification

Quantifiers with finite, bounded ranges generate runtime checks using LINQ:
- `forall` becomes `Enumerable.Range(...).All(...)`
- `exists` becomes `Enumerable.Range(...).Any(...)`

Quantifiers over infinite ranges are verified statically using the Z3 SMT solver and generate a comment in the output:
```csharp
// STATIC ONLY: forall ((i: i32)) ...
```

### Supported Variable Types

Quantifier bound variables should use integer types for finite range iteration:
- `i8`, `i16`, `i32`, `i64`
- `u8`, `u16`, `u32`, `u64`

Non-integer types will generate a warning, as they may not support finite range enumeration.

### Performance Considerations

Nested quantifiers with multiple bound variables result in O(n^k) runtime checks where k is the number of variables. For example:
- Single variable: O(n)
- Two variables: O(n*m)
- Three variables: O(n*m*p)

The verifier emits an informational diagnostic for nested quantifiers to help identify potential performance issues.

---

## Why First-Class Contracts?

### 1. For Agent Comprehension

An agent can understand function behavior without reading implementation:

```
§F{f001:SquareRoot:pub}
  §I{f64:x}
  §O{f64}
  §Q (>= x 0)                            // Only works for non-negative
  §S (>= result 0)                       // Result is non-negative
  §S (<= (- (* result result) x) 0.0001) // result² ≈ x
  // ...
§/F{f001}
```

### 2. For Bug Detection

Contracts surface violations at call sites:

```
// If an agent sees this call:
§C{SquareRoot} §A -5 §/C

// It can immediately flag: violates §Q (>= x 0)
```

### 3. For Test Generation

Contracts provide test oracle:

- Generate inputs satisfying preconditions
- Verify outputs satisfy postconditions

---

## Custom Error Messages

Add context to failures:

```
§Q{message="User ID must be positive"} (> userId 0)
§Q{message="Email cannot be empty"} (!= email "")
§S{message="Password hash must be 64 chars"} (== (len hash) 64)
```

---

## Contract Ordering

Recommended order in function definition:

```
§F{id:name:vis}
  §I{...}           // 1. Inputs
  §O{...}           // 2. Output
  §E{...}           // 3. Effects (optional)
  §Q ...            // 4. Preconditions (0 or more)
  §S ...            // 5. Postconditions (0 or more)
  // body           // 6. Implementation
§/F{id}
```

---

## Runtime Contract Enforcement

Contracts aren't just documentation - they're **executable verification**.

### ContractViolationException

When a contract fails at runtime, Calor throws `ContractViolationException` with rich diagnostic information:

```csharp
public class ContractViolationException : Exception
{
    public string FunctionId { get; }         // e.g., "f001"
    public ContractKind Kind { get; }         // Requires, Ensures, or Invariant
    public int StartOffset { get; }           // Source position
    public int Length { get; }
    public string? SourceFile { get; }
    public int Line { get; }
    public int Column { get; }
}
```

This enables:
- **Precise debugging**: Know exactly which contract failed and where
- **Agent feedback**: Function ID allows agents to locate and fix issues
- **Production monitoring**: Structured exceptions for observability

### Contract Modes

Control how contracts are emitted with `--contract-mode`:

| Mode | Behavior | Use Case |
|:-----|:---------|:---------|
| `debug` (default) | Full diagnostics: condition text, source location | Development |
| `release` | Minimal: exception type + function ID only | Production |
| `off` | No contract checks emitted | Performance-critical paths |

```bash
# Development (default)
calor compile myprogram.calr --contract-mode=debug

# Production
calor compile myprogram.calr --contract-mode=release

# Disable (not recommended)
calor compile myprogram.calr --contract-mode=off
```

### Why Runtime Enforcement Works

In traditional languages, contract libraries exist (Code Contracts for .NET, JSR-303 for Java), but they're:
- **Optional**: Developers skip them under time pressure
- **Verbose**: Annotation overhead discourages use
- **Unmaintained**: Contracts rot as code evolves

Calor solves this because agents:
- Generate contracts automatically (no overhead)
- Maintain contract-implementation consistency
- Never skip verification for convenience

[Learn more: Effects & Contracts Enforcement](/calor/philosophy/effects-contracts-enforcement/)

---

## Contracts Don't Require Effect Declarations

A common question: Do contracts count as effects?

**No.** Contract checks are verification mechanisms, not operational effects:

```
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)       // No §E needed
  §R (/ a b)
§/F{f001}
```

This function is pure (no `§E` declaration) even though the precondition might throw. The `throw` effect in `§E` is only for intentional `§TH` statements in business logic, not contract violations.

---

## Next

- [Effects & Contracts Enforcement](/calor/philosophy/effects-contracts-enforcement/) - Why this matters
- [Effects](/calor/syntax-reference/effects/) - Declaring side effects
- [Enforcement Details](/calor/effects-and-contracts-enforcement/) - Technical specification
