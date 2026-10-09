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
/// #1524 (object initializers) and #1528 (whole-number double literals): convert C#
/// to Calor, compile the Calor, run both programs and compare the observed result,
/// including side-effect order. A case is either native (no interop loss) or kept as
/// C# with a reported <c>object-initializer</c> loss; it is never dropped.
/// </summary>
public sealed class ConverterInitializerAndFloatSemanticsTests
{
    private const string Types = """
        private static string Use(P p) => Trace + p.A;
        public class Inner { public int X { get; set; } = 1; public string Keep = "keep"; }
        public class P
        {
            public P() { Migrated.Trace += "k"; L.Add(0); }
            public P(int seed) { Migrated.Trace += "k" + seed; }
            public int A { get; set; }
            public int B;
            public Inner Inner { get; } = new Inner();
            public Inner? Other { get; set; }
            public List<int> L { get; set; } = new List<int>();
        }
        """;

    private const string Holder = """
        public class H { public P Prop { get; } = new P { A = 1 }; public P Field = new P { A = 2, B = 3 }; }
        """;

    public static TheoryData<string, string, bool> InitializerCases => new()
    {
        // Native §NEW: constructor argument, constructor body, then members in source order.
        { "var p = new P(T(\"c\", 1)) { A = T(\"a\", 2), B = T(\"b\", 3) }; return Trace + p.A + p.B;", "ck1ab23", false },
        { "var p = new P { L = new List<int> { 1, 2 } }; return Trace + string.Join(\",\", p.L);", "k1,2", false },
        { "var p = new P(T(\"c\", 1)) { Other = new Inner { X = T(\"x\", 3) } }; return Trace + p.Other!.X;", "ck1x3", false },
        { "var sb = new StringBuilder { Capacity = 20 }; return sb.Capacity;", "20", false },
        { "var sb = new StringBuilder(\"ab\") { Capacity = 20 }; return sb.Capacity + sb.ToString();", "20ab", false },
        { "return Use(new() { A = T(\"a\", 4) });", "ka4", false },
        // Kept as C#: forms §NEW cannot express.
        { "var l = new List<int>(4) { T(\"x\", 1), 2 }; return Trace + l.Count + l[0];", "x21", true },
        { "List<int> l = new() { 1, 2 }; return l.Count;", "2", true },
        { "var p = new P { Inner = { X = 7 } }; return Trace + p.Inner.X + p.Inner.Keep;", "k7keep", true },
        { "var p = new P { L = { 5 } }; return Trace + string.Join(\",\", p.L);", "k0,5", true },
        { "var p = new P(T(\"c\", 1)) { L = new List<int> { T(\"l\", 5) } }; return Trace + p.L[0];", "ck1l5", true },
        { "var q = new P(7); q = new() { L = new List<int> { 5 } }; return Trace + string.Join(\",\", q.L);", "k7k5", true },
        { "return X() + \"|\" + $\"{Y()}{new P { A = A5() }.A}\" + Trace;", "1|25xyka", true },
    };

    [Theory]
    [MemberData(nameof(InitializerCases))]
    public void ObjectInitializers_PreserveBehaviorOrReportLoss(string body, string expected, bool preserved)
    {
        var conversion = AssertEquivalent(body, expected);
        var initializerLosses = conversion.Losses.Where(loss => loss.Feature == "object-initializer").ToList();
        if (preserved)
            Assert.Contains(initializerLosses, loss => loss.Kind == ConversionLossKind.InteropPreserved);
        else
            Assert.Empty(initializerLosses);
    }

    [Fact]
    public void AutoPropertyDefault_StaysOneObject()
    {
        // The getter used to rebuild the object on every read via `set_A` calls,
        // which did not compile (CS0571) and would have discarded `h.Prop.A = 9`.
        var conversion = AssertEquivalent(
            "var h = new H(); h.Prop.A = 9; return Trace + h.Prop.A + h.Field.A + h.Field.B;",
            "kk923", Types + Holder);
        Assert.DoesNotContain("_objInit", conversion.CalorSource);
        Assert.DoesNotContain(conversion.Losses, loss => loss.Feature == "object-initializer");
    }

    [Fact]
    public void StringBuilderWithoutInitializer_StaysNative()
    {
        var conversion = AssertEquivalent("var sb = new StringBuilder(\"ab\"); return sb.ToString();", "ab");
        Assert.Contains("(sb-new \"ab\")", conversion.CalorSource);
    }

    [Fact]
    public void ObjectInitializer_KeepsMembersInNewBlock()
    {
        var conversion = AssertEquivalent("var sb = new StringBuilder { Capacity = 20 }; return sb.Capacity;", "20");
        Assert.Contains("Capacity = 20", conversion.CalorSource);
        Assert.DoesNotContain("(sb-new)", conversion.CalorSource);
    }

    public static TheoryData<string, string> FloatCases => new()
    {
        { "int n = 5; return n / 2.0;", "2.5" },
        { "return 1 / 2.0;", "0.5" },
        { "return default(double) + 1 / 2.0;", "0.5" },
        { "int n = 5; return n / 1e2;", "0.05" },
        { "int n = -3; return n / -2.0;", "1.5" },
        { "int n = 1; return n / 1e20;", "1E-20" },
        { "int n = 5; return n / 2f;", "2.5" },
        { "int n = 5; return n / 2.0m;", "2.5" },
        { "int n = 5; return $\"{n / 2.0}\";", "2.5" },
        // Negative control: an integer literal keeps integer division.
        { "int n = 5; return n / 2;", "2" },
    };

    [Theory]
    [MemberData(nameof(FloatCases))]
    public void WholeNumberFloatLiterals_KeepFloatingDivision(string body, string expected) =>
        AssertEquivalent(body, expected, types: "");

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

    private static ConversionResult AssertEquivalent(string body, string expected, string types = Types)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Text;
            public class Migrated
            {
                public static string Trace = "";
                private static int T(string tag, int value) { Trace += tag; return value; }
                private static int X() { Trace += "x"; return 1; }
                private static int Y() { Trace += "y"; return 2; }
                private static int A5() { Trace += "a"; return 5; }
                public static object Probe() { {{body}} }
                {{types}}
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success,
            string.Join("; ", conversion.Issues.Select(issue => issue.Message)) + "\n" + conversion.CalorSource);
        Assert.DoesNotContain(conversion.Losses, loss => loss.Kind == ConversionLossKind.Dropped);
        // Effect enforcement is off: member access on the test's own classes reports
        // effect 'unknown' (Calor0410) independently of the behavior compared here.
        var compiled = Program.Compile(conversion.CalorSource!, "initializers.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = false,
            StatusWriter = TextWriter.Null
        });
        Assert.False(compiled.HasErrors,
            string.Join("; ", compiled.Diagnostics.Errors) + "\n" + conversion.CalorSource);
        Assert.Equal(expected, Observe(source));
        Assert.Equal(expected, Observe(compiled.GeneratedCode));
        return conversion;
    }

    private static string Observe(string source)
    {
        var compilation = CSharpCompilation.Create("InitializerOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics) + "\n" + source);
        var value = Assembly.Load(image.ToArray()).GetTypes().Single(type => type.Name == "Migrated")
            .GetMethod("Probe")!.Invoke(null, null)!;
        return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }
}
