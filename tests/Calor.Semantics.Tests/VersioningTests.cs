using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Semantics.Tests;

/// <summary>
/// Tests for semantics versioning (S11).
/// </summary>
public class VersioningTests
{
    /// <summary>
    /// S11: Semantics version mismatch emits diagnostic.
    /// </summary>
    [Fact]
    public void S11_SemanticsVersionMismatch_EmitsDiagnostic()
    {
        // Check that the version checking logic works correctly
        var currentVersion = SemanticsVersion.Current;

        // Same major version, higher minor = possibly incompatible (warning)
        var higherMinor = new Version(currentVersion.Major, currentVersion.Minor + 1, 0);
        var compat1 = SemanticsVersion.CheckCompatibility(higherMinor);
        Assert.Equal(VersionCompatibility.PossiblyIncompatible, compat1);

        // Higher major version = incompatible (error)
        var higherMajor = new Version(currentVersion.Major + 1, 0, 0);
        var compat2 = SemanticsVersion.CheckCompatibility(higherMajor);
        Assert.Equal(VersionCompatibility.Incompatible, compat2);

        // Same version = compatible (an older major is Incompatible; see
        // CompatibleVersions_CorrectlyIdentified)
        var sameVersion = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
        var compat3 = SemanticsVersion.CheckCompatibility(sameVersion);
        Assert.Equal(VersionCompatibility.Compatible, compat3);
    }

    /// <summary>
    /// Current semantics version is 2.0.0.
    /// </summary>
    /// <remarks>
    /// Ratchet test locking in the version bump from task #14 (v0.14 nullability
    /// S5 precursor). Any future bump must update this test in the same PR — this
    /// mirrors the allowlist/ratchet discipline used by prior precursor PRs
    /// (e.g., #1049 F-1 corpus baseline, #1051 NullabilityChecker allowlist).
    /// See <c>docs/plans/v0.14-nullability-enforcement-scoping.md</c> D7.
    /// </remarks>
    [Fact]
    public void CurrentVersion_Is_2_0_0()
    {
        Assert.Equal(2, SemanticsVersion.Major);
        Assert.Equal(0, SemanticsVersion.Minor);
        Assert.Equal(0, SemanticsVersion.Patch);
        Assert.Equal("2.0.0", SemanticsVersion.VersionString);
    }

    /// <summary>
    /// Compatible versions are correctly identified.
    /// </summary>
    [Fact]
    public void CompatibleVersions_CorrectlyIdentified()
    {
        // All 2.0.x versions are compatible with a 2.0.x compiler
        Assert.Equal(VersionCompatibility.Compatible,
            SemanticsVersion.CheckCompatibility(new Version(2, 0, 0)));
        Assert.Equal(VersionCompatibility.Compatible,
            SemanticsVersion.CheckCompatibility(new Version(2, 0, 1)));
        Assert.Equal(VersionCompatibility.Compatible,
            SemanticsVersion.CheckCompatibility(new Version(2, 0, 99)));

        // Older major (1.x, 0.x) is REFUSED — roadmap §3.3 decision 1 / #1084 item 1:
        // a file written for retired semantics is never silently reinterpreted.
        Assert.Equal(VersionCompatibility.Incompatible,
            SemanticsVersion.CheckCompatibility(new Version(1, 0, 0)));
        Assert.Equal(VersionCompatibility.Incompatible,
            SemanticsVersion.CheckCompatibility(new Version(1, 9, 9)));
        Assert.Equal(VersionCompatibility.Incompatible,
            SemanticsVersion.CheckCompatibility(new Version(0, 9, 0)));
    }

    private static CompilationResult CompileModule(string semverLine)
    {
        var source = $$"""
            §M{m001:Test}
            {{semverLine}}
              §F{f001:Answer:pub} () -> int
                §E{}
                §R INT:42
            """;
        return Program.Compile(source, "test.calr", new CompilationOptions { EnforceEffects = false });
    }

    /// <summary>
    /// End-to-end (#1084 item 1): a module declaring <c>§SEMVER{1.0.0}</c> is refused with
    /// Calor0701 at Error severity, and the message carries the migration pointer.
    /// </summary>
    [Fact]
    public void Compile_Semver1x_IsRefused_Calor0701_WithMigrationPointer()
    {
        var result = CompileModule("  §SEMVER{1.0.0}");

        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.SemanticsVersionIncompatible, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("1.0.0", diagnostic.Message);
        Assert.Contains("2.0.0", diagnostic.Message);
        Assert.Contains("issues/1084", diagnostic.Message);
        Assert.Contains("declare §SEMVER{2.0.0} after reviewing nullability semantics (Calor0272/0273/0274)", diagnostic.Message);
        Assert.Equal(2, diagnostic.Span.Line);
        Assert.Empty(result.GeneratedCode);
    }

    /// <summary>0.x is a retired major too — refused the same way as 1.x.</summary>
    [Fact]
    public void Compile_Semver0x_IsRefused_Calor0701()
    {
        var result = CompileModule("  §SEMVER{0.9.0}");

        var diagnostic = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.SemanticsVersionIncompatible, diagnostic.Code);
        Assert.Contains(SemanticsVersion.LegacyMajorMigrationHint, diagnostic.Message);
    }

    /// <summary>A module declaring the compiler's own version compiles clean.</summary>
    [Fact]
    public void Compile_Semver2_0_0_CompilesClean()
    {
        var result = CompileModule("  §SEMVER{2.0.0}");

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("Calor070", StringComparison.Ordinal));
        Assert.NotNull(result.Ast);
        Assert.Equal("2.0.0", result.Ast.DeclaredSemanticsVersion);
        Assert.Contains("42", result.GeneratedCode);
    }

    /// <summary>
    /// A declared minor ahead of the compiler's is PossiblyIncompatible: Calor0700 at
    /// Warning severity, and compilation still succeeds.
    /// </summary>
    [Fact]
    public void Compile_SemverHigherMinor_Warns_Calor0700_AndCompiles()
    {
        var result = CompileModule($"  §SEMVER{{{SemanticsVersion.Major}.{SemanticsVersion.Minor + 1}.0}}");

        Assert.False(result.HasErrors);
        var warning = Assert.Single(result.Diagnostics.Warnings, d => d.Code == DiagnosticCode.SemanticsVersionMismatch);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("42", result.GeneratedCode);
    }

    /// <summary>The pre-existing direction still holds: a newer major is refused with Calor0701.</summary>
    [Fact]
    public void Compile_SemverNewerMajor_IsRefused_Calor0701()
    {
        var result = CompileModule($"  §SEMVER{{{SemanticsVersion.Major + 1}.0.0}}");

        var diagnostic = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.SemanticsVersionIncompatible, diagnostic.Code);
        Assert.Contains("Upgrade the compiler", diagnostic.Message);
        Assert.DoesNotContain("issues/1084", diagnostic.Message);
    }

    /// <summary>
    /// Only exact MAJOR.MINOR.PATCH is accepted; caret/range forms, bare majors, a
    /// leading sign, and whitespace anywhere (Version.TryParse would tolerate the
    /// last two — review round 1 finding 1) are Calor0702 errors rather than
    /// silently accepted. Surrounding whitespace is deliberately NOT trimmed.
    /// </summary>
    [Fact]
    public void Compile_MalformedSemver_IsError_Calor0702()
    {
        foreach (var malformed in new[]
                 {
                     "^1.0.0", ">=2.0.0 <3.0.0", "2", "2.0", "", "+2.0.0", "2 . 0 . 0",
                     " 1.0.0 ", " 1 . 0 . 0 ", "2.0.0 ", "2.0.0.0", "2.0.0-rc1", "v2.0.0",
                 })
        {
            var result = CompileModule($"  §SEMVER{{{malformed}}}");

            var diagnostic = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.SemanticsVersionInvalidDeclaration, diagnostic.Code);
            Assert.Contains("MAJOR.MINOR.PATCH", diagnostic.Message);
            Assert.Null(result.Ast?.DeclaredSemanticsVersion);
        }
    }

    /// <summary>A second §SEMVER in the same module is a Calor0702 error.</summary>
    [Fact]
    public void Compile_DuplicateSemver_IsError_Calor0702()
    {
        var result = CompileModule("  §SEMVER{2.0.0}\n  §SEMVER{2.0.0}");

        var diagnostic = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.SemanticsVersionInvalidDeclaration, diagnostic.Code);
        Assert.Contains("only once", diagnostic.Message);
    }

    /// <summary>
    /// Review round 1 finding 3: duplicate detection keys on "a §SEMVER was seen", not on
    /// the stored value, so a malformed first directive followed by a valid one reports
    /// both the malformed text and the duplicate — the second one is not quietly adopted.
    /// </summary>
    [Fact]
    public void Compile_MalformedThenValidSemver_ReportsBoth_AdoptsNeither()
    {
        var result = CompileModule("  §SEMVER{2}\n  §SEMVER{2.0.0}");

        Assert.Equal(2, result.Diagnostics.Errors.Count);
        Assert.All(result.Diagnostics.Errors,
            d => Assert.Equal(DiagnosticCode.SemanticsVersionInvalidDeclaration, d.Code));
        Assert.Contains(result.Diagnostics.Errors, d => d.Message.Contains("MAJOR.MINOR.PATCH"));
        Assert.Contains(result.Diagnostics.Errors, d => d.Message.Contains("only once"));
        Assert.Null(result.Ast?.DeclaredSemanticsVersion);
    }

    /// <summary>
    /// Review round 1 finding 4: the legacy bracket form the hook used to recommend is
    /// exactly one clear Calor0702 pointing at braces — not an empty-string 0702 plus a
    /// cascade of parser errors.
    /// </summary>
    [Fact]
    public void Compile_BracketSemver_IsSingleError_Calor0702_UseBraces()
    {
        var result = CompileModule("  §SEMVER[1.0.0]");

        var diagnostic = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.SemanticsVersionInvalidDeclaration, diagnostic.Code);
        Assert.Contains("use braces: §SEMVER{MAJOR.MINOR.PATCH}", diagnostic.Message);
    }

    /// <summary>
    /// Review round 1 finding 9: an unterminated §SEMVER{ stops at the end of its line and
    /// reports one Calor0702 instead of swallowing the statements that follow it.
    /// </summary>
    [Fact]
    public void Compile_UnterminatedSemver_IsSingleError_Calor0702()
    {
        var result = CompileModule("  §SEMVER{2.0.0");

        var diagnostic = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.SemanticsVersionInvalidDeclaration, diagnostic.Code);
        Assert.Contains("Unterminated §SEMVER", diagnostic.Message);
        Assert.Equal(2, diagnostic.Span.Line);
    }

    /// <summary>Bare §SEMVER with no brace group at all is a single Calor0702.</summary>
    [Fact]
    public void Compile_BareSemver_IsSingleError_Calor0702()
    {
        var result = CompileModule("  §SEMVER");

        var diagnostic = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.SemanticsVersionInvalidDeclaration, diagnostic.Code);
        Assert.Contains("expected §SEMVER{MAJOR.MINOR.PATCH}", diagnostic.Message);
    }

    /// <summary>
    /// Files that declare nothing keep today's behaviour: they take the compiler's
    /// major and no semantics-version diagnostic is emitted (there is no compile-time
    /// nudge; the only nudge is the write-hook reminder in HookCommand).
    /// </summary>
    [Fact]
    public void Compile_NoSemver_CompilesClean_WithoutVersionDiagnostics()
    {
        var result = CompileModule("");

        Assert.False(result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("Calor070", StringComparison.Ordinal));
        Assert.NotNull(result.Ast);
        Assert.Null(result.Ast.DeclaredSemanticsVersion);
    }

    /// <summary>
    /// Diagnostic codes for version mismatch exist.
    /// </summary>
    [Fact]
    public void VersionDiagnosticCodes_Exist()
    {
        Assert.Equal("Calor0700", DiagnosticCode.SemanticsVersionMismatch);
        Assert.Equal("Calor0701", DiagnosticCode.SemanticsVersionIncompatible);
        Assert.Equal("Calor0702", DiagnosticCode.SemanticsVersionInvalidDeclaration);
    }

    /// <summary>
    /// Version string parsing works.
    /// </summary>
    [Fact]
    public void VersionString_Parsing()
    {
        var result = SemanticsVersion.CheckCompatibility("2.0.0");
        Assert.NotNull(result);
        Assert.Equal(VersionCompatibility.Compatible, result.Value);

        // Invalid version string
        var invalid = SemanticsVersion.CheckCompatibility("not-a-version");
        Assert.Null(invalid);
    }

    /// <summary>
    /// Minor version increments are forward-compatible.
    /// </summary>
    [Fact]
    public void MinorVersionIncrement_ForwardCompatible()
    {
        // Code written for 2.0.0 can run on 2.1.0 (compatible)
        // But 2.1.0 code on 2.0.0 compiler is PossiblyIncompatible

        // Older code on newer compiler
        Assert.Equal(VersionCompatibility.Compatible,
            SemanticsVersion.CheckCompatibility(new Version(2, 0, 0)));

        // Newer code on this compiler (if minor is 0)
        if (SemanticsVersion.Minor == 0)
        {
            Assert.Equal(VersionCompatibility.PossiblyIncompatible,
                SemanticsVersion.CheckCompatibility(new Version(SemanticsVersion.Major, 1, 0)));
        }
    }

    /// <summary>
    /// Major version changes are breaking.
    /// </summary>
    [Fact]
    public void MajorVersionChange_Breaking()
    {
        // Version (Major+1).0.0 is incompatible with this compiler
        Assert.Equal(VersionCompatibility.Incompatible,
            SemanticsVersion.CheckCompatibility(new Version(SemanticsVersion.Major + 1, 0, 0)));

        // Any (Major+1).x version
        Assert.Equal(VersionCompatibility.Incompatible,
            SemanticsVersion.CheckCompatibility(new Version(SemanticsVersion.Major + 1, 5, 0)));

        // Far-future versions
        Assert.Equal(VersionCompatibility.Incompatible,
            SemanticsVersion.CheckCompatibility(new Version(99, 0, 0)));
    }

    /// <summary>
    /// Task #14 ratchet: SemanticsVersion.Major must be exactly 2, unblocking the
    /// v0.14 nullability S5 severity flip (Calor0272/0273/0274 → Error).
    /// </summary>
    /// <remarks>
    /// If this test fails because Major moved past 2, the S5 severity-flip gate
    /// (<c>SemanticsVersion.Major &gt;= 2</c>) still holds — but the ratchet must
    /// be updated in the PR performing the bump. Locking to an exact value (not
    /// <c>&gt;= 2</c>) forces a deliberate PR-level acknowledgement of every
    /// semantics-version movement, matching prior allowlist/precursor discipline.
    /// </remarks>
    [Fact]
    public void SemanticsVersion_Major_RatchetAt_2()
    {
        Assert.Equal(2, SemanticsVersion.Major);
    }

    /// <summary>
    /// Task #14 ratchet: The S5 severity-flip gate predicate
    /// (<c>SemanticsVersion.Major &gt;= 2</c>) is now true. When S5 lands, its
    /// gated Error emission for Calor0272/0273/0274 becomes active.
    /// </summary>
    [Fact]
    public void SemanticsVersion_S5_SeverityGate_IsOpen()
    {
        // The gate S5 will consult. This test observes that Task #14's bump
        // actually satisfies the S5 precondition documented in D7 / F-3 of
        // docs/plans/v0.14-nullability-enforcement-scoping.md.
        Assert.True(SemanticsVersion.Major >= 2);
    }
}
