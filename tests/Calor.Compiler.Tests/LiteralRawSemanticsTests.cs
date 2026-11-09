using System.Reflection;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class LiteralRawSemanticsTests
{
    [Theory]
    [InlineData("String.Concat(\"\\x41\")", "System.String.Concat(\"\\u0041\")", "A")]
    [InlineData("String.Concat(\"${x}\")", "System.String.Concat(\"\\${x}\")", "${x}")]
    [InlineData("String.Concat(\"\\x41\"),4", "System.String.Concat(\"\\u0041\"),4", "   A")]
    [InlineData("String.Concat(\"${x}\"),8", "System.String.Concat(\"\\${x}\"),8", "    ${x}")]
    [InlineData("String.Concat(String.Concat(\"\\x41\"))", "System.String.Concat(System.String.Concat(\"\\u0041\"))", "A")]
    [InlineData("String.Concat(\"${x}\").Length", "System.String.Concat(\"\\${x}\").Length", "4")]
    public void NativeInterpolation_SourceContextTranslationPreservesOriginalCSharpValue(
        string csharpHole, string nativeHole, string expected)
    {
        var original = CompileCSharp("public static class OriginalLiteralContext { "
            + "public static string Probe(int x) => $\"{" + csharpHole + "}\"; }");
        Assert.Equal(expected, InvokeString(original, "OriginalLiteralContext", "Probe", 7));

        var source = "module NativeLiteralContext\n"
            + "  func Probe public (int:x) \u2192 string\n"
            + "    return \"${" + nativeHole + "}\"\n";
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var interpolation = Assert.IsType<InterpolatedStringNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions).Body[0]).Expression);
        Assert.IsNotType<RawCSharpExpressionNode>(
            Assert.IsType<InterpolatedStringExpressionNode>(Assert.Single(interpolation.Parts)).Expression);
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(module) })
        {
            var compiled = Program.Compile(candidate, "literal-context.calr.txt",
                new CompilationOptions { EnforceEffects = true });
            Assert.False(compiled.HasErrors, candidate + "\n" + string.Join("\n", compiled.Diagnostics));
            var assembly = CompileCSharp(compiled.GeneratedCode);
            var method = Assert.Single(assembly.GetTypes().SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)),
                method => method.Name == "Probe");
            Assert.Equal(expected, method.Invoke(null, [7]));
        }
    }

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
        var current = source switch
        {
            "INT:0xFFFF_FFFF" => "0xFFFF_FFFFL",
            "INT:-0x8000_0000" => "-2147483648",
            "LONG:-0x8000_0000_0000_0000" => "-0x8000_0000_0000_0000L",
            "ULONG:18_446_744_073_709_551_615" => "18_446_744_073_709_551_615UL",
            "LONG:9_223_372_036_854_775_807" => "9_223_372_036_854_775_807L",
            "UINT:4_294_967_295" => "4_294_967_295U",
            _ => source
        };
        if (current != source)
            AssertRetiredTypedLiteral(source);
        var expression = ParseExpression(current, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));

        var literal = Assert.IsType<IntLiteralNode>(expression);
        var emitted = literal.Accept(new CSharpEmitter());
        Assert.Equal(source == "INT:-0x8000_0000" ? "-2147483648" : expected, emitted);
        var originalValue = EvaluateLiteralObject(expected);
        var currentValue = EvaluateLiteralObject(emitted);
        Assert.Equal(originalValue.GetType(), currentValue.GetType());
        Assert.Equal(originalValue, currentValue);
    }

    [Theory]
    [InlineData("UINT:-1")]
    [InlineData("ULONG:-0x1")]
    [InlineData("-1U")]
    [InlineData("-0x1UL")]
    public void UnsignedNegative_IsDiagnosed(string source)
    {
        var current = source switch
        {
            "UINT:-1" => "-1U",
            "ULONG:-0x1" => "-0x1UL",
            _ => source
        };
        if (current != source)
            AssertRetiredTypedLiteral(source);
        var diagnostics = new DiagnosticBag();
        var program = $"module Unsigned\n  func Value public () \u2192 object\n    return {current}";
        _ = new Parser(new Lexer(program, diagnostics).TokenizeAllForParser(), diagnostics).Parse();

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnsignedNegativeLiteral);
        Assert.True(Program.Compile(program).HasErrors);
    }

    [Theory]
    [InlineData("9223372036854775808L")]
    [InlineData("0x8000000000000000L")]
    [InlineData("-9223372036854775809L")]
    [InlineData("-0x8000000000000001L")]
    [InlineData("LONG:9223372036854775808")]
    public void ExplicitSignedLongOverflow_IsDiagnosedWithoutUnsignedFallback(string source)
    {
        var current = source == "LONG:9223372036854775808" ? "9223372036854775808L" : source;
        if (current != source)
            AssertRetiredTypedLiteral(source);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(current, diagnostics).TokenizeAll();
        Assert.Contains(tokens, token => token.Kind == TokenKind.Error);
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.SignedIntegerLiteralOverflow);

        var compilation = Program.Compile(
            $"module Overflow\n  func Value public () \u2192 long\n    return {current}",
            "signed-long-overflow.calr",
            new CompilationOptions { EnforceEffects = true });
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
        var current = source switch
        {
            "INT:2_147_483_647" => "2_147_483_647",
            "INT:-2_147_483_648" => "-2_147_483_648",
            _ => source
        };
        if (current != source)
            AssertRetiredTypedLiteral(source);
        var literal = Assert.IsType<IntLiteralNode>(ParseExpression(current, out var diagnostics));

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(expected, literal.Value);
        Assert.Equal(IntegerLiteralWidth.Bits32, literal.Width);
        Assert.Equal(IntegerLiteralSignedness.Signed, literal.Signedness);
    }

    [Theory]
    [InlineData("1_234.5_6", 1234.56)]
    [InlineData("FLOAT:1_234.5_6", 1234.56)]
    [InlineData("DEC:1_234.5_6", 1234.56)]
    public void NumericSeparators_WorkInTypedAndBareFractionalLiterals(
        string source,
        double expected)
    {
        var current = source switch
        {
            "FLOAT:1_234.5_6" => "1_234.5_6d",
            "DEC:1_234.5_6" => "1_234.5_6m",
            _ => source
        };
        if (current != source)
            AssertRetiredTypedLiteral(source);
        var expression = ParseExpression(current, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        switch (expression)
        {
            case FloatLiteralNode floating:
                Assert.False(floating.IsSingle);
                Assert.False(source.StartsWith("DEC:", StringComparison.Ordinal));
                Assert.Equal(expected, floating.Value, precision: 8);
                break;
            case DecimalLiteralNode decimalLiteral:
                Assert.StartsWith("DEC:", source);
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
    public void NumericInterpolation_IsRuntimeUnlessEscaped()
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
        Assert.Equal(InterpolationPartIntent.Expression, placeholderPart.Intent);
        var placeholderCSharp = placeholder.Accept(new CSharpEmitter());
        Assert.Equal("$\"{0}\"", placeholderCSharp);
        Assert.Equal("0", EvaluateStringExpression(placeholderCSharp));

        var escaped = Assert.IsType<StringLiteralNode>(
            ParseExpression("\"\\${-1}\"", out var escapedDiagnostics));
        Assert.False(escapedDiagnostics.HasErrors);
        Assert.Equal("${-1}", escaped.Value);
    }

    [Fact]
    public void MixedVariableAndDigitHoles_AreRuntimeExpressions()
    {
        var interpolated = Assert.IsType<InterpolatedStringNode>(
            ParseExpression("\"${name} ${0}\"", out var diagnostics));

        Assert.False(diagnostics.HasErrors);
        var expression = Assert.IsType<InterpolatedStringExpressionNode>(interpolated.Parts[0]);
        var placeholder = Assert.IsType<InterpolatedStringExpressionNode>(interpolated.Parts[2]);
        Assert.Equal(InterpolationPartIntent.Expression, expression.Intent);
        Assert.Equal(InterpolationPartIntent.Expression, placeholder.Intent);
        Assert.Equal("$\"{name} {0}\"", interpolated.Accept(new CSharpEmitter()));
    }

    [Theory]
    [InlineData("\"${1}\"", "1")]
    [InlineData("\"${1,4:D2}\"", "  01")]
    [InlineData("\"${1,-4:D2}\"", "01  ")]
    [InlineData("\"\\${0}\"", "${0}")]
    [InlineData("\"\\${1,4:D2}\"", "${1,4:D2}")]
    [InlineData("\"${(+ 1 2)} lit=\\${0}\"", "3 lit=${0}")]
    [InlineData("\"${1} ${-1} ${1.5f:F1}\"", "1 -1 1.5")]
    [InlineData("\"\"\"${1,4:D2} \\${0}\"\"\"", "  01 ${0}")]
    public void MeasuredInterpolation_RoundTripsRuntimeAndLiteralHoles(string source, string expected)
    {
        var current = source == "\"${(+ 1 2)} lit=\\${0}\""
            ? "\"${1 + 2} lit=\\${0}\"" : source;
        var expression = ParseExpression(current, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(expected, EvaluateStringExpression(expression.Accept(new CSharpEmitter())));
        var emitted = expression.Accept(new CalorEmitter());
        Assert.DoesNotContain("INT:", emitted);
        Assert.DoesNotContain("SINGLE:", emitted);
        var reparsed = ParseExpression(emitted, out var replayDiagnostics);
        Assert.False(replayDiagnostics.HasErrors, string.Join("\n", replayDiagnostics.Errors));
        Assert.Equal(expected, EvaluateStringExpression(reparsed.Accept(new CSharpEmitter())));
        Assert.Equal(emitted, reparsed.Accept(new CalorEmitter()));
    }

    [Theory]
    [InlineData("-0x8000000000000000L")]
    [InlineData("18446744073709551615UL")]
    [InlineData("4294967295U")]
    [InlineData("-9223372036854775808L")]
    [InlineData("9223372036854775807L")]
    [InlineData("-0f")]
    [InlineData("1.4e-45f")]
    [InlineData("1.2345678901234567890123456789m")]
    [InlineData("79228162514264337593543950335m")]
    [InlineData("1e-28m")]
    [InlineData("-0.0000m")]
    [InlineData("0.0000m")]
    [InlineData("-0.0")]
    public void BareLiteralEmission_PreservesWidthValueAndSign(string source)
    {
        var expression = ParseExpression(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        var emitted = expression.Accept(new CalorEmitter());
        Assert.DoesNotContain(":", emitted);
        var replay = ParseExpression(emitted, out var replayDiagnostics);
        Assert.False(replayDiagnostics.HasErrors, string.Join("\n", replayDiagnostics.Errors));
        Assert.Equal(expression.Accept(new CSharpEmitter()), replay.Accept(new CSharpEmitter()));
        Assert.Equal(emitted, replay.Accept(new CalorEmitter()));
        if (expression is DecimalLiteralNode originalLiteral)
        {
            var replayDecimal = Assert.IsType<DecimalLiteralNode>(replay);
            Assert.Equal(decimal.GetBits(originalLiteral.Value), decimal.GetBits(replayDecimal.Value));
        }
        var originalValue = EvaluateLiteralObject(source);
        var runtimeValue = EvaluateLiteralObject(replay.Accept(new CSharpEmitter()));
        Assert.Equal(originalValue.GetType(), runtimeValue.GetType());
        if (originalValue is decimal originalDecimal)
        {
            Assert.Equal(decimal.GetBits(originalDecimal),
                decimal.GetBits(Assert.IsType<decimal>(runtimeValue)));
        }
        else if (originalValue is float originalSingle)
            Assert.Equal(BitConverter.SingleToInt32Bits(originalSingle),
                BitConverter.SingleToInt32Bits(Assert.IsType<float>(runtimeValue)));
        else if (originalValue is double originalDouble)
            Assert.Equal(BitConverter.DoubleToInt64Bits(originalDouble),
                BitConverter.DoubleToInt64Bits(Assert.IsType<double>(runtimeValue)));
        else
            Assert.Equal(originalValue, runtimeValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiteralStringEmission_PreservesEscapesAndLiteralDigitHoles(bool multiline)
    {
        const string value = "${0}\r\n\"\\\0\a\b\f\t\v\u0001\uD800";
        var literal = new StringLiteralNode(default, value) { IsMultiline = multiline };
        var emitted = literal.Accept(new CalorEmitter());
        var replay = Assert.IsType<StringLiteralNode>(ParseExpression(emitted, out var diagnostics));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(value, replay.Value);
        Assert.Equal(value, EvaluateStringExpression(replay.Accept(new CSharpEmitter())));
        Assert.Equal(emitted, replay.Accept(new CalorEmitter()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0,4:D2")]
    public void ProgrammaticLiteralPlaceholder_EmitsEscapedText(string? sourceText)
    {
        var part = new InterpolatedStringExpressionNode(
            default, new IntLiteralNode(default, 0), formatSpecifier: "D2",
            alignmentClause: "4", intent: InterpolationPartIntent.LiteralPlaceholder,
            sourceText: sourceText);
        var expression = new InterpolatedStringNode(default, [part]);
        var emitted = expression.Accept(new CalorEmitter());
        Assert.Equal("\"\\${0,4:D2}\"", emitted);
        var replay = Assert.IsType<StringLiteralNode>(ParseExpression(emitted, out var diagnostics));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal("${0,4:D2}", replay.Value);
    }

    [Theory]
    [InlineData("1e309")]
    [InlineData("-1e309")]
    [InlineData("3.5e38f")]
    [InlineData("79228162514264337593543950336m")]
    [InlineData("18446744073709551616UL")]
    [InlineData("9223372036854775808L")]
    [InlineData("\"${0\"")]
    [InlineData("\"\"\"${0\"\"\"")]
    public void InvalidMeasuredLiteral_IsRejected(string source)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Lexer(source, diagnostics).TokenizeAll();
        Assert.True(diagnostics.HasErrors);
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
                $"module Utf8\n  func Value public (int:value) \u2192 object\n    return {literal}",
                "interpolated-u8.calr",
                new CompilationOptions { EnforceEffects = true });
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
    [InlineData(BinaryOperator.Subtract, BinaryOperator.Subtract, "checked(a - (checked(b - c)))")]
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

    // Keep historical case identities and payloads; the selected raw boundary is
    // byte-counted, not a C# lexical scanner. These are opaque-byte controls.
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
        AssertCountedRawPayload(payload, "member");
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
        // Inline raw expressions remain unadmitted. A counted statement preserves
        // these bytes, but does not restore the retired expression channel.
        AssertRetiredRawSyntax($"§CS{{{payload}}}");
        AssertCountedRawPayload(payload);
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
        AssertCountedRawPayload(rawPayload);

        const string interopPayload = "\r\npublic int X => 1;\r\n";
        AssertCountedRawPayload(interopPayload, "member", "\r\n");
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
        AssertCountedRawPayload(payload, lineEnding: "\r\n");
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
        AssertCountedRawPayload(payload, lineEnding: "\r\n");
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
        AssertCountedRawPayload(payload, lineEnding: "\r\n");
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
            AssertCountedRawPayload(payload, lineEnding: "\r\n");
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
        AssertCountedRawPayload(payload, lineEnding: "\r\n");
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
        AssertCountedRawPayload(payload, lineEnding: "\r\n");
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
                var payload = $"#if {condition}\n{malformed}\n"
                    + "#else\nvar fallback = 1;\n#endif\n";
                AssertRetiredRawSyntax($"§RAW\n{payload}§/RAW");
                AssertCountedRawPayload(payload);
            }
        }

        // C# lexical validity no longer determines the raw boundary: only the
        // byte count does. Unterminated C# stays opaque, not native or validated.
        const string activeElse =
            "#if false\nvar disabled = \"unterminated\n"
            + "#else\n/* unterminated active else\n#endif\n";
        AssertRetiredRawSyntax($"§RAW\n{activeElse}§/RAW");
        AssertCountedRawPayload(activeElse);

        const string unknownUnclosed = "#if UNKNOWN_SYMBOL\nvar value = 1;\n";
        AssertRetiredRawSyntax($"§RAW\n{unknownUnclosed}§/RAW");
        AssertCountedRawPayload(unknownUnclosed);
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
        Assert.Equal(12, seeds.Count);
        using var currentInputs = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(seedPath)!, "integer-current-inputs.json")));
        var inputs = currentInputs.RootElement.EnumerateArray().ToArray();
        Assert.Equal(seeds.Select(seed => seed[0]),
            inputs.Select(input => input.GetProperty("original").GetString()));

        foreach (var (seed, input) in seeds.Zip(inputs))
        {
            var current = input.GetProperty("current").GetString()!;
            if (current != seed[0])
                AssertRetiredTypedLiteral(seed[0]);
            var literal = Assert.IsType<IntLiteralNode>(
                ParseExpression(current, out var diagnostics));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));

            var emitted = literal.Accept(new CSharpEmitter());
            var expected = decimal.Parse(seed[1], System.Globalization.CultureInfo.InvariantCulture);
            var runtime = EvaluateInteger(emitted);
            Assert.Equal(expected, runtime);
            Assert.Equal(input.GetProperty("runtimeType").GetString(),
                EvaluateLiteralObject(emitted).GetType().FullName);
            var replay = ParseExpression(literal.Accept(new CalorEmitter()), out var replayDiagnostics);
            Assert.False(replayDiagnostics.HasErrors, string.Join("\n", replayDiagnostics));
            Assert.Equal(emitted, replay.Accept(new CSharpEmitter()));
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
        Assert.Equal(3, seeds.Count);

        foreach (var seed in seeds)
        {
            var random = new Random(seed);
            for (var iteration = 0; iteration < 32; iteration++)
            {
                var magnitude = (ulong)random.NextInt64();
                var negative = random.Next(2) == 0;
                var original = $"LONG:{(negative ? "-" : "")}0x{magnitude:X}";
                AssertRetiredTypedLiteral(original);
                var source = $"{(negative ? "-" : "")}0x{magnitude:X}L";
                var expected = negative ? -(decimal)magnitude : magnitude;
                var literal = Assert.IsType<IntLiteralNode>(
                    ParseExpression(source, out var diagnostics));

                Assert.False(
                    diagnostics.HasErrors,
                    $"seed={seed}, iteration={iteration}: "
                    + string.Join(Environment.NewLine, diagnostics.Errors));
                Assert.Equal(IntegerLiteralWidth.Bits64, literal.Width);
                Assert.Equal(IntegerLiteralSignedness.Signed, literal.Signedness);
                Assert.Equal(magnitude, literal.Magnitude);
                Assert.Equal(negative ? IntegerLiteralSign.Negative : IntegerLiteralSign.Positive, literal.Sign);
                Assert.Equal(expected, EvaluateInteger(literal.Accept(new CSharpEmitter())));
            }
        }
    }

    private static ExpressionNode ParseExpression(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var parser = new Parser(
            new Lexer($"module Test\n  func Value public () \u2192 object\n    return {source}", diagnostics)
                .TokenizeAllForParser(),
            diagnostics);
        var module = parser.Parse();
        var function = Assert.Single(module.Functions);
        return Assert.IsAssignableFrom<ExpressionNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body)).Expression);
    }

    private static decimal EvaluateInteger(string emitted)
        => Convert.ToDecimal(EvaluateLiteralObject(emitted), System.Globalization.CultureInfo.InvariantCulture);

    private static object EvaluateLiteralObject(string emitted)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            $"public static class LiteralProbe {{ public static object Value() => {emitted}; }}");
        var compilation = CSharpCompilation.Create(
            $"LiteralProbe_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var value = assembly.GetType("LiteralProbe")!.GetMethod("Value")!.Invoke(null, null)!;
        return value;
    }

    private static void AssertRetiredTypedLiteral(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAll();
        Assert.Contains(tokens, token => token.Kind == TokenKind.Error);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidTypedLiteral);
    }

    private static void AssertRetiredRawSyntax(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAll();
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownSectionMarker);
        Assert.DoesNotContain(tokens, token => token.Kind == TokenKind.RawCSharpExpression);
    }

    private static void AssertCountedRawPayload(string payload, string reason = "statement", string lineEnding = "\n")
    {
        var utf8 = new UTF8Encoding(false, true);
        var count = utf8.GetByteCount(payload);
        var source = $"raw {reason} {count}{lineEnding}{payload}";
        var diagnostics = new DiagnosticBag();
        var token = Assert.Single(new Lexer(source, diagnostics).TokenizeAll()
            .Where(token => token.Kind != TokenKind.Eof));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal(reason == "statement" ? TokenKind.RawCSharp : TokenKind.CSharpInterop, token.Kind);
        var frame = Assert.IsType<RawCSharpFrame>(token.Value);
        Assert.Equal(payload, frame.Code);
        Assert.Equal(utf8.GetBytes(payload), utf8.GetBytes(frame.Code));
        Assert.Equal(source.Length, token.Span.End);
        Assert.False(frame.IsCompilerDirective);

        var module = new Parser(new Lexer(
            $"module RawPayload\n  func Value () \u2192 void\n    {source}", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var statement = Assert.IsType<RawCSharpNode>(Assert.Single(Assert.Single(module.Functions).Body));
        Assert.Equal(payload, statement.CSharpCode);
        var emitted = new CalorEmitter().Emit(module);
        var replay = Assert.Single(RawCSharpFrame.ReadTokens(emitted)
            .Where(token => token.Value is RawCSharpFrame));
        Assert.Equal(frame, Assert.IsType<RawCSharpFrame>(replay.Value));

        var truncatedDiagnostics = new DiagnosticBag();
        var truncated = new Lexer($"raw {reason} {count + 1}{lineEnding}{payload}",
            truncatedDiagnostics).TokenizeAll();
        Assert.Equal(TokenKind.Error, truncated[0].Kind);
        Assert.Contains(truncatedDiagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidRawFrame);
    }

    private static string EvaluateStringExpression(string expression)
        => InvokeString(
            CompileCSharp(
                $"public static class StringProbe {{ public static string Value() => {expression}; }}"),
            "StringProbe",
            "Value");

    private static Assembly CompileCSharp(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + source);
        var compilation = CSharpCompilation.Create(
            $"LiteralRawProbe_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
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
