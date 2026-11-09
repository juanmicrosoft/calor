// migrate_inline_calor: skip - fixture intentionally embeds closer-form Calor literals or uses position/template patterns incompatible with auto-migration.
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class LexerTests
{
    public static IEnumerable<object[]> CanonicalWordSpellings =>
        Lexer.KeywordNames.Where(word => word != "raw").Select(word => new object[] { word });

    [Theory]
    [MemberData(nameof(CanonicalWordSpellings))]
    public void CanonicalKeywordBoundary_IsExactCaseAndEscapable(string word)
    {
        var tokens = Tokenize($"{word} {char.ToUpperInvariant(word[0])}{word[1..]} {word.ToUpperInvariant()} @{word}", out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.True(tokens[0].IsKeyword);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[2].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[3].Kind);
        Assert.Equal("@" + word, tokens[3].Text);
        Assert.Equal(word, tokens[3].Value);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("not")]
    [InlineData("mod")]
    [InlineData("upper")]
    [InlineData("lower")]
    [InlineData("cast")]
    [InlineData("raw")]
    [InlineData("forall")]
    [InlineData("exists")]
    [InlineData("namespace")]
    [InlineData("indexer")]
    [InlineData("operator")]
    [InlineData("block")]
    [InlineData("label")]
    [InlineData("foreach")]
    [InlineData("yieldbreak")]
    public void CanonicalOrdinaryNames_DoNotCaptureWordOperatorsOrBuiltins(string name)
    {
        var diagnostics = new DiagnosticBag();
        var expression = new Parser(new Lexer($"{name}(x, y)", diagnostics).TokenizeAllForParser(), diagnostics)
            .ParseStandaloneExpression();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(name, Assert.IsType<Calor.Compiler.Ast.CallExpressionNode>(expression).Target);
    }

    [Theory]
    [InlineData("1e309d")]
    [InlineData("1e309D")]
    [InlineData("3.5e39f")]
    [InlineData("3.5e39F")]
    [InlineData("1e+d")]
    public void CanonicalFloatingSuffix_InvalidMagnitudeFailsExplicitly(string source)
    {
        var diagnostics = new DiagnosticBag();
        Assert.Contains(new Lexer(source, diagnostics).TokenizeAll(), token => token.Kind == TokenKind.Error);
        Assert.Equal(DiagnosticCode.InvalidTypedLiteral, Assert.Single(diagnostics.Errors).Code);
    }

    [Theory]
    [InlineData("1dD")]
    [InlineData("1fF")]
    [InlineData("1.0df")]
    [InlineData("1.0mD")]
    public void CanonicalFloatingSuffix_ExtraSuffixCannotBecomeAValidExpression(string source)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).ParseStandaloneExpression();
        Assert.Contains(diagnostics.Errors, error => error.Code == DiagnosticCode.UnexpectedToken);
    }

    [Theory]
    [InlineData("2147483647", "System.Int32")]
    [InlineData("-2147483648", "System.Int32")]
    [InlineData("2147483648L", "System.Int64")]
    [InlineData("9223372036854775807L", "System.Int64")]
    [InlineData("-9223372036854775808L", "System.Int64")]
    [InlineData("4294967295U", "System.UInt32")]
    [InlineData("18446744073709551615UL", "System.UInt64")]
    [InlineData("0xFFFFFFFFU", "System.UInt32")]
    [InlineData("1.25f", "System.Single")]
    [InlineData("1F", "System.Single")]
    [InlineData("1.25F", "System.Single")]
    [InlineData("1e2F", "System.Single")]
    [InlineData("1.25", "System.Double")]
    [InlineData("1d", "System.Double")]
    [InlineData("1D", "System.Double")]
    [InlineData("1.25D", "System.Double")]
    [InlineData("1e2d", "System.Double")]
    [InlineData("1.25m", "System.Decimal")]
    [InlineData("-0.0d", "System.Double")]
    [InlineData("-0.0f", "System.Single")]
    [InlineData("-0.00m", "System.Decimal")]
    [InlineData("'π'", "System.Char")]
    [InlineData("\"a\\n\\\"b\"", "System.String")]
    public void CanonicalLiteralBoundary_PreservesClrWidthValueAndSignedZero(string source, string expectedType)
    {
        var diagnostics = new DiagnosticBag();
        var expression = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics)
            .ParseStandaloneExpression();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        var calor = expression.Accept(new CalorEmitter());
        Assert.DoesNotContain('§', calor);
        var roundtripDiagnostics = new DiagnosticBag();
        var roundtrip = new Parser(new Lexer(calor, roundtripDiagnostics).TokenizeAllForParser(), roundtripDiagnostics)
            .ParseStandaloneExpression();
        Assert.False(roundtripDiagnostics.HasErrors, string.Join("\n", roundtripDiagnostics.Errors));
        var generated = roundtrip.Accept(new CSharpEmitter());
        var syntax = CSharpSyntaxTree.ParseText($$"""
            public static class LiteralProbe
            {
                public static object Original() => {{source}};
                public static object Generated() => {{generated}};
            }
            """);
        var compilation = CSharpCompilation.Create("Literal_" + Guid.NewGuid().ToString("N"),
            [syntax], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compilation.Emit(bytes);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var probe = System.Reflection.Assembly.Load(bytes.ToArray()).GetType("LiteralProbe")!;
        var original = probe.GetMethod("Original")!.Invoke(null, null)!;
        var actual = probe.GetMethod("Generated")!.Invoke(null, null)!;
        Assert.Equal(expectedType, original.GetType().FullName);
        Assert.Equal(expectedType, actual.GetType().FullName);
        Assert.Equal(original, actual);
        if (source.StartsWith("-0", StringComparison.Ordinal))
        {
            switch (actual)
            {
                case double value:
                    Assert.Equal(BitConverter.DoubleToInt64Bits((double)original), BitConverter.DoubleToInt64Bits(value));
                    Assert.True(BitConverter.DoubleToInt64Bits(value) < 0);
                    break;
                case float value:
                    Assert.Equal(BitConverter.SingleToInt32Bits((float)original), BitConverter.SingleToInt32Bits(value));
                    Assert.True(BitConverter.SingleToInt32Bits(value) < 0);
                    break;
                case decimal value:
                    Assert.Equal(decimal.GetBits((decimal)original), decimal.GetBits(value));
                    Assert.NotEqual(0, decimal.GetBits(value)[3] & int.MinValue);
                    break;
            }
        }
    }

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
    [InlineData("{INT:42}")]
    [InlineData("{STR:\"x\"}")]
    [InlineData("x:INT:42")]
    public void CanonicalCutover_BracesAndColonsCannotShieldTypedLiteralPrefixes(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAll();
        Assert.Contains(tokens, token => token.Kind == TokenKind.Error);
        Assert.Equal(DiagnosticCode.InvalidTypedLiteral, Assert.Single(diagnostics.Errors).Code);
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
    public void SupplementalWords_ExposeOnlyTheSelectedReservedHeads()
    {
        var tokens = Tokenize("record op pp yield break goto case each", out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(new[]
        {
            TokenKind.Record, TokenKind.OperatorOverload, TokenKind.Preprocessor,
            TokenKind.Yield, TokenKind.Break, TokenKind.Goto, TokenKind.Case,
            TokenKind.Foreach, TokenKind.Eof
        }, tokens.Select(token => token.Kind));
        Assert.DoesNotContain("namespace", Lexer.KeywordNames);
        Assert.DoesNotContain("indexer", Lexer.KeywordNames);
        Assert.DoesNotContain("foreach", Lexer.KeywordNames);
        Assert.DoesNotContain("yieldbreak", Lexer.KeywordNames);
    }

    [Theory]
    [InlineData("0..items.Length", TokenKind.IntLiteral, TokenKind.Identifier)]
    [InlineData("start..end", TokenKind.Identifier, TokenKind.Identifier)]
    [InlineData("source.Start..source.End", TokenKind.Identifier, TokenKind.Identifier)]
    [InlineData(".5..1.5", TokenKind.FloatLiteral, TokenKind.FloatLiteral)]
    [InlineData("1..2", TokenKind.IntLiteral, TokenKind.IntLiteral)]
    [InlineData("@start..@end", TokenKind.Identifier, TokenKind.Identifier)]
    [InlineData("α..ω", TokenKind.Identifier, TokenKind.Identifier)]
    public void SupplementalRange_UsesMaximalMunchWithoutCapturingDottedNames(
        string source, TokenKind left, TokenKind right)
    {
        var tokens = Tokenize(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(new[] { left, TokenKind.RangeOp, right, TokenKind.Eof },
            tokens.Select(token => token.Kind));
        Assert.Equal("..", tokens[1].Text);
        Assert.Equal(source.IndexOf("..", StringComparison.Ordinal), tokens[1].Span.Start);
        Assert.Equal(2, tokens[1].Span.Length);
        Assert.False(tokens[1].IsKeyword);
        Assert.Equal(source, string.Concat(tokens.Take(3).Select(token => token.Text)));
    }

    [Fact]
    public void SupplementalEffectsLabelsAndNamespaceOrigin_RemainContextualAtoms()
    {
        var tokens = Tokenize("<! e> done: block forall exists", out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(new[]
        {
            TokenKind.Less, TokenKind.Exclamation, TokenKind.Identifier, TokenKind.Greater,
            TokenKind.Identifier, TokenKind.Colon, TokenKind.Identifier, TokenKind.Identifier,
            TokenKind.Identifier, TokenKind.Eof
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
    [InlineData("module Bad\n  func Main () → void\n    §INVALID_TAG\n", DiagnosticCode.UnknownSectionMarker)]
    [InlineData("module Bad\n  func Main () → void\n    ! cw\n    System.Console.WriteLine(\"unfinished\n", DiagnosticCode.UnterminatedString)]
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
        DeclarationIdentityStore.Describe("valid-parser-boundary.calr",
            "module Good\n  func Main () → int\n    return 1\n");
        const string source = "module Bad\n  func Main () → void\n    return f(,)\n";
        var lexicalDiagnostics = new DiagnosticBag();
        new Lexer(source, lexicalDiagnostics).TokenizeAllForParser();
        Assert.False(lexicalDiagnostics.HasErrors);
        var exception = Assert.Throws<DeclarationIdentityParseException>(() =>
            DeclarationIdentityStore.Describe("parser-error.calr", source));
        Assert.Contains(exception.Diagnostics.Errors, error =>
            error.Code == DiagnosticCode.UnexpectedToken && error.Span.Line == 3);
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
    public void Tokenize_LiteralPrefixCaseVariant_ReportsRetirement(string source, string formerCanonical)
    {
        var tokens = Tokenize(source, out var diagnostics);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.InvalidTypedLiteral
            && diagnostic.Message.Contains(source[..(source.IndexOf(':') + 1)])
            && diagnostic.Message.Contains("removed"));
        Assert.DoesNotContain(diagnostics.Errors, diagnostic => diagnostic.Message.Contains($"Use '{formerCanonical}'"));
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
    [InlineData("T")]
    [InlineData("/T")]
    [InlineData("V")]
    [InlineData("DEFAULT")]
    [InlineData("ASYNC")]
    [InlineData("VR")]
    [InlineData("OV")]
    [InlineData("AB")]
    [InlineData("SD")]
    [InlineData("XP")]
    [InlineData("SB")]
    [InlineData("BODY")]
    [InlineData("END_BODY")]
    [InlineData("TP")]
    [InlineData("G")]
    public void Tokenize_RemovedKeyword_DiagnosesWithoutConsumingNextNativeWord(string marker)
    {
        var tokens = Tokenize($"§{marker} module", out var diagnostics);

        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(TokenKind.Module, tokens[1].Kind);
        Assert.Contains($"'{tokens[0].Text}' is removed", Assert.Single(diagnostics.Errors).Message);
        Assert.Empty(diagnostics.DiagnosticsWithFixes);
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
    public void Tokenize_ModuleWord_ReturnsCorrectToken()
    {
        var tokens = Tokenize("module", out var diagnostics);

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
    public void Tokenize_RemovedAlias_DoesNotOfferSuccessShapedKeywordOnlyMigration(string alias, string formerCanonical)
    {
        var source = $"§{alias}{{value}}";
        var tokens = Tokenize(source, out var diagnostics);

        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.DoesNotContain("§" + alias, Lexer.KeywordNames);
        Assert.DoesNotContain("§" + alias, SectionMarkerSuggestions.AllMarkers);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, diagnostic.Code);
        Assert.Contains("removed", diagnostic.Message);
        Assert.DoesNotContain($"'§{formerCanonical}'", diagnostic.Message);
        Assert.Empty(diagnostics.DiagnosticsWithFixes);
    }

    [Theory]
    [InlineData("/SW", "W")]
    [InlineData("/ENUM", "EN")]
    public void Tokenize_RemovedAliasCloser_RequiresDedentInsteadOfAnotherCloser(string alias, string formerCanonical)
    {
        var tokens = Tokenize($"§{alias}", out var diagnostics);

        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.DoesNotContain(alias, Lexer.KeywordNames);
        Assert.DoesNotContain(alias, SectionMarkerSuggestions.AllMarkers);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, diagnostic.Code);
        Assert.DoesNotContain($"'§{formerCanonical}'", diagnostic.Message);
        Assert.Contains("dedent", diagnostic.Message);
        Assert.Empty(diagnostics.DiagnosticsWithFixes);
    }

    [Theory]
    [InlineData("match", TokenKind.Match)]
    [InlineData("enum", TokenKind.Enum)]
    [InlineData("where", TokenKind.Where)]
    [InlineData("push", TokenKind.Identifier)]
    [InlineData("when", TokenKind.When)]
    public void Tokenize_CanonicalWordsAndOrdinaryCallNames_ReturnCorrectTokens(string marker, TokenKind kind)
    {
        var tokens = Tokenize(marker, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(kind, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Tokenize_RemovedAlias_DoesNotConsumeFollowingMarker()
    {
        var tokens = Tokenize("§SW match", out var diagnostics);

        Assert.Single(diagnostics.Errors);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(TokenKind.Match, tokens[1].Kind);
        Assert.Equal(TokenKind.Eof, tokens[2].Kind);
    }

    [Fact]
    public void Tokenize_AllKeywords_ReturnsCorrectTokens()
    {
        var source = "module func in out ! return let var => →";
        var tokens = Tokenize(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Module, tokens[0].Kind);
        Assert.Equal(TokenKind.Func, tokens[1].Kind);
        Assert.Equal(TokenKind.In, tokens[2].Kind);
        Assert.Equal(TokenKind.Out, tokens[3].Kind);
        Assert.Equal(TokenKind.Exclamation, tokens[4].Kind);
        Assert.Equal(TokenKind.Return, tokens[5].Kind);
        Assert.Equal(TokenKind.Let, tokens[6].Kind);
        Assert.Equal(TokenKind.Var, tokens[7].Kind);
        Assert.Equal(TokenKind.FatArrow, tokens[8].Kind);
        Assert.Equal(TokenKind.Arrow, tokens[9].Kind);
        Assert.Equal(TokenKind.Eof, tokens[10].Kind);
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
        var tokens = Tokenize("42", out var diagnostics);

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
        var tokens = Tokenize("-123", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Minus, tokens[0].Kind);
        Assert.Equal(TokenKind.IntLiteral, tokens[1].Kind);
        var info = Assert.IsType<IntLiteralInfo>(tokens[1].Value);
        Assert.Equal(123UL, info.Magnitude);
        Assert.Equal(IntegerLiteralSign.Positive, info.Sign);
    }

    [Fact]
    public void Tokenize_StringLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("\"Hello, World!\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("Hello, World!", tokens[0].Value);
    }

    [Fact]
    public void Tokenize_StringLiteralWithEscapes_ReturnsCorrectToken()
    {
        var tokens = Tokenize("\"Hello\\nWorld\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("Hello\nWorld", tokens[0].Value);
    }

    [Fact]
    public void Tokenize_BoolLiteralTrue_ReturnsCorrectToken()
    {
        var tokens = Tokenize("true", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.BoolLiteral, tokens[0].Kind);
        Assert.Equal(true, tokens[0].Value);
    }

    [Fact]
    public void Tokenize_BoolLiteralFalse_ReturnsCorrectToken()
    {
        var tokens = Tokenize("false", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.BoolLiteral, tokens[0].Kind);
        Assert.Equal(false, tokens[0].Value);
    }

    [Fact]
    public void Tokenize_FloatLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("3.14159", out var diagnostics);

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
        var tokens = Tokenize("\"unterminated", out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UnterminatedString);
    }

    [Fact]
    public void Tokenize_HelloWorldProgram_ReturnsCorrectTokens()
    {
        var source = """
            module Hello
              func Main () → void
                ! cw
                System.Console.WriteLine("Hello from Calor!")
            """;

        var tokens = Tokenize(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Module);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Func);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Identifier && t.Text == "System.Console.WriteLine");
        Assert.Contains(tokens, t => t.Kind == TokenKind.StrLiteral && (string)t.Value! == "Hello from Calor!");
        Assert.Equal(TokenKind.Eof, tokens[^1].Kind);
    }

    [Fact]
    public void Tokenize_TracksLineNumbers()
    {
        var source = "module\nfunc";
        var tokens = Tokenize(source, out _);

        Assert.Equal(1, tokens[0].Span.Line);
        Assert.Equal(2, tokens[1].Span.Line);
    }

    [Fact]
    public void Tokenize_TracksColumnNumbers()
    {
        var source = "module Hello";
        var tokens = Tokenize(source, out _);

        Assert.Equal(1, tokens[0].Span.Column);
        Assert.Equal(8, tokens[1].Span.Column);
    }

    [Fact]
    public void Tokenize_NullCoalesceOperator_ReturnsCorrectToken()
    {
        var tokens = Tokenize("??", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count); // NullCoalesce + EOF
        Assert.Equal(TokenKind.NullCoalesce, tokens[0].Kind);
        Assert.Equal("??", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_NullConditionalOperator_ReturnsCorrectToken()
    {
        var tokens = Tokenize("?.", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count); // NullConditional + EOF
        Assert.Equal(TokenKind.NullConditional, tokens[0].Kind);
        Assert.Equal("?.", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_NullOperatorsInContext_ReturnsCorrectTokens()
    {
        var source = "let result ?? defaultValue";
        var tokens = Tokenize(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Let, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("result", tokens[1].Text);
        Assert.Equal(TokenKind.NullCoalesce, tokens[2].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[3].Kind);
        Assert.Equal("defaultValue", tokens[3].Text);
    }
}
