using Calor.Compiler.Analysis;
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

    // Formatting a hole calls the value's ToString(). Built-in types without a
    // manifest contract (decimal, float, uint), object and type parameters stay
    // unknown. The converter declares the sound `! unknown` row for them; the same
    // body under a pure row must still be rejected.
    [Theory]
    [InlineData("decimal", "value,-10:N2")]
    [InlineData("uint", "value")]
    [InlineData("float", "value")]
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
        var pure = WithPureRow(conversion.CalorSource!);
        foreach (var typing in new[] { false, true })
        {
            var declared = Program.Compile(conversion.CalorSource!, "interpolation-boundary.calr",
                StrictInterpolationOptions(typing));
            Assert.False(declared.HasErrors, string.Join("\n", declared.Diagnostics));
            var result = Program.Compile(pure, "interpolation-boundary.calr",
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

    // The restored 0.29 BCL manifest records System.Int32, System.Int64 and
    // System.Double ToString as pure, so their formatting is charged that contract
    // (as in 0.29) rather than Unknown, and the converter writes a pure row.
    [Theory]
    [InlineData("double", "value", true)]
    [InlineData("double", "value:F2", true)]
    [InlineData("double", "value,8", true)]
    [InlineData("int", "value", true)]
    [InlineData("long", "value:D3", true)]
    // Not a manifest row: f5cf0a0fd proves bounded X/x integral holes provider-independent.
    [InlineData("uint", "value:X", true)]
    [InlineData("double", "3.5", true)]
    [InlineData("double", "value + 1", true)]
    [InlineData("double", "return \"\" + value;", false)]
    [InlineData("double", "return value + \"\";", false)]
    [InlineData("double", "return \"v=\" + value;", false)]
    [InlineData("double", "return \"[\" + value + \"]\";", false)]
    [InlineData("double", "string result = \"\"; result += value; return result;", false)]
    [InlineData("int", "return \"v=\" + value;", false)]
    [InlineData("double", "return \"\" + 3.5;", false)]
    public void StrictFormatting_ManifestContractIsCharged(string type, string code, bool interpolation)
    {
        var body = interpolation ? $$"""=> $"{{{code}}}";""" : $$"""{ {{code}} }""";
        var source = $$"""
            #nullable enable
            public static class FormattingBoundary
            {
                public static string Render({{type}} value) {{body}}
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        Assert.Equal(conversion.CalorSource, WithPureRow(conversion.CalorSource!, requireUnknown: false));
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "manifest-formatting.calr",
                StrictInterpolationOptions(typing));
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.NotEmpty(result.GeneratedCode);
        }
    }

    /// <summary>
    /// Replaces the converter's single `! unknown` row with a pure row, or removes
    /// it when <paramref name="removeRow"/> is set.
    /// </summary>
    private static string WithPureRow(string calor, bool requireUnknown = true, bool removeRow = false)
    {
        var lines = calor.Split('\n');
        var rows = lines.Select((line, index) => (line, index))
            .Where(item => item.line.TrimEnd('\r').Trim() == "! unknown").ToArray();
        if (!requireUnknown)
        {
            Assert.Empty(rows);
            return calor;
        }
        var row = Assert.Single(rows);
        if (removeRow)
            return string.Join('\n', lines.Where((_, index) => index != row.index));
        lines[row.index] = row.line.Replace("! unknown", "!", StringComparison.Ordinal);
        return string.Join('\n', lines);
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
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cr'", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    // See StrictInterpolation_UnprovedFormattingIsUnknown: the converter declares
    // `! unknown`, and a pure row over the same body is rejected.
    [Theory]
    [InlineData("decimal", "return \"v=\" + value;")]
    [InlineData("decimal", "string result = \"\"; result += value; return result;")]
    [InlineData("float", "return \"v=\" + value;")]
    [InlineData("uint", "return \"v=\" + value;")]
    [InlineData("uint", "return value + \"\";")]
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
        var pure = WithPureRow(conversion.CalorSource!);
        foreach (var typing in new[] { false, true })
        {
            var declared = Program.Compile(conversion.CalorSource!, "concat-boundary.calr",
                StrictInterpolationOptions(typing));
            Assert.False(declared.HasErrors, string.Join("\n", declared.Diagnostics));
            var result = Program.Compile(pure, "concat-boundary.calr",
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
    [InlineData("double", false)]
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
    [InlineData("double", false, false)]
    [InlineData("bool", false, false)]
    [InlineData("double", false, true)]
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

    // An explicit static constructor carries the type's initialization row. For
    // unproved formatting the converter declares `! unknown` there; without that
    // row the initializer is rejected (Calor0422). System.Double formatting is pure under the
    // restored 0.29 manifest.
    [Theory]
    [InlineData("decimal", "3.5m", true)]
    [InlineData("double", "3.5", false)]
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
        var checkedSource = unknown ? WithPureRow(conversion.CalorSource!, removeRow: true) : conversion.CalorSource!;
        if (unknown)
            Assert.Contains("ctor stat ()\n      ! unknown\n", conversion.CalorSource!.ReplaceLineEndings("\n"),
                StringComparison.Ordinal);
        foreach (var typing in new[] { false, true })
        {
            Assert.False(Program.Compile(conversion.CalorSource!, "concat-type-entry.calr",
                StrictInterpolationOptions(typing)).HasErrors);
            var result = Program.Compile(checkedSource, "concat-type-entry.calr",
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

    [Theory]
    // System.Double and System.Int32 formatting are pure under the restored 0.29
    // manifest. An unproved implicit initializer is covered natively by
    // StrictConcatenation_ImplicitStaticInitializationDoesNotNeedAnExecutableMethod.
    [InlineData("string", "\"\" + 3.5", false, false)]
    [InlineData("string", "\"\" + -1234", false, false)]
    [InlineData("string", "\"\" + 3.5", true, false)]
    [InlineData("string", "\"\" + true", false, false)]
    [InlineData("string", "\"\" + default(bool)", false, false)]
    [InlineData("string", "\"\" + 'x'", false, false)]
    [InlineData("string", "\"\" + \"text\"", false, false)]
    [InlineData("string", "\"\" + null", false, false)]
    [InlineData("int", "1 + 2", false, false)]
    [InlineData("int[]", "new int[] { 1, 2 }", false, false)]
    [InlineData("string", "\"\" + Read()", false, false)]
    public void StrictConcatenation_ImplicitStaticInitializationIsCheckedAtDeclaration(
        string type, string initializer, bool property, bool unknown)
    {
        var source = $$"""
            #nullable enable
            public class StaticInitializationBoundary
            {
                private static {{type}} Text{{(property ? " { get; }" : "")}} = {{initializer}};
                {{(initializer == "\"\" + Read()" ? "private static bool Read() { return true; }" : "")}}
                public static string Render(double unused) { return "constant"; }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "implicit-static-initializer.calr",
                StrictInterpolationOptions(typing));
            Assert.Equal(unknown, result.HasErrors);
            if (unknown)
            {
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.UnknownExternalCall
                    && diagnostic.Message.Contains("concatenation formatting", StringComparison.Ordinal));
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable
                    && diagnostic.Message.Contains("implicit static initializer", StringComparison.Ordinal));
                Assert.Empty(result.GeneratedCode);
            }
            else
                Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Fact]
    public void StrictConcatenation_ImplicitStaticInitializationDoesNotNeedAnExecutableMethod()
    {
        const string source = """
            module StaticInitializationBoundary
              class Box
                field string:Text private static = "" + 3.5m
            """;
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(source, "implicit-static-declaration.calr",
                StrictInterpolationOptions(typing));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("concatenation formatting (decimal)", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable
                && diagnostic.Message.Contains("implicit static initializer", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("true", "", false)]
    [InlineData("'x'", "", false)]
    [InlineData("\"text\"", "", false)]
    [InlineData("Value", "private static string Value = \"text\";", false)]
    [InlineData("Value", "private static string? Value = null;", false)]
    [InlineData("true,6:unused", "", false)]
    [InlineData("Value,6:unused", "private static string Value = \"text\";", false)]
    // Pure under the restored 0.29 manifest (System.Double / System.Int32 ToString).
    [InlineData("3.5", "", false)]
    [InlineData("-1234", "", false)]
    [InlineData("3.5,6:N2", "", false)]
    [InlineData("Value", "private static object Value = true;", true)]
    public void StrictInterpolation_ImplicitInitializationUsesBoundFormatting(
        string value, string member, bool unknown)
    {
        var source = $$"""
            #nullable enable
            public class InterpolationInitializationBoundary
            {
                {{member}}
                private static string Text = $"value={{{value}}}";
                public static string Render() { return "constant"; }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "interpolation-static-initializer.calr",
                StrictInterpolationOptions(typing));
            Assert.Equal(unknown, result.HasErrors);
            if (unknown)
            {
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.UnknownExternalCall
                    && diagnostic.Message.Contains("interpolation formatting", StringComparison.Ordinal));
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable);
                Assert.Empty(result.GeneratedCode);
            }
            else
                Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("default(bool)", false)]
    [InlineData("Missing", true)]
    public void StrictInterpolation_NativeInitializerLiteralOrUnavailableProof(string value, bool unknown)
    {
        var source = $$"""
            module InterpolationInitializationBoundary
              class Box
                field string:Text private static = "value=${{{value}}}"
                func Render public static () → string
                  !
                  return "constant"
            """;
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(source, "native-interpolation-static-initializer.calr",
                StrictInterpolationOptions(typing));
            Assert.Equal(unknown, result.HasErrors);
            if (unknown)
                Assert.Empty(result.GeneratedCode);
            else
                Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Fact]
    public void StrictInterpolation_ImplicitInitializerRetainsOperandEffects()
    {
        const string source = """
            #nullable enable
            public class InterpolationInitializationBoundary
            {
                private static string Read() { System.Console.WriteLine("effect"); return "text"; }
                private static string Text = $"value={Read()}";
                public static string Render() { return "constant"; }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "interpolation-static-effects.calr",
                StrictInterpolationOptions(typing));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData(false, "virtual", false)]
    [InlineData(false, "virtual", true)]
    [InlineData(true, "virtual", false)]
    [InlineData(true, "virtual", true)]
    [InlineData(false, "nonvirtual", false)]
    [InlineData(true, "nonvirtual", false)]
    [InlineData(false, "sealed-receiver", false)]
    [InlineData(true, "sealed-receiver", false)]
    [InlineData(false, "sealed-override", false)]
    [InlineData(true, "sealed-override", false)]
    [InlineData(false, "open-pure-override", false)]
    [InlineData(true, "open-pure-override", false)]
    [InlineData(false, "sealed-effectful-override", false)]
    public void StrictInterpolation_InitializerDispatchNeedsBoundAuthority(
        bool getter, string dispatch, bool fieldRead)
    {
        var virtualMember = dispatch != "nonvirtual";
        var baseMember = getter
            ? $$"""public {{(virtualMember ? "virtual " : "")}}string Value { get { return "safe"; } }"""
            : $$"""public {{(virtualMember ? "virtual " : "")}}string Read() { return "safe"; }""";
        var overrides = dispatch is "virtual" or "sealed-override" or "open-pure-override" or "sealed-effectful-override";
        var body = dispatch is "virtual" or "sealed-effectful-override"
            ? """System.Console.WriteLine("effect"); return "effect";""" : """return "safe";""";
        var sealedOverride = dispatch is "sealed-override" or "sealed-effectful-override";
        var derivedMember = !overrides ? "" : getter
            ? $$"""public {{(sealedOverride ? "sealed " : "")}}override string Value { get { {{body}} } }"""
            : $$"""public {{(sealedOverride ? "sealed " : "")}}override string Read() { {{body}} }""";
        var authoritative = dispatch is "nonvirtual" or "sealed-receiver" || sealedOverride;
        var effectful = dispatch == "sealed-effectful-override";
        var source = $$"""
            #nullable enable
            public class Base { {{baseMember}} }
            public {{(dispatch == "sealed-receiver" ? "sealed " : "")}}class Derived : Base { {{derivedMember}} }
            public class Holder
            {
                private static {{(dispatch == "sealed-receiver" || sealedOverride ? "Derived" : "Base")}} Source = new Derived();
                private static string Text = $"value={{{(getter ? "Source.Value" : "Source.Read()")}}}";
                public static string Render() { return {{(fieldRead ? "Text" : "\"constant\"")}}; }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        Assert.Equal(0, conversion.DropCount);
        Assert.Equal(0, conversion.LossySubstitutionCount);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "initializer-dispatch.calr",
                StrictInterpolationOptions(typing));
            Assert.Equal(getter || !authoritative || effectful, result.HasErrors);
            if (!authoritative)
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.UnknownExternalCall
                    && diagnostic.Message.Contains("unproved initializer dispatch", StringComparison.Ordinal));
            if (effectful)
            {
                Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.UnknownExternalCall);
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable
                    && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
            }
            if (result.HasErrors)
                Assert.Empty(result.GeneratedCode);
            else
                Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("virtual", false, false)]
    [InlineData("virtual", true, false)]
    [InlineData("virtual", false, true)]
    [InlineData("nonvirtual", false, false)]
    [InlineData("sealed-receiver", false, false)]
    [InlineData("sealed-override", false, false)]
    [InlineData("base-qualified", false, false)]
    public void StrictInterpolation_InitializerHelpersRetainDispatch(
        string dispatch, bool nestedHelper, bool concatenation)
    {
        var source = $$"""
            #nullable enable
            public class Base
            {
                public {{(dispatch == "nonvirtual" ? "" : "virtual ")}}string Read() { return "safe"; }
            }
            public {{(dispatch == "sealed-receiver" ? "sealed " : "")}}class Derived : Base
            {
                {{(dispatch == "nonvirtual" ? "" : $$"""public {{(dispatch == "sealed-override" ? "sealed " : "")}}override string Read() { {{(dispatch is "virtual" or "base-qualified" ? """System.Console.WriteLine("effect");""" : "")}} return "text"; }""")}}
                {{(dispatch == "base-qualified" ? """public string ReadBase() { return base.Read(); }""" : "")}}
            }
            public class Holder
            {
                private static {{(dispatch is "sealed-receiver" or "sealed-override" or "base-qualified" ? "Derived" : "Base")}} Source = new Derived();
                private static string ReadSource() { return Source.{{(dispatch == "base-qualified" ? "ReadBase" : "Read")}}(); }
                {{(nestedHelper ? """private static string ReadAgain() { return ReadSource(); }""" : "")}}
                private static string Text = {{(concatenation ? "\"value=\" + " + (nestedHelper ? "ReadAgain()" : "ReadSource()") : "$\"value={" + (nestedHelper ? "ReadAgain()" : "ReadSource()") + "}\"")}};
                public static string Render() { return Text; }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Equal(0, conversion.DropCount);
        Assert.Equal(0, conversion.LossySubstitutionCount);
        Assert.Empty(conversion.Losses);
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(conversion.CalorSource!, "initializer-helper-dispatch.calr",
                StrictInterpolationOptions(typing));
            Assert.Equal(dispatch == "virtual", result.HasErrors);
            if (dispatch == "virtual")
            {
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.UnknownExternalCall
                    && diagnostic.Message.Contains("unproved initializer dispatch", StringComparison.Ordinal));
                Assert.Empty(result.GeneratedCode);
            }
            else
                Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("int", "value,8:X4")]
    [InlineData("uint", "value:X")]
    [InlineData("long", "value,-20:x16")]
    [InlineData("ulong", "value:x00")]
    [InlineData("short", "value:X99")]
    [InlineData("ushort", "value,256:x")]
    [InlineData("sbyte", "value:X0")]
    [InlineData("byte", "value:x09")]
    public void StrictInterpolation_BoundedIntegralHexIsProviderIndependent(string type, string hole)
    {
        var source = $$"""
            module HexBoundary
              func Render public ({{type}}:value) → string
                !
                return "${{{hole}}}"
            """;
        foreach (var native in HexEmissions(source))
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(native, "hex-boundary.calr", HexOptions(typing));
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("float", "value:x")]
    [InlineData("decimal", "value:X4")]
    [InlineData("object", "value:X4")]
    public void StrictInterpolation_HexProofDoesNotAuthorizeUnprovedNeighbors(string type, string hole)
    {
        var source = $$"""
            module HexBoundary
              func Render public ({{type}}:value) → string
                !
                return "${{{hole}}}"
            """;
        foreach (var native in HexEmissions(source))
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(native, "unproved-hex.calr", HexOptions(typing));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("interpolation formatting", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Fact]
    public void StrictInterpolation_HexProofRetainsEvaluatedHoleEffects()
    {
        const string source = """
            module HexBoundary
              func Render public () → string
                !
                return "${Console.Read(),8:X4}"
            """;
        foreach (var native in HexEmissions(source))
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(native, "hex-effects.calr", HexOptions(typing));
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cr'", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("interpolation formatting", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StrictInterpolation_HexProofDoesNotAuthorizeGenericOrCustomFormatting(bool generic)
    {
        var source = $$"""
            public sealed class Loud : System.IFormattable
            {
                public string ToString(string format, System.IFormatProvider provider) => "custom";
            }
            public static class HexBoundary
            {
                public static string Render{{(generic ? "<T>" : "")}}({{(generic ? "T" : "Loud")}} value) => $"{value:X4}";
            }
            """;
        AssertHexFormattingDeclaredUnknown(source);
    }

    [Fact]
    public void StrictInterpolation_HexInitializerUsesTheSameBoundedProof()
    {
        const string source = """
            module HexBoundary
              class Box
                field string:Text static = "${-42,8:X4}"
                func Render public static () → string
                  !
                  return Text
            """;
        foreach (var native in HexEmissions(source))
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(native, "hex-initializer.calr", HexOptions(typing));
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        }
    }

    // 0.30: the converter declares ! unknown on a callable whose strict effects are Unknown.
    // The hex proof does not cover generic or custom IFormattable formatting, so Render is
    // declared ! unknown (never pure), compiles under strict settings, and the same body
    // with a pure row is still rejected for the unresolved interpolation formatting.
    private static void AssertHexFormattingDeclaredUnknown(string source)
    {
        var conversion = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy })
            .Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        var render = Assert.Single(CallGraphAnalysis.EnumerateClasses(conversion.Ast!)
            .SelectMany(CallGraphAnalysis.EnumerateMethods), method => method.Name == "Render");
        Assert.True(EffectEnforcementPass.GetDeclaredEffects(render.Effects).IsUnknown, conversion.CalorSource);
        foreach (var native in HexEmissions(conversion.CalorSource!))
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(native, "unproved-hex-context.calr", HexOptions(typing));
            Assert.False(result.HasErrors, native + "\n" + string.Join("\n", result.Diagnostics));
            var pure = native.Replace("! unknown\n", "!\n", StringComparison.Ordinal);
            Assert.NotEqual(native, pure);
            var rejected = Program.Compile(pure, "unproved-hex-context.calr", HexOptions(typing));
            Assert.True(rejected.Diagnostics.Errors.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("interpolation formatting", StringComparison.Ordinal)),
                pure + "\n" + string.Join("\n", rejected.Diagnostics));
            Assert.Empty(rejected.GeneratedCode);
        }
    }

    private static string[] HexEmissions(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return [source, new CalorEmitter().Emit(module)];
    }

    private static CompilationOptions HexOptions(bool typing) => new()
    {
        EnableTypeChecking = typing, EnforceEffects = true, StrictEffects = true,
        StrictApi = true, UnknownCallPolicy = UnknownCallPolicy.Strict,
        VerifyContracts = false, StatusWriter = TextWriter.Null
    };

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
