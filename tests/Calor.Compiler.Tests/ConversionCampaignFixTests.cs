using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for fixes discovered during the C# to Calor conversion campaign.
/// Each test corresponds to a GitHub issue from the campaign.
/// </summary>
public class ConversionCampaignFixTests
{
    #region Helpers

    private static string ParseAndEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    #endregion

    #region Issue 292: Preserve namespace dots in type names

    [Fact]
    public void Emit_NewWithNamespacedType_PreservesDots()
    {
        var source = @"
§M{m001:Test}
§F{f001:BuildReport:pub}
§O{str}
§E{cw}
§B{sb} §NEW{System.Text.StringBuilder} §/NEW
§R (str sb)
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("new System.Text.StringBuilder()", csharp);
        Assert.DoesNotContain("System_Text_StringBuilder", csharp);
    }

    #endregion
}
