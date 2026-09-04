using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class LiteralRawSemanticsTests : IDisposable
{
    // #1150: generated assemblies go into collectible contexts, unloaded when xUnit
    // disposes this instance — i.e. as soon as the test that made them finishes.
    private readonly CollectibleAssemblyLoader _assemblies = new();

    public void Dispose() => _assemblies.Dispose();

    public static TheoryData<string, string> IntegerBoundaryCorpus => new()
    {
        { "-0x2A", "-0x2A" },
        { "INT:0xFFFF_FFFF", "0xFFFFFFFFL" },
        { "INT:-0x8000_0000", "unchecked((int)0x80000000U)" },
        { "LONG:-0x8000_0000_0000_0000", "unchecked((long)0x8000000000000000UL)" },
        { "0xFFFF_FFFFU", "0xFFFFFFFFU" },
        { "ULONG:18_446_744_073_709_551_615", "18446744073709551615UL" },
        { "LONG:9_223_372_036_854_775_807", "9223372036854775807L" },
        { "UINT:4_294_967_295", "4294967295U" },
    };

    [Theory]
    [MemberData(nameof(IntegerBoundaryCorpus))]
    public void IntegerBoundaryCorpus_EmitsFaithfulCSharp(string source, string expected)
    {
        var expression = ParseExpression(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));

        var literal = Assert.IsType<IntLiteralNode>(expression);
        Assert.Equal(expected, literal.Accept(new CSharpEmitter()));
    }

    [Theory]
    [InlineData("UINT:-1")]
    [InlineData("ULONG:-0x1")]
    [InlineData("-1U")]
    [InlineData("-0x1UL")]
    public void UnsignedNegative_IsDiagnosed(string source)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Lexer(source, diagnostics).TokenizeAll();

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnsignedNegativeLiteral);
    }

    [Theory]
    [InlineData("9223372036854775808L")]
    [InlineData("0x8000000000000000L")]
    [InlineData("-9223372036854775809L")]
    [InlineData("-0x8000000000000001L")]
    [InlineData("LONG:9223372036854775808")]
    public void ExplicitSignedLongOverflow_IsDiagnosedWithoutUnsignedFallback(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAll();

        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.SignedIntegerLiteralOverflow);

        var compilation = Program.Compile(
            $"§M{{m:Overflow}}\n  §F{{f:Value:pub}} () -> object\n    §R {source}",
            "signed-long-overflow.calr",
            new CompilationOptions { EnforceEffects = false });
        Assert.True(compilation.HasErrors);
        Assert.Contains(
            compilation.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.SignedIntegerLiteralOverflow);
        Assert.DoesNotContain("UL", compilation.GeneratedCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedLongMinimumAndUnsignedLong_PreserveRuntimeAndOverloadResolution()
    {
        var min = Assert.IsType<IntLiteralNode>(
            ParseExpression("-9223372036854775808L", out var minDiagnostics));
        var max = Assert.IsType<IntLiteralNode>(
            ParseExpression("9223372036854775807L", out var maxDiagnostics));
        var unsigned = Assert.IsType<IntLiteralNode>(
            ParseExpression("9223372036854775808UL", out var unsignedDiagnostics));
        var maxUnsigned = Assert.IsType<IntLiteralNode>(
            ParseExpression("18446744073709551615UL", out var maxUnsignedDiagnostics));
        Assert.False(minDiagnostics.HasErrors);
        Assert.False(maxDiagnostics.HasErrors);
        Assert.False(unsignedDiagnostics.HasErrors);
        Assert.False(maxUnsignedDiagnostics.HasErrors);

        var minCSharp = min.Accept(new CSharpEmitter());
        var maxCSharp = max.Accept(new CSharpEmitter());
        var unsignedCSharp = unsigned.Accept(new CSharpEmitter());
        var maxUnsignedCSharp = maxUnsigned.Accept(new CSharpEmitter());
        Assert.Equal("-9223372036854775808L", minCSharp);
        Assert.Equal("9223372036854775807L", maxCSharp);
        Assert.Equal("9223372036854775808UL", unsignedCSharp);
        Assert.Equal("18446744073709551615UL", maxUnsignedCSharp);

        var assembly = CompileCSharp($$"""
            public static class LongBoundaryProbe
            {
                public static string Pick(long value) => "long";
                public static string Pick(ulong value) => "ulong";
                public static string MinOverload() => Pick({{minCSharp}});
                public static string MaxOverload() => Pick({{maxCSharp}});
                public static string UnsignedOverload() => Pick({{unsignedCSharp}});
                public static object MinValue() => {{minCSharp}};
                public static object UnsignedValue() => {{unsignedCSharp}};
                public static object MaxUnsignedValue() => {{maxUnsignedCSharp}};
            }
            """);

        Assert.Equal("long", InvokeString(assembly, "LongBoundaryProbe", "MinOverload"));
        Assert.Equal("long", InvokeString(assembly, "LongBoundaryProbe", "MaxOverload"));
        Assert.Equal("ulong", InvokeString(assembly, "LongBoundaryProbe", "UnsignedOverload"));
        Assert.Equal(long.MinValue, InvokeObject(assembly, "LongBoundaryProbe", "MinValue"));
        Assert.Equal(0x8000_0000_0000_0000UL,
            InvokeObject(assembly, "LongBoundaryProbe", "UnsignedValue"));
        Assert.Equal(ulong.MaxValue,
            InvokeObject(assembly, "LongBoundaryProbe", "MaxUnsignedValue"));
    }

    [Theory]
    [InlineData("1_000", 1000L)]
    [InlineData("INT:2_147_483_647", int.MaxValue)]
    [InlineData("INT:-2_147_483_648", int.MinValue)]
    [InlineData("0x7FFF_FFFF", int.MaxValue)]
    public void NumericSeparators_WorkInTypedAndBareIntegers(string source, long expected)
    {
        var literal = Assert.IsType<IntLiteralNode>(ParseExpression(source, out var diagnostics));

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(expected, literal.Value);
    }

    [Theory]
    [InlineData("1_234.5_6", 1234.56)]
    [InlineData("FLOAT:1_234.5_6", 1234.56)]
    [InlineData("DEC:1_234.5_6", 1234.56)]
    public void NumericSeparators_WorkInTypedAndBareFractionalLiterals(
        string source,
        double expected)
    {
        var expression = ParseExpression(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        switch (expression)
        {
            case FloatLiteralNode floating:
                Assert.Equal(expected, floating.Value, precision: 8);
                break;
            case DecimalLiteralNode decimalLiteral:
                Assert.Equal((decimal)expected, decimalLiteral.Value);
                break;
            default:
                Assert.Fail($"Unexpected literal node: {expression.GetType().Name}");
                break;
        }
    }

    [Fact]
    public void StructuralInterpolation_PreservesEscapesBracesPlaceholdersAndClauses()
    {
        const string source = """
            "Cost \${0}: ${name,-10:N2}; braces \{x\}; raw ${value}"
            """;

        var expression = ParseExpression(source, out var diagnostics);
        var interpolated = Assert.IsType<InterpolatedStringNode>(expression);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));

        var csharp = interpolated.Accept(new CSharpEmitter());
        Assert.Equal("$\"Cost ${{0}}: {name,-10:N2}; braces {{x}}; raw {value}\"", csharp);
        Assert.IsType<ReferenceNode>(
            Assert.IsType<InterpolatedStringExpressionNode>(interpolated.Parts[1]).Expression);
    }

    [Fact]
    public void PlaceholderIntent_DistinguishesNegativeExpressionFromLiteralPlaceholder()
    {
        var negative = Assert.IsType<InterpolatedStringNode>(
            ParseExpression("\"${-1}\"", out var negativeDiagnostics));
        var negativePart = Assert.IsType<InterpolatedStringExpressionNode>(
            Assert.Single(negative.Parts));
        Assert.False(negativeDiagnostics.HasErrors);
        Assert.IsType<IntLiteralNode>(negativePart.Expression);
        Assert.Equal(InterpolationPartIntent.Expression, negativePart.Intent);
        var negativeCSharp = negative.Accept(new CSharpEmitter());
        Assert.Equal("$\"{-1}\"", negativeCSharp);
        Assert.Equal("-1", EvaluateStringExpression(negativeCSharp));

        var placeholder = Assert.IsType<InterpolatedStringNode>(
            ParseExpression("\"${0}\"", out var placeholderDiagnostics));
        var placeholderPart = Assert.IsType<InterpolatedStringExpressionNode>(
            Assert.Single(placeholder.Parts));
        Assert.False(placeholderDiagnostics.HasErrors);
        Assert.IsType<IntLiteralNode>(placeholderPart.Expression);
        Assert.Equal(InterpolationPartIntent.LiteralPlaceholder, placeholderPart.Intent);
        var placeholderCSharp = placeholder.Accept(new CSharpEmitter());
        Assert.Equal("\"${0}\"", placeholderCSharp);
        Assert.Equal("${0}", EvaluateStringExpression(placeholderCSharp));

        var escaped = Assert.IsType<StringLiteralNode>(
            ParseExpression("\"\\${-1}\"", out var escapedDiagnostics));
        Assert.False(escapedDiagnostics.HasErrors);
        Assert.Equal("${-1}", escaped.Value);
    }

    [Fact]
    public void MixedExpressionAndLiteralPlaceholder_EmitsIntentNotNodeShape()
    {
        var interpolated = Assert.IsType<InterpolatedStringNode>(
            ParseExpression("\"${name} ${0}\"", out var diagnostics));

        Assert.False(diagnostics.HasErrors);
        var expression = Assert.IsType<InterpolatedStringExpressionNode>(interpolated.Parts[0]);
        var placeholder = Assert.IsType<InterpolatedStringExpressionNode>(interpolated.Parts[2]);
        Assert.Equal(InterpolationPartIntent.Expression, expression.Intent);
        Assert.Equal(InterpolationPartIntent.LiteralPlaceholder, placeholder.Intent);
        Assert.Equal("$\"{name} ${{0}}\"", interpolated.Accept(new CSharpEmitter()));
    }

    [Fact]
    public void InterpolatedUtf8Literal_IsRejectedFailClosed()
    {
        foreach (var literal in new[]
        {
            "\"value=${value}\"u8",
            "\"\"\"value=${value}\"\"\"u8"
        })
        {
            var diagnostics = new DiagnosticBag();
            var tokens = new Lexer(literal, diagnostics).TokenizeAll();

            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Code == DiagnosticCode.InterpolatedUtf8Literal);
            Assert.Equal(TokenKind.Error, tokens[0].Kind);

            var result = Program.Compile(
                $"§M{{m:Utf8}}\n  §F{{f:Value:pub}} (i32:value) -> object\n    §R {literal}",
                "interpolated-u8.calr",
                new CompilationOptions { EnforceEffects = false });
            Assert.True(result.HasErrors);
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code == DiagnosticCode.InterpolatedUtf8Literal);
            Assert.DoesNotContain("$\"", result.GeneratedCode, StringComparison.Ordinal);
        }

        var programmaticNode = new InterpolatedStringNode(
            TextSpan.Empty,
            [
                new InterpolatedStringExpressionNode(
                    TextSpan.Empty,
                    new IntLiteralNode(TextSpan.Empty, 1))
            ])
        {
            IsUtf8 = true
        };
        Assert.Throws<InvalidOperationException>(
            () => programmaticNode.Accept(new CSharpEmitter()));
    }

    [Fact]
    public void CSharpEscapedBraces_RoundTripToSameRuntimeOutput()
    {
        const string original = """
            public static class LiteralShapes
            {
                public static string Format(int value) => $"{{literal}} {value,8:X4}";
            }
            """;
        var originalOutput = InvokeString(
            CompileCSharp(original),
            "LiteralShapes",
            "Format",
            26);

        var conversion = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "BraceRoundTrip"
        }).Convert(original, "brace-roundtrip.cs");
        Assert.True(
            conversion.Success,
            string.Join(Environment.NewLine, conversion.Issues.Select(issue => issue.Message)));
        Assert.Contains("\"{literal} ${value,8:X4}\"", conversion.CalorSource);

        var compilation = Program.Compile(
            conversion.CalorSource!,
            "brace-roundtrip.calr",
            new CompilationOptions { EnforceEffects = false });
        Assert.False(
            compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var roundTripOutput = InvokeString(
            CompileCSharp(compilation.GeneratedCode),
            "LiteralShapes",
            "Format",
            26);

        Assert.Equal("{literal}     001A", originalOutput);
        Assert.Equal(originalOutput, roundTripOutput);
    }

    [Fact]
    public void EscapedInterpolation_RemainsLiteral()
    {
        var expression = ParseExpression("\"Price: \\${value}\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var literal = Assert.IsType<StringLiteralNode>(expression);
        Assert.Equal("Price: ${value}", literal.Value);
        Assert.Equal("\"Price: ${value}\"", literal.Accept(new CSharpEmitter()));
    }

    [Theory]
    [InlineData("\"a\\qz\"", "a\\qz")]
    [InlineData("\"\"\"a\\qz\"\"\"", "a\\qz")]
    public void UnknownEscapes_AreFaithfullyPreserved(string source, string expected)
    {
        var literal = Assert.IsType<StringLiteralNode>(ParseExpression(source, out var diagnostics));

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(expected, literal.Value);
        Assert.Contains("\\\\q", literal.Accept(new CSharpEmitter()));
    }

    [Fact]
    public void TripleInterpolatedString_IsStructuralAndCompiles()
    {
        const string source = "\"\"\"\nvalue=${value:N2}\n\"\"\"";
        var expression = ParseExpression(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        var interpolated = Assert.IsType<InterpolatedStringNode>(expression);
        Assert.True(interpolated.IsMultiline);
        var csharp = interpolated.Accept(new CSharpEmitter());
        Assert.StartsWith("$@\"", csharp, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BinaryOperator.Subtract, BinaryOperator.Subtract, "a - (b - c)")]
    [InlineData(BinaryOperator.Divide, BinaryOperator.Divide, "a / (b / c)")]
    [InlineData(BinaryOperator.LeftShift, BinaryOperator.LeftShift, "a << (b << c)")]
    [InlineData(BinaryOperator.LessThan, BinaryOperator.LessThan, "a < (b < c)")]
    public void EqualPrecedenceRightOperand_IsParenthesized(
        BinaryOperator outer,
        BinaryOperator inner,
        string expected)
    {
        var span = TextSpan.Empty;
        var expression = new BinaryOperationNode(
            span,
            outer,
            new ReferenceNode(span, "a"),
            new BinaryOperationNode(
                span,
                inner,
                new ReferenceNode(span, "b"),
                new ReferenceNode(span, "c")));

        Assert.Equal(expected, expression.Accept(new CSharpEmitter()));
    }

    [Fact]
    public void RawInteropScanner_IgnoresSentinelsInAllCSharpLexicalForms()
    {
        const string payload = """""

            // }§/CSHARP
            /* }§/CSHARP { } */
            var ordinary = "}§/CSHARP";
            var verbatim = @"}§/CSHARP "" {";
            var raw = """}§/CSHARP { }""";
            var interpolated = $"{new { Value = "}§/CSHARP" }}";
            var interpolatedRaw = $$"""{{new { Value = "}§/CSHARP" }}}""";
            var ch = '}';
            if (true) { var nested = new { Value = 1 }; }
            """"";
        var source = $"§CSHARP{{{payload}}}§/CSHARP";
        var diagnostics = new DiagnosticBag();
        var token = Assert.Single(
            new Lexer(source, diagnostics).TokenizeAll().Where(t => t.Kind != TokenKind.Eof));

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(TokenKind.CSharpInterop, token.Kind);
        Assert.Equal(payload, token.Value);
    }

    [Fact]
    public void InlineRawCSharpScanner_TracksLexicalFormsAndNestedBraces()
    {
        const string payload = """""
            new object[]
            {
                "}",
                @"}",
                """}""",
                $"{new { Text = "}" }}",
                $$"""{{new { Text = "}" }}}""",
                '}',
                /* } */ new { Value = 1 },
                // }
                2
            }
            """"";
        var diagnostics = new DiagnosticBag();
        var token = Assert.Single(
            new Lexer($"§CS{{{payload}}}", diagnostics)
                .TokenizeAll()
                .Where(t => t.Kind != TokenKind.Eof));

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(TokenKind.RawCSharpExpression, token.Kind);
        Assert.Equal(payload, token.Value);
    }

    [Fact]
    public void RawAndInteropContents_RoundTripByteForByte()
    {
        const string rawPayload = """
            var x = "§/RAW";
            // §/RAW
            #if MAYBE
            var conditional = "§/RAW";
            #endif
            """;
        var diagnostics = new DiagnosticBag();
        var rawToken = Assert.Single(
            new Lexer($"§RAW\n{rawPayload}§/RAW", diagnostics)
                .TokenizeAll()
                .Where(t => t.Kind != TokenKind.Eof));
        Assert.Equal(rawPayload, rawToken.Value);

        const string interopPayload = "\r\npublic int X => 1;\r\n";
        var interopToken = Assert.Single(
            new Lexer($"§CSHARP{{{interopPayload}}}§/CSHARP", diagnostics)
                .TokenizeAll()
                .Where(t => t.Kind != TokenKind.Eof));
        Assert.Equal(interopPayload, interopToken.Value);
    }

    [Fact]
    public void RawPreprocessorDirectiveComments_DoNotCloseBlockAndPreserveBytes()
    {
        const string payload =
            "#if FIRST // §/RAW\r\n"
            + "#elif SECOND /* §/RAW */\r\n"
            + "#else // another §/RAW\r\n"
            + "#endif /* final §/RAW */\r\n"
            + "var after = 1;\r\n";
        var diagnostics = new DiagnosticBag();
        var token = Assert.Single(
            new Lexer($"§RAW\r\n{payload}§/RAW", diagnostics)
                .TokenizeAll()
                .Where(candidate => candidate.Kind != TokenKind.Eof));

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(TokenKind.RawCSharp, token.Kind);
        Assert.Equal(payload, token.Value);
    }

    [Fact]
    public void RawDirectiveLines_LexStringsCharsAndInterpolationsBeforeSentinels()
    {
        const string payload =
            "#line 1 \"§/RAW\"\r\n"
            + "#pragma payload @\"§/RAW\"\r\n"
            + "#pragma payload \"\"\"§/RAW\"\"\"\r\n"
            + "#pragma payload $\"{\"§/RAW\"}\"\r\n"
            + "#pragma payload $$\"\"\"{{\"§/RAW\"}}\"\"\"\r\n"
            + "#pragma payload '§'/RAW\r\n";
        var diagnostics = new DiagnosticBag();
        var token = Assert.Single(
            new Lexer($"§RAW\r\n{payload}§/RAW", diagnostics)
                .TokenizeAll()
                .Where(candidate => candidate.Kind != TokenKind.Eof));

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(TokenKind.RawCSharp, token.Kind);
        Assert.Equal(payload, token.Value);
    }

    [Fact]
    public void RawBlockComments_ConsumeDirectivesAndSentinelsUntilClosingComment()
    {
        const string payload =
            "#if OUTER\r\n"
            + "/*\r\n"
            + "#if INNER\r\n"
            + "#elif OTHER // §/RAW\r\n"
            + "#else\r\n"
            + "#endif\r\n"
            + "§/RAW\r\n"
            + "\"§/RAW\"\r\n"
            + "*/\r\n"
            + "#elif REAL\r\n"
            + "var branch = 1;\r\n"
            + "#else\r\n"
            + "var branch = 2;\r\n"
            + "#endif\r\n";
        var diagnostics = new DiagnosticBag();
        var token = Assert.Single(
            new Lexer($"§RAW\r\n{payload}§/RAW", diagnostics)
                .TokenizeAll()
                .Where(candidate => candidate.Kind != TokenKind.Eof));

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(TokenKind.RawCSharp, token.Kind);
        Assert.Equal(payload, token.Value);
    }

    [Fact]
    public void RawDisabledRegions_IgnoreMalformedLexicalTextAndResumeNestedActiveBranch()
    {
        foreach (var malformed in new[]
        {
            "var brokenString = \"unterminated",
            "var brokenVerbatim = @\"unterminated",
            "var brokenRaw = \"\"\"unterminated",
            "var brokenInterpolation = $\"{unterminated",
            "/* unterminated disabled comment"
        })
        {
            var payload =
                "#define ENABLED\r\n"
                + "#if false\r\n"
                + malformed + "\r\n"
                + "#if true\r\n"
                + "var nestedDisabled = 0;\r\n"
                + "#endif\r\n"
                + "#elif defined(ENABLED) && true\r\n"
                + "var active = 1;\r\n"
                + "#else\r\n"
                + "var inactive = 2;\r\n"
                + "#endif\r\n";
            var diagnostics = new DiagnosticBag();
            var token = Assert.Single(
                new Lexer($"§RAW\r\n{payload}§/RAW", diagnostics)
                    .TokenizeAll()
                    .Where(candidate => candidate.Kind != TokenKind.Eof));

            Assert.False(
                diagnostics.HasErrors,
                string.Join(Environment.NewLine, diagnostics.Errors));
            Assert.Equal(TokenKind.RawCSharp, token.Kind);
            Assert.Equal(payload, token.Value);
        }
    }

    [Fact]
    public void RawDisabledValidRawString_HidesDirectiveAndDelimiterText()
    {
        const string payload =
            "#if false\r\n"
            + "var text = \"\"\"\r\n"
            + "#endif\r\n"
            + "§/RAW\r\n"
            + "\"\"\";\r\n"
            + "#else\r\n"
            + "var active = 1;\r\n"
            + "#endif\r\n";
        var diagnostics = new DiagnosticBag();
        var token = Assert.Single(
            new Lexer($"§RAW\r\n{payload}§/RAW", diagnostics)
                .TokenizeAll()
                .Where(candidate => candidate.Kind != TokenKind.Eof));

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(TokenKind.RawCSharp, token.Kind);
        Assert.Equal(payload, token.Value);
    }

    [Fact]
    public void RawDisabledValidLexicalConstructs_MakeInternalDirectivesInert()
    {
        const string payload =
            "#if false\r\n"
            + "var ordinary = \"#endif §/RAW\";\r\n"
            + "var verbatim = @\"\r\n#endif\r\n§/RAW\r\n\";\r\n"
            + "var interpolated = $@\"\r\n#endif\r\n{1}\r\n§/RAW\r\n\";\r\n"
            + "var interpolatedRaw = $$\"\"\"\r\n#endif\r\n{{1}}\r\n§/RAW\r\n\"\"\";\r\n"
            + "var character = '§'; // #endif §/RAW\r\n"
            + "/*\r\n#endif\r\n§/RAW\r\n*/\r\n"
            + "// #endif §/RAW\r\n"
            + "#if false\r\n"
            + "var nested = \"\"\"\r\n#endif\r\n§/RAW\r\n\"\"\";\r\n"
            + "#endif\r\n"
            + "#else\r\n"
            + "var active = 1;\r\n"
            + "#endif\r\n";
        var diagnostics = new DiagnosticBag();
        var token = Assert.Single(
            new Lexer($"§RAW\r\n{payload}§/RAW", diagnostics)
                .TokenizeAll()
                .Where(candidate => candidate.Kind != TokenKind.Eof));

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(TokenKind.RawCSharp, token.Kind);
        Assert.Equal(payload, token.Value);
    }

    [Fact]
    public void RawActiveOrUnknownMalformedLexicalText_RemainsUnterminated()
    {
        foreach (var condition in new[] { "true", "UNKNOWN_SYMBOL" })
        {
            foreach (var malformed in new[]
            {
                "/* unterminated active comment",
                "var broken = \"unterminated active string"
            })
            {
                var source =
                    $"§RAW\n#if {condition}\n{malformed}\n"
                    + "#else\nvar fallback = 1;\n#endif\n§/RAW";
                var diagnostics = new DiagnosticBag();
                var tokens = new Lexer(source, diagnostics).TokenizeAll();

                Assert.Equal(TokenKind.Error, tokens[0].Kind);
                Assert.Contains(
                    diagnostics,
                    diagnostic => diagnostic.Code == DiagnosticCode.UnterminatedRawBlock);
            }
        }

        const string activeElse =
            "§RAW\n#if false\nvar disabled = \"unterminated\n"
            + "#else\n/* unterminated active else\n#endif\n§/RAW";
        var elseDiagnostics = new DiagnosticBag();
        var elseTokens = new Lexer(activeElse, elseDiagnostics).TokenizeAll();
        Assert.Equal(TokenKind.Error, elseTokens[0].Kind);
        Assert.Contains(
            elseDiagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnterminatedRawBlock);

        const string unknownUnclosed =
            "§RAW\n#if UNKNOWN_SYMBOL\nvar value = 1;\n§/RAW";
        var unknownDiagnostics = new DiagnosticBag();
        var unknownTokens = new Lexer(unknownUnclosed, unknownDiagnostics).TokenizeAll();
        Assert.Equal(TokenKind.Error, unknownTokens[0].Kind);
        Assert.Contains(
            unknownDiagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnterminatedRawBlock);
    }

    [Fact]
    public void CommittedSeedCorpus_LexEmitRoslynRuntimeRoundTrips()
    {
        var seedPath = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "LiteralRawSemantics",
            "integer-seeds.txt");
        var seeds = File.ReadAllLines(seedPath)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('|'))
            .ToList();
        Assert.NotEmpty(seeds);

        foreach (var seed in seeds)
        {
            var literal = Assert.IsType<IntLiteralNode>(
                ParseExpression(seed[0], out var diagnostics));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));

            var emitted = literal.Accept(new CSharpEmitter());
            var expected = decimal.Parse(seed[1], System.Globalization.CultureInfo.InvariantCulture);
            var runtime = EvaluateInteger(emitted);
            Assert.Equal(expected, runtime);
        }
    }

    [Fact]
    public void CommittedFuzzSeeds_GenerateRoslynRuntimeEquivalentLiterals()
    {
        var seedPath = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "LiteralRawSemantics",
            "fuzz-seeds.txt");
        var seeds = File.ReadAllLines(seedPath)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(int.Parse)
            .ToList();
        Assert.NotEmpty(seeds);

        foreach (var seed in seeds)
        {
            var random = new Random(seed);
            for (var iteration = 0; iteration < 32; iteration++)
            {
                var magnitude = (ulong)random.NextInt64();
                var negative = random.Next(2) == 0;
                var source = $"LONG:{(negative ? "-" : "")}0x{magnitude:X}";
                var expected = negative ? -(decimal)magnitude : magnitude;
                var literal = Assert.IsType<IntLiteralNode>(
                    ParseExpression(source, out var diagnostics));

                Assert.False(
                    diagnostics.HasErrors,
                    $"seed={seed}, iteration={iteration}: "
                    + string.Join(Environment.NewLine, diagnostics.Errors));
                Assert.Equal(expected, EvaluateInteger(literal.Accept(new CSharpEmitter())));
            }
        }
    }

    private static ExpressionNode ParseExpression(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var parser = new Parser(
            new Lexer($"§M{{m:Test}}\n  §F{{f:Value:pub}} () -> object\n    §R {source}", diagnostics)
                .TokenizeAllForParser(),
            diagnostics);
        var module = parser.Parse();
        var function = Assert.Single(module.Functions);
        return Assert.IsAssignableFrom<ExpressionNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body)).Expression);
    }

    // #1150: instance, because the assembly loads into this instance's context.
    private decimal EvaluateInteger(string emitted)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            $"public static class LiteralProbe {{ public static object Value() => {emitted}; }}");
        var name = $"LiteralProbe_{Guid.NewGuid():N}";
        var compilation = CSharpCompilation.Create(
            name,
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        var assembly = _assemblies.Load(stream.ToArray(), name);
        var value = assembly.GetType("LiteralProbe")!.GetMethod("Value")!.Invoke(null, null)!;
        return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // #1150: instance, because CompileCSharp loads into this instance's context.
    private string EvaluateStringExpression(string expression)
        => InvokeString(
            CompileCSharp(
                $"public static class StringProbe {{ public static string Value() => {expression}; }}"),
            "StringProbe",
            "Value");

    private Assembly CompileCSharp(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + source);
        var name = $"LiteralRawProbe_{Guid.NewGuid():N}";
        var compilation = CSharpCompilation.Create(
            name,
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return _assemblies.Load(stream.ToArray(), name);
    }

    private static string InvokeString(
        Assembly assembly,
        string typeName,
        string methodName,
        params object?[] arguments)
    {
        var type = Assert.Single(assembly.GetTypes(), type => type.Name == typeName);
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, arguments));
    }

    private static object InvokeObject(
        Assembly assembly,
        string typeName,
        string methodName,
        params object?[] arguments)
    {
        var type = Assert.Single(assembly.GetTypes(), type => type.Name == typeName);
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var value = method.Invoke(null, arguments);
        Assert.NotNull(value);
        return value;
    }
}
