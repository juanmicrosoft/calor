// migrate_inline_calor: skip - fixture intentionally embeds closer-form Calor literals or uses position/template patterns incompatible with auto-migration.
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class LexerTests
{
    [Theory]
    [InlineData("§M")]
    [InlineData("§F")]
    [InlineData("§CL")]
    [InlineData("§/M")]
    [InlineData("§/C")]
    [InlineData("§/NEW")]
    [InlineData("§/LAM")]
    [InlineData("§SN")]
    [InlineData("§DP")]
    [InlineData("§BR")]
    [InlineData("§RAW")]
    [InlineData("§CSHARP")]
    [InlineData("§CS")]
    [InlineData("§IF")]
    [InlineData("§ARR")]
    [InlineData("§EACHKV")]
    public void CanonicalCutover_RejectsEveryLegacyMarkerBoundary(string marker)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(marker, diagnostics).TokenizeAll();
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(marker, tokens[0].Text);
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, Assert.Single(diagnostics.Errors).Code);
    }

    [Theory]
    [InlineData("INT:42")]
    [InlineData("LONG:42")]
    [InlineData("UINT:42")]
    [InlineData("ULONG:42")]
    [InlineData("FLOAT:1.5")]
    [InlineData("SINGLE:1.5")]
    [InlineData("DEC:1.5")]
    [InlineData("DECIMAL:1.5")]
    [InlineData("BOOL:true")]
    [InlineData("STR:\"text\"")]
    [InlineData("int:42")]
    [InlineData("str:\"text\"")]
    public void CanonicalCutover_RejectsTypedLiteralPrefixes(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAll();
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        var error = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.InvalidTypedLiteral, error.Code);
        Assert.Contains("removed", error.Message);
        Assert.DoesNotContain("Use 'INT:'", error.Message);
    }

    [Theory]
    [InlineData("->")]
    [InlineData("`name`")]
    [InlineData("$\"text\"")]
    [InlineData("@\"text\"")]
    public void CanonicalCutover_RejectsRetiredArrowAndStringIdentifierPrefixes(string source)
    {
        var diagnostics = new DiagnosticBag();
        Assert.Equal(TokenKind.Error, new Lexer(source, diagnostics).TokenizeAll()[0].Kind);
        Assert.NotEmpty(diagnostics.Errors);
    }

    [Fact]
    public void CanonicalCutover_RawStringsAndDocumentationKeepLegacyLookingTextOpaque()
    {
        const string payload = "§M{not Calor}\r\nINT:42 $\"${0}\" `name` ->\n";
        var bytes = System.Text.Encoding.UTF8.GetByteCount(payload);
        var source = $"/// since §SN INT:42\nraw statement {bytes}\n{payload}\n\"§M INT:42 -> \\${{0}}\"";
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAll();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(payload, Assert.IsType<RawCSharpFrame>(tokens[0].Value).Code);
        Assert.Equal("since §SN INT:42", tokens[0].DocComment);
        Assert.Equal(TokenKind.StrLiteral, tokens[1].Kind);
    }

    [Fact]
    public void CanonicalWords_ExposeTheSharedParserInterface()
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer("module class record func let var for from to step each in do while return set ! => → ;", diagnostics)
            .TokenizeAll();
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(new[]
        {
            TokenKind.Module, TokenKind.Class, TokenKind.Record, TokenKind.Func,
            TokenKind.Let, TokenKind.Var, TokenKind.For, TokenKind.From, TokenKind.To,
            TokenKind.Step, TokenKind.Foreach, TokenKind.In, TokenKind.Do, TokenKind.While,
            TokenKind.Return, TokenKind.Assign, TokenKind.Exclamation, TokenKind.FatArrow,
            TokenKind.Arrow, TokenKind.Semicolon, TokenKind.Eof
        }, tokens.Select(token => token.Kind));
    }

    [Fact]
    public void CanonicalEscapedNames_KeepSourceTextAndUnescapedIdentity()
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer("@new(x) upper(x) @module.Module Module", diagnostics).TokenizeAll();
        Assert.False(diagnostics.HasErrors);
        Assert.Equal("@new", tokens[0].Text);
        Assert.Equal("new", tokens[0].Value);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[4].Kind);
        Assert.Equal("upper", tokens[4].Text);
        Assert.Equal("@module", tokens[8].Text);
        Assert.Equal("module", tokens[8].Value);
        Assert.Equal(TokenKind.Dot, tokens[9].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[10].Kind);
        Assert.Equal("Module", tokens[11].Text);
        Assert.Equal(TokenKind.Identifier, tokens[11].Kind);
    }

    [Fact]
    public void CanonicalDocumentation_RemainsAttachedWithoutInterpretingProse()
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer("/// since 1.2.0\n/// deprecated reason\nfunc F", diagnostics).TokenizeAll();
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Func, tokens[0].Kind);
        Assert.Equal("since 1.2.0\ndeprecated reason", tokens[0].DocComment);
    }

    [Theory]
    [InlineData("§M{Bad}\n  §F{Main:pub} () -> void\n    §INVALID_TAG\n", DiagnosticCode.UnknownSectionMarker)]
    [InlineData("§M{Bad}\n  §F{Main:pub} () -> void\n    §E{cw}\n    §P \"unfinished\n", DiagnosticCode.UnterminatedString)]
    public void LexicalErrors_RemainFatalWithoutGenericParserCascades(string source, string code)
    {
        var result = Program.Compile(source, "lexical-error.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Equal(code, Assert.Single(result.Diagnostics.Errors).Code);
        var exception = Assert.Throws<DeclarationIdentityParseException>(() =>
            DeclarationIdentityStore.Describe("lexical-error.calr", source));
        Assert.Equal(code, Assert.Single(exception.Diagnostics.Errors).Code);
    }

    [Fact]
    public void IdentityParsing_StillRejectsParserErrorsAfterSuccessfulLexing()
    {
        const string source = "§M{Bad}\n  §F{Main:pub} () -> void\n    §R §/C\n";
        var exception = Assert.Throws<DeclarationIdentityParseException>(() =>
            DeclarationIdentityStore.Describe("parser-error.calr", source));
        Assert.Contains(exception.Diagnostics.Errors, error => error.Code == DiagnosticCode.UnexpectedToken);
    }

    [Theory]
    [InlineData("int:42", "INT:")]
    [InlineData("Str:\"x\"", "STR:")]
    [InlineData("bool:true", "BOOL:")]
    [InlineData("Float:1.5", "FLOAT:")]
    [InlineData("single:1.5", "SINGLE:")]
    [InlineData("long:42", "LONG:")]
    [InlineData("Uint:42", "UINT:")]
    [InlineData("ulong:42", "ULONG:")]
    [InlineData("dec:1.5", "DEC:")]
    [InlineData("decimal:1.5", "DECIMAL:")]
    public void Tokenize_LiteralPrefixCaseVariant_ReportsExactSpelling(string source, string canonical)
    {
        var tokens = Tokenize(source, out var diagnostics);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.InvalidTypedLiteral && diagnostic.Message.Contains(canonical));
    }

    [Theory]
    [InlineData("bool:trueTest")]
    [InlineData("bool:falseAlarm")]
    public void Tokenize_BooleanParameterName_IsNotALiteralPrefix(string source)
    {
        var tokens = Tokenize(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(TokenKind.Colon, tokens[1].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[2].Kind);
    }

    [Theory]
    [InlineData("T", "§CL")]
    [InlineData("/T", "§CL")]
    [InlineData("V", "§EN")]
    [InlineData("DEFAULT", "'_'")]
    [InlineData("ASYNC", "§AF")]
    [InlineData("VR", "'vr'")]
    [InlineData("OV", "'ov'")]
    [InlineData("AB", "'abs'")]
    [InlineData("SD", "'seal'")]
    [InlineData("XP", "///")]
    [InlineData("SB", "///")]
    [InlineData("BODY", "indentation")]
    [InlineData("END_BODY", "indentation")]
    [InlineData("TP", "<T>")]
    [InlineData("G", "List<i32>")]
    public void Tokenize_RemovedKeyword_ReportsReplacement(string marker, string replacement)
    {
        var tokens = Tokenize($"§{marker} §M", out var diagnostics);

        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(TokenKind.Module, tokens[1].Kind);
        Assert.Contains(replacement, Assert.Single(diagnostics.Errors).Message);
        Assert.DoesNotContain(marker, Lexer.KeywordNames);
        Assert.DoesNotContain(marker, SectionMarkerSuggestions.AllMarkers);
    }

    [Theory]
    [InlineData("∀", "forall")]
    [InlineData("∃", "exists")]
    public void Tokenize_RemovedUnicodeQuantifier_ReportsReplacement(string symbol, string replacement)
    {
        var tokens = Tokenize(symbol, out var diagnostics);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Contains(replacement, Assert.Single(diagnostics.Errors).Message);
    }

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

    [Theory]
    [InlineData("SW", "W")]
    [InlineData("ENUM", "EN")]
    [InlineData("WR", "WHERE")]
    [InlineData("ADD", "PUSH")]
    [InlineData("when", "WHEN")]
    public void Tokenize_RemovedAlias_ReportsCanonicalMarkerAndFix(string alias, string canonical)
    {
        var source = $"§{alias}{{value}}";
        var tokens = Tokenize(source, out var diagnostics);

        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.DoesNotContain(alias, Lexer.KeywordNames);
        Assert.DoesNotContain(alias, SectionMarkerSuggestions.AllMarkers);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, diagnostic.Code);
        Assert.Contains($"'§{canonical}'", diagnostic.Message);

        var fix = Assert.Single(diagnostics.DiagnosticsWithFixes);
        var edit = Assert.Single(fix.Fix.Edits);
        Assert.Equal(1, edit.StartLine);
        Assert.Equal(1, edit.StartColumn);
        Assert.Equal(1, edit.EndLine);
        Assert.Equal(alias.Length + 2, edit.EndColumn);
        Assert.Equal($"§{canonical}", edit.NewText);
        var corrected = edit.NewText + source[(edit.EndColumn - 1)..];
        var correctedTokens = Tokenize(corrected, out var correctedDiagnostics);
        Assert.False(correctedDiagnostics.HasErrors);
        Assert.NotEqual(TokenKind.Error, correctedTokens[0].Kind);
    }

    [Theory]
    [InlineData("/SW", "W")]
    [InlineData("/ENUM", "EN")]
    public void Tokenize_RemovedAliasCloser_RequiresDedentInsteadOfAnotherCloser(string alias, string canonical)
    {
        var tokens = Tokenize($"§{alias}", out var diagnostics);

        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.DoesNotContain(alias, Lexer.KeywordNames);
        Assert.DoesNotContain(alias, SectionMarkerSuggestions.AllMarkers);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, diagnostic.Code);
        Assert.Contains($"'§{canonical}'", diagnostic.Message);
        Assert.Contains("dedenting", diagnostic.Message);
        Assert.Empty(diagnostics.DiagnosticsWithFixes);
    }

    [Theory]
    [InlineData("W", TokenKind.Match)]
    [InlineData("EN", TokenKind.Enum)]
    [InlineData("WHERE", TokenKind.Where)]
    [InlineData("PUSH", TokenKind.Push)]
    [InlineData("WHEN", TokenKind.When)]
    public void Tokenize_CanonicalAliasReplacement_ReturnsCorrectToken(string marker, TokenKind kind)
    {
        var tokens = Tokenize($"§{marker}", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(kind, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Tokenize_RemovedAlias_DoesNotConsumeFollowingMarker()
    {
        var tokens = Tokenize("§SW §W", out var diagnostics);

        Assert.Single(diagnostics.Errors);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(TokenKind.Match, tokens[1].Kind);
        Assert.Equal(TokenKind.Eof, tokens[2].Kind);
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
        var info = Assert.IsType<IntLiteralInfo>(tokens[0].Value);
        Assert.Equal(42UL, info.Magnitude);
        Assert.Equal(IntegerLiteralSign.Positive, info.Sign);
        Assert.Equal(IntegerLiteralBase.Decimal, info.Base);
        Assert.Equal(IntegerLiteralWidth.Bits32, info.Width);
        Assert.Equal(IntegerLiteralSignedness.Signed, info.Signedness);
    }

    [Fact]
    public void Tokenize_NegativeIntLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("INT:-123", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.IntLiteral, tokens[0].Kind);
        var info = Assert.IsType<IntLiteralInfo>(tokens[0].Value);
        Assert.Equal(123UL, info.Magnitude);
        Assert.Equal(IntegerLiteralSign.Negative, info.Sign);
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
            §M{Hello}
            §F{Main:pub}
              §O{void}
              §E{cw}
              §C{Console.WriteLine}
                §A "Hello from Calor!"
              §/C
            §/F
            §/M
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
