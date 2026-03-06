using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class NullCoalescingTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body, string parameters = "", string returnType = "object")
    {
        return $$"""
            §M{m001:Test}
            §F{f001:Main:pub}
              {{parameters}}
              §O{{{returnType}}}
              {{body}}
            §/F{f001}
            §/M{m001}
            """;
    }

    [Fact]
    public void Parse_NullCoalescing_CreatesNullCoalesceNode()
    {
        var source = WrapInFunction(
            "§R (?? a b)",
            "§I{object:a}\n  §I{object:b}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var nullCoalesce = returnStmt!.Expression as NullCoalesceNode;
        Assert.NotNull(nullCoalesce);
        Assert.IsType<ReferenceNode>(nullCoalesce!.Left);
        Assert.IsType<ReferenceNode>(nullCoalesce.Right);
        Assert.Equal("a", ((ReferenceNode)nullCoalesce.Left).Name);
        Assert.Equal("b", ((ReferenceNode)nullCoalesce.Right).Name);
    }

    [Fact]
    public void Emit_NullCoalescing_ProducesCSharpOperator()
    {
        var source = WrapInFunction(
            "§R (?? a b)",
            "§I{object:a}\n  §I{object:b}");
        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join(", ", result.Diagnostics.Select(d => d.Message)));
        Assert.Contains("a ?? b", result.GeneratedCode);
    }

    [Fact]
    public void Parse_ChainedNullCoalescing_CreatesNestedNodes()
    {
        var source = WrapInFunction(
            "§R (?? a (?? b c))",
            "§I{object:a}\n  §I{object:b}\n  §I{object:c}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var outer = returnStmt!.Expression as NullCoalesceNode;
        Assert.NotNull(outer);
        Assert.IsType<ReferenceNode>(outer!.Left);
        var inner = outer.Right as NullCoalesceNode;
        Assert.NotNull(inner);
        Assert.Equal("b", ((ReferenceNode)inner!.Left).Name);
        Assert.Equal("c", ((ReferenceNode)inner.Right).Name);
    }

    [Fact]
    public void Emit_ChainedNullCoalescing_ProducesChainedCSharp()
    {
        var source = WrapInFunction(
            "§R (?? a (?? b c))",
            "§I{object:a}\n  §I{object:b}\n  §I{object:c}");
        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join(", ", result.Diagnostics.Select(d => d.Message)));
        Assert.Contains("a ?? b ?? c", result.GeneratedCode);
    }

    [Fact]
    public void CSharpRoundTrip_NullCoalescing_GetOrDefault()
    {
        var csharp = @"
public static class NullHandling
{
    public static string GetOrDefault(string value, string defaultValue)
    {
        return value ?? defaultValue;
    }
    public static int GetOrZero(int? value)
    {
        return value ?? 0;
    }
    public static int? GetChained(int? a, int? b, int? c)
    {
        return a ?? b ?? c;
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var convResult = converter.Convert(csharp, "Test.cs");
        Assert.True(convResult.Success, "C# to Calor conversion should succeed: " +
            string.Join(", ", convResult.Issues.Select(i => i.Message)));
        Assert.NotNull(convResult.CalorSource);

        // The Calor source should contain null-coalescing operator
        Assert.Contains("??", convResult.CalorSource!);

        // Now compile the Calor source back to C#
        var compResult = Program.Compile(convResult.CalorSource!);
        Assert.False(compResult.HasErrors,
            "Calor to C# compilation should succeed: " +
            string.Join(", ", compResult.Diagnostics.Select(d => d.Message)));

        // Verify the generated C# contains null-coalescing
        Assert.Contains("??", compResult.GeneratedCode);
    }

    [Fact]
    public void NullableParameterType_ParsesCorrectly()
    {
        var source = WrapInFunction(
            "§R (?? value 0)",
            "§I{?i32:value}",
            "i32");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.Single(func.Parameters);
        // ?i32 expands to OPTION[inner=INT] which becomes int?
        Assert.Contains("OPTION", func.Parameters[0].TypeName);
    }
}
