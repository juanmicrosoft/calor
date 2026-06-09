using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for the opt-in Calor0820 lint
/// (<see cref="LegacyStructuralIdLint"/>).
/// </summary>
public class LegacyStructuralIdLintTests
{
    [Fact]
    public void FlagsLegacyModuleId()
    {
        const string src = "§M{m_01j5x7abcdef01j5x7abcdef01:Calc}\n";
        var findings = LegacyStructuralIdLint.Scan(src, "a.calr");

        var f = Assert.Single(findings);
        Assert.Equal("a.calr", f.File);
        Assert.Equal("M", f.Keyword);
        Assert.Equal(1, f.Line);
        Assert.Contains("m_01j5x7abcdef01j5x7abcdef01", f.IdValue);
    }

    [Fact]
    public void NoFindingsOnCompactForm()
    {
        const string src = "§M{Calc}\n§/M\n";
        var findings = LegacyStructuralIdLint.Scan(src, "b.calr");

        Assert.Empty(findings);
    }

    [Fact]
    public void DiagnosticCodeIsRegistered()
    {
        Assert.Equal("Calor0820", DiagnosticCode.LegacyStructuralId);
        Assert.Equal("Calor0821", DiagnosticCode.LegacyUlidPayload);
        Assert.Equal("Calor0822", DiagnosticCode.CompactIdCollision);
    }
}
