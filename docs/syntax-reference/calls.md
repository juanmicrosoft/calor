---
layout: default
title: Calls
parent: Syntax Reference
nav_order: 4
---

# Calls

Call functions and methods with parentheses and comma-separated arguments, as in
C#. A call can be a statement that discards its result, or an expression used by
a binding, another call, a return, or an operator.

## Syntax

```calor-fragment
Flush()
Identity(value)
Add(left, right)
Add(right: second, left: first)
items.ConvertAll<string>(convert)
factory.Create().Compute(argument())
```

Always write `()` for a zero-argument call. There are no implicit arguments,
argument tags, or call-closing tags. Generic type arguments use C# type names.

Built-in type aliases can receive static calls: `string.Concat(left, right)`
and `int.Parse(text)` refer to .NET types. A verbatim name such as `@string`
instead refers to a declaration named `string`; `@string.Length` reads that
value's member. Calor-to-Calor emission preserves this distinction.

The callee can be an expression, including a member chain or a delegate value.
Arguments are evaluated in source order, even when named arguments refer to
parameters in a different order. A receiver or argument with side effects is
not evaluated twice. Calls on the right of `&&`, `||`, `??`, or a conditional
expression run only when that branch is selected.

## Complete example

```calor
module CallExamples
  func Identity (int:value) → int
    !
    return value

  func Add (int:left, int:right) → int
    !
    return left + right

  func Main () → int
    !
    let int:first = Identity(2)
    let int:second = Add(right: 4, left: 3)
    return Add(first, second)
```

`Main()` returns `9`. Nested calls use the same syntax:
`Add(Identity(2), Add(3, 4))`.

## Member access and nullable inputs

Parentheses identify exactly which result receives a member access. These
fragments assume an `Identity` overload for each nullable input type and a
nullable string return from `GetMessage`:

```calor-fragment
let length = Identity(message?.Length)
let otherLength = Identity(message)?.Length
let upper = GetMessage()?.ToUpper()
```

The first call receives `message?.Length`; the second reads `Length` from the
call result. A nullable receiver does not become non-null merely because its
value passed through a call.

Supported possibly-null method arguments produce `Calor0274` in statement and
expression calls alike. Named arguments are checked against the parameter they
name, not their source position. An otherwise applicable native overload is
not rejected solely for nullable string input with `Calor0208`; it reports the
nullability error instead. Genuine overload mismatches still use `Calor0208`.

This is bounded receiving-boundary enforcement, not whole-program null safety.
It does not implicitly unwrap library generic types or enforce constructor
inputs. See [Nullability and .NET Interop](https://calor.dev/docs/guides/nullability-and-dotnet-interop/#calor0274-method-input).

Source-method lookup retains integer literal values, including unary minus.
A `byte` value parameter accepts `255` but rejects `256`. This does not turn an
immutable `int` local into a constant or permit conversions of `ref`/`out` storage.
Literal-overload selection compares conversions argument by argument; a better
conversion for one argument cannot cancel a worse conversion for another.
General constant-expression evaluation is not implemented here.

## Unknown receiver types

An inferred local whose type is unknown is not a declared `object`. Calls on
such author-written locals report `Calor0270` at each receiver and remain
unresolved for analyses. Explicit `object` and `System.Object` inputs are known
types; an incompatible native overload reports `Calor0208`. An inferred match
capture retains the matched value's known reference type and nullable annotation.

Native member calls use the selected metadata method. For example,
`assembly.GetTypes()` retains its `System.Type[]` result for `each` inference.
A type parameter named `@String` does not acquire the .NET string indexer.
For effects, a matching manifest name alone cannot authorize an unselected
computed member call. Strict checking still requires a resolved callee and its
effect contract; raw C# and CLR conversions do not provide that contract.

## By-reference arguments

Declare parameters as `ref T:name`, `out T:name`, or `in T:name`. Mark the
argument with the corresponding modifier: `Bump(ref count)`,
`TryParse(text, out result)`, or `Twice(in count)`. A named argument puts its
name before the modifier: `TryParse(text, result: out number)`.

The callee receives the caller's storage, not a copy. Writes through `ref` or
`out` are visible after the call and require the callee to declare `mut`.

```calor
module RefOutDemo
  func Bump (ref int:x) → void
    ! mut
    set x x + 1

  func Twice (in int:x) → int
    return x * 2

  func ParseOr (string:text, int:fallback) → int
    var int:n = 0
    if System.Int32.TryParse(text, out n)
      return n
    return fallback

  func IsNumber (string:text) → bool
    return System.Int32.TryParse(text, out _)

  func Main () → int
    ! mut
    var int:count = 1
    Bump(ref count)
    return count + ParseOr("40", 0)
```

`Main()` returns `42`: `Bump` updates `count` to `2` and `ParseOr` returns
`40`. Calling `Twice(in count)` with that updated count returns `4` without
changing the caller's storage.

The modifier must match the selected parameter. A by-reference argument does
not fall back to a by-value overload. A mismatch is `Calor0208`.

The operand must identify permitted storage: a local, parameter, writable field,
array element, or a field path through value-type fields. Literals, computed
values, call results, properties, indexer properties, `this`, readonly fields
outside their constructor, and an `in` parameter passed as `ref` or `out` are
rejected with `Calor0212` before C# generation. An `out _` discard is also
rejected with `Calor0212` when a variable named `_` is in scope.

Passing an array element or a field through a receiver as `ref` or `out`
charges `mut`, like assignment to the same storage. Passing a local does not
add that storage effect. The callee's own declared effects still apply.

Null checks, preconditions, and refinements about a variable passed as `ref`
or `out` no longer hold after the call. Inside a function, by-reference
parameters can alias the same storage; a call or field access can invalidate
facts about another such parameter.

Declare and initialize an `out` binding before the call; Calor does not have
inline `out var` declarations. The converter introduces a binding for supported
C# placements and preserves unsupported placements. See the
[C# interoperability guide](https://calor.dev/docs/guides/csharp-interoperability/#call-tryparse-trygetvalue-and-other-ref-out-in-methods).

### Rejected computed operand

This example reports `Calor0212`: `v + 1` is a value, not writable storage.

```calor expect=Calor0212
module RefOutRejected
  func Bump (ref int:x) → void
    ! mut
    set x x + 1

  func Main () → int
    ! mut
    var int:v = 1
    Bump(ref (v + 1))
    return v
```

## Related

- [Bindings](/calor/syntax-reference/binding/) — inferred and explicitly typed locals.
- [Expressions](/calor/syntax-reference/expressions/) — precedence and expression-valued calls.
- [Effects](/calor/syntax-reference/effects/) — call effects and receiving boundaries.
