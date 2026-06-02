using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ParserTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    [Fact]
    public void Parse_MinimalModule_ReturnsModuleNode()
    {
        var source = """
            §M{m001:Test}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("m001", module.Id);
        Assert.Equal("Test", module.Name);
        Assert.Empty(module.Functions);
    }

    [Fact]
    public void Parse_ModuleWithFunction_ReturnsFunctionNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:MyFunc:pub}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Equal("f001", func.Id);
        Assert.Equal("MyFunc", func.Name);
        Assert.Equal(Visibility.Public, func.Visibility);
    }

    [Fact]
    public void Parse_FunctionWithParameters_ReturnsParameterNodes()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Add:pub}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Equal(2, func.Parameters.Count);
        Assert.Equal("a", func.Parameters[0].Name);
        Assert.Equal("INT", func.Parameters[0].TypeName);
        Assert.Equal("b", func.Parameters[1].Name);
    }

    [Fact]
    public void Parse_FunctionWithCallStatement_ReturnsCallNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §C{Console.WriteLine}
                §A "Hello"
              §/C
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var call = func.Body[0] as CallStatementNode;
        Assert.NotNull(call);
        Assert.Equal("Console.WriteLine", call.Target);
        Assert.False(call.Fallible);
        Assert.Single(call.Arguments);

        var arg = call.Arguments[0] as StringLiteralNode;
        Assert.NotNull(arg);
        Assert.Equal("Hello", arg.Value);
    }

    [Fact]
    public void Parse_FunctionWithEffects_ReturnsEffectsNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.NotNull(func.Effects);
        Assert.Equal("console_write", func.Effects.Effects["io"]);
    }

    [Fact]
    public void Parse_MismatchedModuleId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §/M{m002}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.MismatchedId);
        Assert.Contains(diagnostics, d => d.Message.Contains("m002") && d.Message.Contains("m001"));
    }

    [Fact]
    public void Parse_MismatchedFuncId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
            §/F{f999}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.MismatchedId &&
            d.Message.Contains("f999") &&
            d.Message.Contains("f001"));
    }

    [Fact]
    public void Parse_MissingRequiredAttribute_ReportsError()
    {
        var source = """
            §M{m001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.MissingRequiredAttribute);
    }

    [Fact]
    public void Parse_ReturnStatement_ReturnsReturnNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetValue:pub}
              §O{i32}
              §R 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var ret = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(ret);
        Assert.NotNull(ret.Expression);

        var literal = ret.Expression as IntLiteralNode;
        Assert.NotNull(literal);
        Assert.Equal(42, literal.Value);
    }

    [Fact]
    public void Parse_HelloWorldProgram_Succeeds()
    {
        var source = """
            §M{m001:Hello}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §C{Console.WriteLine}
                §A "Hello from Calor!"
              §/C
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("m001", module.Id);
        Assert.Equal("Hello", module.Name);
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Equal("Main", func.Name);
        Assert.Equal(Visibility.Public, func.Visibility);
        Assert.Single(func.Body);

        var call = func.Body[0] as CallStatementNode;
        Assert.NotNull(call);
        Assert.Equal("Console.WriteLine", call.Target);
        Assert.Single(call.Arguments);

        var arg = call.Arguments[0] as StringLiteralNode;
        Assert.NotNull(arg);
        Assert.Equal("Hello from Calor!", arg.Value);
    }
}
