---
layout: default
title: Expressions
parent: Syntax Reference
nav_order: 3
---

# Expressions

## Integer literals

Integer literals preserve sign, base, width, and signedness through compilation.
Decimal and hexadecimal forms accept `_` separators:

```calor
INT:-0x8000_0000
LONG:9_223_372_036_854_775_807
UINT:4_294_967_295
ULONG:18_446_744_073_709_551_615
0xFFFF_FFFFU
```

`INT:` and unsuffixed literals infer 32- or 64-bit signed width from their
value. `LONG:`, `UINT:`, and `ULONG:` request an explicit width/signedness.
Negative `UINT:`/`ULONG:` literals and negative literals with a `U` suffix are
rejected with `Calor0010`.

Negative hexadecimal minimum values are supported and emitted without changing
their runtime value.

An explicit signed `L` suffix must remain signed. Values above
`9223372036854775807L` (or below `-9223372036854775808L`) are rejected with
`Calor0012`; they are never reinterpreted as `ulong`. The exact
`-9223372036854775808L` boundary remains valid, and `UL` continues to request
unsigned 64-bit semantics explicitly.

## String literals and interpolation

Ordinary and triple-quoted strings use `${expression}` interpolation. The
lexer records literal and expression parts structurally; code generation does
not reparse expression text from a decoded string.

```calor
"Value: ${value,-10:N2}"
"""
Name: ${name}
Value: ${value:N2}
"""
```

`\${` keeps interpolation literal. Escaped braces remain literal braces.
Named and numeric interpolation expressions, alignment, and format clauses are
preserved. Unknown escapes retain their backslash and character rather than
silently dropping the backslash.

Numeric placeholder syntax such as `${0}` is preserved as literal placeholder
text. Its intent is recorded explicitly in the AST; `${-1}` is an expression
and evaluates to `-1`, while `\${-1}` remains literal text.

Interpolated UTF-8 literals are rejected with `Calor0011`. Use interpolation
to produce a string and encode it explicitly; Calor never emits invalid
interpolated C# such as `$"..."u8`.

Binary expression emission preserves the AST evaluation tree. In particular,
equal-precedence right operands in subtraction, division, shifts, and
comparisons are parenthesized when required.

Calor uses Lisp-style prefix notation for all operations. This eliminates operator precedence ambiguity.

---

## Prefix Notation

Instead of infix `a + b`, Calor uses prefix `(+ a b)`:

| Infix | Calor Prefix |
|:------|:------------|
| `a + b` | `(+ a b)` |
| `a * b + c` | `(+ (* a b) c)` |
| `a + b * c` | `(+ a (* b c))` |
| `(a + b) * c` | `(* (+ a b) c)` |

---

## Arithmetic Operators

| Operator | Meaning | Example |
|:---------|:--------|:--------|
| `+` | Addition | `(+ a b)` |
| `-` | Subtraction | `(- a b)` |
| `*` | Multiplication | `(* a b)` |
| `/` | Division | `(/ a b)` |
| `%` | Modulo | `(% a b)` |

### Examples

```
(+ 1 2)           // 3
(- 10 3)          // 7
(* 4 5)           // 20
(/ 15 3)          // 5
(% 17 5)          // 2
```

### Nested Expressions

```
// (1 + 2) * 3 = 9
(* (+ 1 2) 3)

// 1 + (2 * 3) = 7
(+ 1 (* 2 3))

// ((a + b) * c) - d
(- (* (+ a b) c) d)
```

---

## Comparison Operators

| Operator | Meaning | Example |
|:---------|:--------|:--------|
| `==` | Equal | `(== a b)` |
| `!=` | Not equal | `(!= a b)` |
| `<` | Less than | `(< a b)` |
| `<=` | Less or equal | `(<= a b)` |
| `>` | Greater than | `(> a b)` |
| `>=` | Greater or equal | `(>= a b)` |

### Examples

```
(== x 0)          // x equals 0
(!= y "")         // y is not empty string
(< age 18)        // age less than 18
(>= score 70)     // score at least 70
```

---

## Logical Operators

| Operator | Meaning | Example |
|:---------|:--------|:--------|
| `&&` | Logical AND | `(&& a b)` |
| `\|\|` | Logical OR | `(\|\| a b)` |
| `!` | Logical NOT | `(! a)` |

### Examples

```
(&& (> x 0) (< x 100))      // x > 0 AND x < 100
(|| (== a 1) (== a 2))      // a == 1 OR a == 2
(! (== x 0))                // NOT (x == 0)
```

### Complex Conditions

```
// (x > 0 && x < 100) || y == 0
(|| (&& (> x 0) (< x 100)) (== y 0))

// !(a == b && c == d)
(! (&& (== a b) (== c d)))
```

---

## Using Expressions

### In Return Statements

```
§R (+ a b)
§R (* (- x 1) 2)
§R (>= score 70)
```

### In Bindings

```
§B{sum} (+ a b)
§B{product} (* x y)
§B{isValid} (&& (> x 0) (< x 100))
```

### In Print Statements

```
§P (+ 1 2)          // prints 3
§P (* x x)          // prints x squared
```

### In Conditions

```
§IF{if1} (> x 0) → §P "positive"
§EI (< x 0) → §P "negative"
§EL → §P "zero"
```

### In Contracts

```
§Q (>= x 0)                      // Requires: x >= 0
§Q (!= divisor 0)                // Requires: divisor not zero
§S (>= result 0)                 // Ensures: result >= 0
§S (<= result (* x x))           // Ensures: result <= x²
```

### In Loop Bounds

Loop bounds can be expressions:

```
§L{for1:i:0:(- n 1):1}    // i from 0 to n-1
§L{for2:j:1:(* 2 n):2}    // j from 1 to 2n, step 2
```

---

## Collection Expressions

Calor provides expressions for querying collections.

### Contains Check (`§HAS`)

Check if a collection contains an element.

| Syntax | Description | C# Equivalent |
|:-------|:------------|:--------------|
| `§HAS{coll} value` | Element in list/set | `coll.Contains(value)` |
| `§HAS{dict} §KEY key` | Key in dictionary | `dict.ContainsKey(key)` |
| `§HAS{dict} §VAL value` | Value in dictionary | `dict.ContainsValue(value)` |

**Examples:**
```
// Check if list contains element
§IF{if1} §HAS{numbers} 5
  §P "Found 5"

// Check if key exists in dictionary
§IF{if2} §HAS{ages} §KEY "alice"
  §P "Alice found"

// Use in binding
§B{hasItem} §HAS{inventory} "sword"
```

### Collection Count (`§CNT`)

Get the number of elements in a collection.

| Syntax | Returns | C# Equivalent |
|:-------|:--------|:--------------|
| `§CNT{coll}` | `i32` | `coll.Count` |

**Examples:**
```
// Get count
§B{size} §CNT{items}

// Use in condition
§IF{if1} (> §CNT{queue} 0)
  §P "Queue not empty"

// Use in loop bound
§L{for1:i:0:(- §CNT{list} 1):1}
  §P list[i]
```

### Using Collection Expressions in Contracts

```
§F{f001:ProcessItems:pub}
  §I{List<i32>:items}
  §O{i32}
  §Q (> §CNT{items} 0)           // Requires: items not empty
  §S (>= result 0)
  // ...
```

---

## Why Prefix Notation?

### 1. No Precedence Ambiguity

Infix:
```javascript
a + b * c    // Is this (a+b)*c or a+(b*c)?
```

Calor:
```
(+ a (* b c))    // Clearly a + (b * c)
(* (+ a b) c)    // Clearly (a + b) * c
```

### 2. Easy AST Manipulation

The structure `(op arg1 arg2)` directly represents the AST node.

### 3. Uniform Syntax

Every operation follows the same pattern: `(operator arguments...)`

---

## Common Patterns

### FizzBuzz Check

```
(== (% i 15) 0)    // i divisible by 15
(== (% i 3) 0)     // i divisible by 3
(== (% i 5) 0)     // i divisible by 5
```

### Range Check

```
(&& (>= x min) (<= x max))    // min <= x <= max
```

### Null Check

```
(!= value null)    // value is not null
```

### Equality with Multiple Values

```
(|| (== x 1) (|| (== x 2) (== x 3)))    // x is 1, 2, or 3
```

---

## Next

- [Control Flow](/calor/syntax-reference/control-flow/) - Loops and conditionals
