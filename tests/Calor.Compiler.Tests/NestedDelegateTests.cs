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
        var source = "§M{Test}\n§CL{Foo:pub}\n  §DEL{MyHandler}\n    §I{str:input}\n    §O{bool}\n  §/DEL\n\n";

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.Empty(diagnostics);
        var cls = Assert.Single(module.Classes);
        Assert.Equal("Foo", cls.Name);
        var del = Assert.Single(cls.NestedDelegates);
        Assert.Equal("MyHandler", del.Name);
        Assert.NotEmpty(del.Id);
        Assert.Null(del.ToolingId);
        Assert.Single(del.Parameters);
        Assert.NotNull(del.Output);
    }

    [Fact]
    public void NestedDelegate_EmitsCSharp()
    {
        var source = "§M{Test}\n§CL{Foo:pub}\n  §DEL{MyHandler}\n    §I{str:input}\n    §O{bool}\n  §/DEL\n\n";

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
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
        var source = "§M{Test}\n§CL{Foo:pub}\n  §DEL{MyHandler}\n    §I{str:input}\n    §O{bool}\n  §/DEL\n\n";

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        Assert.Empty(diagnostics);

        var emitter = new CalorEmitter();
        var result = emitter.Emit(module);

        Assert.Contains("§DEL{MyHandler}", result);
        Assert.DoesNotContain("§/DEL", result);
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
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains(
            "private delegate bool Handler(string input);",
            result.CalorSource);
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Empty(cls.NestedDelegates);
        Assert.Contains(
            cls.InteropBlocks,
            block => block.CSharpCode.Contains(
                "private delegate bool Handler",
                StringComparison.Ordinal));
    }
}
