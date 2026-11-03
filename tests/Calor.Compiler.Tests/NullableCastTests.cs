using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using Xunit;

namespace Calor.Compiler.Tests;

public class NullableCastTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body)
    {
        return $$"""
            §M{Test}
              §F{Main:pub}
                  §I{object:value}
                  §O{object}
                  {{body}}
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
        Assert.Contains("(cast int? value)", result.CalorSource);
        var compiled = Program.Compile(result.CalorSource, "nullable-cast.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Contains("(int?)value", compiled.GeneratedCode);
        var compilation = CSharpCompilation.Create(
            $"NullableCast_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(compiled.GeneratedCode!)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics));
        var type = Assembly.Load(image.ToArray()).GetTypes().Single(t => t.Name == "Foo");
        var method = type.GetMethod("Bar")!;
        Assert.Equal(typeof(int?), method.ReturnType);
        var instance = Activator.CreateInstance(type);
        Assert.Null(method.Invoke(instance, [null]));
        Assert.Equal(42, method.Invoke(instance, [42]));
    }
}
