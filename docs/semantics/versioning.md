# Calor Semantics Versioning Specification

Version: 2.0.0

This document specifies how Calor semantics are versioned and how version compatibility is managed.

Current native source has **no semantics-version declaration syntax**. Pin the
compiler package and test upgrades. The compatibility helpers and historical
declaration checks described below do not provide a native source-version gate.

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

A compiler pin identifies the implementation in use. It does not prove that an
agent followed its rules or that an upgrade preserves every program's behavior.

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
`docs/plans/v0.14-nullability-enforcement-scoping.md` section D7/F-3 and
`docs/plans/v0.14-metadata-binding-scoping.md` section F-7.

### Diagnostic routing correction (2026-09-11)

`Error` severity in the binder is not the same as production CLI rejection.
Historically, at checked main revision `080ed5a7`, `Program.Compile` used
`BindingDiagnosticPolicy`, which excluded `Calor0272` (binding initialization),
`Calor0273` (return), and `Calor0274` (argument). The language server's
`DocumentState` bound directly, so editor diagnostics could differ. Other
compiler passes may independently reject the same program.

This is not a newly introduced regression. The routing policy and its
`Program.Compile` call were introduced in
[`cc62c4ac`](https://github.com/juanmicrosoft/calor/commit/cc62c4ac7b6059de4cb7a31809c298269c6dafe7),
an ancestor of all three release commits below. Each tagged source has the
same exclusion and calls the policy from `Program.Compile`.

| Release tag | Commit | Tagged policy / compilation path |
|-------------|--------|----------------------------------|
| v0.14.0 | `0e32766496beebf6ef43e62d211efd8bf34586e6` | [Policy](https://github.com/juanmicrosoft/calor/blob/0e32766496beebf6ef43e62d211efd8bf34586e6/src/Calor.Compiler/Binding/Scope.cs#L53-L99) / [Compile](https://github.com/juanmicrosoft/calor/blob/0e32766496beebf6ef43e62d211efd8bf34586e6/src/Calor.Compiler/Program.cs#L792-L796) |
| v0.14.1 | `473cb5e302789ddbcb97deb565bf04bcbb7637c4` | [Policy](https://github.com/juanmicrosoft/calor/blob/473cb5e302789ddbcb97deb565bf04bcbb7637c4/src/Calor.Compiler/Binding/Scope.cs#L53-L99) / [Compile](https://github.com/juanmicrosoft/calor/blob/473cb5e302789ddbcb97deb565bf04bcbb7637c4/src/Calor.Compiler/Program.cs#L792-L796) |
| v0.14.2 | `1246d8d9fb36ce64999ebd1e0fc513bcd88c7ab5` | [Policy](https://github.com/juanmicrosoft/calor/blob/1246d8d9fb36ce64999ebd1e0fc513bcd88c7ab5/src/Calor.Compiler/Binding/Scope.cs#L53-L99) / [Compile](https://github.com/juanmicrosoft/calor/blob/1246d8d9fb36ce64999ebd1e0fc513bcd88c7ab5/src/Calor.Compiler/Program.cs#L792-L796) |

These are scoped source-history findings, not a claim that every possibly-null
program compiles or that every published binary has been independently tested.
[Calor 0.22's bounded checks (#1082)](https://github.com/juanmicrosoft/calor/issues/1082)
now activate production errors at specified initialization, native-return and
resolved method-input boundaries. Supported shapes include scalar strings,
supported arrays, bounded string generic payloads and identity-proven nominal
references. The historical exclusion above is not the current routing policy
for those shapes.

General mutation, member writes, constructor inputs and outer generic-container
enforcement remain excluded. An array annotation does not prove every slot
initialized, and these checks do not establish whole-program non-nullness.
See the [current nullability guide](../../website/content/guides/nullability-and-dotnet-interop.mdx).
The D3/D12/D14 safeguards
that demote conditional proofs and retain their runtime guards remain in place.

---

## 3. Version Declaration

### 3.1 Module Declaration

Native modules use the installed compiler's semantics. There is no native
declaration that requests another version or checks compatibility with a
source-specified version. This program uses the installed rules:

```calor
module InstalledSemantics
  func Next public (int:value) → int
    !
    return value + 1
```

`Next(41)` returns 42; `Next(int.MaxValue)` throws `OverflowException`.

### 3.2 Syntax

Do not add a retired section tag, an invented module-header version field or
a comment expecting it to select semantics. The retained declaration-reporting
helper accepts exact three-part versions: three runs of ASCII digits separated
by dots. Caret/range forms, shortened versions, signs, whitespace and prerelease
suffixes are rejected by that helper with `Calor0702`. This API is not a native
source declaration.

### 3.3 Modules That Declare Nothing

Ordinary native modules do not need a version declaration. The write hook
recommends pinning the compiler instead of adding retired syntax.
`calor hook validate-calr-content` checks current lexer/parser errors without
blocking the write. It does not check types, effects, runtime behavior or
source-version compatibility.

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

**Policy:** Minor releases are intended to be compatible. The helper classifies
an older minor within the same major as compatible; that classification is not
a proof that an arbitrary program compiles or behaves identically.

### 4.3 Major Version Changes (X.y.z)

**Incompatible by definition.** Changes may include:
- Evaluation order changes
- Operator precedence changes
- Type system changes (the 2.0.0 bump changed binder nullability severity,
  not production CLI routing; see the correction above)
- Default behavior changes
- Removed constructs

The compatibility helper classifies 1.x and 2.x as incompatible. Native source
currently has no declaration through which to request that comparison.

---

## 5. Compiler Behavior

### 5.1 Version Checking

The installed semantics constants and compatibility helper remain available:

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

They do not establish source-version checking in `calor build`, the language
server, `calor self-check docs` or MCP tools. Native source has no admitted
declaration channel.

### 5.2 Diagnostic Codes

| Code | Severity | Condition |
|------|----------|-----------|
| Calor0700 | Warning | Same major, declared minor newer than the compiler's (might work) |
| Calor0701 | Error | Declared major differs from the compiler's — older **or** newer |
| Calor0702 | Error | Historical declaration is malformed or duplicated |

### 5.3 Checking Logic

The helper requires matching majors. Its historical declaration-reporting path
refuses an older major rather than silently reinterpreting it under newer rules.
That is roadmap
§3.3 decision 1 ("fail-closed; no silent reinterpretation, and no
dual-semantics mode to maintain"), tracked as
[#1084](https://github.com/juanmicrosoft/calor/issues/1084) item 1. The
error message calls for manual review and migration. Changing a version value
alone is not a migration. There is no legacy `Info` compilation mode. Historical
nullability routing and the current bounded receiving checks are distinct, as
described above.

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

`SemanticsVersion.ReportDeclaredVersion` turns that verdict into diagnostics.
Retired source syntax is rejected before it can establish native compatibility.

---

## 6. Version History

### Version 2.0.0 (Current)

Precursor bump for the v0.14 nullability enforcement workstream (task #14).
Unblocked the S5 severity flip (`Calor0272/0273/0274` Info → Error), gated
on `SemanticsVersion.Major >= 2`.

The v0.14.0 promise of legacy `Info` severity was a future plan, not a shipped
mode. The severity helper's lower-major branch does not make older modules
compatible. The historical declaration parser and fail-closed refusal arrived in
[#1087](https://github.com/juanmicrosoft/calor/commit/4ac335d5932383bcf6590b34fdc2ad73ff96afbb):
that commit is not an ancestor of v0.14.0-v0.14.2, but is an ancestor of
[v0.15.0 (`3bb2601e`)](https://github.com/juanmicrosoft/calor/tree/3bb2601e3ff83597ddf2f27dcc334b6399ab97ea).
That parser refused modules declaring `1.x` or `0.x` with `Calor0701`, not an
`Info` compilation mode. Native source does not currently expose an equivalent
declaration. No automated semantics migration tool is supplied; broader
migration work remains tracked in #1084.

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

- **Nullable values and exceptions**
  - Native nullable annotations and `null`
  - Exception propagation and handling

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
3. **Pin the compiler version** used by the build and agent tooling
4. **Test edge cases** related to changed semantics

For historical 1.x → 2.0.0 migration, review the current semantics and the
module's null-handling choices. Test the migrated native code under the pinned
compiler. A version edit or successful compile alone does not establish null
safety. Do not copy the retired declaration into current source.

### 8.2 Mixed-Version Projects

There is no dual-semantics mode. Native modules in a compilation use the
installed compiler. The absence of a source-version gate is not proof that
arbitrarily mixed historical sources have been migrated safely.

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

    // Parses historical declaration text, applies CheckCompatibility, and reports
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
// Semantics version (Calor0700-0709; contract-verification results live in 0710-0719)
public const string SemanticsVersionMismatch = "Calor0700";           // Warning
public const string SemanticsVersionIncompatible = "Calor0701";       // Error
public const string SemanticsVersionInvalidDeclaration = "Calor0702"; // Error
```

### 9.3 Where the Directive Lives

The former section-tag parser and emitter admitted the declaration at module
level. Current native parsing does not expose that syntax, and the retained
`ModuleNode.DeclaredSemanticsVersion` property is not evidence that native
source can populate it. Do not infer a production gate from the token kind,
AST property or compatibility API alone.

---

## 10. Test Cases

The historical `VersioningTests` inputs used the retired declaration syntax.
They do not establish a native end-to-end gate. The matrix below describes
the compatibility/declaration helpers for semantics 2.0.0:

| Version supplied to the helpers | Result |
|-----------------|--------|
| No native declaration | Uses installed semantics; no source-version comparison |
| 2.0.0, 2.0.5 | Compatible |
| 2.1.0 | Declaration helper reports Warning (Calor0700) |
| 1.0.0, 1.9.9, 0.9.0 | Error (Calor0701) with migration pointer to #1084 |
| 3.0.0, 99.0.0 | Error (Calor0701), "upgrade the compiler" |
| `^1.0.0`, `2`, `2.0` | Declaration helper reports Error (Calor0702) |

Example — compatibility API, not a source declaration:

```csharp
SemanticsVersion.CheckCompatibility(new Version(1, 0, 0))
    == VersionCompatibility.Incompatible
```

The historical declaration parser also rejected duplicate declarations, even
when the first was malformed. Native source cannot use that retired parser
route. Its rejection of retired syntax must not be reported as a successful
older-major compatibility check.

---

## References

- Semantic Versioning: https://semver.org/
- Core Semantics: `docs/semantics/core.md`
- Implementation: `src/Calor.Compiler/SemanticsVersion.cs`
- Roadmap: `docs/plans/roadmap-v0.13-v0.15.md` §3.3 decision 1, §4.5 row 2
- Tracking issue: https://github.com/juanmicrosoft/calor/issues/1084
