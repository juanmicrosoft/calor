---
layout: default
title: "Types"
parent: Syntax Reference
nav_order: 2
---

# Types

<!-- Generated from website/content/syntax-reference/types.mdx by calor self-check docs --fix. Edit the source, not this file. -->

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

The keyword `object` always names `System.Object`, including as an array
element type. A source declaration or type parameter named `@object` does
not shadow that keyword.

Qualified .NET names such as `System.String` can also be used. Calor's former
aliases, including `i32`, `f64`, and `str`, are not native source spellings.
The parser reports `Calor0100` for a retired alias:

```calor expect=Calor0100
module RetiredType
  func Main () → void
    let i32:value = 42
```

Native method calls apply string overload and nullability rules to qualified
names and aliases only when they resolve to the CLR string type. A source
type with a similar name does not gain those conversions.

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

Inside a class, `ctor static ()` declares its static constructor. It has no
visibility or parameters. Conversion preserves the constructor and leaves
its initialization order and exception behavior to the CLR.

C# conversion uses resolved types to distinguish a base class from implemented
interfaces, including qualified names and aliases. It does not infer that
distinction from an `I` prefix. An unresolved base kind keeps the declaration
as preserved C#, without claiming native constructor-effect analysis.

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

### Implicit numeric conversions

Calor follows C#'s implicit numeric conversions. A value converts to a wider
type of its family without a cast: `int` to `long`, `float` to `double`, and
any integer type to `float`, `double` or `decimal`. A conversion that can lose
information needs an explicit cast, such as `(float)value`. Examples are
`double` to `float`, `long` to `int`, `double` to `int`, and `decimal` to or
from `double`. The type checker reports `Calor0202` at the expression for a
binding, return, assignment or same-module call argument that needs one.
An integer constant converts to a narrower integer type when its value fits,
so `let byte:mask = 255` is valid and `let byte:mask = 256` is not. A
fractional literal without a suffix is a `double`; write `1.5f` for a `float`.

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

Rectangular initializers keep one brace level per dimension. For example,
`new int[2, 1, 2] { { { 1, 2 } }, { { 3, 4 } } }` creates a rank-three
array. Omit the sizes with `new int[,,]` to infer them from the braces.
Written sizes, empty braces and row boundaries are preserved; mismatched
sizes or ragged rows are rejected rather than padded, flattened or resized.
Initializer expressions still require their own effects in addition to `alloc`.
Array elements must fit inline. When migrating C#, a block-bodied lambda element
is preserved as C# with an `array-element-block` report instead of emitting an
invalid native initializer.

Reading a source static array field through its type retains the declared rank,
including inherited fields. Effect checking accounts for all static field and
property initializers on the declaring type, not just the field being read.
This path currently supports self-contained primitive and primitive-array
initializers. Other initializer forms remain unresolved; an enforced read
cannot silently treat them as pure. Explicit static constructors retain their
constructor-effect checks.

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

Mandatory receiving checks also cover closed source-defined reference elements
in the actual .NET `List<T>`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`,
`IReadOnlyList<T>` and `IReadOnlyCollection<T>` types. This is a deliberately
stricter compatibility rule, enforced with or without optional type checking:
nullable elements cannot enter a non-null element contract, and a nullable
container must be guarded before non-null receiving. A container guard does not
validate its elements. Invariant containers also reject nullable-writer aliases
such as `List<Node>` to `List<Node?>`; covariant read-only targets can accept
nullable elements. Oblivious annotations do not prove non-nullness, except that a
`nullable oblivious` module accepts them as C# does (see
[Oblivious nullability](https://calor.dev/docs/syntax-reference/structure-tags/#oblivious-nullability)).

Selected metadata extension calls retain these annotations when the complete
C# image resolves the actual source-defined payload. For example, `ToList()`
preserves nullable elements and requires a non-null receiver. Selection supplies
typing facts, not an effect contract: `ToList()` remains an unknown-effect call
and is rejected under strict effect enforcement.

This extension does not analyze custom generic containers, nested generic
payloads, type parameters, tuples, delegates or unresolved reference identities.
Those are unsupported boundaries, not evidence that the receiving contract is
safe. In particular, a dotted read that remains unbound is not receiving proof
just because the C# compiler resolves it. Existing string-specific rules and
external-call effect requirements are unchanged.

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
    !
    let int:value = "not an integer"
```

Use an explicit cast when the conversion requires one; a cast can still fail
at runtime. Generic definitions and their arguments matter, so
`List<int>` and `List<string>` are not interchangeable. Operators must be
valid for their operands: logical operators require booleans, and two
references having the same type does not invent an ordering operator.

Types also apply in refinement and `=>` contract predicates.

## See also

- [Bindings](https://calor.dev/docs/syntax-reference/binding/) — inference, initialization, and mutable locals.
- [Expressions](https://calor.dev/docs/syntax-reference/expressions/) — operators, casts, and precedence.
- [Calls](https://calor.dev/docs/syntax-reference/calls/) — argument and return boundaries.
