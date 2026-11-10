# .NET Backend Specification

Semantics Version: 2.0.0

This document specifies how the .NET backend must implement Calor semantics. The backend **conforms to** Calor semantics; it does not **define** them.

The production path calls `CSharpEmitter.Emit(ast)` after AST analyses. It does
not consume CNF; `normal-form.md` describes separate, unintegrated lowering work.

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

The following is a design obligation for a future CNF backend, not the
production compilation path. A backend consuming CNF must preserve temporary
assignments if lowering introduced them:

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

For the separate CNF design (not the production emitter input), a backend
would generate structured code:

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

**Preserved unchecked C# semantics:**
```csharp
// Use unchecked arithmetic
unchecked
{
    var result = a + b;  // Wraps on overflow
}
```

### 4.2 Project-Level Configuration

The native emitter uses explicit checked expressions and statements. It does
not require consumers to enable this C# project setting:

```xml
<PropertyGroup>
  <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>
</PropertyGroup>
```

There is no Calor `--overflow=wrap` or `--overflow=trap` CLI option. Migration
can retain imported unchecked semantics; do not make all preserved C# checked
by changing its project context. See the [core numeric rules](core.md#4-numeric-semantics).

```calor
module BackendOverflow
  func Increment (int:value) → int
    !
    return value + 1
```

`Increment(41)` returns `42`; `Increment(int.MaxValue)` throws
`OverflowException`, including with contract mode off.

### 4.3 Type Mapping

| Calor Type | C# Type | Notes |
|------------|---------|-------|
| `int` | `int` | 32-bit signed |
| `long` | `long` | 64-bit signed |
| `short` | `short` | 16-bit signed |
| `sbyte` | `sbyte` | 8-bit signed |
| `uint` | `uint` | 32-bit unsigned |
| `ulong` | `ulong` | 64-bit unsigned |
| `ushort` | `ushort` | 16-bit unsigned |
| `byte` | `byte` | 8-bit unsigned |
| `double` | `double` | 64-bit IEEE 754 |
| `float` | `float` | 32-bit IEEE 754 |
| `bool` | `bool` | Boolean |
| `string` | `string` | Reference type |
| `void` | `void` | No return value |

### 4.4 Conversions

**Implicit Widening:**
```csharp
// Native int to double (implicit)
int i = 42;
double f = i;  // C# allows implicit widening
```

**Explicit Narrowing:**
```csharp
// Native double to int (explicit required)
double f = 3.14;
int i = checked((int)f);  // Explicit cast required
```

---

## 5. Contracts

### 5.1 Preconditions

**Schematic C# implementation** (the emitter supplies actual IDs and spans):
```csharp
public int MyFunction(int x)
{
    // REQUIRES: x > 0
    if (!(x > 0))
    {
        throw new Calor.Runtime.ContractViolationException(
            "Precondition failed", "f001", Calor.Runtime.ContractKind.Requires,
            startOffset: 0, length: 0, condition: "x > 0");
    }

    // Body
    return checked(x * 2);
}
```

### 5.2 Postconditions

**C# Implementation:**
```csharp
public int MyFunction(int x)
{
    // Body
    var __result = checked(x * 2);

    // ENSURES: result > x
    if (!(__result > x))
    {
        throw new Calor.Runtime.ContractViolationException(
            "Postcondition failed", "f001", Calor.Runtime.ContractKind.Ensures,
            startOffset: 0, length: 0, condition: "__result > x");
    }

    return __result;
}
```

### 5.3 Contract Mode Configuration

| Mode | Behavior |
|------|----------|
| `debug` | Detailed supported precondition and postcondition checks |
| `release` | The same supported checks, with leaner exceptions |
| `off` | No precondition or postcondition guards |

This is a compiler option, not a C# `DEBUG` conditional. Optional verification
can remove only eligible clean proven postcondition guards;
`--keep-proven-guards` retains them. Unsupported requested runtime lowering
fails rather than silently dropping a check. Explicit `proof` statements have
their own guards and remain active with contract mode off.

```calor
module BackendContracts
  func Positive (int:value) → int
    !
    requires value > 0
    => result > 0
    return value
```

`Positive(1)` returns `1`. `Positive(0)` throws `ContractViolationException`
with enabled contracts and returns `0` with contract mode off.

---

## 6. Nullable Values and Exceptions

The backend emits .NET's native representations: `string?` remains `string?`,
`int?` remains `int?`, and `null` remains `null`. The native expression
`value ?? fallback` emits C# null coalescing.

Native `throw`, `try`, `catch`, `finally`, and bare `throw` emit the corresponding
C# statements, including rethrow. Success values use their ordinary return
type; no Calor-specific absence/error runtime types are required.

---

## 7. Pattern Matching

### 7.1 Basic Patterns

**Calor:**
```calor
module BackendMatch
  func Describe (int:code) → string
    !
    return match code
      case 200 → "OK"
      case 404 → "Not Found"
      case _ → "Unknown"
```

**C#:**
```csharp
code switch
{
    200 => "OK",
    404 => "Not Found",
    _ => "Unknown",
}
```

### 7.2 Property Patterns

**Calor:**
```calor-fragment
Person { Age: >= 18 }
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
module BackendCatch
  using System
  func Read (bool:fail) → int
    ! alloc, throw
    try
      if fail
        throw new InvalidOperationException("Not found")
      return 42
    catch InvalidOperationException:ex when ex.Message == "Not found"
      return -1
```

**C#:**
```csharp
catch (InvalidOperationException ex) when (ex.Message == "Not found")
{
    // handler
}
```

---

## 9. Async/Await

### 9.1 Async Functions

**Calor async function:**
```calor
module BackendAsync
  using System.Threading.Tasks
  func Read async () → int
    !
    return await Task.FromResult(42)
```

**C#:**
```csharp
public static async Task<int> Read()
{
    return await Task.FromResult(42);
}
```

### 9.2 Await Expression

**Calor:**
```calor-fragment
await client.GetStringAsync(url)
```

**C#:**
```csharp
await client.GetStringAsync(url)
```

### 9.3 ConfigureAwait

**Calor:**
```calor
module BackendConfiguredAwait
  using System.Threading.Tasks
  func Read async () → int
    !
    return await Task.FromResult(42).ConfigureAwait(false)
```

**C#:**
```csharp
await Task.FromResult(42).ConfigureAwait(false)
```

---

## 10. Classes and Inheritance

### 10.1 Class Definition

**Calor:**
```calor
module BackendClasses
  interface IDrawable
    func Area () → double
      !
  class Shape abstract
    func Area abstract () → double
      !
  class Circle sealed
    extends Shape
    implements IDrawable
    func Area override () → double
      !
      return 3.14159 * 5.0 * 5.0
  func Main () → double
    ! alloc
    let Shape:value = new Circle()
    return value.Area()
```

**C#:**
```csharp
public sealed class Circle : Shape, IDrawable
{
    public override double Area()
    {
        return 3.14159 * 5.0 * 5.0;
    }
}
```

### 10.2 Method Modifiers

| Calor | C# |
|-------|-----|
| `virtual` | `virtual` |
| `override` | `override` |
| `abstract` | `abstract` |
| `sealed` | `sealed` |
| `static` | `static` |

---

## 11. Records

### 11.1 Record Declaration and Construction

The bare `record` declaration represents a C# record class, not an ordinary
class or a record struct. C# synthesizes positional properties, value equality,
hashing, `ToString`, deconstruction and copy behavior. Constructor defaults and
record visibility survive emission. Properties, methods and static members use
the existing indented member syntax. Derived positional records pass their base
arguments in the header: `record D (int:X, int:Y) : B(X)`.

Record structs, partial or nested declarations, positional parameter attributes/modifiers, conditional members
and unrepresented member kinds preserve unchanged C# with a specific converter
fallback reason. They do not count as native record support.

**Calor:**
```calor
module BackendRecords
  record Person (string:Name, int:Age)
  func Create () → Person
    ! alloc, mut
    return new Person(Age: 30, Name: "Alice")
```

**C#:**
```csharp
public record Person(string Name, int Age);
public static class GlobalModule
{
    public static Person Create() => new Person(Age: 30, Name: "Alice");
}
```

### 11.2 With Expressions

**Calor:**
```calor
module BackendRecordCopy
  record Person (string:Name, int:Age)
  func WithAge (Person:person) → Person
    ! alloc, mut
    return person with { Age = 30 }
```

**C#:**
```csharp
person with { Age = 30 }
```

Constructors and custom init accessors retain the existing effect rules.
Construction and copying charge their actual constructor and accessor bodies;
record names do not bypass `Calor0410` or unknown-call diagnostic `Calor0411`.

---

## 12. Effects (Compile-Time Enforcement)

Ordinary compilation enforces declared effect rows using selected source and
metadata calls and reviewed external contracts. Unknown calls are not assumed
pure. This is bounded compile-time enforcement, not a proof of arbitrary
library implementations or converter-preserved C#. The C# backend:

1. **Does not emit runtime effect tracking** (by default)
2. Emits ordinary CLR calls after the compile-time checks
3. Does not require a runtime effect attribute to enforce a source row

```calor-fragment
func Print (string:text) → void
  ! cw
  System.Console.WriteLine(text)
```

---

## 13. Code Generation Guidelines

### 13.1 Naming Conventions

| Calor | C# |
|-------|-----|
| Function `myFunc` | Method `myFunc` (spelling preserved) |
| Parameter `myParam` | Parameter `myParam` (camelCase) |
| Local `myVar` | Local `myVar` (camelCase) |
| Type `MyType` | Type `MyType` (PascalCase) |
| File label `myModule` | File identity, not a CLR namespace or class |
| `module Label Demo` | CLR namespace `Demo` |
| Nested `module Tools` | Named static container `Tools` |

Root functions use the generated `GlobalModule` container. Reserved C#
identifiers are escaped as needed; emission does not rename declarations to
PascalCase.

### 13.2 Generated Code Markers

The emitter should include markers in generated code:

```csharp
// <auto-generated>
// This code was generated by the Calor compiler.
// Do not modify this file directly.
// </auto-generated>
```

### 13.3 Source Mapping

Source mapping can emit `#line` directives when a source path is supplied;
this is not a claim that every standalone emitter call enables them:

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
└── Attributes.cs
```

---

## 15. Validation Checklist

These are validation obligations, not a claim of exhaustive enforcement:

- [ ] Left-to-right evaluation preserved
- [ ] Short-circuit semantics correct
- [ ] Integer overflow traps (default)
- [ ] Type conversions explicit where required
- [ ] Contracts generate correct exception type
- [ ] Contract exception includes function ID
- [ ] Native nullable types and exception handling
- [ ] Pattern matching follows admitted native forms; general exhaustiveness
      is not proved for arbitrary guarded patterns
- [ ] Async/await preserves semantics

---

## References

- Core Semantics: `docs/semantics/core.md`
- CNF Specification: `docs/semantics/normal-form.md`
- C# Emitter: `src/Calor.Compiler/CodeGen/CSharpEmitter.cs`
- Runtime Library: `src/Calor.Runtime/`
