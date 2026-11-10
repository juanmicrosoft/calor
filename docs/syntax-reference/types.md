---
layout: default
title: Types
parent: Syntax Reference
nav_order: 2
---

# Types

Calor uses C# type names, .NET nullable types, ordinary generics, and suffix
array notation. A declaration's type precedes its name: `int:count`, not
`count:int`. Initializers and arguments must be compatible with their
receiving types.

## Primitive types and literals

| Type | Meaning | Literal examples |
|:-----|:--------|:-----------------|
| `int` | 32-bit signed integer | `42`, `-17` |
| `long` | 64-bit signed integer | `42L`, `-17L` |
| `uint`, `ulong` | Unsigned integers | `42U`, `42UL` |
| `short`, `ushort`, `sbyte`, `byte` | Smaller integer types | Use a compatible value or an explicit cast |
| `float` | 32-bit floating point | `3.14f` |
| `double` | 64-bit floating point | `3.14`, `2.718` |
| `decimal` | Decimal arithmetic | `18.00m` |
| `char` | UTF-16 code unit | `'x'` |
| `string` | UTF-16 text | `"hello"` |
| `bool` | Boolean | `true`, `false` |
| `object` | .NET object reference | An appropriate reference or boxed value |
| `void` | No returned value | Return types only |

Qualified .NET names such as `System.String` can also be used. Calor's former
aliases, including `i32`, `f64`, and `str`, are not native source spellings.
The parser reports `Calor0100` for a retired alias:

```calor expect=Calor0100
module RetiredType
  func Main () → void
    let i32:value = 42
```

Function signatures put the same type-first declarations in parentheses:

```calor-fragment
func Count public (int:count) → int
func Name public (string:name) → string
func Price public (double:price, bool:active) → void
```

### Constructor arguments

`new Pair(right: "right", left: "left")` calls constructor parameters named
`right` and `left`; these are not object-initializer properties. Named
arguments evaluate in source order even when their names reorder the
parameters. Records use the same construction syntax. The selected constructor
must actually exist in the source or referenced assembly.

Inside a class, `ctor stat ()` declares its static constructor. It has no
visibility or parameters. Conversion preserves the constructor and leaves
its initialization order and exception behavior to the CLR.

### Numeric and character literals

C# migration resolves a primitive `default` literal from its contextual type
and emits the corresponding zero or `false`, retaining its numeric width and
overload selection. In authored native code, use `default(int)` or another
supported primitive type rather than a bare `default` identifier.

Integer literals accept decimal or hexadecimal digits and `_` separators.
Use `L`, `U`, or `UL` to request signed 64-bit, unsigned 32-bit or unsigned
64-bit values. A signed `L` literal never becomes unsigned to accommodate a
larger value: out-of-range signed literals report `Calor0012`.
Negative unsigned literals report `Calor0010`.

The lowercase `f` suffix selects a single-precision value, including `42f`
and `1e-3f`. Fractions or exponents without `f` or `m` are `double`.
Decimal literals use `m`, as in `18.0000m` or `100m`.

Character literals contain exactly one UTF-16 code unit. Empty or multi-unit
characters, raw line breaks and invalid escapes report `Calor0112`. Escapes
include `'\n'`, `'\''`, `'\\'`, `'\u0041'`, `\x` with one to four hexadecimal
digits, and `\U` with eight digits whose decoded value still fits one code
unit. Supplementary Unicode characters require strings; escaped surrogate
code units remain representable. Character subtraction such as `'4' - '0'`
produces an `int`.

## Anonymous objects

Use `new { Name = expression }` to create an anonymous object. Identifier and
member projections can omit the name: `new { count, text.Length }`. Use
`@class` or another escaped identifier for a keyword property name. Members
evaluate left to right, and their names must be distinct.

Anonymous construction requires `alloc`; effects of member expressions are
also charged. Nested anonymous objects use the same syntax. Inferred bindings
retain their member types across aliases. Reading an anonymous member is
pure, but further operations on its value keep their own effects: reading
`value.File.Length` still requires `fs:r` when `File` is a `FileInfo`.

## Arrays

Sized jagged creation uses the allocated rank first: `new int[2][]` allocates
two slots for inner `int[]` arrays, initially null. `new int[2, 3][]`
allocates a rectangular array of inner-array slots. Trailing ranks do not
allocate the inner arrays; creating the outer array still requires `alloc`.

Use `T[]` for a one-dimensional array, `T[,]` for a two-dimensional array,
and `T[][]` for an array of arrays. Array rank and element type are part of
the type. An array is not a `List<T>`: for example, `File.ReadAllLines`
returns `string[]`.

Create arrays with `new`, read elements with brackets, and read their size
with `Length`.

```calor
module ArrayTypes
  func Main () → int
    ! alloc
    let int[]:values = new int[]{1, 2, 3}
    return values[0] + values[1] + values[2]
```

`Main()` returns `6`. Array creation requires the `alloc` effect. The
declaration retains the explicit `int[]` type in generated C#.

## Nullable types

Write `T?`, with a suffix question mark. `string?` and a resolved `User?`
are nullable references. `int?` is the .NET nullable value type
`System.Nullable<int>`. Prefix spellings such as `?string` are retired.

Use an ordinary compatible value or `null`. The `??` operator supplies a
fallback when its left operand is null:

```calor
module NullableFallback
  func Greeting public (string?:name) → string
    !
    return name ?? "anonymous"

  func Main () → string
    !
    return Greeting(null)
```

`Main()` returns `"anonymous"`, not an option wrapper.

Element and container nullability are different:

| Type | Meaning |
|:-----|:--------|
| `string?[]` | Array whose string elements may be null |
| `string[]?` | Possibly-null array of non-null string elements |
| `string?[]?` | Both the array and its elements may be null |
| `List<string?>` | List whose string elements may be null |

A nullable annotation is not a universal proof that every operation is safe.
Supported receiving boundaries check nullable input, but arbitrary later
writes and constructor inputs remain outside the bounded guarantee.
See [Nullability and .NET Interop](https://calor.dev/docs/guides/nullability-and-dotnet-interop/)
for the exact support table and complete safe and rejected examples.

## Failure uses exceptions

Return the ordinary success type and throw on failure. The `throw` effect
declares exceptions; `alloc` covers construction of the exception object.
Use `try`, `catch`, and `finally` for handling and cleanup; a bare `throw`
inside a catch body rethrows the current exception.

```calor
module Division
  func Divide public (int:a, int:b) → int
    ! throw, alloc
    if b == 0
      throw new ArgumentException("Division by zero")
    return a / b

  func Main () → int
    ! throw, alloc
    try
      return Divide(1, 0)
    catch ArgumentException:error
      return Divide(84, 2)
```

`Main()` catches the deliberate zero-divisor exception and returns `42`.
There is no built-in success/error return-type encoding.

## Generic types

Generic arguments use angle brackets, including when nested:

```calor-fragment
List<int>
Dictionary<string, List<int>>
IEnumerable<string>
Func<int, string>
```

Import the required namespace with `using`, or use a qualified type name.
Type parameters declared by a generic function or class can be used in its
parameters, return type, and body. A type annotation does not supply a
missing reference, infer every generic method argument, or fix an invalid
overload.

Third-party types named `Option<T>` or `Result<T,E>` are ordinary generic
types, not Calor built-ins. Reference their assembly and use their library
API. Square-bracket generic arguments are not native syntax.

## Collections

Use ordinary .NET construction and calls, not special collection tags:

```calor
module CollectionTypes
  using System.Collections.Generic
  func Main () → int
    ! alloc, mut
    let List<int>:numbers = new List<int>{1, 2, 3}
    let Dictionary<string,int>:ages = new Dictionary<string,int>{{"alice", 30}}
    let HashSet<string>:tags = new HashSet<string>{"urgent", "review"}
    numbers.Add(4)
    set numbers[0] 10
    set ages["charlie"] 35
    return numbers[0] + numbers.Count + ages["charlie"] + tags.Count
```

`Main()` returns `51`. Construction and mutations require the appropriate effects.
Built-in collection initializers can use the compiler's bounded fresh-object
proof; a later `Add`, an arbitrary comparer, or an unknown user callback does
not acquire that proof.

| Operation | Ordinary .NET member |
|:----------|:---------------------|
| Add or insert | `numbers.Add(value)`, `numbers.Insert(index, value)` |
| Remove or clear | `numbers.Remove(value)`, `numbers.Clear()` |
| Read size | `numbers.Count` |
| Check membership | `numbers.Contains(value)` |
| Check dictionary keys or values | `ages.ContainsKey(key)`, `ages.ContainsValue(value)` |

## Type compatibility

An annotation is a receiving boundary, not a cast. Supported implicit
conversions include numeric widening, such as `int` to `long`. Unrelated
types still produce `Calor0202`:

```calor expect=Calor0202
module IncompatibleType
  func Main () → void
    let int:value = "not an integer"
```

Use an explicit cast when the conversion requires one; a cast can still fail
at runtime. Generic definitions and their arguments matter, so
`List<int>` and `List<string>` are not interchangeable. Operators must be
valid for their operands: logical operators require booleans, and two
references having the same type does not invent an ordering operator.

Types also apply in `requires` and `=>` contract predicates.

## See also

- [Bindings](/calor/syntax-reference/binding/) — inference, initialization, and mutable locals.
- [Expressions](/calor/syntax-reference/expressions/) — operators, casts, and precedence.
- [Calls](/calor/syntax-reference/calls/) — argument and return boundaries.
