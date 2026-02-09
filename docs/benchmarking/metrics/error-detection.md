---
layout: default
title: Error Detection
parent: Benchmarking
nav_order: 4
---

# Error Detection Metric

**Category:** Error Detection
**Result:** Calor wins ([see current ratio](/calor/benchmarking/results/))
**What it measures:** Bug identification and contract violation detection

---

## Overview

The Error Detection metric measures how effectively an AI agent can identify bugs and contract violations using explicit semantics.

---

## Why It Matters

When reviewing code, agents need to detect:
- Precondition violations at call sites
- Postcondition violations in implementations
- Missing null checks
- Bounds errors
- Invariant violations

Explicit contracts make these violations visible without deep analysis.

---

## How It's Measured

### Calor Detection Factors

| Factor | Points | Rationale |
|:-------|:-------|:----------|
| Has `§Q` (requires) | 0.25 | Preconditions catch caller errors |
| Has `§S` (ensures) | 0.20 | Postconditions catch implementation errors |
| Has `§IV` (invariant) | 0.15 | Invariants catch state errors |
| Has `§E{...}` (effects) | 0.10 | Effect tracking catches side-effect bugs |
| Has typed inputs | 0.05 | Type errors caught at compile time |
| Has typed output | 0.05 | Return type errors caught |

Base score: 0.30

### C# Detection Factors

| Factor | Points | Rationale |
|:-------|:-------|:----------|
| Has `Debug.Assert` | 0.15 | Runtime assertion checking |
| Has `Contract.Requires` | 0.20 | Code Contracts precondition |
| Has `Contract.Ensures` | 0.15 | Code Contracts postcondition |
| Has null checks (`??`, `?.`) | 0.05 | Null safety |
| Has `ArgumentNullException` | 0.10 | Explicit null validation |
| Has `throw new` | 0.05 | Exception-based validation |
| Has `readonly` | 0.05 | Immutability enforcement |
| Has `const` | 0.05 | Compile-time constants |

Base score: 0.30, cap at 0.90

---

## Example Comparison

### Calor with Contracts

```
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)              // Explicit: b must not be zero
  §Q (>= a 0)              // Explicit: a must be non-negative
  §S (>= result 0)         // Explicit: result is non-negative
  §R (/ a b)
§/F{f001}
```

**Detection capability:**
- Can flag any call where `b` might be 0
- Can flag any call where `a` might be negative
- Can verify implementation satisfies postcondition

### C# Equivalent

```csharp
public static int Divide(int a, int b)
{
    if (b == 0)
        throw new ArgumentException("b cannot be zero");
    if (a < 0)
        throw new ArgumentException("a must be non-negative");

    var result = a / b;
    Debug.Assert(result >= 0);
    return result;
}
```

**Detection capability:**
- Must recognize exception pattern as precondition
- Must recognize Debug.Assert as postcondition
- Less structured, more inference required

---

## Bug Categories

### Null Reference

**Calor detection:**
```
§Q (!= input null)    // Explicit null check requirement
```

**C# detection:**
```csharp
if (input == null) throw new ArgumentNullException();
// or
input ?? throw new ArgumentNullException();
```

### Bounds Check

**Calor detection:**
```
§Q (>= index 0)
§Q (< index (len array))
```

**C# detection:**
```csharp
if (index < 0 || index >= array.Length)
    throw new ArgumentOutOfRangeException();
```

### Contract Violation

**Calor:** Direct contract syntax enables automated verification.

**C#:** Must parse exception throws and assertions.

---

## Real-World Example

### Finding a Bug

Given this buggy call:

```
// Calling Divide with potentially zero divisor
§B{result} §C{Divide} §A x §A (- y y) §/C    // (y - y) = 0!
```

**Calor agent detection:**
1. See `§C{Divide}`
2. Look up `Divide` contracts: `§Q (!= b 0)`
3. Evaluate second argument: `(- y y)` = 0
4. Report: "Precondition violation: `(!= b 0)` fails when `b = (- y y) = 0`"

**C# agent detection:**
1. See `Divide(x, y - y)`
2. Find Divide implementation
3. Parse exception throw pattern
4. Infer precondition
5. Evaluate argument
6. Report potential issue

---

## Scoring Example

### Calor Code

```
§F{f001:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32!str}
  §Q (!= b 0)
  §S (|| (== result.IsOk true) (!= result.Error ""))
  §IF{if1} (== b 0) → §R §ERR "Division by zero"
  §EL → §R §OK (/ a b)
  §/I{if1}
§/F{f001}
```

**Score:**
- Base: 0.30
- `§Q`: +0.25
- `§S`: +0.20
- `§I{`: +0.05
- `§O{`: +0.05

**Total: 0.85**

### C# Equivalent

```csharp
public static Result<int, string> SafeDivide(int a, int b)
{
    if (b == 0)
        return Result.Err("Division by zero");
    return Result.Ok(a / b);
}
```

**Score:**
- Base: 0.30
- Has type annotations: +0.10
- Has return: +0.05

**Total: 0.45**

**Ratio: 0.85 / 0.45 = 1.89x** (Calor significantly better for this example)

---

## Interpretation

Calor's error detection advantage indicates that first-class contracts provide substantially better bug detection signals.

The advantage is highest when:
- Functions have explicit preconditions and postconditions
- Multiple contracts are present
- Contracts use complex conditions

[See current benchmark results →](/calor/benchmarking/results/)

---

## Next

- [Edit Precision](/calor/benchmarking/metrics/edit-precision/) - How IDs enable precise changes
