---
layout: default
title: Syntax Reference
nav_order: 4
has_children: true
permalink: /syntax-reference/
---

# Syntax Reference

Complete reference for OPAL syntax. Always use V2+ syntax (Lisp-style expressions).

---

## Quick Reference Table

| Element | Syntax | Example |
|:--------|:-------|:--------|
| Module | `§M[id:name]` | `§M[m001:Calculator]` |
| Function | `§F[id:name:visibility]` | `§F[f001:Add:pub]` |
| Input | `§I[type:name]` | `§I[i32:x]` |
| Output | `§O[type]` | `§O[i32]` |
| Effects | `§E[codes]` | `§E[cw,fr,net]` |
| Requires | `§Q expr` | `§Q (>= x 0)` |
| Ensures | `§S expr` | `§S (>= result 0)` |
| For Loop | `§L[id:var:from:to:step]` | `§L[l1:i:1:100:1]` |
| While Loop | `§WH[id] condition` | `§WH[w1] (> i 0)` |
| Do-While Loop | `§DO[id]...§/DO[id] cond` | `§DO[d1]...§/DO[d1] (< i 10)` |
| If/ElseIf/Else | `§IF...§EI...§EL` | `§IF (> x 0) → §R x §EL → §R 0` |
| Call | `§C[target]...§/C` | `§C[Math.Max] §A 1 §A 2 §/C` |
| C# Attribute | `[@Name]` or `[@Name(args)]` | `[@HttpPost]`, `[@Route("api")]` |
| Print | `§P expr` | `§P "Hello"` |
| Return | `§R expr` | `§R (+ a b)` |
| Binding | `§B[name] expr` | `§B[x] (+ 1 2)` |
| Operations | `(op args...)` | `(+ a b)`, `(== x 0)` |
| Close tag | `§/X[id]` | `§/F[f001]` |

---

## Types

| Type | Description | C# Equivalent |
|:-----|:------------|:--------------|
| `i32` | 32-bit integer | `int` |
| `i64` | 64-bit integer | `long` |
| `f32` | 32-bit float | `float` |
| `f64` | 64-bit float | `double` |
| `str` | String | `string` |
| `bool` | Boolean | `bool` |
| `void` | No return value | `void` |
| `?T` | Optional T | `T?` (nullable) |
| `T!E` | Result (T or error E) | `Result<T, E>` |

---

## Operators

| Category | Operators |
|:---------|:----------|
| Arithmetic | `+`, `-`, `*`, `/`, `%` |
| Comparison | `==`, `!=`, `<`, `<=`, `>`, `>=` |
| Logical | `&&`, `\|\|`, `!` |

All operators use Lisp-style prefix notation: `(+ a b)`, `(&& x y)`

---

## Effect Codes

| Code | Effect | Description |
|:-----|:-------|:------------|
| `cw` | Console write | `Console.WriteLine` |
| `cr` | Console read | `Console.ReadLine` |
| `fw` | File write | File system writes |
| `fr` | File read | File system reads |
| `net` | Network | HTTP, sockets, etc. |
| `db` | Database | Database operations |

---

## ID Conventions

| Element | Convention | Example |
|:--------|:-----------|:--------|
| Modules | `m001`, `m002` | `§M[m001:Calculator]` |
| Functions | `f001`, `f002` | `§F[f001:Add:pub]` |
| Loops | `for1`, `while1`, `do1` | `§L[for1:i:1:10:1]` |
| Conditionals | `if1`, `if2` | `§IF[if1] condition` |

---

## Complete Example

```
§M[m001:FizzBuzz]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §L[for1:i:1:100:1]
    §IF[if1] (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I[if1]
  §/L[for1]
§/F[f001]
§/M[m001]
```

---

## Detailed Reference

- [Structure Tags](/opal/syntax-reference/structure-tags/) - Modules, functions, closing tags
- [Types](/opal/syntax-reference/types/) - Type system, Option, Result
- [Expressions](/opal/syntax-reference/expressions/) - Lisp-style operators
- [Control Flow](/opal/syntax-reference/control-flow/) - Loops, conditionals
- [Contracts](/opal/syntax-reference/contracts/) - Requires, ensures
- [Effects](/opal/syntax-reference/effects/) - Effect declarations
