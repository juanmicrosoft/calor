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
    public void TokenizeWithIndent_NoIndentation_EmitsNoIndentOrDedent()
    {
        var src = "§M\n§/M\n";
        var tokens = TokenizeWithIndent(src, out var diag);

        Assert.False(diag.HasErrors);
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Indent);
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Dedent);
    }

    [Fact]
    public void TokenizeWithIndent_SingleNestedBlock_EmitsBalancedIndentDedent()
    {
        var src = "§M\n  §F\n  §/F\n§/M\n";
        var tokens = TokenizeWithIndent(src, out var diag);

        Assert.False(diag.HasErrors);
        int indents = tokens.Count(t => t.Kind == TokenKind.Indent);
        int dedents = tokens.Count(t => t.Kind == TokenKind.Dedent);
        Assert.Equal(1, indents);
        Assert.Equal(1, dedents);
    }

    [Fact]
    public void TokenizeWithIndent_TwoLevelsDeep_EmitsTwoIndentsAndTwoDedents()
    {
        var src = "§M\n  §F\n    §L\n    §/L\n  §/F\n§/M\n";
        var tokens = TokenizeWithIndent(src, out var diag);

        Assert.False(diag.HasErrors);
        Assert.Equal(2, tokens.Count(t => t.Kind == TokenKind.Indent));
        Assert.Equal(2, tokens.Count(t => t.Kind == TokenKind.Dedent));
    }

    [Fact]
    public void TokenizeWithIndent_DedentToStackLevel_NoDiagnostic()
    {
        // Two sibling §F blocks at the same column. Indent goes 0→2 once,
        // stays at 2 across both, dedents 2→0 once at §/M.
        var src = "§M\n  §F\n  §/F\n  §F\n  §/F\n§/M\n";
        var tokens = TokenizeWithIndent(src, out var diag);

        Assert.False(diag.HasErrors);
        Assert.Equal(1, tokens.Count(t => t.Kind == TokenKind.Indent));
        Assert.Equal(1, tokens.Count(t => t.Kind == TokenKind.Dedent));
    }

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
    public void TokenizeWithIndent_BracketSuppression_NoIndentInsideParens()
    {
        // Inside (), indent tracking is suppressed.
        var src = "§B (\n  a\n  b\n) §R\n";
        var tokens = TokenizeWithIndent(src, out var diag);

        Assert.False(diag.HasErrors);
        // No INDENT/DEDENT should be emitted inside the parenthesized expression.
        Assert.Empty(tokens.Where(t => t.Kind == TokenKind.Indent));
        Assert.Empty(tokens.Where(t => t.Kind == TokenKind.Dedent));
    }

    [Fact]
    public void TokenizeWithIndent_EmitsEofAtEnd_DrainsRemainingStack()
    {
        // Unclosed indent at EOF: drain via implicit dedents, then Eof.
        var src = "§M\n  §F\n";
        var tokens = TokenizeWithIndent(src, out var diag);

        Assert.False(diag.HasErrors);
        Assert.Equal(TokenKind.Eof, tokens[^1].Kind);
        Assert.Equal(1, tokens.Count(t => t.Kind == TokenKind.Indent));
        Assert.Equal(1, tokens.Count(t => t.Kind == TokenKind.Dedent));
    }

    [Fact]
    public void TokenizeWithIndent_PlainTokenizeStillFiltersTrivia()
    {
        // The plain Tokenize() entry point MUST be unchanged: it filters
        // out whitespace, newlines, and does NOT emit Indent/Dedent.
        var src = "§M\n  §F\n  §/F\n§/M\n";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(src, diag);
        var tokens = lexer.TokenizeAll();

        Assert.False(diag.HasErrors);
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Indent);
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Dedent);
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Whitespace);
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Newline);
    }

    [Fact]
    public void TokenizeWithIndent_BlankLine_IgnoredForIndentTracking()
    {
        var src = "§M\n\n  §F\n\n  §/F\n§/M\n";
        var tokens = TokenizeWithIndent(src, out var diag);

        Assert.False(diag.HasErrors);
        Assert.Equal(1, tokens.Count(t => t.Kind == TokenKind.Indent));
        Assert.Equal(1, tokens.Count(t => t.Kind == TokenKind.Dedent));
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
