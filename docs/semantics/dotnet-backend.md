# .NET Backend Specification

Version: 1.0.0

This document specifies how the .NET backend must implement Calor semantics. The backend **conforms to** Calor semantics; it does not **define** them.

---

## Why This Document Exists

> **"It emits C#" is not a semantics specification.**

The .NET backend generates C# code, but C# is not the source of truth for Calor semantics. This document specifies:

1. **Where C# behavior matches Calor** → Direct emission is safe
2. **Where C# behavior differs or is unspecified** → Additional code is required
3. **How to verify conformance** → Test cases and validation checklists

Without this specification, subtle differences between C# and Calor could cause:
- "Works on my machine" bugs
- Version-specific behavior changes
- Agent-generated code that fails on different .NET versions

---

## 1. Core Principle

> **The emitter must not rely on unspecified behavior in C#.**

When C# evaluation order matches Calor semantics, direct emission is acceptable. When C# behavior is unspecified or differs, the emitter must generate code that enforces Calor semantics.

### What "Unspecified" Means

C# specifies many behaviors, but not all. For example:
- Argument evaluation order **is** specified (left-to-right) ✓
- `checked` overflow behavior **is** specified ✓
- Some optimization behaviors **are not** specified ✗

The emitter must never assume unspecified behavior matches Calor semantics.

---

## 2. Evaluation Order Enforcement

### 2.1 Function Arguments

C# specifies left-to-right argument evaluation (C# spec 12.6.2.2), which matches Calor semantics.

**Direct emission is safe:**
```csharp
// Calor: f(a(), b(), c())
f(a(), b(), c());  // C# guarantees left-to-right
```

### 2.2 Binary Operators

C# evaluates binary operator operands left-to-right (C# spec 12.4.1), matching Calor.

**Direct emission is safe:**
```csharp
// Calor: a() + b()
a() + b();  // C# guarantees left-to-right
```

### 2.3 Complex Expressions with Side Effects

When lowering CNF to C#, preserve temporary assignments if the CNF introduced them:

**CNF:**
```
t1 = a()
t2 = b()
t3 = t1 + t2
```

**C# (from CNF):**
```csharp
var t1 = a();
var t2 = b();
var t3 = t1 + t2;
```

---

## 3. Short-Circuit Operators

### 3.1 Logical AND (`&&`)

C# `&&` short-circuits identically to Calor.

**Direct emission is safe:**
```csharp
// Calor: A && B
A && B;  // C# short-circuits
```

### 3.2 Logical OR (`||`)

C# `||` short-circuits identically to Calor.

**Direct emission is safe:**
```csharp
// Calor: A || B
A || B;  // C# short-circuits
```

### 3.3 From CNF

When emitting from CNF (which has control flow), generate structured code:

**CNF:**
```
t_result = false
branch A -> then_block, end_block
then_block:
  t_result = B
end_block:
```

**C# Option 1 (reconstruct `&&`):**
```csharp
var t_result = A && B;
```

**C# Option 2 (preserve CNF structure):**
```csharp
bool t_result;
if (A)
{
    t_result = B;
}
else
{
    t_result = false;
}
```

---

## 4. Numeric Semantics

### 4.1 Integer Overflow

**Calor Default:** TRAP (throw OverflowException)

**C# Implementation:**
```csharp
// Use checked arithmetic
checked
{
    var result = a + b;  // Throws OverflowException on overflow
}
```

**Compiler Flag `--overflow=wrap`:**
```csharp
// Use unchecked arithmetic
unchecked
{
    var result = a + b;  // Wraps on overflow
}
```

### 4.2 Project-Level Configuration

The emitter should generate project files with appropriate settings:

```xml
<PropertyGroup>
  <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>
</PropertyGroup>
```

Or wrap arithmetic in `checked` blocks when `--overflow=trap`.

### 4.3 Type Mapping

| Calor Type | C# Type | Notes |
|------------|---------|-------|
| `INT` / `i32` | `int` | 32-bit signed |
| `i64` | `long` | 64-bit signed |
| `i16` | `short` | 16-bit signed |
| `i8` | `sbyte` | 8-bit signed |
| `u32` | `uint` | 32-bit unsigned |
| `u64` | `ulong` | 64-bit unsigned |
| `u16` | `ushort` | 16-bit unsigned |
| `u8` | `byte` | 8-bit unsigned |
| `FLOAT` / `f64` | `double` | 64-bit IEEE 754 |
| `f32` | `float` | 32-bit IEEE 754 |
| `BOOL` | `bool` | - |
| `STRING` | `string` | - |
| `VOID` | `void` | - |

### 4.4 Conversions

**Implicit Widening:**
```csharp
// Calor: INT → FLOAT (implicit)
int i = 42;
double f = i;  // C# allows implicit widening
```

**Explicit Narrowing:**
```csharp
// Calor: FLOAT → INT (explicit required)
double f = 3.14;
int i = (int)f;  // Explicit cast required
```

---

## 5. Contracts

### 5.1 Preconditions

**C# Implementation:**
```csharp
public int MyFunction(int x)
{
    // REQUIRES: x > 0
    if (!(x > 0))
    {
        throw new Calor.Runtime.ContractViolationException(
            functionId: "f001",
            message: "x must be positive",
            expression: "x > 0",
            kind: ContractKind.Requires);
    }

    // Body
    return x * 2;
}
```

### 5.2 Postconditions

**C# Implementation:**
```csharp
public int MyFunction(int x)
{
    // Body
    var __result = x * 2;

    // ENSURES: result > x
    if (!(__result > x))
    {
        throw new Calor.Runtime.ContractViolationException(
            functionId: "f001",
            message: "result must be greater than input",
            expression: "result > x",
            kind: ContractKind.Ensures);
    }

    return __result;
}
```

### 5.3 Contract Mode Configuration

| Mode | Behavior |
|------|----------|
| `Debug` | All contracts checked |
| `Release` | Only preconditions checked |
| `None` | No contract checking |

**C# Implementation with Conditional:**
```csharp
#if DEBUG
if (!(condition))
{
    throw new ContractViolationException(...);
}
#endif
```

---

## 6. Option<T> and Result<T,E>

### 6.1 Option<T>

**C# Implementation using records:**
```csharp
namespace Calor.Runtime
{
    public abstract record Option<T>
    {
        public sealed record Some(T Value) : Option<T>;
        public sealed record None : Option<T>;

        public static Option<T> Some(T value) => new Some(value);
        public static Option<T> None() => new None();
    }
}
```

### 6.2 Result<T,E>

**C# Implementation:**
```csharp
namespace Calor.Runtime
{
    public abstract record Result<T, E>
    {
        public sealed record Ok(T Value) : Result<T, E>;
        public sealed record Err(E Error) : Result<T, E>;

        public static Result<T, E> Ok(T value) => new Ok(value);
        public static Result<T, E> Err(E error) => new Err(error);
    }
}
```

---

## 7. Pattern Matching

### 7.1 Basic Patterns

**Calor:**
```calor
§MATCH{m1} §REF{name=opt}
  §CASE §SOME{x} => §R §REF{name=x}
  §CASE §NONE => §R INT:0
§/MATCH{m1}
```

**C#:**
```csharp
opt switch
{
    Option<int>.Some(var x) => x,
    Option<int>.None => 0,
}
```

### 7.2 Property Patterns

**Calor:**
```calor
§PPROP{Person} §PMATCH{Age} §PREL{gte} 18
```

**C#:**
```csharp
Person { Age: >= 18 }
```

---

## 8. Exception Handling

### 8.1 Try/Catch/Finally

**Direct mapping to C#:**
```csharp
try
{
    // try body
}
catch (IOException ex)
{
    // catch body
}
finally
{
    // finally body
}
```

### 8.2 Exception Filters

**Calor catch with filter:**
```calor
§CA{IOException:ex}{when=§OP{kind=EQ} §REF{name=ex.Message} STR:"Not found"}
```

**C#:**
```csharp
catch (IOException ex) when (ex.Message == "Not found")
{
    // handler
}
```

---

## 9. Async/Await

### 9.1 Async Functions

**Calor async function:**
```calor
§F{f1:fetchData:pub:async}
  §O{Task{string}}
  ...
§/F{f1}
```

**C#:**
```csharp
public async Task<string> FetchData()
{
    // body
}
```

### 9.2 Await Expression

**Calor:**
```calor
§AWAIT §C{client.GetStringAsync} §A url §/C
```

**C#:**
```csharp
await client.GetStringAsync(url)
```

### 9.3 ConfigureAwait

**Calor:**
```calor
§AWAIT{false} expr
```

**C#:**
```csharp
await expr.ConfigureAwait(false)
```

---

## 10. Classes and Inheritance

### 10.1 Class Definition

**Calor:**
```calor
§CLASS{c1:Circle:seal}
  §EXT{Shape}
  §IMPL{IDrawable}
  §FLD{f64:Radius:pri}
  §METHOD{m1:Area:pub:over} §O{f64} §E{}
    §R §OP{kind=MUL} 3.14159 §OP{kind=MUL} §REF{name=Radius} §REF{name=Radius}
  §/METHOD{m1}
§/CLASS{c1}
```

**C#:**
```csharp
public sealed class Circle : Shape, IDrawable
{
    private double Radius;

    public override double Area()
    {
        return 3.14159 * Radius * Radius;
    }
}
```

### 10.2 Method Modifiers

| Calor | C# |
|-------|-----|
| `virt` | `virtual` |
| `over` | `override` |
| `abs` | `abstract` |
| `seal` | `sealed` |
| `stat` | `static` |

---

## 11. Records

### 11.1 Record Definition

**Calor:**
```calor
§RECORD{r1:Person}
  §FIELD{Name}{STRING}
  §FIELD{Age}{INT}
§/RECORD{r1}
```

**C#:**
```csharp
public record Person(string Name, int Age);
```

### 11.2 With Expressions

**Calor:**
```calor
§WITH §REF{name=person}
  §SET{Age} 30
§/WITH
```

**C#:**
```csharp
person with { Age = 30 }
```

---

## 12. Effects (Advisory)

Effects in Calor are primarily compile-time checked. The C# backend:

1. **Does not emit runtime effect tracking** (by default)
2. **Generates XML documentation** for effect declarations
3. **May emit attributes** for tooling integration

```csharp
/// <summary>
/// Effects: [io=file_read, io=console_write]
/// </summary>
[CalorEffects("io:file_read", "io:console_write")]
public void ProcessFile(string path) { ... }
```

---

## 13. Code Generation Guidelines

### 13.1 Naming Conventions

| Calor | C# |
|-------|-----|
| Function `myFunc` | Method `MyFunc` (PascalCase) |
| Parameter `myParam` | Parameter `myParam` (camelCase) |
| Local `myVar` | Local `myVar` (camelCase) |
| Type `MyType` | Type `MyType` (PascalCase) |
| Module `myModule` | Namespace/Class `MyModule` |

### 13.2 Generated Code Markers

The emitter should include markers in generated code:

```csharp
// <auto-generated>
// This file was generated by the Calor compiler.
// Semantics version: 1.0.0
// Do not modify directly.
// </auto-generated>
```

### 13.3 Source Mapping

Generate `#line` directives for debugging:

```csharp
#line 42 "MyModule.calr"
public int Add(int a, int b)
#line default
{
    return a + b;
}
```

---

## 14. Runtime Library

The .NET backend depends on `Calor.Runtime.dll`:

```
Calor.Runtime
├── ContractViolationException.cs
├── Option.cs
├── Result.cs
└── Effects/
    └── EffectAttributes.cs
```

---

## 15. Validation Checklist

The backend must ensure:

- [ ] Left-to-right evaluation preserved
- [ ] Short-circuit semantics correct
- [ ] Integer overflow traps (default)
- [ ] Type conversions explicit where required
- [ ] Contracts generate correct exception type
- [ ] Contract exception includes function ID
- [ ] Option/Result types from runtime library
- [ ] Pattern matching exhaustive (compiler enforced)
- [ ] Async/await preserves semantics

---

## References

- Core Semantics: `docs/semantics/core.md`
- CNF Specification: `docs/semantics/normal-form.md`
- C# Emitter: `src/Calor.Compiler/CodeGen/CSharpEmitter.cs`
- Runtime Library: `src/Calor.Runtime/`
