---
layout: default
title: Effects and Contracts Enforcement
nav_order: 7
---

# Effects and Contracts Enforcement

This document specifies how Calor enforces effects (§E) and contracts (§Q/§S) both at compile-time and runtime.

**Enforcement is enabled by default** because Calor is designed for coding agents, not humans. Agents generate annotations for free and maintain them consistently - there's no annotation burden to avoid.

For the motivation behind this design, see [The Verification Opportunity](/calor/philosophy/the-verification-opportunity/).

---

## Effect Code Mapping Table (Source of Truth)

| Surface Code | Internal Kind    | Internal Value  | Description                |
|--------------|------------------|-----------------|----------------------------|
| `cw`         | IO               | console_write   | Console output             |
| `cr`         | IO               | console_read    | Console input              |
| `fw`         | IO               | file_write      | File write/delete          |
| `fr`         | IO               | file_read       | File read/exists           |
| `net`        | IO               | network         | Network operations         |
| `http`       | IO               | http            | HTTP requests              |
| `db`         | IO               | database        | Database operations        |
| `time`       | Nondeterminism   | time            | System time access         |
| `rand`       | Nondeterminism   | random          | Random number generation   |
| `mut`        | Mutation         | heap_write      | Observable heap writes     |
| `throw`      | Exception        | intentional     | Intentional throw statements |

### Internal-only Kinds (Not User-Visible in v1)

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
- The `throw` effect applies only to intentional `§THROW` statements

**Missing §E means: no operational effects allowed**
- Contract-generated exceptions are not "operational effects"
- The function body must be free of IO, Mutation, and Nondeterminism

## Unknown External Call Policy

| Mode              | Behavior                                      | When              |
|-------------------|-----------------------------------------------|-------------------|
| `strict` (default)| Unknown calls → worst-case effects → error    | v1 implementation |
| `warn`            | Unknown calls → warning + assume worst-case   | Future option     |
| `stub-required`   | Unknown calls → compile error unless stubbed  | Future option     |

**Escape hatch design**: Wire through `CompilationOptions.UnknownCallPolicy` enum even though only `Strict` is implemented in v1.

## Compile-Time Enforcement

### Diagnostic Codes

| Code       | Name               | Description                                          |
|------------|--------------------|------------------------------------------------------|
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
   - Recursion within SCC resolves via fixpoint
4. **Check**: ComputedEffects ⊆ DeclaredEffects for each function

### Effect Inference Coverage

Effect inference covers:
- Statements: §P (print), §C (call), §THROW
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

System.IO.File::ReadAllText(System.String) → fr
System.IO.File::WriteAllText(System.String,System.String) → fw
System.IO.File::Exists(System.String) → fr
System.IO.File::Delete(System.String) → fw

System.Net.Http.HttpClient::GetAsync(System.String) → net
System.Net.Http.HttpClient::SendAsync(System.Net.Http.HttpRequestMessage) → net

System.Random::Next() → rand
System.DateTime::get_Now() → time
System.DateTime::get_UtcNow() → time
System.Guid::NewGuid() → rand
```

## Project-Level Stubs

Create a `calor.effects.json` file in your project directory:

```json
{
  "stubs": {
    "MyCompany.Logging.Logger::Log(System.String)": ["cw"],
    "MyCompany.Data.Repository::Save(MyCompany.Data.Entity)": ["db", "mut"],
    "MyCompany.Http.Client::Fetch(System.String)": ["net"]
  }
}
```

Project stubs override built-in catalog entries on conflict.

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
  <CalorContractMode Condition="'$(CalorContractMode)' == ''">debug</CalorContractMode>
  <CalorEnforceEffects Condition="'$(CalorEnforceEffects)' == ''">true</CalorEnforceEffects>
  <CalorUnknownCallPolicy Condition="'$(CalorUnknownCallPolicy)' == ''">strict</CalorUnknownCallPolicy>
</PropertyGroup>
```

## Example Usage

### Function with Effects

```
§M{m1:Greeting}
§F{f001:WriteGreeting:pub}
  §I{str:name}
  §O{void}
  §E{cw}
  §P name
§/F{f001}
§/M{m1}
```

### Pure Function with Contracts

```
§M{m1:Math}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §S (== (* result b) a)
  §R (/ a b)
§/F{f001}
§/M{m1}
```

### Function Calling Another with Effects

```
§M{m1:App}
§F{f001:LoadUserName:pri}
  §I{i32:userId}
  §O{str}
  §E{fr}
  // ... file read implementation
§/F{f001}

§F{f002:GreetUser:pub}
  §I{i32:userId}
  §O{void}
  §E{cw,fr}                    // Must declare both effects
  §B{name} §C{f001:LoadUserName} userId §/C
  §P name
§/F{f002}
§/M{m1}
```

## Error Examples

### Missing Effect Declaration

```
error Calor0410: Function 'greetUser' uses effect 'console_write' but does not declare it
  Call chain: greetUser → WriteGreeting → Console.WriteLine
```

### Unknown External Call

```
error Calor0411: Unknown external call to 'MyLib.Helper::DoSomething()'
  in strict mode. Add stub in calor.effects.json or use a known method.
```

## Version History

- **v1.0**: Initial implementation with strict mode only
- Future: `warn` and `stub-required` policies
