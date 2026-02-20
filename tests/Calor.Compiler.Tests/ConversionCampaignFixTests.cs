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

    #region Issue 294: Support §PROP inside §IFACE for interface properties

    [Fact]
    public void Emit_InterfaceWithProperty_EmitsPropertyNotMethod()
    {
        var source = @"
§M{m001:Test}
§IFACE{i001:IOrder}
§PROP{p001:Purchased:datetime:pub}
§GET §/GET
§/PROP{p001}
§/IFACE{i001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("interface IOrder", csharp);
        // Property should appear (either get-only or get-set depending on branch)
        Assert.Contains("DateTime Purchased", csharp);
        Assert.Contains("get;", csharp);
        Assert.DoesNotContain("DateTime Purchased()", csharp);
    }

    [Fact]
    public void Emit_InterfaceWithPropertyAndMethod_EmitsBoth()
    {
        var source = @"
§M{m001:Test}
§IFACE{i001:IOrder}
§PROP{p001:Cost:f64:pub}
§GET §/GET
§/PROP{p001}
§MT{m001:GetDescription}
§O{str}
§/MT{m001}
§/IFACE{i001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("interface IOrder", csharp);
        Assert.Contains("double Cost", csharp);
        Assert.Contains("get;", csharp);
        Assert.Contains("string GetDescription()", csharp);
    }

    #endregion
}
