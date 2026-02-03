---
layout: default
title: Expressions
parent: Syntax Reference
nav_order: 3
---

# Expressions

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
§/I{if1}
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
