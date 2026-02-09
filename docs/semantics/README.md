# Calor Formal Semantics

## Why Formal Semantics Matter for Agent-Authored Code

**An agent-friendly language needs a spec that is tighter than "it emits C#."**

Emitting C# can hide a lot of semantic gaps. When an agent writes Calor code, it needs to know *exactly* what that code will do—not what the C# compiler happens to do today, or what one version of the .NET runtime does differently than another.

If the semantics are not crisp, you get "works on this compiler version" behavior, and that kills trust.

---

## The Problem with "It Compiles to X"

Consider a language that "compiles to JavaScript" or "emits C#". What does this code do?

```
result = f(a(), b()) + g(c())
```

The answer depends on:
- **Evaluation order**: Is `a()` called before `b()`? Before `c()`? The backend language may not specify this.
- **Side effects**: If `a()` modifies state that `b()` reads, the result depends on call order.
- **Overflow behavior**: Does `INT_MAX + 1` wrap, trap, or produce undefined behavior?
- **Null semantics**: What happens when you dereference `null`?

If your spec says "emits C#", you've delegated these decisions to C#—but C# may not fully specify them, or may change them between versions.

**For agents, this is catastrophic.** An agent trained on one set of behaviors will generate incorrect code when those behaviors change or differ.

---

## Calor's Solution: Crisp, Backend-Independent Semantics

Calor defines its semantics **independently of any backend**. The C# emitter must conform to Calor semantics, not define them.

### What We Specify

| Concern | Calor's Decision | Why It Matters |
|---------|-----------------|----------------|
| **Evaluation Order** | Strictly left-to-right | Agents can reason about side-effect ordering |
| **Short-Circuit** | `&&`/`||` always short-circuit | Predictable control flow |
| **Scoping** | Lexical with explicit shadowing | No surprises from dynamic lookup |
| **Integer Overflow** | TRAP by default | Safety-first; silent bugs are unacceptable |
| **Type Conversions** | Explicit for narrowing | Prevents accidental data loss |
| **Nullability** | `Option<T>` for optional values | No null pointer exceptions |
| **Exceptions** | Typed, with semantic meaning | `ContractViolationException` carries context |

### Why This Matters for Agents

1. **Trainable Rules**: Agents can be trained on precise semantics, not approximations.
2. **Testable Behavior**: Every semantic decision has corresponding tests.
3. **Version Stability**: Semantics are versioned; agents know which rules apply.
4. **Trust**: Code behaves the same regardless of backend implementation details.

---

## The Three Pillars

### 1. Precise Definitions

Every construct has a formal definition:

- **Evaluation order**: Section 2 of `core.md`
- **Scoping and shadowing**: Section 3 of `core.md`
- **Lifetimes**: Defined by scope boundaries
- **Numeric behavior**: Section 4 of `core.md`

### 2. Precise Mapping to .NET

The backend spec (`dotnet-backend.md`) defines exactly how Calor semantics map to .NET:

- When direct emission is safe (C# matches Calor)
- When additional code is required (checked arithmetic, temporaries)
- Type mappings (Calor `i32` → C# `int`)
- Exception types and their contents

### 3. Stable Versioning

Agents will be trained and prompted against specific rules. The versioning spec (`versioning.md`) ensures:

- **Semantic Versioning**: MAJOR.MINOR.PATCH with clear upgrade rules
- **Module Declaration**: Code declares which semantics version it targets
- **Compatibility Checking**: Compiler warns/errors on version mismatches
- **Migration Path**: Clear guidance for upgrading between versions

---

## How It Works: The CNF Pipeline

Calor uses an intermediate representation called **Calor Normal Form (CNF)** to enforce semantics:

```
Source → Parser → AST → TypeChecker → Binder → CNF Lowering → CNF → C# Emitter → C#
                                                    ↑
                                          Semantics enforced here
```

CNF makes semantics explicit:
- **Explicit temporaries**: Evaluation order is baked in
- **Explicit control flow**: No implicit fall-through
- **Explicit types**: No implicit conversions
- **Explicit labels/branches**: Short-circuit lowered to control flow

See `normal-form.md` for the full CNF specification.

---

## Test-Backed Semantics

Every semantic decision is backed by tests in `tests/Calor.Semantics.Tests/`:

| Semantic | Test | What It Verifies |
|----------|------|------------------|
| S1 | `FunctionArguments_EvaluatedLeftToRight` | Arguments evaluated in order |
| S2 | `BinaryOperators_EvaluatedLeftToRight` | Operators evaluated in order |
| S3 | `LogicalAnd_ShortCircuits` | `&&` doesn't evaluate right if left is false |
| S4 | `LogicalOr_ShortCircuits` | `||` doesn't evaluate right if left is true |
| S5 | `LexicalScoping_InnerReadsOuter` | Inner scope accesses outer variables |
| S6 | `ReturnFromNestedScope` | Return unwinds to function boundary |
| S7 | `IntegerOverflow_Traps` | Overflow throws `OverflowException` |
| S8 | `NumericConversion_IntToFloat` | Implicit widening works |
| S9 | `OptionNone_BehavesCorrectly` | `Option<T>` semantics correct |
| S10 | `RequiresFails_ThrowsContractViolation` | Contracts throw with function ID |
| S11 | `SemanticsVersionMismatch_EmitsDiagnostic` | Version checking works |

---

## Documents in This Directory

| Document | Purpose |
|----------|---------|
| `inventory.md` | Catalog of all 134 AST constructs |
| `core.md` | **Core semantics specification** (evaluation, scoping, numerics, contracts) |
| `normal-form.md` | CNF intermediate representation specification |
| `dotnet-backend.md` | How .NET backend implements Calor semantics |
| `versioning.md` | Semantic versioning for agent training stability |

---

## For Agent Developers

If you're building agents that generate Calor code:

1. **Train on the spec, not the output**: Use `core.md` as the source of truth
2. **Version your prompts**: Include `§SEMVER{1.0.0}` in generated modules
3. **Test against semantics tests**: Your generated code should pass the same tests
4. **Don't assume C# behavior**: If it's not in the Calor spec, don't rely on it

---

## Current Version

**Semantics Version: 1.0.0**

This is the initial formal semantics specification. See `versioning.md` for version history and upgrade guidance.
