using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class MalformedNewExpressionTests
{
    [Theory]
    [InlineData("§NEW{}")]
    [InlineData("§NEW{   }")]
    [InlineData("§NEW{<i32>}")]
    public void EmptyOrGenericOnlyType_ReportsStableDiagnosticWithoutThrowing(
        string expression)
    {
        var source = Wrap(expression);
        var parseDiagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, parseDiagnostics);
        var parser = new Parser(
            lexer.TokenizeAllForParser(),
            parseDiagnostics);

        ModuleNode? module = null;
        var parseException = Record.Exception(() => module = parser.Parse());

        Assert.Null(parseException);
        Assert.NotNull(module);
        var parseError = Assert.Single(
            parseDiagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ExpectedTypeName);
        Assert.Equal(
            "§NEW requires a non-empty type name.",
            parseError.Message);

        var bindingDiagnostics = new DiagnosticBag();
        var bindException = Record.Exception(
            () => new Binder(bindingDiagnostics).Bind(module!));

        Assert.Null(bindException);
        var bindingError = Assert.Single(
            bindingDiagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ExpectedTypeName);
        Assert.Equal(parseError.Message, bindingError.Message);

        CompilationResult? result = null;
        var compileException = Record.Exception(
            () => result = Program.Compile(source));

        Assert.Null(compileException);
        Assert.NotNull(result);
        Assert.True(result.HasErrors);
        var compileError = Assert.Single(
            result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ExpectedTypeName);
        Assert.Equal(parseError.Message, compileError.Message);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.CliInternalError);
    }

    [Fact]
    public void ValidGenericType_PreservesNormalizedTypeIdentity()
    {
        const string source =
            """
            §M{m001:ValidNew}
              §NS{ns1:Alpha}
                §CL{c1:Box:pub}<T>
                  §CTOR{ctor1:pub} ()
                    §P STR:"created"
              §NS{ns2:Beta}
                §F{f1:Make:pub} () -> Alpha.Box<i32>
                  §R §NEW{global::Alpha.Box<i32>} §/NEW
            """;
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(
            lexer.TokenizeAllForParser(),
            diagnostics);
        var module = parser.Parse();

        var bound = new Binder(diagnostics).Bind(module);

        Assert.False(
            diagnostics.HasErrors,
            string.Join(
                Environment.NewLine,
                diagnostics.Errors.Select(error => error.Message)));
        var creation = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundReturnStatement>(
                Assert.Single(bound.Functions.Single(function =>
                    function.Symbol.Name == "Make").Body)).Expression);
        Assert.Equal("global::Alpha.Box", creation.TypeReference.Name);
        Assert.Equal("Alpha.Box`1", creation.ResolvedType!.QualifiedName);
        Assert.Equal(
            "Alpha.Box`1",
            TypeIdentity.ToLookupName(
                creation.TypeReference.Name,
                creation.TypeReference.TypeArguments.Count));
    }

    private static string Wrap(string expression) =>
        $$"""
        §M{m001:MalformedNew}
          §F{f1:Make:pub} () -> object
            §R {{expression}}
        """;
}
