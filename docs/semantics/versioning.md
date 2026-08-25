# Calor Semantics Versioning Specification

Version: 2.0.0

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

**Semantics Version: 2.0.0**

Bumped from `1.0.0` as the v0.14 nullability workstream precursor (task #14) —
required to unlock the S5 severity flip that promotes `Calor0272/0273/0274`
from Info to Error under `SemanticsVersion.Major >= 2`. See
`docs/plans/v0.14-nullability-enforcement-scoping.md` §D7/F-3 and
`docs/plans/v0.14-metadata-binding-scoping.md` §F-7.

---

## 3. Version Declaration

### 3.1 Module Declaration

A module declares the semantics version it was written for with a
`§SEMVER` directive, conventionally the first line of the module body:

```calor
§M{m001:MyModule}
  §SEMVER{2.0.0}
  §F{f001:Main:pub} () -> void
    §E{}
    §R
```

### 3.2 Syntax

Exactly one form is accepted: `§SEMVER{MAJOR.MINOR.PATCH}` with three
numeric components.

```
§SEMVER{2.0.0}
```

Caret (`^2.0.0`), range (`>=2.0.0 <3.0.0`), and shortened (`2`, `2.0`)
forms are **not** supported and are rejected with `Calor0702`. A module may
contain at most one `§SEMVER`; a second one is also `Calor0702`.

### 3.3 Modules That Declare Nothing

A module without `§SEMVER` takes the compiler's own version (currently
2.0.0). No diagnostic is emitted for a missing declaration; the only nudge
is the `calor hook` write-time reminder, which suggests `§SEMVER{2.0.0}`.

---

## 4. Compatibility Rules

### 4.1 Patch Version Changes (x.y.Z)

**Fully compatible.** Changes include:
- Documentation clarifications
- Specification bug fixes
- Test additions

**Example:** 2.0.0 → 2.0.1 is safe.

### 4.2 Minor Version Changes (x.Y.z)

**Backward compatible.** Changes include:
- New constructs added
- New operators added
- New optional behaviors
- Extended standard library

**Example:** Code written for 2.0.0 will compile and run correctly under 2.1.0.

### 4.3 Major Version Changes (X.y.z)

**Incompatible by definition.** Changes may include:
- Evaluation order changes
- Operator precedence changes
- Type system changes (2.0.0: non-nullable `string` and the
  `Calor0272/0273/0274` nullability errors)
- Default behavior changes
- Removed constructs

**Example:** Code written for 1.x is refused by a 2.x compiler (see §5).

---

## 5. Compiler Behavior

### 5.1 Version Checking

The compiler checks the declared version against its supported semantics
while parsing the module, so every consumer of the parser — `calor build`,
the language server, `calor self-check docs`, the MCP tools — reaches the
same verdict:

```csharp
// src/Calor.Compiler/SemanticsVersion.cs
public static class SemanticsVersion
{
    public const int Major = 2;
    public const int Minor = 0;
    public const int Patch = 0;
    public static readonly Version Current = new(Major, Minor, Patch);
}
```

### 5.2 Diagnostic Codes

| Code | Severity | Condition |
|------|----------|-----------|
| Calor0700 | Warning | Same major, declared minor newer than the compiler's (might work) |
| Calor0701 | Error | Declared major differs from the compiler's — older **or** newer |
| Calor0702 | Error | `§SEMVER` is not `MAJOR.MINOR.PATCH`, or appears more than once |

### 5.3 Checking Logic

The major must match exactly. A declared major **older** than the
compiler's (for example `§SEMVER{1.0.0}` on the 2.0.0 compiler) is refused
rather than silently reinterpreted under the newer rules — this is roadmap
§3.3 decision 1 ("fail-closed; no silent reinterpretation, and no
dual-semantics mode to maintain"), tracked as
[#1084](https://github.com/juanmicrosoft/calor/issues/1084) item 1. The
error message carries the migration pointer: migrate the module and declare
`§SEMVER{2.0.0}` after reviewing nullability semantics
(`Calor0272/0273/0274`).

```csharp
public static VersionCompatibility CheckCompatibility(Version declared)
{
    if (declared.Major != Major)
        return VersionCompatibility.Incompatible;       // Calor0701
    if (declared.Minor > Minor)
        return VersionCompatibility.PossiblyIncompatible; // Calor0700
    return VersionCompatibility.Compatible;
    // Patch differences are always compatible
}
```

`SemanticsVersion.ReportDeclaredVersion` turns that verdict into the
diagnostics above; the parser calls it when it meets `§SEMVER`.

---

## 6. Version History

### Version 2.0.0 (Current)

Precursor bump for the v0.14 nullability enforcement workstream (task #14).
Unblocked the S5 severity flip (`Calor0272/0273/0274` Info → Error), gated
on `SemanticsVersion.Major >= 2`.

Since v0.15.0 the `§SEMVER` directive is parsed and checked: files declaring
`1.x` (or `0.x`) are refused with `Calor0701` and a migration pointer
(#1084 item 1); no committed `.calr` in this repository declared a version,
so the change broke nothing in-tree. Automated 1.x → 2.0.0 migration is
demand-driven (#1084 item 3).

### Version 1.0.0

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

For 1.x → 2.0.0 specifically: review every `string`-typed binding, return,
and argument for possibly-null values (`Calor0272/0273/0274` become errors),
then change the declaration to `§SEMVER{2.0.0}`. Until you do, the 2.x
compiler refuses the file with `Calor0701`.

### 8.2 Mixed-Version Projects

There is no dual-semantics mode: every module in a compilation must declare
the compiler's major (or declare nothing). A project cannot keep a
`§SEMVER{1.0.0}` module alongside `§SEMVER{2.0.0}` ones on a 2.x compiler —
the 1.x module is refused until it is migrated.

---

## 9. Implementation

### 9.1 SemanticsVersion Class

```csharp
// src/Calor.Compiler/SemanticsVersion.cs
namespace Calor.Compiler;

public static class SemanticsVersion
{
    public const int Major = 2;
    public const int Minor = 0;
    public const int Patch = 0;
    public static readonly Version Current = new(Major, Minor, Patch);
    public static string VersionString => $"{Major}.{Minor}.{Patch}";

    public static VersionCompatibility CheckCompatibility(Version declared)
    {
        if (declared.Major != Major)
            return VersionCompatibility.Incompatible;
        if (declared.Minor > Minor)
            return VersionCompatibility.PossiblyIncompatible;
        return VersionCompatibility.Compatible;
    }

    // Parses the §SEMVER text, applies CheckCompatibility, and reports
    // Calor0700 / Calor0701 / Calor0702 into the diagnostic bag.
    public static bool ReportDeclaredVersion(
        DiagnosticBag diagnostics, TextSpan span, string? versionText);
}

public enum VersionCompatibility
{
    Compatible,
    PossiblyIncompatible,  // Warning (Calor0700)
    Incompatible           // Error (Calor0701)
}
```

### 9.2 Diagnostic Codes

In `src/Calor.Compiler/Diagnostics/Diagnostic.cs`:

```csharp
// Semantics version (Calor0700-0709; contract-verification results live in 0710-0729)
public const string SemanticsVersionMismatch = "Calor0700";           // Warning
public const string SemanticsVersionIncompatible = "Calor0701";       // Error
public const string SemanticsVersionInvalidDeclaration = "Calor0702"; // Error
```

### 9.3 Where the Directive Lives

- `Parsing/Lexer.cs` — `§SEMVER{...}` lexes as one `TokenKind.SemVer`
  token whose value is the raw brace content.
- `Parsing/Parser.cs` (`ParseModule`) — accepts the directive at module
  level, calls `SemanticsVersion.ReportDeclaredVersion`, and stores the
  text on `ModuleNode.DeclaredSemanticsVersion`.
- `Migration/CalorEmitter.cs` — re-emits the directive so Calor → Calor
  round-trips preserve it.

---

## 10. Test Cases

`tests/Calor.Semantics.Tests/VersioningTests.cs` pins the behaviour end to
end. The matrix below is for the 2.0.0 compiler:

| Module declares | Result |
|-----------------|--------|
| (nothing) | Compatible — takes 2.0.0, no diagnostic |
| 2.0.0, 2.0.5 | Compatible |
| 2.1.0 | Warning (Calor0700), compiles |
| 1.0.0, 1.9.9, 0.9.0 | Error (Calor0701) with migration pointer to #1084 |
| 3.0.0, 99.0.0 | Error (Calor0701), "upgrade the compiler" |
| `^1.0.0`, `2`, `2.0`, second `§SEMVER` | Error (Calor0702) |

Example — refused legacy module:

<!-- drift:ignore -->
```calor
§M{m1:Legacy}
  §SEMVER{1.0.0}
```

Expected: `Calor0701` (Error) — "Module declares semantics version 1.0.0,
but this compiler implements 2.0.0 and refuses files written for an older
major. ... declare §SEMVER{2.0.0} after reviewing nullability semantics
(Calor0272/0273/0274). See https://github.com/juanmicrosoft/calor/issues/1084."

---

## References

- Semantic Versioning: https://semver.org/
- Core Semantics: `docs/semantics/core.md`
- Implementation: `src/Calor.Compiler/SemanticsVersion.cs`
- Roadmap: `docs/plans/roadmap-v0.13-v0.15.md` §3.3 decision 1, §4.5 row 2
- Tracking issue: https://github.com/juanmicrosoft/calor/issues/1084
