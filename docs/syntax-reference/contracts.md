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
```

---

## Contract Patterns

### Typed .NET calls in verified contracts

The verifier recognizes ordinary .NET string calls through the existing call AST.
For example, the current syntax accepts:

```
§Q §C{s.Contains} §A "b" §/C
§S (== result §C{s.StartsWith} §A "a" §A System.StringComparison.Ordinal §/C)
```

Receiver identities come from the binder. String overloads must resolve against
.NET metadata with exact parameter types. Supported calls are `s.Contains(value)`,
`s.Equals(value)`, `s.Substring(start[, count])`, `string.IsNullOrEmpty(s)`,
`string.Equals(a, b)`, and `string.Concat` with two to four string arguments.
`StartsWith`, `EndsWith` and `IndexOf` require an explicit
`StringComparison.Ordinal`; the ordinal overloads of `Contains` and `Equals`
are also supported. Culture-sensitive, named, by-reference, generic and other
overloads remain unverifiable, with runtime checks retained.

`s.Length`, integer-array `.Length` and metadata-resolvable `List<T>.Count` are
recognized through receiver types, including inside integer quantifiers. Their results are signed `int`
values bounded between zero and `Int32.MaxValue`. Lists use a count function
on their reference sort; list element access is not added. The legacy length
nodes retain their existing unsigned model.

These forms lower to existing verifier operations before the whitelist is
applied. String/reference assumptions, substring range conditions, result binding
and conditional-position refusals are unchanged. Lowering affects verification
only, not the executable AST, effects or evaluation order. This adds no grammar
for computed callees or indexed assignment.
The converter still emits legacy string built-ins during this staged migration;
recognizing ordinary calls in contracts does not remove that vocabulary.

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
```

### Non-Empty Collection

```
§F{f001:First:pub}
  §I{List:items}
  §O{?Item}
  §Q (> (count items) 0)                 // list not empty
  §S (!= result null)                    // result exists
  // ...
```

### State Preservation

```
§F{f001:Increment:pub}
  §I{i32:x}
  §O{i32}
  §S (== result (+ x 1))                 // result is exactly x + 1
  §R (+ x 1)
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
```

### Runtime vs Static Verification

Quantifiers with certified finite ranges generate runtime checks:
- `forall` uses `Calor.Runtime.ContractQuantifier.Range(...).All(...)`.
- `exists` uses `Calor.Runtime.ContractQuantifier.Range(...).Any(...)`.

A certified prefix is a leading sequence of primitive guards and bounds whose
reads are stable and whose evaluation order the compiler can preserve.
Potentially throwing bounds stay behind their guards. An empty partial domain
prevents evaluation of later bounds. Inner bounds may depend on already-bound
outer variables.

The remaining predicate keeps its original short-circuit structure and observable
evaluations. The compiler reports `Calor0326` when it cannot produce a faithful
finite runtime check—for example, when discovering a range would require moving
an unknown getter ahead of another condition. It does not silently replace that
check with a static-only comment.

Z3 can also analyze quantified formulas statically. A proof result does not make
an unsupported requested runtime lowering valid.

### Supported Variable Types

Quantifier bound variables should use integer types for finite range iteration:
- `i8`, `i16`, `i32`, `i64`
- `u8`, `u16`, `u32`, `u64`

The runtime enumerator preserves each listed type's domain, including its minimum
and maximum values. Other types are not supported by this finite-domain enumerator.

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
calor -i myprogram.calr -o myprogram.g.cs --contract-mode=debug

# Production
calor -i myprogram.calr -o myprogram.g.cs --contract-mode=release

# Disable (not recommended)
calor -i myprogram.calr -o myprogram.g.cs --contract-mode=off
```

There is no `calor compile` subcommand — compilation is the default command, taking
`--input`/`-i` and `--output`/`-o`. See [compile (default)](../cli/compile.md).

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
```

This function is pure (no `§E` declaration) even though the precondition might throw. The `throw` effect in `§E` is only for intentional `§TH` statements in business logic, not contract violations.

---

## See Also: Refinement Types

Contracts define what a function requires and guarantees. **Refinement types** extend this idea to the type level — instead of writing `§Q (>= x 0)` at the function, you declare `§I{i32:x} | (>= # INT:0)` on the parameter itself. The constraint becomes part of the type, and the obligation engine verifies it automatically.

- [Refinement Types](/calor/syntax-reference/refinement-types/) — Type-level constraints with `§RTYPE`, inline `|`, and `§PROOF`
- [Dependent Types Tutorial](/calor/guides/dependent-types-tutorial/) — Progressive walkthrough

---

## Next

- [Effects & Contracts Enforcement](/calor/philosophy/effects-contracts-enforcement/) - Why this matters
- [Effects](/calor/syntax-reference/effects/) - Declaring side effects
- [Enforcement Details](/calor/effects-and-contracts-enforcement/) - Technical specification
