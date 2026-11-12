using Calor.Compiler.Ast;
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

    [Theory]
    [InlineData("double", "return \"\" + value;")]
    [InlineData("double", "return value + \"\";")]
    [InlineData("double", "return \"v=\" + value;")]
    [InlineData("double", "return \"[\" + value + \"]\";")]
    [InlineData("double", "string result = \"\"; result += value; return result;")]
    [InlineData("int", "return \"v=\" + value;")]
    [InlineData("decimal", "return \"v=\" + value;")]
    [InlineData("float", "return \"v=\" + value;")]
    [InlineData("uint", "return \"v=\" + value;")]
    [InlineData("double", "return \"\" + 3.5;")]
    [InlineData("object", "return \"\" + value;")]
    [InlineData("T", "return \"\" + value;")]
    public void StrictConcatenation_UnprovedFormattingIsUnknown(string type, string body)
    {
        var source = $$"""
            #nullable enable
            public static class ConcatenationBoundary
            {
                public static string Render{{(type == "T" ? "<T>" : "")}}({{type}} value) { {{body}} }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "concat-boundary.calr",
                StrictInterpolationOptions(typing));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("concatenation formatting", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("string", "return \"[\" + value + \"]\";")]
    [InlineData("string?", "return value + \"\";")]
    [InlineData("char", "return \"[\" + value + \"]\";")]
    [InlineData("bool", "return \"[\" + value + \"]\";")]
    [InlineData("bool", "string result = \"\"; result += value; return result;")]
    [InlineData("string?", "return \"[\" + null + \"]\";")]
    [InlineData("bool", "return \"\" + true;")]
    [InlineData("string", "return \"\" + nameof(value);")]
    [InlineData("int", "return value + 1 == 2 ? \"yes\" : \"no\";")]
    public void StrictConcatenation_ClosedFormattingAndArithmeticRemainSupported(string type, string body)
    {
        var source = $$"""
            #nullable enable
            public static class ConcatenationBoundary
            {
                public static string Render({{type}} value) { {{body}} }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "concat-safe.calr",
                StrictInterpolationOptions(typing));
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Fact]
    public void StrictConcatenation_FormattingDoesNotEraseOperandEffects()
    {
        const string source = """
            module ConcatenationBoundary
              func Render public () → string
                !
                return "[" + Console.ReadLine() + "]"
            """;
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(source, "concat-effects.calr", StrictInterpolationOptions(typing));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cr'", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Message.Contains("concatenation formatting", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StrictConcatenation_UserDefinedAdditionRetainsItsOwnEffects(bool effectful, bool conversion)
    {
        var source = $$"""
            module ConcatenationBoundary
              class Box
                {{(conversion ? "op implicit (Box:right) → string" : "op + (string:left, Box:right) → string")}}
                  {{(effectful ? "! cw" : "!")}}
                  {{(effectful ? "Console.WriteLine(\"operator\")" : "")}}
                  return {{(conversion ? "\"\"" : "left")}}
              func Render public (Box:value) → string
                !
                return "" + value
            """;
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(source, "concat-operator.calr", StrictInterpolationOptions(typing));
            Assert.Equal(effectful, result.HasErrors);
            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Message.Contains("concatenation formatting", StringComparison.Ordinal));
            if (effectful)
            {
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
                Assert.Empty(result.GeneratedCode);
            }
            else
                Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("double", true)]
    [InlineData("bool", false)]
    [InlineData("char", false)]
    [InlineData("string", false)]
    public void StrictConcatenation_SdkCompoundUsesTheSelectedOperation(string type, bool unknown)
    {
        var source = $$"""
            module ConcatenationBoundary
              func Render public ({{type}}:value) → string
                !
                var string:result = ""
                set result result + value
                return result
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var function = Assert.Single(module.Functions);
        var assignment = Assert.IsType<AssignmentStatementNode>(function.Body[1]);
        var addition = Assert.IsType<BinaryOperationNode>(assignment.Value);
        var replacement = function.CopyMetadataTo(new FunctionNode(function.Span, function.Id, function.Name,
            function.Visibility, function.Parameters, function.Output, function.Effects,
            function.Preconditions, function.Postconditions,
            [
                function.Body[0],
                new CompoundAssignmentStatementNode(assignment.Span, assignment.Target,
                    CompoundAssignmentOperator.Add, addition.Right),
                function.Body[2]
            ], function.Attributes));
        module = module.With(updated =>
        {
            updated.Functions = [replacement];
            updated.Items = module.Items.Select(item => ReferenceEquals(item, function) ? replacement : item).ToArray();
        });
        new EffectEnforcementPass(diagnostics, strictEffects: true).Enforce(module);
        Assert.Equal(unknown, diagnostics.HasErrors);
        if (unknown)
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("concatenation formatting (double)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("double", true, false)]
    [InlineData("bool", false, false)]
    [InlineData("double", true, true)]
    [InlineData("bool", false, true)]
    public void StrictConcatenation_SourceInitializationUsesTheSameBoundary(string type, bool unknown, bool bare)
    {
        var source = bare ? $$"""
            module ConcatenationBoundary
              class Box
                field {{type}}:Value static
                field string:Text static = "" + Value
                func Render public static () → string
                  !
                  return Text
            """ : $$"""
            module ConcatenationBoundary
              class Box
                field {{type}}:Value static
                field string:Text static = "" + Value
              func Render public () → string
                !
                return Box.Text
            """;
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(source, "concat-initializer.calr", StrictInterpolationOptions(typing));
            Assert.Equal(unknown, result.HasErrors);
            if (unknown)
            {
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.UnknownExternalCall
                    && diagnostic.Message.Contains("concatenation formatting (double)", StringComparison.Ordinal));
                Assert.Empty(result.GeneratedCode);
            }
            else
                Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("double", "3.5", true)]
    [InlineData("bool", "true", false)]
    public void StrictConcatenation_ExplicitTypeEntryChargesInitialization(
        string type, string value, bool unknown)
    {
        var source = $$"""
            #nullable enable
            public class ConcatenationBoundary
            {
                private static string Text = "" + {{value}};
                static ConcatenationBoundary() { }
                public static string Render(double unused) { return "constant"; }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "concat-type-entry.calr",
                StrictInterpolationOptions(typing));
            Assert.Equal(unknown, result.HasErrors);
            if (unknown)
            {
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.UnknownExternalCall
                    && diagnostic.Message.Contains($"concatenation formatting ({type})", StringComparison.Ordinal));
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable);
                Assert.Empty(result.GeneratedCode);
            }
            else
                Assert.NotEmpty(result.GeneratedCode);
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
