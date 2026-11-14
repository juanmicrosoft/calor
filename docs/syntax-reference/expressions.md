---
layout: default
title: "Expressions"
parent: Syntax Reference
nav_order: 3
---

# Expressions

<!-- Generated from website/content/syntax-reference/expressions.mdx by calor self-check docs --fix. Edit the source, not this file. -->

Calor expressions use infix operators with C# precedence, ordinary postfix calls
and indexing. Write `a + b`, `items[index]` and `value.Method(argument)`.
Parentheses group an expression; they do not introduce a prefix operation.

Runtime string holes also accept calls and indexing, as in
`"${Format(x)}:${labels[0]}"`. Their receivers and indices evaluate in place.

Keywords and operators are case-sensitive. Use the symbolic operators below,
not word aliases such as `add` or `eq`. Pattern combinators `and`, `or` and
`not` belong to patterns, not ordinary Boolean expressions.

## Precedence and grouping

The table runs from strongest to weakest binding.

| Category | Operators or forms |
|:---------|:-------------------|
| Postfix | Calls, member access, indexing |
| Unary | `!`, `~`, unary `+` and `-` |
| Multiplicative | `*`, `/`, `%` |
| Additive | `+`, `-` |
| Shift | `<<`, `>>` |
| Relational | `<`, `<=`, `>`, `>=` |
| Equality | `==`, `!=` |
| Bitwise AND | `&` |
| Bitwise XOR | `^` |
| Bitwise OR | `\|` |
| Logical AND | `&&` |
| Logical OR | `\|\|` |
| Null coalescing | `??` |
| Conditional | `condition ? whenTrue : whenFalse` |

Binary operators associate to the left, except `??`, which associates to the
right. Conditional expressions also associate to the right.
Use parentheses when grouping would otherwise be hard to read.

```calor
module ExpressionPrecedence
  func Main () → (int, int, int, int, int, int, int, int, bool, int, int)
    !
    let int?:missing = null
    let int:coalesced = missing ?? 3 + 4 * 2
    let int:conditional = false ? 1 : true ? 2 : 3
    return (2 + 3 * 4, (2 + 3) * 4, 12 - 5 - 2, 32 / 4 / 2, 1 << 2 + 1, 6 & 3, 6 ^ 3, 4 | 1, !false && true || false, coalesced, conditional)
```

`Main()` returns the tuple `(14, 20, 5, 4, 8, 2, 5, 5, true, 11, 2)`.
For example, `1 << 2 + 1`
means `1 << (2 + 1)`, and `32 / 4 / 2` means `(32 / 4) / 2`.

## Arithmetic operators

| Operator | Meaning |
|:---------|:--------|
| `+`, `-` | Addition and subtraction |
| `*` | Multiplication |
| `/` | Division |
| `%` | Remainder |

Integer division truncates toward zero. A nonzero integer remainder has the
dividend's sign; it is not always a positive mathematical modulo.
Integer arithmetic is checked by default. Overflow and integer division by zero
throw exceptions rather than produce a successful result.

```calor
module ArithmeticBehavior
  func Increment (int:value) → int
    !
    return value + 1

  func Divide (int:numerator, int:denominator) → int
    !
    return numerator / denominator

  func Remainder (int:numerator, int:denominator) → int
    !
    return numerator % denominator

  func Main () → (int, int, int)
    !
    return (Increment(41), Divide(-7, 3), Remainder(-7, 3))
```

`Main()` returns `(42, -2, -1)`. `Increment(2147483647)` throws
`OverflowException`, `Divide(1, 0)` throws `DivideByZeroException`, and
`Divide(-2147483648, -1)` throws `OverflowException`.
These are body exceptions, not failed runtime contract checks.

### Explicit overflow scopes

`checked(expression)` and `unchecked(expression)` override the surrounding
integer overflow policy, including narrowing casts. Scopes nest; the innermost
scope controls each operation. They do not change operand order or how many
times an operand runs, and do not change arithmetic inside a called function.
Division by zero still throws in either scope.

```calor
module OverflowExpressions
  func Wrap (int:value) → int
    !
    return unchecked(value + 1)

  func Narrow (long:value) → int
    !
    return checked((int)value)
```

`Wrap(2147483647)` returns `-2147483648`. `Narrow(2147483648L)` throws
`OverflowException`. Use `@checked` and `@unchecked` for ordinary identifiers
with these names. Explicit overflow scopes currently remain unmodeled by the
contract verifier; they do not justify eliding a runtime guard.

## Comparison and logical operators

Comparisons use two operands: `a == b`, `a != b`, `a < b`, `a <= b`,
`a > b` and `a >= b`. Write null explicitly: `value != null`.
Logical operators are `&&`, `||` and unary `!`.

Combine two comparisons for a range check. `minimum <= value <= maximum`
is not a chained comparison.

```calor
module ComparisonExpressions
  func InRange (int:value, int:minimum, int:maximum) → bool
    !
    return value >= minimum && value <= maximum

  func Recognized (int:value) → bool
    !
    return value == 1 || value == 2 || value == 3

  func HasValue (string?:value) → bool
    !
    return value != null

  func Main () → string
    !
    return "${InRange(5, 0, 10)}:${InRange(11, 0, 10)}:${Recognized(2)}:${Recognized(4)}:${HasValue(null)}:${HasValue("")}"
```

`Main()` returns `True:False:True:False:False:True`. An empty string is not null.

## Evaluation order and short circuiting

Operands and call arguments evaluate left to right. `&&` evaluates its right
operand only when the left is true; `||` evaluates it only when the left is false.
`??` evaluates its fallback only when the left value is null. A conditional
expression evaluates only its selected branch.

This program records the expressions that actually run. Its helper's `mut` row
declares the list mutation; the caller also declares the list allocation.

```calor
module ExpressionEvaluation
  func Record (List<int>:trace, int:value) → int
    ! mut
    trace.Add(value)
    return value

  func Pair (int:left, int:right) → int
    !
    return left * 10 + right

  func Main () → ((bool, bool, bool, bool), (int, int, int, int), List<int>)
    ! alloc, mut
    let List<int>:trace = new List<int>()
    let bool:andSkipped = false && Record(trace, 1) > 0
    let bool:orSkipped = true || Record(trace, 2) > 0
    let bool:andTaken = true && Record(trace, 3) > 0
    let bool:orTaken = false || Record(trace, 4) > 0
    let int:chosen = true ? Record(trace, 5) : Record(trace, 6)
    let int?:present = 7
    let int?:missing = null
    let int:a = present ?? Record(trace, 7)
    let int:b = missing ?? Record(trace, 8)
    let int:pair = Pair(Record(trace, 9), Record(trace, 10))
    return ((andSkipped, orSkipped, andTaken, orTaken), (chosen, a, b, pair), trace)
```

`Main()` returns three groups: the booleans `(false, true, true, true)`,
the integers `(5, 7, 8, 100)`, and the trace list.
The trace is exactly `3, 4, 5, 8, 9, 10`: skipped branches contribute nothing,
and each selected expression runs once.

## Using expressions

Expressions can appear in bindings, returns, call arguments, conditions,
contracts and loop bounds.

| Position | Example fragment |
|:---------|:-----------------|
| Binding | `let int:sum = a + b` |
| Return | `return (x - 1) * 2` |
| Call argument | `Console.WriteLine(x * x)` |
| Condition | `if value > 0` |
| Precondition | `requires divisor != 0` |
| Postcondition | `=> result >= 0` |
| Range bound | `for i from 0 to count - 1 step 1` |

A console call needs `cw` in its caller's effect row. Snapshot range bounds
evaluate once; the semicolon loop form reevaluates its condition.
See [control flow](https://calor.dev/docs/syntax-reference/control-flow/) and
[contracts](https://calor.dev/docs/syntax-reference/contracts/).

### Expression-valued allocation sizes

Array dimensions are expressions at the allocation site, not quoted source
text. Their calls retain normal order and short-circuit behavior:

```calor
module ArrayDimensions
  func Size (int:n) → int
    !
    return n + 1

  func Main () → int
    ! alloc
    let int[]:values = new int[Size(2)]
    return values.Length
```

`Main()` returns `3`. Moving `Size(2)` ahead of a conditional branch would
change the program if that helper had effects; syntax migration must not do so.

## Collection expressions

Collections use ordinary .NET members, not dedicated query tags.

Dictionary construction supports collection initializers:
`new Dictionary<string, int>() { { "alice", 30 }, { "bob", 40 } }`.
Each key and value evaluates in source order, followed by that pair's `Add`.
A duplicate key throws before later entries run.

| Expression | Meaning |
|:-----------|:--------|
| `items.Contains(value)` | Membership in a list or set |
| `mapping.ContainsKey(key)` | Dictionary key lookup |
| `mapping.ContainsValue(value)` | Dictionary value lookup |
| `items.Count` | Collection count |
| `array.Length` | Array length |
| `items[index]` | Indexed element |

```calor
module CollectionExpressions
  func NonEmptyCount (List<int>:items) → int
    !
    requires items.Count > 0
    => result > 0
    // Get count
    return items.Count

  func Main () → (bool, bool, bool, int, int)
    ! alloc, mut
    let List<int>:items = new List<int>()
    items.Add(5)
    let Dictionary<string,int>:ages = new Dictionary<string,int>()
    ages.Add("alice", 30)
    // Check if list contains element
    return (items.Contains(5), ages.ContainsKey("alice"), ages.ContainsValue(30), NonEmptyCount(items), items[0])
```

`Main()` returns `(true, true, true, 1, 5)`.
Use an ordinary property read for `Count` or `Length`, not a zero-argument call.
See [types](https://calor.dev/docs/syntax-reference/types/) for collection construction and
[calls](https://calor.dev/docs/syntax-reference/calls/) for metadata overload resolution.

## Native computed member reads

The compiler resolves `(value ?? "none").Length` using the actual
`System.String` property identity when `value` is a `string?` parameter.
The same bounded resolution covers conditional string receivers and resolved
string-returning calls. It does not infer a property's type or purity merely
from the spelling `Length`.

The receiver runs once. A coalescing fallback or conditional branch runs only
when selected, and its effects still contribute to the enclosing callable.
Nullable annotations remain attached to resolved values. Unknown, source,
foreign, opaque or type-parameter receivers do not acquire a string purity
contract by using the same member name.

For `value?.Length.ToString()`, the continuation selects `int.ToString()` on
the underlying `Length` value. The complete result is still nullable: a null
`value` skips the getter, the call and its arguments. The receiver evaluates
once. Assigning that nullable result to a non-nullable destination still
requires a fallback or a separate proof.

Parenthesized conditional receivers such as `(value?.Length).ToString()` remain
unsupported native postfix forms. They must not be interpreted as the chain
above: C# invokes `Nullable<int>.ToString()` outside the conditional region,
which returns an empty string when the lifted value is null.

## Type intrinsics

Use `typeof(T)`, `nameof(name)` and `sizeof(T)` with ordinary parentheses.
For example, `sizeof(int)` returns `4`. Other types follow C#'s rules for
`sizeof` and unsafe contexts.

## Next

- [Current language](https://calor.dev/docs/syntax-reference/current-language/) — generated syntax catalog
- [String operations](https://calor.dev/docs/syntax-reference/string-operations/) — interpolation and string members
- [Control flow](https://calor.dev/docs/syntax-reference/control-flow/) — loops, conditionals and matching
