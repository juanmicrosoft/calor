using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Commands;
using Calor.Compiler.Effects;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1600: safe non-capturing local functions become same-point lambda bindings.
/// Forward/mutual calls and method-group conversions preserve the member.
/// Native rows compile with default effect enforcement and run against the original C#.
/// Preserved members retain counted losses and the existing permissive unknown-call
/// policy for callers without native declarations.
/// </summary>
public class LocalFunctionConversionTests
{
    private const string R0Fixtures = "tests/TestData/InteropScope/R0/fixtures";

    public enum Surface
    {
        LibraryDefault,
        CliDefault,
        CliPassthrough,
        McpDefault,
        McpPassthroughOnError,
        McpPassthroughOnErrorModuleName,
        ProjectMigrate,
        ProjectMigratePassthrough
    }

    // Beyond the registered rows: a C-1 shadow on an INSTANCE method whose outer
    // body still reads an instance field, mutual recursion, an effectful void local
    // declared after the return (call-before-declaration), and a local parameter
    // that shadows the outer parameter.
    private const string MoreShapes = """
        using System;
        using System.Text;

        public class Shapes
        {
            private int _count = 10;
            public int Foo() => 999;
            public static int Twice(int x) => x * 2;

            public int ShadowOnInstance()
            {
                int Foo() => 1;
                return Foo() + _count;
            }

            public bool Parity(int n)
            {
                bool Even(int k) => k == 0 || Odd(k - 1);
                bool Odd(int k) => k != 0 && Even(k - 1);
                return Even(n);
            }

            public static string Effects()
            {
                var sb = new StringBuilder();
                Append(sb, "x");
                Append(sb, "y");
                return sb.ToString() + Twice(3);

                static void Append(StringBuilder target, string s)
                {
                    target.Append(s);
                    Console.Write("");
                }
            }

            public static int Shadowing(int x)
            {
                int Square(int x) => x * x;
                return Square(x + 1);
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                var s = new Shapes();
                return s.ShadowOnInstance() + "|" + s.Parity(7) + "|" + Shapes.Effects() + "|" + Shapes.Shadowing(3);
            }
        }
        """;

    // Enclosing callables of other kinds: a void expression-bodied local with a
    // compound assignment (once silently lowered to `=`), a local in an async method,
    // a static local after the yields of an iterator, and a local using the
    // enclosing method's type parameter.
    private const string EdgeShapes = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public static class Probe
        {
            public static string Log = "";

            public static void Note()
            {
                void Write(string s) => Log += s;
                Write("a");
                Write("b");
            }

            public static async Task<int> Later()
            {
                int Twice(int x) => x * 2;
                await Task.Yield();
                return Twice(21);
            }

            public static IEnumerable<int> Seq(int n)
            {
                for (int i = 0; i < n; i++)
                    yield return Sq(i);

                static int Sq(int x) => x * x;
            }

            public static T Pick<T>(T a, T b)
            {
                T First(T x, T y) => x;
                return First(a, b);
            }

            public static string Run()
            {
                Note();
                int sum = 0;
                foreach (var v in Seq(4)) sum += v;
                return Log + "|" + Later().Result + "|" + sum + "|" + Pick("p", "q");
            }
        }
        """;

    // F3-LOCAL-05 (not fixtured at R0): an async local function stays preserved.
    private const string AsyncLocal = """
        using System.Threading.Tasks;

        public static class Probe
        {
            public static string Run()
            {
                int value = Compute().Result;
                return "" + value;
            }

            private static async Task<int> Compute()
            {
                async Task<int> Inner()
                {
                    await Task.Yield();
                    return 41;
                }
                return await Inner() + 1;
            }
        }
        """;

    // One native-shaped and one capturing local function in the same member: the
    // whole member is preserved, never half-converted.
    private const string MixedMember = """
        public static class Probe
        {
            public static int Mixed(int k)
            {
                int Plain(int x) => x + 1;
                int AddK(int x) => x + k;
                return Plain(1) + AddK(1);
            }

            public static string Run() => "" + Mixed(5);
        }
        """;

    private const string SamePointLambdas = """
        public static class Probe
        {
            private static string Log = "";
            private static int Visits;
            public static string Run()
            {
                static int Twice(int x) => x * 2;
                void Note(int x) { Log += x; Visits++; }
                int Widen(byte x) => x;
                int Effect(int x) { Visits++; return x + Visits; }
                Note(Twice(3));
                int value = Effect(Widen(2));
                return Log + ":" + value + ":" + Visits + ":" + Pick("first", "second");
            }
            private static T Pick<T>(T a, T b)
            {
                T First(T x, T y) => x;
                return First(a, b);
            }
        }
        """;

    private const string LambdaInvocationTiming = """
        public static class Probe
        {
            public static int Visits;
            public static string Run()
            {
                static int Bump() => ++Visits;
                int Post(int value) => value++;
                int before = Visits;
                return before + ":" + Bump() + ":" + Post(4) + ":" + Bump() + ":" + Visits;
            }
        }
        """;

    public static TheoryData<string, string, Surface> NativeRows()
    {
        var data = new TheoryData<string, string, Surface>();
        foreach (var surface in Enum.GetValues<Surface>())
        {
            data.Add("F3-LOCAL-02", "3|1002", surface);
            data.Add("same-point", "6:4:2:first", surface);
            data.Add("invocation-timing", "0:1:4:2:2", surface);
            data.Add("F3-LOCAL-01", "3|120", surface);
            data.Add("official-074", "1:1:1:2:479001600:1932053504:11", surface);
        }
        return data;
    }

    public static IEnumerable<object[]> Surfaces() =>
        Enum.GetValues<Surface>().Select(surface => new object[] { surface });

    [Fact]
    public void DirectSelfRecursion_UsesOnlyAnInitializedSelfBindingAndLambdaAssignment()
    {
        const string source = """
            public static class Probe
            {
                public static string Run()
                {
                    static int Factorial(int n)
                    {
                        if (n <= 1) return 1;
                        return n * Factorial(n - 1);
                    }
                    return Factorial(0) + ":" + Factorial(1) + ":" + Factorial(5);
                }
            }
            """;
        Assert.Equal("1:1:120", Run(source));
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues) + "\n" + converted.CalorSource);
        Assert.Empty(converted.Losses);
        Assert.DoesNotContain("§CS", converted.CalorSource);
        Assert.DoesNotContain("§RAW", converted.CalorSource);
        var originalBody = Assert.Single(converted.Ast!.Classes).Methods.Single(method => method.Name == "Run").Body;
        var binding = Assert.IsType<BindStatementNode>(originalBody[0]);
        Assert.True(binding.IsMutable);
        Assert.Null(binding.Initializer);
        var assignment = Assert.IsType<AssignmentStatementNode>(originalBody[1]);
        Assert.Equal(binding.Name, Assert.IsType<ReferenceNode>(assignment.Target).Name);
        Assert.False(Assert.IsType<LambdaExpressionNode>(assignment.Value).IsStatic);
        var compilation = Program.Compile(converted.CalorSource!, "direct-recursion.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join("\n", compilation.Diagnostics) + "\n" + converted.CalorSource);
        Assert.Contains("= default!;", compilation.GeneratedCode);
        Assert.Equal("1:1:120", Run(compilation.GeneratedCode));
    }

    public static TheoryData<bool, bool, int, int> RecursiveArithmeticCases()
    {
        var data = new TheoryData<bool, bool, int, int>();
        foreach (var isStatic in new[] { false, true })
        foreach (var expressionBody in new[] { false, true })
        foreach (var (input, expected) in new[]
                 { (-5, 1), (0, 1), (1, 1), (2, 2), (12, 479001600), (13, 1932053504) })
            data.Add(isStatic, expressionBody, input, expected);
        return data;
    }

    private static string RecursiveSource(bool isStatic, bool expressionBody) => $$"""
        public static class Probe
        {
            public static string Run(int n)
            {
                int before = 7;
                {{(isStatic ? "static " : "")}}int Factorial(int value)
                {{(expressionBody
                    ? "=> value <= 1 ? 1 : value * Factorial(value - 1);"
                    : "{ if (value <= 1) return 1; return value * Factorial(value - 1); }")}}
                return before + ":" + Factorial(n) + ":" + Factorial(2);
            }
        }
        """;

    [Theory]
    [MemberData(nameof(RecursiveArithmeticCases))]
    public void SelfRecursion_PreservesArithmeticDeclarationPointAndOnlyItsInternalCapture(
        bool isStatic, bool expressionBody, int input, int expected)
    {
        var source = RecursiveSource(isStatic, expressionBody);
        var expectedResult = $"7:{expected}:2";
        Assert.Equal(expectedResult, Run(source, false, input));
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Empty(converted.Losses);
        Assert.DoesNotContain(converted.Issues, issue => issue.Feature == "local-function");
        var body = Assert.Single(converted.Ast!.Classes).Methods.Single(method => method.Name == "Run").Body;
        Assert.Equal("before", Assert.IsType<BindStatementNode>(body[0]).Name);
        var self = Assert.IsType<BindStatementNode>(body[1]);
        Assert.Equal("Factorial", self.Name);
        Assert.True(self.IsMutable);
        Assert.Null(self.Initializer);
        Assert.Empty(Assert.IsType<EffectsNode>(self.Row).Effects);
        var assignment = Assert.IsType<AssignmentStatementNode>(body[2]);
        Assert.Equal(self.Name, Assert.IsType<ReferenceNode>(assignment.Target).Name);
        Assert.False(Assert.IsType<LambdaExpressionNode>(assignment.Value).IsStatic);
        Assert.DoesNotContain(RecursiveAstWalker.DescendantsAndSelf(converted.Ast),
            node => node is LocalFunctionStatementNode or CSharpInteropBlockNode or RawCSharpExpressionNode);

        var compiled = Program.Compile(converted.CalorSource!, "recursive-arithmetic.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics) + "\n" + converted.CalorSource);
        Assert.Equal(expectedResult, Run(compiled.GeneratedCode, false, input));
        var parsedSelf = RecursiveAstWalker.DescendantsAndSelf(compiled.Ast!)
            .OfType<BindStatementNode>().Single(bind => bind.Name == "Factorial");
        Assert.Empty(Assert.IsType<EffectsNode>(parsedSelf.Row).Effects);

        var tree = CSharpSyntaxTree.ParseText(compiled.GeneratedCode);
        var model = CSharpCompilation.Create("CaptureProof", [tree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).GetSemanticModel(tree);
        var lambda = Assert.Single(tree.GetRoot().DescendantNodes().OfType<LambdaExpressionSyntax>());
        var flow = model.AnalyzeDataFlow(lambda.Body)!;
        Assert.True(flow.Succeeded);
        Assert.Equal(["Factorial"], flow.CapturedInside.Select(symbol => symbol.Name));
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(12, 479001600)]
    [InlineData(13, 0)]
    public void SelfRecursion_GloballyCheckedSource_RemainsCheckedInsideTheLambda(int input, int expected)
    {
        var source = RecursiveSource(isStatic: true, expressionBody: false);
        var tree = CSharpSyntaxTree.ParseText(source);
        var original = CSharpCompilation.Create("CheckedRecursion", [tree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: true));
        var context = new ConversionContext { SourceFile = "checked-recursion.cs" };
        var ast = new RoslynSyntaxVisitor(context, original.GetSemanticModel(tree))
            .Convert(tree.GetCompilationUnitRoot(), "CheckedRecursion");
        Assert.False(context.HasErrors, string.Join("\n", context.Issues));
        Assert.Empty(context.Losses);
        var calor = new CalorEmitter().Emit(ast);
        Assert.Contains("overflow=checked", calor);
        var compiled = Program.Compile(calor, "checked-recursion.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics) + "\n" + calor);
        if (input == 13)
        {
            Assert.Throws<OverflowException>(() => Run(source, true, input));
            Assert.Throws<OverflowException>(() => Run(compiled.GeneratedCode, false, input));
        }
        else
        {
            Assert.Equal($"7:{expected}:2", Run(source, true, input));
            Assert.Equal(Run(source, true, input), Run(compiled.GeneratedCode, false, input));
        }
    }

    [Theory]
    [InlineData("return checked(value * Factorial(value - 1));")]
    [InlineData("checked { return value * Factorial(value - 1); }")]
    public void SelfRecursion_ExplicitOverflowRegionsRemainPreservedWithTheirExceptions(string recursiveReturn)
    {
        var source = $$"""
            public static class Probe
            {
                public static string Run(int n)
                {
                    static int Factorial(int value)
                    {
                        if (value <= 1) return 1;
                        {{recursiveReturn}}
                    }
                    return "" + Factorial(n);
                }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Contains(converted.Issues, issue => issue.Feature == "local-function");
        Assert.Contains(converted.Losses, loss => loss.Feature == "method"
            && loss.Kind == ConversionLossKind.InteropPreserved);
        Assert.Contains("§CSHARP", converted.CalorSource);
        var compiled = Program.Compile(converted.CalorSource!, "preserved-checked-recursion.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal("479001600", Run(source, false, 12));
        Assert.Equal(Run(source, false, 12), Run(compiled.GeneratedCode, false, 12));
        Assert.Throws<OverflowException>(() => Run(source, false, 13));
        Assert.Throws<OverflowException>(() => Run(compiled.GeneratedCode, false, 13));
    }

    [Theory]
    [InlineData("int L(int n) { Hits++; if (n <= 1) return 1; return n * L(n - 1); } return L(3);")]
    [InlineData("int L(int n) { Touch(); if (n <= 1) return 1; return n * L(n - 1); } return L(3);")]
    [InlineData("int L(int n) => n == 0 ? Hits : L(n - 1); return L(3);")]
    [InlineData("int L(int n) { int value = n; value++; return n == 0 ? value : n * L(n - 1); } return L(3);")]
    [InlineData("int L(int n) { ref int alias = ref n; return n <= 1 ? 1 : n * L(n - 1); } return L(3);")]
    [InlineData("int L(int n) => n == 0 ? 1 : L(n - 1) + new Tag(); return L(3);")]
    [InlineData("dynamic L(dynamic n) => n <= 1 ? 1 : n * L(n - 1); return (int)L(3);")]
    [InlineData("int L(int n) { new object(); return n <= 1 ? 1 : n * L(n - 1); } return L(3);")]
    [InlineData("int L(int n) { if (n < 0) throw new System.ArgumentException(); return n <= 1 ? 1 : n * L(n - 1); } return L(3);")]
    [InlineData("return L(3); int L(int n) => n <= 1 ? 1 : n * L(n - 1);")]
    [InlineData("int L(int n) => n <= 1 ? 1 : n * L(n - 1); System.Func<int,int> f = L; return f(3);")]
    [InlineData("int L(int n) => n <= 1 ? 1 : n * L(n - 1); string name = nameof(L); return L(3) + name.Length;")]
    [InlineData("int L(int n) { System.Func<int> again = () => L(n - 1); return n <= 1 ? 1 : n * again(); } return L(3);")]
    [InlineData("int k = 2; int L(int n) => n == 0 ? k : n * L(n - 1); return L(3);")]
    [InlineData("int A(int n) => n <= 1 ? 1 : n * B(n - 1); int B(int n) => n <= 1 ? 1 : n * A(n - 1); return A(3);")]
    [InlineData("int L(int n = 1) => n <= 1 ? 1 : n * L(n - 1); return L(3);")]
    [InlineData("int L(int n) => n <= 1 ? 1 : n * L(n: n - 1); return L(3);")]
    [InlineData("int L(int n) { if (n <= 1) { int L = 1; return L; } return n * L(n - 1); } return L(3);")]
    [InlineData("int L(int n) => n <= 1 ? 1 : n * L(n - 1); return 42;")]
    public void SelfRecursion_WholeBodyProofAndExcludedUsesRemainCountedPreservation(string body)
    {
        var source = $$"""
            public class C
            {
                public static int Hits;
                public static int Touch() => ++Hits;
                public struct Tag
                {
                    public static int operator +(int value, Tag tag) { Hits++; return value + 1; }
                }
                public int M() { {{body}} }
            }
            public static class Probe
            {
                public static string Run() => new C().M() + ":" + C.Hits;
            }
            """;
        var expected = Run(source);
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Contains(converted.Issues, issue => issue.Feature == "local-function");
        Assert.Contains(converted.Losses, loss => loss.Feature == "method"
            && loss.Kind == ConversionLossKind.InteropPreserved);
        var compiled = Program.Compile(converted.CalorSource!, "preserved-recursion.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, UnknownCallPolicy = UnknownCallPolicy.Permissive });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics) + "\n" + converted.CalorSource);
        Assert.Equal(expected, Run(compiled.GeneratedCode));
    }

    [Fact]
    public void SelfRecursion_StaticCaptureErrorsCannotBecomeLegalCapturingLambdas()
    {
        const string source = """
            public static class Probe
            {
                public static int Run(int outer)
                {
                    static int Factorial(int value) => value <= 1 ? outer : value * Factorial(value - 1);
                    return Factorial(3);
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("StaticCapture", [tree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS8421");
        var context = new ConversionContext { SourceFile = "static-capture.cs" };
        var ast = new RoslynSyntaxVisitor(context, compilation.GetSemanticModel(tree))
            .Convert(tree.GetCompilationUnitRoot(), "StaticCapture");
        Assert.Contains(context.Issues, issue => issue.Feature == "local-function");
        Assert.Contains(context.Losses, loss => loss.Feature == "method"
            && loss.Kind == ConversionLossKind.InteropPreserved);
        Assert.DoesNotContain(RecursiveAstWalker.DescendantsAndSelf(ast), node => node is LambdaExpressionNode);
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task PreservedInitializerInsideLambda_RunsAtInvocation_NotDeclaration(Surface surface)
    {
        const string source = """
            public static class Probe
            {
                public static int Constructions;
                public sealed class Box
                {
                    public int Value;
                    public Box() { Constructions++; }
                }
                public static string Run()
                {
                    static int Fresh() => new Box { Value = 2 }.Value;
                    int before = Constructions;
                    return before + ":" + Fresh() + ":" + Fresh() + ":" + Constructions;
                }
            }
            """;
        const string expected = "0:2:2:2";
        Assert.Equal(expected, Run(source));
        var (calor, losses) = await ConvertAsync(source, surface);
        Assert.Equal(["initializer-interop"], losses);
        Assert.DoesNotContain("§CSHARP", calor);
        Assert.Contains("§CS{", calor);
        Assert.Contains("§LAM{", calor);
        var compilation = Program.Compile(calor, "initializer-lambda.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Theory]
    [MemberData(nameof(NativeRows))]
    public async Task LocalFunctions_ConvertNatively_CompileByDefault_AndRunTheSame(
        string row, string expected, Surface surface)
    {
        var source = row switch
        {
            "more-shapes" => MoreShapes,
            "edge-shapes" => EdgeShapes,
            "same-point" => SamePointLambdas,
            "invocation-timing" => LambdaInvocationTiming,
            "official-074" => File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
                "tests/TestData/CSharpImport/Level4_Methods/074_LocalFunctions.cs")),
            _ => R0Fixture(row)
        };
        Assert.Equal(expected, row == "official-074" ? RunOfficialScorecard(source) : Run(source));

        var (calor, losses) = await ConvertAsync(source, surface);

        Assert.DoesNotContain("§CSHARP", calor);
        Assert.DoesNotContain("§CS{", calor);
        Assert.Empty(losses);
        Assert.Contains("§LAM{", calor);

        // Default compile: effects enforced, no --permissive-effects.
        var compilation = Program.Compile(calor, row + ".calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Contains("static (", compilation.GeneratedCode);
        Assert.DoesNotContain(RecursiveAstWalker.DescendantsAndSelf(compilation.Ast!),
            node => node is LocalFunctionStatementNode);
        Assert.Equal(expected, row == "official-074"
            ? RunOfficialScorecard(compilation.GeneratedCode) : Run(compilation.GeneratedCode));
    }

    public static TheoryData<string, string, int, Surface> PreservedRows()
    {
        var data = new TheoryData<string, string, int, Surface>();
        foreach (var surface in Enum.GetValues<Surface>())
        {
            data.Add("F3-LOCAL-04", "5|g3|2", 3, surface);
            data.Add("F3-LOCAL-05", "42", 1, surface);
            data.Add("mixed", "8", 1, surface);
            data.Add("F3-LOCAL-03", "6|5|60|3000|502", 1, surface);
            data.Add("more-shapes", "11|False|xy6|16", 2, surface);
            data.Add("edge-shapes", "ab|42|14|p", 1, surface);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PreservedRows))]
    public async Task OutsideTheSlice_IsPreservedAsInterop_AndRunsTheSame(
        string row, string expected, int preservedMembers, Surface surface)
    {
        var source = row switch
        {
            "F3-LOCAL-05" => AsyncLocal,
            "mixed" => MixedMember,
            "more-shapes" => MoreShapes,
            "edge-shapes" => EdgeShapes,
            _ => R0Fixture(row)
        };
        Assert.Equal(expected, Run(source));

        var (calor, losses) = await ConvertAsync(source, surface);

        // Honest: each containing member is a counted interop loss, never a native §F.
        Assert.Equal(preservedMembers, Regex.Matches(calor, @"§CSHARP\{").Count);
        Assert.Equal(preservedMembers, losses.Count(feature => feature == "method"));
        Assert.DoesNotContain("post-validation-fallback", losses);

        // The callers of preserved members charge assumed effects (R0 baseline),
        // so behaviour is compared under --permissive-effects, as at R0.
        var compilation = Program.Compile(calor, row + ".calr", new CompilationOptions
        {
            StatusWriter = TextWriter.Null,
            UnknownCallPolicy = UnknownCallPolicy.Permissive
        });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.DoesNotContain(RecursiveAstWalker.DescendantsAndSelf(compilation.Ast!),
            node => node is LocalFunctionStatementNode);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("F3-LOCAL-04")]
    [InlineData("F3-LOCAL-05")]
    [InlineData("F3-LOCAL-03")]
    public void NoFallback_RefusesOutsideTheSlice_ByName(string row)
    {
        var result = new CSharpToCalorConverter(NoFallbackOptions())
            .Convert(row == "F3-LOCAL-05" ? AsyncLocal : R0Fixture(row));
        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Feature == "local-function"
            && issue.Severity == ConversionIssueSeverity.Error);
    }

    [Theory]
    [InlineData("F3-LOCAL-01")]
    [InlineData("F3-LOCAL-02")]
    public void NoFallback_AcceptsTheSlice(string row)
    {
        var result = new CSharpToCalorConverter(NoFallbackOptions()).Convert(R0Fixture(row));
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain(result.Issues, issue => issue.Feature == "local-function");
    }

    [Theory]
    // Implicit and explicit `this`, an outer local, an outer parameter through a lambda.
    [InlineData("private int _f = 1; public int M() { int L() => _f; return L(); }")]
    [InlineData("private int _f = 1; public int M() { int L() => this._f; return L(); }")]
    [InlineData("public int Inst() => 1; public int M() { int L() => Inst(); return L(); }")]
    [InlineData("public int M() { int k = 2; int L() => k; return L(); }")]
    [InlineData("public int M(int p) { int L() { System.Func<int> f = () => p; return f(); } return L(); }")]
    // Placement and shape outside the slice.
    [InlineData("public int M(bool b) { if (b) { int L() => 1; return L(); } return 0; }")]
    [InlineData("public int M() { int L() { int N() => 1; return N(); } return L(); }")]
    [InlineData("public int M() { int L(int x = 1) => x; return L(); }")]
    [InlineData("public int M() { int v = 1; int L(ref int x) => x; return L(ref v); }")]
    [InlineData("public int M() { static T L<T>(T x) => x; return L(1); }")]
    [InlineData("public C() { int L() => 1; L(); }")]
    [InlineData("public int M() { return L(1); int L(int x) => x; }")]
    [InlineData("public int M() { int A(int x) => B(x); int B(int x) => x; return A(1); }")]
    [InlineData("public int M() { int L(int x) => x; return L(x: 1); }")]
    [InlineData("public int M() { int L(int x) => x; System.Func<int,int> f = L; return f(1); }")]
    [InlineData("public int M() { int L(int x) => x; System.Func<int> f = () => L(1); return f(); }")]
    [InlineData("public int M() { int L(params int[] x) => x.Length; return L(1); }")]
    [InlineData("public int M() { int L(System.Span<int> x) => x.Length; return L(new int[1]); }")]
    [InlineData("public int M() { int L() => 9; return 1; }")]
    // Method-group uses where Calor does not check the effect row (Codex round 3).
    [InlineData("public System.Func<int, int>[] M() { int L(int x) => x; return new System.Func<int, int>[] { L }; }")]
    [InlineData("public System.Func<int, int> M(System.Func<int, int> f) { int L(int x) => x; return f ?? L; }")]
    [InlineData("public static System.Func<int, int> S; public void M() { int L(int x) => x; C.S = L; }")]
    public void CapturingOrUnsupportedShapes_PreserveTheMember(string members)
    {
        var result = new CSharpToCalorConverter().Convert("public class C\n{\n" + members + "\n}\n");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(result.Issues, issue => issue.Feature == "local-function");
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.DoesNotMatch(@"\n {6,}§F\{", result.CalorSource!);
    }

    [Theory]
    // Names that look like captures but are not: a member access on a parameter,
    // an object-initializer member, a named argument, a static member, a static local.
    [InlineData("public int M() { int L(System.Text.StringBuilder sb) => sb.Length; return L(new System.Text.StringBuilder(\"ab\")); }", true)]
    // Compiled elsewhere: a nested type's constructor is an unknown call to the effect pass.
    [InlineData("public class P { public int X; } public int M() { int L() => new P { X = 2 }.X; return L(); }", false)]
    [InlineData("public static int S(int value) => value; public int M() { int L() => S(value: 3); return L(); }", true)]
    [InlineData("private static int _s = 4; public int M() { int L() => _s; return L(); }", true)]
    [InlineData("private int _f = 1; public int M() { static int L(int x) => x; return L(_f); }", true)]
    public void NonCapturingLocalDeclaration_LowersWithoutCaptureOrRebinding(string members, bool compile)
    {
        var source = "public class C\n{\n" + members + "\n}\n";
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain(result.Issues, issue => issue.Feature == "local-function");
        Assert.DoesNotContain("§CSHARP", result.CalorSource);
        Assert.Contains("§LAM{", result.CalorSource!);
        Assert.DoesNotMatch(@"\n {6,}§F\{f\d+:L\}", result.CalorSource!);
        if (!compile)
        {
            Assert.Contains(result.Losses, loss =>
                loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == "initializer-interop");
            Assert.Contains("§CS{", result.CalorSource);
            return;
        }
        Assert.Empty(result.Losses);
        var compilation = Program.Compile(result.CalorSource!, "shape.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join("\n", compilation.Diagnostics.Errors) + "\n" + result.CalorSource);
    }

    [Fact]
    public void ExpressionBodiedCompoundAssignment_KeepsItsOperator()
    {
        // Found while testing F3: `=> Log += s` on a method was converted to `Log = s`.
        var source = """
            public static class Probe
            {
                public static string Log = "";
                public static void Write(string s) => Log += s;
                public static string Run()
                {
                    Write("a");
                    Write("b");
                    return Log;
                }
            }
            """;
        Assert.Equal("ab", Run(source));
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var compilation = Program.Compile(result.CalorSource!, "compound.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("ab", Run(compilation.GeneratedCode));
    }

    [Fact]
    public void Registry_AdvertisesThePartialSlice()
    {
        var info = FeatureSupport.GetFeatureInfo("local-function")!;
        Assert.Equal(SupportLevel.Partial, info.Support);
        Assert.Contains("non-capturing", info.Description);
        Assert.Contains("§CSHARP", info.Description);
    }

    // ---- Codex review round 1 regression (converter) ----

    [Fact]
    public void Review1_UnresolvedNames_AreNotProofOfNoCapture()
    {
        // `_f` is declared in another part of the partial class, invisible here.
        var result = new CSharpToCalorConverter().Convert(
            "public partial class C { public int M() { int L() => _f; return L(); } }");
        Assert.Contains(result.Issues, issue => issue.Feature == "local-function");
        Assert.DoesNotMatch(@"\n {6,}§F\{", result.CalorSource ?? "");
    }

    // ---- harness ----

    private static ConversionOptions NoFallbackOptions() => ConvertCommand.BuildCSharpToCalorOptions(
        benchmark: false, verbose: false, explain: false, noFallback: true,
        passthrough: false, explicitCallClosers: false);

    private static string R0Fixture(string id) => File.ReadAllText(Path.Combine(
        CliTestHarness.FindRepoRoot(), R0Fixtures, id + ".cs.txt"));

    /// <summary>Converts on one surface; returns the Calor and the loss features.</summary>
    private static async Task<(string Calor, List<string> Losses)> ConvertAsync(string source, Surface surface)
    {
        switch (surface)
        {
            case Surface.LibraryDefault:
                return Library(new ConversionOptions());
            case Surface.CliDefault or Surface.CliPassthrough:
            {
                var dir = Directory.CreateTempSubdirectory("calor-local-lambda-").FullName;
                try
                {
                    File.WriteAllText(Path.Combine(dir, "Fixture.cs"), source);
                    var flags = surface == Surface.CliPassthrough ? new[] { "--passthrough" } : [];
                    var (exit, stdout, stderr) = CliTestHarness.RunCli(dir,
                        ["convert", "Fixture.cs", "-o", "Fixture.calr", "--format", "json", .. flags]);
                    Assert.True(exit == 0, stdout + "\n" + stderr);
                    using var document = JsonDocument.Parse(stdout);
                    var data = document.RootElement.GetProperty("data");
                    var losses = data.TryGetProperty("losses", out var locations)
                        ? locations.EnumerateArray().Select(loss => loss.GetProperty("feature").GetString()!).ToList()
                        : [];
                    Assert.Equal(data.GetProperty("lossCount").GetInt32(), losses.Count);
                    Assert.Equal(losses.Count == 0 ? "native" : "preserved",
                        data.GetProperty("conversionPaths").GetProperty("outcome").GetString());
                    return (File.ReadAllText(Path.Combine(dir, "Fixture.calr")), losses);
                }
                finally
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            case Surface.McpDefault or Surface.McpPassthroughOnError or Surface.McpPassthroughOnErrorModuleName:
            {
                var args = new Dictionary<string, object> { ["source"] = source };
                if (surface != Surface.McpDefault)
                    args["passthroughOnError"] = true;
                if (surface == Surface.McpPassthroughOnErrorModuleName)
                    args["moduleName"] = "Custom";
                var result = await new ConvertTool().ExecuteAsync(
                    JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement);
                Assert.False(result.IsError, result.Content[0].Text);
                using var payload = JsonDocument.Parse(result.Content[0].Text!);
                var root = payload.RootElement;
                var losses = root.GetProperty("lossSummary").GetProperty("locations").EnumerateArray()
                    .Select(location => location.GetProperty("feature").GetString()!).ToList();
                return (root.GetProperty("calorSource").GetString()!, losses);
            }
            default:
            {
                var dir = Path.Combine(Path.GetTempPath(), "calor-f3-847-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    File.WriteAllText(Path.Combine(dir, "Fixture.cs"), source);
                    var migrator = new ProjectMigrator(new MigrationPlanOptions
                    {
                        Parallel = false,
                        SkipAnalyze = true,
                        SkipVerify = true,
                        PassthroughOnError = surface == Surface.ProjectMigratePassthrough
                    });
                    var plan = await migrator.CreatePlanAsync(dir, MigrationDirection.CSharpToCalor);
                    var report = await migrator.ExecuteAsync(plan);
                    var file = Assert.Single(report.FileResults);
                    Assert.NotEqual(FileMigrationStatus.Failed, file.Status);
                    return (File.ReadAllText(file.OutputPath!), file.Losses.Select(loss => loss.Feature).ToList());
                }
                finally
                {
                    try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
                }
            }
        }

        (string, List<string>) Library(ConversionOptions options)
        {
            var result = new CSharpToCalorConverter(options).Convert(source);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
            return (result.CalorSource!, result.Losses.Select(loss => loss.Feature).ToList());
        }
    }

    /// <summary>Compiles C# in memory and returns <c>Probe.Run()</c>.</summary>
    private static string Run(string csharp, bool checkOverflow = false, params object[] arguments)
        => RunAssembly(csharp, checkOverflow, assembly =>
        {
            var probe = assembly.GetTypes().Single(type => type.Name == "Probe");
            var run = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            return Assert.IsType<string>(run.Invoke(null, arguments));
        });

    private static string RunOfficialScorecard(string csharp) => RunAssembly(csharp, false, assembly =>
    {
        var calculator = assembly.GetTypes().Single(type => type.Name == "Calculator");
        var factorial = calculator.GetMethod("Factorial", BindingFlags.Public | BindingFlags.Static)!;
        var values = new[] { -5, 0, 1, 2, 12, 13 }
            .Select(input => Assert.IsType<int>(factorial.Invoke(null, [input])));
        var calculate = calculator.GetMethod("Calculate", BindingFlags.Public | BindingFlags.Static)!;
        return string.Join(":", values) + ":" + Assert.IsType<int>(calculate.Invoke(null, [2, 3]));
    });

    private static string RunAssembly(string csharp, bool checkOverflow, Func<Assembly, string> invoke)
    {
        var compilation = CSharpCompilation.Create("F3_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(csharp)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: checkOverflow));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics) + "\n" + csharp);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            try
            {
                return invoke(assembly);
            }
            catch (TargetInvocationException invocation) when (invocation.InnerException != null)
            {
                throw invocation.InnerException;
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
