// migrate_inline_calor: skip
using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for the opt-in Calor0830 lint
/// (<see cref="LegacyCloserFormLint"/>).
/// </summary>
public class LegacyCloserFormLintTests
{
    [Fact]
    public void FlagsLegacyModuleCloser()
    {
        const string src = "§M{Calc}\n  §F{add}\n  §/F\n§/M\n";
        var findings = LegacyCloserFormLint.Scan(src, "a.calr");

        Assert.Equal(2, findings.Count);
        Assert.Equal("F", findings[0].Keyword);
        Assert.Equal("M", findings[1].Keyword);
        Assert.Equal("a.calr", findings[0].File);
    }

    [Fact]
    public void FlagsLegacyControlFlowClosers()
    {
        const string src = "§L{i:0:10}\n  §IF (== i 0)\n    §P \"zero\"\n  §/I\n§/L\n";
        var findings = LegacyCloserFormLint.Scan(src, "b.calr");

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Keyword == "I");
        Assert.Contains(findings, f => f.Keyword == "L");
    }

    [Fact]
    public void FlagsLegacyClosersWithIdPayload()
    {
        const string src = "§M{m1:Calc}\n§/M{m1}\n";
        var findings = LegacyCloserFormLint.Scan(src, "c.calr");

        var f = Assert.Single(findings);
        Assert.Equal("M", f.Keyword);
        Assert.Equal(2, f.Line);
        // RemovedLength should cover §/M{m1} (8 bytes UTF-8: § is 2 bytes
        // in UTF-8, but we measure in chars internally so length is 7).
        Assert.Equal(7, f.RemovedLength);
    }

    [Fact]
    public void DoesNotFlagInlineExpressionClosers()
    {
        // §/C (call), §/T (subscript), §/NEW, §/A all stay in source.
        const string src = "§B{x} §C{Math.Abs} §A INT:-5 §/C\n";
        var findings = LegacyCloserFormLint.Scan(src, "d.calr");

        Assert.Empty(findings);
    }

    [Fact]
    public void DoesNotFlagRetainedStatefulClosers()
    {
        // §/DO carries condition; §/PP carries condition; §/K is the
        // match-arm delimiter — all retained in Phase 4 design.
        const string src = "§DO\n  §P \"x\"\n§/DO (< i 10)\n";
        var findings = LegacyCloserFormLint.Scan(src, "e.calr");

        Assert.Empty(findings);
    }

    [Fact]
    public void DoesNotFlagCollectionLiteralClosers()
    {
        const string src = "§LIST{xs:i32}\n  §PUSH{xs} INT:1\n§/LIST{xs}\n";
        var findings = LegacyCloserFormLint.Scan(src, "f.calr");

        Assert.Empty(findings);
    }

    [Fact]
    public void DoesNotFlagPureIndentSource()
    {
        const string src = "§M{Calc}\n  §F{add:vis=pub}\n    §I{a:i32}\n    §R a\n";
        var findings = LegacyCloserFormLint.Scan(src, "g.calr");

        Assert.Empty(findings);
    }

    [Fact]
    public void DiagnosticCodeIsRegistered()
    {
        Assert.Equal("Calor0830", DiagnosticCode.LegacyCloserForm);
    }

    [Fact]
    public void ColumnIsOneBasedAtSectionMarker()
    {
        // Closer indented two spaces; column should be 3 (1-based).
        const string src = "§F{add}\n  §/F\n";
        var findings = LegacyCloserFormLint.Scan(src, "h.calr");

        var f = Assert.Single(findings);
        Assert.Equal(2, f.Line);
        Assert.Equal(3, f.Column);
    }
}
