---
layout: default
title: Bindings
parent: Syntax Reference
nav_order: 3
---

# Bindings

Use `let` for an immutable local binding and `var` for a mutable one.
An explicit type precedes the name, as it does in a function parameter.
Use `set` to assign a new value to mutable storage.

## Syntax

```calor-fragment
let name = expression
let Type:name = expression
var name = expression
var Type:name = expression
var Type:name
set name expression
```

An inferred binding requires `= expression` on its declaration. A declared
type supplies the receiving boundary; it does not make an incompatible value
valid. Explicit types use C# spellings such as `int`, `long`, `double`,
`string`, and `string?`.

An explicitly typed mutable local can be declared before it is initialized,
but every path to a read must assign it first. Generated C# validation still
checks definite assignment. A local declaration is not a class field; use
`field` for fields.

<a id="inference-table"></a>
<a id="examples"></a>

## Inferred bindings

The binder uses the initializer's bound type. Literal examples include
`42` (`int`), `"Calor"` (`string`), `true` (`bool`), and `3.14` (`double`).
An identifier retains its declared type; a call retains its resolved return
type; an operator supplies the result type, not necessarily an operand's type.
Internal tool output may use names such as `INT` and `STRING`; these are not
the type spellings to write in source.

Inferred locals emit C# `var`; explicitly typed locals emit the declared
type. An annotation is not silently discarded during emission.

```calor
module BindingExamples
  func Main () → int
    !
    let score = 85
    let name = "Calor"
    let active = true
    let pi = 3.14
    var counter = 0
    var total = 0
    for i from 1 to 10 step 1
      set counter counter + 1
      set total total + i
    return score + counter + total
```

`Main()` returns `150`: the loop runs ten times and the sum of `1` through
`10` is `55`. The counted-loop variable `i` is read-only.

## Explicit types and initialization

An annotation can request a supported implicit conversion, such as `int` to
`long`. It cannot turn a string into an integer or erase nullable input.
When an initializer spans lines, put `=` on the declaration line and indent
its continuation.

```calor
module ExplicitBindings
  func Pick (bool:first) → int
    !
    var int:value
    if first
      set value 7
    else
      set value 9
    let long:wide = value
    let double:fraction = wide
    return value

  func Main () → int
    !
    return Pick(true) + Pick(false)
```

`Main()` returns `16`. Both branches initialize `value` before it is read.
Give a binding an explicit initializer when it can be initialized immediately.

## Diagnostics

### Missing inferred initializer

The native parser reports `Calor0100` for an inferred binding without
`= expression`. It rejects the declaration before the binder's historical
`Calor0250` missing-type-or-initializer check can run.

```calor expect=Calor0100
module MissingInitializer
  func Main () → void
    let value
```

### Read-only storage

Writing an immutable `let` local or a counted-loop variable reports
`Calor0213`, including writes through `ref` or `out`. Declare a reassigned
local with `var`. If a loop body must change its counter, use a mutable
binding with `while` instead of assigning the counted-loop variable.

```calor expect=Calor0213
module ReadOnlyBinding
  func Main () → int
    let value = 1
    set value 2
    return value
```

### Strict inference

Strict binding inference is enabled by default. `Calor0251` rejects an
inferred bare `null`; supply an appropriate nullable type instead.

```calor expect=Calor0251
module InferredNull
  func Main () → void
    let value = null
```

`Calor0253` rejects ambiguous inference from mixed integer and floating-point
literal operands. An explicit receiving type makes the intended conversion
clear.

```calor expect=Calor0253
module MixedNumericInference
  func Main () → void
    let value = 0 + 0.0
```

`Calor0252` covers unsupported generic-return inference. A type annotation
does not create a missing generic method or fix an invalid call.

The CLI option `--no-strict-bind-inference`, or
`CompilationOptions.StrictBindInference = false`, disables the strict
inference checks. It does not disable ordinary type compatibility or
supported nullability-boundary errors.

### Nullable initialization

`Calor0272` rejects a supported possibly-null initializer at a non-null
receiving boundary:

```calor expect=Calor0272
module NullableBinding
  func Main () → void
    let string?:input = null
    let string:value = input
```

Inference retains supported initializer nullability; omitting an annotation
does not make a nullable call result non-null. Initializing a mutable binding
is checked, but later assignment is outside this bounded guarantee.
Constructor inputs are a separate, uncovered boundary.
See [Nullability and .NET Interop](https://calor.dev/docs/guides/nullability-and-dotnet-interop/)
for complete safe and rejected examples. Native returns and method inputs
use `Calor0273` and `Calor0274`.

## Conditional declarations

Statement-level `pp condition`, `pp elif condition`, and `pp else` branches
are checked independently. One branch cannot read another branch's bindings.
Conditional compilation does not itself introduce a C# scope, so a binding
declared at the top of a branch can remain visible after the conditional.

When branches declare the same name, later writes respect the strictest
mutability: if any branch declares it with `let`, it remains read-only.
Writing it reports `Calor0213`, even when the mutable branch is selected.

```calor expect=Calor0213
module ConditionalBinding
  func Main () → int
    pp DEBUG
      let value = 1
    pp else
      var value = 2
    set value 3
    return value
```

Redeclaring a local from the surrounding statement scope reports
`Calor0258`, because `pp` does not introduce a C# scope. Incompatible
branch types, or a declaration that conditionally changes which outer
symbol a name refers to, report `Calor0201`. A type annotation is not a
way to bypass these checks.

## Collection compatibility and scope

An array can implement `IEnumerable<T>` or `IList<T>` without being a
`List<T>`. `Calor0254` catches supported array-to-concrete-collection
mismatches in bindings, returns, assignments and same-module call arguments.
Known array-returning framework calls and declared source return types supply
this information; a method's name alone does not establish its type.

Normal type checking reports `Calor0202` for this resolved source return type:

```calor expect=Calor0202
module ArrayIsNotList
  func Values () → int[]
    ! alloc
    return new int[]{1, 2}

  func Main () → void
    ! alloc
    let List<int>:items = Values()
```

Do not treat this targeted diagnostic as complete signature resolution for
every constructor, operator, external method or lambda. Ordinary compatibility
checks and generated C# validation still apply. Earlier type checking can
report `Calor0202` before the targeted pass runs. In particular, a direct array
initializer assigned to `List<int>` currently reaches generated C# validation
and reports `Calor1002`, rather than the targeted `Calor0254`.

`Calor0255` rejects a new local that shadows an enclosing local, parameter or
loop variable. Sibling blocks can independently reuse names, and a local can
hide a field.

```calor expect=Calor0255
module ShadowedBinding
  func Main (int:value) → int
    if value > 0
      let int:value = 1
    return value
```

A `var` binding whose name already exists in a live scope reassigns that
storage rather than shadowing it. Prefer `set` when reassignment is intended.
Its type stays fixed: incompatible mutable rebinds report `Calor0256`, and
rebinding an immutable local reports `Calor0260`. `Calor0258` rejects duplicate
immutable declarations in one scope. `Calor0257` rejects writes to
collection-iteration variables. A type annotation cannot change the type of
storage that already exists.

## Round-trip

A C# `var x = 42;` converts to `let x = 42` when immutable, and an explicitly
typed declaration retains its type as `let int:x = 42`. Mutable declarations
use `var`, with later assignments written as `set x expression`.
The C# emitter uses `var` for inferred declarations and the named type for
explicit ones. Formatting does not add a type annotation that was absent.
The converter chooses mutability from the source writes, not from the C#
`var` keyword alone.

## See also

- [Types](/calor/syntax-reference/types/) — C# type spellings.
- [Expressions](/calor/syntax-reference/expressions/) — initializer expressions and precedence.
- [Calls](/calor/syntax-reference/calls/) — resolved return types and by-reference arguments.
