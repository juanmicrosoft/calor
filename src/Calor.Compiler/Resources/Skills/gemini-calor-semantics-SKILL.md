---
name: calor-semantics
description: Calor formal semantics reference covering evaluation order, scoping, overflow, and contracts.
---

# @calor-semantics - Calor Formal Semantics Reference

Calor has formal semantics (v1.0.0) that define precise runtime behavior. **These differ from C#.**

## Quick Reference

| Rule | ID | Calor Behavior |
|------|-----|----------------|
| Evaluation Order | S1, S2 | Strictly left-to-right |
| Short-Circuit | S3, S4 | `&&`/`||` always short-circuit |
| Scoping | S5, S6 | Lexical; inner does NOT mutate outer |
| Integer Overflow | S7 | TRAP (throws `OverflowException`) |
| Type Coercion | S8 | Explicit narrowing, implicit widening |
| Option/Result | S9 | Explicit unwrap required |
| Contracts | S10 | `§Q` before body, `§S` after body |

## S1, S2: Evaluation Order

All expressions evaluate **strictly left-to-right**.

```calor
§B{result} (+ (f) (g) (h))  // f(), then g(), then h()
```

**C# difference:** C# has unspecified argument evaluation order.

## S3, S4: Short-Circuit Evaluation

`&&` and `||` always short-circuit:

```calor
§IF{i1} (&& (check x) (expensive y))  // expensive only if check passes
§/I{i1}
```

## S5, S6: Scoping Rules

Lexical scoping with shadowing. **Inner scope does NOT mutate outer:**

```calor
§B{x} 10
§IF{i1} condition
  §B{x} 20        // shadows outer x, does NOT modify it
§/I{i1}
§P x              // prints 10
```

## S7: Integer Overflow

All integer arithmetic is **checked by default**. Overflow throws `OverflowException`.

```calor
§B{max} 2147483647
§B{overflow} (+ max 1)   // THROWS OverflowException
```

Use `§UNCHECKED` block for wrapping behavior if needed.

## S8: Type Coercion

- **Widening (safe):** Implicit - `i32` to `i64`
- **Narrowing (lossy):** Explicit `§CAST` required

```calor
§B{wide} x                  // i32 → i64 OK (implicit)
§B{narrow} §CAST{i32} y     // i64 → i32 requires explicit cast
```

## S9: Option and Result

Option (`?T`) and Result (`T!E`) must be explicitly unwrapped:

```calor
§B{maybeVal} §SM 42         // Some(42)
§B{noVal} §NN{type=i32}     // None

§B{okResult} §OK 100        // Ok(100)
§B{errResult} §ERR "fail"   // Err("fail")
```

## S10: Contracts

Contracts are checked at runtime:
- `§Q` (Requires): Checked **before** function body
- `§S` (Ensures): Checked **after** function body

```calor
§F{f1:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)               // precondition: b != 0
  §S (>= result 0)          // postcondition: result >= 0
  §R (/ a b)
§/F{f1}
```

## S11: Exception Handling

Exception handling follows standard structured exception semantics:

- `§TR` try blocks execute in order
- `§CA` catch clauses are evaluated in declaration order (most specific first)
- `§FI` finally blocks always execute (on normal exit or exception)
- `§WHEN` filters are evaluated before entering catch block
- `§RT` rethrow preserves original stack trace

```calor
§TR{t1}
  §R (/ a b)
§CA{DivideByZeroException:ex}
  §R 0
§CA{Exception:ex} §WHEN (! (== ex.Message ""))
  §P ex.Message
  §RT
§FI
  §P "cleanup"
§/TR{t1}
```

## S12: Async/Await

Async operations follow standard async/await semantics:

- `§AF`/`§AMT` declares async context (returns Task<T>)
- `§AWAIT` suspends execution until task completes
- Multiple awaits execute sequentially within same function
- Exception propagation works through async boundaries

```calor
§AF{f1:ProcessAsync:pub}
  §O{str}
  §B{str:data} §AWAIT §C{FetchAsync} §/C    // suspends here
  §B{str:result} §AWAIT §C{TransformAsync} §A data §/C
  §R result
§/AF{f1}
```

## S13: Collection Semantics

Collection operations have well-defined behavior:

- `§LIST`/`§DICT`/`§HSET` initialize with provided elements
- `§PUSH` adds to end (list/set), `§PUT` adds/updates (dict)
- `§REM` removes first occurrence (list), by key (dict), or by value (set)
- `§HAS` returns bool for membership test
- Iteration order: lists maintain order, dicts preserve insertion order, sets unordered

## Test Reference

| Test ID | Semantic Rule |
|---------|---------------|
| S1 | Left-to-right evaluation (binary ops) |
| S2 | Left-to-right evaluation (function args) |
| S3 | `&&` short-circuits |
| S4 | `||` short-circuits |
| S5 | Lexical scoping |
| S6 | Shadowing does not mutate outer |
| S7 | Integer overflow traps |
| S8 | Explicit narrowing casts |
| S9 | Option/Result explicit unwrap |
| S10 | Contract evaluation order |

See `docs/semantics/core.md` for the full formal specification.

## ID Integrity Rules

### Canonical IDs (Production Code)
```
f_01J5X7K9M2NPQRSTABWXYZ12    Function
m_01J5X7K9M2NPQRSTABWXYZ12    Module
c_01J5X7K9M2NPQRSTABWXYZ12    Class
mt_01J5X7K9M2NPQRSTABWXYZ12   Method
ctor_01J5X7K9M2NPQRSTABWXYZ12 Constructor
p_01J5X7K9M2NPQRSTABWXYZ12    Property
i_01J5X7K9M2NPQRSTABWXYZ12    Interface
e_01J5X7K9M2NPQRSTABWXYZ12    Enum
```

### Test IDs (ONLY in tests/, docs/, examples/)
```
f001, m001, c001              Sequential test IDs
```

### Agent Rules - CRITICAL
1. **NEVER** modify an existing ID
2. **NEVER** copy IDs when extracting code
3. **OMIT** IDs for new declarations - run `calor ids assign`
4. **VERIFY** before commit: `calor ids check`

### Preservation Rules
| Operation | ID Behavior |
|-----------|-------------|
| Rename | PRESERVE |
| Move file | PRESERVE |
| Reformat | PRESERVE |
| Extract helper | NEW ID |

### Verification Steps
```bash
calor ids check .
calor ids assign . --dry-run
```
