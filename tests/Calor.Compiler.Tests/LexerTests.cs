using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class LexerTests
{
    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    [Fact]
    public void Tokenize_SectionMarker_ReturnsCorrectToken()
    {
        var tokens = Tokenize("§M", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count); // M + EOF
        Assert.Equal(TokenKind.Module, tokens[0].Kind);
    }

    [Fact]
    public void Tokenize_AllKeywords_ReturnsCorrectTokens()
    {
        var source = "§M §/M §F §/F §I §O §E §C §/C §A §R";
        var tokens = Tokenize(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Module, tokens[0].Kind);
        Assert.Equal(TokenKind.EndModule, tokens[1].Kind);
        Assert.Equal(TokenKind.Func, tokens[2].Kind);
        Assert.Equal(TokenKind.EndFunc, tokens[3].Kind);
        Assert.Equal(TokenKind.In, tokens[4].Kind);
        Assert.Equal(TokenKind.Out, tokens[5].Kind);
        Assert.Equal(TokenKind.Effects, tokens[6].Kind);
        Assert.Equal(TokenKind.Call, tokens[7].Kind);
        Assert.Equal(TokenKind.EndCall, tokens[8].Kind);
        Assert.Equal(TokenKind.Arg, tokens[9].Kind);
        Assert.Equal(TokenKind.Return, tokens[10].Kind);
    }

    [Fact]
    public void Tokenize_Brackets_ReturnsCorrectTokens()
    {
        var tokens = Tokenize("[id=m001]", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.OpenBracket, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("id", tokens[1].Text);
        Assert.Equal(TokenKind.Equals, tokens[2].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[3].Kind);
        Assert.Equal("m001", tokens[3].Text);
        Assert.Equal(TokenKind.CloseBracket, tokens[4].Kind);
    }

    [Fact]
    public void Tokenize_IntLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("INT:42", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.IntLiteral, tokens[0].Kind);
        Assert.Equal(42, tokens[0].Value);
    }

    [Fact]
    public void Tokenize_NegativeIntLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("INT:-123", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.IntLiteral, tokens[0].Kind);
        Assert.Equal(-123, tokens[0].Value);
    }

    [Fact]
    public void Tokenize_StringLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("STR:\"Hello, World!\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("Hello, World!", tokens[0].Value);
    }

    [Fact]
    public void Tokenize_StringLiteralWithEscapes_ReturnsCorrectToken()
    {
        var tokens = Tokenize("STR:\"Hello\\nWorld\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("Hello\nWorld", tokens[0].Value);
    }

    [Fact]
    public void Tokenize_BoolLiteralTrue_ReturnsCorrectToken()
    {
        var tokens = Tokenize("BOOL:true", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.BoolLiteral, tokens[0].Kind);
        Assert.Equal(true, tokens[0].Value);
    }

    [Fact]
    public void Tokenize_BoolLiteralFalse_ReturnsCorrectToken()
    {
        var tokens = Tokenize("BOOL:false", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.BoolLiteral, tokens[0].Kind);
        Assert.Equal(false, tokens[0].Value);
    }

    [Fact]
    public void Tokenize_FloatLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("FLOAT:3.14159", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.FloatLiteral, tokens[0].Kind);
        Assert.Equal(3.14159, (double)tokens[0].Value!, 5);
    }

    [Fact]
    public void Tokenize_Identifier_ReturnsCorrectToken()
    {
        var tokens = Tokenize("myVariable", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("myVariable", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_DottedIdentifier_ReturnsCorrectToken()
    {
        var tokens = Tokenize("Console.WriteLine", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("Console.WriteLine", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_UnterminatedString_ReportsError()
    {
        var tokens = Tokenize("STR:\"unterminated", out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UnterminatedString);
    }

    [Fact]
    public void Tokenize_HelloWorldProgram_ReturnsCorrectTokens()
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

        var tokens = Tokenize(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Module);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Func);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Call);
        Assert.Contains(tokens, t => t.Kind == TokenKind.StrLiteral && (string)t.Value! == "Hello from Calor!");
        Assert.Contains(tokens, t => t.Kind == TokenKind.EndModule);
    }

    [Fact]
    public void Tokenize_TracksLineNumbers()
    {
        var source = "§M\n§F";
        var tokens = Tokenize(source, out _);

        Assert.Equal(1, tokens[0].Span.Line);
        Assert.Equal(2, tokens[1].Span.Line);
    }

    [Fact]
    public void Tokenize_TracksColumnNumbers()
    {
        var source = "§M {m001:Test}";
        var tokens = Tokenize(source, out _);

        Assert.Equal(1, tokens[0].Span.Column);
        Assert.Equal(4, tokens[1].Span.Column); // {
    }

    [Fact]
    public void Tokenize_NullCoalesceOperator_ReturnsCorrectToken()
    {
        var tokens = Tokenize("§??", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count); // NullCoalesce + EOF
        Assert.Equal(TokenKind.NullCoalesce, tokens[0].Kind);
        Assert.Equal("§??", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_NullConditionalOperator_ReturnsCorrectToken()
    {
        var tokens = Tokenize("§?.", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count); // NullConditional + EOF
        Assert.Equal(TokenKind.NullConditional, tokens[0].Kind);
        Assert.Equal("§?.", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_NullOperatorsInContext_ReturnsCorrectTokens()
    {
        // Test §?? and §?. in a more realistic context
        var source = "§B result §?? defaultValue";
        var tokens = Tokenize(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Bind, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("result", tokens[1].Text);
        Assert.Equal(TokenKind.NullCoalesce, tokens[2].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[3].Kind);
        Assert.Equal("defaultValue", tokens[3].Text);
    }
}
