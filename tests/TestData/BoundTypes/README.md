# Bound-type corpus expectations

The corpus test retains 18 logical subjects under `samples/` and `benchmarks/`.
Their frozen originals and `displaystring-corpus-golden.json` are not current
syntax fixtures. `registered_ledger_historical_replay.py bound-types` archives
compiler `a728e0e2a985d5dd11a092f2d463f24e30f4c66c`, its original test, registry,
and inputs, then executes exactly one passing original golden test. It checks
that today's frozen originals and golden have not changed. The original
aggregate is 656 bound expressions; current mirrors never replace that replay.

`displaystring-current-expectations.json` is an ordinary current-compiler
fixture. It records every subject's exact histogram and bound function order.
The test requires all 18 registered active mirrors, zero parse/binding errors,
identical histograms after native parse/bind/emit/parse/bind, and the original
`>400` expression floor. Neither a parse failure nor an exception can remove a
subject.

Every native module header explicitly retains the original emitted namespace
and module wrapper identity; the corpus test checks both. TypeSystem's typed
catch names its exception variable so the handler is not consumed as malformed
header data.

| Logical subject | Original | Current |
|---|---:|---:|
| benchmarks/arithmetic/div-by-zero.calr | 47 | 50 |
| benchmarks/arithmetic/overflow.calr | 35 | 47 |
| benchmarks/loops/bounds-violation.calr | 84 | 97 |
| benchmarks/null-safety/null-deref.calr | 39 | 43 |
| benchmarks/security/command-injection.calr | 92 | 122 |
| benchmarks/security/path-traversal.calr | 49 | 59 |
| benchmarks/security/sql-injection.calr | 63 | 77 |
| samples/Collections/collections.calr | 31 | 101 |
| samples/Contracts/contracts.calr | 16 | 45 |
| samples/FizzBuzz/fizzbuzz.calr | 22 | 30 |
| samples/Generics/generics.calr | 18 | 36 |
| samples/HelloWorld/hello.calr | 1 | 3 |
| samples/PatternMatching/matching.calr | 91 | 101 |
| samples/SdkSample/program.calr | 6 | 18 |
| samples/TypeSystem/typesystem.calr | 24 | 36 |
| samples/Verification/mixed-contracts.calr | 12 | 29 |
| samples/Verification/proven-contracts.calr | 19 | 63 |
| samples/Verification/violation-detected.calr | 7 | 25 |
| **Total** | **656** | **982** |

These are different representations, not 326 newly proven expressions. Current
binding visits contract clauses. Native calls expose their receiver and call
expression, including explicit `Console.WriteLine` calls. Collection operations
become ordinary .NET creation, calls, and indexing instead of specialized opaque
statements. Native spellings such as `int`, `string`, `string?`, and
`IReadOnlyList<T>` replace retired spellings. Generic class names retain arity.
Structural `.Count` reads remove two original dotted-name binding errors, but
their bound result remains honestly `OBJECT`, with no selected bound metadata
member. The separate AST checker now uses an annotated C# projection to select
the actual metadata property and its scalar result type. `GetCount<T>` no longer
fails with `Calor0202`; this is typing evidence, not bound-result or getter-effect
authority.

A separate selected-getter identity proof carries the actual metadata property
and constructed CLR receiver into the existing effect-key path. It selects the
existing ``System.Collections.Generic.List`1`` catalog contract; it does not
supply an effect row or add a permission. Opaque receivers, source getters,
invalid or ambiguous selections, and configuration-dependent identities cannot
borrow this proof. A selected scalar getter with no catalog contract still fails
strict enforcement with `Calor0411` and `Calor0410`.

All eleven sample mirrors now compile through default `Program.Compile`, with
type checking and effect enforcement enabled, before and after native emission.
Independent C# compilation and execution of the generic sample and focused
function verify empty and nonempty `List<int>` and `List<string>` counts.
Explicit pure-row controls also pass strict enforcement using the existing
List contract; source lookalikes and effectful receivers retain their negative
controls. Two non-scalar `ProcessStartInfo.ArgumentList`
reads retain their informational `Calor0270` diagnostics; they are not claimed
as supported scalar metadata reads.

## Explicit representation boundaries

The Contracts mirror retains `requires b != 0` on `Divide`; it is not replaced
with a body guard. The original malformed message attribute produces
`message= divisor must not be zero ""`. The native contract produces
`Precondition failed: b != 0`. Independent original/current C# runtime checks
pin both exact messages, the unchanged `ContractViolationException` type,
`ContractKind.Requires`, condition, and valid return value. This mirror is
deliberately **not** described as fully behavior-identical.

The overflow mirror preserves checked arithmetic using one actual
converter-produced `checked-block` interop frame, not native checked-expression
support. Its exact payload is `"        checked { result = a + b; }\n"`:
36 UTF-8 bytes, including eight leading spaces and the newline. The test compares
the entire frame and metadata with fresh converter output. Independent C#
oracles compare the original checked expression, equivalent checked assignment,
and emitted mirror on normal, maximum, positive-overflow, and negative-overflow
inputs. No benchmark process, shell, or filesystem payload is executed.

Scalar metadata controls retain all 9/1 `Replace` and 2 `GetHashCode` calls and
compare their selected CLR methods with Roslyn, including actual receiver
identity. Lambda controls retain structural parameter/return slots and explicit
display strings, with the disclosed native `i32`/`str` to `int`/`string` spelling
change. The receiver side channel retains both authoritative `StringBuilder`
and genuinely unresolved receiver types; a pure variant still fails effect
enforcement.

Structural field/property reads emit a spaced member separator, including
verbatim value receivers such as `@int . Length`. Re-emission must retain a real
member node rather than fuse it into a dotted identifier. Native class properties
bind through their accessible non-static declarations and readable getters,
including their declared type and generic substitution. Getter visibility is
checked in the selected property's declaring type before its scalar type can
select a constructor. Foreign setter-only, private-getter, private-property, and
static-through-instance reads retain no such authority. All conditional
alternatives must be readable; filtering out an unreadable branch cannot grant
authority to the remaining branch. Public get-only and
generic getters remain usable, including a public getter with a private setter.
Protected and private-protected reads from a derived class require a receiver
whose static type is that derived class or a subtype, not its base class.
Same-class private getters remain readable through named instances. The
private-protected control uses declaration visibility inherited by its getter;
it does not claim support for compound restricted-accessor syntax.
The generic getter control proves binding and independently compiled emitted C#,
not the separate closed-generic getter effect-closure boundary.
Struct storage-path handling and existing `this`/`base` policies stay separate.
The fixed Money converter control requires native operator/property conversion
with zero interop, substitution, or drop, and preserves its original C# runtime
oracle after a second native round trip.
