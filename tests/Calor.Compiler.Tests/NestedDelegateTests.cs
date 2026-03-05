using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class NestedDelegateTests
{
    [Fact]
    public void NestedDelegate_InClass_ParsesSuccessfully()
    {
        var source = "§M{m001:Test}\n§CL{c001:Foo:pub}\n  §DEL{d001:MyHandler:pub}\n    §I{str:input}\n    §O{bool}\n  §/DEL{d001}\n§/CL{c001}\n§/M{m001}";

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.Empty(diagnostics);
        var cls = Assert.Single(module.Classes);
        Assert.Equal("Foo", cls.Name);
        var del = Assert.Single(cls.NestedDelegates);
        Assert.Equal("MyHandler", del.Name);
        Assert.Equal("d001", del.Id);
        Assert.Single(del.Parameters);
        Assert.NotNull(del.Output);
    }

    [Fact]
    public void NestedDelegate_EmitsCSharp()
    {
        var source = "§M{m001:Test}\n§CL{c001:Foo:pub}\n  §DEL{d001:MyHandler}\n    §I{str:input}\n    §O{bool}\n  §/DEL{d001}\n§/CL{c001}\n§/M{m001}";

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        Assert.Empty(diagnostics);

        var emitter = new CSharpEmitter();
        var result = emitter.Emit(module);

        Assert.Contains("delegate", result);
        Assert.Contains("MyHandler", result);
    }

    [Fact]
    public void NestedDelegate_EmitsCalor()
    {
        var source = "§M{m001:Test}\n§CL{c001:Foo:pub}\n  §DEL{d001:MyHandler}\n    §I{str:input}\n    §O{bool}\n  §/DEL{d001}\n§/CL{c001}\n§/M{m001}";

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        Assert.Empty(diagnostics);

        var emitter = new CalorEmitter();
        var result = emitter.Emit(module);

        Assert.Contains("DEL{d001:MyHandler}", result);
        Assert.Contains("/DEL{d001}", result);
    }

    [Fact]
    public void CSharp_NestedDelegate_Converts()
    {
        var csharp = @"
public class Foo
{
    private delegate bool Handler(string input);
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");

        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.Contains("DEL", result.CalorSource);
        var cls = Assert.Single(result.Ast!.Classes);
        var del = Assert.Single(cls.NestedDelegates);
        Assert.Equal("Handler", del.Name);
    }
}
