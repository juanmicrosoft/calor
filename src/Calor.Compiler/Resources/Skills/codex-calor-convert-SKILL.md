---
name: calor-convert
description: Convert C# code to Calor syntax with type mappings, operator conversions, and effect detection.
---

# $calor-convert - Convert C# to Calor

**Calor syntax requirements:**
- Use Lisp-style expressions: `(+ a b)`
- Use arrow syntax for conditionals: `§IF[id] condition → action`
- Use `§P` for Console.WriteLine, `§B` for variable bindings

## Semantic Guarantees

When converting C# to Calor, be aware that Calor has stricter semantics:

| C# Behavior | Calor Behavior | Action |
|-------------|----------------|--------|
| Unspecified argument order | Left-to-right (S1) | Safe to convert |
| Unchecked arithmetic | Overflow traps (S7) | Consider if overflow expected |
| Implicit narrowing | Explicit required (S8) | Add `§CAST` |
| Guard clauses | Use `§Q` contracts | Convert to preconditions |

**Always add `§SEMVER[1.0.0]` to converted modules.**

See `docs/semantics/core.md` for full specification.

## Type Mappings

| C# | Calor |
|---|---|
| `int` | `i32` |
| `long` | `i64` |
| `float` | `f32` |
| `double` | `f64` |
| `string` | `str` |
| `bool` | `bool` |
| `void` | `void` |
| `T?` | `?T` |
| `Result<T,E>` | `T!E` |
| `DateTime` | `datetime` |
| `DateTimeOffset` | `datetimeoffset` |
| `TimeSpan` | `timespan` |
| `DateOnly` | `date` |
| `TimeOnly` | `time` |
| `Guid` | `guid` |
| `IReadOnlyList<T>` | `ReadList<T>` |
| `IReadOnlyDictionary<K,V>` | `ReadDict<K,V>` |

## Operator Mappings

| C# | Calor |
|---|---|
| `a + b` | `(+ a b)` |
| `a - b` | `(- a b)` |
| `a * b` | `(* a b)` |
| `a / b` | `(/ a b)` |
| `a % b` | `(% a b)` |
| `a == b` | `(== a b)` |
| `a != b` | `(!= a b)` |
| `a < b` | `(< a b)` |
| `a > b` | `(> a b)` |
| `a <= b` | `(<= a b)` |
| `a >= b` | `(>= a b)` |
| `a && b` | `(&& a b)` |
| `a \|\| b` | `(\|\| a b)` |
| `!a` | `(! a)` |

## Structure Conversion

### Namespace → Module
```csharp
namespace MyApp { ... }
```
```calor
§M[m001:MyApp]
...
§/M[m001]
```

### Method → Function
```csharp
public static int Add(int a, int b) {
    return a + b;
}
```
```calor
§F[f001:Add:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §R (+ a b)
§/F[f001]
```

### For Loop
```csharp
for (int i = 1; i <= 100; i++) { ... }
```
```calor
§L[for1:i:1:100:1]
  ...
§/L[for1]
```

### If/Else
```csharp
if (x > 0) { DoA(); }
else if (x < 0) { DoB(); }
else { DoC(); }
```
```calor
§IF[if1] (> x 0) → §C[DoA] §/C
§EI (< x 0) → §C[DoB] §/C
§EL → §C[DoC] §/C
§/I[if1]
```

### Console.WriteLine
```csharp
Console.WriteLine("Hello");
Console.WriteLine(x);
```
```calor
§P "Hello"
§P x
```

### Variable Declaration
```csharp
var result = a + b;
```
```calor
§B[result] (+ a b)
```

## Effect Detection

Add `§E[...]` based on C# calls:

| C# Usage | Effect |
|---|---|
| `Console.Write*` | `cw` |
| `Console.Read*` | `cr` |
| `File.Write*`, `StreamWriter` | `fw` |
| `File.Read*`, `StreamReader` | `fr` |
| `HttpClient`, `WebRequest` | `net` |
| `SqlConnection`, `DbContext` | `db` |

## Contract Conversion

```csharp
// Precondition comment or ArgumentException
if (x < 0) throw new ArgumentException();
```
```calor
§Q (>= x 0)
```

```csharp
// Postcondition - Debug.Assert at end
Debug.Assert(result >= 0);
```
```calor
§S (>= result 0)
```

## C# Attribute Conversion

Attributes are preserved using inline bracket syntax `[@Attribute]`:

### Class Attributes
```csharp
[Route("api/[controller]")]
[ApiController]
public class TestController : ControllerBase { }
```
```calor
§CLASS[c001:TestController:ControllerBase][@Route("api/[controller]")][@ApiController]
§/CLASS[c001]
```

### Method Attributes
```csharp
[HttpPost]
[Authorize]
public void Post() { }
```
```calor
§METHOD[m001:Post:pub][@HttpPost][@Authorize]
§/METHOD[m001]
```

### Attribute Arguments
| C# | Calor |
|---|---|
| `[Required]` | `[@Required]` |
| `[Route("api")]` | `[@Route("api")]` |
| `[JsonProperty(PropertyName="id")]` | `[@JsonProperty(PropertyName="id")]` |
| `[Range(1, 100)]` | `[@Range(1, 100)]` |

## Supported Features

- Static methods
- Primitive types
- Basic control flow (for, if/else)
- Console I/O
- Arithmetic/comparison operators
- Simple contracts
- C# attributes (on classes, methods, properties, fields, parameters)

## Not Yet Supported

- Async/await
- LINQ
- Generics
- Events/delegates
- Exception handling (try/catch)
- Collections (List, Dictionary)

## Conversion Example

### C# Input
```csharp
namespace Calculator {
    public static class Program {
        public static void Main() {
            Console.WriteLine(Add(5, 3));
        }

        public static int Add(int a, int b) {
            if (a < 0 || b < 0)
                throw new ArgumentException("negative");
            return a + b;
        }
    }
}
```

### Calor Output
```calor
§M[m001:Calculator]
§SEMVER[1.0.0]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §C[Console.WriteLine]
    §A §C[Add] §A 5 §A 3 §/C
  §/C
§/F[f001]

§F[f002:Add:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §Q (&& (>= a 0) (>= b 0))
  §R (+ a b)
§/F[f002]
§/M[m001]
```
