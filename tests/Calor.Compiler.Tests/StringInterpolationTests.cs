using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
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
        Assert.Equal("Hello, ${name}!", tokens[0].Value);
    }

    [Fact]
    public void Lex_MultipleInterpolations()
    {
        var tokens = Tokenize("\"${a} and ${b}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("${a} and ${b}", tokens[0].Value);
    }

    [Fact]
    public void Lex_NestedExpression()
    {
        var tokens = Tokenize("\"${(+ a b)}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("${(+ a b)}", tokens[0].Value);
    }

    [Fact]
    public void Lex_FormatSpecifier()
    {
        var tokens = Tokenize("\"${value:F2}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("${value:F2}", tokens[0].Value);
    }

    [Fact]
    public void Lex_InterpolationWithEscapes()
    {
        var tokens = Tokenize("\"Title: ${title}\\nBody: ${body}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("Title: ${title}\nBody: ${body}", tokens[0].Value);
    }

    [Fact]
    public void Lex_NestedBracesInInterpolation()
    {
        var tokens = Tokenize("\"${dict{key}}\"", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("${dict{key}}", tokens[0].Value);
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
    public void Emit_FormatPlaceholder_PreservedAsLiteral()
    {
        var (converted, hasInterpolation) = CSharpEmitter.ConvertInlineInterpolation("${0} and ${1}");
        Assert.False(hasInterpolation);
        Assert.Equal("${0} and ${1}", converted);
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
        var converter = new CSharpToCalorConverter();
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
        Assert.Contains("$\"{a} + {b} = {a + b}\"", output);
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
        var converter = new CSharpToCalorConverter();
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
        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharp, "test.cs");
        Assert.True(conversionResult.Success,
            $"Conversion failed: {string.Join("; ", conversionResult.Issues.Select(i => i.Message))}");

        var calor = conversionResult.CalorSource!;

        // Compile back to C#
        var compilationResult = Program.Compile(calor, "test.calr",
            new CompilationOptions { EnforceEffects = false });
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
    }
}
