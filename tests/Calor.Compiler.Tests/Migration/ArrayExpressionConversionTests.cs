using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.25 F5 (#1132): array expressions convert natively and keep C# evaluation
/// semantics. Each registered R0 row (F5-ARRAY-01..05) and each extra witness is
/// converted on every surface, compiled, run, and compared with the original C#. The
/// witnesses log every side effect, so a reordered, duplicated, skipped or eagerly
/// evaluated subexpression changes the result. Nothing may be preserved as C# (no rescue,
/// no raw preservation) on a native row. Rows retain the original unknown-effect observations.
/// Separate current observations require enforced compilation and exact metadata binding for
/// the repaired rows and explicitly registered migration surfaces. Other rows retain their
/// recorded Calor0410 expectation for 'unknown'.
/// </summary>
public class ArrayExpressionConversionTests
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

    public static IEnumerable<object[]> SizedArraySurfaces() =>
        Enum.GetValues<Surface>().Select(surface => new object[] { surface });

    [Theory]
    [InlineData("object[]", "new object[2]", false)]
    [InlineData("object[]", "new object[2]", true)]
    [InlineData("object?[]", "new object[2]", false)]
    [InlineData("object?[]", "new object[2]", true)]
    [InlineData("object[,]", "new object[2, 3]", false)]
    [InlineData("object[,]", "new object[2, 3]", true)]
    [InlineData("object?[,]", "new object[2, 3]", false)]
    [InlineData("object?[,]", "new object[2, 3]", true)]
    [InlineData("object[][]", "new object[2][]", false)]
    [InlineData("object[][]", "new object[2][]", true)]
    [InlineData("object?[][]", "new object[2][]", false)]
    [InlineData("object?[][]", "new object[2][]", true)]
    public void PredefinedObjectArrays_KeepClrIdentityAcrossNamespaces(
        string type, string allocation, bool namespaced)
    {
        var source = $$"""
            module PredefinedObjectArrays{{(namespaced ? " Custom" : "")}}
              class @object
              module P
                func Read public ({{type}}:values) → int
                  !
                  return values.GetLength(0)
            """;
        var compiled = Program.Compile(source, "predefined-object-arrays.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
        var bound = new Calor.Compiler.Binding.Binder(new DiagnosticBag()).Bind(compiled.Ast!);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(Assert.Single(bound.Functions).Body)).Expression);
        Assert.Equal(SpecialType.System_Array, call.ResolvedMetadataMethod?.ContainingType.SpecialType);
        var array = Assert.IsType<ArrayBoundType>(call.Receiver!.MethodInputArrayType);
        while (array.ElementType is ArrayBoundType nested)
            array = nested;
        var element = Assert.IsType<NominalBoundType>(array.ElementType);
        Assert.Equal(SpecialType.System_Object, element.RoslynSymbol?.SpecialType);
        Assert.Null(element.Declaration);
        Assert.Equal(type.StartsWith("object?", StringComparison.Ordinal)
                ? Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Annotated
                : Calor.Compiler.Binding.BoundTypes.NullableAnnotation.NotAnnotated,
            element.NullableAnnotation);
        var caller = "public static class Probe { public static string Run() => "
            + (namespaced ? "Custom." : "") + "P.Read(" + allocation + ").ToString(); }";
        Assert.Equal("2", Run(compiled.GeneratedCode, caller));
    }

    [Theory]
    [InlineData("values.GetLength(0)")]
    [InlineData("(values).GetLength(0)")]
    public void PredefinedObjectArrays_DoNotTreatSourceInstancesAsArrays(string expression)
    {
        var source = $$"""
            module SourceObject
              class @object
                func GetLength public (int:dimension) → int
                  ! cw
                  System.Console.WriteLine("read")
                  return dimension
              module P
                func Read public (@object:values) → int
                  !
                  return {{expression}}
            """;
        var result = Program.Compile(source, "source-object.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("Function 'Read'", StringComparison.Ordinal)
            && error.Message.Contains("'unknown'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PredefinedObjectArrays_AreNotShadowedByVerbatimTypeParameters(bool namespaced)
    {
        var source = $$"""
            module GenericObjectArrays{{(namespaced ? " Custom" : "")}}
              module P
                func Read<@object> public (object[]:values) → int
                  !
                  return values.GetLength(0)
            """;
        var result = Program.Compile(source, "generic-object-arrays.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var caller = "public static class Probe { public static string Run() => "
            + (namespaced ? "Custom." : "") + "P.Read<int>(new object[2]).ToString(); }";
        Assert.Equal("2", Run(result.GeneratedCode, caller));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void InferredLinqCollections_RejectForeignMetadataTypeParameters(
        bool sameSpelledSourceParameter, bool resolveCompanion, bool list)
    {
        var source = $$"""
            using System.Linq;
            public static class Probe
            {
                public static string Read{{(sameSpelledSourceParameter ? "<TSource>" : "")}}(Configuration configuration)
                {
                    var assembliesToScan = configuration.AssembliesToRegister.Distinct().{{(list ? "ToList" : "ToArray")}}();
                    return string.Join(",", assembliesToScan);
                }
                public static string Run()
                {
                    return Read{{(sameSpelledSourceParameter ? "<string>" : "")}}(new Configuration());
                }
            }
            """;
        const string companion = """
            public sealed class Configuration
            {
                public int[] AssembliesToRegister => new int[] { 3, 3, 7 };
            }
            """;
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview,
            DocumentationMode.Parse, SourceCodeKind.Regular, preprocessorSymbols: []);
        var input = resolveCompanion ? source + Environment.NewLine + companion : source;
        var tree = CSharpSyntaxTree.ParseText(input, parseOptions);
        var semanticCompilation = CSharpCompilation.Create("InferredArrayAuthority",
            [tree],
            new CSharpToCalorConverter().GetSemanticMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = semanticCompilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText == "assembliesToScan");
        var local = Assert.IsAssignableFrom<ILocalSymbol>(model.GetDeclaredSymbol(declaration));
        var element = list
            ? Assert.Single(Assert.IsAssignableFrom<INamedTypeSymbol>(local.Type).TypeArguments)
            : Assert.IsAssignableFrom<IArrayTypeSymbol>(local.Type).ElementType;
        if (resolveCompanion)
        {
            Assert.Equal(SpecialType.System_Int32, element.SpecialType);
        }
        else
        {
            var foreign = Assert.IsAssignableFrom<ITypeParameterSymbol>(element);
            Assert.Equal("TSource", foreign.Name);
            Assert.Equal(list ? "ToList" : "ToArray", foreign.ContainingSymbol.Name);
            Assert.Empty(foreign.DeclaringSyntaxReferences);
            Assert.DoesNotContain(model.LookupNamespacesAndTypes(declaration.SpanStart, name: foreign.Name),
                symbol => SymbolEqualityComparer.Default.Equals(symbol, foreign));
            if (sameSpelledSourceParameter)
                Assert.Contains(model.LookupNamespacesAndTypes(declaration.SpanStart, name: foreign.Name),
                    symbol => symbol is ITypeParameterSymbol { Name: "TSource" });
        }

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            ParseOptions = parseOptions,
            DefinedSymbols = [],
            ModuleName = "Calor0270Leg",
            GracefulFallback = true,
            AutoGenerateIds = true
        }).Convert(input, "Probe.cs");
        // The incomplete per-file input still needs its companion to build; do not hide that failure.
        Assert.Equal(resolveCompanion, result.Success);
        var errors = result.Issues.Where(issue => issue.Severity == ConversionIssueSeverity.Error).ToArray();
        Assert.Equal(resolveCompanion ? 0 : 2, errors.Length);
        Assert.All(errors, issue =>
        {
            Assert.Equal("roundtrip-csharp-validation", issue.Feature);
            Assert.Contains("'Configuration'", issue.Message);
        });
        Assert.Empty(result.Losses);
        RawFrameAssertions.AssertFramed(result.CalorSource!, present: false);
        var binding = Assert.Single(result.CalorSource!.Split('\n')
            .Where(line => Regex.IsMatch(line, @"^\s*let (?:\S+:)?assembliesToScan = ")));
        Assert.StartsWith(resolveCompanion
                ? $"let {(list ? "List<int>" : "int[]")}:assembliesToScan = "
                : "let assembliesToScan = ",
            binding.TrimStart());
        Assert.DoesNotContain(list ? "List<TSource>" : "TSource[]", result.CalorSource);
        var generated = ParseInferredCollectionSource(result.CalorSource!);
        Assert.Equal("3,7", Run(source, companion));
        Assert.Equal("3,7", Run(generated, resolveCompanion ? null : companion));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InferredLinqCollections_KeepSourceVisibleGenericParameters(bool typeParameterOnType, bool list)
    {
        var source = $$"""
            using System.Linq;
            using System.Collections.Generic;
            public static class Copy{{(typeParameterOnType ? "<TSource>" : "")}}
            {
                public static {{(list ? "List<TSource>" : "TSource[]")}} Read{{(typeParameterOnType ? "" : "<TSource>")}}(TSource[] source)
                {
                    var values = source.Distinct().{{(list ? "ToList" : "ToArray")}}();
                    return values;
                }
            }
            """;
        var caller = $$"""
            public static class Probe
            {
                public static string Run()
                {
                    return string.Join(",", Copy{{(typeParameterOnType ? "<int>" : "")}}.Read{{(typeParameterOnType ? "" : "<int>")}}(new int[] { 3, 3, 7 }));
                }
            }
            """;
        var result = new CSharpToCalorConverter().Convert(source, "Probe.cs");
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
        Assert.Empty(result.Losses);
        RawFrameAssertions.AssertFramed(result.CalorSource!, present: false);
        Assert.Contains($"let {(list ? "List<TSource>" : "TSource[]")}:values = ", result.CalorSource);
        var generated = ParseInferredCollectionSource(result.CalorSource!);
        Assert.Equal("3,7", Run(source, caller));
        Assert.Equal("3,7", Run(generated, caller));
    }

    private static string ParseInferredCollectionSource(string calor)
    {
        var diagnostics = new DiagnosticBag();
        var parser = new Parsing.Parser(
            new Parsing.Lexer(calor, diagnostics).TokenizeAllForParser(), diagnostics);
        var module = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        return new CSharpEmitter().Emit(module);
    }

    [Theory]
    [InlineData("u8[]", "byte[]")]
    [InlineData("u16[,]", "ushort[,]")]
    [InlineData("u32[][,]", "uint[][,]")]
    [InlineData("i64[][]", "long[][]")]
    [InlineData("f32[,,][]", "float[,,][]")]
    [InlineData("List<u8[]>[]", "List<byte[]>[]")]
    public void ExpandedNumericArrayTypes_KeepEveryRank(string compact, string expected) =>
        Assert.Equal(expected, TypeMapper.CalorToCSharp(Parsing.AttributeHelper.ExpandType(compact)));

    [Theory]
    [MemberData(nameof(SizedArraySurfaces))]
    public async Task SizedArrayBindings_UseIdentityFreeHeaders_AndPreserveRuntime(Surface surface)
    {
        const string source = """
            namespace SizedArrayCutover
            {
                public static class Probe
                {
                    private static string Log = "";
                    private static int Size() { Log += "s"; return 3; }
                    private static int Negative() { Log += "n"; return -1; }
                    public static T[] Make<T>(T value, int count)
                    {
                        T[] result = new T[count];
                        result[0] = value;
                        return result;
                    }
                    public static string Run()
                    {
                        int[] numbers = new int[Size()];
                        numbers[2] = 7;
                        string[] words = Make("x", 2);
                        int[] empty = new int[0];
                        try
                        {
                            int[] invalid = new int[Negative()];
                            return "bad";
                        }
                        catch (System.OverflowException)
                        {
                            return numbers.Length + ":" + numbers[2] + ":" + words.Length + ":" + words[0] + ":" + empty.Length + ":" + Log;
                        }
                    }
                }
            }
            """;
        const string expected = "3:7:2:x:0:sn";
        Assert.Equal(expected, Run(source));
        var calor = await ConvertNativeWithRealCliAsync(source, surface);
        RawFrameAssertions.AssertFramed(calor, present: false);
        Assert.Contains("new T[count]", calor);
        Assert.Contains("new int[0]", calor);
        var compilation = Program.Compile(calor, "sized-array.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Theory]
    [MemberData(nameof(SizedArraySurfaces))]
    public async Task EagerArrayUpdates_PreserveReadsConversionsAliasesAndExceptionTiming(Surface surface)
    {
        const string source = """
            using System;
            public struct Number
            {
                public int Value;
                public Number(int value) { Value = value; }
                public static implicit operator int(Number number)
                {
                    Probe.Log += "c";
                    if (number.Value < 0) throw new InvalidOperationException();
                    return number.Value;
                }
            }
            public static class Probe
            {
                public static string Log = "";
                private static int[,] Current;
                private static Number Read(int value) { Log += "r"; return new Number(value); }
                private static int Stop() { Log += "t"; throw new InvalidOperationException(); }
                private static int[,] Missing() { Log += "n"; return null; }
                private static int[,] Get() { Log += "g"; return Current; }
                private static int Rebind()
                {
                    Log += "r";
                    Current = new int[1, 1];
                    Current[0, 0] = 9;
                    return 0;
                }
                private static string Alias(ref int left, ref int right)
                {
                    int[] values = { left, right++, left };
                    return values[0] + ":" + values[1] + ":" + values[2] + ":" + left;
                }
                public static string Run()
                {
                    int n = 1;
                    int[] values = { n, n++, n };
                    int[,] shape = new int[n, n++];
                    shape[0, 0] = 7;
                    int k = 0;
                    int picked = shape[k, k++];
                    string result = values[0] + ":" + values[1] + ":" + values[2] + ":" + n + ":" + picked + ":" + k;
                    int q = 0;
                    try { int[] bad = { Read(-1), q++, Read(9) }; }
                    catch (InvalidOperationException) { result += "|" + q + ":" + Log; }
                    Log = "";
                    try { int[,] bad = new int[Stop(), q++]; }
                    catch (InvalidOperationException) { result += "|" + q + ":" + Log; }
                    Log = "";
                    int negative = -1;
                    try { int[,] bad = new int[negative, q++]; }
                    catch (OverflowException) { result += "|" + q; }
                    q = 0;
                    try { int bad = Missing()[q, q++]; }
                    catch (NullReferenceException) { result += "|" + q + ":" + Log; }
                    Log = "";
                    q = 0;
                    try { int bad = shape[Read(-1), q++]; }
                    catch (InvalidOperationException) { result += "|" + q + ":" + Log; }
                    q = 0;
                    try { int bad = shape[99, q++]; }
                    catch (IndexOutOfRangeException) { result += "|" + q; }
                    byte small = 255;
                    byte[] narrow = { small++, small };
                    result += "|" + narrow[0] + ":" + narrow[1] + ":" + small;
                    int prefix = 0;
                    int[] prefixes = { ++prefix, prefix, prefix++, prefix };
                    result += "|" + prefixes[0] + prefixes[1] + prefixes[2] + prefixes[3] + ":" + prefix;
                    int alias = 1;
                    result += "|" + Alias(ref alias, ref alias);
                    int[,] rectangle = { { n, n++ }, { n, n++ } };
                    result += "|" + (rectangle[0, 0] + rectangle[0, 1] + rectangle[1, 0] + rectangle[1, 1]) + ":" + n;
                    int[][] nested = { new[] { n, n++ }, new[] { n, n++ } };
                    result += "|" + (nested[0][0] + nested[0][1] + nested[1][0] + nested[1][1]) + ":" + n;
                    Current = shape;
                    Log = "";
                    q = 0;
                    int beforeRebind = Get()[q++, Rebind()];
                    return result + "|" + beforeRebind + ":" + Current[0, 0] + ":" + q + ":" + Log;
                }
            }
            """;
        const string expected = "1:1:2:3:7:1|0:rc|0:t|1|1:n|0:rc|1|255:0:0|1112:2|1:1:2:2|14:5|22:7|7:9:1:gr";
        Assert.Equal(expected, Run(source));
        var calor = await ConvertNativeWithRealCliAsync(source, surface);
        Assert.DoesNotContain("§CS", calor);
        Assert.DoesNotMatch(@"\((?:inc|pre-inc|dec|pre-dec|post-inc|post-dec)\s", calor);
        Assert.Matches(@"(?m)^\s*set ", calor);
        var compilation = Program.Compile(calor, "eager-array.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Theory]
    [MemberData(nameof(SizedArraySurfaces))]
    public async Task InPlaceArrayUpdateBoundaries_RemainExplicitlyNonNative(Surface surface)
    {
        const string source = """
            public static class Probe
            {
                private static int Seed = 2;
                private static int[] Initial = { Seed, Seed++, Seed };
                private static string Lazy()
                {
                    int next = 0;
                    bool flag = false;
                    int[] values = { flag ? next++ : 4, next++, next };
                    return values[0] + ":" + values[1] + ":" + values[2] + ":" + next;
                }
                public static string Run()
                {
                    int multi = 1;
                    int[] first = { multi, multi++ }, second = { multi, multi++ };
                    return Lazy() + "|" + Initial[0] + Initial[1] + Initial[2] + ":" + Seed
                        + "|" + first[0] + first[1] + second[0] + second[1] + ":" + multi;
                }
            }
            """;
        const string expected = "4:0:1:1|223:3|1122:3";
        Assert.Equal(expected, Run(source));
        var (calor, losses) = await ConvertWithRealCliAsync(source, surface);
        Assert.Contains("conditional-expression-hoisting", losses);
        RawFrameAssertions.AssertFramed(calor, present: true);
        var compilation = Program.Compile(calor, "preserved-array-update.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    private static async Task<string> ConvertNativeWithRealCliAsync(string source, Surface surface)
    {
        var (calor, losses) = await ConvertWithRealCliAsync(source, surface);
        Assert.Empty(losses);
        return calor;
    }

    [Fact]
    public void StaticAndInstanceConstructors_StayNativeAndRetainInitializationTiming()
    {
        const string source = """
            public class Gate
            {
                public static int Value;
                static Gate() { Probe.Hits = Probe.Hits + 1; }
                public Gate(int value) { Value = value; }
            }
            public static class Probe
            {
                public static int Hits;
                public static string Run()
                {
                    int before = Hits;
                    int value = Gate.Value;
                    int after = Hits;
                    var gate = new Gate(7);
                    return before + "|" + after + "|" + value + "|" + Hits + "|" + Gate.Value;
                }
            }
            """;
        const string expected = "0|1|0|1|7";
        Assert.Equal(expected, Run(source));
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
        Assert.Empty(result.Losses);
        RawFrameAssertions.AssertFramed(result.CalorSource!, present: false);
        var diagnostics = new DiagnosticBag();
        var module = new Parsing.Parser(
            new Parsing.Lexer(result.CalorSource!, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        var constructors = Assert.Single(module.Classes, cls => cls.Name == "Gate").Constructors;
        Assert.Equal(2, constructors.Count);
        Assert.Single(constructors, constructor => constructor.IsStatic);
        Assert.Single(constructors, constructor => !constructor.IsStatic);
        foreach (var calor in new[] { result.CalorSource!, new CalorEmitter().Emit(module) })
        {
            var compiled = Program.Compile(calor, "static-and-instance.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
            Assert.Equal(expected, Run(compiled.GeneratedCode));
        }
    }

    [Theory]
    [InlineData("static")]
    [InlineData("public stat")]
    [InlineData("stat private")]
    public void StaticConstructorHeaders_RejectNoncanonicalOrVisibilityModifiers(string modifiers)
    {
        var source = $$"""
            module StaticConstructorHeader
              class Value
                ctor {{modifiers}} ()
                  let int:value = 1
            """;
        var result = Program.Compile(source, "static-constructor-header.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.InvalidModifier);
    }

    public static IEnumerable<object[]> EagerArrayAddressArguments()
    {
        foreach (var modifier in new[] { "ref", "in" })
        foreach (var scenario in new[] { "valid", "null", "bounds", "rebind", "rebind-null", "rebind-bounds" })
        foreach (var surface in Enum.GetValues<Surface>())
            yield return [modifier, scenario, surface];
    }

    [Theory]
    [MemberData(nameof(SizedArraySurfaces))]
    public async Task ArrayValuedLocalLambdas_CompileByDefaultAndRunLikeTheOriginal(Surface surface)
    {
        var source = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests/TestData/InteropScope/T1/fixtures/X-INT-07.cs.txt"));
        var expected = Run(source);
        var calor = await ConvertNativeWithRealCliAsync(source, surface);
        Assert.DoesNotContain("§CS", calor);
        RawFrameAssertions.AssertFramed(calor, Calor.Compiler.Parsing.TokenKind.RawCSharp, present: false);
        var compilation = Program.Compile(calor, "array-local-lambdas.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Theory]
    [MemberData(nameof(SizedArraySurfaces))]
    public async Task EarlierBinaryOperands_AreEvaluatedBeforeLaterUpdates(Surface surface)
    {
        const string source = """
            public class B
            {
                public static int V;
                static B() { Probe.X = 10; }
            }
            public class C
            {
                public static int V;
                static C() { Probe.Hits++; }
            }
            public class Failing
            {
                public static int V;
                static Failing()
                {
                    Probe.Log += "f";
                    int unused = Probe.Missing[0];
                }
            }
            public static class Probe
            {
                public static int X = 1;
                public static int Hits;
                public static string Log = "";
                public static int[] Missing;
                public static string Run()
                {
                    int seen = B.V + X++;
                    string first = X + ":" + seen;
                    int local = 1;
                    int secondSeen = C.V + local++;
                    string second = Hits + ":" + local;
                    int x = 10;
                    int[] a = new int[30];
                    a[x + x++] += 5;
                    string third = a[20] + ":" + a[21] + ":" + x;
                    x = 10;
                    int[] b = new int[30];
                    b[x + x++] = 5;
                    string fourth = b[20] + ":" + b[21] + ":" + x;
                    int other = 3;
                    try { int unused = Failing.V + other++; }
                    catch (System.TypeInitializationException) { }
                    return first + "|" + second + "|" + third + "|" + fourth
                        + "|" + other + ":" + Log;
                }
            }
            """;
        const string expected = "11:10|1:2|5:0:11|5:0:11|3:f";
        Assert.Equal(expected, Run(source));
        var calor = await ConvertNativeWithRealCliAsync(source, surface);
        Assert.Equal(3, Regex.Matches(calor, @"(?m)^\s*ctor stat \(\)").Count);
        Assert.DoesNotContain("§CS", calor);
        RawFrameAssertions.AssertFramed(calor, Calor.Compiler.Parsing.TokenKind.RawCSharp, present: false);
        var compilation = Program.Compile(calor, "eager-binary-order.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Theory]
    [MemberData(nameof(SizedArraySurfaces))]
    public async Task FieldInitializerUpdates_PreserveTheirStorageAndExecutionPoint(Surface surface)
    {
        const string source = """
            public class Notice
            {
                private static int _value = ++Probe.N;
                private static int _sum = Probe.N + ++Probe.N;
                private static int _last = Probe.N++;
                public static string Read() => _value + ":" + _sum + ":" + _last;
            }
            public static class Probe
            {
                public static int N = 4;
                public static string Run()
                {
                    string seen = Notice.Read();
                    return seen + ":" + N;
                }
            }
            """;
        const string expected = "5:11:6:7";
        Assert.Equal(expected, Run(source));
        var (calor, losses) = await ConvertWithRealCliAsync(source, surface);
        Assert.Equal(new[] { "field", "field", "field" }, losses);
        Assert.Contains("private static int _value = ++Probe.N;", calor);
        var compilation = Program.Compile(calor, "preserved-field-update.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("int", "+=", "3", "4", "7")]
    [InlineData("int", "-=", "3", "4", "-1")]
    [InlineData("int", "*=", "3", "4", "12")]
    [InlineData("int", "/=", "12", "4", "3")]
    [InlineData("int", "%=", "17", "4", "1")]
    [InlineData("int", "&=", "6", "3", "2")]
    [InlineData("int", "|=", "6", "3", "7")]
    [InlineData("int", "^=", "6", "3", "5")]
    [InlineData("int", "<<=", "3", "2", "12")]
    [InlineData("int", ">>=", "12", "2", "3")]
    [InlineData("byte", "+=", "255", "1", "0")]
    [InlineData("string", "+=", "\"a\"", "\"b\"", "ab")]
    public void RefParameterCompoundUpdates_AreNativeAndPreserveMutation(
        string type, string operation, string initial, string later, string expected)
    {
        var source = $$"""
            public static class Probe
            {
                private static {{type}} Update(ref {{type}} value, {{type}} later)
                {
                    value {{operation}} later;
                    return value;
                }
                public static string Run()
                {
                    {{type}} value = {{initial}};
                    {{type}} later = {{later}};
                    {{type}} seen = Update(ref value, later);
                    return seen + ":" + value;
                }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        Assert.Equal(expected + ":" + expected, Run(source));
        var parsed = Program.Compile(converted.CalorSource!, "ref-update.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(parsed.HasErrors, string.Join("\n", parsed.Diagnostics.Errors));
        Assert.Equal(Run(source), Run(parsed.GeneratedCode));
        var roundTrip = Program.Compile(new CalorEmitter().Emit(parsed.Ast!), "ref-update.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(roundTrip.HasErrors, string.Join("\n", roundTrip.Diagnostics.Errors));
        Assert.Equal(parsed.GeneratedCode, roundTrip.GeneratedCode);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("ref ", true)]
    [InlineData("out ", true)]
    public void ParameterWrites_RequireMutationOnlyForCallerStorage(string modifier, bool requiresMutation)
    {
        var source = $$"""
            module ParameterWrites
              func Write ({{modifier}}int:value) → int
                !
                set value 4
                return value
            """;
        var pure = Program.Compile(source, "parameter-write.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Equal(requiresMutation, pure.HasErrors);
        if (requiresMutation)
            Assert.All(pure.Diagnostics.Errors, diagnostic =>
                Assert.True(diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("'mut'", StringComparison.Ordinal), diagnostic.ToString()));
        var declared = Program.Compile(source.Replace("\n    !\n", "\n    ! mut\n", StringComparison.Ordinal),
            "parameter-write.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(declared.HasErrors, string.Join("\n", declared.Diagnostics.Errors));
    }

    [Theory]
    [InlineData(false, "7:7")]
    [InlineData(true, "7")]
    public void RefParameterCompoundUpdates_KeepAliasedReadsAndInitializedOutWrites(bool output, string expected)
    {
        var source = output ? """
            public static class Probe
            {
                private static void Update(out int value)
                {
                    value = 3;
                    value += 4;
                }
                public static string Run()
                {
                    int value = 0;
                    Update(out value);
                    return "" + value;
                }
            }
            """ : """
            public static class Probe
            {
                private static int Update(ref int left, ref int right)
                {
                    left += ++right;
                    return left;
                }
                public static string Run()
                {
                    int value = 3;
                    int seen = Update(ref value, ref value);
                    return seen + ":" + value;
                }
            }
            """;
        Assert.Equal(expected, Run(source));
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        var compiled = Program.Compile(converted.CalorSource!, "aliased-ref-update.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(expected, Run(compiled.GeneratedCode));
    }

    [Theory]
    [MemberData(nameof(EagerArrayAddressArguments))]
    public async Task EagerArrayAddresses_AreValidatedBeforeLaterArgumentEffects(
        string modifier, string scenario, Surface surface)
    {
        var initializer = scenario.EndsWith("null", StringComparison.Ordinal) ? "null" : "new[] { 3, 4 }";
        var start = scenario.EndsWith("bounds", StringComparison.Ordinal) ? "2" : "0";
        var rebinds = scenario.StartsWith("rebind", StringComparison.Ordinal);
        var later = rebinds ? "Rebind(ref xs)" : "++other";
        var body = modifier == "ref" ? "value += later; return value;" : "return value + later;";
        var source = $$"""
            public static class Probe
            {
                private static string Log = "";
                private static int Use({{modifier}} int value, int later)
                {
                    Log += "u";
                    {{body}}
                }
                private static int Rebind(ref int[] xs)
                {
                    Log += "r";
                    xs = new[] { 30, 40 };
                    return 6;
                }
                public static string Run()
                {
                    int[] xs = {{initializer}};
                    int[] original = xs;
                    int i = {{start}};
                    int other = 5;
                    try
                    {
                        int seen = Use({{modifier}} xs[i++], {{later}});
                        return seen + ":" + i + ":" + other + ":" + xs[0] + ":" + original[0] + ":" + Log;
                    }
                    catch (System.NullReferenceException) { return "null:" + i + ":" + other + ":" + Log; }
                    catch (System.IndexOutOfRangeException) { return "bounds:" + i + ":" + other + ":" + Log; }
                }
            }
            """;
        var expected = scenario switch
        {
            "null" or "rebind-null" => "null:1:5:",
            "bounds" or "rebind-bounds" => "bounds:3:5:",
            "rebind" when modifier == "ref" => "9:1:5:30:9:ru",
            "rebind" => "9:1:5:30:3:ru",
            _ when modifier == "ref" => "9:1:6:9:9:u",
            _ => "9:1:6:3:3:u"
        };
        Assert.Equal(expected, Run(source));
        var (calor, losses) = await ConvertWithRealCliAsync(source, surface);
        var feature = rebinds ? modifier + "-parameter" : "compound-assignment-order";
        Assert.Equal(new[] { feature }, losses);
        // The converter may preserve the enclosing member rather than only the statement.
        RawFrameAssertions.AssertFramed(calor, present: true);
        var compilation = Program.Compile(calor, "eager-array-address.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    private static async Task<(string Calor, List<string> Losses)> ConvertWithRealCliAsync(string source, Surface surface)
    {
        if (surface is Surface.CliDefault or Surface.CliPassthrough)
        {
            var dir = Directory.CreateTempSubdirectory("calor-sized-array-").FullName;
            try
            {
                File.WriteAllText(Path.Combine(dir, "Fixture.cs"), source);
                var flags = surface == Surface.CliPassthrough ? new[] { "--passthrough" } : [];
                var (exit, stdout, stderr) = CliTestHarness.RunCli(dir,
                    ["convert", "Fixture.cs", "-o", "Fixture.calr", "--format", "json", .. flags]);
                Assert.True(exit == 0, stdout + "\n" + stderr);
                using var document = JsonDocument.Parse(stdout);
                var data = document.RootElement.GetProperty("data");
                var losses = data.TryGetProperty("losses", out var items) && items.ValueKind == JsonValueKind.Array
                    ? items.EnumerateArray().Select(loss => loss.GetProperty("feature").GetString()!).ToList()
                    : [];
                Assert.Equal(losses.Count, data.GetProperty("lossCount").GetInt32());
                var outcome = data.GetProperty("conversionPaths").GetProperty("outcome").GetString();
                if (losses.Count == 0)
                    Assert.Equal("native", outcome);
                else
                    Assert.NotEqual("native", outcome);
                return (File.ReadAllText(Path.Combine(dir, "Fixture.calr")), losses);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        return await ConvertAsync(source, surface);
    }

    // Element order around a call that mutates what the other elements read, in
    // expression and statement position; object initializers as 2-D elements; a ?: that
    // must not evaluate the unselected branch; && / || laziness over array operands;
    // index order; an array argument after another argument; rank 3, sized and bare
    // initializers; a nested array; an element that throws part-way through a row.
    private const string Ordering = """
        using System;

        public class Cell
        {
            public int V;
        }

        public static class L
        {
            public static string log = "";
            public static int counter = 0;

            public static int S(int v)
            {
                log = log + v;
                return v;
            }

            public static int Next()
            {
                counter = counter + 1;
                log = log + "n" + counter;
                return counter;
            }

            public static bool T(bool b, int v)
            {
                log = log + "t" + v;
                return b;
            }

            public static int Boom()
            {
                throw new InvalidOperationException("boom");
            }

            public static int Sum(int a, int[] xs)
            {
                int s = a;
                foreach (var x in xs)
                {
                    s = s + x;
                }
                return s;
            }
        }

        public static class A1
        {
            public static string Go()
            {
                var r = new int[] { L.counter, L.Next(), L.counter };
                int[] a = { L.counter, L.Next(), L.counter };
                return r[0] + "," + r[1] + "," + r[2] + "," + a[0] + "," + a[1] + "," + a[2];
            }
        }

        public static class A2
        {
            public static string Go()
            {
                Cell[,] g = { { new Cell { V = L.Next() }, new Cell { V = L.counter } } };
                return g[0, 0].V + "," + g[0, 1].V;
            }
        }

        public static class A3
        {
            public static Cell[,] Pick(bool flag)
            {
                return flag ? new Cell[,] { { new Cell { V = L.S(1) } } } : new Cell[,] { { new Cell { V = L.S(2) }, new Cell { V = L.S(3) } } };
            }

            public static Cell[] Pick1(bool flag)
            {
                return flag ? new Cell[] { new Cell { V = L.S(4) } } : new Cell[] { new Cell { V = L.S(5) } };
            }

            public static string Go()
            {
                return Pick(false).GetLength(1) + "," + Pick1(true)[0].V;
            }
        }

        public static class A4
        {
            public static string Go()
            {
                bool lazyAnd = L.T(false, 1) && new int[] { L.S(9) }.Length > 0;
                bool lazyOr = L.T(true, 2) || new int[,] { { L.S(8) } }.Length > 0;
                int len = new int[,] { { L.S(3), L.S(4) } }.Length;
                return "" + lazyAnd + lazyOr + len;
            }
        }

        public static class A5
        {
            public static string Go()
            {
                int[] a = { 5, 6 };
                Cell[,] p = new Cell[1, 2];
                p[0, 1] = new Cell { V = 7 };
                int idx = a[L.S(0)] + p[L.S(0), L.S(1)].V;
                int sum = L.Sum(L.S(3), new int[] { L.S(4), L.S(5) });
                return idx + "," + sum;
            }
        }

        public static class A7
        {
            public static int[,,] Cube()
            {
                return new int[,,] { { { L.S(4), L.S(5) } }, { { L.S(6), L.S(7) } } };
            }

            public static string Go()
            {
                var c = Cube();
                var sized = new int[2, 2] { { 1, 2 }, { 3, 4 } };
                int[,,] local = { { { 1 }, { 2 } } };
                return c[1, 0, 1] + "," + c.Rank + "," + sized[1, 0] + "," + local[0, 1, 0] + local.GetLength(1);
            }
        }

        public static class A8
        {
            public static string Go()
            {
                var nested = new object[] { new int[] { L.S(6) }, L.S(7) };
                string thrown = "none";
                try
                {
                    var bad = new int[,] { { L.S(1), L.Boom(), L.S(2) } };
                    thrown = "len" + bad.Length;
                }
                catch (InvalidOperationException e)
                {
                    thrown = e.Message;
                }
                return ((int[])nested[0])[0] + "," + thrown;
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                return A1.Go() + "|" + A2.Go() + "|" + A3.Go() + "|" + A4.Go() + "|" + A5.Go() + "|" + A7.Go() + "|" + A8.Go() + "|" + L.log;
            }
        }
        """;

    // Arrays as field, property and bare initializers, and size expressions with calls
    // in argument and return position (kept in place, not hoisted ahead of the call's
    // earlier argument).
    private const string Placement = """
        using System;

        public class Holder
        {
            public static int[,] Grid = new int[,] { { 1, 2 }, { 3, 4 } };
            public int[,,] Cube { get; } = new int[,,] { { { 5 } } };
            public static int[,] Bare = { { 6, 7 } };
        }

        public static class L
        {
            public static string log = "";

            public static int S(int v)
            {
                log = log + v;
                return v;
            }

            public static int Use(int a, int[] xs)
            {
                return a + xs.Length;
            }

            public static int Use2(int a, int[,] xs)
            {
                return a + xs.Length;
            }

            public static int[,] Make()
            {
                return new int[L.S(5) + 1, L.S(6)];
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                int a = L.Use(L.S(1), new int[L.S(2)]);
                int b = L.Use2(L.S(3), new int[L.S(4), 2]);
                return a + "," + b + "," + L.Make().Length + "," + Holder.Grid[1, 0] + new Holder().Cube[0, 0, 0] + Holder.Bare[0, 1] + "|" + L.log;
            }
        }
        """;

    // Codex round 1 reproductions that must stay native: an inline array argument before
    // a hoisted constructor argument; postfix in an element; a computed size after a
    // plain one; constructor arguments inside an element; written sizes behind an empty
    // level; an implicitly typed array of calls; jagged and rectangular-of-jagged
    // creation; fresh (not shared) empty arrays; a nested sized array.
    private const string Shapes = """
        public class Cell
        {
            public int N;

            public Cell(int n)
            {
                N = n;
            }

            public Cell(int a, int b)
            {
                N = a * 10 + b;
            }
        }

        public static class L
        {
            public static string log = "";
            public static int x = 0;
            public static int n = 1;

            public static int S(int v)
            {
                log = log + v;
                return v;
            }

            public static int Bump()
            {
                x = x + 1;
                return x;
            }

            public static int Next()
            {
                n = n + 1;
                return n;
            }

            public static int Use(int[] a, Cell c)
            {
                return a.Length + c.N;
            }
        }

        public static class Probe
        {
            static int[][] Make(bool f)
            {
                return f ? new int[][] { new int[L.n + 1], new int[L.S(6)] } : new int[][] { new int[L.S(7)] };
            }

            public static string Run()
            {
                int u = L.Use(new int[] { L.S(1) }, new Cell(L.S(2)));
                int y = 0;
                int[] b = { y, y++, y };
                var d = new int[L.n, L.Next()];
                Cell[] cs = { new Cell(L.x, L.Bump()) };
                var e1 = new int[0, 3] { };
                var e2 = new int[2, 0, 3] { { }, { } };
                var imp = new[] { L.S(3), L.S(4) };
                var jag = new int[2][];
                var mj = new int[,][] { { new int[] { 7 } } };
                bool fresh = new int[0] == new int[0];
                int[] empty = { };
                var nested = new int[][] { new int[0], new int[2] };
                var made = Make(true);
                return u + "|" + b[0] + b[1] + b[2] + "|" + d.GetLength(0) + d.GetLength(1) + made[0].Length + made[1].Length
                    + "|" + cs[0].N + "|" + e1.GetLength(1) + e2.GetLength(2) + "|" + (imp is int[]) + "|" + jag.Length + mj[0, 0][0]
                    + "|" + fresh + empty.Length + nested[1].Length + "|" + L.log;
            }
        }
        """;

    // Codex round 2 reproductions that must stay native: a sized 2-D array as a 2-D
    // element (it took the parent's next row), `n++` in a size, an implicitly typed
    // array received as object[], and `null` / `default` before a hoisted argument.
    private const string Round2 = """
        #nullable enable
        public class Cell
        {
            public int N;

            public Cell(int n)
            {
                N = n;
            }
        }

        public static class L
        {
            public static int x = 0;

            public static int Bump()
            {
                x = x + 1;
                return x;
            }

            public static string S()
            {
                return "a";
            }

            public static int Use(string? s, int[] a, Cell c)
            {
                return (s == null ? 1000 : 0) + a.Length + c.N;
            }

            public static int Use2(int[] a, int d, Cell c)
            {
                return a.Length + d + c.N;
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                object[,] grid = new object[,] { { new int[1, 1] }, { 42 } };
                int n = 1;
                var shape = new int[n, n++];
                object[] oa = new[] { L.S() };
                int u = L.Use(null, new int[] { 1 }, new Cell(L.Bump()));
                int v = L.Use2(new int[] { 1 }, default, new Cell(L.Bump()));
                return grid.GetLength(0) + "," + ((int[,])grid[0, 0])[0, 0] + "," + grid[1, 0] + "|" + shape.GetLength(0) + shape.GetLength(1) + n
                    + "|" + (oa is string[]) + "|" + u + "," + v;
            }
        }
        """;

    // Codex round 3 reproductions that must stay native: an implicit conversion inside
    // each element stays with its element (no hoisting), `g[n, n++]` reads its indices in
    // order, a 1-D array of rectangular arrays, written sizes behind an empty level that
    // are constant expressions, an implicitly typed rectangular array; and (verification
    // pass) a constructor argument and a multi-line anonymous object in statement elements,
    // and a declared int[][,] with a bare initializer.
    private const string Round3 = """
        using System;

        public struct X
        {
            public int V;

            public X(int v)
            {
                V = v;
            }

            public static implicit operator int(X x)
            {
                L.log = L.log + "c";
                return x.V;
            }
        }

        public static class L
        {
            public static string log = "";

            public static int T(int v)
            {
                log = log + "t";
                return v;
            }

            public static X S(int v)
            {
                log = log + "s";
                return new X(v);
            }

        }

        public static class Probe
        {
            public static string Run()
            {
                int[] a = { L.S(1), L.S(2) };
                L.log = L.log + "|";
                int[,] g = { { 10, 20 }, { 30, 40 } };
                int n = 0;
                int picked = g[n, n++];
                var mr = new int[][,] { new int[,] { { 1 } } };
                var cs = new int[1, 1 + 1, 0] { { { }, { } } };
                var imp = new[,] { { 1, 2 }, { 3, 4 } };
                int[] cv = { L.S(5), new X(L.T(6)) };
                object[] an = { new { A = 7 } };
                int[][,] dj = { new int[,] { { 8 } } };
                return a[0] + a[1] + "," + picked + "," + mr[0][0, 0] + cs.GetLength(1) + imp[1, 0] + "," + (cv[0] + cv[1]) + an.Length + dj[0][0, 0] + "|" + L.log;
            }
        }
        """;

    // Codex round 3: an `in` argument whose address calls a method cannot run after a
    // hoisted later argument, so the member is preserved; a method-group element keeps
    // its target type.
    private const string Round3Preserved = """
        using System;

        public struct X
        {
            public int V;

            public static implicit operator int(X x)
            {
                L.log = L.log + "c";
                return x.V;
            }
        }

        public class Cell
        {
        }

        public static class L
        {
            public static string log = "";

            public static X S(int v)
            {
                log = log + "s";
                return new X { V = v };
            }

            public static int I()
            {
                log = log + "1";
                return 0;
            }

            public static int T(int v)
            {
                log = log + "2";
                return v;
            }

            public static int Id(int x) => x;

            public static string Id(string x) => x;

            public static Func<int, int> Make() => Id;

            public static int Use(in int a, int[] b, Cell c) => a + b.Length;
        }

        public static class Probe
        {
            public static string Run()
            {
                int[] a = { L.S(1), L.S(2) };
                L.log = L.log + "|";
                Func<int, int>[] fs = { L.Id, L.Make() };
                int[,] g = { { 10, 20 }, { 30, 40 } };
                int n = 0;
                int picked = g[n, n++];
                int[] xs = { 5 };
                int used = L.Use(in xs[L.I()], new int[] { L.T(3) }, new Cell());
                var mr = new int[][,] { new int[,] { { 1 } } };
                var cs = new int[1, 1 + 1, 0] { { { }, { } } };
                var imp = new[,] { { 1, 2 }, { 3, 4 } };
                return a[0] + a[1] + "," + fs[0](4) + fs[1](5) + "," + picked + "," + used + "," + mr[0][0, 0] + cs.GetLength(1) + imp[1, 0] + "|" + L.log;
            }
        }
        """;

    // The last value records the original default-options unknown-effect observation.
    // PR 2 Codex round 1: a local read before an array argument that writes the local
    // (by ref), when a later argument is hoisted. The read was left in place and ran
    // after the hoisted array (11, not 1).
    private const string Pr2Arguments = """
        public class Cell
        {
        }

        public static class L
        {
            public static int Bump(ref int x)
            {
                x = x + 1;
                return x;
            }

            public static int Use(int first, int[] xs, Cell c)
            {
                return first * 10 + xs[0];
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                int x = 0;
                int r = L.Use(x, new int[] { L.Bump(ref x) }, new Cell());
                int y = 1;
                int q = L.Use(y, new int[] { y }, new Cell());
                return r + "," + x + "|" + (q - 10);
            }
        }
        """;

    public static TheoryData<string, string, Surface, bool> Rows()
    {
        var data = new TheoryData<string, string, Surface, bool>();
        foreach (var surface in Enum.GetValues<Surface>())
        {
            data.Add("F5-ARRAY-01", "2x2|2|3", surface, true);
            data.Add("F5-ARRAY-02", "13|2|5|67125", surface, false);
            data.Add("F5-ARRAY-03", "3|3|3|4", surface, false);
            data.Add("F5-ARRAY-04", "1|2|50|boom|123", surface, false);
            data.Add("F5-ARRAY-05", "3|4|3|9|b|6", surface, true);
            data.Add("ordering", "0,1,1,1,2,2|3,3|2,4|FalseTrue2|12,12|7,3,3,22|6,boom|n1n2n3234t1t2340013454567671", surface, true);
            data.Add("placement", "3,11,36,357|123456", surface, false);
            data.Add("shapes", "3|001|1236|1|33|True|27|False02|12346", surface, true);
            data.Add("round2", "2,0,42|112|True|1002,3", surface, true);
            data.Add("round3", "3,10,123,1118|scsc|sctc", surface, true);
            data.Add("pr2-args", "1,1|1", surface, false);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task ArrayExpressions_ConvertNatively_AndRunLikeTheOriginal(string row, string expected, Surface surface, bool unknownEffects)
    {
        var source = Source(row);
        Assert.Equal(expected, Run(source));

        var (calor, losses) = await ConvertAsync(source, surface);

        // Native: nothing preserved as C# and nothing rescued after the fact.
        Assert.Empty(losses);
        RawFrameAssertions.AssertFramed(calor, present: false);
        Assert.DoesNotContain("§CS{", calor);

        var strict = Program.Compile(calor, row + ".calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        var currentUnknownEffects = unknownEffects;
        var hasCurrentResults = row is "F5-ARRAY-05" or "round2" or "round3"
            || (row is "F5-ARRAY-01" or "ordering" or "shapes"
                && surface is Surface.ProjectMigrate or Surface.ProjectMigratePassthrough);
        if (hasCurrentResults)
        {
            using var current = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                CliTestHarness.FindRepoRoot(), "tests/TestData/InteropScope/R0/array-current-results.json")));
            var root = current.RootElement;
            Assert.Equal(Enum.GetNames<Surface>(),
                root.GetProperty("surfaces").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(new[] { "F5-ARRAY-05", "round2", "round3", "F5-ARRAY-01", "ordering", "shapes" },
                root.GetProperty("rows").EnumerateObject().Select(value => value.Name));
            var observation = root.GetProperty("rows").GetProperty(row);
            if (observation.TryGetProperty("surfaces", out var surfaces))
            {
                Assert.Equal(new[] { nameof(Surface.ProjectMigrate), nameof(Surface.ProjectMigratePassthrough) },
                    surfaces.EnumerateArray().Select(value => value.GetString()));
                Assert.Contains(surface.ToString(), surfaces.EnumerateArray().Select(value => value.GetString()));
            }
            Assert.Equal(unknownEffects, observation.GetProperty("recordedUnknownEffects").GetBoolean());
            Assert.Equal(expected, observation.GetProperty("trace").GetString());
            Assert.True(observation.GetProperty("strictCompileSuccess").GetBoolean());
            currentUnknownEffects = false;
            AssertCurrentArraySelections(strict, observation);
        }
        Assert.True(strict.HasErrors == currentUnknownEffects,
            string.Join(Environment.NewLine, strict.Diagnostics.Errors) + "\n" + calor);
        Assert.All(strict.Diagnostics.Errors, error =>
            Assert.True(error.Code == "Calor0410" && error.Message.Contains("'unknown'", StringComparison.Ordinal), error.ToString()));
        var compilation = Program.Compile(calor, row + ".calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
        if (hasCurrentResults)
        {
            Assert.Equal(compilation.GeneratedCode, strict.GeneratedCode);
            Assert.Equal(expected, Run(strict.GeneratedCode));
        }
    }

    internal static void AssertCurrentArraySelections(CompilationResult compilation, JsonElement observation)
    {
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        var diagnostics = new DiagnosticBag();
        var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(compilation.Ast!);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        var nodes = BoundDescendants(bound).ToArray();
        var calls = nodes.OfType<BoundCallExpression>()
            .Where(call => call.ResolvedMethodName == "GetLength").ToArray();
        Assert.Equal(observation.GetProperty("getLengthCalls").GetInt32(), calls.Length);
        var resolver = new EffectResolver(new Effects.Manifests.ManifestLoader(loadUserLevelManifests: false));
        Assert.All(calls, call =>
        {
            var method = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
            Assert.Equal(SpecialType.System_Array, method.ContainingType.SpecialType);
            Assert.Equal("GetLength", method.Name);
            Assert.False(method.IsStatic);
            Assert.False(method.IsGenericMethod);
            Assert.Equal(SpecialType.System_Int32, Assert.Single(method.Parameters).Type.SpecialType);
            Assert.Equal(SpecialType.System_Int32, method.ReturnType.SpecialType);
            var key = EffectResolverKey.FromBoundReceiver(
                Binding.Metadata.MetadataBinderResult.ToNominalBoundType(method.ContainingType),
                method.Name, call.ResolvedParameterTypes);
            var contract = resolver.Resolve(key);
            Assert.Equal(EffectResolutionStatus.PureExplicit, contract.Status);
            Assert.Equal("embedded:Calor.Compiler.Manifests.bcl-system.calor-effects.json", contract.Source);
        });
        if (observation.TryGetProperty("getLengthReceivers", out var receivers))
        {
            Assert.Equal(calls.Length, receivers.GetArrayLength());
            for (var index = 0; index < calls.Length; index++)
            {
                var shape = Assert.IsType<ArrayBoundType>(calls[index].Receiver!.MethodInputArrayType);
                var expected = receivers[index];
                Assert.Equal(expected.GetProperty("rank").GetInt32(), shape.Rank);
                Assert.Equal(expected.GetProperty("element").GetString(), shape.ElementType.DisplayString);
                if (expected.TryGetProperty("sourceElement", out var owner))
                {
                    var element = Assert.IsType<NominalBoundType>(shape.ElementType);
                    Assert.Same(bound.SymbolsById.Values.OfType<TypeSymbol>()
                        .Single(type => type.QualifiedName == owner.GetString()), element.Declaration);
                }
            }
        }
        var properties = nodes.OfType<BoundFieldAccessExpression>()
            .Select(field => field.ResolvedMetadataMember).OfType<IPropertySymbol>()
            .Where(property => property.ContainingType.SpecialType == SpecialType.System_Array).ToArray();
        Assert.Equal(observation.GetProperty("arrayProperties").EnumerateArray().Select(value => value.GetString()),
            properties.Select(property => property.Name));
        Assert.All(properties, property =>
        {
            Assert.False(property.IsStatic);
            Assert.Empty(property.Parameters);
            Assert.NotNull(property.GetMethod);
            Assert.Equal(SpecialType.System_Int32, property.Type.SpecialType);
        });
        if (observation.TryGetProperty("sourceFields", out var fields))
            foreach (var expected in fields.EnumerateArray())
            {
                var reads = nodes.OfType<BoundFieldAccessExpression>()
                    .Where(field => field.FieldName == expected.GetProperty("name").GetString()).ToArray();
                Assert.Equal(expected.GetProperty("count").GetInt32(), reads.Length);
                Assert.All(reads, read =>
                {
                    var field = Assert.IsType<VariableSymbol>(read.ResolvedField);
                    Assert.True(field.IsField);
                    Assert.False(field.IsStatic);
                    Assert.Equal(expected.GetProperty("owner").GetString(), field.DeclaringTypeName);
                    Assert.Equal(expected.GetProperty("type").GetString(), field.TypeName);
                    Assert.Same(bound.SymbolsById[field.Id], field);
                    Assert.Same(bound.SymbolsById.Values.OfType<TypeSymbol>()
                        .Single(type => type.QualifiedName == field.DeclaringTypeName),
                        Assert.IsType<NominalBoundType>(read.Target.Type).Declaration);
                    Assert.False(read.HasUnknownInferredType);
                });
            }
        if (observation.TryGetProperty("sourceCalls", out var sourceCalls))
            foreach (var expected in sourceCalls.EnumerateArray())
            {
                var selected = nodes.OfType<BoundCallExpression>()
                    .Where(call => call.ResolvedSymbol?.Name == expected.GetProperty("name").GetString()).ToArray();
                Assert.Equal(expected.GetProperty("count").GetInt32(), selected.Length);
                Assert.All(selected, call =>
                {
                    var method = call.ResolvedSymbol!;
                    Assert.Same(bound.SymbolsById[method.Id], method);
                    Assert.Equal(expected.GetProperty("owner").GetString(), method.ContainingTypeName);
                    Assert.Equal(expected.GetProperty("returnType").GetString(), method.ReturnType);
                    Assert.Equal(expected.GetProperty("parameters").EnumerateArray().Select(value => value.GetString()),
                        method.Parameters.Select(parameter => parameter.TypeName));
                    Assert.Null(call.ResolvedMetadataMethod);
                });
            }
    }

    [Fact]
    public void HistoricalRepro_ReturnsTheArrayInPlace()
    {
        // F5-ARRAY-01: the expression-position §ARR2D used to be a block bound to a
        // name the C# never declared (CS0103 arr2d005) plus a bare `new …;` (CS0201).
        var result = new CSharpToCalorConverter().Convert(R0Fixture("F5-ARRAY-01"));
        Assert.True(result.Success, string.Join("\n", result.Issues));
        RawFrameAssertions.AssertFramed(result.CalorSource!, present: false);
        Assert.Contains("return new Cell[,] { { new Cell() { V = 1 }", result.CalorSource);
        Assert.DoesNotMatch(@"(?m)^\s*(?:let|var) (?:\S+:)?_hoist", result.CalorSource);
        Assert.Contains("(cells[0, 1]).V", result.CalorSource);
    }

    [Fact]
    public void GeneratedNames_NeverReuseAUserName()
    {
        // F5-ARRAY-04: the user's `_hoist000` local and the parameters named like the
        // ids the converter used to generate (arr2d005/arr2d006) stay the only owners
        // of those names.
        var result = new CSharpToCalorConverter().Convert(R0Fixture("F5-ARRAY-04"));
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var calor = result.CalorSource!;
        Assert.Single(Regex.Matches(calor, @"(?m)^\s*(?:let|var) (?:\S+:)?_hoist000\s*="));
        Assert.Contains("(int:arr2d005, int:arr2d006)", calor);
        Assert.DoesNotMatch(@"(?m)^\s*(?:let|var) (?:\S+:)?arr2d00[56]\s*=", calor);

        var context = new ConversionContext { OriginalSource = "int arr001 = 0; var _hoist000 = arr002;" };
        Assert.Equal("arr003", context.GenerateId("arr"));
        Assert.True(context.IsReservedName("_hoist000"));

        // Codex round 1: a name spelled only with an escape inside an interpolation hole,
        // or only in another file of the project (a base-class field), is reserved too.
        const string probe = """
            public class Box { public Box(int a, int b) { } }

            public class Probe2 : Base
            {
                static int S() { return 1; }

                public string Go()
                {
                    int[] a = { S(), 2 };
                    // A §NEW argument with a call is hoisted: the temp needs a fresh name.
                    var box = new Box(a[0], S());
                    int[] b = { a[0], S() };
                    return $"{\u005fhoist001}" + a[0] + b.Length + _hoist002;
                }
            }
            """;
        var baseTree = CSharpSyntaxTree.ParseText("public class Base { public int _hoist000 = 99; public int _hoist002 = 97; }");
        // _hoist001 is spelled here only through the escape; the base tree supplies 000 and 002.
        var escaped = new CSharpToCalorConverter(new ConversionOptions { AdditionalSemanticSyntaxTrees = [baseTree], ValidateRoundTripCSharp = false })
            .Convert(probe);
        Assert.True(escaped.Success, string.Join("\n", escaped.Issues));
        Assert.DoesNotMatch(@"(?m)^\s*(?:let|var) (?:\S+:)?_hoist00[012]\s*=", escaped.CalorSource!);
        foreach (var reserved in new[] { "_hoist000", "_hoist001", "_hoist002" })
            Assert.True(escaped.Context.IsReservedName(reserved), reserved);
        var freshName = escaped.Context.GenerateId("_hoist");
        Assert.DoesNotContain(freshName, new[] { "_hoist000", "_hoist001", "_hoist002" });
        Assert.NotEqual(freshName, escaped.Context.GenerateId("_hoist"));

        // Codex round 2: a name inside an #if branch that is active only under the
        // conversion's own symbols is reserved too (it was parsed without them).
        const string conditional = """
            public static class Probe
            {
            #if FOO
                static int _ho\u0069st000 = 99;
                static int S() { return 1; }
                public static int Run() { int[] a = { S() }; return _ho\u0069st000 + a[0]; }
            #endif
            }
            """;
        var withSymbols = new CSharpToCalorConverter(new ConversionOptions { DefinedSymbols = ["FOO"] }).Convert(conditional);
        Assert.True(withSymbols.Success, string.Join("\n", withSymbols.Issues));
        Assert.DoesNotMatch(@"(?m)^\s*(?:let|var) (?:\S+:)?_hoist000\s*=", withSymbols.CalorSource!);
    }

    [Fact]
    public async Task StatementPositionArrays_PreserveEmittedCSharpAcrossModuleMigration()
    {
        // Keep the historical R0 bytes; compare codegen across the approved Length/module spellings.
        var baseline = File.ReadAllText(ActiveSyntaxInputs.ForCurrentTypedInput(CliTestHarness.FindRepoRoot(),
            "tests/TestData/InteropScope/R0/generated/F5-ARRAY-03.mcp-default.calr.txt",
            ActiveSyntaxInputs.InputKind.CalorText));
        var (calor, losses) = await ConvertAsync(R0Fixture("F5-ARRAY-03"), Surface.McpDefault);
        Assert.Empty(losses);
        RawFrameAssertions.AssertFramed(calor, present: false);
        var old = Program.Compile(baseline, "arrays.calr");
        var current = Program.Compile(calor, "arrays.calr");
        Assert.False(old.HasErrors, string.Join("\n", old.Diagnostics.Errors));
        Assert.False(current.HasErrors, string.Join("\n", current.Diagnostics.Errors));
        Assert.Equal(old.GeneratedCode, current.GeneratedCode);
        Assert.Contains("module Probe", calor);
        var probe = Assert.Single(CSharpSyntaxTree.ParseText(current.GeneratedCode).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>(), declaration => declaration.Identifier.ValueText == "Probe");
        Assert.Contains(probe.Modifiers, modifier => modifier.IsKind(SyntaxKind.PublicKeyword));
        Assert.Contains(probe.Modifiers, modifier => modifier.IsKind(SyntaxKind.StaticKeyword));
        var run = Assert.Single(probe.Members.OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == "Run");
        Assert.Contains(run.Modifiers, modifier => modifier.IsKind(SyntaxKind.PublicKeyword));
        Assert.Contains(run.Modifiers, modifier => modifier.IsKind(SyntaxKind.StaticKeyword));
    }

    private static string UnsupportedShapeSource()
    {
        // Negative controls: a size with a call in a ?: branch, and an assignment used as
        // an array element or an index, cannot be written in place natively; each is
        // preserved as C# with a named loss, never hoisted or evaluated twice.
        const string source = """
            public static class L
            {
                public static string log = "";

                public static int S(int v)
                {
                    log = log + v;
                    return v;
                }

                public static int Bump()
                {
                    x = x + 1;
                    return x;
                }

                public static int x = 0;
            }

            public static class Probe
            {
                public static string Run()
                {
                    bool flag = false;
                    int[] xs = flag ? new int[L.S(7)] : new int[1];
                    int x = 0;
                    int[] ys = flag ? new int[] { x = 5 } : new int[] { 9 };
                    // Codex round 1: an assignment used as an element or index was queued
                    // ahead of the statement AND kept as the value (evaluated twice).
                    int[] a = { L.x, L.x = L.Bump(), L.x };
                    var t = new[] { 10, 20 };
                    int pick = t[L.x = 0];
                    int[] sized = new int[L.x = L.Bump()];
                    return xs.Length + "," + ys[0] + "," + x + "," + a[0] + a[1] + a[2] + "," + pick + "," + sized.Length + L.x + "|" + L.log;
                }
            }
            """;
        return source;
    }

    [Fact]
    public void UnsupportedShapes_ArePreservedWithAReport_AndRunLikeTheOriginal()
    {
        var source = UnsupportedShapeSource();
        using var current = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            CliTestHarness.FindRepoRoot(), "tests/TestData/InteropScope/R0/array-current-results.json")));
        var observation = current.RootElement.GetProperty("preservations").GetProperty("UnsupportedShapes");
        Assert.Equal(5, observation.GetProperty("recordedStatementCount").GetInt32());
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Equal(observation.GetProperty("currentMemberCount").GetInt32(), result.Losses.Count);
        Assert.All(result.Losses, loss =>
        {
            Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
            Assert.Equal(observation.GetProperty("feature").GetString(), loss.Feature);
            Assert.Equal("interop", loss.Path);
            Assert.Null(loss.Trigger);
        });
        var compilation = Program.Compile(result.CalorSource!, "preserved.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("1,9,0,011,10,11|", Run(source));
        Assert.Equal(Run(source), Run(compilation.GeneratedCode));
    }

    [Theory]
    [MemberData(nameof(SurfacesOnly))]
    public async Task UnsupportedShapes_CurrentMemberPreservation_RetainsExactOriginalBodyOnEverySurface(Surface surface)
    {
        var source = UnsupportedShapeSource();
        using var current = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            CliTestHarness.FindRepoRoot(), "tests/TestData/InteropScope/R0/array-current-results.json")));
        Assert.Equal(Enum.GetNames<Surface>(),
            current.RootElement.GetProperty("surfaces").EnumerateArray().Select(value => value.GetString()));
        var observation = current.RootElement.GetProperty("preservations").GetProperty("UnsupportedShapes");
        Assert.Equal(5, observation.GetProperty("recordedStatementCount").GetInt32());
        Assert.Equal(1, observation.GetProperty("currentMemberCount").GetInt32());
        var originalMethod = Assert.Single(CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == observation.GetProperty("member").GetString());
        var (calor, losses) = await ConvertAsync(source, surface);
        Assert.Equal(new[] { observation.GetProperty("feature").GetString() }, losses);
        RawFrameAssertions.AssertFramed(calor, present: true);
        var strict = Program.Compile(calor, "preserved-shapes.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(strict.HasErrors, string.Join("\n", strict.Diagnostics.Errors));
        var owner = Assert.Single(strict.Ast!.Classes,
            type => type.Name == observation.GetProperty("owner").GetString());
        var preserved = Assert.Single(owner.InteropBlocks);
        Assert.Empty(owner.Methods);
        Assert.Equal(Calor.Compiler.Ast.InteropMemberKind.Method, preserved.MemberKind);
        Assert.Equal(observation.GetProperty("feature").GetString(), preserved.FeatureName);
        Assert.Equal(originalMethod.ToString(), preserved.CSharpCode.Trim());
        var helpers = Assert.Single(strict.Ast.Classes, type => type.Name == "L");
        Assert.Empty(helpers.InteropBlocks);
        Assert.Equal(new[] { "S", "Bump" }, helpers.Methods.Select(method => method.Name));
        Assert.All(helpers.Methods, method =>
            Assert.Empty(method.Body.OfType<Calor.Compiler.Ast.RawCSharpNode>()));
        var permissive = Program.Compile(calor, "preserved-shapes.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(permissive.HasErrors, string.Join("\n", permissive.Diagnostics.Errors));
        Assert.Equal(strict.GeneratedCode, permissive.GeneratedCode);
        Assert.Equal("1,9,0,011,10,11|", observation.GetProperty("trace").GetString());
        Assert.Equal(observation.GetProperty("trace").GetString(), Run(source));
        Assert.Equal(Run(source), Run(strict.GeneratedCode));
        Assert.Equal(Run(source), Run(permissive.GeneratedCode));
    }

    [Fact]
    public void InAddressBeforeAHoistedArgument_IsPreserved_AndRunsLikeTheOriginal()
    {
        var result = new CSharpToCalorConverter().Convert(Round3Preserved);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Single(result.Losses, loss => loss.Feature == "conditional-expression-hoisting");
        var compilation = Program.Compile(result.CalorSource!, "round3.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("3,45,10,6,123|scsc|12", Run(Round3Preserved));
        Assert.Equal(Run(Round3Preserved), Run(compilation.GeneratedCode));
    }

    // PR 2 Codex round 2: an earlier argument bound to an untyped temp (because a later
    // one is hoisted) loses its target typing (an overloaded method group, CS8917) or runs
    // its user-defined conversion after the hoisted argument ("sc" instead of "cs").
    private const string Pr2Retyped = """
        using System;

        public class Cell
        {
        }

        public struct X
        {
            public int V;

            public X(int v)
            {
                V = v;
            }

            public static implicit operator int(X x)
            {
                L.Log = L.Log + "c";
                return x.V;
            }
        }

        public static class L
        {
            public static string Log = "";

            public static int S()
            {
                Log = Log + "s";
                return 2;
            }

            public static X MakeX(int v)
            {
                return new X(v);
            }

            public static int Id(int x) => x;
            public static string Id(string x) => x;

            public static int Use(int first, int[] xs, Cell c)
            {
                return first + xs[0];
            }

            public static int Use2(Func<int, int> f, int[] xs, Cell c)
            {
                return f(xs[0]);
            }

            // PR 2 Codex round 3: a constant stays in place (its constant conversion to
            // byte? picks this overload; a temp is a non-constant int and picks long?).
            public const int C = 1;

            public static int UseB(byte? x, int[] xs, Cell c) => 1;

            public static int UseB(long? x, int[] xs, Cell c) => 2;

            // PR 2 extra verification: a constant whose conversion to the parameter is
            // user-defined is not left in place either (the conversion ran after S(): "sy").
            public static int UseY(Y y, int[] xs, Cell c) => xs[0];
        }

        public class Y
        {
            public static implicit operator Y(int value)
            {
                L.Log = L.Log + "y";
                return new Y();
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                return Go(L.MakeX(1));
            }

            public static string Go(X v)
            {
                int a = L.Use(v, new int[] { L.S() }, new Cell());
                int b = L.Use2(L.Id, new int[] { 4 }, new Cell());
                int k = L.UseB(L.C, new int[] { 1 }, new Cell());
                int y = L.UseY(L.C, new int[] { L.S() }, new Cell());
                return a + "," + b + "," + k + "," + y + "|" + L.Log;
            }
        }
        """;

    [Fact]
    public void RetypedEarlierArguments_ArePreserved_AndRunLikeTheOriginal()
    {
        var result = new CSharpToCalorConverter().Convert(Pr2Retyped);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Equal(3, result.Losses.Count(loss => loss.Feature == "conditional-expression-hoisting"));
        var compilation = Program.Compile(result.CalorSource!, "retyped.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("3,4,1,2|csys", Run(Pr2Retyped));
        Assert.Equal(Run(Pr2Retyped), Run(compilation.GeneratedCode));
    }

    [Fact]
    public void ImplicitArray_KeepsTheDeclaredStaticType()
    {
        // PR 2 Codex round 2: `object[] xs = new[] { "a" }` creates a string[] but xs is an
        // object[]: overload resolution (and the effect receiver) follow the declared type.
        const string source = """
            public static class Probe
            {
                static int Pick(object[] x) => 1;
                static int Pick(string[] x) => 2;

                public static string Run()
                {
                    object[] xs = new[] { "a" };
                    var ys = new[] { "b" };
                    return "" + Pick(xs) + Pick(ys) + (xs is string[]);
                }
            }
            """;
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Empty(result.Losses);
        Assert.Contains("let object[]:xs = ", result.CalorSource);
        var compilation = Program.Compile(result.CalorSource!, "declared.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("12True", Run(source));
        Assert.Equal(Run(source), Run(compilation.GeneratedCode));

        // The receiver keeps main's effect result: object[].Initialize is an unknown call,
        // not a Task[] member that a namespace default would certify.
        const string effect = """
            public static class P
            {
                public static void Run(System.Threading.Tasks.Task t)
                {
                    object[] xs = new[] { t };
                    xs.Initialize();
                }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(effect);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Contains("let object[]:xs = ", converted.CalorSource);

        // PR 2 Codex round 3: a nullable declared type (`object[]?`) is not the created
        // string[] either; the block form (`§ARR{zs:str}`) would declare string[].
        const string nullable = """
            #nullable enable
            public static class P
            {
                static string S() => "c";

                public static int Run()
                {
                    object[]? zs = new[] { S() };
                    object[][,] js = new[] { new string[,] { { "d" } } };
                    return zs.Length + js.Length;
                }
            }
            """;
        var annotated = new CSharpToCalorConverter().Convert(nullable);
        Assert.True(annotated.Success, string.Join("\n", annotated.Issues));
        var annotatedCompilation = Program.Compile(annotated.CalorSource!, "nullable-declared.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(annotatedCompilation.HasErrors, string.Join("\n", annotatedCompilation.Diagnostics));
        var declarations = CSharpSyntaxTree.ParseText(annotatedCompilation.GeneratedCode).GetRoot()
            .DescendantNodes().OfType<VariableDeclarationSyntax>().ToArray();
        Assert.Equal("object[]?", Assert.Single(declarations,
            declaration => declaration.Variables.Any(variable => variable.Identifier.ValueText == "zs")).Type.ToString());
        Assert.Equal("object[][,]", Assert.Single(declarations,
            declaration => declaration.Variables.Any(variable => variable.Identifier.ValueText == "js")).Type.ToString());
    }

    [Fact]
    public void SizedArrays_OfLowercaseMappedTypes_ParseWithTheirCloser()
    {
        // #1554 CI (FluentValidation `Ids = new Guid[0]`): `guid`, `datetime` and `timespan` are
        // not types to the §ARR attribute reader, so `§ARR{arrGuid1:0} §/ARR` read
        // `guid` as the id and the closer mismatched. The id now goes first for such types.
        const string source = """
            using System;

            public class Model
            {
                public Guid[] Ids = Array.Empty<Guid>();
            }

            public static class Probe
            {
                static string Use(Guid[] g, DateTime[] d, TimeSpan[] t, int[] i) => g.Length + "," + d.Length + "," + t.Length + "," + i.Length;

                public static string Run()
                {
                    var model = new Model { Ids = new Guid[0] };
                    return Use(new Guid[2], new DateTime[3], new TimeSpan[1], new int[4]) + "," + model.Ids.Length + "," + model.Ids.GetType().Name;
                }
            }
            """;
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Empty(result.Losses);
        RawFrameAssertions.AssertFramed(result.CalorSource!, present: false);
        Assert.Contains("new Guid[2]", result.CalorSource);
        Assert.Contains("new DateTime[3]", result.CalorSource);
        Assert.Contains("new TimeSpan[1]", result.CalorSource);
        Assert.Contains("new int[4]", result.CalorSource);
        var compilation = Program.Compile(result.CalorSource!, "lowercase.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("2,3,1,4,0,Guid[]", Run(source));
        Assert.Equal(Run(source), Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("new { }", "{ }")]
    [InlineData("new { @class = 1, @default = 2 }", "{ class = 1, default = 2 }")]
    [InlineData("new { value, @class }", "{ value = 7, class = 3 }")]
    [InlineData("new { item.Length }", "{ Length = 2 }")]
    [InlineData("new { A = new { B = 2 } }", "{ A = { B = 2 } }")]
    [InlineData("new {\n        A = value,\n        B = new { @class = @class },\n      }", "{ A = 7, B = { class = 3 } }")]
    public void AnonymousObjects_KeepNativeShapeNamesAndAllocation(string expression, string expected)
    {
        const string caller = """
            public static class Probe
            {
                public static string Run() => P.Make().ToString();
            }
            """;
        var original = $$"""
            public static class P
            {
                public static object Make()
                {
                    int value = 7;
                    int @class = 3;
                    string item = "hi";
                    return {{expression}};
                }
            }
            """;
        Assert.Equal(expected, Run(original, caller));
        var source = $$"""
            module NativeAnonymousObjects
              module P
                func Make public () → object
                  ! alloc
                  let int:value = 7
                  let int:@class = 3
                  let string:item = "hi"
                  return {{expression}}
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parsing.Parser(
            new Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        foreach (var calor in new[] { source, new CalorEmitter().Emit(module) })
        {
            RawFrameAssertions.AssertFramed(calor, present: false);
            Assert.DoesNotContain("§ANON", calor);
            var compiled = Program.Compile(calor, "anonymous-object.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
            Assert.Equal(expected, Run(compiled.GeneratedCode, caller));
            var pure = Program.Compile(calor.Replace("! alloc", "!", StringComparison.Ordinal), "pure-anonymous.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.Contains(pure.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("'alloc'", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("new { 1 }")]
    [InlineData("new { value + 1 }")]
    [InlineData("new { (value) }")]
    [InlineData("new { A = 1 A = 2 }")]
    [InlineData("new { A = 1, A = 2 }")]
    public void AnonymousObjects_RejectInvalidProjectionAndDuplicateNames(string expression)
    {
        var diagnostics = new DiagnosticBag();
        var source = $"module InvalidAnonymous\n  func Make () → object\n    return {expression}\n";
        new Parsing.Parser(new Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnonymousObjects_RetainInitializerEffects(bool declaresRead)
    {
        var source = $$"""
            module AnonymousInitializerEffects
              func Make public () → object
                ! alloc{{(declaresRead ? ", cr" : "")}}
                return new { Text = System.Console.ReadLine() }
            """;
        var result = Program.Compile(source, "anonymous-effects.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Equal(!declaresRead, result.HasErrors);
        Assert.All(result.Diagnostics.Errors, error => Assert.True(error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'cr'", StringComparison.Ordinal), error.ToString()));
    }

    [Theory]
    [InlineData(false, "1:2:2|2")]
    [InlineData(true, "throw:12")]
    public void AnonymousObjects_ConvertedInitializersKeepOrderSnapshotsAndExceptions(bool throws, string expected)
    {
        var source = $$"""
            public static class P
            {
                public static int State = 1;
                private static int Next() { State = State + 1; return State; }
                private static int Fail()
                {
                    State = State + 10;
                    throw new System.InvalidOperationException();
                }
                public static object Make()
                {
                    return new
                    {
                        First = State,
                        Middle = new int[] { Next() },
                        Last = {{(throws ? "Fail()" : "State")}}{{(throws ? ", Never = Next()" : "")}}
                    };
                }
            }
            """;
        const string caller = """
            public static class Probe
            {
                public static string Run()
                {
                    try
                    {
                        var result = P.Make();
                        var type = result.GetType();
                        var first = type.GetProperty("First").GetValue(result);
                        var middle = (int[])type.GetProperty("Middle").GetValue(result);
                        var last = type.GetProperty("Last").GetValue(result);
                        return first + ":" + middle[0] + ":" + last + "|" + P.State;
                    }
                    catch (System.InvalidOperationException) { return "throw:" + P.State; }
                }
            }
            """;
        Assert.Equal(expected, Run(source, caller));
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join(Environment.NewLine, converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        var diagnostics = new DiagnosticBag();
        var module = new Parsing.Parser(
            new Parsing.Lexer(converted.CalorSource!, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        foreach (var calor in new[] { converted.CalorSource!, new CalorEmitter().Emit(module) })
        {
            var compiled = Program.Compile(calor, "converted-anonymous.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
            Assert.Equal(expected, Run(compiled.GeneratedCode, caller));
        }
    }

    [Theory]
    [InlineData("value.A + \":\" + alias.Nested.B", "1:2")]
    [InlineData("(value).A + \":\" + (alias.Nested).B", "1:2")]
    [InlineData("value.Values[0] + \":\" + alias.Values.Length", "3:2")]
    [InlineData("value.Text.Length + \":\" + (alias).Text.Length", "3:3")]
    [InlineData("value.@class + \":\" + alias.Nested.B", "6:2")]
    [InlineData("(new { C = 5 }).C", "5")]
    public void AnonymousObjects_ConvertedPropertiesRemainUsable(string expression, string expected)
    {
        var source = $$"""
            public static class Probe
            {
                public static string Run()
                {
                    var value = new { A = 1, Nested = new { B = 2 }, Values = new int[] { 3, 4 }, Text = "abc", @class = 6 };
                    var alias = value;
                    return "" + ({{expression}});
                }
            }
            """;
        Assert.Equal(expected, Run(source));
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join(Environment.NewLine, converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        var diagnostics = new DiagnosticBag();
        var module = new Parsing.Parser(
            new Parsing.Lexer(converted.CalorSource!, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        foreach (var calor in new[] { converted.CalorSource!, new CalorEmitter().Emit(module) })
        {
            var compiled = Program.Compile(calor, "anonymous-properties.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
            Assert.Equal(expected, Run(compiled.GeneratedCode));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnonymousObjects_RetainEffectsOfProjectedMemberReads(bool declaresRead)
    {
        var source = $$"""
            module AnonymousProjectedEffects
              func Read public (System.IO.FileInfo:info) → long
                ! alloc{{(declaresRead ? ", fs:r" : "")}}
                let value = new { Box = info }
                return value.Box.Length
            """;
        var result = Program.Compile(source, "anonymous-projected-effects.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Equal(!declaresRead, result.HasErrors);
        Assert.All(result.Diagnostics.Errors, error => Assert.True(error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'fs:r'", StringComparison.Ordinal), error.ToString()));
    }

    [Theory]
    [InlineData("let object:value = new { A = 1 }\n    return value.A")]
    [InlineData("let value = new { Box = opaque }\n    return value.Box.A")]
    public void AnonymousObjects_DoNotInventPropertiesOnOpaqueValues(string body)
    {
        var source = $"module OpaqueAnonymous\n  func Read (object:opaque) → int\n    ! alloc\n    {body}\n";
        var result = Program.Compile(source, "opaque-anonymous.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'unknown'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("new int[2][]")]
    [InlineData("new int[0][]")]
    [InlineData("new int[2, 3][]")]
    [InlineData("new int[2][,]")]
    [InlineData("new int[2][][,]")]
    [InlineData("new string?[2][]")]
    [InlineData("new System.DateTime[2][]")]
    [InlineData("new int[2][] { new int[] { 1 }, new int[] { 2, 3 } }")]
    public void SizedJaggedArrays_RetainTheirClrShapeAndAllocationCharge(string expression)
    {
        const string observation = """
            var value = (System.Array)P.Make();
            string elements = "";
            foreach (var element in value)
                elements += element is System.Array inner ? inner.GetType() + ":" + inner.Length + ";" : "null;";
            return value.GetType() + "|" + value.GetType().GetElementType() + "|" + value.Rank + "|" + value.Length + "|" + elements;
            """;
        var caller = "public static class Probe { public static string Run() { " + observation + " } }";
        var expected = Run("public static class P { public static object Make() => " + expression + "; }", caller);
        var source = $$"""
            module SizedJaggedArray
              module P
                func Make public () → object
                  ! alloc
                  return {{expression}}
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parsing.Parser(
            new Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        foreach (var calor in new[] { source, new CalorEmitter().Emit(module) })
        {
            var result = Program.Compile(calor, "sized-jagged.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            Assert.Equal(expected, Run(result.GeneratedCode, caller));
            var pure = Program.Compile(calor.Replace("! alloc", "!", StringComparison.Ordinal), "pure-sized-jagged.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.Contains(pure.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("'alloc'", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("new int[2][,")]
    [InlineData("new int[2][, 3]")]
    [InlineData("new int[2][]?")]
    public void SizedJaggedArrays_RejectMalformedTrailingRanks(string expression)
    {
        var source = $$"""
            module InvalidSizedJaggedArray
              module P
                func Make public () → object
                  ! alloc
                  return {{expression}}
            """;
        var diagnostics = new DiagnosticBag();
        new Parsing.Parser(new Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void EmptyJaggedArray_KeepsItsElementRank()
    {
        const string caller = """
            public static class Probe
            {
                public static string Run()
                {
                    var value = P.Make();
                    return value.GetType() + "|" + value.Length + "|" + value.GetType().GetElementType();
                }
            }
            """;
        foreach (var expression in new[] { "new int[][] {}", "System.Array.Empty<int[]>()" })
        {
            var calor = $$"""
                module T
                  module P
                    func Make public () → int[][]
                      ! alloc
                      return {{expression}}
                """;
            var compilation = Program.Compile(calor, "empty.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
            Assert.Contains(expression.StartsWith("new", StringComparison.Ordinal)
                ? "new int[][] { }" : "Array.Empty<int[]>()", compilation.GeneratedCode);
            Assert.Equal("System.Int32[][]|0|System.Int32[]", Run(compilation.GeneratedCode, caller));
        }
    }

    [Fact]
    public void ElementAccessMember_ParsesAsAGroup()
    {
        const string calor = """
            module T
              class Cell public
                field int:V public
              module G
                func Get public (Cell[,]:g, Cell[]:xs) → int
                  return g[0, 1].V + xs[0].V
            """;
        var compilation = Program.Compile(calor, "group.calr", new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Contains("g[0, 1].V", compilation.GeneratedCode);
        Assert.Contains("xs[0].V", compilation.GeneratedCode);
    }

    [Theory]
    [InlineData("Holder")]
    [InlineData("Derived")]
    [InlineData("Proof.Holder")]
    [InlineData("Alias")]
    public void SourceStaticArrayFields_RetainDeclarationShapeAndInitializationEffects(string receiver)
    {
        var source = $$"""
            module StaticFields Proof
              using Alias = Proof.Holder
              class Holder
                field int[,]:Grid public static = new int[,] { { 1, 2 }, { 3, 4 } }
              class Derived
                extends Holder
              module Probe
                func Run public () → string
                  ! alloc
                  return "" + {{receiver}}.Grid.GetLength(0) + "," + {{receiver}}.Grid[1, 0]
            """;
        var parsed = Program.Compile(source, "source-static-array.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(parsed.HasErrors, string.Join(Environment.NewLine, parsed.Diagnostics.Errors));
        foreach (var calor in new[] { source, new CalorEmitter().Emit(parsed.Ast!) })
        {
            var result = Program.Compile(calor, "source-static-array.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            Assert.Equal("2,3", Run(result.GeneratedCode));
            var diagnostics = new DiagnosticBag();
            var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(result.Ast!);
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            var fields = BoundDescendants(bound).OfType<BoundFieldAccessExpression>()
                .Where(field => field.FieldName == "Grid").ToArray();
            Assert.Equal(2, fields.Length);
            Assert.All(fields, field =>
            {
                Assert.Equal("Proof.Holder", field.ResolvedField!.DeclaringTypeName);
                Assert.True(field.ResolvedField.IsStatic);
                Assert.Equal("Holder", field.SourceStaticFieldOwner!.Name);
                Assert.Equal(2, field.MethodInputArrayType!.Rank);
                Assert.Equal(Calor.Compiler.Binding.BoundTypes.NullableAnnotation.NotAnnotated,
                    field.MethodInputArrayType.NullableAnnotation);
                Assert.NotNull(Assert.IsType<BoundTypeReferenceExpression>(field.Target).ResolvedType);
            });
            var call = Assert.Single(BoundDescendants(bound).OfType<BoundCallExpression>());
            Assert.Equal(SpecialType.System_Array, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
            var pure = Program.Compile(calor.Replace("! alloc", "!", StringComparison.Ordinal),
                "pure-static-array.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.Contains(pure.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("'alloc'", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("field int[]:Other public static = new int[1]")]
    [InlineData("property int[]:Other static = new int[1]\n      get public")]
    public void SourceStaticArrayFields_ChargeOtherInitializersInTheDeclaringType(string other)
    {
        var source = $$"""
            module StaticFields
              class Holder
                field int:Value public static = 9
                {{other}}
              module Probe
                func Run public () → string
                  ! alloc
                  return "" + Holder.Value
            """;
        var result = Program.Compile(source, "other-static-initializer.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Equal("9", Run(result.GeneratedCode));
        var pure = Program.Compile(source.Replace("! alloc", "!", StringComparison.Ordinal),
            "pure-other-static-initializer.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(pure.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'alloc'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("GetLength(0)", "2")]
    [InlineData("Length", "4")]
    [InlineData("LongLength", "4")]
    [InlineData("Rank", "2")]
    public void SourceStaticArrayFields_ArrayMembersChargeReceiverInitialization(string member, string expected)
    {
        var source = $$"""
            module StaticFields
              class Holder
                field int[,]:Grid public static = new int[2, 2]
              module Probe
                func Run public () → string
                  ! alloc
                  return "" + Holder.Grid.{{member}}
            """;
        var result = Program.Compile(source, "static-array-member.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Equal(expected, Run(result.GeneratedCode));
        var pure = Program.Compile(source.Replace("! alloc", "!", StringComparison.Ordinal),
            "pure-static-array-member.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(pure.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'alloc'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("field string:Other public static = System.Environment.CurrentDirectory")]
    [InlineData("property string:Other static = System.Environment.CurrentDirectory\n      get public")]
    [InlineData("field object:Other public static = new object()")]
    [InlineData("field int[]:Other public static = Make()")]
    [InlineData("field int:Other public static = @null")]
    public void SourceStaticArrayFields_DoNotHideUnboundInitialization(string other)
    {
        var source = $$"""
            module StaticFields
              class Holder
                field int:Value public static = 9
                field int:@null public static = 1
                {{other}}
                func Make public static () → int[]
                  ! alloc
                  return new int[1]
              func Read () → int
                !
                return Holder.Value
            """;
        var parseDiagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(source, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics).Parse();
        Assert.False(parseDiagnostics.HasErrors, string.Join(Environment.NewLine, parseDiagnostics.Errors));
        var result = Program.Compile(source, "unbound-static-initializer.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        if (other == "field object:Other public static = new object()")
        {
            var error = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.ForbiddenEffect, error.Code);
            Assert.Equal("Function 'Read' uses effect 'alloc' but does not declare it", error.Message);
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                || diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            var allowedSource = source.Replace("    !\n", "    ! alloc\n", StringComparison.Ordinal);
            var allowedDiagnostics = new DiagnosticBag();
            _ = new Parser(new Lexer(allowedSource, allowedDiagnostics).TokenizeAllForParser(), allowedDiagnostics).Parse();
            Assert.False(allowedDiagnostics.HasErrors, string.Join(Environment.NewLine, allowedDiagnostics.Errors));
            var allowed = Program.Compile(allowedSource, "bound-object-initializer.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.False(allowed.HasErrors, string.Join(Environment.NewLine, allowed.Diagnostics.Errors));
            Assert.Equal("9", Run(allowed.GeneratedCode,
                "public static class Probe { public static string Run() => GlobalModule.Read().ToString(); }"));
        }
        else
        {
            Assert.Contains(result.Diagnostics, error => error.Code == DiagnosticCode.UnknownExternalCall
                && error.Message.Contains("Holder..cctor", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("'unknown'", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SourceStaticFields_PreservedMethodDoesNotInventTypeInitialization()
    {
        const string source = """
            public static class Holder
            {
                public static int Value = 9;
                public static ref int Escape(ref int value) => ref value;
            }
            public static class Probe
            {
                public static string Run() => "" + Holder.Value;
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join(Environment.NewLine, conversion.Issues));
        Assert.Contains(conversion.Ast!.Classes.Single(owner => owner.Name == "Holder").InteropBlocks,
            block => block.CSharpCode.Contains("Escape", StringComparison.Ordinal));
        var result = Program.Compile(conversion.CalorSource!, "preserved-method-initialization.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("9", Run(source));
        Assert.Equal("9", Run(result.GeneratedCode));

        var invoked = Program.Compile(conversion.CalorSource + """

              func Invoke (ref int:value) → int
                ! mut
                return Holder.Escape(ref value)
            """, "preserved-method-invocation.calr");
        Assert.Contains(invoked.Diagnostics, error => error.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(invoked.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'unknown'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public static int Value = Unknown();")]
    [InlineData("static Holder() { Unknown(); }")]
    [InlineData("public static int Value { get; } = Unknown();")]
    [InlineData("public static void Read() {} public static int Value = Unknown();")]
    [InlineData("[System.Runtime.CompilerServices.ModuleInitializer] public static void Start() {}")]
    [InlineData("#if HIDDEN\npublic static int Value = Unknown();\n#endif\npublic static void Read() {}")]
    [InlineData("public static void Read(")]
    [InlineData("}\nclass Other { public static int Value = Unknown(); }\nclass Tail {")]
    public void SourceStaticFields_UnprovenPreservedMembersRemainInitializationBoundaries(string payload)
    {
        var block = new CSharpInteropBlockNode(TextSpan.Empty, payload, memberKind: InteropMemberKind.Method);
        Assert.True(CallGraphAnalysis.PreservedMemberMayInitializeType(block));
    }

    [Theory]
    [InlineData("Holder:Holder", "field int:Grid public")]
    [InlineData("", "field int:Grid private static = 2")]
    [InlineData("", "field int:Grid public = 2")]
    [InlineData("", "property int:Grid static\n      get public\n        return System.Environment.TickCount")]
    public void SourceStaticArrayFields_DoNotInventStaticFieldIdentity(string parameters, string member)
    {
        var source = $$"""
            module StaticFields
              class Holder
                {{member}}
              func Read ({{parameters}}) → int
                !
                return Holder.Grid
            """;
        var result = Program.Compile(source, "unresolved-static-field.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        if (parameters.Length == 0)
            Assert.True(result.HasErrors);
        var bound = new Calor.Compiler.Binding.Binder(new DiagnosticBag()).Bind(result.Ast!);
        Assert.All(BoundDescendants(bound).OfType<BoundFieldAccessExpression>()
            .Where(field => field.FieldName == "Grid"), field => Assert.Null(field.SourceStaticFieldOwner));
    }

    [Fact]
    public void SourceStaticArrayFields_ShadowedReceiversRetainActualGetterEffects()
    {
        const string source = """
            module StaticFields
              class Holder
                field int:Grid public static = 9
              class Other
                property int:Grid
                  get public
                    return System.Console.Read()
              func Read (Other:Holder) → int
                !
                return (Holder).Grid
            """;
        var result = Program.Compile(source, "shadowed-static-field.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors,
            error => error.Code == DiagnosticCode.AccessorEffectContractUnavailable);
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'cr'", StringComparison.Ordinal));
        var bound = new Calor.Compiler.Binding.Binder(new DiagnosticBag()).Bind(result.Ast!);
        var field = Assert.Single(BoundDescendants(bound).OfType<BoundFieldAccessExpression>()
            .Where(field => field.FieldName == "Grid"));
        Assert.Null(field.SourceStaticFieldOwner);
        var target = Assert.IsType<BoundVariableExpression>(field.Target);
        Assert.True(target.Variable.IsParameter);
        Assert.Equal("Other", target.Variable.TypeName);
    }

    [Fact]
    public void SourceStaticArrayFields_RetainNoHeaderStaticConstructorEnforcement()
    {
        const string original = """
            public class Holder
            {
                public static int Value = 9;
                static Holder() { System.Console.WriteLine("initialize"); }
            }
            public static class Probe { public static int Read() => Holder.Value; }
            """;
        var conversion = new CSharpToCalorConverter().Convert(original);
        Assert.True(conversion.Success, string.Join(Environment.NewLine, conversion.Issues));
        Assert.Empty(conversion.Losses);
        var result = Program.Compile(conversion.CalorSource!, "static-constructor-field.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors,
            error => error.Code == DiagnosticCode.ConstructorEffectContractUnavailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceStaticArrayFields_IncludeStaticConstructorEffectsRegardlessOfDeclarationOrder(bool callerFirst)
    {
        const string holder = """
            class Holder
              field int:Value public static
              ctor stat ()
                set Value 1
            """;
        const string read = """
            func Read public () → int
              !
              return Holder.Value
            """;
        var source = "module StaticFields\n" + string.Join("\n",
            (callerFirst ? read + "\n" + holder : holder + "\n" + read)
                .Split('\n').Select(line => "  " + line));
        var result = Program.Compile(source, "ordered-static-constructor.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'mut'", StringComparison.Ordinal));
        var allowed = Program.Compile(source.Replace("    !\n", "    ! mut\n", StringComparison.Ordinal),
            "allowed-static-constructor.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(allowed.HasErrors, string.Join(Environment.NewLine, allowed.Diagnostics.Errors));
        Assert.Equal("1", Run(allowed.GeneratedCode,
            "public static class Probe { public static string Run() => GlobalModule.Read().ToString(); }"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?")]
    public void SourceStaticArrayFields_PreserveDeclaredArrayNullability(string nullable)
    {
        var source = $$"""
            module StaticFields
              class Holder
                field int[,]{{nullable}}:Grid public static
              module Probe
                func Run public () → string
                  !
                  return "" + (Holder.Grid == null)
            """;
        var result = Program.Compile(source, "nullable-static-array.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Equal("True", Run(result.GeneratedCode));
        var bound = new Calor.Compiler.Binding.Binder(new DiagnosticBag()).Bind(result.Ast!);
        var field = Assert.Single(BoundDescendants(bound).OfType<BoundFieldAccessExpression>());
        Assert.Equal(nullable.Length == 0
                ? Calor.Compiler.Binding.BoundTypes.NullableAnnotation.NotAnnotated
                : Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Annotated,
            field.MethodInputArrayType!.NullableAnnotation);
    }

    [Fact]
    public void SourceStaticArrayFields_WritesChargeMutationAndTypeInitialization()
    {
        const string source = """
            module StaticFields
              class Holder
                field int:Value public static
                field int[]:Other public static = new int[1]
              module Writer
                func Write public () → string
                  ! alloc, mut
                  set Holder.Value 2
                  return "done"
            """;
        var result = Program.Compile(source, "write-static-field.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Equal("done|2", Run(result.GeneratedCode,
            """public static class Probe { public static string Run() => Writer.Write() + "|" + Holder.Value; }"""));
        foreach (var (header, missing) in new[] { ("! mut", "alloc"), ("! alloc", "mut") })
        {
            var forbidden = Program.Compile(source.Replace("! alloc, mut", header, StringComparison.Ordinal),
                "forbidden-static-write.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.Contains(forbidden.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains($"'{missing}'", StringComparison.Ordinal));
        }
    }

    private const string RectangularArrayCaller = """
        public static class Probe
        {
            public static string Run()
            {
                var value = (System.Array)P.Make();
                string dimensions = "", elements = "";
                for (int i = 0; i < value.Rank; i++)
                    dimensions += (i == 0 ? "" : ",") + value.GetLength(i);
                foreach (var element in value)
                {
                    if (elements != "") elements += ",";
                    if (element is System.Array inner)
                        foreach (var item in inner) elements += item + ";";
                    else
                        elements += element;
                }
                return dimensions + "|" + elements;
            }
        }
        """;

    [Theory]
    [InlineData("new int[2, 1, 2] { { { 1, 2 } }, { { 3, 4 } } }", "2,1,2|1,2,3,4")]
    [InlineData("new int[,,] { { { 1, 2 } }, { { 3, 4 } } }", "2,1,2|1,2,3,4")]
    [InlineData("new int[2, 1, 1, 2] { { { { 1, 2 } } }, { { { 3, 4 } } } }", "2,1,1,2|1,2,3,4")]
    [InlineData("new int[,,] {}", "0,0,0|")]
    [InlineData("new int[0, 3] {}", "0,3|")]
    [InlineData("new int[1, 0, 3] { {} }", "1,0,3|")]
    [InlineData("new int[1 + 1, 1, 2] { { { 1, 2 } }, { { 3, 4 } } }", "2,1,2|1,2,3,4")]
    [InlineData("new int[,,] { { { 1, 2, }, }, { { 3, 4, }, }, }", "2,1,2|1,2,3,4")]
    [InlineData("new int[1, 1, 2][] { { { new int[] {1, 2}, new int[] {3} } } }", "1,1,2|1;2;,3;")]
    public void NativeRectangularInitializers_PreserveNestingAndRuntime(string expression, string expected)
    {
        var original = "public static class P { public static object Make() => " + expression + "; }";
        Assert.Equal(expected, Run(original, RectangularArrayCaller));
        var authored = $$"""
            module RectangularInitializer
              module P
                func Make public () → object
                  ! alloc
                  return {{expression}}
            """;
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join(Environment.NewLine, converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        foreach (var source in new[] { authored, converted.CalorSource! })
        {
            var diagnostics = new DiagnosticBag();
            var module = new Parsing.Parser(
                new Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            foreach (var calor in new[] { source, new CalorEmitter().Emit(module) })
            {
                var result = Program.Compile(calor, "rectangular-initializer.calr",
                    new CompilationOptions { StatusWriter = TextWriter.Null });
                Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
                Assert.Equal(expected, Run(result.GeneratedCode, RectangularArrayCaller));
                var pure = Program.Compile(calor.Replace("! alloc", "!", StringComparison.Ordinal),
                    "pure-rectangular-initializer.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
                Assert.Contains(pure.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                    && error.Message.Contains("'alloc'", StringComparison.Ordinal));
            }
        }
    }

    [Theory]
    [MemberData(nameof(SurfacesOnly))]
    public async Task NativeRectangularInitializers_ConvertOnEverySurface(Surface surface)
    {
        const string original = """
            public static class Probe
            {
                public static string Run()
                {
                    var values = new int[2, 1, 2] { { { 1, 2 } }, { { 3, 4 } } };
                    return values.GetLength(0) + "," + values.GetLength(1) + "," + values.GetLength(2)
                        + "|" + values[0, 0, 0] + values[0, 0, 1] + values[1, 0, 0] + values[1, 0, 1];
                }
            }
            """;
        Assert.Equal("2,1,2|1234", Run(original));
        var (calor, losses) = await ConvertAsync(original, surface);
        Assert.Empty(losses);
        RawFrameAssertions.AssertFramed(calor, present: false);
        var result = Program.Compile(calor, "surface-rectangular-initializer.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Equal("2,1,2|1234", Run(result.GeneratedCode));
    }

    [Theory]
    [InlineData("new[,] { { 1, 2 }, { 3, 4 } }", "2,2|1,2,3,4")]
    [InlineData("new[,,] { { { 1, 2 } }, { { 3, 4 } } }", "2,1,2|1,2,3,4")]
    public void NativeRectangularInitializers_ConvertInferredElementTypes(string expression, string expected)
    {
        var original = "public static class P { public static object Make() => " + expression + "; }";
        Assert.Equal(expected, Run(original, RectangularArrayCaller));
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join(Environment.NewLine, converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        var result = Program.Compile(converted.CalorSource!, "inferred-rectangular-initializer.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Equal(expected, Run(result.GeneratedCode, RectangularArrayCaller));
    }

    [Theory]
    [InlineData("new int[,] { { 1, 2 }, { 3, 4 } }", new long[] { 2, 2 })]
    [InlineData("new int[,,] { { { 1, 2 } }, { { 3, 4 } } }", new long[] { 2, 1, 2 })]
    [InlineData("new int[,,] {}", new long[] { 0, 0, 0 })]
    [InlineData("new int[,,] { {}, {} }", new long[] { 2, 0, 0 })]
    public void NativeRectangularInitializers_RetainInferredBoundDimensions(string expression, long[] expected)
    {
        var source = $$"""
            module InferredBoundDimensions
              module P
                func Make public () → object
                  ! alloc
                  return {{expression}}
            """;
        var result = Program.Compile(source, "inferred-bound-dimensions.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var bound = new Calor.Compiler.Binding.Binder(new DiagnosticBag()).Bind(result.Ast!);
        var creation = Assert.Single(BoundDescendants(bound).OfType<BoundMultiDimArrayCreation>());
        Assert.Equal(expected, creation.DimensionSizes.Select(size => Assert.IsType<BoundIntLiteral>(size).Value));
    }

    [Theory]
    [InlineData(false, "1234|1234")]
    [InlineData(true, "throw|123")]
    public void NativeRectangularInitializers_PreserveElementOrderAndExceptions(bool fail, string expected)
    {
        var original = $$"""
            public static class P
            {
                public static string State = "";
                public static int Next(int value)
                {
                    State += value;
                    if ({{(fail ? "true" : "false")}} && value == 3)
                        throw new System.InvalidOperationException();
                    return value;
                }
                public static object Make() => new int[2, 1, 2]
                    { { { Next(1), Next(2) } }, { { Next(3), Next(4) } } };
            }
            """;
        const string caller = """
            public static class Probe
            {
                public static string Run()
                {
                    try
                    {
                        var values = (int[,,])P.Make();
                        return "" + values[0, 0, 0] + values[0, 0, 1]
                            + values[1, 0, 0] + values[1, 0, 1] + "|" + P.State;
                    }
                    catch (System.InvalidOperationException) { return "throw|" + P.State; }
                }
            }
            """;
        Assert.Equal(expected, Run(original, caller));
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join(Environment.NewLine, converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        var result = Program.Compile(converted.CalorSource!, "rectangular-element-order.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Equal(expected, Run(result.GeneratedCode, caller));
    }

    [Theory]
    [InlineData("new int[2, 2] {}")]
    [InlineData("new int[1, 1, 1] {}")]
    [InlineData("new int[0,] {}")]
    [InlineData("new int[0, 0,] {}")]
    [InlineData("new int[3, 1, 2] { { { 1, 2 } }, { { 3, 4 } } }")]
    [InlineData("new int[2, 1, 2] { { { 1 } }, { { 2, 3, 4 } } }")]
    [InlineData("new int[,,] { { {1} }, { {2}, {3}, {4} }, { {5}, {6} } }")]
    public void NativeRectangularInitializers_DoNotNormalizeInvalidShapes(string expression)
    {
        var original = "public static class P { public static object Make() => " + expression + "; }";
        var csharp = CSharpCompilation.Create("InvalidRectangularInitializer",
            [CSharpSyntaxTree.ParseText(original)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Contains(csharp.GetDiagnostics(), diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var source = $$"""
            module InvalidRectangularInitializer
              module P
                func Make public () → object
                  ! alloc
                  return {{expression}}
            """;
        var result = Program.Compile(source, "invalid-rectangular-initializer.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        var converted = new CSharpToCalorConverter().Convert(original);
        if (converted.Success)
            Assert.True(Program.Compile(converted.CalorSource!, "invalid-converted-rectangular-initializer.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null }).HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRectangularInitializers_ChargeDeepElementEffects(bool declaresRead)
    {
        var source = $$"""
            module RectangularInitializerEffects
              module P
                func Make public () → object
                  ! alloc{{(declaresRead ? ", cr" : "")}}
                  return new string?[1, 1, 1] { { { System.Console.ReadLine() } } }
            """;
        var result = Program.Compile(source, "rectangular-element-effects.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Equal(!declaresRead, result.HasErrors);
        Assert.All(result.Diagnostics.Errors, error => Assert.True(error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'cr'", StringComparison.Ordinal), error.ToString()));
    }

    [Theory]
    [InlineData("§ARR2D{i32:2:1:2} §ROW 1 2 §ROW 3 4 §/ARR2D", "i32[,,]", "new int[2, 1, 2] { { { 1, 2 } }, { { 3, 4 } } }", true)]
    [InlineData("§ARR2D{i32:2:2} §ROW 1 2 §ROW 3 4 §/ARR2D", "i32[,]", "new int[2, 2] { { 1, 2 }, { 3, 4 } }", true)]
    [InlineData("§ARR2D{i32:2:2} §ROW 1 2 §/ARR2D", "i32[,]", "new int[2, 2] { { 1, 2 } }", false)]
    public void SizedRows_KeepTheirShape_AndAMismatchIsAnError(string array, string type, string csharp, bool compiles)
    {
        // Rank 3+ and sized initializers: the rows are innermost vectors, regrouped by
        // the sizes. A row count that disagrees with the sizes is rejected (CS0847),
        // never padded or truncated.
        var expression = array switch
        {
            "§ARR2D{i32:2:1:2} §ROW 1 2 §ROW 3 4 §/ARR2D" =>
                "new int[2, 1, 2] { { { 1, 2 } }, { { 3, 4 } } }",
            "§ARR2D{i32:2:2} §ROW 1 2 §ROW 3 4 §/ARR2D" =>
                "new int[2, 2] { { 1, 2 }, { 3, 4 } }",
            "§ARR2D{i32:2:2} §ROW 1 2 §/ARR2D" => "new int[2, 2] { { 1, 2 } }",
            _ => throw new ArgumentOutOfRangeException(nameof(array), array, null)
        };
        var calor = $$"""
            module T
              module G
                func Make public () → {{TypeMapper.ToSurfaceType(type)}}
                  ! alloc
                  return {{expression}}
            """;
        var compilation = Program.Compile(calor, "sized.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(csharp, compilation.GeneratedCode ?? "");
        Assert.Equal(!compiles, compilation.HasErrors);
    }

    [Theory]
    [InlineData("§B{x} §ARR2D{str} §ROW §C{Console.ReadLine} §/C §/ARR2D", true)]
    [InlineData("§B{x} §ARR2D{str} §ROW \"r\" §/ARR2D", false)]
    public void RowElements_AreCharged(string statement, bool undeclaredEffect)
    {
        var nativeStatement = statement switch
        {
            "§B{x} §ARR2D{str} §ROW §C{Console.ReadLine} §/C §/ARR2D" =>
                "let x = new string?[,] { { System.Console.ReadLine() } }",
            "§B{x} §ARR2D{str} §ROW \"r\" §/ARR2D" => "let x = new string?[,] { { \"r\" } }",
            _ => throw new ArgumentOutOfRangeException(nameof(statement), statement, null)
        };
        var calor = $$"""
            module T
              module G
                func Go public () → void
                  ! alloc
                  {{nativeStatement}}
            """;
        var compilation = Program.Compile(calor, "effects.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Equal(undeclaredEffect, compilation.Diagnostics.Errors.Any(d => d.Code == "Calor0410"));
        Assert.All(compilation.Diagnostics.Errors, diagnostic =>
            Assert.True(diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cr'", StringComparison.Ordinal), diagnostic.ToString()));
    }

    // Narrowed (PR 2): elements Calor writes as a block. The extra verification pass's
    // MAJOR witness (a multi-line anonymous object in a rectangular statement array) and
    // collection initializers as elements (their block was emitted ahead of the array
    // and the element itself dropped: `{ new List<int> { 1 }, 2 }` had one element).
    private const string BlockElements = """
        using System.Collections.Generic;

        public delegate int D(int x);

        public static class W
        {
            // PR 2 Codex round 1: a block lambda element was bound to an untyped temp
            // ahead of the array, which loses its delegate type (Func<int, int> is not D).
            public static string Lambda()
            {
                D[] ds = new D[] { (int x) => { int y = x; y = y + 1; return y; } };
                return "" + ds.Length;
            }

            public static string Witness()
            {
                object[,] a = { { new { A = 1 } } };
                return a.Length + "," + a[0, 0];
            }

            public static string List1D()
            {
                object[] b = { new List<int> { 1 }, 2 };
                return "" + b.Length;
            }

            public static string ListRect()
            {
                return "" + new object[,] { { new List<int> { 3 }, 4 } }.Length;
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                return W.Witness() + "|" + W.List1D() + "|" + W.ListRect() + "|" + W.Lambda();
            }
        }
        """;

    [Theory]
    [MemberData(nameof(SurfacesOnly))]
    public async Task BlockElements_ArePreservedWithAReport_AndRunLikeTheOriginal(Surface surface)
    {
        Assert.Equal("1,{ A = 1 }|2|2|1", Run(BlockElements));
        var (calor, losses) = await ConvertAsync(BlockElements, surface);
        // Each statement is preserved as C# (or its member, where statements are not),
        // and reported; none is written natively with a lost or misplaced element.
        Assert.NotEmpty(losses);
        Assert.All(losses, loss => Assert.Equal("array-element-block", loss));
        RawFrameAssertions.AssertFramed(calor, present: true);
        var compilation = Program.Compile(calor, "block.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Single(losses);
        var owner = Assert.Single(compilation.Ast!.Classes, type => type.Name == "W");
        Assert.Empty(owner.InteropBlocks);
        var lambda = Assert.Single(owner.Methods, method => method.Name == "Lambda");
        var preserved = Assert.Single(lambda.Body.OfType<Calor.Compiler.Ast.RawCSharpNode>());
        var originalMethod = Assert.Single(CSharpSyntaxTree.ParseText(BlockElements).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == "Lambda");
        Assert.Equal(originalMethod.Body!.Statements[0].ToString(), preserved.CSharpCode.Trim());
        Assert.Equal(new[] { "Lambda", "Witness", "List1D", "ListRect" },
            owner.Methods.Select(method => method.Name));
        Assert.All(owner.Methods.Where(method => method.Name != "Lambda"),
            method => Assert.Empty(method.Body.OfType<Calor.Compiler.Ast.RawCSharpNode>()));
        Assert.Equal(Run(BlockElements), Run(compilation.GeneratedCode));
    }

    public static TheoryData<Surface> SurfacesOnly()
    {
        var data = new TheoryData<Surface>();
        foreach (var surface in Enum.GetValues<Surface>())
            data.Add(surface);
        return data;
    }

    private static string Source(string row) => row switch
    {
        "ordering" => Ordering,
        "placement" => Placement,
        "shapes" => Shapes,
        "round2" => Round2,
        "round3" => Round3,
        "pr2-args" => Pr2Arguments,
        _ => R0Fixture(row)
    };

    private static string R0Fixture(string id) => File.ReadAllText(Path.Combine(
        CliTestHarness.FindRepoRoot(), R0Fixtures, id + ".cs.txt"));

    private static async Task<(string Calor, List<string> Losses)> ConvertAsync(string source, Surface surface)
    {
        switch (surface)
        {
            case Surface.LibraryDefault:
                return Library(new ConversionOptions());
            case Surface.CliDefault or Surface.CliPassthrough:
                return Library(ConvertCommand.BuildCSharpToCalorOptions(
                    benchmark: false, verbose: false, explain: false, noFallback: false,
                    passthrough: surface == Surface.CliPassthrough, explicitCallClosers: false));
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
                var dir = Path.Combine(Path.GetTempPath(), "calor-f5-1132-" + Guid.NewGuid().ToString("N"));
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

    private static IEnumerable<BoundNode> BoundDescendants(BoundNode node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
            foreach (var descendant in BoundDescendants(child))
                yield return descendant;
    }

    private static string Run(string csharp, string? additionalSource = null)
    {
        var compilation = CSharpCompilation.Create("F5_" + Guid.NewGuid().ToString("N"),
            additionalSource is null
                ? [CSharpSyntaxTree.ParseText(csharp)]
                : [CSharpSyntaxTree.ParseText(csharp), CSharpSyntaxTree.ParseText(additionalSource)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics) + "\n" + csharp);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var probe = context.LoadFromStream(stream).GetTypes().Single(type => type.Name == "Probe");
            var run = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            return Assert.IsType<string>(run.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }
}
