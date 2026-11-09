using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class NullCoalescingTests
{
    [Theory]
    [InlineData("null", "null")]
    [InlineData("\"\"", "0")]
    [InlineData("\"abc\"", "3")]
    public void Original095_NativeRoundTrip_PreservesBranchesAndRefStores(string receiver, string expected)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Directory.Build.props")))
            root = Path.GetDirectoryName(root)!;
        var path = Path.Combine(root, "tests", "TestData", "CSharpImport", "Level5_Advanced", "095_NullCoalescing.cs");
        var bytes = File.ReadAllBytes(path);
        var source = File.ReadAllText(path);
        var witness = $$"""
            public static class Witness
            {
                public static string Run()
                {
                    if (NullCoalescing.NullHandling.GetOrDefault(null, "fallback") != "fallback"
                        || NullCoalescing.NullHandling.GetOrDefault("kept", "fallback") != "kept"
                        || NullCoalescing.NullHandling.GetOrZero(null) != 0
                        || NullCoalescing.NullHandling.GetOrZero(7) != 7
                        || NullCoalescing.NullHandling.GetSafe(null) != ""
                        || NullCoalescing.NullHandling.GetSafe("kept") != "kept"
                        || NullCoalescing.NullHandling.GetChained(1, 2, 3) != 1
                        || NullCoalescing.NullHandling.GetChained(null, 2, 3) != 2
                        || NullCoalescing.NullHandling.GetChained(null, null, 3) != 3
                        || NullCoalescing.NullHandling.GetChained(null, null, null) != null)
                        throw new System.Exception("coalescing branch changed");
                    string absent = null;
                    string present = "kept";
                    string empty = "";
                    string stillNull = null;
                    NullCoalescing.NullHandling.SetIfNull(ref absent, "assigned");
                    NullCoalescing.NullHandling.SetIfNull(ref present, "ignored");
                    NullCoalescing.NullHandling.SetIfNull(ref empty, "ignored");
                    NullCoalescing.NullHandling.SetIfNull(ref stillNull, null);
                    if (absent != "assigned" || present != "kept" || empty != "" || stillNull != null)
                        throw new System.Exception("ref store changed");
                    return NullCoalescing.NullHandling.GetLength({{receiver}});
                }
            }
            """;
        AssertNativeRuntimeAndReemission(source, witness, expected, Path.GetFileName(path));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false, "fallback|1|0|1")]
    [InlineData(true, "3|1|0|0")]
    public void NativeConditionalCallChain_EvaluatesReceiverOnceAndFallbackLazily(bool present, string expected)
    {
        var source = """
            public static class Probe
            {
                private static int Reads;
                private static int Arguments;
                private static int Fallbacks;
                private static string? Target(bool present) { Reads++; return present ? "abc" : null; }
                private static string Fallback() { Fallbacks++; return "fallback"; }
                public static string Run(bool present)
                {
                    Reads = 0;
                    Arguments = 0;
                    Fallbacks = 0;
                    var value = Target(present)?.Length.ToString() ?? Fallback();
                    return value + "|" + Reads + "|" + Arguments + "|" + Fallbacks;
                }
            }
            """;
        AssertNativeRuntimeAndReemission(source,
            $"public static class Witness {{ public static string Run() => Probe.Run({present.ToString().ToLowerInvariant()}); }}",
            expected);
    }

    [Theory]
    [InlineData(false, "fallback|1|0|1")]
    [InlineData(true, "bc|1|1|0")]
    public void NativeConditionalCallArguments_StayInsideTheConditionalRegion(bool present, string expected)
    {
        var source = """
            public static class Probe
            {
                private static int Reads;
                private static int Arguments;
                private static int Fallbacks;
                private static string? Target(bool present) { Reads++; return present ? "abc" : null; }
                private static int Argument() { Arguments++; return 1; }
                private static string Fallback() { Fallbacks++; return "fallback"; }
                public static string Run(bool present)
                {
                    Reads = 0;
                    Arguments = 0;
                    Fallbacks = 0;
                    var value = Target(present)?.Substring(Argument()).ToString() ?? Fallback();
                    return value + "|" + Reads + "|" + Arguments + "|" + Fallbacks;
                }
            }
            """;
        AssertNativeRuntimeAndReemission(source,
            $"public static class Witness {{ public static string Run() => Probe.Run({present.ToString().ToLowerInvariant()}); }}",
            expected);
    }

    [Theory]
    [InlineData(false, "fallback")]
    [InlineData(true, "System.InvalidOperationException:argument")]
    public void NativeConditionalArguments_PreserveExceptionLaziness(bool present, string expected)
    {
        const string source = """
            public static class Probe
            {
                private static int Argument() { throw new System.InvalidOperationException("argument"); }
                public static string Run(bool present)
                {
                    string? value = present ? "abc" : null;
                    return value?.Substring(Argument()) ?? "fallback";
                }
            }
            """;
        var witness = $$"""
            public static class Witness
            {
                public static string Run()
                {
                    try { return Probe.Run({{present.ToString().ToLowerInvariant()}}); }
                    catch (System.Exception exception) { return exception.GetType().FullName + ":" + exception.Message; }
                }
            }
            """;
        AssertNativeRuntimeAndReemission(source, witness, expected);
    }

    [Theory]
    [InlineData("null", "assigned|1")]
    [InlineData("\"kept\"", "kept|0")]
    public void NativeCoalescingStore_EvaluatesRightOnlyWhenNull(string initial, string expected)
    {
        var source = $$"""
            public static class Probe
            {
                private static int Calls;
                private static string Right() { Calls++; return "assigned"; }
                public static string Run()
                {
                    Calls = 0;
                    string value = {{initial}};
                    value ??= Right();
                    return value + "|" + Calls;
                }
            }
            """;
        AssertNativeRuntimeAndReemission(source,
            "public static class Witness { public static string Run() => Probe.Run(); }", expected);
    }

    [Theory]
    [InlineData("property", false, "assigned|1|0|1|1|1")]
    [InlineData("property", true, "kept|1|0|1|0|0")]
    [InlineData("indexer", false, "assigned|1|1|1|1|1")]
    [InlineData("indexer", true, "kept|1|1|1|0|0")]
    [InlineData("implicit", false, "assigned|0|0|1|1|1")]
    [InlineData("implicit", true, "kept|0|0|1|0|0")]
    [InlineData("field", false, "assigned|0|0|0|0|1")]
    [InlineData("field", true, "kept|0|0|0|0|0")]
    public void NativeCoalescingAccessors_PreserveReceiverGetterSetterAndRhsCounts(
        string target, bool populated, string expected)
    {
        var assignment = target switch
        {
            "property" => "Get(box).Value ??= Right();",
            "indexer" => "Get(box)[Index()] ??= Right();",
            "implicit" => "box.Assign();",
            "field" => "box.AssignField();",
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        var source = $$"""
            public sealed class Box
            {
                public string? Field;
                public int Gets;
                public int Sets;
                public string? Value
                {
                    get { Gets++; return Field; }
                    set { Sets++; Field = value; }
                }
                public string? this[int index]
                {
                    get { Gets++; return Field; }
                    set { Sets++; Field = value; }
                }
                public void Assign() { Value ??= Probe.Right(); }
                public void AssignField() { Field ??= Probe.Right(); }
            }
            public static class Probe
            {
                private static int Receivers;
                private static int Indices;
                private static int Rights;
                private static Box Get(Box box) { Receivers++; return box; }
                private static int Index() { Indices++; return 0; }
                public static string Right() { Rights++; return "assigned"; }
                public static string Run()
                {
                    Receivers = 0;
                    Indices = 0;
                    Rights = 0;
                    var box = new Box();
                    box.Field = {{(populated ? "\"kept\"" : "null")}};
                    {{assignment}}
                    return box.Field + "|" + Receivers + "|" + Indices + "|" + box.Gets + "|" + box.Sets + "|" + Rights;
                }
            }
            """;
        AssertNativeRuntimeAndReemission(source,
            "public static class Witness { public static string Run() => Probe.Run(); }", expected);
    }

    [Fact]
    public void UnprovedCoalescingStoreNodes_AreNotRewrittenAsUnconditionalStores()
    {
        var receiver = new CallExpressionNode(default, "Receiver", []);
        ExpressionNode[] targets =
        [
            new ReferenceNode(default, "Value"),
            new FieldAccessNode(default, receiver, "Value"),
            new ArrayAccessNode(default, receiver, new CallExpressionNode(default, "Index", []))
        ];
        foreach (var target in targets)
        {
            var store = new CompoundAssignmentStatementNode(default, target,
                CompoundAssignmentOperator.NullCoalesce, new StringLiteralNode(default, "assigned"));
            var error = Assert.Throws<NotSupportedException>(() => store.Accept(new CalorEmitter()));
            Assert.Contains("declaration-proven lowering", error.Message);
        }
    }

    private static void AssertNativeRuntimeAndReemission(
        string source, string witness, string expected, string sourceFile = "NullCoalescingNative.cs")
    {
        Assert.Equal(expected, ExecuteNativeWitness(source, witness));
        var conversion = new CSharpToCalorConverter().Convert(source, sourceFile);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.True(conversion.Losses.Count == 0,
            string.Join("\n", conversion.Losses) + "\n" + string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.NotNull(conversion.CalorSource);
        Assert.DoesNotContain("§", conversion.CalorSource);
        Assert.DoesNotContain("raw ", conversion.CalorSource);
        Assert.DoesNotContain("TODO", conversion.CalorSource);
        var parsed = Parse(conversion.CalorSource, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var reemitted = new CalorEmitter().Emit(parsed);
        Assert.DoesNotContain("§", reemitted);
        foreach (var text in new[] { conversion.CalorSource, reemitted })
        {
            var compiled = Program.Compile(text, "null-coalescing-native.calr",
                new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(expected, ExecuteNativeWitness(compiled.GeneratedCode, witness));
        }
    }

    private static string ExecuteNativeWitness(string source, string witness)
    {
        var compilation = CSharpCompilation.Create("NullCoalescingNative_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(witness)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var run = context.LoadFromStream(stream).GetType("Witness")!.GetMethod("Run")!;
            return Assert.IsType<string>(run.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body, string parameters = "", string returnType = "object")
    {
        return $$"""
            §M{Test}
              §F{Main:pub}
                  {{parameters}}
                  §O{{{returnType}}}
                  {{body}}
            """;
    }

    [Fact]
    public void Parse_NullCoalescing_CreatesNullCoalesceNode()
    {
        var source = WrapInFunction(
            "§R (?? a b)",
            "§I{object:a}\n      §I{object:b}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var nullCoalesce = returnStmt!.Expression as NullCoalesceNode;
        Assert.NotNull(nullCoalesce);
        Assert.IsType<ReferenceNode>(nullCoalesce!.Left);
        Assert.IsType<ReferenceNode>(nullCoalesce.Right);
        Assert.Equal("a", ((ReferenceNode)nullCoalesce.Left).Name);
        Assert.Equal("b", ((ReferenceNode)nullCoalesce.Right).Name);
    }

    [Fact]
    public void Emit_NullCoalescing_ProducesCSharpOperator()
    {
        var source = WrapInFunction(
            "§R (?? a b)",
            "§I{object:a}\n      §I{object:b}");
        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join(", ", result.Diagnostics.Select(d => d.Message)));
        Assert.Contains("a ?? b", result.GeneratedCode);
    }

    [Fact]
    public void Parse_ChainedNullCoalescing_CreatesNestedNodes()
    {
        var source = WrapInFunction(
            "§R (?? a (?? b c))",
            "§I{object:a}\n      §I{object:b}\n      §I{object:c}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var outer = returnStmt!.Expression as NullCoalesceNode;
        Assert.NotNull(outer);
        Assert.IsType<ReferenceNode>(outer!.Left);
        var inner = outer.Right as NullCoalesceNode;
        Assert.NotNull(inner);
        Assert.Equal("b", ((ReferenceNode)inner!.Left).Name);
        Assert.Equal("c", ((ReferenceNode)inner.Right).Name);
    }

    [Fact]
    public void Emit_ChainedNullCoalescing_ProducesChainedCSharp()
    {
        var source = WrapInFunction(
            "§R (?? a (?? b c))",
            "§I{object:a}\n      §I{object:b}\n      §I{object:c}");
        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join(", ", result.Diagnostics.Select(d => d.Message)));
        Assert.Contains("a ?? b ?? c", result.GeneratedCode);
    }

    [Fact]
    public void CSharpRoundTrip_NullCoalescing_GetOrDefault()
    {
        var csharp = @"
public static class NullHandling
{
    public static string GetOrDefault(string value, string defaultValue)
    {
        return value ?? defaultValue;
    }
    public static int GetOrZero(int? value)
    {
        return value ?? 0;
    }
    public static int? GetChained(int? a, int? b, int? c)
    {
        return a ?? b ?? c;
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy
        });
        var convResult = converter.Convert(csharp, "Test.cs");
        Assert.True(convResult.Success, "C# to Calor conversion should succeed: " +
            string.Join(", ", convResult.Issues.Select(i => i.Message)));
        Assert.NotNull(convResult.CalorSource);

        // The Calor source should contain null-coalescing operator
        Assert.Contains("??", convResult.CalorSource!);

        // Now compile the Calor source back to C#
        var compResult = Program.Compile(convResult.CalorSource!);
        Assert.False(compResult.HasErrors,
            "Calor to C# compilation should succeed: " +
            string.Join(", ", compResult.Diagnostics.Select(d => d.Message)));

        // Verify the generated C# contains null-coalescing
        Assert.Contains("??", compResult.GeneratedCode);
    }

    [Fact]
    public void NullableParameterType_ParsesCorrectly()
    {
        var source = WrapInFunction(
            "§R (?? value 0)",
            "§I{?i32:value}",
            "i32");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.Single(func.Parameters);
        // ?i32 expands to NULLABLE[inner=INT] which becomes int?
        Assert.Equal("NULLABLE[inner=INT]", func.Parameters[0].TypeName);
    }
}
