---
layout: default
title: Types
parent: Syntax Reference
nav_order: 2
---

# Types

Calor has primitives, .NET nullable types, ordinary generics, and arrays.

Canonical output uses C# primitive names, suffix arrays (`T[]`, `T[,]`,
`T[][]`), and suffix nullability (`T?`). User-defined names retain their case.
This is a staged printing and parsing update: compact names such as `i32`,
prefix arrays and nullable types, and typed literal prefixes remain accepted
until the coordinated source-grammar cutover. Their continued acceptance is
not the final retirement of the old syntax.

---

## Primitive Types

| Calor | Description | C# | Range |
|:-----|:------------|:---|:------|
| `int` | 32-bit signed integer | `int` | -2³¹ to 2³¹-1 |
| `long` | 64-bit signed integer | `long` | -2⁶³ to 2⁶³-1 |
| `sbyte` | 8-bit signed integer | `sbyte` | -128 to 127 |
| `short` | 16-bit signed integer | `short` | -32768 to 32767 |
| `byte` | 8-bit unsigned integer | `byte` | 0 to 255 |
| `ushort` | 16-bit unsigned integer | `ushort` | 0 to 65535 |
| `uint` | 32-bit unsigned integer | `uint` | 0 to 2³²-1 |
| `ulong` | 64-bit unsigned integer | `ulong` | 0 to 2⁶⁴-1 |
| `float` | 32-bit floating point | `float` | ±3.4 × 10³⁸ |
| `double` | 64-bit floating point | `double` | ±1.8 × 10³⁰⁸ |
| `string` | String | `string` | UTF-16 text |
| `bool` | Boolean | `bool` | `true` or `false` |
| `char` | UTF-16 code unit | `char` | U+0000 to U+FFFF |
| `decimal` | 128-bit precise decimal | `decimal` | ±7.9 × 10²⁸ |
| `void` | No value | `void` | (return type only) |

---

## Usage in Declarations

### Input Parameters

```
§I{int:count}       // int count
§I{string:name}     // string name
§I{double:price}    // double price
§I{bool:active}     // bool active
§I{byte[]:data}     // byte[] data
§I{string[]:args}   // string[] args
```

### Output Types

```
§O{int}             // returns int
§O{string}          // returns string
§O{void}            // returns nothing
§O{byte[]}          // returns byte[]
```

### Constructor arguments

`§NEW{Type}` calls a constructor. Use `§A[name] value` for a named argument;
names select constructor parameters, not object-initializer properties.
Arguments are evaluated in source order, even when names reorder the parameters.
Records use this same constructor syntax.

```
§B{pair:Pair} §NEW{Pair} §A[Right] "right" §A[Left] "left"
```

### Single-precision literals

The lowercase `f` suffix selects a 32-bit floating-point literal, including
whole numbers and scientific notation: `1.5f`, `42f`, and `1e-3f`.
Without the suffix, a fraction or exponent selects a 64-bit floating-point
literal. Values outside the finite 32-bit range produce a diagnostic.

### Decimal Literals

Canonical decimal literals use the `m` suffix. Integer widths use `U`, `L`,
or `UL` when needed; fractions without `f` or `m` have type `double`.

```
§B{~price:decimal} 18.0000m
§B{~total:decimal} 100m
§B{~rate:decimal} 0.05m
```

### Character Literals

Single quotes produce `char`; double quotes produce `string`. Character literals
contain exactly one UTF-16 code unit. Empty literals, multiple characters, raw
line breaks, and invalid escapes report `Calor0112`.

```
§B{digit:char} '0'
§B{newline:char} '\n'
§B{quote:char} '\''
§B{slash:char} '\\'
§B{letter:char} '\u0041'
```

Escapes follow C#: `\0`, `\a`, `\b`, `\f`, `\n`, `\r`, `\t`, `\v`, `\'`, `\"`,
`\\`, `\x` (one to four hexadecimal digits), `\u` (four digits), and `\U` (eight
digits). The decoded value must fit one UTF-16 code unit; supplementary Unicode
characters require a string. Escaped surrogate code units round-trip unchanged.

Characters participate in numeric arithmetic: `(- c '0')` returns an `int`.
The existing `(char-lit "x")` spelling remains accepted and formats as `'x'`.

---

## Array Types

Arrays use C# suffix notation. Rank and nesting are preserved.

### Syntax

```
T[]                 // Array of T
T[,]                // Two-dimensional array
T[][]               // Jagged array (array of arrays)
```

### Examples

| Calor Type | C# Equivalent |
|:----------|:--------------|
| `byte[]` | `byte[]` |
| `int[]` | `int[]` |
| `string[]` | `string[]` |
| `bool[]` | `bool[]` |
| `int[][]` | `int[][]` |
| `int[,]` | `int[,]` |

### Usage

```
§FLD{byte[]:_buffer:priv}      // private byte[] field
§I{string[]:args}             // string[] parameter
§O{int[]}                    // returns int[]
```

---

## Nullable References (`T?`)

For supported reference types, `string?` means C# `string?`, and `Foo?`
means a nullable `Foo` reference. These annotations do not wrap the value.
Suffix order matters: `string?[]` has nullable elements; `string[]?` is a
nullable array.

```calor
§M{NullableReferenceExample}
  §F{Identity:pub} (string?:value) -> string?
    §E{}
    §R value
  §F{Greeting:pub} () -> string?
    §E{}
    §B{greeting:string?} "hello"
    §R greeting
```

Nullable-reference typing is not a promise of complete null-safety enforcement.
The staged `Calor0272`/`Calor0273`/`Calor0274` binder findings remain analysis-only;
some older unsafe assignments still fail the TypeChecker first. General mutation
is outside those receiving-boundary checks.

Nullable-value spellings such as `int?` emit C# `int?`, not a reference.
This reference-typing repair does not expand their TypeChecker support.
Unknown external types and arbitrary generic payloads also need separate analysis;
absence of an error is not proof that they were resolved.

Use ordinary values or `null` for absence. A fallback uses the existing
null-coalescing expression:

```calor
§M{NullableFallback}
  §F{Greeting:pub} (string?:name) -> string
    §E{}
    §R (?? name "anonymous")
```

---

## Failure Uses Exceptions

Return the ordinary success type and throw an exception on failure. Declare
the exception effect `throw` (and `alloc` when constructing the exception).

```calor
§M{Division}
  §F{Divide:pub} (int:a, int:b) -> int
    §E{throw,alloc}
    §IF (== b 0)
      §TH §NEW{ArgumentException} §A "Division by zero" §/NEW
    §R (/ a b)
```

Handle exceptions with `§TR` / `§CA`; use `§FI` for cleanup and `§RT` to
rethrow. No special success/error return-type encoding is required.

---

## Generic Types

Generic types use angle bracket syntax inline.

Third-party types named `Option<T>` or `Result<T,E>` are ordinary generic
types, not Calor built-ins. Import their namespace and use their library API.

### Syntax

```
List<T>                 // List of T
Dictionary<K, V>        // Dictionary with key K and value V
IEnumerable<T>          // Enumerable of T
Func<T, U>              // Function from T to U
```

### Examples

```
§I{List<int>:numbers}              // List<int> parameter
§I{Dictionary<string, int>:scores} // Dictionary<string, int>
§O{IEnumerable<string>}           // Returns IEnumerable<string>
§FLD{HashSet<T>:_items:priv}     // Generic field with type parameter T
```

### Nested Generic Types

Generic types can be nested.

```
§I{Dictionary<string, List<int>>:data} // Dictionary<string, List<int>>
§O{List<Tuple<string, int>>}           // List<Tuple<string, int>>
```

### Type Parameters

When defining generic functions or classes, type parameters (like `T`, `U`) can be used as types.

```
§F{First:pub}<T>
  §I{List<T>:items}
  §O{T}
  §R items[0]
```

See [Structure Tags - Generics](/calor/syntax-reference/structure-tags/#generics) for more on defining generic functions and classes.

---

## Collection Types

Calor provides built-in syntax for creating and manipulating collections with type-safe operations.

### List Creation

```
§LIST{name:elementType}
  element1
  element2
```

| Part | Description |
|:-----|:------------|
| `name` | Variable name for the list |
| `elementType` | Type of elements (`int`, `string`, etc.) |

**Example:**
```
§LIST{numbers:int}
  1
  2
  3
```

### Dictionary Creation

```
§DICT{name:keyType:valueType}
  §KV key1 value1
  §KV key2 value2
```

| Part | Description |
|:-----|:------------|
| `name` | Variable name for the dictionary |
| `keyType` | Type of keys |
| `valueType` | Type of values |
| `§KV` | Key-value pair entry |

**Example:**
```
§DICT{ages:string:int}
  §KV "alice" 30
  §KV "bob" 25
```

### HashSet Creation

```
§HSET{name:elementType}
  element1
  element2
```

**Example:**
```
§HSET{tags:string}
  "urgent"
  "review"
```

### Inline Collections

`§LIST`, `§DICT` and `§HSET` blocks are statements: they declare a local, so they belong in a
method body. A block directly in a class body is error `Calor0100`. Where a value is needed
instead, such as a field or property default or an element of another collection, write the
collection on one line and close it with its id:

```
§FLD{List<int>:xs:pub} = §LIST{int} 1 2 3 §/LIST
§FLD{List<List<int>>:grid:pub} = §LIST{List<int>} §LIST{int} 1 §/LIST §/LIST
§PROP{Tags:HashSet<string>:pub:get} = §HSET{string} "a" "b" §/HSET
```

This compiles to `new List<int>() { 1, 2, 3 }`: the elements are added in order, as a C#
collection initializer does.

### Collection Operations

| Operation | Syntax | Description |
|:----------|:-------|:------------|
| Add to list/set | `§PUSH{coll} value` | `collection.Add(value)` |
| Set dictionary entry | `§PUT{dict} key value` | `dict[key] = value` |
| Set list index | `§SETIDX{list} idx value` | `list[index] = value` |
| Insert at index | `§C{list.Insert} §A idx §A val §/C` | `list.Insert(idx, val)` |
| Remove element | `§C{coll.Remove} §A val §/C` | `collection.Remove(val)` |
| Clear collection | `§C{coll.Clear} §/C` | `collection.Clear()` |

**Example:**
```
§PUSH{numbers} 4              // numbers.Add(4)
§PUT{ages} "charlie" 35       // ages["charlie"] = 35
§SETIDX{numbers} 0 10         // numbers[0] = 10
```

### Collection Queries

| Query | Syntax | Returns |
|:------|:-------|:--------|
| Contains element | `§HAS{coll} value` | `bool` |
| Contains key | `§HAS{dict} §KEY key` | `bool` |
| Contains value | `§HAS{dict} §VAL value` | `bool` |
| Collection count | `§CNT{coll}` | `int` |

**Example:**
```
§IF §HAS{numbers} 5
  §P "Found 5"

§B{count} §CNT{ages}
```

---

## Type Annotations in Contracts

Types matter in contracts for proper comparisons:

```
§F{Clamp:pub}
  §I{int:value}
  §I{int:min}
  §I{int:max}
  §O{int}
  §Q (<= min max)           // Requires: min <= max
  §S (>= result min)        // Ensures: result >= min
  §S (<= result max)        // Ensures: result <= max
  // ...
```

---

## Type Compatibility

| Operation | Valid Types |
|:----------|:------------|
| Arithmetic (`+`, `-`, `*`, `/`) | Numeric (`int`, `long`, `float`, `double`) |
| Modulo (`%`) | Integer (`int`, `long`) |
| Comparison (`<`, `>`, etc.) | Numeric, `string` |
| Equality (`==`, `!=`) | Any matching types |
| Logical (`&&`, `\|\|`) | `bool` |

---

## Literals

| Type | Literal Examples |
|:-----|:-----------------|
| `int` | `42`, `-17`, `0` |
| `long` | `42L`, `-17L` |
| `float` | `3.14f` |
| `double` | `3.14`, `2.718` |
| `string` | `"hello"`, `"world"` |
| `bool` | `true`, `false` |
| `char` | `'A'`, `'x'` |
| `decimal` | `DECIMAL:18.0`, `DEC:0.05`, `100m` |

---

## Next

- [Expressions](/calor/syntax-reference/expressions/) - Operators and expressions
