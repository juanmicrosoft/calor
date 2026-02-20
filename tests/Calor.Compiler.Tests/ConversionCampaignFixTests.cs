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

    #region Issue 290: Read-only properties should emit { get; } not { get; set; }

    [Fact]
    public void Emit_ReadOnlyAutoProperty_EmitsGetOnly()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Foo:pub}
§PROP{p001:Name:str:pub}
§GET §/GET
§/PROP{p001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("{ get; }", csharp);
        Assert.DoesNotContain("get; set;", csharp);
    }

    [Fact]
    public void Emit_ReadWriteAutoProperty_EmitsGetSet()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Foo:pub}
§PROP{p001:Name:str:pub}
§GET §/GET
§SET §/SET
§/PROP{p001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("get; set;", csharp);
    }

    #endregion
}
