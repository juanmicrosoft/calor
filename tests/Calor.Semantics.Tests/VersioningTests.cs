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

        // Same or lower version = compatible
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

        // Older major (1.x, 0.x) is accepted (backward compatible per CheckCompatibility)
        Assert.Equal(VersionCompatibility.Compatible,
            SemanticsVersion.CheckCompatibility(new Version(1, 0, 0)));
        Assert.Equal(VersionCompatibility.Compatible,
            SemanticsVersion.CheckCompatibility(new Version(0, 9, 0)));
    }

    /// <summary>
    /// Diagnostic codes for version mismatch exist.
    /// </summary>
    [Fact]
    public void VersionDiagnosticCodes_Exist()
    {
        Assert.Equal("Calor0700", DiagnosticCode.SemanticsVersionMismatch);
        Assert.Equal("Calor0701", DiagnosticCode.SemanticsVersionIncompatible);
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
