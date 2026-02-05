using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ControlFlowTests
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
    public void Parse_ForLoop_ReturnsForStatementNode()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Main}{visibility=public}
              §OUT{type=VOID}
              §BODY
                §FOR{id=for1}{var=i}{from=0}{to=10}{step=1}
                  §CALL{target=Console.WriteLine}{fallible=false}
                    §ARG i
                  §END_CALL
                §END_FOR{id=for1}
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var forStmt = func.Body[0] as ForStatementNode;
        Assert.NotNull(forStmt);
        Assert.Equal("i", forStmt.VariableName);
        Assert.Single(forStmt.Body);
    }

    [Fact]
    public void Parse_ForLoop_MismatchedId_ReportsError()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Main}{visibility=public}
              §BODY
                §FOR{id=for1}{var=i}{from=0}{to=10}
                §END_FOR{id=for999}
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.MismatchedId &&
            d.Message.Contains("for1") &&
            d.Message.Contains("for999"));
    }

    [Fact]
    public void Parse_IfStatement_ReturnsIfStatementNode()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Main}{visibility=public}
              §OUT{type=VOID}
              §BODY
                §IF{id=if1} (== x INT:0)
                  §CALL{target=Console.WriteLine}{fallible=false}
                    §ARG STR:"Zero"
                  §END_CALL
                §END_IF{id=if1}
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var ifStmt = func.Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.Single(ifStmt.ThenBody);
        Assert.Empty(ifStmt.ElseIfClauses);
        Assert.Null(ifStmt.ElseBody);
    }

    [Fact]
    public void Parse_IfElseStatement_ReturnsIfStatementNodeWithElse()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Main}{visibility=public}
              §OUT{type=VOID}
              §BODY
                §IF{id=if1} BOOL:true
                  §CALL{target=Console.WriteLine}{fallible=false}
                    §ARG STR:"Then"
                  §END_CALL
                §EL
                  §CALL{target=Console.WriteLine}{fallible=false}
                    §ARG STR:"Else"
                  §END_CALL
                §END_IF{id=if1}
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var ifStmt = module.Functions[0].Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.Single(ifStmt.ThenBody);
        Assert.NotNull(ifStmt.ElseBody);
        Assert.Single(ifStmt.ElseBody);
    }

    [Fact]
    public void Parse_IfElseIfElse_ReturnsCorrectStructure()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Main}{visibility=public}
              §OUT{type=VOID}
              §BODY
                §IF{id=if1} (== x INT:1)
                  §CALL{target=Console.WriteLine}{fallible=false}
                    §ARG STR:"One"
                  §END_CALL
                §EI (== x INT:2)
                  §CALL{target=Console.WriteLine}{fallible=false}
                    §ARG STR:"Two"
                  §END_CALL
                §EL
                  §CALL{target=Console.WriteLine}{fallible=false}
                    §ARG STR:"Other"
                  §END_CALL
                §END_IF{id=if1}
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var ifStmt = module.Functions[0].Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.Single(ifStmt.ThenBody);
        Assert.Single(ifStmt.ElseIfClauses);
        Assert.NotNull(ifStmt.ElseBody);
    }

    [Fact]
    public void Parse_WhileLoop_ReturnsWhileStatementNode()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Main}{visibility=public}
              §OUT{type=VOID}
              §BODY
                §WHILE{id=w1} (< x INT:10)
                  §CALL{target=Console.WriteLine}{fallible=false}
                    §ARG x
                  §END_CALL
                §END_WHILE{id=w1}
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var whileStmt = module.Functions[0].Body[0] as WhileStatementNode;
        Assert.NotNull(whileStmt);
        Assert.Single(whileStmt.Body);
    }

    [Fact]
    public void Parse_BindStatement_ReturnsBindStatementNode()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Main}{visibility=public}
              §OUT{type=VOID}
              §BODY
                §BIND{name=x}{type=INT} INT:42
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var bindStmt = module.Functions[0].Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);
        Assert.Equal("x", bindStmt.Name);
        Assert.Equal("INT", bindStmt.TypeName);
        Assert.NotNull(bindStmt.Initializer);
    }

    [Fact]
    public void Parse_BinaryOperation_ReturnsBinaryOperationNode()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Add}{visibility=public}
              §IN{name=a}{type=INT}
              §IN{name=b}{type=INT}
              §OUT{type=INT}
              §BODY
                §RETURN (+ a b)
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var retStmt = module.Functions[0].Body[0] as ReturnStatementNode;
        Assert.NotNull(retStmt);

        var binOp = retStmt.Expression as BinaryOperationNode;
        Assert.NotNull(binOp);
        Assert.Equal(BinaryOperator.Add, binOp.Operator);
    }

    [Fact]
    public void Compile_ForLoop_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §L{l1:i:1:10:1}
                §C{Console.WriteLine}
                  §A i
                §/C
              §/L{l1}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("for (var i = 1; i <= 10; i++)", result.GeneratedCode);
        Assert.Contains("Console.WriteLine(i)", result.GeneratedCode);
    }

    [Fact]
    public void Compile_IfElse_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §IF{i1} BOOL:true
                §C{Console.WriteLine}
                  §A STR:"Yes"
                §/C
              §EL
                §C{Console.WriteLine}
                  §A STR:"No"
                §/C
              §/I{i1}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("if (true)", result.GeneratedCode);
        Assert.Contains("else", result.GeneratedCode);
    }

    [Fact]
    public void Compile_Bind_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §B{x} INT:42
              §C{Console.WriteLine}
                §A x
              §/C
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("var x = 42;", result.GeneratedCode);
        Assert.Contains("Console.WriteLine(x)", result.GeneratedCode);
    }

    [Fact]
    public void Compile_BinaryOperation_GeneratesValidCSharp()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Add}{visibility=public}
              §IN{name=a}{type=INT}
              §IN{name=b}{type=INT}
              §OUT{type=INT}
              §BODY
                §RETURN (+ a b)
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("return (a + b);", result.GeneratedCode);
    }

    [Fact]
    public void Compile_Modulo_GeneratesValidCSharp()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=IsEven}{visibility=public}
              §IN{name=n}{type=INT}
              §OUT{type=BOOL}
              §BODY
                §RETURN (== (% n INT:2) INT:0)
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("return ((n % 2) == 0);", result.GeneratedCode);
    }

    [Fact]
    public void Parse_DoWhileLoop_ReturnsDoWhileStatementNode()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Main}{visibility=public}
              §OUT{type=VOID}
              §BODY
                §DO{id=do1}
                  §CALL{target=Console.WriteLine}{fallible=false}
                    §ARG x
                  §END_CALL
                §END_DO{id=do1} (< x INT:10)
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var doStmt = module.Functions[0].Body[0] as DoWhileStatementNode;
        Assert.NotNull(doStmt);
        Assert.Equal("do1", doStmt.Id);
        Assert.Single(doStmt.Body);
        Assert.NotNull(doStmt.Condition);
    }

    [Fact]
    public void Parse_DoWhileLoop_MismatchedId_ReportsError()
    {
        var source = """
            §MODULE{id=m001}{name=Test}
            §FUNC{id=f001}{name=Main}{visibility=public}
              §BODY
                §DO{id=do1}
                §END_DO{id=do999} (< x INT:10)
              §END_BODY
            §END_FUNC{id=f001}
            §END_MODULE{id=m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.MismatchedId &&
            d.Message.Contains("do1") &&
            d.Message.Contains("do999"));
    }

    [Fact]
    public void Compile_DoWhileLoop_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §B{i} INT:0
              §DO{d1}
                §C{Console.WriteLine}
                  §A i
                §/C
              §/DO{d1} (< i INT:10)
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("do", result.GeneratedCode);
        Assert.Contains("while ((i < 10));", result.GeneratedCode);
    }
}
