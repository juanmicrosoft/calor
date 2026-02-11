using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.Utilities;
using Xunit;
using CalorDiagnostic = Calor.Compiler.Diagnostics.Diagnostic;
using CalorSeverity = Calor.Compiler.Diagnostics.DiagnosticSeverity;
using LspSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace Calor.LanguageServer.Tests.Utilities;

public class DiagnosticConverterTests
{
    [Fact]
    public void ToLspDiagnostic_Error_ConvertsSeverity()
    {
        var span = new TextSpan(0, 5, 1, 1);
        var diagnostic = new CalorDiagnostic("Calor0001", "Test error", span, CalorSeverity.Error);

        var lspDiag = DiagnosticConverter.ToLspDiagnostic(diagnostic, "hello");

        Assert.Equal(LspSeverity.Error, lspDiag.Severity);
        Assert.Equal("Calor0001", lspDiag.Code);
        Assert.Equal("Test error", lspDiag.Message);
        Assert.Equal("calor", lspDiag.Source);
    }

    [Fact]
    public void ToLspDiagnostic_Warning_ConvertsSeverity()
    {
        var span = new TextSpan(0, 5, 1, 1);
        var diagnostic = new CalorDiagnostic("Calor0002", "Test warning", span, CalorSeverity.Warning);

        var lspDiag = DiagnosticConverter.ToLspDiagnostic(diagnostic, "hello");

        Assert.Equal(LspSeverity.Warning, lspDiag.Severity);
    }

    [Fact]
    public void ToLspDiagnostic_Info_ConvertsSeverity()
    {
        var span = new TextSpan(0, 5, 1, 1);
        var diagnostic = new CalorDiagnostic("Calor0003", "Test info", span, CalorSeverity.Info);

        var lspDiag = DiagnosticConverter.ToLspDiagnostic(diagnostic, "hello");

        Assert.Equal(LspSeverity.Information, lspDiag.Severity);
    }

    [Fact]
    public void ToLspDiagnostic_ConvertsSingleLineRange()
    {
        var source = "hello world";
        var span = new TextSpan(0, 5, 1, 1);
        var diagnostic = new CalorDiagnostic("Calor0001", "Test", span);

        var lspDiag = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);

        Assert.Equal(0, lspDiag.Range.Start.Line);
        Assert.Equal(0, lspDiag.Range.Start.Character);
    }

    [Fact]
    public void ToLspDiagnosticSingleLine_ConvertsRange()
    {
        var span = new TextSpan(10, 5, 2, 5);
        var diagnostic = new CalorDiagnostic("Calor0001", "Test", span);

        var lspDiag = DiagnosticConverter.ToLspDiagnosticSingleLine(diagnostic);

        Assert.Equal(1, lspDiag.Range.Start.Line);  // 2-1 = 1 (0-based)
        Assert.Equal(4, lspDiag.Range.Start.Character); // 5-1 = 4 (0-based)
        Assert.Equal(9, lspDiag.Range.End.Character); // 4 + 5
    }

    [Fact]
    public void ToLspDiagnostic_WithFix_PreservesFixDescription()
    {
        var span = new TextSpan(0, 5, 1, 1);
        var fix = new SuggestedFix("Fix description", new TextEdit("test.calr", 1, 1, 1, 5, "fixed"));
        var diagnostic = new DiagnosticWithFix("Calor0001", "Test error", span, fix);

        var lspDiag = DiagnosticConverter.ToLspDiagnostic(diagnostic, "hello");

        Assert.Equal("Fix description", lspDiag.Data?.ToString());
    }

    [Fact]
    public void ToLspSeverity_MapsCorrectly()
    {
        Assert.Equal(LspSeverity.Error, DiagnosticConverter.ToLspSeverity(CalorSeverity.Error));
        Assert.Equal(LspSeverity.Warning, DiagnosticConverter.ToLspSeverity(CalorSeverity.Warning));
        Assert.Equal(LspSeverity.Information, DiagnosticConverter.ToLspSeverity(CalorSeverity.Info));
    }

    [Fact]
    public void ToLspDiagnostics_ConvertsBag()
    {
        var source = "test code";
        var bag = new DiagnosticBag();
        bag.ReportError(new TextSpan(0, 1, 1, 1), "Calor0001", "Error 1");
        bag.ReportWarning(new TextSpan(0, 1, 1, 1), "Calor0002", "Warning 1");

        var lspDiags = DiagnosticConverter.ToLspDiagnostics(bag, source).ToList();

        Assert.Equal(2, lspDiags.Count);
        Assert.Contains(lspDiags, d => d.Code == "Calor0001" && d.Severity == LspSeverity.Error);
        Assert.Contains(lspDiags, d => d.Code == "Calor0002" && d.Severity == LspSeverity.Warning);
    }

    [Fact]
    public void ToLspDiagnostics_EmptyBag_ReturnsEmpty()
    {
        var source = "test code";
        var bag = new DiagnosticBag();

        var lspDiags = DiagnosticConverter.ToLspDiagnostics(bag, source).ToList();

        Assert.Empty(lspDiags);
    }
}
