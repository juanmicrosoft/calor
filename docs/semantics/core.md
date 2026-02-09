# Calor Core Semantics Specification

Version: 1.0.0

This document defines the formal semantics of the Calor programming language. These semantics are **backend-independent** - any backend (including the .NET backend) must conform to these rules.

---

## 1. Design Principles

### 1.1 Why "Emits C#" Is Not Enough

> **Problem:** An agent-friendly language needs a spec that is tighter than "it emits C#."

Emitting C# can hide semantic gaps:

| What You Write | What You Need to Know |
|----------------|----------------------|
| `f(a(), b())` | Is `a()` guaranteed to be called before `b()`? |
| `x + y` | What happens if the result overflows? |
| `inner.x = 1` | Does this shadow or mutate the outer `x`? |
| `return` in `if` | Does this return from the function or just the block? |

If these answers depend on C# implementation details, agents cannot reliably reason about their code. **If the semantics are not crisp, you get "works on this compiler version" behavior, and that kills trust.**

### 1.2 Backend Independence

> **Key Principle:** The semantics of Calor are defined independently of any backend. The C# emitter must conform to Calor semantics, not define them.

The backend must:
- Enforce evaluation order through code generation
- Never rely on unspecified backend behavior
- Generate explicit temporaries when needed to preserve semantics

This means agents can be trained on Calor semantics and trust that any conforming backend will produce correct behavior.

### 1.3 Safety First

Calor prioritizes safety and predictability:
- Overflow traps by default (no silent wraparound bugs)
- Explicit type conversions for narrowing (no accidental data loss)
- Contracts are first-class semantic constructs (not just comments)
- Effects are tracked and enforced (side effects are visible)

### 1.4 Agent Trainability

Every semantic rule in this document:
- Has a precise, unambiguous definition
- Has corresponding test cases in `tests/Calor.Semantics.Tests/`
- Is version-controlled (see `versioning.md`)
- Is independent of backend implementation details

Agents trained on these rules can generate correct code across compiler versions.

---

## 2. Evaluation Order

### 2.1 Left-to-Right Evaluation

All expressions with multiple sub-expressions are evaluated **strictly left-to-right**.

#### 2.1.1 Function Arguments

```calor
f(a(), b(), c())
```

**Semantics:** Evaluate `a()`, then `b()`, then `c()`, then call `f`.

**Rationale:** Predictable side-effect ordering enables reasoning about code behavior.

**Test Reference:** `S1: FunctionArguments_EvaluatedLeftToRight`

#### 2.1.2 Binary Operators

```calor
a() + b() + c()
```

**Semantics:** Evaluate `a()`, then `b()`, compute `a() + b()`, then evaluate `c()`, compute result.

**Test Reference:** `S2: BinaryOperators_EvaluatedLeftToRight`

### 2.2 Short-Circuit Evaluation

Logical operators `&&` and `||` use short-circuit evaluation.

#### 2.2.1 Logical AND (`&&`)

```calor
left && right
```

**Semantics:**
1. Evaluate `left`
2. If `left` is `false`, result is `false` (do NOT evaluate `right`)
3. If `left` is `true`, evaluate `right` and return its value

**Test Reference:** `S3: LogicalAnd_ShortCircuits`

#### 2.2.2 Logical OR (`||`)

```calor
left || right
```

**Semantics:**
1. Evaluate `left`
2. If `left` is `true`, result is `true` (do NOT evaluate `right`)
3. If `left` is `false`, evaluate `right` and return its value

**Test Reference:** `S4: LogicalOr_ShortCircuits`

### 2.3 Conditional Expressions

```calor
condition ? whenTrue : whenFalse
```

**Semantics:**
1. Evaluate `condition`
2. If `condition` is `true`, evaluate and return `whenTrue` (do NOT evaluate `whenFalse`)
3. If `condition` is `false`, evaluate and return `whenFalse` (do NOT evaluate `whenTrue`)

### 2.4 Null Coalescing

```calor
left ?? right
```

**Semantics:**
1. Evaluate `left`
2. If `left` is not `null`, return `left` (do NOT evaluate `right`)
3. If `left` is `null`, evaluate and return `right`

---

## 3. Scoping Rules

### 3.1 Lexical Scoping

Calor uses **lexical scoping** with parent chain lookup. See `src/Calor.Compiler/Binding/Scope.cs:74-82`.

```calor
§BIND{name=x}{type=INT} INT:1
§IF{if1}
  §COND BOOL:true
  §THEN
    §BIND{name=x}{type=INT} INT:2   // Shadows outer x
    §PRINT §REF{name=x}              // Prints 2
§/IF{if1}
§PRINT §REF{name=x}                  // Prints 1 (outer x unchanged)
```

### 3.2 Shadowing

Inner scope bindings **shadow** outer bindings with the same name.

**Key Semantics:**
- Inner binding takes precedence within its scope
- Shadowing does NOT mutate the outer binding
- When inner scope exits, outer binding is restored

**Test Reference:** `S5: InnerScope_ShadowsOuter`

### 3.3 Scope Resolution Order

1. Look up in current scope
2. If not found, look up in parent scope
3. Continue until found or root scope reached
4. If not found in any scope, emit `Calor0200: UndefinedReference`

### 3.4 Return from Nested Scope

Return statements in nested scopes must correctly unwind to the function boundary.

```calor
§IF{if1}
  §COND BOOL:true
  §THEN
    §R INT:42   // Returns from function, not just if block
§/IF{if1}
```

**Test Reference:** `S6: ReturnFromNestedScope`

---

## 4. Numeric Semantics

### 4.1 Integer Overflow

**Default Behavior:** TRAP (throw `OverflowException`)

```calor
§BIND{name=max}{type=INT} INT:2147483647
§BIND{name=result}{type=INT} §OP{kind=ADD} §REF{name=max} INT:1
// Throws OverflowException
```

**Rationale:** Safety-first philosophy aligns with the contracts design. Silent wraparound can hide bugs.

**Compiler Flag:** `--overflow=[trap|wrap]`
- `trap` (default): Overflow throws `OverflowException`
- `wrap`: Overflow wraps around (two's complement)

**Test Reference:** `S7: IntegerOverflow_Traps`

### 4.2 Type Coercion

| Conversion | Behavior | Rationale |
|------------|----------|-----------|
| INT → FLOAT | Implicit (widening) | No precision loss |
| FLOAT → INT | Explicit required | Potential precision loss |
| Narrowing conversions | Explicit required | Data may be lost |

```calor
§BIND{name=i}{type=INT} INT:42
§BIND{name=f}{type=FLOAT} §REF{name=i}           // OK: implicit widening
§BIND{name=j}{type=INT} §CAST{INT} §REF{name=f}  // Required: explicit narrowing
```

**Test Reference:** `S8: NumericConversion_IntToFloat`

### 4.3 Division

- Integer division: Truncates toward zero
- Division by zero: Throws `DivideByZeroException`
- Float division by zero: Returns `Infinity` or `NaN` per IEEE 754

### 4.4 Numeric Literals

| Type | Examples | Range |
|------|----------|-------|
| `INT` (i32) | `42`, `-1`, `0` | -2^31 to 2^31-1 |
| `FLOAT` (f64) | `3.14`, `-0.5`, `1e10` | IEEE 754 double |
| `BOOL` | `true`, `false` | - |

---

## 5. Contracts

Contracts are semantic constructs that specify behavioral requirements.

### 5.1 Preconditions (REQUIRES)

```calor
§REQUIRES{message="x must be positive"} §OP{kind=GT} §REF{name=x} INT:0
```

**Semantics:**
1. Evaluated **before** function body execution
2. Has access to all function parameters
3. If condition is `false`, throws `ContractViolationException`
4. Exception message includes:
   - Contract message (if specified)
   - Function identifier
   - Contract expression

**Test Reference:** `S10: RequiresFails_ThrowsContractViolation`

### 5.2 Postconditions (ENSURES)

```calor
§ENSURES{message="result must be positive"} §OP{kind=GT} result INT:0
```

**Semantics:**
1. Evaluated **after** function body, **before** return
2. Has access to:
   - All function parameters
   - Special `result` binding (the return value)
3. If condition is `false`, throws `ContractViolationException`

### 5.3 Invariants

```calor
§INVARIANT{message="balance must be non-negative"} §OP{kind=GTE} §REF{name=balance} INT:0
```

**Semantics:**
- Evaluated at specified points (entry/exit of methods)
- Applies to type-level state

### 5.4 Contract Evaluation Order

1. All REQUIRES clauses (in declaration order)
2. Function body
3. All ENSURES clauses (in declaration order)

---

## 6. Option<T> and Result<T,E>

### 6.1 Option<T>

Represents an optional value: either `Some(value)` or `None`.

```calor
§SOME INT:42      // Option<INT> containing 42
§NONE{INT}        // Option<INT> containing nothing
```

**Semantics:**
- `Some(v)` wraps a value
- `None` represents absence of value
- Must be pattern-matched or explicitly unwrapped

**Test Reference:** `S9: OptionNone_BehavesCorrectly`

### 6.2 Result<T,E>

Represents a computation that may fail: either `Ok(value)` or `Err(error)`.

```calor
§OK INT:42                    // Result<INT,E> success
§ERR STR:"not found"          // Result<T,STRING> failure
```

---

## 7. Effects System

Effects track side-effect capabilities of functions.

### 7.1 Effect Categories

| Effect | Symbol | Description |
|--------|--------|-------------|
| Console Read | `cr` | Reads from console |
| Console Write | `cw` | Writes to console |
| File Read | `fs:r` | Reads from files |
| File Write | `fs:w` | Writes to files |
| Network Read | `nr` | Network input |
| Network Write | `nw` | Network output |
| Mutation | `mut` | Mutates state |
| Exception | `ex` | May throw |

### 7.2 Effect Declaration

```calor
§F{f001:readFile:pub}
  §I{string:path}
  §O{string}
  §E{io=fs:r,ex=IOException}
  ...
§/F{f001}
```

### 7.3 Effect Enforcement

Functions may only:
1. Perform effects they declare
2. Call functions whose effects are subsets of their own

---

## 8. Match Expressions

### 8.1 Exhaustiveness

Match expressions MUST be exhaustive. The compiler emits `Calor0500: NonExhaustiveMatch` if cases don't cover all possibilities.

### 8.2 Evaluation

```calor
§MATCH{m1} expr
  §CASE pattern1 guard1 => body1
  §CASE pattern2 => body2
  §CASE _ => default
§/MATCH{m1}
```

**Semantics:**
1. Evaluate `expr` once
2. Try patterns in order until one matches
3. If pattern has guard, evaluate guard; if false, try next pattern
4. Execute matched case body
5. Return result (for match expressions)

### 8.3 Pattern Precedence

Patterns are tried in **declaration order**. First matching pattern wins.

---

## 9. Exception Handling

### 9.1 Try/Catch/Finally

```calor
§TRY{try1}
  body
§CATCH{ExceptionType:var}
  handler
§FINALLY
  cleanup
§/TRY{try1}
```

**Semantics:**
1. Execute `body`
2. If exception occurs, find first matching catch clause
3. Execute matched handler
4. `FINALLY` block always executes (even if no exception)

### 9.2 Exception Propagation

Uncaught exceptions propagate up the call stack until caught or program terminates.

### 9.3 Rethrow

`§RETHROW` re-throws the current exception, preserving the original stack trace.

---

## 10. Memory Model

### 10.1 Value vs Reference Types

| Category | Behavior | Examples |
|----------|----------|----------|
| Value Types | Copied on assignment | INT, FLOAT, BOOL, structs |
| Reference Types | Reference copied | Classes, arrays, delegates |

### 10.2 Immutability

By default, bindings are immutable:

```calor
§BIND{name=x}{type=INT} INT:42
§SET §REF{name=x} INT:43  // ERROR: x is immutable
```

Mutable bindings require explicit declaration:

```calor
§BIND{name=x}{type=INT}{mut=true} INT:42
§SET §REF{name=x} INT:43  // OK
```

---

## 11. Async/Await

### 11.1 Semantics

```calor
§AWAIT expr
```

**Semantics:**
1. Evaluate `expr` (must return awaitable type)
2. If already completed, continue with result
3. If not completed, suspend execution and return control
4. When completed, resume with result

### 11.2 ConfigureAwait

```calor
§AWAIT{false} expr  // ConfigureAwait(false)
```

---

## 12. Diagnostics

### 12.1 Semantic Error Codes

| Code | Name | Description |
|------|------|-------------|
| Calor0200 | UndefinedReference | Reference to undefined symbol |
| Calor0201 | DuplicateDefinition | Symbol already defined |
| Calor0202 | TypeMismatch | Type incompatibility |
| Calor0300 | InvalidPrecondition | Malformed REQUIRES |
| Calor0301 | InvalidPostcondition | Malformed ENSURES |
| Calor0302 | ContractViolation | Runtime contract failure |
| Calor0500 | NonExhaustiveMatch | Match not exhaustive |
| Calor0700 | SemanticsVersionMismatch | Warning: version mismatch |
| Calor0701 | SemanticsVersionIncompatible | Error: incompatible version |

---

## 13. Versioning

See `docs/semantics/versioning.md` for details on semantics versioning.

**Current Version:** 1.0.0

Modules can declare required semantics version:

```calor
§M{m001:MyModule}
  §SEMVER{1.0.0}
```

---

## References

- Scope Implementation: `src/Calor.Compiler/Binding/Scope.cs`
- Diagnostics: `src/Calor.Compiler/Diagnostics/Diagnostic.cs`
- Test Suite: `tests/Calor.Semantics.Tests/`
- CNF Specification: `docs/semantics/normal-form.md`
- Backend Specification: `docs/semantics/dotnet-backend.md`
