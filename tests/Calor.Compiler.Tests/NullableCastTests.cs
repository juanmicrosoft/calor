using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class NullableCastTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body)
    {
        return $$"""
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{object:value}
              §O{object}
              {{body}}
            §/F{f001}
            §/M{m001}
            """;
    }

    [Fact]
    public void ParseLispTypeName_NullablePrefix_ParsesCorrectly()
    {
        var source = WrapInFunction("§R (cast ?i32 value)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var typeOp = returnStmt!.Expression as TypeOperationNode;
        Assert.NotNull(typeOp);
        Assert.Equal(TypeOp.Cast, typeOp!.Operation);
        Assert.Equal("i32?", typeOp.TargetType);
    }

    [Fact]
    public void ParseLispTypeName_NullableGeneric_ParsesCorrectly()
    {
        var source = WrapInFunction("§R (cast ?List<int> value)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var typeOp = returnStmt!.Expression as TypeOperationNode;
        Assert.NotNull(typeOp);
        Assert.Equal(TypeOp.Cast, typeOp!.Operation);
        Assert.Equal("List<int>?", typeOp.TargetType);
    }

    [Fact]
    public void ParseLispTypeName_PostfixNullable_StillWorks()
    {
        var source = WrapInFunction("§R (cast i32? value)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var typeOp = returnStmt!.Expression as TypeOperationNode;
        Assert.NotNull(typeOp);
        Assert.Equal(TypeOp.Cast, typeOp!.Operation);
        Assert.Equal("i32?", typeOp.TargetType);
    }

    [Fact]
    public void CSharpRoundTrip_NullableCast_ProducesPostfixNotation()
    {
        var csharp = @"
public class Foo
{
    public int? Bar(object value)
    {
        return (int?)value;
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, "Conversion should succeed");
        Assert.NotNull(result.CalorSource);
        Assert.True(result.CalorSource!.Length > 0);
        // Converter should emit postfix notation (i32?) not prefix (?i32) in cast position
        Assert.Contains("cast i32?", result.CalorSource);
        Assert.DoesNotContain("cast ?i32", result.CalorSource);
    }
}
