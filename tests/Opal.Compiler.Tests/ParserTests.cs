using Opal.Compiler.Ast;
using Opal.Compiler.Diagnostics;
using Opal.Compiler.Parsing;
using Xunit;

namespace Opal.Compiler.Tests;

public class ParserTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    [Fact]
    public void Parse_MinimalModule_ReturnsModuleNode()
    {
        var source = """
            §MODULE[id=m001][name=Test]
            §END_MODULE[id=m001]
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
            §MODULE[id=m001][name=Test]
            §FUNC[id=f001][name=MyFunc][visibility=public]
              §OUT[type=VOID]
              §BODY
              §END_BODY
            §END_FUNC[id=f001]
            §END_MODULE[id=m001]
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
            §MODULE[id=m001][name=Test]
            §FUNC[id=f001][name=Add][visibility=public]
              §IN[name=a][type=INT]
              §IN[name=b][type=INT]
              §OUT[type=INT]
              §BODY
              §END_BODY
            §END_FUNC[id=f001]
            §END_MODULE[id=m001]
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
            §MODULE[id=m001][name=Test]
            §FUNC[id=f001][name=Main][visibility=public]
              §OUT[type=VOID]
              §BODY
                §CALL[target=Console.WriteLine][fallible=false]
                  §ARG STR:"Hello"
                §END_CALL
              §END_BODY
            §END_FUNC[id=f001]
            §END_MODULE[id=m001]
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
            §MODULE[id=m001][name=Test]
            §FUNC[id=f001][name=Main][visibility=public]
              §OUT[type=VOID]
              §EFFECTS[io=console_write]
              §BODY
              §END_BODY
            §END_FUNC[id=f001]
            §END_MODULE[id=m001]
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
            §MODULE[id=m001][name=Test]
            §END_MODULE[id=m002]
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
            §MODULE[id=m001][name=Test]
            §FUNC[id=f001][name=Main][visibility=public]
              §BODY
              §END_BODY
            §END_FUNC[id=f999]
            §END_MODULE[id=m001]
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
            §MODULE[id=m001]
            §END_MODULE[id=m001]
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.MissingRequiredAttribute);
    }

    [Fact]
    public void Parse_ReturnStatement_ReturnsReturnNode()
    {
        var source = """
            §MODULE[id=m001][name=Test]
            §FUNC[id=f001][name=GetValue][visibility=public]
              §OUT[type=INT]
              §BODY
                §RETURN INT:42
              §END_BODY
            §END_FUNC[id=f001]
            §END_MODULE[id=m001]
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
            §MODULE[id=m001][name=Hello]
            §FUNC[id=f001][name=Main][visibility=public]
              §OUT[type=VOID]
              §EFFECTS[io=console_write]
              §BODY
                §CALL[target=Console.WriteLine][fallible=false]
                  §ARG STR:"Hello from OPAL!"
                §END_CALL
              §END_BODY
            §END_FUNC[id=f001]
            §END_MODULE[id=m001]
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
        Assert.Equal("Hello from OPAL!", arg.Value);
    }
}
