---
layout: default
title: Refinement Types
parent: Syntax Reference
nav_order: 6
---

# Refinement Types

Refinement types let you constrain values at the type level. A refined type is a base type plus a predicate — the compiler verifies the predicate holds, and erases the refinement in the emitted C#.

---

## Quick Reference

| Syntax | Purpose | Example |
|:-------|:--------|:--------|
| `§RTYPE{id:Name:base} (pred)` | Define a named refinement type | `§RTYPE{r1:NatInt:i32} (>= # INT:0)` |
| `§ITYPE{id:Name:base:size}` | Define an indexed (size-parameterized) type | `§ITYPE{it1:SizedList:List:n}` |
| `§ITYPE{id:Name:base:size} (pred)` | Indexed type with size constraint | `§ITYPE{it1:NonEmpty:List:n} (> # INT:0)` |
| `§I{type:param} \| (pred)` | Inline refinement on a parameter | `§I{i32:age} \| (>= # INT:0)` |
| `§PROOF{id:desc} (expr)` | Proof obligation (assert a fact) | `§PROOF{p1:positive} (>= x INT:0)` |
| `#` | Self-reference (the value being refined) | `(>= # INT:0)` |

---

## Named Refinement Types (`§RTYPE`)

Define a refinement type at module level. The predicate uses `#` to refer to the value being constrained.

### Syntax

```
§RTYPE{id:Name:baseType} (predicate)
```

- `id` — unique identifier (e.g., `r1`)
- `Name` — the refinement type name, used in parameter declarations
- `baseType` — the underlying type (`i32`, `str`, etc.)
- `predicate` — a boolean expression using `#` for the value

### Examples

```
§RTYPE{r1:NatInt:i32} (>= # INT:0)
§RTYPE{r2:Port:i32} (&& (>= # INT:1) (<= # INT:65535))
§RTYPE{r3:NonEmpty:str} (> (len #) INT:0)
```

These read as:
- **NatInt**: an `i32` that is non-negative
- **Port**: an `i32` between 1 and 65535
- **NonEmpty**: a `str` with length greater than 0

### Using Named Refinement Types

Once defined, use the name as a parameter type:

```
§RTYPE{r1:NatInt:i32} (>= # INT:0)

§F{f001:Process:pub}
  §I{NatInt:count}
  §O{void}
  // count is guaranteed non-negative
§/F{f001}
```

The obligation engine creates a verification obligation for `count` — proving the refinement predicate holds given the function's preconditions.

---

## Inline Refinements (`|`)

For one-off constraints, attach a predicate directly to a parameter with `|`:

### Syntax

```
§I{baseType:paramName} | (predicate)
```

### Examples

```
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b} | (!= # INT:0)         // b must be non-zero
  §O{i32}
  §R (/ a b)
§/F{f001}
```

```
§F{f002:SetPort:pub}
  §I{i32:port} | (&& (>= # INT:1) (<= # INT:65535))
  §O{void}
  // ...
§/F{f002}
```

### Inline Refinements with Default Values

Inline refinements can coexist with default parameter values:

```
§I{i32:age} | (>= # INT:0) = INT:18
```

---

## Self-Reference (`#`)

The `#` symbol refers to the value being constrained. It is only valid inside refinement predicates:

```
§RTYPE{r1:Positive:i32} (> # INT:0)     // # = the i32 value
§I{i32:x} | (>= # INT:0)                // # = x
```

Using `#` outside a refinement predicate is a compile error (`SelfRefOutsidePredicate`).

---

## Proof Obligations (`§PROOF`)

A proof obligation asserts that a condition holds at a specific point in the function body. The obligation engine attempts to prove it using Z3.

### Syntax

```
§PROOF{id:description} (boolean-expression)
§PROOF{id} (boolean-expression)
```

- `id` — unique identifier
- `description` — optional human-readable label

### Examples

```
§F{f001:Transfer:pub}
  §I{i32:balance}
  §I{i32:amount} | (> # INT:0)
  §O{i32}
  §Q (>= balance amount)

  §B{newBalance:i32} (- balance amount)
  §PROOF{p1:non-negative} (>= newBalance INT:0)

  §R newBalance
§/F{f001}
```

Z3 proves this: given `balance >= amount` and `amount > 0`, then `balance - amount >= 0`.

### Proof Obligation Statuses

| Status | Meaning | C# Output |
|:-------|:--------|:----------|
| Discharged | Z3 proved the condition | `// PROVEN: proof obligation [p1]` |
| Failed | Z3 found a counterexample | Runtime guard (throws on violation) |
| Timeout | Z3 couldn't decide in time | Runtime guard (configurable by policy) |
| Boundary | Public API — can't verify callers | Runtime guard (always emitted) |

---

## Indexed Types (`§ITYPE`)

Indexed types are size-parameterized types — a collection type annotated with a size variable so the compiler can track bounds and prove array/list accesses are safe using Z3.

### Syntax

```
§ITYPE{id:Name:baseType:sizeParam}                     // no constraint
§ITYPE{id:Name:baseType:sizeParam} (constraint on #)   // with constraint
```

- `id` — unique identifier (e.g., `it1`)
- `Name` — the indexed type name, used in parameter declarations
- `baseType` — the underlying collection type (`List`, `i32[]`, etc.)
- `sizeParam` — name of the size variable (becomes a Z3 integer)
- `constraint` — optional predicate on the size (e.g., `(> # INT:0)` for non-empty)

### Examples

```
§ITYPE{it1:SizedList:List:n}                            // List with n elements
§ITYPE{it2:NonEmptyArr:i32[]:n} (> # INT:0)             // array with n > 0 elements
§ITYPE{it3:BoundedVec:List:n} (&& (> # INT:0) (< # INT:1000))
```

### Using Indexed Types

Declare a parameter with the indexed type name. The size parameter becomes a Z3 variable available in preconditions and postconditions:

```
§ITYPE{it1:SizedList:List:n}

§F{f001:Sum:priv}
  §I{SizedList:items}        // items has n elements
  §I{i32:n}                  // size parameter as explicit param
  §I{i32:i}
  §O{i32}
  §Q (&& (>= i INT:0) (< i n))
  §R §IDX items i            // proven safe: 0 <= i < n
§/F{f001}
```

The obligation engine creates an `IndexBounds` obligation for each `§IDX` on an indexed-typed array. The condition is `(&& (>= index INT:0) (< index sizeParam))`. When the function has preconditions or inline refinements bounding the index, Z3 discharges the obligation automatically.

### Erasure

Indexed type definitions are erased in C#. Parameters with indexed type names are mapped to their base types:

| Calor | Emitted C# |
|:------|:-----------|
| `§ITYPE{it1:SizedList:List:n}` | *(nothing — erased)* |
| `§I{SizedList:items}` | `List items` |
| `§I{SizedList<i32>:items}` | `List<int> items` |

---

## Erasure

Refinement types are a **compile-time construct**. In the emitted C#, they are erased:

| Calor | Emitted C# |
|:------|:-----------|
| `§RTYPE{r1:NatInt:i32} (>= # INT:0)` | *(nothing — erased)* |
| `§I{NatInt:count}` | `int count` |
| `§I{i32:x} \| (>= # INT:0)` | `int x` |
| `§PROOF{p1:check} (cond)` (Discharged) | `// PROVEN: proof obligation [p1: check]` |
| `§PROOF{p1:check} (cond)` (Failed) | `if (!(cond)) throw new InvalidOperationException(...)` |

The refinement constrains what values are valid. The obligation engine verifies those constraints and emits runtime guards only where verification fails.

---

## Obligation Engine

The obligation engine is the verification pipeline for refinement types:

1. **Generation** — Walk the AST and create obligations for every refined parameter and `§PROOF` statement
2. **Solving** — For each obligation, use Z3's assume-negate-check pattern:
   - Assume all preconditions (`§Q`)
   - Negate the obligation condition
   - If UNSAT → Discharged (proven)
   - If SAT → Failed (counterexample found)
   - If UNKNOWN → Timeout
3. **Guard Discovery** — For failed obligations, discover the simplest guard that would discharge them
4. **Code Generation** — Emit runtime guards based on obligation status and policy

### Obligation Kinds

| Kind | Source |
|:-----|:-------|
| `RefinementEntry` | Parameter with inline or named refinement |
| `ProofObligation` | `§PROOF` statement |
| `IndexBounds` | `§IDX` on an indexed-typed array |

### Public vs Private Functions

For **public** functions, caller constraints can't be verified — parameter refinement obligations are marked `Boundary` and always emit runtime guards. For **private** functions, the solver attempts full verification.

---

## Obligation Policy

The obligation policy controls what happens for each obligation status. Three built-in policies:

### Default

| Status | Action |
|:-------|:-------|
| Discharged | Ignore (proven, no guard needed) |
| Failed | Error (compilation fails) |
| Timeout | WarnAndGuard |
| Boundary | AlwaysGuard |
| Unsupported | WarnOnly |

### Strict

All non-discharged obligations are compilation errors. Use in CI for maximum safety.

### Permissive

Failed obligations emit warnings and guards instead of errors. Use during development or migration.

---

## Complete Example

```
§M{m001:Banking}

// Named refinement types
§RTYPE{r1:PositiveAmount:i32} (> # INT:0)
§RTYPE{r2:NonNegBalance:i32} (>= # INT:0)

§F{f001:Transfer:pub}
  §I{NonNegBalance:balance}
  §I{PositiveAmount:amount}
  §O{i32}
  §Q (>= balance amount)                  // sufficient funds

  §B{result:i32} (- balance amount)
  §PROOF{p1:balance-safe} (>= result INT:0)

  §R result
§/F{f001}

§/M{m001}
```

The obligation engine:
1. Creates `Boundary` obligations for `balance` and `amount` (public function)
2. Creates a `ProofObligation` for `p1`
3. Z3 proves `p1`: given `balance >= 0`, `amount > 0`, and `balance >= amount`, then `balance - amount >= 0`
4. Emitted C#: runtime guards for the public boundary parameters, proven comment for `p1`

---

## See Also

- [Dependent Types Tutorial](/calor/guides/dependent-types-tutorial/) — Progressive walkthrough with MCP agent guidance
- [Contracts](/calor/syntax-reference/contracts/) — Preconditions and postconditions
- [Static Verification](/calor/philosophy/static-verification/) — Z3-based contract verification
- [MCP Tools](/calor/cli/mcp/) — Agent guidance tools for refinement types
