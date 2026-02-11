---
layout: default
title: Types
parent: Syntax Reference
nav_order: 2
---

# Types

Calor has a simple type system with primitives, optionals, results, and arrays.

---

## Primitive Types

| Calor | Description | C# | Range |
|:-----|:------------|:---|:------|
| `i32` | 32-bit signed integer | `int` | -2³¹ to 2³¹-1 |
| `i64` | 64-bit signed integer | `long` | -2⁶³ to 2⁶³-1 |
| `u8` | 8-bit unsigned integer | `byte` | 0 to 255 |
| `u16` | 16-bit unsigned integer | `ushort` | 0 to 65535 |
| `u32` | 32-bit unsigned integer | `uint` | 0 to 2³²-1 |
| `u64` | 64-bit unsigned integer | `ulong` | 0 to 2⁶⁴-1 |
| `f32` | 32-bit floating point | `float` | ±3.4 × 10³⁸ |
| `f64` | 64-bit floating point | `double` | ±1.8 × 10³⁰⁸ |
| `str` | String | `string` | UTF-16 text |
| `bool` | Boolean | `bool` | `true` or `false` |
| `void` | No value | `void` | (return type only) |

---

## Usage in Declarations

### Input Parameters

```
§I{i32:count}       // int count
§I{str:name}        // string name
§I{f64:price}       // double price
§I{bool:active}     // bool active
§I{[u8]:data}       // byte[] data
§I{[str]:args}      // string[] args
```

### Output Types

```
§O{i32}             // returns int
§O{str}             // returns string
§O{void}            // returns nothing
§O{[u8]}            // returns byte[]
```

---

## Array Types

Calor uses bracket notation `[T]` for array types, which aligns with common programming language conventions.

### Syntax

```
[T]                 // Array of T
[[T]]               // Jagged array (array of arrays)
```

### Examples

| Calor Type | C# Equivalent |
|:----------|:--------------|
| `[u8]` | `byte[]` |
| `[i32]` | `int[]` |
| `[str]` | `string[]` |
| `[bool]` | `bool[]` |
| `[[i32]]` | `int[][]` |

### Usage

```
§FLD{[u8]:_buffer:priv}       // private byte[] field
§I{[str]:args}                // string[] parameter
§O{[i32]}                     // returns int[]
```

---

## Option Type (`?T`)

Options represent values that may be absent.

### Syntax

```
?T                  // Option of T (may be null)
```

### Examples

```
§F{f001:Find:pub}
  §I{str:key}
  §O{?str}              // might return a string, might return nothing
  // ...
§/F{f001}

§F{f002:Process:pub}
  §I{?i32:maybeValue}   // accepts null
  §O{void}
  // ...
§/F{f002}
```

### Creating Option Values

```
§SM value           // Some(value) - has a value
§NN                 // None - no value
```

### Example

```
§F{f001:FindUser:pub}
  §I{i32:id}
  §O{?User}
  §IF{if1} (== id 0)
    §R §NN
  §EL
    §R §SM user
  §/I{if1}
§/F{f001}
```

---

## Result Type (`T!E`)

Results represent computations that may fail.

### Syntax

```
T!E                 // Result: either T (success) or E (error)
```

### Examples

```
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32!str}           // returns int on success, string error on failure
  §IF{if1} (== b 0)
    §R §ERR "Division by zero"
  §EL
    §R §OK (/ a b)
  §/I{if1}
§/F{f001}
```

### Creating Result Values

```
§OK value           // Ok(value) - success
§ERR "message"      // Err(message) - failure
```

### Common Patterns

**Parse integer:**
```
§F{f001:ParseInt:pub}
  §I{str:text}
  §O{i32!str}
  // ...implementation
§/F{f001}
```

**File read:**
```
§F{f001:ReadFile:pub}
  §I{str:path}
  §O{str!str}
  §E{fs:r}
  // ...implementation
§/F{f001}
```

---

## Generic Types

Generic types use angle bracket syntax inline.

### Syntax

```
List<T>                 // List of T
Dictionary<K, V>        // Dictionary with key K and value V
IEnumerable<T>          // Enumerable of T
Func<T, U>              // Function from T to U
```

### Examples

```
§I{List<i32>:numbers}           // List<int> parameter
§I{Dictionary<str, i32>:scores} // Dictionary<string, int>
§O{IEnumerable<str>}            // Returns IEnumerable<string>
§FLD{HashSet<T>:_items:pri}     // Generic field with type parameter T
```

### Nested Generic Types

Generic types can be nested.

```
§I{Dictionary<str, List<i32>>:data}     // Dictionary<string, List<int>>
§O{List<Tuple<str, i32>>}               // List<(string, int)>
```

### Type Parameters

When defining generic functions or classes, type parameters (like `T`, `U`) can be used as types.

```
§F{f001:First:pub}<T>
  §I{List<T>:items}
  §O{T}
  §R items[0]
§/F{f001}
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
§/LIST{name}
```

| Part | Description |
|:-----|:------------|
| `name` | Variable name for the list |
| `elementType` | Type of elements (`i32`, `str`, etc.) |

**Example:**
```
§LIST{numbers:i32}
  1
  2
  3
§/LIST{numbers}
```

### Dictionary Creation

```
§DICT{name:keyType:valueType}
  §KV key1 value1
  §KV key2 value2
§/DICT{name}
```

| Part | Description |
|:-----|:------------|
| `name` | Variable name for the dictionary |
| `keyType` | Type of keys |
| `valueType` | Type of values |
| `§KV` | Key-value pair entry |

**Example:**
```
§DICT{ages:str:i32}
  §KV "alice" 30
  §KV "bob" 25
§/DICT{ages}
```

### HashSet Creation

```
§HSET{name:elementType}
  element1
  element2
§/HSET{name}
```

**Example:**
```
§HSET{tags:str}
  "urgent"
  "review"
§/HSET{tags}
```

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
| Collection count | `§CNT{coll}` | `i32` |

**Example:**
```
§IF{if1} §HAS{numbers} 5 → §P "Found 5"
§/I{if1}

§B{count} §CNT{ages}
```

---

## Type Annotations in Contracts

Types matter in contracts for proper comparisons:

```
§F{f001:Clamp:pub}
  §I{i32:value}
  §I{i32:min}
  §I{i32:max}
  §O{i32}
  §Q (<= min max)           // Requires: min <= max
  §S (>= result min)        // Ensures: result >= min
  §S (<= result max)        // Ensures: result <= max
  // ...
§/F{f001}
```

---

## Type Compatibility

| Operation | Valid Types |
|:----------|:------------|
| Arithmetic (`+`, `-`, `*`, `/`) | Numeric (`i32`, `i64`, `f32`, `f64`) |
| Modulo (`%`) | Integer (`i32`, `i64`) |
| Comparison (`<`, `>`, etc.) | Numeric, `str` |
| Equality (`==`, `!=`) | Any matching types |
| Logical (`&&`, `\|\|`) | `bool` |

---

## Literals

| Type | Literal Examples |
|:-----|:-----------------|
| `i32` | `42`, `-17`, `0` |
| `i64` | `42L`, `-17L` |
| `f32` | `3.14f` |
| `f64` | `3.14`, `2.718` |
| `str` | `"hello"`, `"world"` |
| `bool` | `true`, `false` |

---

## Next

- [Expressions](/calor/syntax-reference/expressions/) - Operators and expressions
