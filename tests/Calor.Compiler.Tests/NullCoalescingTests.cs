using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using DiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;
using NullableAnnotation = Calor.Compiler.Binding.BoundTypes.NullableAnnotation;

namespace Calor.Compiler.Tests;

public class NullCoalescingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Original095_ProjectConversion_IsStrictNativeWithExactMetadata(bool implicitSystem)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Directory.Build.props")))
            root = Path.GetDirectoryName(root)!;
        var path = Path.Combine(root, "tests", "TestData", "CSharpImport", "Level5_Advanced", "095_NullCoalescing.cs");
        var bytes = File.ReadAllBytes(path);
        var source = File.ReadAllText(path);
        var converter = implicitSystem
            ? new CSharpToCalorConverter(new ConversionOptions
            {
                IncludeImplicitSystemUsing = true,
                NullableContextOptions = NullableContextOptions.Disable,
                ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
            })
            : new CSharpToCalorConverter();
        var project = converter.ConvertProject(source, path);
        Assert.True(project.Success);
        var conversion = Assert.Single(project.Outputs).Conversion;
        Assert.Empty(conversion.Losses);
        Assert.Equal(0, conversion.InteropPreservationCount);
        foreach (var text in new[] { conversion.CalorSource!, new CalorEmitter().Emit(Parse(conversion.CalorSource!, out _)) })
        {
            var compiled = Program.Compile(text, "095.calr");
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
            var tree = CSharpSyntaxTree.ParseText(compiled.GeneratedCode);
            var image = CSharpCompilation.Create("ConditionalSelection", [tree],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(image.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
            var invocation = Assert.Single(tree.GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>());
            var selected = Assert.IsAssignableFrom<IMethodSymbol>(image.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol);
            Assert.Equal(SpecialType.System_Int32, selected.ContainingType.SpecialType);
            Assert.Equal("ToString", selected.Name);
        }
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ConditionalContinuation_BindsUnderlyingMemberAndLiftsOnlyTheResult()
    {
        const string source = """
            module Conditional
              func Probe (string?:value) → string?
                !
                return value?.Length.ToString()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source, out _));
        var conditional = Assert.IsType<BoundConditionalAccessExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions.Single().Body)).Expression);
        Assert.Equal(NullableAnnotation.Annotated, Assert.IsType<NominalBoundType>(conditional.Type).NullableAnnotation);
        var call = Assert.IsType<BoundCallExpression>(conditional.WhenNotNull);
        Assert.Equal(SpecialType.System_Int32, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
        var length = Assert.IsType<BoundFieldAccessExpression>(call.Receiver);
        var property = Assert.IsAssignableFrom<IPropertySymbol>(length.ResolvedMetadataMember);
        Assert.Equal(SpecialType.System_String, property.ContainingType.SpecialType);
        Assert.Equal(SpecialType.System_Int32, property.Type.SpecialType);
        Assert.Empty(length.Target.Children);
        Assert.Equal(NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(length.Target.Type).NullableAnnotation);
        Assert.Equal([call], conditional.DeferredChildren);
        Assert.IsType<BoundVariableExpression>(conditional.Receiver);
        Assert.False(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("string", "value?.Length.ToString()")]
    [InlineData("int", "System.Int32.Parse(value?.ToString())")]
    [InlineData("string[]", "value?.Split(new char[0])")]
    public void ConditionalResult_DoesNotSuppressReceivingChecks(string resultType, string expression)
    {
        var result = Program.Compile($$"""
            module Conditional
              func Probe (string?:value) → {{resultType}}
                !
                return {{expression}}
            """);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, item => item.Code is
            DiagnosticCode.NullableReturnFromNonNullable or DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Theory]
    [InlineData("string[]", "new string[0]", "string[]")]
    [InlineData("List<string?>", "new List<string?>()", "List<string>")]
    public void ConditionalSourceResults_RetainArrayContainerAndGenericPayloadChecks(
        string type, string value, string receiving)
    {
        var result = Program.Compile($$"""
            module Conditional
              using System.Collections.Generic
              class Box public
                func Read public () → {{type}}
                  ! alloc
                  return {{value}}
              func Probe (Box?:value) → {{receiving}}
                ! alloc
                return value?.Read()
            """);
        Assert.Contains(result.Diagnostics.Errors, item => item.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Theory]
    [InlineData("string?:value", "value?.Length")]
    [InlineData("int[]?:value", "value?.Length")]
    public void ConditionalValueResults_AreLiftedToActualNullableValueTypes(string parameter, string expression)
    {
        var source = $"module Conditional\n  func Probe ({parameter}) → int?\n    return {expression}\n";
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source, out _));
        var result = Assert.IsType<BoundConditionalAccessExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions.Single().Body)).Expression);
        var lifted = Assert.IsType<GenericInstantiationBoundType>(result.Type);
        Assert.Equal(SpecialType.System_Nullable_T, lifted.RoslynSymbol!.OriginalDefinition.SpecialType);
        Assert.Equal(SpecialType.System_Int32, lifted.RoslynSymbol.TypeArguments.Single().SpecialType);
    }

    [Fact]
    public void ConditionalNullableValueReceiver_SelectsTheUnderlyingMethod()
    {
        const string source = """
            module Conditional
              func Probe (int?:value) → string?
                !
                return value?.ToString()
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var bound = new Binder(new DiagnosticBag()).Bind(compiled.Ast!);
        var conditional = Assert.IsType<BoundConditionalAccessExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions.Single().Body)).Expression);
        Assert.Equal(SpecialType.System_Int32,
            Assert.IsType<BoundCallExpression>(conditional.WhenNotNull).ResolvedMetadataMethod!.ContainingType.SpecialType);
        const string original = """
            public static class Probe
            {
                public static string? Read(int? value) => value?.ToString();
            }
            """;
        AssertNativeRuntimeAndReemission(original,
            "public static class Witness { public static string Run() => (Probe.Read(null) ?? \"null\") + \"|\" + Probe.Read(42); }", "null|42",
            enforceEffects: true);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("System.DateTime")]
    public void ConditionalNonNullableValueReceivers_AreRejected(string type)
    {
        var result = Program.Compile($$"""
            module Conditional
              func Probe ({{type}}:value) → string?
                !
                return value?.ToString()
            """);
        Assert.Contains(result.Diagnostics.Errors, item => item.Message.Contains("CS0023", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "fallback|1|0")]
    [InlineData(true, "BC|1|1")]
    public void ConditionalContinuation_NestedAccessSkipsArgumentsAndEvaluatesReceiverOnce(bool present, string expected)
    {
        const string source = """
            public static class Probe
            {
                private static int Reads;
                private static int Arguments;
                private static string? Target(bool present) { Reads++; return present ? "abc" : null; }
                private static int Argument() { Arguments++; return 1; }
                public static string Run(bool present)
                {
                    Reads = Arguments = 0;
                    var value = Target(present)?.Substring(Argument())?.ToUpperInvariant() ?? "fallback";
                    return value + "|" + Reads + "|" + Arguments;
                }
            }
            """;
        AssertNativeRuntimeAndReemission(source,
            $"public static class Witness {{ public static string Run() => Probe.Run({present.ToString().ToLowerInvariant()}); }}",
            expected, enforceEffects: true);
    }

    [Theory]
    [InlineData("value?.Length.ToString()", "null|0|3")]
    [InlineData("(value?.Length).ToString()", "|0|3")]
    public void ConditionalParentheses_DistinguishNativeContinuationFromLiftedNullableReceiver(
        string expression, string expected)
    {
        var source = $$"""
            public static class Probe
            {
                private static string Read(string? value) => {{expression}};
                public static string Run() => (Read(null) ?? "null") + "|" + Read("") + "|" + Read("abc");
            }
            """;
        var actual = ExecuteNativeWitness(source, "public static class Witness { public static string Run() => Probe.Run(); }");
        Assert.Equal(expected, actual);
        if (expression.StartsWith('('))
        {
            var rejected = Program.Compile("""
                module Conditional
                  func Probe (string?:value) → string
                    return (value?.Length).ToString()
                """);
            Assert.True(rejected.HasErrors);
            Assert.Contains(rejected.Diagnostics.Errors, item => item.Message.Contains("not yet supported", StringComparison.Ordinal));
            var conversion = new CSharpToCalorConverter().ConvertProject(source, "GroupedConditional.cs");
            Assert.True(conversion.Success);
            var output = Assert.Single(conversion.Outputs).Conversion;
            Assert.Empty(output.Losses);
            Assert.Equal(0, output.InteropPreservationCount);
            var compiled = Program.Compile(output.CalorSource!);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(actual, ExecuteNativeWitness(compiled.GeneratedCode,
                "public static class Witness { public static string Run() => Probe.Run(); }"));
            Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
            var tree = CSharpSyntaxTree.ParseText(compiled.GeneratedCode);
            var image = CSharpCompilation.Create("GroupedSelection", [tree],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = Assert.Single(tree.GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Where(call => call.Expression.ToString().EndsWith(".ToString", StringComparison.Ordinal)));
            var selected = Assert.IsAssignableFrom<IMethodSymbol>(image.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol);
            Assert.Equal(SpecialType.System_Nullable_T, selected.ContainingType.OriginalDefinition.SpecialType);
        }
        else
            AssertNativeRuntimeAndReemission(source,
                "public static class Witness { public static string Run() => Probe.Run(); }", actual);
    }

    [Theory]
    [InlineData("System.Globalization.TextInfo", "CultureName.ToString()")]
    [InlineData("Missing", "Length.ToString()")]
    public void ConditionalUnknownReceiversAndGetters_RemainFailClosed(string type, string tail)
    {
        var result = Program.Compile($$"""
            module Conditional
              func Probe ({{type}}?:value) → string?
                !
                return value?.{{tail}}
            """);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, item => item.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void ConditionalSourceShadow_DoesNotBorrowFrameworkGetterPurity()
    {
        const string source = """
            module Conditional Conditional
              class String public
                property int:Length public
                  get
                    System.Console.WriteLine(1)
                    return 3
              func Probe (Conditional.String?:value) → string?
                !
                return value?.Length.ToString()
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, item => item.Code == DiagnosticCode.ForbiddenEffect
            && item.Message.Contains("cw", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("null", "null|0")]
    [InlineData("\"aax\"", "bbx|1")]
    public void ConditionalMethodInConstructor_PreservesSelectionAndArgumentLaziness(
        string value, string expected)
    {
        const string source = """
            module Conditional Conditional
              class Box public
                field string?:Location public
                ctor public (string?:value)
                  set this.Location value?.Replace(Probe.Argument(), "b")
              module Probe public
                field int:Calls public static
                func Argument public () → string
                  ! mut
                  set Calls (Calls + 1)
                  return "a"
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(compiled.Ast!);
        var selected = bound.Functions.SelectMany(function => Descendants(function))
            .OfType<BoundCallExpression>().Single(call => call.ResolvedMetadataMethod?.Name == "Replace");
        Assert.Equal(SpecialType.System_String, selected.ResolvedMetadataMethod!.ContainingType.SpecialType);
        Assert.Equal([SpecialType.System_String, SpecialType.System_String],
            selected.ResolvedMetadataMethod.Parameters.Select(parameter => parameter.Type.SpecialType));
        var witness = $$"""
            public static class Witness {
                public static string Run() {
                    var box = new Conditional.Box({{value}});
                    return (box.Location ?? "null") + "|" + Conditional.Probe.Calls;
                }
            }
            """;
        Assert.Equal(expected, ExecuteNativeWitness(compiled.GeneratedCode, witness));
        var reemitted = new CalorEmitter().Emit(compiled.Ast!);
        var reparsed = Program.Compile(reemitted);
        Assert.False(reparsed.HasErrors, string.Join("\n", reparsed.Diagnostics));
        Assert.Equal(expected, ExecuteNativeWitness(reparsed.GeneratedCode, witness));
    }

    private static IEnumerable<BoundNode> Descendants(BoundNode node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    private static string[] DeclaredMetadata(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Metadata", [tree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
        return CSharpToCalorConverter.DeclaredTypeMetadata(compilation.GetSemanticModel(tree), tree.GetCompilationUnitRoot());
    }

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
        string source, string witness, string expected, string sourceFile = "NullCoalescingNative.cs",
        bool enforceEffects = false)
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
                new CompilationOptions { EnforceEffects = enforceEffects, StatusWriter = TextWriter.Null });
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
            module Test Test block
              func Main ({{parameters}}) → {{returnType}}
                !
                {{body}}
            """;
    }

    [Fact]
    public void Parse_NullCoalescing_CreatesNullCoalesceNode()
    {
        var source = WrapInFunction(
            "return a ?? b",
            "object:a, object:b");
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
            "return a ?? b",
            "object:a, object:b");
        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join(", ", result.Diagnostics.Select(d => d.Message)));
        Assert.Contains("a ?? b", result.GeneratedCode);
    }

    [Fact]
    public void Parse_ChainedNullCoalescing_CreatesNestedNodes()
    {
        var source = WrapInFunction(
            "return a ?? b ?? c",
            "object:a, object:b, object:c");
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
            "return a ?? b ?? c",
            "object:a, object:b, object:c");
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
            "return value ?? 0",
            "int?:value",
            "int");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.Single(func.Parameters);
        Assert.Equal("int?", func.Parameters[0].TypeName);
    }
}
