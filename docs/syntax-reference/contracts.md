---
layout: default
title: "Contracts"
parent: Syntax Reference
nav_order: 5
---

# Contracts

<!-- Generated from website/content/syntax-reference/contracts.mdx by calor self-check docs --fix. Edit the source, not this file. -->

Contracts state what a function requires from its caller and what it guarantees
on a normal return. Write preconditions with `requires` and postconditions with
`=>`, before the first body statement.

```calor
module ContractDivide
  func Divide (int:dividend, int:divisor) → int
    !
    requires divisor > 0
    requires dividend >= 0
    => result >= 0
    => result <= dividend
    return dividend / divisor
```

`Divide(9, 2)` returns `4`. Both input conditions are needed: allowing a negative
divisor would contradict the promised non-negative result.

## Preconditions

Each `requires` line is an ordinary Boolean expression over the function's
inputs. Multiple lines must all hold. With runtime checks enabled, a failed
precondition throws `Calor.Runtime.ContractViolationException` before the body
runs. For example, `Divide(9, 0)` fails its precondition rather than reaching
integer division.

Contracts do not replace the implementation or automatically prove every call
safe. A runtime test that exercises a guard is not a verifier proof.

## Postconditions and `result`

In a postcondition, `result` denotes the value the body returns, not a local
variable to declare or assign. A postcondition is checked on a normal return
when its runtime guard is enabled. It does not promise that the body cannot
throw.

```calor
module AbsoluteValue
  func Abs (int:x) → int
    !
    => result >= 0
    if x >= 0
      return x
    return 0 - x
```

`Abs(-5)` returns `5`. The default checked integer arithmetic throws
`OverflowException` for `Abs(int.MinValue)`; that execution does not reach a
normal return or establish the postcondition.

## Range validation

Separate a valid input range from the promised result range.

```calor
module ContractClamp
  func Clamp (int:value, int:min, int:max) → int
    !
    requires min <= max
    => result >= min
    => result <= max
    if value < min
      return min
    if value > max
      return max
    return value

  func Main () → int
    !
    return Clamp(-3, 0, 10) + Clamp(15, 0, 10) + Clamp(5, 0, 10)
```

`Main()` returns `15`. An inverted range violates the precondition; it is not
silently repaired.

## Non-empty collections

Use the actual collection type and its members in the contract.

```calor
module NonEmptyCollection
  func First (int[]:items) → int
    !
    requires items.Length > 0
    => result == items[0]
    return items[0]
```

`First(new int[] { 42 })` returns `42`. With checks enabled, an empty array
fails the precondition before indexing. The declared input is non-null; a
nullable input would need its own null guard before reading `Length`.

<a id="runtime-contract-enforcement"></a>
<a id="contract-modes"></a>
<a id="runtime-vs-static-verification"></a>

## Runtime modes and verification

`--contract-mode debug` emits detailed runtime checks. `--contract-mode release`
emits leaner checks; `--contract-mode off` emits none.
Turning checks off does not make invalid inputs valid,
prove a contract or prevent exceptions from the body.

The verifier reports `proven`, `refuted`, `assumed`, `unknown`, `timeout`,
`unsupported` or `unavailable`. Only a clean, non-vacuous, assumption-free
`proven` postcondition is eligible for proof-based runtime-guard removal with
`--verify`. Pass `--keep-proven-guards` to retain otherwise-emitted guards.
An inconclusive result does not authorize removal.

Runtime lowering has its own support boundary. Requested unsupported lowering,
including uncertified quantified checks, fails rather than silently dropping a
guard. See [verification guarantees and limits](https://calor.dev/docs/guides/verification-guarantees/).

## Source predicates

A contract can call a source function that returns `bool`, including a
selected static member such as `Predicates.Positive(x)`. Normal overload
resolution, accessibility and argument checking still apply.

```calor
module FunctionCallBoundary
  func Positive (int:value) → bool
    !
    return value > 0

  func Identity (int:x) → int
    !
    requires Positive(x)
    => Positive(result)
    return x
```

`Identity(1)` returns `1`. With checks enabled, `Identity(0)` fails its
precondition. Predicate receiver and argument expressions run once, in source
order. Turning contracts off removes their runtime evaluation, not the body.
Their effects must still fit the enclosing function even when runtime guards
are off; an effectful predicate cannot hide behind an omitted guard.
Inherited contracts contribute the calls that will actually execute.

Runtime support does not make a source-function call provable by Z3. Such
contracts remain unsupported for static verification and retain runtime
guards with verification enabled.

## Bounded string verification

Native calls such as `string.IsNullOrEmpty(s)`, `string.Concat(s, "d")` and
`s.Substring(1)` can use the verifier's existing string models when binding
selects the supported .NET string overload. A same-spelled source method or
shadowed receiver does not inherit that model. Named, generic, by-reference
and unsupported overloads remain unsupported for verification.

String proofs remain `assumed`, not `proven`, and retain runtime guards.
Substring models also retain their existing normal-return and index-range
restrictions. Culture-sensitive comparisons are not modeled; supported
comparison overloads require the actual `StringComparison.Ordinal` constant.
Nested calls are supported only when every evaluated receiver and argument
is itself modeled. This does not add arbitrary method inlining.

## Quantified contracts

`forall` checks every value in a range; `exists` checks whether at least one
value satisfies the predicate. Ranges are **half-open**: `start..end` means
`start <= i && i < end`, unlike the inclusive counted-loop range.
An empty range is true for `forall` and false for `exists`.

```calor-fragment
forall int:i in 0..n : items[i] >= 0
exists int:i in 0..n : items[i] == target
forall int:i in 0..n, int:j in 0..n : matrix[i,j] == matrix[j,i]
forall int:i where i >= 0 && i < n : items[i] >= 0
```

Keep the quantified bindings, guard and predicate on one physical line.
Multiple bindings are comma-separated; an inner bound may depend on an
earlier variable. Express implication as `!condition || consequence`, not a
special arrow operator. Quantifier variables are lexical bindings, not IDs.

### Matrix symmetry

Check the matrix's dimensions before indexing it in the quantified predicate.
`GetLength` uses the actual `System.Array` metadata method and its existing
effect contract.

```calor
module MatrixSymmetry
  func CheckSymmetric (int[,]:matrix, int:n) → bool
    !
    requires n >= 0
    requires matrix.GetLength(0) == n && matrix.GetLength(1) == n
    requires forall int:i in 0..n, int:j in 0..n : matrix[i,j] == matrix[j,i]
    return true
```

`CheckSymmetric(new int[,] { { 1, 2 }, { 2, 3 } }, 2)` returns `true`.
A non-square matrix or an asymmetric entry fails a precondition with guards
enabled. An empty `0 × 0` matrix passes for `n = 0`. Contract mode `off` removes
these checks; successful compilation is not a static proof of symmetry.
The compiler reports `Calor0325`, an informational notice about the nested
quantifier's quadratic runtime cost.

### All array elements non-negative

```calor
module NonNegativeArray
  func ProcessArray (int[]:arr, int:n) → bool
    !
    requires n >= 0 && n == arr.Length
    requires forall int:i in 0..n : arr[i] >= 0
    return true
```

`ProcessArray(new int[] { 0, 3 }, 2)` returns `true`. A negative element
fails the second precondition with checks enabled, before the body runs.

### Search precondition

```calor
module ArraySearch
  func Find (int[]:arr, int:target) → int
    !
    requires exists int:i in 0..arr.Length : arr[i] == target
    => result >= 0 && result < arr.Length
    => arr[result] == target
    for i from 0 to arr.Length - 1 step 1
      if arr[i] == target
        return i
    return -1
```

`Find(new int[] { 4, 9 }, 9)` returns `1`. This is a linear search, not a
claim that an unsorted input supports binary search. With checks enabled,
an absent target fails the precondition; with guards off, the body returns
`-1`.

### Runtime and static limits

Certified finite domains lower to `ContractQuantifier.Range(...).All(...)`
or `.Any(...)`. The compiler preserves guard order and short-circuiting;
potentially throwing bounds stay behind their guards. `Calor0326` rejects a
requested runtime guard whose finite domain cannot be lowered faithfully.
It is not silently replaced by a comment.

Signed and unsigned primitive integer domains retain their own bounds.
Nested quantifiers cost the product of their domain sizes at runtime:
two ranges of lengths `n` and `m` may require `n * m` predicate evaluations.
A proof result does not make unsupported requested runtime lowering valid.

If runtime quantifier lowering is the only compilation error, explicitly
requested static verification still reports its result, including `Unsupported`
for nested quantifiers. Compilation fails without generated code.

Write a proof-obligation statement as `proof condition`, or
`proof{"description"} condition` to name it. Formatting preserves its
condition and description, including escaped quotes and newlines.

## Typed .NET calls in verified contracts

The verifier recognizes a bounded set of ordinary string calls after exact
receiver and overload resolution: `Contains`, `Equals`, `Substring`,
`string.IsNullOrEmpty`, and `string.Concat` with two to four string arguments.
`StartsWith`, `EndsWith` and `IndexOf` require an explicit
`System.StringComparison.Ordinal`; ordinal `Contains` and `Equals` overloads
are also modeled. Culture-sensitive, named, by-reference, generic and other
unsupported overloads retain their runtime checks.

String and integer-array `Length`, and metadata-resolvable `List<T>.Count`,
can be modeled as signed integers from zero through `Int32.MaxValue`,
including inside integer quantifiers. Modeling a list's count does not model
its element reads or make an arbitrary producer pure.
Verification lowering does not change the executable AST or evaluation order.

## State preservation

`=> result == x + 1` can describe an increment's result. It does not snapshot
arbitrary heap state. There is no native `old` expression on this page: a
balance-transfer precondition is not a proof of total-balance conservation.
Choose explicit state representation and postconditions rather than inventing
an old-value syntax.

<a id="contract-ordering"></a>
<a id="custom-error-messages"></a>

## Declaration order and failure messages

Put the signature first, followed by effect and contract lines, then the body.
Keep contracts above the first statement; they are declaration clauses, not
statements to execute in the middle of a function.

The native `requires` form does not have a custom-message clause. When an API
needs a particular exception type and message, use an explicit condition and
exception in the body rather than inventing a contract attribute.

## Related references

- [Types](https://calor.dev/docs/syntax-reference/types/) — arrays, nullable inputs and exceptions
- [Effects](https://calor.dev/docs/syntax-reference/effects/) — declarations for side effects
- [Verification guarantees](https://calor.dev/docs/guides/verification-guarantees/) — proof outcomes and guard removal
