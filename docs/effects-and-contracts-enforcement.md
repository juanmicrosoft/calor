---
layout: default
title: Effects and Contracts Enforcement
nav_order: 7
---

# Effects and Contracts Enforcement

This document specifies how Calor enforces effects (§E) and contracts (§Q/§S) both at compile-time and runtime.

**Enforcement is enabled by default** because Calor is designed for coding agents, not humans. Agents generate annotations for free and maintain them consistently - there's no annotation burden to avoid.

For the motivation behind this design, see [Effects & Contracts Enforcement](/calor/philosophy/effects-contracts-enforcement/).

---

## Effect Code Mapping Table (Source of Truth)

| Surface Code | Internal Kind    | Internal Value  | Description                |
|--------------|------------------|-----------------|----------------------------|
| `cw`         | IO               | console_write   | Console output             |
| `cr`         | IO               | console_read    | Console input              |
| `fs:w`       | IO               | file_write      | File write/delete          |
| `fs:r`       | IO               | file_read       | File read/exists           |
| `fs:rw`      | IO               | file_readwrite  | File read + write          |
| `net:r`      | IO               | network_read    | Network read (GET)         |
| `net:w`      | IO               | network_write   | Network write (POST/PUT)   |
| `net:rw`     | IO               | network         | Network read + write       |
| `http`       | IO               | http            | HTTP requests              |
| `db:r`       | IO               | database_read   | Database read (SELECT)     |
| `db:w`       | IO               | database_write  | Database write (INSERT/UPDATE/DELETE) |
| `db:rw`      | IO               | database        | Database read + write      |
| `time`       | Nondeterminism   | time            | System time access         |
| `rand`       | Nondeterminism   | random          | Random number generation   |
| `mut`        | Mutation         | heap_write      | Observable heap writes     |
| `throw`      | Exception        | intentional     | Intentional throw statements |

### Internal-only Kinds (Not User-Visible)

- **Allocation**: Memory allocation. **Not required in §E** to avoid every function needing `alloc`.
- **Unknown**: Worst-case for unresolved external calls.

## Mutation Definition

**Mutation (`mut`) means observable heap writes:**
- Field or property sets on objects that were **not allocated within the current function**
- Field or property sets on parameters (unless marked explicitly mutable, if such marking exists)

**Does NOT include:**
- Local variable assignment
- Writes to objects allocated within the same function scope
- Contract instrumentation

## Contract Effect Semantics

**Contract checks do NOT require a `throw` effect declaration.**
- Contracts are a verification mechanism, not a business effect
- A function with `§Q` and `§S` but no `§E` is valid and compiles
- The `throw` effect applies only to intentional `§TH` statements

**Missing §E means: no operational effects allowed**
- Contract-generated exceptions are not "operational effects"
- The function body must be free of IO, Mutation, and Nondeterminism

## Unknown External Call Policy

| Mode              | Behavior                                      | When              |
|-------------------|-----------------------------------------------|-------------------|
| `strict` (default)| Unknown calls → worst-case effects → error    | Current implementation |
| `warn`            | Unknown calls → warning + assume worst-case   | Future option     |
| `stub-required`   | Unknown calls → compile error unless stubbed  | Future option     |

**Escape hatch design**: Wire through `CompilationOptions.UnknownCallPolicy` enum even though only `Strict` is currently implemented.

## Compile-Time Enforcement

### Diagnostic Codes

| Code       | Name               | Description                                          |
|------------|--------------------|------------------------------------------------------|
| Calor0406  | EffectInferenceDidNotConverge | The SCC fixpoint (cap 100 rounds, floored at group size + 1) or the instantiated-charge worklist (cap 10 000 steps) stopped with effect sets still changing; always an error |
| Calor0410  | ForbiddenEffect    | Function uses effect not declared in §E              |
| Calor0411  | UnknownExternalCall| Call to unknown external method in strict mode       |
| Calor0412  | MissingSpecificEffect | A specific effect is missing from declaration     |
| Calor0413  | AmbiguousStub      | Multiple stubs match call signature                  |

### SCC-Based Interprocedural Analysis

The effect enforcement pass uses Strongly Connected Component (SCC) analysis:

1. **Build call graph** from AST (track callee → caller edges)
2. **Compute SCCs** using Tarjan's algorithm
3. **Process SCCs in reverse topological order**:
   - For each SCC, iterate functions until effects stabilize
   - Recursion within SCC resolves via fixpoint (capped at 100 rounds or the group's size + 1, whichever is larger; reaching the cap with sets still changing is Calor0406, an error)
4. **Check**: ComputedEffects ⊆ DeclaredEffects for each function

### Effect Inference Coverage

Effect inference covers:
- Statements: §P (print), §C (call), §TH
- Expressions: CallExpressionNode
- **Property getters/setters**: Treat as calls, need stubs
- **Constructors**: `Type::.ctor()` in catalog
- **Lambdas**: Nested body contributes to enclosing function
- **Async/await**: Treat awaited call as contributing effects

### Call Chain Tracking

- Store (function, span) pairs during traversal
- Report minimal chain in diagnostic: `f001 → f002 → Console.WriteLine`

## Built-in Effects Catalog

The catalog uses fully-qualified signatures for disambiguation:

```
Namespace.Type::Method(ParamType1,ParamType2)
Namespace.Type::get_PropertyName()
Namespace.Type::set_PropertyName(System.String)
Namespace.Type::.ctor(System.String)
```

### Matching Rules

1. Exact match wins
2. If multiple matches exist, emit `Calor0413` (AmbiguousStub) and require disambiguation

### Standard Library Coverage

```
System.Console::WriteLine(System.String) → cw
System.Console::Write(System.String) → cw
System.Console::ReadLine() → cr

System.IO.File::ReadAllText(System.String) → fs:r
System.IO.File::WriteAllText(System.String,System.String) → fs:w
System.IO.File::Exists(System.String) → fs:r
System.IO.File::Delete(System.String) → fs:w

System.Net.Http.HttpClient::GetAsync(System.String) → net:r
System.Net.Http.HttpClient::SendAsync(System.Net.Http.HttpRequestMessage) → net:rw

System.Random::Next() → rand
System.DateTime::get_Now() → time
System.DateTime::get_UtcNow() → time
System.Guid::NewGuid() → rand
```

`System.String` has no blanket pure contract. Its built-in coverage is limited
to exact signatures for `IsNullOrEmpty(string)`, `ToUpperInvariant()`,
`ToLowerInvariant()`, `ToString()`, `Equals(string)`, `Equals(string,string)`,
`CompareOrdinal(string,string)`, `Contains(char)`, `IndexOf(char)`,
the `String(char[]?)` constructor, and the `Length` getter.
Unlisted members and overloads remain Unknown.

`String(char[]?)` copies UTF-16 code units without invoking user code or
ambient culture. It returns an empty string for null or empty input and
otherwise copies the array; later array writes cannot change the string.
It does not validate Unicode sequences and has no argument-error resource
lookup. Allocation and runtime allocation failure follow the existing
allocation-only convention, not a guarantee of successful allocation.
Every other String constructor remains Unknown.

`ToCharArray()` remains Unknown: the call site's null-receiver check can
trigger exception resource lookup through the virtual `CurrentUICulture.Name`
getter before method entry. Successful copies within the method do not prove
the whole call safe. The ranged
`ToCharArray(int,int)` overload also remains Unknown.

Selected metadata String instance calls and getters require a proven non-null
receiver. This is intentionally stricter than C# nullable warnings: the
`callvirt` null check runs before the contracted method body, and constructing
its exception can invoke an overridden `CurrentUICulture.Name` getter.
Nullable, oblivious, and unknown receivers fail the hard receiving boundary
(`Calor0274`), even with type checking disabled. Casting null to `string` is
not proof. Non-null declarations and values, sound flow guards, non-null
coalescing, and conditional access retain their existing annotations and
behavior. Static helpers and constructors are not instance receiving
boundaries; unlisted overloads still need an independent effect contract.

In particular, `ToUpper()` and `ToLower()`, including their `CultureInfo`
overloads, can invoke user-defined `TextInfo` getters. Culture-sensitive
comparisons, formatting, and enumerable overloads can also dispatch to user
code. They cannot inherit permission from a safe overload's name. Strict
compilation rejects unresolved effects rather than guessing a `mut` row.

`System.Int32`, `System.Int64`, `System.Double`, and `System.Boolean` also
have no default or method-name-wide pure contracts. Their finite coverage is
`Equals(T)`, `CompareTo(T)` (where `T` is the declaring type), and
`GetHashCode()`. Boolean additionally covers `ToString()` and
`ToString(IFormatProvider)`: both return fixed literals and ignore the provider.
These .NET 10 implementations use value comparisons and bit operations;
Double's NaN and signed-zero helpers do not dispatch to user code.

The exact static `Single.IsNaN(Single)` and `Double.IsNaN(Double)` overloads
are also covered. Both .NET 10 bodies compare the argument with itself and
return the unordered result. They have no calls, type initializers, provider
dispatch or exception helpers. No other Single member receives a contract.
Metadata argument lookup preserves `float` as `System.Single`, not an
unresolved placeholder; this type fact grants no additional effects.

Numeric `Parse`, `TryParse`, and `ToString` overloads remain Unknown.
`NumberFormatInfo.GetInstance` can call an arbitrary provider's `GetFormat`;
its current-culture path can call a `CultureInfo` override. Even parameterless
integer formatting reaches that path for negative values. Passing an invariant
provider at one call site does not justify a contract for every provider.
Boolean `Parse` is also omitted: successful parsing avoids culture dispatch,
but invalid input reaches exception resource lookup through the virtual
`CurrentUICulture.Name` getter. Unlisted overloads, including Boolean
`TryParse` with its output write, have no purity claim.

Native interpolation has a separate, bounded .NET 10 proof for fixed-width
integer `X`/`x` holes. Literal formats with zero to two precision digits take
the bit/hex path for either sign without reading a provider. The complete
interpolation, including constant alignment, must fit in the default handler's
initial 256-character buffer. Its selected handler and integer types must be
the runtime metadata definitions, not source lookalikes. Custom handlers,
expression-tree/lambda contexts, other formats and larger results receive no
new permission. Hole-expression effects remain charged. This is not a manifest
grant for numeric `ToString`; see [compilation](cli/compile.md).

One contextual exception is a selected `Int32.ToString()` whose receiver is
the directly bound core-library `String.Length` getter. String lengths are
nonnegative, so .NET 10 formats them through unsigned decimal conversion
before any current-culture lookup. Its small-number cache contains only
runtime-created decimal strings; cache initialization and string allocation
follow the existing allocation-only convention. Direct and conditional access
retain receiver evaluation effects and the getter's non-null boundary.
This is not a general integer contract: locals, constants, array lengths,
source getters named `Length`, nullable integers, boxed/interface receivers,
and format/provider overloads do not acquire this proof. Existing source
initializer and callback authority checks are unchanged.

`System.Convert` has no type default, wildcard, or method-name-wide grant,
including in the crypto manifest. Only the exact `ToString(string?)`,
`ToString(bool)`, and `ToString(char)` overloads, with or without an
`IFormatProvider`, are covered. String returns its input unchanged, including
null. Boolean returns a fixed literal. Char allocates a one-code-unit string
and writes only that new string. All six ignore the provider and ambient
culture; allocation follows the existing allocation-only convention.

`Convert.ToString(object?)` can call `IConvertible.ToString`,
`IFormattable.ToString`, or a virtual `object.ToString` override. Numeric
formatting can reach a provider or current-culture callback, including on
the parameterless negative-integer path. These overloads remain Unknown,
not pure because a base implementation or one input happens to be safe.
Wrapping an uncontracted call in interpolation or string addition does not
make it pure.
All other Convert members, including base64 and hex helpers, remain
uncontracted; this is not a claim that every omitted overload has an effect.

The dispatch review uses .NET 10 CoreLib source at
[`0427b9abbe`](https://github.com/dotnet/dotnet/tree/0427b9abbead3fa64c6655eb4290c537397131fa/src/runtime/src/libraries/System.Private.CoreLib/src/System),
cross-checked against the selected runtime members and their IL.
This is bounded contract coverage, not a proof of the whole BCL.

Selected metadata constants, such as `System.Math.PI`, do not evaluate their
namespace/type qualifiers or run a type initializer. This does not grant purity
to same-spelled source getters or to static readonly fields.
The type checker also uses the selected type of a nonconstant static metadata
field instead of treating its namespace as a variable. This type information is
not an effect contract: static readonly reads still require independent effect proof.

## Project-Level Effect Manifests

Create a `.calor-effects.json` file in your project directory to declare effects for types not covered by the built-in manifests:

```json
{
  "version": "1.0",
  "description": "Effects for MyCompany types",
  "mappings": [
    {
      "type": "MyCompany.Logging.Logger",
      "methods": {
        "Log": ["cw"]
      }
    },
    {
      "type": "MyCompany.Data.Repository",
      "methods": {
        "Save": ["db:w", "mut"]
      }
    },
    {
      "type": "MyCompany.Http.Client",
      "methods": {
        "Fetch": ["net:r"]
      }
    }
  ]
}
```

Project-level manifests have the highest priority and override all other sources. See `docs/guides/effect-manifests.md` for the full manifest schema.

## Runtime Contract Enforcement

### ContractViolationException

When a contract fails, Calor throws `Calor.Runtime.ContractViolationException`:

```csharp
public class ContractViolationException : Exception
{
    public string FunctionId { get; }
    public ContractKind Kind { get; }  // Requires, Ensures, Invariant

    // Span as offset/length (canonical)
    public int StartOffset { get; }
    public int Length { get; }

    // Derived (for human readability)
    public string? SourceFile { get; }
    public int Line { get; }
    public int Column { get; }
}
```

### Contract Modes

| Mode      | Behavior                                                |
|-----------|---------------------------------------------------------|
| `off`     | No contract checks emitted                              |
| `debug`   | Full message: function ID, source span, condition text  |
| `release` | Lean message: exception type + function ID only         |

**CLI**: `--contract-mode off|debug|release`

## MSBuild Integration

Add these properties to your project file or `Directory.Build.props`:

```xml
<PropertyGroup>
  <CalorEnforceEffects Condition="'$(CalorEnforceEffects)' == ''">true</CalorEnforceEffects>
  <CalorPermissiveEffects Condition="'$(CalorPermissiveEffects)' == ''">false</CalorPermissiveEffects>
</PropertyGroup>
```

| Property | Default | CLI equivalent | Meaning |
|---|---|---|---|
| `CalorEnforceEffects` | `true` | `--enforce-effects` / `--no-enforce-effects` | Run the effect checker at all. |
| `CalorPermissiveEffects` | `false` | `--permissive-effects` | Permissive policy, for converted code. `Calor0410` ("uses effect X but does not declare it") is reported as a **warning** instead of an error, in the per-file pass and in the cross-module pass alike. `Calor0411` (unknown external call) and `Calor0425` ("cannot be decided whether the effects fit") are **suppressed** — not reported at all. |

Both properties are strict MSBuild booleans: only `true` / `false` (any casing) are accepted,
and a value such as `1` or `banana` fails the build with `MSB4030`, not silently.

`Calor0411` is a **warning** under MSBuild even at the strict default, because it is an error
only under `--strict-effects` and the MSBuild task exposes no equivalent property; permissive
turns it off rather than demoting it.

**What permissive does not waive.** The waiver is bounded, and the bound is the part worth
knowing: an effect row that does not fit its destination (`Calor0424`) and an override or
interface implementation that **broadens** the effects it inherited (`Calor0420` /
`Calor0421`) stay **errors under every flag**. (Narrowing is legal — an override may promise
less than its base method, never more.) `--permissive-effects` relaxes what the compiler
assumes about calls it cannot resolve; it never relaxes a row the code states and then
contradicts.

The value is part of the build cache's options fingerprint, so switching it recompiles every
file once (the first build after the switch is a full one).

## Example Usage

### Function with Effects

```
§M{Greeting}
  §F{WriteGreeting:pub}
    §I{str:name}
    §O{void}
    §E{cw}
    §P name
```

### Pure Function with Contracts

```
§M{Math}
  §F{Divide:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §Q (!= b 0)
    §S (== (* result b) a)
    §R (/ a b)
```

### Function Calling Another with Effects

```
§M{App}
  §F{LoadUserName:priv}
    §I{i32:userId}
    §O{str}
    §E{fs:r}
  // ... file read implementation

  §F{GreetUser:pub}
    §I{i32:userId}
    §O{void}
    §E{cw, fs:r}                  // Must declare both effects
    §B{name} §C{f001:LoadUserName} userId §/C
    §P name
```

## Error Examples

### Missing Effect Declaration

```
error Calor0410: Function 'greetUser' uses effect 'console_write' but does not declare it
  Call chain: greetUser → WriteGreeting → Console.WriteLine
```

### Unknown External Call

```
error Calor0411: Unknown external call to 'MyLib.Helper.DoSomething()'.
  Add effect declaration in a .calor-effects.json manifest.
```

## Version History

- **1.0**: Initial implementation with strict mode only
- Future: `warn` and `stub-required` policies
