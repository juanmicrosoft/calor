---
layout: default
title: Structure Tags
parent: Syntax Reference
nav_order: 1
---

# Structure Tags

Structure tags define the organization of Calor code: modules, functions, and their boundaries.

> **Indent-only block structure.** Calor uses Python-style significant
> indentation: a block opens with `§M`, `§F`, `§CL`, `§L`, `§IF`, … and
> ends at the first line that **dedents** back to the parent column. No
> closing tags are required. The compiler default is `2` spaces per
> nesting level (mixing tabs and spaces is rejected). Legacy closer tags
> (`§/M`, `§/F`, `§/L`, `§/I`, …) are still accepted during the
> transition window, but all examples below use the indent-only form;
> bulk-migrate older corpora with
> [`calor format`](/calor/cli/format/).

---

## Modules

Modules are like C# namespaces. They group related functions.

### Syntax

```
§M{name}
  // contents
```

### Example

```
§M{Calculator}
  // functions go here
```

### Rules

- `name` becomes the C# namespace
- The module block extends until the next line that dedents back to
  column 0 (or end of file)
- Legacy `§/M` closers are still accepted but discouraged; migrate
  with [`calor format`](/calor/cli/format/)

---

## Functions

Functions are the primary code containers.

### Syntax

```
§F{name:visibility}
  §I{type:param}       // inputs (0 or more)
  §O{type}             // output (required)
  §E{effects}          // effects (optional)
  §Q condition         // preconditions (0 or more)
  §S condition         // postconditions (0 or more)
  // body
```

### Visibility

| Value | Meaning | C# Equivalent |
|:------|:--------|:--------------|
| `pub` | Public | `public static` |
| `pri` | Private | `private static` |

### Examples

**Simple function:**
```
§F{Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
```

**Function with effects:**
```
§F{PrintSum:pub}
  §I{i32:a}
  §I{i32:b}
  §O{void}
  §E{cw}
  §P (+ a b)
```

**Function with contracts:**
```
§F{Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
```

---

## Async Functions

Async functions use `§AF` instead of `§F` and automatically wrap return types in `Task<T>`.

### Syntax

```
§AF{name:visibility}
  §I{type:param}       // inputs (0 or more)
  §O{type}             // output (auto-wrapped to Task<T>)
  // body with §AWAIT expressions
```

### Examples

**Simple async function:**
```
§AF{FetchDataAsync:pub}
  §I{str:url}
  §O{str}
  §B{result} §AWAIT §C{httpClient.GetStringAsync} §A url §/C
  §R result
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
§AF{ProcessAsync:pub}
  §O{void}
  §AWAIT §C{Task.Delay} §A 1000 §/C
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
```

### Example

```
§CL{c001:DataService:pub}
  §AMT{mt001:GetUserAsync:pub}
    §I{i32:id}
    §O{User}
    §B{user} §AWAIT §C{_repository.FindAsync} §A id §/C
    §R user
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

§R §AWAIT §C{ComputeAsync} §A x §/C
```

---

## Lambda Expressions

Lambda expressions create anonymous functions using `§LAM`/`§/LAM` blocks.

### Syntax

```
§LAM{id:param1:type1}              // single parameter
§LAM{id:param1:type1:param2:type2} // multiple parameters
§LAM{id}                           // no parameters
```

The header encodes parameters as colon-separated `name:type` pairs after the ID.
Parameters without known types use `object` as the default type.

### Single Parameter

```
§B{doubler} §LAM{lam1:x:i32} (* x 2) §/LAM{lam1}
```

Emits: `var doubler = (int x) => x * 2;`

### Multiple Parameters

Parameters are listed as consecutive `name:type` pairs:

```
§B{add} §LAM{lam1:a:i32:b:i32} (+ a b) §/LAM{lam1}
```

Emits: `var add = (int a, int b) => a + b;`

**Three parameters:**
```
§B{combine} §LAM{lam1:x:i32:y:str:z:bool} §C{Process} §A x §A y §A z §/C §/LAM{lam1}
```

### No Parameters

```
§B{getTime} §LAM{lam1} §C{DateTime.Now} §/C §/LAM{lam1}
```

### As LINQ Arguments

Lambdas are commonly used as arguments inside `§C{...}` calls:

```
§C{numbers.Where} §A §LAM{lam1:n:i32} (!= (% n 3) 0) §/LAM{lam1} §/C
§C{items.Select} §A §LAM{lam2:x:i32} (* x 2) §/LAM{lam2} §/C
```

### Statement Body Lambdas

For multi-statement bodies, include statements between the tags:

```
§B{printer} §LAM{lam1:x:i32}
§P x
§P (* x 2)
§/LAM{lam1}
```

### Async Lambdas

Add `async` after the ID, before parameters:

```
§LAM{lam1:async:x:i32}
§B{result} §AWAIT §C{ProcessAsync} §A x §/C
§R result
§/LAM{lam1}
```

> **Note:** `§I{type:name}` inline parameter declarations are NOT supported inside
> `§LAM` headers. All parameters must be encoded in the header using the
> `name:type` pair format.

---

## Delegate Definitions

Delegates define function signatures that can be passed as values.

### Syntax

```
§DEL{id:name}
  §I{type:param}       // parameters (0 or more)
  §O{type}             // return type (optional for void)
  §E{effects}          // effects (optional)
```

### Examples

**Calculator delegate:**
```
§DEL{d001:Calculator}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
```

Emits: `public delegate int Calculator(int a, int b);`

**Void delegate:**
```
§DEL{d001:Logger}
  §I{str:message}
```

Emits: `public delegate void Logger(string message);`

**Delegate with effects:**
```
§DEL{d001:FileProcessor}
  §I{str:path}
  §O{bool}
  §E{fs:rw}
```

---

## Enum Definitions

Enums define a set of named constants.

### Syntax

```
§EN{id:Name}
  Member1
  Member2 = 1
  Member3 = 2
```

With underlying type:
```
§EN{id:Name:underlyingType}
  ...
```

### Examples

**Simple enum:**
```
§EN{e001:Color}
  Red
  Green
  Blue
```

Emits:
```csharp
public enum Color
{
    Red,
    Green,
    Blue,
}
```

**Enum with explicit values:**
```
§EN{e001:StatusCode}
  Ok = 200
  NotFound = 404
  Error = 500
```

**Enum with underlying type:**
```
§EN{e001:Flags:u8}
  None = 0
  Read = 1
  Write = 2
```

Emits:
```csharp
public enum Flags : byte
{
    None = 0,
    Read = 1,
    Write = 2,
}
```

---

## Enum Extension Methods

Extension methods can be added to enums using `§EEXT` (Enum EXTension).

### Syntax

```
§EEXT{id:EnumName}
  §F{f001:MethodName:pub}
    §I{EnumName:self}
    §O{returnType}
    // body using self
```

The first parameter with the enum type (or named `self`) becomes the `this` parameter.

### Example

```
§EN{e001:Color}
  Red
  Green
  Blue

§EEXT{ext001:Color}
  §F{f001:ToHex:pub}
    §I{Color:self}
    §O{str}
    §W{sw1} self
    §K Color.Red → §R "#FF0000"
    §K Color.Green → §R "#00FF00"
    §K Color.Blue → §R "#0000FF"

  §F{f002:IsPrimary:pub}
    §I{Color:self}
    §O{bool}
    §R (|| (== self Color.Red) (|| (== self Color.Green) (== self Color.Blue)))
```

Emits:
```csharp
public enum Color
{
    Red,
    Green,
    Blue,
}

public static class ColorExtensions
{
    public static string ToHex(this Color self)
    {
        return self switch
        {
            Color.Red => "#FF0000",
            Color.Green => "#00FF00",
            Color.Blue => "#0000FF",
            _ => throw new ArgumentOutOfRangeException(nameof(self))
        };
    }

    public static bool IsPrimary(this Color self)
    {
        return self == Color.Red || self == Color.Green || self == Color.Blue;
    }
}
```

---

## Event Definitions

Events allow objects to notify subscribers of state changes.

### Syntax

```
§EVT{id:name:visibility:delegateType}
```

| Part | Description |
|:-----|:------------|
| `id` | Unique identifier |
| `name` | Event name |
| `visibility` | `pub`, `pri`, `prot` |
| `delegateType` | Delegate type for handlers |

### Example

```
§CL{c001:Button:pub}
  §EVT{e001:Click:pub:EventHandler}
    §EVT{e002:ValueChanged:pub:EventHandler<ValueChangedEventArgs>}
```

Emits:
```csharp
public class Button
{
    public event EventHandler Click;
    public event EventHandler<ValueChangedEventArgs> ValueChanged;
}
```

---

## Event Subscribe/Unsubscribe

Use `§SUB` and `§UNSUB` to add or remove event handlers.

### Syntax

```
§SUB eventRef handlerRef      // Subscribe
§UNSUB eventRef handlerRef    // Unsubscribe
```

### Examples

**Subscribe to event:**
```
§SUB button.Click OnButtonClick
```

Emits: `button.Click += OnButtonClick;`

**Unsubscribe from event:**
```
§UNSUB button.Click OnButtonClick
```

Emits: `button.Click -= OnButtonClick;`

**Subscribe with lambda:**
```
§SUB button.Click (sender, e) → §P "Clicked!"
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
§I{[u8}:data}       // byte[] data
§I{[str}:args}      // string[] args
```

### Multiple Parameters

```
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §I{i32:c}
  §O{i32}
  §R (+ (+ a b) c)
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
§O{[u8}}     // returns byte[]
§O{[str}}    // returns string[]
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
  §FLD{[u8}:_buffer:priv}       // private byte[] _buffer
  §FLD{[i32}:_indices:priv}     // private int[] _indices

  §MT{m001:ProcessData:pub}
    §I{[str}:args}              // string[] args parameter
    §O{i32}
    §R args.Length
```

---

## Block Structure (Indentation)

Calor blocks are delimited by **indentation**, like Python. A block
opens with one of the structural tags (`§M`, `§F`, `§AF`, `§MT`,
`§AMT`, `§CL`, `§IFACE`, `§L`, `§WH`, `§DO`, `§IF`, `§W`, `§TR`,
`§LAM`, `§DEL`, `§EN`, `§EEXT`, `§PROP`) and ends at the next line that
dedents back to (or past) the parent column.

### Rules

1. The compiler default is **2 spaces per nesting level**
2. Tabs and spaces must not be mixed within a block
3. Chain continuations (`§EI`, `§EL`, `§K`, `§WHEN`, `§CA`, `§FI`)
   sit at the **same** column as their parent (`§IF`, `§W`, `§TR`),
   not indented inside it
4. Legacy `§/X` closers are still accepted and ignored

### Example

```
§M{Example}
  §F{Main:pub}
    §O{void}
    §E{cw}
    §L{i:1:10:1}
      §IF (> i 5)
        §P i
      §EL
        §P "small"
```

The `§/I`, `§/L`, `§/F`, `§/M` closers are inferred from the dedents.
If you write them explicitly, the lexer drops them silently — but
calorfmt will strip them in a future release.

### Tag Reference

| Opener | Purpose |
|:-------|:--------|
| `§M{name}` | Module |
| `§F{name:vis}` | Function |
| `§AF{name:vis}` | Async function |
| `§CL{name}` | Class |
| `§IFACE{name}` | Interface |
| `§MT{name:vis}` | Method |
| `§AMT{name:vis}` | Async method |
| `§L{var:from:to:step}` | For loop |
| `§WH cond` | While loop |
| `§DO` | Do-while loop (condition on closing line during transition) |
| `§IF cond` | Conditional (with `§EI` / `§EL` continuations) |
| `§W expr` | Switch (with `§K` cases) |
| `§TR` | Try (with `§CA` / `§FI`) |
| `§LAM{params}` | Lambda (block body) |
| `§DEL{name}` | Delegate definition |
| `§EN{name}` | Enum |
| `§EEXT{enumName}` | Enum extension methods |
| `§PROP{name:type:vis}` | Property |
| `§EVT{name:vis:type}` | Event (single line, no block) |
| `§C{target}` | Call expression (`§/C` ends the argument list) |

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

  §MT{m002:GetAll:pub}
    §O{IReadOnlyList<T>}
    §R _items
```

**Generic interface:**
```
§IFACE{i001:IRepository}<T>
  §WHERE T : class
  §MT{m001:Get}
    §I{i32:id}
    §O{T}
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
```

**Property with validation:**
```
§PROP{p001:Email:str:pub}[@Required][@EmailAddress]
  §GET
    §SET
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

## Why Indent-Based Blocks?

1. **Familiar to agents** - LLMs trained on Python have strong priors for indentation
2. **Tag-light** - No `§/X` to type, no mismatched-closer errors
3. **Refactoring safe** - Stable IDs on openers still survive code movement
4. **Lower edit cost** - In edit-workload studies, indent form reduced agent token cost by ~16% with no regression in correctness

---

## Next

- [Types](/calor/syntax-reference/types/) - Type system reference
