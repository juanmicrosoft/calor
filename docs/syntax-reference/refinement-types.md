---
layout: default
title: Refinement Types
parent: Syntax Reference
nav_order: 6
---

# Value Constraints

Use ordinary .NET types and `requires` clauses to constrain function inputs.
Use `=>` postconditions to describe the result. Retired named refinements,
inline refinement predicates and self-reference syntax are not current source
forms.

A precondition does **not** create a new nominal type, attach an invariant to
every local assignment or establish that a verifier proved every caller safe.
Those are different guarantees.

## Constraining a parameter

This function accepts an `int` port in the inclusive range 1 through 65535.

```calor
module PortConstraint
  func SelectPort (int:port) → int
    !
    requires port >= 1 && port <= 65535
    => result == port
    return port

  func Main () → int
    !
    return SelectPort(443)
```

`Main()` returns `443`. With runtime contracts enabled, `SelectPort(0)` and
`SelectPort(65536)` throw `ContractViolationException`.

The parameter's type is still `int`. Repeating this precondition in another
function does not create two distinct port types, nor does it implicitly
validate every integer binding in the program.

## Non-empty text

State null and length requirements explicitly. `&&` short circuits, so the
length read does not run when the value is null.

```calor
module TextConstraint
  func Length (string:text) → int
    !
    requires text != null && text.Length > 0
    => result > 0
    return text.Length

  func Main () → int
    !
    return Length("calor")
```

`Main()` returns `5`. With runtime contracts enabled, null or empty text fails
the precondition. A .NET caller can still supply null at runtime despite the
non-nullable parameter annotation; the runtime guard is a separate check.

## Relating several parameters

A precondition can relate inputs directly. No special predicate self-reference
is needed.

```calor
module TransferConstraint
  func Transfer (int:balance, int:amount) → int
    !
    requires balance >= 0
    requires amount > 0
    requires balance >= amount
    => result >= 0
    return balance - amount

  func Main () → int
    !
    return Transfer(10, 3)
```

`Main()` returns `7`. `Transfer(3, 5)` fails its sufficient-funds precondition
when runtime contracts are enabled. The preconditions together also bound the
successful subtraction; a successful example execution alone is not a verifier
proof of that property.

## Entry conditions are not local invariants

A precondition applies at function entry. It does not turn an ordinary mutable
local into a permanently constrained value.

```calor
module ConstraintBoundary
  func Reset (int:count) → int
    !
    requires count >= 0
    var int:remaining = count
    set remaining -1
    return remaining

  func Main () → int
    !
    return Reset(5)
```

`Main()` returns `-1`, even with runtime contracts enabled. `remaining` is an
ordinary `int`, not an erased named refinement with a guard on each write.

If your API needs distinct domain types, use an ordinary class or record and
design its construction and mutation rules explicitly. A precondition on one
function is not a substitute for that type's whole lifecycle.

## Runtime checks and verification

The [contracts reference](/calor/syntax-reference/contracts/) explains enabled and
off runtime contract modes. Disabling checks does not make invalid calls safe:
`SelectPort(0)` then returns `0`, `Transfer(3, 5)` returns `-2`, and empty text
has length `0`. Null text reaches a body `NullReferenceException`.

Effect enforcement remains independent of runtime contract mode. A pure row
does not prove a precondition or prevent ordinary body exceptions.

Verifier results must be read with their actual status, assumptions and
supported expression coverage. Unsupported, timed-out, assumption-dependent or
vacuous results are not clean proofs. Only an eligible proof from the relevant
compilation can authorize proof-based guard removal; running a program or seeing
a successful C# build does not do so.

## Proof obligations

`proof condition` asserts a Boolean condition at a body point. It is not a
new type or an input declaration. Keep the condition in native expression
syntax; do not attach a source ID or a historical description header.

```calor
module TransferProof
  func Transfer (int:balance, int:amount) → int
    !
    requires balance >= 0
    requires amount > 0 && balance >= amount
    proof balance - amount >= 0
    return balance - amount
```

`Transfer(10, 3)` returns `7`. This version states the proof over parameters:
the obligation solver's support for arbitrary local facts is not implied by
a successful runtime assertion. Only an eligible proof from this compilation
can authorize guard removal. `--keep-proven-guards` retains otherwise-emitted
guards; a timeout or unsupported expression is not a proof.

Without a proof tracker, this statement emits a guard that throws
`InvalidOperationException` if false. `--contract-mode off` disables
precondition and postcondition guards, **not** this standalone proof guard:
`Transfer(3, 5)` then reaches the proof and throws rather than returning `-2`.
With ordinary precondition checks enabled, that call fails earlier with
`ContractViolationException`.

## Named and indexed types

This page replaces the retired refinement authoring guide. It does not promise
the old named-type distinction or per-assignment refinement guards.
Named refinement definitions, inline self-reference predicates and indexed
type aliases are not native authoring forms. The old erased aliases did not
become ordinary distinct CLR domain types.

Use the real collection type and explicitly relate its length to an index.
A precondition does not create an indexed type or remove the CLR's bounds
check by itself:

```calor
module ArrayIndexConstraint
  func At (int[]:items, int:index) → int
    !
    requires index >= 0 && index < items.Length
    => result == items[index]
    return items[index]
```

`At(new int[] { 4, 9 }, 1)` returns `9`. With checks enabled, index `2`
fails the precondition; with guards off, the array access itself throws.
This is not a replacement guarantee of automatically discharged index-bounds
obligations on every array operation.

## See also

- [Contracts](/calor/syntax-reference/contracts/) — preconditions, postconditions and guard modes
- [Bindings](/calor/syntax-reference/binding/) — immutable and mutable locals
- [Types](/calor/syntax-reference/types/) — .NET types and nullability
- [Static verification](/calor/philosophy/static-verification/) — verifier concepts and limitations
