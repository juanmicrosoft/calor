---
name: calor-convert
description: Convert C# code to Calor syntax with type mappings, operator conversions, and effect detection.
---

# @calor-convert - Convert C# to Calor

**Calor syntax requirements:**
- Use Lisp-style expressions: `(+ a b)`
- Use arrow syntax for conditionals: `§IF{id} condition → action`
- Use `§P` for Console.WriteLine, `§B` for variable bindings

## Semantic Guarantees

When converting C# to Calor, be aware that Calor has stricter semantics:

| C# Behavior | Calor Behavior | Action |
|-------------|----------------|--------|
| Unspecified argument order | Left-to-right (S1) | Safe to convert |
| Unchecked arithmetic | Overflow traps (S7) | Consider if overflow expected |
| Implicit narrowing | Explicit required (S8) | Add `§CAST` |
| Guard clauses | Use `§Q` contracts | Convert to preconditions |



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
| `List<T>` | `List<T>` |
| `Dictionary<K,V>` | `Dict<K,V>` |
| `HashSet<T>` | `HashSet<T>` |
| `IReadOnlyList<T>` | `ReadList<T>` |
| `IReadOnlyDictionary<K,V>` | `ReadDict<K,V>` |
| `Task<T>` | (unwrapped in async) |
| `ValueTask<T>` | (unwrapped in async) |

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
| `a ?? b` | `§?? a b` |
| `a?.b` | `§?. a b` |
| `a..b` | `§RANGE a b` |
| `^n` | `§^ n` |

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
§EN{e1:Color}
Red
Green
Blue
§/EN{e1}
```

```csharp
public enum StatusCode {
    Ok = 200,
    NotFound = 404,
    Error = 500
}
```
```calor
§EN{e1:StatusCode}
Ok = 200
NotFound = 404
Error = 500
§/EN{e1}
```

### Enum Extension Methods
```csharp
public static class ColorExtensions {
    public static string ToHex(this Color color) {
        return color switch {
            Color.Red => "#FF0000",
            Color.Green => "#00FF00",
            Color.Blue => "#0000FF",
            _ => "#000000"
        };
    }
}
```
```calor
§EEXT{ext1:Color}
§F{f1:ToHex:pub}
§I{Color:color}
§O{str}
§W{sw1} color
  §K Color.Red → §R "#FF0000"
  §K Color.Green → §R "#00FF00"
  §K Color.Blue → §R "#0000FF"
  §K _ → §R "#000000"
§/W{sw1}
§/F{f1}
§/EEXT{ext1}
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

## Async/Await Conversion

### Async Method
```csharp
public async Task<string> GetDataAsync(string url)
{
    var result = await client.GetStringAsync(url);
    return result;
}
```
```calor
§AMT{mt1:GetDataAsync:pub}
§I{str:url}
§O{str}
§B{str:result} §AWAIT §C{client.GetStringAsync} §A url §/C
§R result
§/AMT{mt1}
```

### Async Function
```csharp
public static async Task<int> ComputeAsync(int x)
{
    await Task.Delay(100);
    return x * 2;
}
```
```calor
§AF{f1:ComputeAsync:pub}
§I{i32:x}
§O{i32}
§AWAIT §C{Task.Delay} §A 100 §/C
§R (* x 2)
§/AF{f1}
```

### ConfigureAwait(false)
```csharp
var data = await GetDataAsync().ConfigureAwait(false);
```
```calor
§B{data} §AWAIT{false} §C{GetDataAsync} §/C
```

## Collection Conversion

### List Initialization
```csharp
var numbers = new List<int> { 1, 2, 3 };
numbers.Add(4);
numbers.Insert(0, 0);
numbers[1] = 10;
numbers.Remove(3);
numbers.Clear();
```
```calor
§LIST{numbers:i32}
  1
  2
  3
§/LIST{numbers}
§PUSH{numbers} 4
§INS{numbers} 0 0
§SETIDX{numbers} 1 10
§REM{numbers} 3
§CLR{numbers}
```

### Dictionary Initialization
```csharp
var ages = new Dictionary<string, int> {
    ["alice"] = 30,
    ["bob"] = 25
};
ages["charlie"] = 35;
ages.Remove("bob");
```
```calor
§DICT{ages:str:i32}
  §KV "alice" 30
  §KV "bob" 25
§/DICT{ages}
§PUT{ages} "charlie" 35
§REM{ages} "bob"
```

### HashSet Initialization
```csharp
var tags = new HashSet<string> { "urgent", "review" };
tags.Add("approved");
tags.Remove("review");
```
```calor
§HSET{tags:str}
  "urgent"
  "review"
§/HSET{tags}
§PUSH{tags} "approved"
§REM{tags} "review"
```

### Foreach
```csharp
foreach (var item in collection) { ... }
```
```calor
§EACH{e1:item:collection}
...
§/EACH{e1}
```

### Dictionary Foreach
```csharp
foreach (var kv in dict) {
    Console.WriteLine($"{kv.Key}: {kv.Value}");
}
```
```calor
§EACHKV{e1:k:v:dict}
§P k
§P v
§/EACHKV{e1}
```

## Exception Handling Conversion

### Try/Catch
```csharp
try {
    return a / b;
}
catch (DivideByZeroException ex) {
    return 0;
}
```
```calor
§TR{t1}
§R (/ a b)
§CA{DivideByZeroException:ex}
§R 0
§/TR{t1}
```

### Try/Catch/Finally
```csharp
try {
    Process();
}
catch (Exception e) {
    Log(e);
}
finally {
    Cleanup();
}
```
```calor
§TR{t1}
§C{Process} §/C
§CA{Exception:e}
§C{Log} §A e §/C
§FI
§C{Cleanup} §/C
§/TR{t1}
```

### Throw
```csharp
throw new ArgumentException("Invalid");
```
```calor
§TH "Invalid"
```

### Rethrow
```csharp
catch (Exception ex) {
    Log(ex);
    throw;
}
```
```calor
§CA{Exception:ex}
§C{Log} §A ex §/C
§RT
```

### Exception Filter (when)
```csharp
catch (Exception ex) when (ex.Message.Contains("retry"))
{
    Retry();
}
```
```calor
§CA{Exception:ex} §WHEN (C{ex.Message.Contains} §A "retry" §/C)
§C{Retry} §/C
```

## Lambda and Delegate Conversion

### Lambda Expression
```csharp
Func<int, int> doubler = x => x * 2;
```
```calor
§B{Func<i32,i32>:doubler} §LAM{x} (* x 2)
```

### Multi-parameter Lambda
```csharp
Func<int, int, int> add = (a, b) => a + b;
```
```calor
§B{Func<i32,i32,i32>:add} §LAM{a:b} (+ a b)
```

### Statement Lambda
```csharp
Action<int> printer = x => {
    Console.WriteLine(x);
};
```
```calor
§B{Action<i32>:printer} §LAM{x}
§P x
§/LAM
```

### Delegate Declaration
```csharp
public delegate int Calculator(int a, int b);
```
```calor
§DEL{d1:Calculator:pub}
§I{i32:a}
§I{i32:b}
§O{i32}
§/DEL{d1}
```

## Event Conversion

### Event Declaration
```csharp
public event EventHandler Click;
```
```calor
§EVT{evt1:Click:pub:EventHandler}
```

### Event Subscription
```csharp
button.Click += OnClick;
button.Click -= OnClick;
```
```calor
§SUB{button.Click} OnClick
§UNSUB{button.Click} OnClick
```

## Effect Detection

Add `§E{...}` based on C# calls:

| C# Usage | Effect |
|---|---|
| `Console.Write*` | `cw` |
| `Console.Read*` | `cr` |
| `File.Write*`, `StreamWriter` | `fs:w` |
| `File.Read*`, `StreamReader` | `fs:r` |
| `HttpClient`, `WebRequest` | `net:rw` |
| `SqlConnection`, `DbContext` | `db:rw` |

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
§CL{c1:TestController:ControllerBase}[@Route("api/[controller]")][@ApiController]
§/CL{c1}
```

### Method Attributes
```csharp
[HttpPost]
[Authorize]
public void Post() { }
```
```calor
§MT{m1:Post:pub}[@HttpPost][@Authorize]
§/MT{m1}
```

### Attribute Arguments
| C# | Calor |
|---|---|
| `[Required]` | `[@Required]` |
| `[Route("api")]` | `[@Route("api")]` |
| `[JsonProperty(PropertyName="id")]` | `[@JsonProperty(PropertyName="id")]` |
| `[Range(1, 100)]` | `[@Range(1, 100)]` |

## String Interpolation Conversion

```csharp
var message = $"Hello, {name}! You have {count} items.";
```
```calor
§B{str:message} §INTERP "Hello, {name}! You have {count} items." §/INTERP
```

## Supported Features

- Classes, interfaces, records, structs
- Enums (with optional explicit values and extension methods)
- Methods, properties, fields, constructors
- Control flow (for, foreach, while, do-while, if/else, switch)
- Try/catch/finally, throw, exception filters
- Async/await with ConfigureAwait support
- Lambdas and delegates
- Events (declaration, subscribe, unsubscribe)
- Collections (List, Dictionary, HashSet with operations)
- Generics with constraints
- Pattern matching (type, property, positional, relational)
- String interpolation
- Null-conditional and null-coalescing operators
- Range and index-from-end operators
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
