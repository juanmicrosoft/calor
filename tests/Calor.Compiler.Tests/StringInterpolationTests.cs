using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class StringInterpolationTests
{
    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    // --- Lexer tests ---

    [Fact]
    public void Lex_SimpleInterpolatedString()
    {
        var tokens = Tokenize("\"Hello, ${name}!\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("Hello, ${name}!", Assert.IsType<StringLiteralInfo>(tokens[0].Value).Value);
    }

    [Fact]
    public void Lex_MultipleInterpolations()
    {
        var tokens = Tokenize("\"${a} and ${b}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("${a} and ${b}", Assert.IsType<StringLiteralInfo>(tokens[0].Value).Value);
    }

    [Fact]
    public void Lex_NestedExpression()
    {
        var tokens = Tokenize("\"${(+ a b)}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("${(+ a b)}", Assert.IsType<StringLiteralInfo>(tokens[0].Value).Value);
    }

    [Fact]
    public void Lex_FormatSpecifier()
    {
        var tokens = Tokenize("\"${value:F2}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("${value:F2}", Assert.IsType<StringLiteralInfo>(tokens[0].Value).Value);
    }

    [Fact]
    public void Lex_InterpolationWithEscapes()
    {
        var tokens = Tokenize("\"Title: ${title}\\nBody: ${body}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("Title: ${title}\nBody: ${body}", Assert.IsType<StringLiteralInfo>(tokens[0].Value).Value);
    }

    [Fact]
    public void Lex_NestedBracesInInterpolation()
    {
        var tokens = Tokenize("\"${dict{key}}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("${dict{key}}", Assert.IsType<StringLiteralInfo>(tokens[0].Value).Value);
    }

    [Fact]
    public void Lex_EscapedDollarSign()
    {
        var tokens = Tokenize("\"Price: \\${value}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("Price: ${value}", tokens[0].Value);
    }

    // --- Emitter tests ---

    [Fact]
    public void Emit_SimpleInterpolation_ProducesCSharpInterpolatedString()
    {
        var (converted, hasInterpolation) = CSharpEmitter.ConvertInlineInterpolation("Hello, ${name}!");
        Assert.True(hasInterpolation);
        Assert.Equal("Hello, {name}!", converted);
    }

    [Fact]
    public void Emit_PrefixExpr_ProducesCSharpInfix()
    {
        var (converted, hasInterpolation) = CSharpEmitter.ConvertInlineInterpolation("${(+ a b)}");
        Assert.True(hasInterpolation);
        Assert.Equal("{a + b}", converted);
    }

    [Fact]
    public void Emit_DigitHoles_AreRuntimeExpressions()
    {
        var (converted, hasInterpolation) = CSharpEmitter.ConvertInlineInterpolation("${0} and ${1}");
        Assert.True(hasInterpolation);
        Assert.Equal("{0} and {1}", converted);
    }

    // --- Round-trip test ---

    [Fact]
    public void RoundTrip_CSharpInterpolatedString_PreservesSemantics()
    {
        var csharp = @"
public static class Formatting
{
    public static string Greet(string name) => $""Hello, {name}!"";
    public static string Format(int a, int b) => $""{a} + {b} = {a + b}"";
}
";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var conversionResult = converter.Convert(csharp, "test.cs");
        Assert.True(conversionResult.Success,
            $"Conversion failed: {string.Join("; ", conversionResult.Issues.Select(i => i.Message))}");

        var calor = conversionResult.CalorSource!;
        // Verify the Calor source contains ${...} interpolation syntax
        Assert.Contains("${", calor);

        // Compile back to C#
        var compilationResult = Program.Compile(calor, "test.calr",
            new CompilationOptions { EnforceEffects = false });
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");

        var output = compilationResult.GeneratedCode;
        Assert.Contains("$\"Hello, {name}!\"", output);
        Assert.Contains("$\"{a} + {b} = {unchecked(a + b)}\"", output);
        const string probe = """

            public static class Probe
            {
                public static string Run() =>
                    Formatting.Greet("Calor") + "|" +
                    Formatting.Format(int.MaxValue, 1) + "|" +
                    Formatting.Format(-2, 3);
            }
            """;
        const string expected = "Hello, Calor!|2147483647 + 1 = -2147483648|-2 + 3 = 1";
        Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(csharp + probe));
        Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(output + probe));
    }

    [Fact]
    public void RoundTrip_VerbatimInterpolatedString_PreservesSemantics()
    {
        var csharp = @"
public static class MultiLine
{
    public static string Format(string title, string body) => $""Title: {title}\nBody: {body}"";
}
";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var conversionResult = converter.Convert(csharp, "test.cs");
        Assert.True(conversionResult.Success,
            $"Conversion failed: {string.Join("; ", conversionResult.Issues.Select(i => i.Message))}");

        var calor = conversionResult.CalorSource!;
        Assert.Contains("${", calor);

        // Compile back to C#
        var compilationResult = Program.Compile(calor, "test.calr",
            new CompilationOptions { EnforceEffects = false });
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        const string probe = """

            public static class Probe
            {
                public static string Run() =>
                    MultiLine.Format("quote \" and ${0}", "first\nsecond\\third");
            }
            """;
        const string expected = "Title: quote \" and ${0}\nBody: first\nsecond\\third";
        Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(csharp + probe));
        Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(compilationResult.GeneratedCode + probe));
    }

    [Fact]
    public void RoundTrip_FormatSpecifier_PreservesSemantics()
    {
        var csharp = @"
public static class NumberFormat
{
    public static string FormatNumber(double value) => $""Value: {value:F2}"";
}
";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var conversionResult = converter.Convert(csharp, "test.cs");
        Assert.True(conversionResult.Success,
            $"Conversion failed: {string.Join("; ", conversionResult.Issues.Select(i => i.Message))}");

        var calor = conversionResult.CalorSource!;

        // Compile back to C#
        var compilationResult = Program.Compile(calor, "test.calr",
            new CompilationOptions { EnforceEffects = false });
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        const string probe = """

            public static class Probe
            {
                public static string Run() =>
                    NumberFormat.FormatNumber(1.25) + "|" +
                    NumberFormat.FormatNumber(-2.5) + "|" +
                    NumberFormat.FormatNumber(double.NaN) + "|" +
                    NumberFormat.FormatNumber(double.PositiveInfinity);
            }
            """;
        var expected = InteropScope.RefOutImplementationTests.Run(csharp + probe);
        Assert.StartsWith("Value: ", expected);
        Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(compilationResult.GeneratedCode + probe));
    }

    [Theory]
    [InlineData("double", "value")]
    [InlineData("double", "value:F2")]
    [InlineData("double", "value,8")]
    [InlineData("decimal", "value,-10:N2")]
    [InlineData("int", "value")]
    [InlineData("long", "value:D3")]
    [InlineData("uint", "value:X")]
    [InlineData("float", "value")]
    [InlineData("double", "3.5")]
    [InlineData("double", "value + 1")]
    [InlineData("object", "value")]
    [InlineData("T", "value")]
    public void StrictInterpolation_UnprovedFormattingIsUnknown(string type, string hole)
    {
        var source = $$"""
            #nullable enable
            public static class InterpolationBoundary
            {
                public static string Render{{(type == "T" ? "<T>" : "")}}({{type}} value) => $"{{{hole}}}";
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "interpolation-boundary.calr",
                StrictInterpolationOptions(typing));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("interpolation formatting", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("string", "value")]
    [InlineData("string?", "value,8")]
    [InlineData("char", "value,-4")]
    [InlineData("bool", "value")]
    [InlineData("bool", "value,8:ignored")]
    [InlineData("string", "\"text\"")]
    [InlineData("bool", "true")]
    [InlineData("string", "nameof(value)")]
    public void StrictInterpolation_ClosedProviderIndependentHolesRemainSupported(string type, string hole)
    {
        var source = $$"""
            #nullable enable
            public static class InterpolationBoundary
            {
                public static string Render({{type}} value) => $"[{{{hole}}}]";
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "interpolation-safe.calr",
                StrictInterpolationOptions(typing));
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Fact]
    public void StrictInterpolation_FormattingDoesNotEraseHoleExpressionEffects()
    {
        const string source = """
            module InterpolationBoundary
              func Render public () → string
                !
                return "${Console.ReadLine()}"
            """;
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(source, "interpolation-effects.calr", StrictInterpolationOptions(typing));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cr'", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
            result = Program.Compile(source.Replace("${Console.ReadLine()}", "${Console.ReadLine()}|${Console.Read()}",
                StringComparison.Ordinal), "interpolation-effects.calr", StrictInterpolationOptions(typing));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("interpolation formatting", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    private static CompilationOptions StrictInterpolationOptions(bool typing) => new()
    {
        EnableTypeChecking = typing,
        EnforceEffects = true,
        StrictEffects = true,
        UnknownCallPolicy = UnknownCallPolicy.Strict,
        UnsafeTranspileOnly = false,
        VerifyContracts = false,
        StatusWriter = TextWriter.Null
    };
}
