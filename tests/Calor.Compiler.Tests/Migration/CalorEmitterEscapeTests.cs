using Calor.Compiler.Commands;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests.PostConversion;

/// <summary>
/// Tests for the CalorEmitter brace escaping fix and ConvertCommand validation.
/// </summary>
public class CalorEmitterEscapeTests
{
    private static string ConvertToCalor(string csharpSource)
    {
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharpSource);
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => $"[{i.Severity}] {i.Message}")));
        Assert.NotNull(result.CalorSource);
        var emitter = new CalorEmitter();
        return emitter.Emit(result.Ast!);
    }

    // ========================
    // Direct unit tests for EscapeBracesInIdentifier
    // ========================

    [Fact]
    public void EscapeBracesInIdentifier_NoBraces_ReturnsUnchanged()
    {
        Assert.Equal("Console.WriteLine", CalorEmitter.EscapeBracesInIdentifier("Console.WriteLine"));
    }

    [Fact]
    public void EscapeBracesInIdentifier_PlainBraces_Escapes()
    {
        // Braces outside any string should be escaped
        Assert.Equal("target\\{x\\}", CalorEmitter.EscapeBracesInIdentifier("target{x}"));
    }

    [Fact]
    public void EscapeBracesInIdentifier_StringLiteralTarget_PreservesBraces()
    {
        // The exact pattern from the bug report: "Error: {0}".FormatWith
        var input = "\"Error: {0}\".FormatWith";
        var result = CalorEmitter.EscapeBracesInIdentifier(input);
        Assert.Equal("\"Error: {0}\".FormatWith", result);
    }

    [Fact]
    public void EscapeBracesInIdentifier_VerbatimString_PreservesBraces()
    {
        // Verbatim string @"text {0}".Method
        var input = "@\"text {0}\".Method";
        var result = CalorEmitter.EscapeBracesInIdentifier(input);
        Assert.Equal("@\"text {0}\".Method", result);
    }

    [Fact]
    public void EscapeBracesInIdentifier_VerbatimStringWithDoubledQuotes_HandlesCorrectly()
    {
        // Verbatim string with "" inside: @"say ""hello {0}""".Method
        var input = "@\"say \"\"hello {0}\"\"\".Method";
        var result = CalorEmitter.EscapeBracesInIdentifier(input);
        // Braces inside the verbatim string should be preserved
        Assert.Equal("@\"say \"\"hello {0}\"\"\".Method", result);
    }

    [Fact]
    public void EscapeBracesInIdentifier_EscapedQuoteInString_HandlesCorrectly()
    {
        // Regular string with escaped quote: "say \"hello {0}\"".Method
        var input = "\"say \\\"hello {0}\\\"\".Method";
        var result = CalorEmitter.EscapeBracesInIdentifier(input);
        // Braces inside the string should be preserved
        Assert.Equal("\"say \\\"hello {0}\\\"\".Method", result);
    }

    [Fact]
    public void EscapeBracesInIdentifier_EscapedBackslashBeforeQuote_ClosesString()
    {
        // String ending with \\": the \\ is an escaped backslash, then " closes the string
        // "path\\" followed by {x} outside
        var input = "\"path\\\\\"{x}";
        var result = CalorEmitter.EscapeBracesInIdentifier(input);
        // The {x} is outside the string, should be escaped
        Assert.Equal("\"path\\\\\"\\{x\\}", result);
    }

    [Fact]
    public void EscapeBracesInIdentifier_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", CalorEmitter.EscapeBracesInIdentifier(""));
    }

    [Fact]
    public void EscapeBracesInIdentifier_MultipleBraces_AllEscapedOutsideStrings()
    {
        var input = "{a}.\"text {0}\".{b}";
        var result = CalorEmitter.EscapeBracesInIdentifier(input);
        Assert.Equal("\\{a\\}.\"text {0}\".\\{b\\}", result);
    }

    // ========================
    // Integration tests (C# → Calor conversion)
    // ========================

    [Fact]
    public void PlainMethodTarget_NoExtraBraces()
    {
        var csharp = @"
public class Test
{
    public int Add(int a, int b)
    {
        return a + b;
    }

    public int UseAdd()
    {
        return Add(1, 2);
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.DoesNotContain("\\{", calor);
        Assert.DoesNotContain("\\}", calor);
    }

    [Fact]
    public void StringInterpolation_NoBraceCorruption()
    {
        var csharp = @"
public class Test
{
    public string Format(int x)
    {
        return $""Value: {x}"";
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.DoesNotContain("\\{x\\}", calor);
    }

    [Fact]
    public void StringFormat_NoBraceCorruption()
    {
        var csharp = @"
using System;
public class Test
{
    private string FormatMessage(string arg)
    {
        return string.Format(""Error: {0}"", arg);
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.DoesNotContain("\\{0\\}", calor);
    }

    // ========================
    // ValidateCalorSource tests
    // ========================

    [Fact]
    public void ValidateCalorSource_ValidSource_ReturnsNoErrors()
    {
        var validCalor = @"
§M{m1:Test}
  §CL{c1:Test}
    §MT{mt1:M:pub}
      §O{void}
    §/MT{mt1}
  §/CL{c1}
§/M{m1}
";
        var errors = ConvertCommand.ValidateCalorSource(validCalor);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCalorSource_InvalidSource_ReturnsErrors()
    {
        // Malformed Calor with unmatched tags
        var invalidCalor = "§M{m1:Test}\n§CL{c1:Test}\n§/M{m1}";
        var errors = ConvertCommand.ValidateCalorSource(invalidCalor);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidateCalorSource_EmptySource_ReturnsErrors()
    {
        // Empty source is invalid Calor (requires a module)
        var errors = ConvertCommand.ValidateCalorSource("");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidateCalorSource_EscapedBraceCorruption_DetectsErrors()
    {
        // Simulate the kind of corruption that \{ escaping would produce
        var corruptedCalor = "§C{obj.\\{method\\}} §/C";
        var errors = ConvertCommand.ValidateCalorSource(corruptedCalor);
        Assert.NotEmpty(errors);
    }
}
