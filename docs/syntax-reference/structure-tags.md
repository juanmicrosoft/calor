---
layout: default
title: Structure Tags
parent: Syntax Reference
nav_order: 1
---

# Structure Tags

Structure tags define the organization of Calor code: modules, functions, and their boundaries.

---

## Modules

Modules are like C# namespaces. They group related functions.

### Syntax

```
§M{id:name}
  // contents
§/M{id}
```

### Example

```
§M{m001:Calculator}
  // functions go here
§/M{m001}
```

### Rules

- `id` must be unique within the file
- `name` becomes the C# namespace
- Every `§M` must have a matching `§/M` with the same ID

---

## Functions

Functions are the primary code containers.

### Syntax

```
§F{id:name:visibility}
  §I{type:param}       // inputs (0 or more)
  §O{type}             // output (required)
  §E{effects}          // effects (optional)
  §Q condition         // preconditions (0 or more)
  §S condition         // postconditions (0 or more)
  // body
§/F{id}
```

### Visibility

| Value | Meaning | C# Equivalent |
|:------|:--------|:--------------|
| `pub` | Public | `public static` |
| `pri` | Private | `private static` |

### Examples

**Simple function:**
```
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
```

**Function with effects:**
```
§F{f001:PrintSum:pub}
  §I{i32:a}
  §I{i32:b}
  §O{void}
  §E{cw}
  §P (+ a b)
§/F{f001}
```

**Function with contracts:**
```
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
§/F{f001}
```

---

## Async Functions

Async functions use `§AF` instead of `§F` and automatically wrap return types in `Task<T>`.

### Syntax

```
§AF{id:name:visibility}
  §I{type:param}       // inputs (0 or more)
  §O{type}             // output (auto-wrapped to Task<T>)
  // body with §AWAIT expressions
§/AF{id}
```

### Examples

**Simple async function:**
```
§AF{f001:FetchDataAsync:pub}
  §I{str:url}
  §O{str}
  §B{result} §AWAIT §C{httpClient.GetStringAsync} §A url §/C
  §R result
§/AF{f001}
```

Emits C#:
```csharp
public static async Task<string> FetchDataAsync(string url)
{
    var result = await httpClient.GetStringAsync(url);
    return result;
}
```

**Async void function (returns Task):**
```
§AF{f001:ProcessAsync:pub}
  §O{void}
  §AWAIT §C{Task.Delay} §A 1000 §/C
§/AF{f001}
```

### Automatic Task Wrapping

| Declared Output | Emitted Return Type |
|:----------------|:--------------------|
| `§O{void}` | `Task` |
| `§O{i32}` | `Task<int>` |
| `§O{str}` | `Task<string>` |
| `§O{Task<i32>}` | `Task<int>` (no double-wrap) |

---

## Async Methods

Async methods in classes use `§AMT` instead of `§MT`.

### Syntax

```
§AMT{id:name:visibility}
  §I{type:param}
  §O{type}
  // body
§/AMT{id}
```

### Example

```
§CL{c001:DataService:pub}
  §AMT{mt001:GetUserAsync:pub}
    §I{i32:id}
    §O{User}
    §B{user} §AWAIT §C{_repository.FindAsync} §A id §/C
    §R user
  §/AMT{mt001}
§/CL{c001}
```

### Modifiers

Async methods support the same modifiers as regular methods:

```
§AMT{mt001:ProcessAsync:pub:virt}    // public virtual async
§AMT{mt002:HandleAsync:prot:ovr}     // protected override async
§AMT{mt003:ComputeAsync:pub:stat}    // public static async
```

---

## Await Expression

Use `§AWAIT` to await async operations.

### Syntax

```
§AWAIT expression                    // Simple await
§AWAIT{false} expression             // await with ConfigureAwait(false)
§AWAIT{true} expression              // await with ConfigureAwait(true)
```

### Examples

**Simple await:**
```
§B{data} §AWAIT §C{client.GetAsync} §A url §/C
```

**With ConfigureAwait(false) for library code:**
```
§B{data} §AWAIT{false} §C{client.GetAsync} §A url §/C
```

Emits: `var data = await client.GetAsync(url).ConfigureAwait(false);`

### Using Await in Conditions and Expressions

```
§IF{if1} §AWAIT §C{IsValidAsync} §A id §/C
  §P "Valid"
§/I{if1}

§R §AWAIT §C{ComputeAsync} §A x §/C
```

---

## Input Parameters

Input parameters define function arguments.

### Syntax

```
§I{type:name}
```

### Examples

```
§I{i32:x}           // int x
§I{str:name}        // string name
§I{bool:flag}       // bool flag
§I{?i32:maybeVal}   // int? maybeVal (nullable)
§I{[u8]:data}       // byte[] data
§I{[str]:args}      // string[] args
```

### Multiple Parameters

```
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §I{i32:c}
  §O{i32}
  §R (+ (+ a b) c)
§/F{f001}
```

---

## Output Type

Every function must declare its output type.

### Syntax

```
§O{type}
```

### Examples

```
§O{void}     // returns nothing
§O{i32}      // returns int
§O{str}      // returns string
§O{?i32}     // returns nullable int
§O{i32!str}  // returns Result<int, string>
§O{[u8]}     // returns byte[]
§O{[str]}    // returns string[]
```

---

## Array Types

Calor uses bracket notation `[T]` for array types, which aligns with common programming language conventions.

### Syntax

```
[elementType]         // Single-dimensional array
[[elementType]]       // Jagged array (array of arrays)
```

### Examples

| Calor Type | C# Equivalent |
|:----------|:--------------|
| `[u8]` | `byte[]` |
| `[i32]` | `int[]` |
| `[str]` | `string[]` |
| `[bool]` | `bool[]` |
| `[[i32]]` | `int[][]` |

### Usage in Fields and Methods

```
§CL{c001:DataProcessor}
  §FLD{[u8]:_buffer:priv}       // private byte[] _buffer
  §FLD{[i32]:_indices:priv}     // private int[] _indices

  §MT{m001:ProcessData:pub}
    §I{[str]:args}              // string[] args parameter
    §O{i32}
    §R args.Length
  §/MT{m001}
§/CL{c001}
```

---

## Closing Tags

Every structural element must be closed with a matching tag.

### Rules

1. Opening `§X{id:...}` must have closing `§/X{id}`
2. IDs must match exactly
3. Nesting must be proper (no overlapping scopes)

### Correct Nesting

```
§M{m001:Example}
  §F{f001:Main:pub}
    §L{for1:i:1:10:1}
      §IF{if1} (> i 5)
        §P i
      §/I{if1}
    §/L{for1}
  §/F{f001}
§/M{m001}
```

### Incorrect (Overlapping)

```
// WRONG: if1 closed after for1
§L{for1:i:1:10:1}
  §IF{if1} (> i 5)
§/L{for1}
  §/I{if1}     // Error: if1 overlaps for1
```

---

## Tag Reference

| Opening | Closing | Purpose |
|:--------|:--------|:--------|
| `§M{id:name}` | `§/M{id}` | Module |
| `§F{id:name:vis}` | `§/F{id}` | Function |
| `§AF{id:name:vis}` | `§/AF{id}` | Async function |
| `§MT{id:name:vis}` | `§/MT{id}` | Method |
| `§AMT{id:name:vis}` | `§/AMT{id}` | Async method |
| `§L{id:var:from:to:step}` | `§/L{id}` | For loop |
| `§WH{id} cond` | `§/WH{id}` | While loop |
| `§DO{id}` | `§/DO{id} cond` | Do-while loop |
| `§IF{id} cond` | `§/I{id}` | Conditional |
| `§C{target}` | `§/C` | Call (no ID needed) |

---

## Generics

Calor supports generic functions, classes, interfaces, and methods using angle bracket syntax.

### Type Parameters

Type parameters are declared using `<T>` suffix syntax after the tag attributes.

```
§F{id:name:vis}<T>         // Generic function with one type parameter
§F{id:name:vis}<T, U>      // Generic function with two type parameters
§CL{id:name}<T>            // Generic class
§IFACE{id:name}<T>         // Generic interface
§MT{id:name:vis}<T>        // Generic method
```

### Constraints

Type parameter constraints are declared using `§WHERE` clauses.

**New syntax (recommended):**
```
§WHERE T : class                    // T must be a reference type
§WHERE T : struct                   // T must be a value type
§WHERE T : new()                    // T must have parameterless constructor
§WHERE T : IComparable<T>           // T must implement interface
§WHERE T : class, IComparable<T>    // Multiple constraints
```

**Legacy syntax (still supported):**
```
§WR{T:class}                        // T must be a reference type
§WR{T:IComparable}                  // T must implement interface
```

### Generic Type References

Generic types are written inline using angle bracket syntax.

```
§I{List<T>:items}                   // Parameter of type List<T>
§I{Dictionary<str, T>:lookup}       // Nested generic types
§O{IEnumerable<T>}                  // Generic return type
§FLD{List<T>:_items:pri}            // Generic field type
```

### Examples

**Generic identity function:**
```
§F{f001:Identity:pub}<T>
  §I{T:value}
  §O{T}
  §R value
§/F{f001}
```

**Generic class with constraint:**
```
§CL{c001:Repository:pub}<T>
  §WHERE T : class
  §FLD{List<T>:_items:pri}

  §MT{m001:Add:pub}
    §I{T:item}
    §O{void}
    §C{_items.Add} §A item §/C
  §/MT{m001}

  §MT{m002:GetAll:pub}
    §O{IReadOnlyList<T>}
    §R _items
  §/MT{m002}
§/CL{c001}
```

**Generic interface:**
```
§IFACE{i001:IRepository}<T>
  §WHERE T : class
  §MT{m001:Get}
    §I{i32:id}
    §O{T}
  §/MT{m001}
§/IFACE{i001}
```

---

## C# Attributes

C# attributes are preserved during conversion using inline bracket syntax `[@Attribute]`.

### Syntax

```
§CL{id:name}[@AttributeName]
§CL{id:name}[@AttributeName(args)]
§MT{id:name:vis}[@Attr1][@Attr2]
```

### Examples

**Class with routing attributes (ASP.NET Core):**
```
§CL{c001:JoinController:ControllerBase}[@Route("api/[controller]")][@ApiController]
  §MT{m001:Post:pub}[@HttpPost]
  §/MT{m001}
§/CL{c001}
```

**Property with validation:**
```
§PROP{p001:Email:str:pub}[@Required][@EmailAddress]
  §GET
  §SET
§/PROP{p001}
```

### Attribute Arguments

| Style | Syntax | Example |
|:------|:-------|:--------|
| No args | `[@Name]` | `[@ApiController]` |
| Positional | `[@Name(value)]` | `[@Route("api/test")]` |
| Named | `[@Name(Key="value")]` | `[@JsonProperty(PropertyName="id")]` |
| Mixed | `[@Name(pos, Key=val)]` | `[@Range(1, 100, ErrorMessage="Invalid")]` |

### Supported Elements

Attributes can be attached to:
- Classes: `§CL{...}[@attr]`
- Interfaces: `§IFACE{...}[@attr]`
- Methods: `§MT{...}[@attr]`
- Properties: `§PROP{...}[@attr]`
- Fields: `§FLD{...}[@attr]`
- Parameters: `§I{type:name}[@attr]`

---

## Why Explicit Closing Tags?

1. **Unambiguous parsing** - No brace-counting needed
2. **ID verification** - Compiler catches mismatches
3. **Agent-friendly** - Easy to identify scope boundaries
4. **Refactoring safe** - IDs survive code movement

---

## Next

- [Types](/calor/syntax-reference/types/) - Type system reference
