---
layout: default
title: Syntax Reference
nav_order: 4
has_children: true
permalink: /syntax-reference/
---

# Syntax Reference

Calor uses lowercase declaration headers, indentation and ordinary C# expression
operators. The [generated current-language reference](/calor/syntax-reference/current-language/)
is the compiler-checked catalog of constructs and complete examples.

## Quick reference table

The entries below are syntax fragments. An effect or contract line belongs
inside its function, before the first body statement.

| Element | Native syntax |
|:--------|:--------------|
| Compilation unit | `module FileLabel Optional.Namespace` |
| Named static container | An indented `module Utilities` |
| Function or method | `func Add (int:a, int:b) → int` |
| Parameter | `int:value` in the signature |
| Return | `return a + b` |
| Call | `Math.Max(a, b)` |
| Immutable binding | `let int:count = 3` or `let count = 3` |
| Mutable binding | `var int:count = 3` or `var count = 3` |
| Assignment | `set count count + 1` |
| Effects | `! cw, fs:w`; a bare `!` declares no effects |
| Precondition | `requires divisor != 0` |
| Postcondition | `=> result >= 0` |
| Branch | `if condition`, `else if condition`, `else` |
| Counted loop, snapshot bounds | `for i from 1 to 10 step 1` |
| Collection iteration | `each item in items` |
| While loop | `while condition` |
| Lambda | `(int:x) ! => x * 2` |
| Member access and indexing | `value.Name`, `items[index]` |
| Interpolation | `"hello ${name}"` |
| Block end | Dedent to the parent column |

Use two spaces per indentation level. Expressions use parentheses, commas and
brackets where C# uses them; there are no call or block closer tags.
See [calls](/calor/syntax-reference/calls/),
[bindings](/calor/syntax-reference/binding/) and
[control flow](/calor/syntax-reference/control-flow/) for complete examples.

## Modules and namespaces

The root module's first name is a compilation-unit label, not a namespace or a
static class name. The optional second name supplies the C# namespace.
Omitting it means the global namespace.

An indented named module is different: it is a static container.

```calor
module LabelExample Demo
  module Arithmetic
    func Add (int:a, int:b) → int
      !
      return a + b

  func Main () → int
    !
    return Arithmetic.Add(20, 22)
```

`Demo.DemoModule.Main()` returns `42`. `Arithmetic` is the named static
container; `LabelExample` is neither that container nor the namespace.

Native header imports retain compilation-unit scope. C# namespace-local imports
and more complex namespace topologies can require faithful converter
preservation; moving an import is not a semantics-preserving shortcut.

## Types

Use C# type names and type-first bindings and parameters.

| Type | Meaning |
|:-----|:--------|
| `int`, `long` | Signed 32-bit and 64-bit integers |
| `float`, `double` | Binary floating-point values |
| `decimal` | Decimal arithmetic |
| `string`, `char`, `bool` | Text, UTF-16 code unit, Boolean |
| `void` | No body result |
| `T?` | Nullable type |
| `T[]` | Array |
| `List<T>`, `Dictionary<TKey,TValue>` | Ordinary .NET generic collections |

See [types](/calor/syntax-reference/types/) for nullability, arrays, exceptions and
collections, and [inheritance](https://calor.dev/docs/syntax-reference/inheritance/) for classes,
constructors and interface dispatch.

## Operators

Expressions use infix operators with C# precedence. Parentheses make grouping
explicit: `a + b * c` and `(a + b) * c` are different expressions.

| Category | Operators |
|:---------|:----------|
| Arithmetic | `+`, `-`, `*`, `/`, `%` |
| Comparison | `==`, `!=`, `<`, `<=`, `>`, `>=` |
| Logical | `&&`, `\|\|`, `!` |
| Conditional expression | `condition ? whenTrue : whenFalse` |

Integer arithmetic is checked by default. String operations use ordinary members
such as `text.Length` and `text.Contains(part, System.StringComparison.Ordinal)`;
see [string operations](/calor/syntax-reference/string-operations/).

## Effect Codes

| Code | Effect |
|:-----|:-------|
| `cw` | Console output |
| `cr` | Console input |
| `fs:r` | Filesystem read |
| `fs:w` | Filesystem write |
| `net:rw` | Network read/write |
| `db:rw` | Database read/write |
| `alloc` | Allocation |
| `mut` | Heap mutation |
| `throw` | Explicit exceptions |

An effect row bounds the side effects of a function or callback. Common codes
include `cw` and `cr` for console output and input, `fs:r` and `fs:w` for
filesystem access, and `alloc`, `mut` and `throw` for allocation, mutation and
exceptions. A call to an unresolved operation is not silently treated as pure.

Preconditions use `requires`; postconditions use `=>` and can refer to `result`.
Runtime checks, effect enforcement and verifier proofs are separate mechanisms.
See [effects](/calor/syntax-reference/effects/) and
[contracts](/calor/syntax-reference/contracts/) for their exact boundaries.

## ID conventions

Declaration IDs do not appear in source. Tools maintain persistent identities in
the project's `.calor/ids.json` sidecar and synchronize active compilation inputs.
Use supported rename and move tools to carry identity across refactoring.
Plain-text reconciliation refuses ambiguous matches.

Names such as `count` and `items` are ordinary program identifiers, not identity
fields. See [stable identifiers](/calor/philosophy/stable-identifiers/).

## Documentation comments

Put `///` comments immediately before the declaration they document.
Consecutive lines form one comment; a blank line or ordinary `//` comment
ends the association. The formatter preserves documentation comments.
`--require-docs` and `--strict-api` recognize them.

```calor
/// Arithmetic helpers.
module DocumentedArithmetic
  /// Adds two integers.
  func Add (int:x, int:y) → int
    !
    return x + y
```

Version annotations also use documentation comments: `/// since 1.0.0`,
`/// deprecated 2.0.0 Replacement`, and `/// breaking 3.0.0 "Description"`.
They are not source identity fields. See the
[generated catalog](/calor/syntax-reference/current-language/#introduction-version-metadata)
for the supported metadata forms.

Frozen benchmark archives preserve the language originally measured.
They are historical inputs, not current parser-compatibility examples.
Active-syntax regression copies do not remeasure those experiments or change
their ledgers and measured commits.

## Complete example

```calor
module FizzBuzz
  func Classify (int:value) → string
    !
    if value % 15 == 0
      return "FizzBuzz"
    else if value % 3 == 0
      return "Fizz"
    else if value % 5 == 0
      return "Buzz"
    return value.ToString()

  func Main () → void
    ! cw
    for i from 1 to 100 step 1
      Console.WriteLine(Classify(i))
```

`Main()` prints the FizzBuzz sequence from one through 100. `Classify(15)`,
`Classify(3)`, `Classify(5)` and `Classify(7)` return `"FizzBuzz"`, `"Fizz"`,
`"Buzz"` and `"7"` respectively. Blocks end by dedenting. The
[control-flow reference](/calor/syntax-reference/control-flow/) adds loops and
other branching forms.

## Detailed reference

- [Current language](/calor/syntax-reference/current-language/) — generated construct catalog and executable examples
- [Declaration structure](/calor/syntax-reference/structure-tags/) — declarations and block organization
- [Types](/calor/syntax-reference/types/) — primitive types, nullability and collections
- [Bindings](/calor/syntax-reference/binding/) — immutable and mutable locals
- [Calls](/calor/syntax-reference/calls/) — arguments, overloads and by-reference calls
- [Expressions](/calor/syntax-reference/expressions/) — operators and expression forms
- [Control flow](/calor/syntax-reference/control-flow/) — loops, conditionals and matching
- [Contracts](/calor/syntax-reference/contracts/) — preconditions, postconditions and runtime guards
- [Effects](/calor/syntax-reference/effects/) — declarations and callback rows
- [String operations](/calor/syntax-reference/string-operations/) — strings, regex and formatting
- [Value constraints](/calor/syntax-reference/refinement-types/) — input contracts and their limits
