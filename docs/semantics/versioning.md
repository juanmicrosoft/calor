# Calor Semantics Versioning Specification

Version: 1.0.0

This document specifies how Calor semantics are versioned and how version compatibility is managed.

---

## Why Versioning Matters for Agents

> **Agents will be trained and prompted against specific rules.**

When an agent generates Calor code, it relies on specific semantic behaviors:
- "Overflow traps" (not wraps)
- "Left-to-right evaluation" (not unspecified)
- "`&&` short-circuits" (not eager)

If these rules change between versions without clear versioning:
1. Agents trained on v1 rules will generate incorrect code on v2 compilers
2. Prompts that describe v1 behavior will mislead agents on v2
3. Code that "worked before" will silently break

**Stable versioning ensures that agents know exactly which rules apply.**

---

## 1. Version Format

Calor semantics versions follow [Semantic Versioning 2.0.0](https://semver.org/):

```
MAJOR.MINOR.PATCH
```

- **MAJOR**: Breaking semantic changes (agents must be retrained)
- **MINOR**: Backward-compatible semantic additions (old code still works)
- **PATCH**: Clarifications, bug fixes in semantics (no behavior change)

---

## 2. Current Version

**Semantics Version: 1.0.0**

This is the initial formal semantics version for Calor.

---

## 3. Version Declaration

### 3.1 Module Declaration

Modules can declare their required semantics version:

```calor
§M{m001:MyModule}
  §SEMVER{1.0.0}
  ...
§/M{m001}
```

### 3.2 Syntax Variants

```calor
// Exact version
§SEMVER{1.0.0}

// Minimum version (any 1.x.x compatible)
§SEMVER{^1.0.0}

// Range
§SEMVER{>=1.0.0 <2.0.0}
```

---

## 4. Compatibility Rules

### 4.1 Patch Version Changes (x.y.Z)

**Fully compatible.** Changes include:
- Documentation clarifications
- Specification bug fixes
- Test additions

**Example:** 1.0.0 → 1.0.1 is safe.

### 4.2 Minor Version Changes (x.Y.z)

**Backward compatible.** Changes include:
- New constructs added
- New operators added
- New optional behaviors
- Extended standard library

**Example:** Code written for 1.0.0 will compile and run correctly under 1.1.0.

### 4.3 Major Version Changes (X.y.z)

**May be incompatible.** Changes may include:
- Evaluation order changes
- Operator precedence changes
- Type system changes
- Default behavior changes
- Removed constructs

**Example:** Code written for 1.x may not work correctly under 2.0.

---

## 5. Compiler Behavior

### 5.1 Version Checking

The compiler checks declared versions against its supported semantics:

```csharp
// src/Calor.Compiler/SemanticsVersion.cs
public static class SemanticsVersion
{
    public const int Major = 1;
    public const int Minor = 0;
    public const int Patch = 0;
    public static readonly Version Current = new(Major, Minor, Patch);
}
```

### 5.2 Diagnostic Codes

| Code | Severity | Condition |
|------|----------|-----------|
| Calor0700 | Warning | Module version newer than compiler (might work) |
| Calor0701 | Error | Module version incompatible (major mismatch) |

### 5.3 Checking Logic

```csharp
public static void CheckSemanticsVersion(Version declared, Version compiler)
{
    if (declared.Major > compiler.Major)
    {
        // Error: Incompatible major version
        Emit(DiagnosticCode.SemanticsVersionIncompatible,
             $"Module requires semantics v{declared} but compiler supports v{compiler}");
    }
    else if (declared.Major == compiler.Major &&
             declared.Minor > compiler.Minor)
    {
        // Warning: Module may use features not in this compiler
        Emit(DiagnosticCode.SemanticsVersionMismatch,
             $"Module targets semantics v{declared}, compiler supports v{compiler}");
    }
    // Patch differences are always compatible
}
```

---

## 6. Version History

### Version 1.0.0 (Current)

Initial formal semantics specification including:

- **Evaluation Order**
  - Left-to-right function argument evaluation
  - Left-to-right binary operator evaluation
  - Short-circuit `&&` and `||`

- **Scoping**
  - Lexical scoping with parent chain lookup
  - Inner scope shadows outer
  - Return from nested scope

- **Numeric Semantics**
  - Integer overflow traps by default
  - INT→FLOAT implicit
  - FLOAT→INT explicit

- **Contracts**
  - REQUIRES evaluated before body
  - ENSURES evaluated after body with `result` binding
  - ContractViolationException with FunctionId

- **Option<T> and Result<T,E>**
  - Pattern matching semantics
  - Exhaustiveness checking

---

## 7. Future Versioning Guidelines

### 7.1 When to Bump MAJOR

- Changing evaluation order of existing constructs
- Changing default overflow behavior
- Removing constructs
- Changing type coercion rules
- Changing contract semantics

### 7.2 When to Bump MINOR

- Adding new syntax constructs
- Adding new operators
- Adding new built-in types
- Extending pattern matching
- Adding optional compiler flags

### 7.3 When to Bump PATCH

- Fixing ambiguities in specification
- Adding test cases
- Improving documentation
- Fixing compiler bugs that didn't match spec

---

## 8. Migration Guidance

### 8.1 Upgrading Modules

When upgrading a module to a new semantics version:

1. **Review changelog** for breaking changes
2. **Run tests** with new compiler version
3. **Update §SEMVER** declaration
4. **Test edge cases** related to changed semantics

### 8.2 Multi-Version Projects

Projects can contain modules targeting different semantics versions:

```
project/
├── legacy/
│   └── old_module.calr  // §SEMVER{1.0.0}
└── modern/
    └── new_module.calr  // §SEMVER{1.1.0}
```

The compiler will:
1. Check each module against its declared version
2. Emit warnings if version mismatches exist
3. Use the highest required version for cross-module calls

---

## 9. Implementation

### 9.1 SemanticsVersion Class

```csharp
// src/Calor.Compiler/SemanticsVersion.cs
namespace Calor.Compiler;

/// <summary>
/// Defines the current semantics version supported by this compiler.
/// </summary>
public static class SemanticsVersion
{
    /// <summary>Major version - breaking changes.</summary>
    public const int Major = 1;

    /// <summary>Minor version - backward-compatible additions.</summary>
    public const int Minor = 0;

    /// <summary>Patch version - clarifications and fixes.</summary>
    public const int Patch = 0;

    /// <summary>Full version object.</summary>
    public static readonly Version Current = new(Major, Minor, Patch);

    /// <summary>Version string for display.</summary>
    public static string VersionString => $"{Major}.{Minor}.{Patch}";

    /// <summary>
    /// Checks if a declared version is compatible with this compiler.
    /// </summary>
    public static VersionCompatibility CheckCompatibility(Version declared)
    {
        if (declared.Major > Major)
            return VersionCompatibility.Incompatible;
        if (declared.Major == Major && declared.Minor > Minor)
            return VersionCompatibility.PossiblyIncompatible;
        return VersionCompatibility.Compatible;
    }
}

public enum VersionCompatibility
{
    Compatible,
    PossiblyIncompatible,  // Warning
    Incompatible           // Error
}
```

### 9.2 Diagnostic Codes

Add to `src/Calor.Compiler/Diagnostics/Diagnostic.cs`:

```csharp
// Semantics version (Calor0700-0799)
public const string SemanticsVersionMismatch = "Calor0700";     // Warning
public const string SemanticsVersionIncompatible = "Calor0701"; // Error
```

---

## 10. Test Cases

### S11: SemanticsVersionMismatch_EmitsDiagnostic

```calor
// Test: Module declares newer version than compiler supports
§M{m1:Test}
  §SEMVER{99.0.0}  // Future version
§/M{m1}
```

Expected: Diagnostic `Calor0701` (Error)

### Version Compatibility Matrix

| Module Version | Compiler Version | Result |
|----------------|------------------|--------|
| 1.0.0 | 1.0.0 | Compatible |
| 1.0.0 | 1.1.0 | Compatible |
| 1.0.0 | 2.0.0 | Compatible |
| 1.1.0 | 1.0.0 | Warning (Calor0700) |
| 2.0.0 | 1.0.0 | Error (Calor0701) |

---

## References

- Semantic Versioning: https://semver.org/
- Core Semantics: `docs/semantics/core.md`
- Implementation: `src/Calor.Compiler/SemanticsVersion.cs`
