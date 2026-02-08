# /calor-convert - Convert C# to Calor

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

## Agent-Optimized Format Rules

Calor uses a compact format optimized for AI agents:

| Rule | Why it helps agents | Example |
|------|---------------------|---------|
| No indentation | Indentation has no semantic value for agents | `§L{l1:i:1:10:1}` not `  §L{l1:i:1:10:1}` |
| One statement per line | Clean diffs, targeted edits | Each `§X` on its own line |
| No blank lines | Reduces token count without losing structure | No empty lines between statements |
| Abbreviated IDs | Padding adds no value | `§M{m1:Name}` not `§M{m001:Name}` |
| Keep expression spaces | Helps agents parse tokens | `(+ a b)` not `(+a b)` |
| No trailing whitespace | Reduces noise in diffs | Lines end at last meaningful char |

**ID Abbreviation Rules:**
- `m001` → `m1` (module)
- `f001` → `f1` (function)
- `for1` → `l1` (loop)
- `if1` → `i1` (conditional)
- `while1` → `w1` (while loop)
- `do1` → `d1` (do-while loop)

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
§M{m1:MyApp}
...
§/M{m1}
```

### Method → Function
```csharp
public static int Add(int a, int b) {
    return a + b;
}
```
```calor
§F{f1:Add:pub}
§I{i32:a}
§I{i32:b}
§O{i32}
§R (+ a b)
§/F{f1}
```

### Enum
```csharp
public enum Color { Red, Green, Blue }
```
```calor
§ENUM{e1:Color}
Red
Green
Blue
§/ENUM{e1}
```

```csharp
public enum StatusCode {
    Ok = 200,
    NotFound = 404,
    Error = 500
}
```
```calor
§ENUM{e1:StatusCode}
Ok = 200
NotFound = 404
Error = 500
§/ENUM{e1}
```

### For Loop
```csharp
for (int i = 1; i <= 100; i++) { ... }
```
```calor
§L{l1:i:1:100:1}
...
§/L{l1}
```

### If/Else
```csharp
if (x > 0) { DoA(); }
else if (x < 0) { DoB(); }
else { DoC(); }
```
```calor
§IF{i1} (> x 0) → §C{DoA} §/C
§EI (< x 0) → §C{DoB} §/C
§EL → §C{DoC} §/C
§/I{i1}
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
§B{result} (+ a b)
```

## Effect Detection

Add `§E{...}` based on C# calls:

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
§CLASS{c1:TestController:ControllerBase}[@Route("api/[controller]")][@ApiController]
§/CLASS{c1}
```

### Method Attributes
```csharp
[HttpPost]
[Authorize]
public void Post() { }
```
```calor
§METHOD{m1:Post:pub}[@HttpPost][@Authorize]
§/METHOD{m1}
```

### Attribute Arguments
| C# | Calor |
|---|---|
| `[Required]` | `[@Required]` |
| `[Route("api")]` | `[@Route("api")]` |
| `[JsonProperty(PropertyName="id")]` | `[@JsonProperty(PropertyName="id")]` |
| `[Range(1, 100)]` | `[@Range(1, 100)]` |

## Supported Features

- Classes, interfaces, records, structs
- Enums (with optional explicit values)
- Methods, properties, fields, constructors
- Control flow (for, foreach, while, do-while, if/else, switch)
- Try/catch/finally, throw
- Async/await
- Lambdas and delegates
- Generics with constraints
- Pattern matching (basic patterns)
- String interpolation
- Null-conditional and null-coalescing operators
- C# attributes (on classes, methods, properties, fields, parameters)
- Contracts (preconditions, postconditions)

## Partially Supported

- LINQ (method syntax works, query syntax may need review)
- ref/out parameters (kept as-is with warning)

## Not Yet Supported

- Unsafe code and pointers
- goto/labeled statements

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
§M{m1:Calculator}
§SEMVER[1.0.0]
§F{f1:Main:pub}
§O{void}
§E{cw}
§C{Console.WriteLine}
§A §C{Add} §A 5 §A 3 §/C
§/C
§/F{f1}
§F{f2:Add:pub}
§I{i32:a}
§I{i32:b}
§O{i32}
§Q (&& (>= a 0) (>= b 0))
§R (+ a b)
§/F{f2}
§/M{m1}
```

## ID Integrity Rules

### Canonical IDs (Production Code)
```
f_01J5X7K9M2NPQRSTABWXYZ12    Function
m_01J5X7K9M2NPQRSTABWXYZ12    Module
c_01J5X7K9M2NPQRSTABWXYZ12    Class
mt_01J5X7K9M2NPQRSTABWXYZ12   Method
ctor_01J5X7K9M2NPQRSTABWXYZ12 Constructor
p_01J5X7K9M2NPQRSTABWXYZ12    Property
i_01J5X7K9M2NPQRSTABWXYZ12    Interface
e_01J5X7K9M2NPQRSTABWXYZ12    Enum
```

### Test IDs (ONLY in tests/, docs/, examples/)
```
f001, m001, c001              Sequential test IDs
```

### Agent Rules - CRITICAL
1. **NEVER** modify an existing ID
2. **NEVER** copy IDs when extracting code
3. **OMIT** IDs for new declarations - run `calor ids assign`
4. **VERIFY** before commit: `calor ids check`

### Preservation Rules
| Operation | ID Behavior |
|-----------|-------------|
| Rename | PRESERVE |
| Move file | PRESERVE |
| Reformat | PRESERVE |
| Extract helper | NEW ID |

### Verification Steps
```bash
calor ids check .
calor ids assign . --dry-run
```
