using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the Phase 3 (RFC §4.1) lexer INDENT/DEDENT post-pass.
/// The plain Tokenize() path is unchanged and covered by LexerTests.
/// </summary>
public class LexerIndentTests
{
    private static List<Token> TokenizeWithIndent(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeWithIndentAll();
    }

    private static List<TokenKind> KindsOnly(List<Token> tokens)
        => tokens.Select(t => t.Kind).Where(k => k != TokenKind.Newline).ToList();

    [Fact]
    public void TokenizeWithIndent_DedentToInconsistentLevel_ReportsCalor0099()
    {
        // Open at col 0, indent to col 4, then dedent to col 2 (not on the stack).
        var src = "§M\n    §F\n  §/F\n§/M\n";
        var tokens = TokenizeWithIndent(src, out var diag);

        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Errors, d => d.Code == DiagnosticCode.MixedIndentation);
    }

    [Fact]
    public void TokenizeWithIndent_MixedTabsAndSpaces_ReportsCalor0099()
    {
        var src = "§M\n \t §F\n§/M\n";
        var tokens = TokenizeWithIndent(src, out var diag);

        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Errors, d => d.Code == DiagnosticCode.MixedIndentation);
    }

    [Fact]
    public void IndentTokenKind_IsNotTrivia()
    {
        // INDENT and DEDENT must NOT be classified as trivia (the parser
        // relies on them being non-trivia structural tokens).
        var indentTok = new Token(TokenKind.Indent, "", new TextSpan(0, 0, 1, 1));
        var dedentTok = new Token(TokenKind.Dedent, "", new TextSpan(0, 0, 1, 1));
        Assert.False(indentTok.IsTrivia);
        Assert.False(dedentTok.IsTrivia);
    }
}
