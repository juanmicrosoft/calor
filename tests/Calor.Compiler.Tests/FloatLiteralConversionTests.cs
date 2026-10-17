using System.Globalization;
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

/// <summary>
/// #1528 (whole-number double literals): convert C# to Calor, compile the Calor, run both
/// programs and compare the observed value and its runtime type. A whole-number double such
/// as <c>2.0</c> must stay a floating-point literal, so <c>n / 2.0</c> keeps floating division.
/// </summary>
public sealed class FloatLiteralConversionTests
{
    public static TheoryData<string, string> FloatCases => new()
    {
        { "int n = 5; return n / 2.0;", "Double:2.5" },
        { "return 1 / 2.0;", "Double:0.5" },
        { "return default(double) + 1 / 2.0;", "Double:0.5" },
        { "return default(float) + 2f;", "Single:2" },
        { "return default(decimal) + 2m;", "Decimal:2" },
        // The widths on their own: an int 0 would pass the two sums above.
        { "return default(float);", "Single:0" },
        { "return default(decimal);", "Decimal:0" },
        { "int n = 5; return n / 1e2;", "Double:0.05" },
        { "int n = -3; return n / -2.0;", "Double:1.5" },
        { "int n = 1; return n / 1e20;", "Double:1E-20" },
        { "int n = 1; return n / 3f;", "Single:0.33333334" },
        { "int n = 5; return n / 2.0m;", "Decimal:2.5" },
        { "int n = 5; return $\"{n / 2.0}\";", "String:2.5" },
        // Negative control: an integer literal keeps integer division.
        { "int n = 5; return n / 2;", "Int32:2" },
    };

    [Theory]
    [MemberData(nameof(FloatCases))]
    public void WholeNumberFloatLiterals_KeepFloatingDivision(string body, string expected) =>
        AssertEquivalent(body, expected, typed: true);

    [Fact]
    public void DefaultOfAliasedName_UsesTheResolvedType() =>
        AssertEquivalent("return default(Single);", "Int32:0", typed: true,
            usings: "using Single = System.Int32;");

    [Fact]
    public void AttributeFloatArguments_KeepTheirOverload()
    {
        const string attributes = """
            public class KindAttribute : Attribute
            {
                private readonly string _value;
                public KindAttribute(int x) { _value = "int"; }
                public KindAttribute(double x) { _value = "double"; }
                public KindAttribute(float x) { _value = "float"; }
                public override string ToString() => _value;
            }
            [Kind(2.0)] public class MD { }
            [Kind(2f)] public class MF { }
            [Kind(2)] public class MI { }
            """;
        AssertEquivalent(
            "return typeof(MD).GetCustomAttributes(false)[0] + \",\" + typeof(MF).GetCustomAttributes(false)[0] + \",\" + typeof(MI).GetCustomAttributes(false)[0];",
            "double,float,int", types: attributes);
    }

    [Theory]
    [InlineData(2.0, false, "2.0")]
    [InlineData(-2.0, false, "-2.0")]
    [InlineData(100.0, false, "100.0")]
    [InlineData(1e20, false, "1E+20")]
    [InlineData(0.5, false, "0.5")]
    [InlineData(2.0, true, "SINGLE:2")]
    public void FloatLiteral_EmitsLexerFloatForm(double value, bool single, string expected)
    {
        var node = new FloatLiteralNode(new TextSpan(0, 0, 1, 1), value) { IsSingle = single };
        var emitted = new CalorEmitter().Visit(node);
        Assert.Equal(expected, emitted);

        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(emitted, diagnostics).TokenizeAll();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(TokenKind.FloatLiteral, tokens[0].Kind);
    }

    [Theory]
    [InlineData(double.MaxValue)]
    [InlineData(double.Epsilon)]
    [InlineData(123456789012345.0)]
    [InlineData(-0.0)]
    public void FloatLiteral_RoundTripsValueThroughLexer(double value)
    {
        var emitted = new CalorEmitter().Visit(new FloatLiteralNode(new TextSpan(0, 0, 1, 1), value));
        var diagnostics = new DiagnosticBag();
        var expression = new Parser(new Lexer($"§M{{m:R}}\n  §F{{f:V:pub}} () -> f64\n    §R {emitted}", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, emitted + ": " + string.Join(Environment.NewLine, diagnostics.Errors));
        var returned = Assert.IsType<ReturnStatementNode>(Assert.Single(expression.Functions).Body[0]).Expression;
        var literal = returned switch
        {
            FloatLiteralNode floating => floating.Value,
            UnaryOperationNode { Operand: FloatLiteralNode floating } => -floating.Value,
            _ => throw new Xunit.Sdk.XunitException($"{emitted} parsed as {returned?.GetType().Name}")
        };
        Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(literal));
    }

    private static ConversionResult AssertEquivalent(
        string body, string expected, string types = "", bool typed = false, string usings = "")
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Text;
            {{usings}}
            public class Migrated
            {
                public static object Probe() { {{body}} }
                {{types}}
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success,
            string.Join("; ", conversion.Issues.Select(issue => issue.Message)) + "\n" + conversion.CalorSource);
        Assert.DoesNotContain(conversion.Losses, loss => loss.Kind == ConversionLossKind.Dropped);
        // Effect enforcement is off: member access on the test's own classes reports
        // effect 'unknown' (Calor0410) independently of the value compared here.
        var compiled = Program.Compile(conversion.CalorSource!, "floats.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = false,
            StatusWriter = TextWriter.Null
        });
        Assert.False(compiled.HasErrors,
            string.Join("; ", compiled.Diagnostics.Errors) + "\n" + conversion.CalorSource);
        Assert.Equal(expected, Observe(source, typed));
        Assert.Equal(expected, Observe(compiled.GeneratedCode, typed));
        return conversion;
    }

    private static string Observe(string source, bool typed)
    {
        var compilation = CSharpCompilation.Create("FloatOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics) + "\n" + source);
        // Both programs run under the invariant culture, so an interpolated double formats
        // the same way on every host.
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var value = Assembly.Load(image.ToArray()).GetTypes().Single(type => type.Name == "Migrated")
                .GetMethod("Probe")!.Invoke(null, null)!;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
            return typed ? value.GetType().Name + ":" + text : text;
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
