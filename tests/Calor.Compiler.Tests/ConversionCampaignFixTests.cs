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

    private static DiagnosticBag ParseWithDiagnostics(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        parser.Parse();

        return diagnostics;
    }

    #endregion

    #region Issue 289: Emit default: instead of case _: in switch

    [Fact]
    public void Emit_WildcardMatchCase_EmitsDefault()
    {
        var source = @"
§M{m001:Test}
§F{f001:MatchTest:pub}
§I{i32:x}
§O{str}
§W{m1} x
§K 1
§R ""one""
§K _
§R ""other""
§/W{m1}
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("default:", csharp);
        Assert.DoesNotContain("case _:", csharp);
    }

    [Fact]
    public void Emit_MatchWithMultipleCasesAndWildcard_OnlyWildcardIsDefault()
    {
        var source = @"
§M{m001:Test}
§F{f001:MatchTest:pub}
§I{i32:x}
§O{str}
§W{m1} x
§K 1
§R ""one""
§K 2
§R ""two""
§K _
§R ""other""
§/W{m1}
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("case 1:", csharp);
        Assert.Contains("case 2:", csharp);
        Assert.Contains("default:", csharp);
        Assert.DoesNotContain("case _:", csharp);
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
    #endregion

    #region Issue 291: Remove @ prefix from this and double/float keywords

    [Fact]
    public void Emit_ThisMemberAccess_NoAtPrefix()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Account:pub}
§FLD{str:_name:priv}
§MT{m001:SetName:pub}
§I{str:name}
§ASSIGN this._name name
§/MT{m001}
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
        Assert.Contains("this._name", csharp);
        Assert.DoesNotContain("@this", csharp);
    }

    [Fact]
    public void Emit_ThisInConstructor_NoAtPrefix()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Account:pub}
§FLD{str:_id:priv}
§CTOR{ctor1:pub}
§I{str:id}
§ASSIGN this._id id
§/CTOR{ctor1}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("get; set;", csharp);
        Assert.Contains("this._id", csharp);
        Assert.DoesNotContain("@this", csharp);
    }

    #endregion
}
