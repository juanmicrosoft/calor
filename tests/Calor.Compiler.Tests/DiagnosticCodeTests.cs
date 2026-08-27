using System.Reflection;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Z3-independent guards for the diagnostic-code numbering. These run in every
/// CI environment (unlike Calor.Verification.Tests, which self-skips without the
/// Z3 native library), so a fat-fingered renumber of the verification sub-band
/// cannot slip through a Z3-less CI. Regression guard for #702, which killed the
/// Calor0700/0701 collision between semantics-version and contract-verification
/// diagnostics by moving all verification results into a disjoint 0710-0715 band.
/// </summary>
public class DiagnosticCodeTests
{
    [Fact]
    public void SemanticsVersionCodes_HaveTheirCanonicalValues()
    {
        Assert.Equal("Calor0700", DiagnosticCode.SemanticsVersionMismatch);
        Assert.Equal("Calor0701", DiagnosticCode.SemanticsVersionIncompatible);
    }

    [Fact]
    public void ContractVerificationCodes_OccupyTheDisjoint0710Band()
    {
        // These literal values are the contract the CHANGELOG publishes to agents
        // filtering verification output. Changing a number here is a breaking
        // change and must be a deliberate edit, not an accident.
        Assert.Equal("Calor0710", DiagnosticCode.Z3Unavailable);
        Assert.Equal("Calor0711", DiagnosticCode.PreconditionMayBeViolated);
        Assert.Equal("Calor0712", DiagnosticCode.PostconditionMayBeViolated);
        Assert.Equal("Calor0713", DiagnosticCode.PostconditionProven);
        Assert.Equal("Calor0714", DiagnosticCode.VerificationSummary);
        Assert.Equal("Calor0715", DiagnosticCode.VerificationCacheStats);
    }

    [Fact]
    public void VerificationAndSemanticsVersionBands_DoNotCollide()
    {
        // The whole point of #702: no verification code shares a number with a
        // semantics-version code. Assert the property directly so any future code
        // added to either group that re-introduces a collision fails here.
        var semanticsVersion = new[]
        {
            DiagnosticCode.SemanticsVersionMismatch,
            DiagnosticCode.SemanticsVersionIncompatible,
        };
        var verification = new[]
        {
            DiagnosticCode.Z3Unavailable,
            DiagnosticCode.PreconditionMayBeViolated,
            DiagnosticCode.PostconditionMayBeViolated,
            DiagnosticCode.PostconditionProven,
            DiagnosticCode.VerificationSummary,
            DiagnosticCode.VerificationCacheStats,
        };

        Assert.Empty(semanticsVersion.Intersect(verification));
    }

    [Fact]
    public void Calor0418_IsRetainedForTheProvablyNonFunctionResidual()
    {
        // v0.15 E4 (roadmap §4.2, design-doc §10.1): Calor0418 no longer rejects
        // the invocation of a function-typed value — the value's effect row is
        // charged instead. The code is RETAINED, not removed, for exactly one
        // residual: invoking a value whose type is provably not a function type,
        // where there is no row to read. This reads the catalogue so that
        // deleting the constant (or re-purposing its number) is a red test, and
        // its doc comment must say what the residual is.
        var field = typeof(DiagnosticCode).GetField(
            nameof(DiagnosticCode.DelegateInvocation),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal("Calor0418", (string)field!.GetValue(null)!);

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Calor.Compiler",
            "Diagnostics", "Diagnostic.cs"));
        var declaration = source.IndexOf("public const string DelegateInvocation", StringComparison.Ordinal);
        Assert.True(declaration > 0);
        var docComment = source[Math.Max(0, declaration - 2000)..declaration];
        Assert.Contains("PROVABLY not a function type", docComment);
        Assert.Contains("retained for that residual only", docComment);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void AllDiagnosticCodeConstants_AreUnique()
    {
        // Broader backstop: no two DiagnosticCode constants share a value at all.
        // Catches a collision reintroduced anywhere in the code space, not just the
        // two bands #702 touched.
        var values = typeof(DiagnosticCode)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        var duplicates = values
            .GroupBy(v => v)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }
}
