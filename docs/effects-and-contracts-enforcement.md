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

Direct bound calls to an unannotated private/internal function can instantiate
bare `ref`/`out` stores against proved caller-owned local storage. The callee's
inferred row and callable-value row remain unchanged. Heap/global writes,
public declared rows, unresolved calls and caller by-reference storage still
charge their full effects. Nested calls, recursive calls, initializer bodies
and escaping callables do not acquire this local-storage proof.

## Contract Effect Semantics

**Contract checks do NOT require a `throw` effect declaration.**
- Contracts are a verification mechanism, not a business effect
- Preconditions and postconditions do not add operational effects.
- The `throw` effect applies only to intentional `throw` statements.

Postconditions bind `result` with the declared return type, including `string`
versus `string?`. Nullable results still need a non-null guard before member
access. A nullable return from a nonnullable function remains an error.

**Missing declarations depend on the callable boundary.**
- Private and internal functions and methods infer their rows from their bodies.
- Public/protected functions/methods, public operators, interface/abstract/extern methods, and
  delegates require an explicit own row (`Calor0124`); `!` declares Pure.
- An own annotation on a private/internal body is a parse error (`Calor0125`).
- Contract-generated exceptions are not "operational effects"
- Public callers still declare every reachable operational effect.

Non-public inference converges across recursive components and higher-order row
charges before variance checks. Unknown bodies remain errors in strict mode;
declaration-only inputs cannot stand in for analyzed bodies. Inferred body rows
do not grant contracts to open virtual slots or method-group values that can
dispatch to another implementation. Constructor, accessor and initializer
contracts are unchanged. Callback parameter and return types require explicit
rows regardless of optional type checking or effect-enforcement settings
(`Calor0407`). Detection uses selected bound callable types, not a blanket
`Func`/`Action` name match. An explicit Unknown row preserves the prior unknown
contract and does not authorize invocation.

The C# converter omits synthesized own rows on private/internal functions and
concrete methods. Recompilation infers those bodies rather than trusting a
printed permission. Callback parameter/return rows and their effect binders
remain explicit; abstract and extern declarations retain boundary rows.
Unproved callback positions are emitted as `! unknown`, never Pure. Probe
boundary rows are restrictive budgets, not evidence that a body satisfies them.
Public callers must still cover every reachable effect.

Since 0.30 the converter also writes `! unknown` as the own row of any function
or method whose strict inference is Unknown — typically a call to an API with no
effect contract. Permissive inference would have printed a narrower row (often
`!`) that the next strict compile rejects with `Calor0410`. The marker is honest
about the gap: the output compiles under default settings, and every caller is
charged `unknown` until a manifest entry supplies the contract.

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

The built-in manifests declare these members pure, as Calor 0.29 did:

- every `System.String` member, the `Length` getter and the `String(char[])`
  constructor;
- `Parse`, `TryParse` and `ToString` on `Int32`, `Int64`, `Double` and
  `Boolean`, each of which also has a pure type default;
- every `System.Convert` member, including the base64 and hex helpers in the
  crypto manifest;
- the `System.Linq.Enumerable` operators and the `System.Linq` namespace default;
- `Object.Equals`, `GetHashCode`, `GetType`, `ReferenceEquals` and
  `MemberwiseClone`;
- `System.Nullable<T>` (type default), plus the exact `Single.IsNaN` and
  `Double.IsNaN` overloads and the `CultureInfo.InvariantCulture` getter.

Interpolating or concatenating a value of a built-in type, or a `Nullable<T>` of
one, charges what the manifest records for that type's parameterless
`ToString()`. Other types, including source types and type parameters, stay
Unknown, because formatting them can run an arbitrary `ToString` override.

**Known gap.** These contracts are approximations, not proofs. Culture-sensitive
formatting and parsing read `CultureInfo.CurrentCulture`, whose providers can be
overridden. `Enumerable` operators call the delegates, enumerators and comparers
they are given. `Object.Equals` and `GetHashCode` dispatch to overrides. A
November 2026 review withdrew these contracts. The withdrawal was reverted on
2026-11-14 because it stopped ordinary code such as `n.ToString()` from
compiling and turned converted code into preserved C#, which has no effect
checking at all. Replacing them with exact, sound contracts is tracked as a
separate issue.

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
