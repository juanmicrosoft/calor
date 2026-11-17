using System.Reflection;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

public sealed class NativeMetadataClosureTests
{
    [Fact]
    public void SourceClassArray_DefaultConversionPreservesTheOriginalFixtureAndMetadata()
    {
        var source = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests/TestData/InteropScope/R0/fixtures/F5-ARRAY-01.cs.txt"));
        var conversion = new CSharpToCalorConverter().Convert(source, "F5-ARRAY-01.cs");
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Empty(conversion.Losses);
        RawFrameAssertions.AssertFramed(conversion.CalorSource!, present: false);
        Assert.Contains("nullable oblivious", conversion.CalorSource!);
        var original = CompileAssembly(source);
        var originalTree = CSharpSyntaxTree.ParseText(source);
        var metadataCompilation = CSharpCompilation.Create("ArrayMetadata", [originalTree],
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var before = CSharpToCalorConverter.DeclaredTypeMetadata(
            metadataCompilation.GetSemanticModel(originalTree), originalTree.GetCompilationUnitRoot());
        foreach (var native in new[] { conversion.CalorSource!, new CalorEmitter().Emit(Parse(conversion.CalorSource!)) })
        {
            var compiled = Compile(native);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Empty(compiled.Diagnostics);
            var bound = new Binder(new DiagnosticBag()).Bind(compiled.Ast!);
            var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
                .Where(call => call.ResolvedMethodName == "GetLength").ToArray();
            Assert.Equal(2, calls.Length);
            Assert.All(calls, call =>
            {
                var method = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
                Assert.Equal(SpecialType.System_Array, method.ContainingType.SpecialType);
                Assert.False(method.IsStatic);
                Assert.False(method.IsGenericMethod);
                Assert.Equal(SpecialType.System_Int32, method.ReturnType.SpecialType);
                Assert.Equal(SpecialType.System_Int32, Assert.Single(method.Parameters).Type.SpecialType);
                var shape = Assert.IsType<Calor.Compiler.Binding.BoundTypes.ArrayBoundType>(
                    call.Receiver!.MethodInputArrayType);
                Assert.Equal(2, shape.Rank);
                Assert.Equal(Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Oblivious, shape.NullableAnnotation);
                var element = Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(shape.ElementType);
                Assert.Equal("Cell", element.Declaration?.QualifiedName);
                Assert.Equal(Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Oblivious, element.NullableAnnotation);
                var key = EffectResolverKey.FromBoundReceiver(
                    Calor.Compiler.Binding.Metadata.MetadataBinderResult.ToNominalBoundType(method.ContainingType),
                    method.Name, call.ResolvedParameterTypes);
                var contract = new EffectResolver().Resolve(key);
                Assert.Equal(EffectResolutionStatus.PureExplicit, contract.Status);
                Assert.Equal("embedded:Calor.Compiler.Manifests.bcl-system.calor-effects.json", contract.Source);
            });
            var reads = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>()
                .Where(read => read.FieldName == "V").ToArray();
            Assert.Equal(2, reads.Length);
            Assert.All(reads, read =>
            {
                Assert.True(read.ResolvedField is { IsField: true, IsStatic: false, DeclaringTypeName: "Cell" });
                Assert.Same(bound.SymbolsById[read.ResolvedField!.Id], read.ResolvedField);
                Assert.Same(Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(read.Target.Type).Declaration,
                    bound.SymbolsById.Values.OfType<TypeSymbol>().Single(type => type.Name == "Cell"));
                Assert.False(read.HasUnknownInferredType);
            });
            var generatedTree = CSharpSyntaxTree.ParseText(compiled.GeneratedCode);
            var generatedCompilation = metadataCompilation.ReplaceSyntaxTree(originalTree, generatedTree);
            Assert.Equal(before, CSharpToCalorConverter.DeclaredTypeMetadata(
                generatedCompilation.GetSemanticModel(generatedTree), generatedTree.GetCompilationUnitRoot()));
            var generated = CompileAssembly(compiled.GeneratedCode);
            Assert.Equal("2x2|2|3", original.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
            Assert.Equal("2x2|2|3", generated.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
            foreach (var assembly in new[] { original, generated })
            {
                var result = assembly.GetType("Grid")!.GetMethod("Build")!;
                Assert.Equal(2, result.ReturnType.GetArrayRank());
                Assert.Same(assembly.GetType("Cell"), result.ReturnType.GetElementType());
                Assert.Equal(typeof(int), assembly.GetType("Cell")!.GetField("V")!.FieldType);
                var nullable = new NullabilityInfoContext().Create(result.ReturnParameter);
                Assert.Equal(NullabilityState.Unknown, nullable.ReadState);
                Assert.Equal(NullabilityState.Unknown, nullable.ElementType!.ReadState);
            }
        }
    }

    [Fact]
    public void SourceClassArray_ReadsRetainReceiverIndexAndArgumentExceptionOrdering()
    {
        const string source = """
            using System;
            public class Cell { public int V; }
            public static class Probe
            {
                private static string Log = "";
                private static Cell[,] Values;
                private static Cell[,] Receiver() { Log += "r"; return Values; }
                private static int Index(int value, string mark) { Log += mark; return value; }
                private static int Stop() { Log += "t"; throw new InvalidOperationException(); }
                public static string Run()
                {
                    Values = new Cell[,] { { new Cell { V = 7 } } };
                    Log = "";
                    int value = Receiver()[Index(0, "a"), Index(0, "b")].V;
                    string result = value + ":" + Log;
                    Log = "";
                    int length = Receiver().GetLength(Index(0, "d"));
                    result += "|" + length + ":" + Log;
                    Log = "";
                    try { int bad = Receiver()[Index(9, "a"), Index(0, "b")].V; }
                    catch (IndexOutOfRangeException) { result += "|" + Log; }
                    Log = "";
                    try { int bad = Receiver()[Stop(), Index(0, "b")].V; }
                    catch (InvalidOperationException) { result += "|" + Log; }
                    Log = "";
                    try { int bad = Receiver().GetLength(Index(9, "d")); }
                    catch (IndexOutOfRangeException) { result += "|" + Log; }
                    Log = "";
                    Values = null;
                    try { int bad = Receiver()[Index(0, "a"), Index(0, "b")].V; }
                    catch (NullReferenceException) { result += "|" + Log; }
                    Log = "";
                    try { int bad = Receiver().GetLength(Index(0, "d")); }
                    catch (NullReferenceException) { result += "|" + Log; }
                    return result;
                }
            }
            """;
        const string expected = "7:rab|1:rd|rab|rt|rd|rab|rd";
        Assert.Equal(expected, CompileAssembly(source).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        var converted = new CSharpToCalorConverter().Convert(source, "ArrayOrdering.cs");
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        foreach (var native in new[] { converted.CalorSource!, new CalorEmitter().Emit(Parse(converted.CalorSource!)) })
        {
            var compiled = Compile(native);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(expected, CompileAssembly(compiled.GeneratedCode).GetType("Probe")!
                .GetMethod("Run")!.Invoke(null, null));
            // Private helpers carry no row in 0.30 (Calor0125); Run declares the only one,
            // `! throw, alloc, mut`. Dropping `mut` from it must be refused.
            Assert.Contains("! throw, alloc, mut", native);
            var withoutMutation = Compile(native.Replace("! throw, alloc, mut", "! throw, alloc", StringComparison.Ordinal));
            Assert.Contains(withoutMutation.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        }
    }

    [Theory]
    [InlineData("(Receiver(values)[0, 0]).V")]
    [InlineData("(values[Mark(0), 0]).V")]
    [InlineData("(values[0, Mark(0)]).V")]
    [InlineData("Receiver(values).GetLength(0)")]
    [InlineData("values.GetLength(Mark(0))")]
    public void SourceClassArray_SelectedReadsRetainEveryEvaluatedChildEffect(string expression)
    {
        var source = $$"""
            module SourceArrayEvaluation
              class Cell public
                field int:V public
              func Receiver (Cell[,]:values) → Cell[,]
                ! cw
                System.Console.WriteLine("receiver")
                return values
              func Mark (int:value) → int
                ! cw
                System.Console.WriteLine("index")
                return value
              func Read (Cell[,]:values) → int
                !
                return {{expression}}
            """;
        var rejected = Compile(source);
        Assert.Contains(rejected.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Function 'Read'", StringComparison.Ordinal)
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
        var accepted = Compile(source.Replace("    !\n", "    ! cw\n", StringComparison.Ordinal));
        Assert.False(accepted.HasErrors, string.Join("\n", accepted.Diagnostics));
    }

    [Theory]
    [InlineData("Missing[]", "")]
    [InlineData("Cell[]", "<Cell>")]
    [InlineData("Cell[]?", "")]
    public void SourceClassArray_UnestablishedGenericOrNullableReceiversRemainUnselected(string type, string parameters)
    {
        var source = $$"""
            module SourceArrayBoundary
              class Cell public
                field int:V public
              func Read{{parameters}} ({{type}}:values) → int
                !
                return values.GetLength(0)
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("Array")]
    [InlineData("Cell")]
    public void SourceClassArray_SameSpelledNonArrayReceiversDoNotBorrowArrayContracts(string type)
    {
        var source = $$"""
            module SourceArrayShadow
              class {{type}} public
                func GetLength public (int:dimension) → int
                  ! cw
                  System.Console.WriteLine("source")
                  return dimension
              func Read ({{type}}:values) → int
                !
                return (values).GetLength(0)
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        Assert.All(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => Assert.NotEqual(SpecialType.System_Array, call.ResolvedMetadataMethod?.ContainingType.SpecialType));
        Assert.Contains(Compile(source).Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("sbyte", "byte")]
    [InlineData("byte", "long")]
    [InlineData("short", "ushort")]
    [InlineData("short", "byte")]
    [InlineData("uint", "long")]
    [InlineData("long", "ulong")]
    [InlineData("byte", "ushort")]
    [InlineData("byte", "char")]
    [InlineData("byte", "int")]
    public void NativeIntegerLiteralOverloads_SelectTheOriginalCSharpWinner(string first, string second)
    {
        foreach (var reverse in new[] { false, true })
        {
            var a = reverse ? second : first;
            var b = reverse ? first : second;
            var source = $$"""
                public static class Probe
                {
                    private static string Pick({{a}} value) => "{{a}}";
                    private static string Pick({{b}} value) => "{{b}}";
                    public static string Run() => Pick(1);
                }
                """;
            var expected = CompileAssembly(source).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
            var converted = new CSharpToCalorConverter().Convert(source);
            Assert.True(converted.Success, string.Join("\n", converted.Issues));
            Assert.Empty(converted.Losses);
            RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
            AssertSelectedLiteralOverload(converted.CalorSource!, Assert.IsType<string>(expected));
            var compiled = Compile(converted.CalorSource!);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
            var actual = CompileAssembly(compiled.GeneratedCode).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData("byte value, int ignored = 0", "long value")]
    [InlineData("byte value", "params int[] values")]
    [InlineData("params byte[] values", "long value")]
    [InlineData("byte value", "byte value, int ignored = 0")]
    [InlineData("byte value", "params byte[] values")]
    [InlineData("byte value, long later = 0", "long value, byte later = 0")]
    public void NativeIntegerLiteralOverloads_CompareTargetsBeforeOptionalAndParamsPenalties(
        string first, string second)
    {
        foreach (var reverse in new[] { false, true })
        {
            var a = reverse ? second : first;
            var b = reverse ? first : second;
            var original = $$"""
                public static class Probe
                {
                    private static string Pick({{a}}) => "first";
                    private static string Pick({{b}}) => "second";
                    public static string Run() => Pick(1);
                }
                """;
            var expected = Assert.IsType<string>(
                CompileAssembly(original).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
            var converted = new CSharpToCalorConverter().Convert(original);
            Assert.True(converted.Success, string.Join("\n", converted.Issues));
            Assert.Empty(converted.Losses);
            RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
            AssertSelectedLiteralOverload(converted.CalorSource!, expected);
            var compiled = Compile(converted.CalorSource!);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
            Assert.Equal(expected, CompileAssembly(compiled.GeneratedCode).GetType("Probe")!
                .GetMethod("Run")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("small: 255, big: 65535", false)]
    [InlineData("big: 65535, small: 255", false)]
    [InlineData("small: 256, big: 1", true)]
    [InlineData("big: 1, small: 256", true)]
    public void NativeIntegerLiteralArguments_UseArgumentPositionsAfterNamedMapping(string arguments, bool invalid)
    {
        var source = $$"""
            module NamedConstants
              func Pick (byte:small, ushort:big) → int
                !
                return small + big
              func Run public () → int
                !
                return Pick({{arguments}})
            """;
        var compiled = Compile(source);
        Assert.Equal(invalid, compiled.HasErrors);
        if (invalid)
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        else
            Assert.Equal(65790, CompileAssembly(compiled.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods()).Single(method => method.Name == "Run").Invoke(null, null));
    }

    [Fact]
    public void NativeIntegerLiteralArguments_KeepReferenceStorageAndByteNarrowing()
    {
        const string source = """
            public static class Probe
            {
                private static byte Update(ref byte value, byte later)
                {
                    value += later;
                    return value;
                }
                public static string Run()
                {
                    byte value = 255;
                    byte seen = Update(ref value, 1);
                    return seen + ":" + value;
                }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        var compiled = Compile(converted.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal("0:0", CompileAssembly(source).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        Assert.Equal("0:0", CompileAssembly(compiled.GeneratedCode).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public void NativeIntegerLiteralArguments_RetainSourceEnumExtensionReceiverMapping(int value, bool valid)
    {
        const string original = """
            public enum Mode { First = 1 }
            public static class Extensions
            {
                public static int Read(this Mode mode, byte delta) => delta;
            }
            public static class Probe
            {
                public static int Run() => Mode.First.Read(255);
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        Assert.Contains("Read(255)", converted.CalorSource!);
        var source = converted.CalorSource!.Replace("Read(255)", $"Read({value})", StringComparison.Ordinal);
        var compiled = Compile(source);
        Assert.Equal(!valid, compiled.HasErrors);
        if (valid)
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
            Assert.Equal("Extensions.Read", call.ResolvedSymbol?.Name);
            Assert.Equal(CompileAssembly(original).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null),
                CompileAssembly(compiled.GeneratedCode).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
        }
        else
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Theory]
    [InlineData("sbyte", "-128", true)]
    [InlineData("sbyte", "-129", false)]
    [InlineData("byte", "255", true)]
    [InlineData("byte", "256", false)]
    [InlineData("short", "-32768", true)]
    [InlineData("short", "-32769", false)]
    [InlineData("ushort", "65535", true)]
    [InlineData("ushort", "65536", false)]
    [InlineData("ulong", "1L", true)]
    public void NativeIntegerLiteralArguments_CheckParsedValuesIncludingUnaryMinus(
        string type, string argument, bool valid)
    {
        var source = $$"""
            module ConstantRange
              func Pick ({{type}}:value) → int
                !
                return 1
              func Run public () → int
                !
                return Pick({{argument}})
            """;
        var compiled = Compile(source);
        Assert.Equal(!valid, compiled.HasErrors);
        if (valid)
            Assert.Equal(1, CompileAssembly(compiled.GeneratedCode).GetTypes()
                .SelectMany(owner => owner.GetMethods()).Single(method => method.Name == "Run").Invoke(null, null));
        else
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("out")]
    [InlineData("in")]
    public void NativeIntegerLiteralArguments_DoNotAuthorizeByReferenceConversions(string modifier)
    {
        var body = modifier == "out" ? "! mut\n    set value 0" : "!\n    return";
        var source = $$"""
            module RefConstants
              func Pick ({{modifier}} byte:value) → void
                {{body}}
              func Run () → void
                ! mut
                Pick({{modifier}} 1)
            """;
        Parse(source);
        var compiled = Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.NoMatchingOverload or DiagnosticCode.InvalidByReferenceOperand);
    }

    [Theory]
    [InlineData("byte", "ushort", "ushort", "byte")]
    [InlineData("byte", "long", "long", "int")]
    public void NativeIntegerLiteralOverloads_RetainAmbiguityWhenDifferentArgumentsPreferDifferentTargets(
        string firstA, string secondA, string firstB, string secondB)
    {
        var source = $$"""
            module AmbiguousConstants
              func Pick ({{firstA}}:first, {{secondA}}:second) → int
                !
                return 1
              func Pick ({{firstB}}:first, {{secondB}}:second) → int
                !
                return 2
              func Run () → int
                !
                return Pick(1, 1)
            """;
        var compiled = Compile(source);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
    }

    [Theory]
    [InlineData("byte", "ushort", false)]
    [InlineData("ushort", "byte", true)]
    public void NativeIntegerLiteralOverloads_CombineNullAndLiteralTargetPreferences(
        string stringValueType, string objectValueType, bool ambiguous)
    {
        var original = $$"""
            public static class Probe
            {
                private static string Pick(string? text, {{stringValueType}} value) => "string";
                private static string Pick(object? text, {{objectValueType}} value) => "object";
                public static string Run() => Pick(null, 1);
            }
            """;
        var source = $$"""
            module MixedConstants
              func Pick (string?:text, {{stringValueType}}:value) → string
                !
                return "string"
              func Pick (object?:text, {{objectValueType}}:value) → string
                !
                return "object"
              func Run public () → string
                !
                return Pick(null, 1)
            """;
        var compiled = Compile(source);
        if (ambiguous)
        {
            var oracle = CSharpCompilation.Create("AmbiguousConstants",
                [CSharpSyntaxTree.ParseText(original)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.Contains(oracle.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0121");
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
        }
        else
        {
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
            var expected = CompileAssembly(original).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null);
            AssertSelectedLiteralOverload(source, Assert.IsType<string>(expected));
            var actual = CompileAssembly(compiled.GeneratedCode).GetTypes()
                .SelectMany(owner => owner.GetMethods()).Single(method => method.Name == "Run").Invoke(null, null);
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("255", true)]
    [InlineData("256", false)]
    public void NativeIntegerLiteralArguments_RespectExplicitGenericSubstitution(string argument, bool valid)
    {
        var source = $$"""
            module GenericConstants
              func Pick<T> (T:value) → T
                !
                return value
              func Run public () → int
                !
                return Pick<byte>({{argument}})
            """;
        var compiled = Compile(source);
        Assert.Equal(!valid, compiled.HasErrors);
        if (valid)
            Assert.Equal(int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture),
                CompileAssembly(compiled.GeneratedCode).GetTypes()
                    .SelectMany(owner => owner.GetMethods()).Single(method => method.Name == "Run").Invoke(null, null));
        else
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Theory]
    [InlineData("new int[3]", 3)]
    [InlineData("new int[] { 1, 2 }", 2)]
    [InlineData("new int[2, 3]", 6)]
    [InlineData("new int[,] { { 1, 2 }, { 3, 4 } }", 4)]
    [InlineData("new int[2, 1, 3]", 6)]
    [InlineData("new int[][] { new int[0], new int[1] }", 2)]
    [InlineData("new string[2]", 2)]
    [InlineData("new string[] { \"a\", null }", 2)]
    public void NativeAllocatedArrayLength_UsesItsBoundShape(string allocation, int expected)
    {
        foreach (var receiver in new[] { "values", "(values)", "copy" })
        {
            var source = $$"""
                module AllocatedArrayLength
                  func Probe public () → int
                    ! alloc
                    try
                      let values = {{allocation}}
                      let copy = values
                      return {{receiver}}.Length
                    catch System.Exception:error
                      return -1
                """;
            foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
            {
                var result = Compile(candidate);
                Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
                var assembly = CompileAssembly(result.GeneratedCode);
                var method = assembly.GetTypes().SelectMany(type => type.GetMethods())
                    .Single(method => method.Name == "Probe");
                Assert.Equal(expected, method.Invoke(null, null));
                var pure = Compile(candidate.Replace("! alloc", "!", StringComparison.Ordinal));
                Assert.Contains(pure.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
            }
        }
    }

    [Theory]
    [InlineData("@int")]
    [InlineData("@value")]
    public void NativeAllocatedArrayLength_RetainsVerbatimReceiverIdentity(string name)
    {
        var source = $$"""
            module VerbatimArrayLength
              func Probe public () → int
                ! alloc
                let {{name}} = new int[3]
                return {{name}}.Length
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var assembly = CompileAssembly(result.GeneratedCode);
        Assert.Equal(3, assembly.GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Probe").Invoke(null, null));
    }

    [Theory]
    [InlineData("value.Length")]
    [InlineData("(value).Length")]
    public void NativeArrayLengthFacts_DoNotCertifyMetadataPropertyGetters(string access)
    {
        var source = $$"""
            module MetadataLengthGetter
              func Probe public (System.IO.FileInfo:value) → long
                !
                return {{access}}
            """;
        var rejected = Compile(source);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Probe")
            && diagnostic.Message.Contains("fs:r"));
        var accepted = Compile(source.Replace("    !\n", "    ! fs:r\n", StringComparison.Ordinal));
        Assert.False(accepted.HasErrors, string.Join("\n", accepted.Diagnostics));
    }

    [Theory]
    [InlineData("string", "string.Concat(\"a\", \"b\")", "ab")]
    [InlineData("int", "int.MaxValue", "2147483647")]
    [InlineData("long", "long.MaxValue", "9223372036854775807")]
    [InlineData("short", "short.MinValue", "-32768")]
    [InlineData("sbyte", "sbyte.MinValue", "-128")]
    [InlineData("byte", "byte.MaxValue", "255")]
    [InlineData("ushort", "ushort.MaxValue", "65535")]
    [InlineData("uint", "uint.MaxValue", "4294967295")]
    [InlineData("ulong", "ulong.MaxValue", "18446744073709551615")]
    [InlineData("float", "float.IsNaN(float.NaN)", "True")]
    [InlineData("double", "double.IsNaN(double.NaN)", "True")]
    [InlineData("decimal", "decimal.One", "1")]
    [InlineData("bool", "bool.Parse(\"true\")", "True")]
    [InlineData("char", "char.IsUpper('A')", "True")]
    public void NativePrimitiveStaticReceiver_RoundTripsWithoutBecomingAVerbatimVariable(
        string alias, string expression, string expected)
    {
        var source = $$"""
            module PrimitiveStaticReceiver
              func Probe public () → object
                !
                return {{expression}}
            """;
        var reemitted = new CalorEmitter().Emit(Parse(source));
        Assert.DoesNotContain("@" + alias + ".", reemitted);
        var converted = ConvertPrimitiveReceiver($$"""
            public static class PrimitiveStaticReceiver
            {
                public static object Probe() => {{expression}};
            }
            """);
        foreach (var candidate in new[] { source, reemitted, converted })
        {
            var assembly = CompileAssembly(new CSharpEmitter().Emit(Parse(candidate)));
            var result = assembly.GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "Probe").Invoke(null, null);
            Assert.Equal(expected, Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("short")]
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("ulong")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("char")]
    public void NativePrimitiveNamedVerbatimParameter_RetainsItsValueReceiver(string alias)
    {
        var source = $$"""
            module VerbatimPrimitiveReceiver
              func Probe public (string:@{{alias}}) → int
                !
                return @{{alias}}.Length
            """;
        var reemitted = new CalorEmitter().Emit(Parse(source));
        Assert.Contains("@" + alias + " . Length", reemitted);
        var structural = Assert.IsType<FieldAccessNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(Parse(reemitted).Functions).Body[0]).Expression);
        var reference = Assert.IsType<ReferenceNode>(structural.Target);
        Assert.Equal(alias, reference.Name);
        Assert.True(reference.IsVerbatim);
        var converted = ConvertPrimitiveReceiver($$"""
            public static class VerbatimPrimitiveReceiver
            {
                public static int Probe(string @{{alias}}) => @{{alias}}.Length;
            }
            """);
        foreach (var candidate in new[] { source, reemitted, converted })
        {
            var module = Parse(candidate);
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(module);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
            var receiver = Assert.IsType<BoundVariableExpression>(member.Target);
            Assert.Same(Assert.Single(Assert.Single(bound.Functions).Symbol.Parameters), receiver.Variable);
            Assert.Equal(alias, receiver.Variable.Name);
            var property = Assert.IsAssignableFrom<IPropertySymbol>(member.ResolvedMetadataMember);
            Assert.Equal("Length", property.Name);
            Assert.Equal(SpecialType.System_String, property.ContainingType.SpecialType);
            Assert.Equal(SpecialType.System_Int32, property.Type.SpecialType);
            var assembly = CompileAssembly(new CSharpEmitter().Emit(module));
            var method = assembly.GetTypes().SelectMany(type => type.GetMethods()).Single(method => method.Name == "Probe");
            Assert.Equal(3, method.Invoke(null, ["abc"]));
        }
    }

    [Theory]
    [InlineData("float . NaN", SpecialType.System_Single)]
    [InlineData("double . NaN", SpecialType.System_Double)]
    [InlineData("decimal . One", SpecialType.System_Decimal)]
    [InlineData("sbyte . MinValue", SpecialType.System_SByte)]
    [InlineData("char . MaxValue", SpecialType.System_Char)]
    public void NativePrimitiveConstants_RetainSelectedFieldTypeAndRuntimeBoxing(
        string expression, SpecialType expectedType)
    {
        var source = $$"""
            module PrimitiveConstant
              func Probe public () → object
                !
                return {{expression}}
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var read = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        var field = Assert.IsAssignableFrom<IFieldSymbol>(read.ResolvedMetadataMember);
        Assert.True(field.IsStatic);
        Assert.True(field.IsConst);
        Assert.Equal(expectedType, field.ContainingType.SpecialType);
        Assert.Equal(expectedType, field.Type.SpecialType);
        var resultType = Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(read.Type);
        Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(field.Type, resultType.RoslynSymbol));
        Assert.False(read.HasUnknownInferredType);

        var original = $$"""
            public static class PrimitiveConstant
            {
                public static object Probe() => {{expression}};
            }
            """;
        var expected = CompileAssembly(original).GetType("PrimitiveConstant")!.GetMethod("Probe")!.Invoke(null, null)!;
        var conversion = new CSharpToCalorConverter().Convert(original);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Empty(conversion.Losses);
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)), conversion.CalorSource! })
        {
            var compiled = Compile(candidate);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            var actual = CompileAssembly(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "Probe").Invoke(null, null)!;
            Assert.Equal(expected.GetType(), actual.GetType());
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData("char.IsDigit('A')", SpecialType.System_Char, true)]
    [InlineData("char.IsUpper('A')", SpecialType.System_Char, false)]
    // The restored 0.29 BCL manifest (12b1adb3e) declares Single.IsNaN pure, so the
    // discriminating pair is IsInfinity: covered for Double by its default row, not for Single.
    [InlineData("float.IsNaN(float.NaN)", SpecialType.System_Single, true)]
    [InlineData("float.IsInfinity(float.NaN)", SpecialType.System_Single, false)]
    [InlineData("double.IsNaN(double.NaN)", SpecialType.System_Double, true)]
    [InlineData("double.IsInfinity(double.NaN)", SpecialType.System_Double, true)]
    public void NativePrimitiveCalls_UseOnlyTheSelectedTypeContract(
        string expression, SpecialType expectedType, bool hasContract)
    {
        var source = $$"""
            module PrimitiveContract
              func Probe public () → object
                !
                return {{expression}}
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.Equal(expectedType, selected.ContainingType.SpecialType);
        Assert.Equal(expectedType, Assert.Single(selected.Parameters).Type.SpecialType);
        Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(selected.ReturnType,
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(call.Type).RoslynSymbol));
        var resolution = new EffectResolver().Resolve(EffectResolverKey.FromBoundReceiver(
            call.Receiver!.Type, selected.Name, call.ResolvedParameterTypes));
        Assert.Equal(hasContract, resolution.Status != EffectResolutionStatus.Unknown);
        Assert.Equal(hasContract ? "embedded:Calor.Compiler.Manifests.bcl-system.calor-effects.json" : "unknown",
            resolution.Source);
        var compiled = Compile(source);
        Assert.Equal(!hasContract, compiled.HasErrors);
        if (!hasContract)
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("System.EventArgs.Empty", "System.EventArgs")]
    [InlineData("string.Empty", "string")]
    public void NativeReadonlyMetadataFields_DoNotBorrowPropertyGetterContracts(string expression, string type)
    {
        var source = $$"""
            module ReadonlyField
              func Probe public () → {{type}}
                !
                return {{expression}}
            """;
        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var read = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        var field = Assert.IsAssignableFrom<IFieldSymbol>(read.ResolvedMetadataMember);
        Assert.True(field.IsStatic);
        Assert.True(field.IsReadOnly);
        Assert.False(field.IsConst);
        var effects = new DiagnosticBag();
        new EffectEnforcementPass(effects).Enforce(module);
        Assert.Contains(effects, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
            && diagnostic.Message.Contains(field.ToDisplayString(), StringComparison.Ordinal));
        Assert.DoesNotContain(effects, diagnostic => diagnostic.Message.Contains("get_Empty", StringComparison.Ordinal));
        Assert.True(Compile(source).HasErrors);
    }

    [Fact]
    public void NativeCharacterOverloads_PreserveSourceIdentityBoxingAndExceptions()
    {
        const string original = """
            public static class CharacterOverloads
            {
                private static string Pick(char value) => "char";
                private static string Pick(string value) => "string";
                private static string Pick(object value) => "object";
                public static object Probe() => Pick('\uD800');
                public static object Box() => '\uD800';
                public static object Throw() => double.Parse("not-a-number");
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(original);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Empty(conversion.Losses);
        var compiled = Compile(conversion.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var oracle = CompileAssembly(original).GetType("CharacterOverloads")!;
        var actual = CompileAssembly(compiled.GeneratedCode).GetType("CharacterOverloads")!;
        Assert.Equal(oracle.GetMethod("Probe")!.Invoke(null, null), actual.GetMethod("Probe")!.Invoke(null, null));
        Assert.Equal('\uD800', Assert.IsType<char>(actual.GetMethod("Box")!.Invoke(null, null)));
        Assert.IsType<FormatException>(Assert.Throws<TargetInvocationException>(
            () => oracle.GetMethod("Throw")!.Invoke(null, null)).InnerException);
        Assert.IsType<FormatException>(Assert.Throws<TargetInvocationException>(
            () => actual.GetMethod("Throw")!.Invoke(null, null)).InnerException);
    }

    [Fact]
    public void NativeCharacterOverloads_DoNotReinterpretANominalCharDeclaration()
    {
        const string source = """
            module CharacterIdentity
              class Char
                field int:Value public
              func Pick (@Char:value) → string
                !
                return "nominal"
              func Pick (char:value) → string
                !
                return "char"
              func Probe public () → string
                !
                return Pick('x')
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal("CHAR", TypeIdentity.Canonicalize(Assert.Single(call.ResolvedSymbol!.Parameters).TypeName));
        var compiled = Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal("char", CompileAssembly(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Probe").Invoke(null, null));
    }

    private static string ConvertPrimitiveReceiver(string source)
    {
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Equal(0, result.InteropPreservationCount);
        Assert.Equal(0, result.LossySubstitutionCount);
        Assert.Equal(0, result.DropCount);
        return Assert.IsType<string>(result.CalorSource);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("i64")]
    [InlineData("u32")]
    [InlineData("f32")]
    [InlineData("str")]
    public void NativeOrdinaryParameterNamedLikeARetiredTypeAlias_IsNotReinterpretedAsAType(string name)
    {
        var source = $$"""
            module OrdinaryTypeLikeReceiver
              func Probe public (string:{{name}}) → int
                !
                return {{name}}.Length
            """;
        var reemitted = new CalorEmitter().Emit(Parse(source));
        foreach (var candidate in new[] { source, reemitted })
        {
            var assembly = CompileAssembly(new CSharpEmitter().Emit(Parse(candidate)));
            var method = assembly.GetTypes().SelectMany(type => type.GetMethods()).Single(method => method.Name == "Probe");
            Assert.Equal(3, method.Invoke(null, ["abc"]));
        }
    }

    [Fact]
    public void NativeDelegateInvoke_PreservesCapturedMutationAndReturnValuesAtRuntime()
    {
        const string authored = """
            module CapturedDelegateInvoke
              func Probe public () → int
                ! mut
                var int:count = 0
                let Func<int>:callback = () =>
                  set count count + 1
                  return count
                let int:first = callback.Invoke()
                return first * 10 + callback.Invoke()
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Program.Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var assembly = CompileAssembly(result.GeneratedCode);
            var method = assembly.GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "Probe");
            Assert.Equal(12, method.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("", "", "StringComparison.Ordinal")]
    [InlineData(" Demo", "", "StringComparison.Ordinal")]
    [InlineData("", "  using System\n", "StringComparison.Ordinal")]
    [InlineData("", "  using Comparison = System.StringComparison\n", "Comparison.Ordinal")]
    [InlineData("", "", "System.StringComparison.Ordinal")]
    public void NativeImplicitSystemImport_RetainsTheActualStringComparisonFieldAndSelectedOverload(
        string namespaceSuffix, string imports, string comparison)
    {
        var source = $$"""
            module Strings{{namespaceSuffix}}
            {{imports}}  func FirstField (string:line) → string
                !
                let int:i = line.IndexOf(",", {{comparison}})
                if i < 0
                  return line
                return line.Substring(0, i)
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        var field = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>()
            .Single(member => member.FieldName == "Ordinal");
        var metadataField = Assert.IsAssignableFrom<IFieldSymbol>(field.ResolvedMetadataMember);
        Assert.True(metadataField.IsConst);
        Assert.True(metadataField.IsStatic);
        Assert.Equal("System.StringComparison", metadataField.ContainingType.ToDisplayString());
        Assert.Equal(TypeKind.Enum, metadataField.Type.TypeKind);
        Assert.False(field.HasUnknownInferredType);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions).Body[0]).Initializer);
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.Equal(SpecialType.System_String, selected.ContainingType.SpecialType);
        Assert.Equal(2, selected.Parameters.Length);
        Assert.Equal(SpecialType.System_String, selected.Parameters[0].Type.SpecialType);
        Assert.True(SymbolEqualityComparer.Default.Equals(metadataField.Type, selected.Parameters[1].Type));
        var result = Program.Compile(source);
        Assert.Empty(result.Diagnostics);
        var assembly = CompileAssembly(result.GeneratedCode);
        var method = assembly.GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "FirstField");
        foreach (var (input, expected) in new[]
        {
            ("a,b", "a"), ("abc", "abc"), ("", ""), (",b", ""), ("a,b,c", "a")
        })
            Assert.Equal(expected, method.Invoke(null, [input]));
    }

    [Theory]
    [InlineData("  enum StringComparison\n    Ordinal\n", "", "", "StringComparison.Ordinal")]
    [InlineData("", "", ", int:StringComparison", "StringComparison.Ordinal")]
    [InlineData("", "<StringComparison>", "", "StringComparison.Ordinal")]
    [InlineData("  using StringComparison = System.ConsoleColor\n", "", "", "StringComparison.Red")]
    [InlineData("  using StringComparison = System.StringComparison\n  using StringComparison = System.ConsoleColor\n",
        "", "", "StringComparison.Ordinal")]
    public void NativeImplicitSystemImport_DoesNotBorrowTheEnumThroughShadowedOrConflictingOwners(
        string declarations, string typeParameters, string parameters, string comparison)
    {
        var source = $$"""
            module ShadowedComparison
            {{declarations}}  func FirstField{{typeParameters}} (string:line{{parameters}}) → string
                !
                let int:i = line.IndexOf(",", {{comparison}})
                if i < 0
                  return line
                return line.Substring(0, i)
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions).Body[0]).Initializer);
        Assert.Null(call.ResolvedMetadataMethod);
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
    }

    [Fact]
    public void NativeImplicitSystemImport_DoesNotManufactureObjectDelegateResultNullability()
    {
        const string source = """
            module DelegateResult
              func Pick (object:value) → int
                !
                return 7
              func Probe () → int
                !
                return Pick(((Func<object>)(() => null))())
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var cast = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundTypeOperationExpression>().Single();
        var signature = Assert.IsType<Calor.Compiler.Binding.BoundTypes.FunctionBoundType>(cast.Type);
        Assert.Equal(Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Oblivious,
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(signature.ReturnType).NullableAnnotation);
        Assert.Contains(Compile(source).Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Theory]
    [InlineData("(Func<int>)(() => 9)", true)]
    [InlineData("(f)(1)", false)]
    [InlineData("(T)-n", false)]
    [InlineData("(T)!flag", false)]
    [InlineData("(T)~n", false)]
    [InlineData("(T)+n", false)]
    [InlineData("(Func<int>)-n", false)]
    [InlineData("(N.T)(value)", false)]
    [InlineData("(N.T)!flag", false)]
    [InlineData("(N.F<int>)(() => 9)", true)]
    [InlineData("(f)(1, 2)", false)]
    public void NativeCastDisambiguation_RetainsStagedGroupedCallBoundaries(string expression, bool isCast)
    {
        var expected = SyntaxFactory.ParseExpression(expression);
        Assert.False(expected.ContainsDiagnostics, expected.ToFullString());
        if (expression.Contains('!') || expression.Contains('~'))
        {
            var diagnostics = new DiagnosticBag();
            var source = $"module CastSyntax\n  func Probe () → object\n    !\n    return {expression}";
            new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.True(diagnostics.HasErrors);
            return;
        }
        var module = Parse($"module CastSyntax\n  func Probe () → object\n    !\n    return {expression}");
        var actual = Assert.IsType<ReturnStatementNode>(
            Assert.Single(Assert.Single(module.Functions).Body)).Expression;
        Assert.True(isCast == (actual is TypeOperationNode { Operation: TypeOp.Cast }),
            $"{expression}: C# {expected.Kind()}, native {actual!.GetType().Name}");
    }

    [Theory]
    [InlineData("(f) (x)", false)]
    [InlineData("(obj.Child.Pick)(value)", true)]
    [InlineData("(f)((x) => x)", false)]
    public void NativeGroupedCallees_RetainTheirOriginalCallNodesAndSpans(string expression, bool computed)
    {
        var source = $"module GroupedCalls\n  func Probe () → object\n    !\n    return {expression}";
        var value = Assert.IsType<ReturnStatementNode>(Assert.Single(Parse(source).Functions).Body[0]).Expression;
        if (computed)
        {
            var call = Assert.IsType<ExpressionCallNode>(value);
            var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
            Assert.Equal("Pick", member.FieldName);
            Assert.Equal("Pick", source.Substring(member.FieldNameSpan.Start, member.FieldNameSpan.Length));
        }
        else
            Assert.IsType<CallExpressionNode>(value);
    }

    [Fact]
    public void NativeGroupedNamedCall_RetainsItsRuntimeAndConsoleEffect()
    {
        const string authored = """
            module GroupedRuntime
              func Echo (int:value) → int
                ! cw
                System.Console.WriteLine(value)
                return value
              func Probe () → int
                ! cw
                return (Echo)(3)
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(3, CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods()).Single(method => method.Name == "Probe").Invoke(null, null));
            var forbidden = Compile(source.Replace("func Probe () → int\n    ! cw", "func Probe () → int\n    !",
                StringComparison.Ordinal));
            Assert.Contains(forbidden.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'"));
        }
    }

    [Fact]
    public void NativeGroupedDelegateCall_RetainsItsRuntimeAndUnknownEffect()
    {
        const string source = """
            module GroupedDelegate
              func Probe (Func<int,int>:f ! unknown) → int
                !
                return (f)(3)
            """;
        Assert.IsType<CallExpressionNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(Parse(source).Functions).Body[0]).Expression);
        Assert.Contains(Compile(source).Diagnostics, diagnostic => diagnostic.Code == "Calor0425");
        var method = CompileAssembly(new CSharpEmitter().Emit(Parse(source))).GetTypes()
            .SelectMany(type => type.GetMethods()).Single(method => method.Name == "Probe");
        Assert.Equal(4, method.Invoke(null, [new Func<int, int>(value => value + 1)]));
    }

    [Fact]
    public void NativeCastLambda_SourceFuncShadowDoesNotAcquireABclDelegateSignature()
    {
        const string source = """
            module ShadowedDelegate
              class Func<T>
              func Probe () → object
                !
                return ((Func<int>)(() => 9))()
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.IsType<BoundExpressionCall>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.IsNotType<Calor.Compiler.Binding.BoundTypes.FunctionBoundType>(call.Target.Type);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("nameof(n).Length", "1")]
    [InlineData("(nameof(n)).Length", "1")]
    [InlineData("\"abc\".Length", "3")]
    public void NativeNameofAndLiteralLength_RetainStringMetadataAndPureInterpolation(string expression, string expected)
    {
        var authored = "module Native Native\n  func Probe (int:n) → string\n    !\n    return \"${" + expression + "}\"";
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            var interpolation = Assert.IsType<BoundInterpolatedStringExpression>(
                Assert.IsType<BoundReturnStatement>(Assert.Single(Assert.Single(bound.Functions).Body)).Expression);
            var member = Assert.IsType<BoundFieldAccessExpression>(Assert.Single(interpolation.Children));
            Assert.False(member.HasUnknownInferredType);
            Assert.Equal("INT", member.Type.DisplayString);
            var property = Assert.IsAssignableFrom<IPropertySymbol>(member.ResolvedMetadataMember);
            Assert.Equal(SpecialType.System_String, property.ContainingType.SpecialType);
            Assert.Equal(SpecialType.System_Int32, property.Type.SpecialType);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(expected, CompileAssembly(result.GeneratedCode).GetType("Native.NativeModule")!
                .GetMethod("Probe")!.Invoke(null, [12]));
        }
    }

    [Fact]
    public void NativeNameofLength_DoesNotMakeUnknownStringMembersPure()
    {
        var result = Compile("""
            module UnknownNameofMember
              func Probe (int:n) → int
                !
                return nameof(n).Missing
            """);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void NativeCastLambdaInvocation_RetainsTheOriginalNineWithoutAnOpaqueAssumption()
    {
        const string authored = """
            module Native Native
              func Probe () → string
                !
                return "${((Func<int>)(() => 9))()}"
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            var interpolation = Assert.IsType<BoundInterpolatedStringExpression>(
                Assert.IsType<BoundReturnStatement>(Assert.Single(Assert.Single(bound.Functions).Body)).Expression);
            var call = Assert.IsType<BoundExpressionCall>(Assert.Single(interpolation.Children));
            var cast = Assert.IsType<BoundTypeOperationExpression>(call.Target);
            var lambda = Assert.IsType<BoundLambdaExpression>(cast.Operand);
            Assert.Equal(9, Assert.IsType<BoundIntLiteral>(lambda.ExpressionBody).Value);
            var signature = Assert.IsType<Calor.Compiler.Binding.BoundTypes.FunctionBoundType>(cast.Type);
            Assert.Empty(signature.ParameterTypes);
            Assert.Equal(signature.ReturnType, call.Type);
            Assert.False(call.HasUnknownInferredType);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Calor04", StringComparison.Ordinal));
            Assert.Equal("9", CompileAssembly(result.GeneratedCode).GetType("Native.NativeModule")!
                .GetMethod("Probe")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("Func<int>", "int")]
    [InlineData("System.Func<int>", "int")]
    [InlineData("Func<object>", "object")]
    [InlineData("Func<System.Object>", "object")]
    public void NativeCastLambdaInvocation_RetainsTheAuthoredDelegateReturnOverload(string type, string expected)
    {
        var authored = $$"""
            module DelegateReturnIdentity
              func Pick (int:value) → string
                !
                return "int"
              func Pick (object:value) → string
                !
                return "object"
              func Probe () → string
                !
                return Pick((({{type}})(() => 9))())
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Probe");
            Assert.Equal(expected, method.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("Func<object?>", "9")]
    [InlineData("Func<object?>", "null")]
    [InlineData("System.Func<object?>", "9")]
    [InlineData("System.Func<object?>", "null")]
    [InlineData("Func<System.Object?>", "null")]
    public void NativeCastLambdaInvocation_RetainsAuthoredNullableObjectReturn(string type, string value)
    {
        var authored = $$"""
            module NullableDelegateReturn
              func Pick (object:value) → int
                !
                return 1
              func Probe () → int
                !
                return Pick((({{type}})(() => {{value}}))())
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
        }
    }

    [Theory]
    [InlineData("9")]
    [InlineData("\"value\"")]
    [InlineData("new System.Collections.Generic.List<int>()")]
    [InlineData("new int[0]")]
    public void NativeCastLambdaInvocation_ObjectResultUsesTheBoundNonNullBody(string value)
    {
        var authored = $$"""
            module NonNullDelegateResult
              func Pick (object:value) → int
                !
                return 7
              func Probe () → int
                ! alloc
                return Pick(((Func<object>)(() => {{value}}))())
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Probe");
            Assert.Equal(7, method.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("object", false)]
    [InlineData("object?", true)]
    [InlineData("string?", true)]
    [InlineData("int?", true)]
    public void NativeCastLambdaInvocation_ObjectResultPreservesBoundBodyNullability(string type, bool nullable)
    {
        var authored = $$"""
            module PossiblyNullDelegateResult
              func Pick (object:value) → int
                !
                return 7
              func Probe ({{type}}:input) → int
                !
                return Pick(((Func<object>)(() => input))())
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            if (nullable)
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
            else
                Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        }
    }

    [Fact]
    public void NativeCastLambdaInvocation_DoesNotSuppressUnknownObjectConstructor()
    {
        var result = Compile("""
            module UnknownObjectConstructor
              func Pick (object:value) → int
                !
                return 7
              func Probe () → int
                !
                return Pick(((Func<object>)(() => new object()))())
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0411");
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Theory]
    [InlineData("((Func<int>)(() => Next()))()")]
    [InlineData("((Func<int,int>)((x) => x))(Next())")]
    public void NativeCastLambdaInvocation_ChargesBodyAndArgumentEffects(string expression)
    {
        var authored = $$"""
            module DelegateEffects
              func Next () → int
                ! cw
                System.Console.WriteLine("next")
                return 9
              func Probe () → int
                !
                return {{expression}}
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'"));
        }
    }

    [Fact]
    public void NativeCastUnknownDelegate_DoesNotAcquireALiteralBodyProof()
    {
        var result = Compile("""
            module UnknownDelegate
              func Probe (Func<int>:value ! unknown) → int
                !
                return ((Func<int>)value)()
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0419");
    }

    [Theory]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let items = new System.Collections.Generic.List<int> {1, 2}")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let items = new System.Collections.Generic.Dictionary<string,int> {{\"a\", 1}}")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let items = new System.Collections.Generic.HashSet<string> {\"a\"}")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let System.Collections.Generic.List<int>:items = new System.Collections.Generic.List<int> {1, 2}")]
    public void OriginalIdentityFreeCollectionInitializers_RetainTheirNativeTypes(string source)
    {
        var module = Parse(source);
        var creation = Assert.IsType<NewExpressionNode>(
            Assert.IsType<BindStatementNode>(Assert.Single(Assert.Single(module.Functions).Body)).Initializer);
        Assert.NotEmpty(creation.CollectionInitializers);
        Assert.Empty(creation.Initializers);
        var generated = new CSharpEmitter().Emit(module);
        Assert.Contains("new System.Collections.Generic.", generated);
        var method = CompileAssembly(generated).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Run");
        method.Invoke(null, null);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<int>", "1, 2")]
    [InlineData("System.Collections.Generic.HashSet<string>", "\"a\", \"a\", \"b\"")]
    [InlineData("System.Collections.Generic.Dictionary<string,int>", "{\"a\", 1}, {\"b\", 2}")]
    public void NativeCollectionInitializers_RetainClrAddSemanticsAndReemit(string typeName, string entries)
    {
        var authored = $$"""
            module Collections
              func Make () → {{typeName}}
                ! alloc, mut
                return new {{typeName}} { {{entries}} }
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Make");
            var value = method.Invoke(null, null);
            switch (value)
            {
                case List<int> list:
                    Assert.Equal(new[] { 1, 2 }, list);
                    break;
                case HashSet<string> set:
                    Assert.Equal(new[] { "a", "b" }, set.Order().ToArray());
                    break;
                case Dictionary<string, int> dictionary:
                    Assert.Equal(2, dictionary.Count);
                    Assert.Equal(1, dictionary["a"]);
                    Assert.Equal(2, dictionary["b"]);
                    break;
                case System.Collections.ArrayList arrayList:
                    Assert.Equal(new object[] { 1, "two" }, arrayList.ToArray());
                    break;
                default:
                    Assert.Fail($"Unexpected collection runtime type: {value?.GetType()}");
                    break;
            }
        }
    }

    [Fact]
    public void NativeArrayListInitializer_DoesNotInventAnEffectContract()
    {
        const string source = """
            module UnknownCollectionEffects
              func Make () → System.Collections.ArrayList
                ! alloc, mut
                return new System.Collections.ArrayList {1, "two"}
            """;
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        var generated = new CSharpEmitter().Emit(Parse(source));
        var method = CompileAssembly(generated).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Make");
        var list = Assert.IsType<System.Collections.ArrayList>(method.Invoke(null, null));
        Assert.Equal(new object[] { 1, "two" }, list.ToArray());
    }

    [Fact]
    public void NativeCollectionInitializers_PreserveConstructorAndElementOrderAndConditionalLaziness()
    {
        const string authored = """
            module CollectionOrder
              using System.Collections.Generic
              func Next (ref int:counter) → int
                ! mut
                set counter (counter + 1)
                return counter
              func Make () → List<int>
                ! alloc, mut
                var int:counter = 0
                return new List<int>(Next(ref counter)) {Next(ref counter), Next(ref counter)}
              func Conditional (bool:take) → int
                ! alloc, mut
                var int:counter = 0
                let List<int>:values = take ? new List<int> {Next(ref counter)} : new List<int>()
                return counter
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var methods = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .ToArray();
            var list = Assert.IsType<List<int>>(methods.Single(method => method.Name == "Make").Invoke(null, null));
            Assert.Equal(new[] { 2, 3 }, list);
            Assert.Equal(2, list.Capacity);
            var conditional = methods.Single(method => method.Name == "Conditional");
            Assert.Equal(0, conditional.Invoke(null, [false]));
            Assert.Equal(1, conditional.Invoke(null, [true]));
        }
    }

    [Fact]
    public void NativeDictionaryInitializer_DuplicateKeysThrowRatherThanUpsert()
    {
        const string source = """
            module DuplicateKeys
              func Make () → System.Collections.Generic.Dictionary<string,int>
                ! alloc, mut
                return new System.Collections.Generic.Dictionary<string,int> {{"a", 1}, {"a", 2}}
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = CompileAssembly(result.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Make");
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, null));
        Assert.IsType<ArgumentException>(error.InnerException);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<int>", "\"wrong\"")]
    [InlineData("System.Collections.Generic.Dictionary<string,int>", "{\"a\"}")]
    [InlineData("System.Text.StringBuilder", "\"not an Add\"")]
    public void NativeCollectionInitializers_RejectMissingOrIncompatibleAddOverloads(string typeName, string entries)
    {
        var result = Compile($$"""
            module InvalidCollection
              func Make () → {{typeName}}
                ! alloc, mut
                return new {{typeName}} { {{entries}} }
            """);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void NativeSourceTypedList_RetainsTheOriginalNestedConstructorAndSourceIdentity()
    {
        const string authored = """
            module SourceList
              using System.Collections.Generic
              class WidgetException
                field int:Value public = 23
              func Make () → List<WidgetException>
                ! alloc, mut
                let List<WidgetException>:values = new List<WidgetException> {new WidgetException()}
                return values
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
            var creation = Assert.IsType<BoundNewExpression>(
                Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions).Body[0]).Initializer);
            var entry = Assert.Single(creation.CollectionInitializers);
            var element = Assert.IsType<BoundNewExpression>(Assert.Single(entry.Arguments));
            Assert.Equal("WidgetException", element.ResolvedType!.Name);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Make");
            var list = Assert.IsAssignableFrom<System.Collections.IList>(method.Invoke(null, null));
            Assert.Equal(typeof(List<>), list.GetType().GetGenericTypeDefinition());
            var value = Assert.Single(list.Cast<object>());
            Assert.Equal("WidgetException", value.GetType().Name);
            Assert.Equal(23, value.GetType().GetField("Value")!.GetValue(value));
        }
    }

    [Fact]
    public void OriginalTypeRenameProgram_RetainsItsListAndNestedConstructor()
    {
        const string source = """
            module TypeRefactor
              using System.Collections.Generic
              interface IWidget
                func Marker () → void
                  !
              class WidgetException
                extends System.Exception
                implements IWidget
                func Create static () → WidgetException
                  !
                  return new WidgetException()
                func Marker () → void
                  !
                  System.Console.WriteLine("marker")
              class DerivedException
                extends WidgetException
                field int:Code
              func Use (object:value) → WidgetException
                !
                let WidgetException[]:items = new WidgetException[1]
                let List<WidgetException>:values = new List<WidgetException> {new WidgetException()}
                try
                  return (WidgetException)value
                catch WidgetException:ex
                  return WidgetException.Create()
            """;
        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var collection = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundBindStatement>(use.Body[1]).Initializer);
        var nested = Assert.IsType<BoundNewExpression>(
            Assert.Single(Assert.Single(collection.CollectionInitializers).Arguments));
        Assert.Equal("WidgetException", nested.ResolvedType!.Name);
        Assert.Equal("WidgetException", source.Substring(nested.TypeNameSpan.Start, nested.TypeNameSpan.Length));
        var widget = bound.SymbolsById.Values.OfType<TypeSymbol>().Single(type => type.Name == "WidgetException");
        var elementType = Assert.Single(collection.TypeReference.TypeArguments);
        Assert.Equal(widget.Id, elementType.ResolvedTypeSymbolId);
        Assert.Equal("WidgetException", source.Substring(elementType.Span.Start, elementType.Span.Length));
        var factory = Calor.Compiler.Analysis.Dataflow.BoundNodeHelpers.DescendantsAndSelf(bound)
            .OfType<BoundCallExpression>().Single(call => call.ResolvedSymbol?.Name == "WidgetException.Create");
        Assert.Equal(widget.Id, factory.ReceiverTypeSymbolId);
        Assert.Equal("WidgetException",
            source.Substring(factory.ReceiverSpan!.Value.Start, factory.ReceiverSpan.Value.Length));
        Assert.Contains("new List<WidgetException>", new CSharpEmitter().Emit(module));
        CompileAssembly(new CSharpEmitter().Emit(module));
    }

    [Fact]
    public void NativeCollectionAlias_RetainsActualAddIdentity()
    {
        const string source = """
            module CollectionAlias
              using Items = System.Collections.Generic.List<int>
              func Make () → Items
                ! alloc, mut
                return new Items {1, 2}
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = CompileAssembly(result.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Make");
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<List<int>>(method.Invoke(null, null)));
    }

    [Theory]
    [InlineData("Bag", "Bag")]
    [InlineData("Bag<T>", "Bag<int>")]
    public void NativeCollectionInitializer_DoesNotInferEnumerableFromAnAddName(string declaration, string constructed)
    {
        var source = $$"""
            module NonEnumerable
              class {{declaration}}
                func Add (int:value) → void
                  !
                  return
              func Make () → {{constructed}}
                ! alloc, mut
                return new {{constructed}} {1}
            """;
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(Parse(source));
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.TypeMismatch
            && diagnostic.Message.Contains("System.Collections.IEnumerable"));
        Assert.True(Compile(source).HasErrors);
    }

    [Fact]
    public void NativeCollectionInitializer_SimplificationRetainsOrderedAddArguments()
    {
        var module = Parse("""
            module SimplifiedCollection
              func Make () → System.Collections.Generic.List<int>
                ! alloc, mut
                return new System.Collections.Generic.List<int>(1 + 1) {1 + 2, 2 + 3}
            """);
        var statement = Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
        var simplifier = new Calor.Compiler.Verification.ExpressionSimplifier();
        var creation = Assert.IsType<NewExpressionNode>(simplifier.Simplify(statement.Expression!));
        Assert.True(simplifier.Changed);
        Assert.Equal(2, Assert.IsType<IntLiteralNode>(Assert.Single(creation.Arguments)).Value);
        Assert.Equal(new long[] { 3, 5 }, creation.CollectionInitializers.Select(entry =>
            Assert.IsType<IntLiteralNode>(Assert.Single(entry.Arguments)).Value));
        Assert.All(creation.CollectionInitializers, entry => Assert.Equal(TextSpan.Empty, entry.CalleeSpan));
    }

    [Fact]
    public void NativeCollectionInitializer_InheritedContractRewritingRetainsElements()
    {
        var module = Parse("""
            module RewrittenCollection
              func Make (int:value) → System.Collections.Generic.List<int>
                ! alloc, mut
                return new System.Collections.Generic.List<int>(value) {value, value + 1}
            """);
        var original = Assert.IsType<NewExpressionNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body)).Expression);
        var rewrite = typeof(Calor.Compiler.Verification.ContractInheritanceChecker)
            .GetMethod("RewriteReferences", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(rewrite);
        var creation = Assert.IsType<NewExpressionNode>(rewrite.Invoke(null,
            [original, new Dictionary<string, string> { ["value"] = "renamed" }, null]));
        Assert.Equal("renamed", Assert.IsType<ReferenceNode>(Assert.Single(creation.Arguments)).Name);
        Assert.Equal(2, creation.CollectionInitializers.Count);
        Assert.Equal("renamed", Assert.IsType<ReferenceNode>(
            Assert.Single(creation.CollectionInitializers[0].Arguments)).Name);
        Assert.Equal("renamed", Assert.IsType<ReferenceNode>(Assert.IsType<BinaryOperationNode>(
            Assert.Single(creation.CollectionInitializers[1].Arguments)).Left).Name);
        Assert.Equal(original.CollectionInitializers.Select(entry => entry.Span),
            creation.CollectionInitializers.Select(entry => entry.Span));
        Assert.All(creation.CollectionInitializers, entry => Assert.Equal(TextSpan.Empty, entry.CalleeSpan));
    }

    [Fact]
    public void NativeCollectionInitializer_RetainsMultilineAndInterpolationBoundaries()
    {
        const string authored = """
            module CollectionBoundaries
              using System.Collections.Generic
              func Make () → List<int>
                ! alloc, mut
                return new List<int> {
                  1,
                  2,
                }
              func Format () → string
                ! alloc, mut
                return "${new List<int> {1, 2}.Count}"
              func After () → int
                !
                return 9
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var methods = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .ToArray();
            Assert.Equal(new[] { 1, 2 },
                Assert.IsType<List<int>>(methods.Single(method => method.Name == "Make").Invoke(null, null)));
            Assert.Equal("2", methods.Single(method => method.Name == "Format").Invoke(null, null));
            Assert.Equal(9, methods.Single(method => method.Name == "After").Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("mut")]
    [InlineData("")]
    public void NativeCollectionInitializer_StillChargesAllocation(string declared)
    {
        var result = Compile($$"""
            module CollectionEffects
              func Make () → System.Collections.Generic.List<int>
                ! {{declared}}
                return new System.Collections.Generic.List<int> {1}
            """);
        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'alloc'"));
    }

    [Theory]
    [InlineData("List<int>", "(int)1, (int)2")]
    [InlineData("HashSet<int>", "(int)1, (int)2")]
    [InlineData("Dictionary<int,int>", "{(int)1, (int)10}, {(int)2, (int)20}")]
    [InlineData("List<int>", "Echo(1), Echo(2)")]
    [InlineData("HashSet<int>", "Echo(1), Echo(2)")]
    [InlineData("Dictionary<int,int>", "{Echo(1), Echo(10)}, {Echo(2), Echo(20)}")]
    public void OriginalSixNativeCollectionRows_RetainAllocOnlyAndClrOracles(string type, string entries)
    {
        var authored = $$"""
            module Native Native
              using System.Collections.Generic
              func Echo private (int:value) → int
                return value
              func Probe () → object
                ! alloc
                let values = new {{type}} { {{entries}} }
                return values
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            var creation = Assert.IsType<BoundNewExpression>(Assert.IsType<BoundBindStatement>(
                bound.Functions.Single(function => function.Symbol.Name == "Probe").Body[0]).Initializer);
            Assert.All(creation.CollectionInitializers, call =>
            {
                Assert.True(call.MutatesOnlyFreshCollectionReceiver);
                Assert.NotNull(call.ResolvedMetadataMethod);
            });
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var value = CompileAssembly(result.GeneratedCode).GetType("Native.NativeModule")!
                .GetMethod("Probe")!.Invoke(null, null);
            if (value is IDictionary<int, int> dictionary)
            {
                Assert.Equal(2, dictionary.Count);
                Assert.Equal(10, dictionary[1]);
                Assert.Equal(20, dictionary[2]);
            }
            else if (value is HashSet<int> set)
                Assert.True(set.SetEquals([1, 2]));
            else
                Assert.Equal([1, 2], Assert.IsAssignableFrom<IEnumerable<int>>(value));
        }
    }

    [Theory]
    [InlineData("List<int>", "1", "")]
    [InlineData("HashSet<string>", "\"a\"", "")]
    [InlineData("Dictionary<string,int>", "{\"a\", 1}", "")]
    [InlineData("Items", "1", "using Items = System.Collections.Generic.List<int>")]
    public void FreshBclCollectionIdentity_RetainsAliasesAndSafeStringKeys(string type, string entries, string import)
    {
        var authored = $$"""
            module FreshIdentity
              using System.Collections.Generic
              {{import}}
              func Make () → {{type}}
                ! alloc
                return new {{type}} { {{entries}} }
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            CompileAssembly(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("new System.Collections.Generic.HashSet<object> {key}")]
    [InlineData("new System.Collections.Generic.Dictionary<object,int> {{key, 1}}")]
    [InlineData("new System.Collections.Generic.List<int>(2) {1}")]
    public void CollectionInitializers_WithoutTheFreshCallbackFreeProofRetainMutation(string expression)
    {
        var authored = $$"""
            module UnprovedFreshCollection
              func Probe (object:key) → object
                ! alloc
                return {{expression}}
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
            var creation = Assert.IsType<BoundNewExpression>(
                Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
            Assert.All(creation.CollectionInitializers, call => Assert.False(call.MutatesOnlyFreshCollectionReceiver));
            var result = Compile(source);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'mut'"));
        }
    }

    [Theory]
    [InlineData("Next(external)", "")]
    [InlineData("1", "values.Add(2)")]
    public void FreshCollectionDischarge_DoesNotRemoveExternalOrPostConstructionMutation(string entry, string after)
    {
        var authored = $$"""
            module ExternalMutation
              using System.Collections.Generic
              func Next (List<int>:external) → int
                ! mut
                external.Add(1)
                return external.Count
              func Probe (List<int>:external) → List<int>
                ! alloc
                let values = new List<int> { {{entry}} }
                {{after}}
                return values
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'mut'"));
        }
    }

    [Theory]
    [InlineData("Tracked")]
    [InlineData("List")]
    [InlineData("HashSet")]
    public void FreshCollectionDischarge_ExcludesSourceNamesAndConstructors(string name)
    {
        var authored = $$"""
            module CustomCollection
              class {{name}} extends System.Collections.Generic.List<int>
                field int:Total public = 0
                ctor ()
                  System.Console.WriteLine("construction")
                func Add (int:value) → void
                  ! mut
                  set this.Total (this.Total + value)
              func Probe () → {{name}}
                ! alloc
                return new {{name}} {1}
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
            var creation = Assert.IsType<BoundNewExpression>(
                Assert.IsType<BoundReturnStatement>(
                    bound.Functions.Single(function => function.Symbol.Name == "Probe").Body[0]).Expression);
            Assert.All(creation.CollectionInitializers, call => Assert.False(call.MutatesOnlyFreshCollectionReceiver));
            var result = Compile(source);
            foreach (var effect in new[] { "mut", "cw" })
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains($"'{effect}'"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshSourceElementList_OnlyDischargesTheGenuineBclAdd(bool constructorWrites)
    {
        var authored = $$"""
            module SourceElementEffects
              using System.Collections.Generic
              class Item
                ctor ()
                  {{(constructorWrites ? "System.Console.WriteLine(\"construction\")" : "")}}
              func Probe () → List<Item>
                ! alloc
                return new List<Item> {new Item()}
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            var creation = Assert.IsType<BoundNewExpression>(Assert.IsType<BoundReturnStatement>(
                bound.Functions.Single(function => function.Symbol.Name == "Probe").Body[0]).Expression);
            var call = Assert.Single(creation.CollectionInitializers);
            Assert.True(call.MutatesOnlyFreshCollectionReceiver);
            Assert.Null(call.ResolvedMetadataMethod);
            Assert.Equal("System.Collections.Generic.List<T>",
                call.CollectionMethodDefinition!.ContainingType.ToDisplayString());
            var result = Compile(source);
            if (constructorWrites)
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'"));
            else
            {
                Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
                var method = CompileAssembly(result.GeneratedCode).GetTypes()
                    .SelectMany(type => type.GetMethods()).Single(method => method.Name == "Probe");
                Assert.Single(Assert.IsAssignableFrom<System.Collections.IList>(method.Invoke(null, null)).Cast<object>());
            }
        }
    }

    [Fact]
    public void FreshCollectionDischarge_ExcludesExplicitComparerConstructors()
    {
        const string authored = """
            module ComparerEffects
              using System.Collections.Generic
              func Probe (IEqualityComparer<int>:comparer) → HashSet<int>
                ! alloc
                return new HashSet<int>(comparer) {1}
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
            var creation = Assert.IsType<BoundNewExpression>(
                Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
            Assert.False(Assert.Single(creation.CollectionInitializers).MutatesOnlyFreshCollectionReceiver);
            Assert.Contains(Compile(source).Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'mut'"));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeConstruction_SelectedBaseInitializationCannotLoseCalls(
        bool declaredConstructor, bool staticInitializer)
    {
        var authored = $$"""
            module BaseInitialization
              class Parent
                field int:Value public {{(staticInitializer ? "static" : "")}} = Initialize()
                func Initialize private static () → int
                  System.Console.WriteLine("base initialization")
                  return 7
                {{(declaredConstructor ? "ctor ()" : "")}}
              class Child
                extends Parent
              func Make () → Child
                ! alloc
                return new Child()
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions
            {
                EnableTypeChecking = typed,
                DeferGeneratedOutputValidation = true
            });
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'Make'", StringComparison.Ordinal)
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Parent")]
    public void NativeConstruction_UnavailableBaseInitializationStaysClosed(string baseType)
    {
        var authored = $$"""
            module UnavailableBase
              class Parent
                ctor private ()
              class Child
                extends {{baseType}}
              func Make () → Child
                ! alloc
                return new Child()
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions
            {
                EnableTypeChecking = typed,
                DeferGeneratedOutputValidation = true
            });
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NativeConstruction_FieldReceivingConversionIsCharged()
    {
        const string authored = """
            module InitializerConversion
              class Item
                op implicit (int:value) → Item
                  ! cw
                  System.Console.WriteLine(value)
                  return new Item()
              class Holder
                field Item:Value public = 1
                ctor ()
              func Make () → Holder
                ! alloc
                return new Holder()
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions
            {
                EnableTypeChecking = typed,
                DeferGeneratedOutputValidation = true
            });
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'Make'", StringComparison.Ordinal)
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NativeConstruction_SelectedInitializerMethodChargesOtherTypeInitialization()
    {
        // 0.30 (D1): a static initializer's effects beyond mut/alloc must be declared on the
        // type's static constructor row (otherwise Calor0422); `new Holder()` still charges them.
        // 0.30 (gap A): Read may run Seed's static constructor, so its own row declares cw.
        const string authored = """
            module StaticInitializationDependency
              class Seed
                field int:Value public static = Initialize()
                ctor stat ()
                  ! cw
                func Initialize private static () → int
                  System.Console.WriteLine("static initialization")
                  return 7
                func Read public static () → int
                  ! cw
                  return 42
              class Holder
                field int:Value public = Seed.Read()
              func Make () → Holder
                ! alloc
                return new Holder()
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions
            {
                EnableTypeChecking = typed,
                DeferGeneratedOutputValidation = true
            });
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'Make'", StringComparison.Ordinal)
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
            var allowed = Program.Compile(source.Replace("! alloc", "! alloc, cw", StringComparison.Ordinal),
                null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.False(allowed.HasErrors, string.Join("\n", allowed.Diagnostics));
            var assembly = CompileAssembly(allowed.GeneratedCode);
            var make = assembly.GetTypes().SelectMany(type => type.GetMethods()).Single(method => method.Name == "Make");
            var holder = make.Invoke(null, null)!;
            Assert.Equal(42, holder.GetType().GetField("Value")!.GetValue(holder));
        }
    }

    [Fact]
    public void NativeConstruction_StructZeroInitializationDoesNotInventABaseCall()
    {
        const string authored = """
            module ValueInitialization
              class Point struct
                field int:Value public
              func Make () → Point
                ! alloc
                return new Point()
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            var assembly = CompileAssembly(result.GeneratedCode);
            var make = assembly.GetTypes().SelectMany(type => type.GetMethods()).Single(method => method.Name == "Make");
            var point = make.Invoke(null, null)!;
            Assert.True(point.GetType().IsValueType);
            Assert.Equal(0, point.GetType().GetField("Value")!.GetValue(point));
        }
    }

    [Fact]
    public void NativeConstruction_ProjectContextDoesNotClosePartialDeclarations()
    {
        const string authored = """
            module PartialInitialization
              class Service partial
                ctor ()
                func Read () → int
                  !
                  return 42
              func Make () → Service
                ! alloc
                return new Service()
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        {
            var standalone = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.False(standalone.HasErrors, string.Join("\n", standalone.Diagnostics));
            var assembly = CompileAssembly(standalone.GeneratedCode);
            var make = assembly.GetTypes().SelectMany(type => type.GetMethods()).Single(method => method.Name == "Make");
            var service = make.Invoke(null, null)!;
            Assert.Equal(42, service.GetType().GetMethod("Read")!.Invoke(service, null));
            var project = Program.Compile(source, null, new CompilationOptions
            {
                EnableTypeChecking = typed,
                DeferGeneratedOutputValidation = true,
                CrossModuleFunctionModules = new Dictionary<string, CrossModuleFunctionTarget>()
            });
            Assert.Contains(project.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("", "Next()")]
    [InlineData("(Next())", "1")]
    public void NativeCollectionInitializer_DoesNotDropConstructorOrElementCallEffects(string arguments, string entry)
    {
        var source = $$"""
            module ElementEffects
              func Next () → int
                ! cw
                System.Console.WriteLine("element")
                return 1
              func Make () → System.Collections.Generic.List<int>
                ! alloc, mut
                return new System.Collections.Generic.List<int>{{arguments}} { {{entry}} }
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var result = Compile(candidate);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'"));
        }
    }

    [Fact]
    public void NativeCollectionInitializer_ChargesASourceAddRatherThanABclNameMatch()
    {
        const string source = """
            module SourceAdd
              class Tracked extends System.Collections.Generic.List<int>
                func Add (int:value) → void
                  ! cw
                  System.Console.WriteLine(value)
              func Make () → Tracked
                ! alloc, mut
                return new Tracked {1}
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var result = Compile(candidate);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'"));
            Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        }
    }

    [Theory]
    [InlineData("new System.Collections.Generic.List<int> {1, Name = 2}")]
    [InlineData("new System.Collections.Generic.List<int> {{}}")]
    [InlineData("new System.Collections.Generic.List<int> {{1,}}")]
    public void NativeCollectionInitializer_RejectsMalformedOrMixedEntries(string expression)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer($"module Invalid\n  func Make () → void\n    !\n    let value = {expression}",
            diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void IndexedSubtraction_RetainsCastAndUtf16ScalarArithmetic()
    {
        const string authored = """
            module CodecArithmetic
              func Scalar (string:text, int:i, int:scalar) → int
                !
                return ((65536 + ((scalar - 55296) * 1024)) + ((int)(text[i]) - 56320))
              func Previous (byte[]:values, int:i) → int
                !
                return (values[i]) - 1
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var methods = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods()).ToArray();
            Assert.Equal(0x1F600, methods.Single(method => method.Name == "Scalar")
                .Invoke(null, ["\uD83D\uDE00", 1, 0xD83D]));
            Assert.Equal(254, methods.Single(method => method.Name == "Previous")
                .Invoke(null, [new byte[] { 255 }, 0]));
        }
    }

    [Theory]
    [InlineData("int[]")]
    [InlineData("int[,]")]
    [InlineData("int[][]")]
    public void ArrayCasts_RetainActualClrArrayIdentity(string type)
    {
        var authored = $"module ArrayCasts\n  func Preserve (object:value) → {type}\n    !\n    return ({type})value\n";
        object input = type switch
        {
            "int[]" => new[] { 42 },
            "int[,]" => new int[,] { { 42 } },
            "int[][]" => new[] { new[] { 42 } },
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(owner => owner.GetMethods())
                .Single(method => method.Name == "Preserve");
            Assert.Equal(input.GetType(), method.ReturnType);
            Assert.Same(input, method.Invoke(null, [input]));
        }
    }

    [Theory]
    [InlineData("System.SByte", "int")]
    [InlineData("System.Byte", "int")]
    [InlineData("System.Int16", "int")]
    [InlineData("System.UInt16", "int")]
    [InlineData("System.Int32", "int")]
    [InlineData("System.UInt32", "uint")]
    [InlineData("System.Int64", "long")]
    [InlineData("System.UInt64", "ulong")]
    [InlineData("System.Single", "float")]
    [InlineData("System.Double", "double")]
    [InlineData("System.Decimal", "decimal")]
    public void ClrNumericAliases_RetainArithmeticTypingAndRealParameterIdentity(string alias, string output)
    {
        var authored = $$"""
            module Numbers
              func Divide ({{alias}}:left, {{alias}}:right) → {{output}}
                !
                let quotient = left / right
                return quotient
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(owner => owner.GetMethods())
                .Single(method => method.Name == "Divide");
            var parameterType = Type.GetType(alias, throwOnError: true)!;
            Assert.All(method.GetParameters(), parameter => Assert.Equal(parameterType, parameter.ParameterType));
            var arguments = new[] { Convert.ChangeType(6, parameterType), Convert.ChangeType(3, parameterType) };
            Assert.Equal(Convert.ChangeType(2, method.ReturnType), method.Invoke(null, arguments));
        }
    }

    [Theory]
    [InlineData("System.Boolean")]
    [InlineData("System.String")]
    public void ClrNonNumericAliases_DoNotAcquireArithmeticOperations(string alias)
    {
        var source = $"module Numbers\n  func Divide ({alias}:left, {alias}:right) → int\n    !\n    let quotient = left / right\n    return quotient";
        var diagnostics = new DiagnosticBag();
        new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("Example.Int32")]
    [InlineData("System.int32")]
    [InlineData("System.Int32Thing")]
    public void ClrNumericAliases_DoNotResolveLookalikeTypeNames(string name)
        => Assert.Null(TypeChecking.PrimitiveType.FromName(name));

    [Theory]
    [InlineData("byte[]", 3)]
    [InlineData("int[]", 3)]
    [InlineData("int[,]", 6)]
    [InlineData("string", 3)]
    public void NativeLength_HasIntegerTypeInOrdinaryLoopConditions(string type, int expected)
    {
        var authored = $$"""
            module Lengths
              func Count ({{type}}:values) → int
                ! mut
                var int:i = 0
                while i < (values).Length
                  set i i + 1
                return i
            """;
        object input = type switch
        {
            "byte[]" => new byte[] { 1, 2, 3 },
            "int[]" => new[] { 1, 2, 3 },
            "int[,]" => new int[2, 3],
            "string" => "Ωab",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(owner => owner.GetMethods())
                .Single(method => method.Name == "Count");
            Assert.Equal(expected, method.Invoke(null, [input]));
        }
    }

    [Theory]
    [InlineData("byte[]", "Count")]
    [InlineData("string", "length")]
    [InlineData("int", "Length")]
    public void NativeLength_DoesNotPermitUnmodeledMembersOrReceivers(string type, string member)
    {
        var source = $"module Lengths\n  func Count ({type}:values) → int\n    !\n    while (values).{member} > 0\n      return 1\n    return 0";
        var diagnostics = new DiagnosticBag();
        var module = Parse(source);
        Assert.Contains(Assert.Single(module.Functions).Body, statement => statement is WhileStatementNode);
        new TypeChecking.TypeChecker(diagnostics).Check(module);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("byte[]")]
    [InlineData("string")]
    public void NativeLength_DoesNotBecomeABooleanOrAnUnknownType(string type)
    {
        var source = $"module Lengths\n  func Count ({type}:values) → int\n    !\n    while (values).Length\n      return 1\n    return 0";
        var diagnostics = new DiagnosticBag();
        var module = Parse(source);
        Assert.Contains(Assert.Single(module.Functions).Body, statement => statement is WhileStatementNode);
        new TypeChecking.TypeChecker(diagnostics).Check(module);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        Assert.True(Compile(source).HasErrors);
    }

    [Fact]
    public void MetadataAliases_FollowLexicalAncestryRatherThanEqualNamespaceNames()
    {
        var parsed = Parse("""
            module ScopedAliases
              class Left
                func Make () → object
                  !
                  return new Builder()
              class Right
                func Make () → object
                  !
                  return new Builder()
              class Outside
                func Make () → object
                  !
                  return new Builder()
            """);
        var scopes = new[]
        {
            new NamespaceScopeInfo("outer", "Shared", "Shared", null, false, TextSpan.Empty),
            new NamespaceScopeInfo("left", "Repeated", "Shared.Repeated", "outer", false, TextSpan.Empty),
            new NamespaceScopeInfo("right", "Repeated", "Shared.Repeated", "outer", false, TextSpan.Empty)
        };
        var ids = new[] { "left", "right", "outer" };
        for (var index = 0; index < parsed.Classes.Count; index++)
        {
            parsed.Classes[index].NamespaceScopeId = ids[index];
            parsed.Classes[index].NamespaceIdentity = scopes.Single(scope => scope.Id == ids[index]).FullName;
        }
        var module = parsed.With(update =>
        {
            update.NamespaceScopes = scopes;
            update.Usings =
            [
                new UsingDirectiveNode(TextSpan.Empty, "System.Version", "Builder",
                    namespaceScopeId: "outer"),
                new UsingDirectiveNode(TextSpan.Empty, "System.Text.StringBuilder", "Builder",
                    namespaceScopeId: "left"),
                new UsingDirectiveNode(TextSpan.Empty, "System.IO.StringWriter", "Builder",
                    namespaceScopeId: "right")
            ];
        });
        var bound = new Binder(new DiagnosticBag()).Bind(module);
        var names = bound.Functions.Select(function =>
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(
                Assert.IsType<BoundNewExpression>(
                    Assert.IsType<BoundReturnStatement>(Assert.Single(function.Body)).Expression).Type)
            .RoslynSymbol!.ToDisplayString()).ToArray();
        Assert.Equal(new[] { "System.Text.StringBuilder", "System.IO.StringWriter", "System.Version" }, names);
    }

    [Theory]
    [InlineData("using StringBuilder = Missing.StringBuilder")]
    [InlineData("using StringBuilder = Other\n  using Other = System.Text.StringBuilder")]
    public void MetadataAliases_DoNotGuessMissingTargetsOrChainSiblingAliases(string imports)
    {
        var module = Parse($$"""
            module InvalidAlias
              {{imports}}
              func Make () → object
                !
                return new StringBuilder()
            """);
        var bound = new Binder(new DiagnosticBag()).Bind(module);
        var creation = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(Assert.Single(bound.Functions).Body)).Expression);
        Assert.Null(Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(creation.Type).RoslynSymbol);
    }

    [Fact]
    public void MetadataAliases_RetainConstructedGenericTypeAndGetterIdentity()
    {
        const string authored = """
            module GenericAlias
              using Items = System.Collections.Generic.List<int>
              func Read () → int
                ! alloc
                let Items:items = new Items()
                return items.Count
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Read");
            Assert.Equal(0, method.Invoke(null, null));
            var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
            var created = Assert.IsType<BoundNewExpression>(
                Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions).Body[0]).Initializer);
            Assert.Equal("System.Collections.Generic.List<int>",
                Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(created.Type).RoslynSymbol!.ToDisplayString());
        }
    }

    [Fact]
    public void MetadataImports_DoNotReplaceAShadowingSourceTypeWithABclType()
    {
        const string source = """
            module SourceShadow
              using System.Text
              class StringBuilder
                field int:Length public = 17
              func Read () → int
                ! alloc
                let StringBuilder:item = new StringBuilder()
                return item.Length
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var created = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundBindStatement>(bound.Functions.Single(function => function.Symbol.Name == "Read").Body[0]).Initializer);
        Assert.NotNull(created.ResolvedType);
        Assert.Null(Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(created.Type).RoslynSymbol);
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = CompileAssembly(result.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Read");
        Assert.Equal(17, method.Invoke(null, null));
    }

    [Theory]
    [InlineData("Text = System.Text", "Text.StringBuilder")]
    [InlineData("Builder = System.Text.StringBuilder", "Builder")]
    [InlineData("System.Text", "StringBuilder")]
    public void NativeNamespaceImports_RetainActualMetadataConstructorAndPropertyIdentity(string import, string typeName)
    {
        var source = $$"""
            module NamespaceFile Contoso.Tools
              using {{import}}
              class Box
                func Read () → int
                  ! alloc
                  let {{typeName}}:builder = new {{typeName}}("example")
                  return builder.Length
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var binding = Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions).Body[0]);
        var created = Assert.IsType<BoundNewExpression>(binding.Initializer);
        Assert.Equal("System.Text.StringBuilder",
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(created.Type).RoslynSymbol!.ToDisplayString());
        var graph = Calor.Compiler.Analysis.CallGraphAnalysis.Build(Parse(source));
        var callerId = Assert.Single(graph.Functions).Key;
        Assert.NotNull(graph.MetadataCreationType(callerId, created.Span));
        var returned = Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[1]);
        var member = Assert.IsType<BoundFieldAccessExpression>(returned.Expression);
        Assert.NotNull(graph.MetadataValueType(callerId, member.Target.Span));
        var effects = new DiagnosticBag();
        new Calor.Compiler.Effects.EffectEnforcementPass(effects).Enforce(Parse(source));
        Assert.False(effects.HasErrors, string.Join(Environment.NewLine, effects));
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var type = CompileAssembly(result.GeneratedCode).GetType("Contoso.Tools.Box")!;
        var method = type.GetMethod("Read", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        Assert.Equal(7, method.Invoke(Activator.CreateInstance(type), null));
    }

    [Fact]
    public void EscapedDefaultBinding_RemainsAVariableRatherThanADefaultInitializer()
    {
        const string authored = """
            module DefaultIdentifier
              func Run (int:@default) → int
                !
                let int:value = @default
                return value
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var method = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(method => method.Name == "Run");
            Assert.Equal(31, method.Invoke(null, [31]));
        }
    }

    [Theory]
    [InlineData("W1-F1-refout", false)]
    [InlineData("W1-F1-refout", true)]
    [InlineData("W1-F1-outvar-statement", false)]
    [InlineData("W1-F1-outvar-statement", true)]
    public void OriginalByReferenceFixtures_RetainNativeConverterCapability(string fixture, bool passthrough)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var source = File.ReadAllText(Path.Combine(root, "tests", "TestData", "InteropScope", "W1",
            "fixtures", fixture + ".cs.txt"));
        var conversion = new CSharpToCalorConverter(new ConversionOptions
        {
            RescueUnusableMembers = true,
            PassthroughOnError = passthrough
        }).Convert(source);
        Assert.True(conversion.Success, string.Join(Environment.NewLine, conversion.Issues)
            + Environment.NewLine + conversion.CalorSource);
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Equal(0, conversion.LossySubstitutionCount);
        Assert.Equal(0, conversion.DropCount);
        Assert.DoesNotContain("@default", conversion.CalorSource);
        var compiled = Compile(conversion.CalorSource!);
        Assert.False(compiled.HasErrors, conversion.CalorSource + Environment.NewLine
            + string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
        var methods = CompileAssembly(compiled.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        if (fixture == "W1-F1-refout")
            Assert.Equal(44, methods.Single(method => method.Name == "Run").Invoke(null, null));
        else
        {
            var parse = methods.Single(method => method.Name == "ParseOrZero");
            Assert.Equal(42, parse.Invoke(null, ["42"]));
            Assert.Equal(0, parse.Invoke(null, ["bad"]));
        }
    }

    [Fact]
    public void NativeReferenceDefaults_RetainSourceAndMetadataOverloadIdentityAfterReemission()
    {
        const string authored = """
            module Defaults
              func Pick (object:value) → int
                !
                return 1
              func Pick (string?:value) → int
                !
                return 2
              func ObjectChoice () → int
                !
                return Pick(default(object))
              func StringChoice () → int
                !
                return Pick(default(string))
              func ObjectText () → string?
                !
                return System.Convert.ToString(default(object))
              func StringText () → string?
                !
                return System.Convert.ToString(default(string))
            """;
        var module = Parse(authored);
        string ReemitDefault(string functionName)
        {
            var statement = Assert.IsType<ReturnStatementNode>(
                module.Functions.Single(function => function.Name == functionName).Body[0]);
            return Assert.IsType<CallExpressionNode>(statement.Expression).Arguments[0].Accept(new CalorEmitter());
        }
        var objectDefault = ReemitDefault("ObjectChoice");
        var stringDefault = ReemitDefault("StringChoice");
        var reemitted = authored.Replace("default(object)", objectDefault, StringComparison.Ordinal)
            .Replace("default(string)", stringDefault, StringComparison.Ordinal);
        foreach (var source in new[] { authored, reemitted })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            var methods = CompileAssembly(result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .ToArray();
            Assert.Equal(1, methods.Single(method => method.Name == "ObjectChoice").Invoke(null, null));
            Assert.Equal(2, methods.Single(method => method.Name == "StringChoice").Invoke(null, null));
            Assert.Equal("", methods.Single(method => method.Name == "ObjectText").Invoke(null, null));
            Assert.Null(methods.Single(method => method.Name == "StringText").Invoke(null, null));

            var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
            foreach (var (name, selectedIndex) in new[] { ("ObjectChoice", 0), ("StringChoice", 1) })
            {
                var function = bound.Functions.Single(function => function.Symbol.Name == name);
                var call = Assert.IsType<BoundCallExpression>(
                    Assert.IsType<BoundReturnStatement>(function.Body[0]).Expression);
                Assert.Equal(bound.Functions[selectedIndex].Symbol.Id, call.ResolvedSymbol!.Id);
            }
            foreach (var (name, expectedType) in new[]
            {
                ("ObjectText", SpecialType.System_Object), ("StringText", SpecialType.System_String)
            })
            {
                var function = bound.Functions.Single(function => function.Symbol.Name == name);
                var call = Assert.IsType<BoundCallExpression>(
                    Assert.IsType<BoundReturnStatement>(function.Body[0]).Expression);
                Assert.Equal("System.Convert", call.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
                Assert.Equal(expectedType, Assert.Single(call.ResolvedMetadataMethod.Parameters).Type.SpecialType);
                Assert.Equal(Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Annotated,
                    Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(call.Arguments[0].Type).NullableAnnotation);
            }
        }
    }

    [Fact]
    public void NativeStringDefault_DoesNotAcquireANonNullGuaranteeFromItsCast()
    {
        var binding = Compile("""
            module DefaultNull
              func Run () → string
                !
                let string:value = default(string)
                return value
            """);
        Assert.Contains(binding.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableToNonNullableBinding);
        var returning = Compile("""
            module DefaultNull
              func Run () → string
                !
                return default(string)
            """);
        Assert.Contains(returning.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Fact]
    public void NativeInventory_ByReferenceCallsRetainMetadataThroughTheProjectDriver()
    {
        const string source = """
            module Inventory overflow unchecked
              using System.Collections.Generic

              module Inventory
                func ParseOr (string:text, int:fallback) → int
                  !
                  var int:n = default(int)
                  if int.TryParse(text, out n)
                    return n
                  return fallback

                func CountOf (Dictionary<string,int>:stock, string:item) → int
                  !
                  var int:count = default(int)
                  stock.TryGetValue(item, out count)
                  return count

                func IsNumber (string:text) → bool
                  !
                  return int.TryParse(text, out _)

                func Bump (ref int:x) → void
                  ! mut
                  set x (x + 1)

                func Twice (in int:x) → int
                  !
                  return x * 2

                func Run () → int
                  ! mut
                  var int:total = 1
                  Bump(ref total)
                  return Twice(in total) + ParseOr("40", 0)
            """;
        var directory = Path.Combine(Path.GetTempPath(), "calor-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = new FileInfo(Path.Combine(directory, "inventory.calr"));
            File.WriteAllText(file.FullName, source);
            var diagnostics = new DiagnosticBag();
            var result = CompilationDriver.CompileAll([file],
                _ => new CompilationOptions { StatusWriter = TextWriter.Null },
                crossModuleEnforcement: true, crossModulePolicy: Calor.Compiler.Effects.UnknownCallPolicy.Strict,
                diagnosticSink: diagnostics);
            Assert.False(result.AnyErrors, string.Join(Environment.NewLine, diagnostics));
            Assert.Empty(result.Skipped);
            var compiled = Assert.Single(result.Compiled);
            var methods = CompileAssembly(compiled.Result.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .ToArray();
            Assert.Equal(44, methods.Single(method => method.Name == "Run").Invoke(null, null));
            var parse = methods.Single(method => method.Name == "ParseOr");
            Assert.Equal(13, parse.Invoke(null, ["13", 7]));
            Assert.Equal(7, parse.Invoke(null, ["bad", 7]));
            var count = methods.Single(method => method.Name == "CountOf");
            var stock = new Dictionary<string, int> { ["found"] = 9 };
            Assert.Equal(9, count.Invoke(null, [stock, "found"]));
            Assert.Equal(0, count.Invoke(null, [stock, "missing"]));
            var isNumber = methods.Single(method => method.Name == "IsNumber");
            Assert.Equal(true, isNumber.Invoke(null, ["13"]));
            Assert.Equal(false, isNumber.Invoke(null, ["bad"]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("int[]", "new int[] {10, 20, 30, 40}")]
    [InlineData("string", "\"abcd\"")]
    [InlineData("string[]", "new string[] {\"a\", \"b\", \"c\", \"d\"}")]
    public void NativeSlices_RetainTheirContainerType(string typeName, string value)
    {
        var source = $$"""
            module Slices
              func Slice ({{typeName}}:values) → {{typeName}}
                ! alloc
                let {{typeName}}:copy = values[1..^1]
                return copy
              func Direct () → {{typeName}}
                ! alloc
                return ({{value}})[1..^1]
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var assembly = CompileAssembly(result.GeneratedCode);
        var methods = assembly.GetTypes().SelectMany(type => type.GetMethods()).ToArray();
        var direct = methods.Single(method => method.Name == "Direct").Invoke(null, null);
        if (typeName == "int[]")
            Assert.Equal(new[] {20, 30}, Assert.IsType<int[]>(direct));
        else if (typeName == "string[]")
            Assert.Equal(new[] {"b", "c"}, Assert.IsType<string[]>(direct));
        else
            Assert.Equal("bc", direct);
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var slice = Assert.IsType<BoundArrayAccess>(
            Assert.IsType<BoundBindStatement>(bound.Functions[0].Body[0]).Initializer);
        if (typeName.EndsWith("[]", StringComparison.Ordinal))
            Assert.IsAssignableFrom<IArrayTypeSymbol>(slice.MetadataArrayResultType);
        else
            Assert.Equal(SpecialType.System_String,
                Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(slice.Type).RoslynSymbol!.SpecialType);
    }

    [Theory]
    [InlineData("values[\"bad\"..]")]
    [InlineData("values[1..true]")]
    [InlineData("values[^\"bad\"]")]
    public void NativeSlices_InvalidEndpointTypesRemainErrors(string expression)
    {
        var source = $"module InvalidSlice\n  func Slice (int[]:values) → int[]\n    ! alloc\n    return {expression}";
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("let List<string>:lines = File.ReadAllLines(path)\n    return lines.Count", "int")]
    [InlineData("return File.ReadAllLines(path)", "List<string>")]
    [InlineData("let List<string>:lines = System.IO.File.ReadAllLines(path)\n    return lines.Count", "int")]
    public void NativeArrayResults_RetainTheCalorLayerCollectionDiagnostic(string body, string output)
    {
        var source = $"module Files\n  func Get (string:path) → {output}\n    ! fs:r\n    {body}";
        var result = Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.BindArrayToConcreteCollection);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.CodeGenCompilationError);
        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        var statement = Assert.Single(bound.Functions).Body[0];
        var call = Assert.IsType<BoundCallExpression>(statement is BoundBindStatement binding
            ? binding.Initializer : Assert.IsType<BoundReturnStatement>(statement).Expression);
        Assert.Equal("System.IO.File", call.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
        Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod.ReturnType);
        Assert.NotNull(call.MetadataArrayResultType);
    }

    [Fact]
    public void NativeCharacterContract_RetainsTheUnicodePredicateAndActualMetadataTarget()
    {
        const string authored = """
            module AgentReference41
              func DigitValue (char:c) → int
                !
                requires char.IsDigit(c)
                return c - '0'
            """;
        var module = Parse(authored);
        var predicate = Assert.Single(Assert.Single(module.Functions).Preconditions).Condition.Accept(new CalorEmitter());
        foreach (var source in new[] { authored, authored.Replace("char.IsDigit(c)", predicate, StringComparison.Ordinal) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            Assert.Contains("char.IsDigit", result.GeneratedCode);
            var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "DigitValue");
            Assert.Equal(5, method.Invoke(null, ['5']));
            Assert.Equal('\u0665' - '0', method.Invoke(null, ['\u0665']));
        }
    }

    [Fact]
    public void StringIndexReceiver_UsesTheSelectedCharToStringMethodWithoutUnknownEffects()
    {
        const string authored = """
            module M
              func Test private (string:text) → string
                return text[0].ToString()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(authored));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.Equal(SpecialType.System_Char, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
        Assert.Equal("ToString", call.ResolvedMetadataMethod.Name);
        var result = Compile(authored);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == "Test");
        Assert.Equal("Ω", method.Invoke(null, ["Ωvalue"]));
    }

    [Theory]
    [InlineData("char")]
    [InlineData("int")]
    public void MissingEscapedPrimitiveReceiver_DoesNotBecomeAStaticMetadataType(string name)
    {
        var source = $"module M\n  func Test () → string\n    !\n    return @{name}.ToString()";
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.IsNotType<BoundTypeReferenceExpression>(call.Receiver);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(Compile(source).HasErrors);
    }

    [Fact]
    public void EscapedPrimitiveValueReceiver_RetainsItsValueIdentity()
    {
        const string source = """
            module M
              func Test (char:@char) → string
                !
                return @char.ToString()
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.IsType<BoundVariableExpression>(call.Receiver);
        Assert.Equal(SpecialType.System_Char, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Contains("@char.ToString()", result.GeneratedCode);
    }

    [Fact]
    public void DeclaredGenericList_DoesNotAcquireTheBclCollectionIdentity()
    {
        const string source = """
            module M
              class List<T>
                func Read (string:path) → List<string>
                  ! fs:r
                  return File.ReadAllLines(path)
            """;
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(Parse(source));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.BindArrayToConcreteCollection);
    }

    [Fact]
    public void ByteListCalls_RetainExactMetadataOverloadsAndArrayResult()
    {
        const string source = """
            module Codec
              func Encode (List<byte>:bytes, byte:item) → byte[]
                ! mut
                bytes.Add(item)
                return bytes.ToArray()
              func Write (List<byte>:bytes, byte:item, string:path) → void
                ! mut, fs:w
                bytes.Add(item)
                File.WriteAllBytes(path, bytes.ToArray())
              func WriteEncoded (List<byte>:bytes, byte:item, string:path) → void
                ! mut, fs:w
                File.WriteAllBytes(path, Encode(bytes, item))
            """;
        var metadataDiagnostics = new DiagnosticBag();
        var metadataBound = new Binder(metadataDiagnostics).Bind(Parse(source));
        var write = metadataBound.Functions.Single(function => function.Symbol.Name == "WriteEncoded");
        var writeCall = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(write.Body[0]).Expression);
        Assert.True(writeCall.ResolvedMetadataMethod is not null,
            $"Array argument: {writeCall.Arguments[1].Type.DisplayString}; "
            + $"input element: {writeCall.Arguments[1].MethodInputArrayType?.ElementType.DisplayString}");
        Assert.Equal("System.IO.File", writeCall.ResolvedMetadataMethod.ContainingType.ToDisplayString());
        Assert.Equal(SpecialType.System_Byte, Assert.IsAssignableFrom<IArrayTypeSymbol>(
            writeCall.ResolvedMetadataMethod.Parameters[1].Type).ElementType.SpecialType);
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Encode");
        var values = new List<byte> { 7 };
        Assert.Equal(new byte[] { 7, 255 }, Assert.IsType<byte[]>(method.Invoke(null, [values, (byte)255])));
        var path = Path.Combine(Path.GetTempPath(), $"calor-byte-codec-{Guid.NewGuid():N}.bin");
        try
        {
            method.DeclaringType!.GetMethod("WriteEncoded")!.Invoke(null, [new List<byte> { 2 }, (byte)254, path]);
            Assert.Equal(new byte[] { 2, 254 }, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var encode = bound.Functions.Single(function => function.Symbol.Name == "Encode");
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(encode.Body[1]).Expression);
        var array = Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod!.ReturnType);
        Assert.Equal(SpecialType.System_Byte, array.ElementType.SpecialType);
        Assert.Equal("System.Collections.Generic.List<byte>", call.ResolvedMetadataMethod.ContainingType.ToDisplayString());
    }

    [Fact]
    public void SourceGenericListCalls_RetainTheSourceTypeParameter()
    {
        const string source = """
            module GenericCollection
              func Append<T> (List<T>:items, T:item) → T[]
                ! mut
                items.Add(item)
                return items.ToArray()
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Append").MakeGenericMethod(typeof(string));
        Assert.Equal(new[] { "first", "second" },
            Assert.IsType<string[]>(method.Invoke(null, [new List<string> { "first" }, "second"])));
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var add = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.Equal("Add", add.ResolvedMetadataMethod!.Name);
        Assert.Equal("T", Assert.IsAssignableFrom<ITypeParameterSymbol>(
            Assert.Single(add.ResolvedMetadataMethod.Parameters).Type).Name);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[1]).Expression);
        var array = Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod!.ReturnType);
        var parameter = Assert.IsAssignableFrom<ITypeParameterSymbol>(array.ElementType);
        Assert.Equal("T", parameter.Name);
        Assert.Equal(TypeKind.TypeParameter, parameter.TypeKind);
    }

    [Fact]
    public void ClassGenericListCalls_RetainInterfaceConstraints()
    {
        const string source = """
            module GenericClass
              class Repository<T>
                where T : IDisposable
                func Append (List<T>:items, T:item) → T[]
                  ! mut
                  items.Add(item)
                  return items.ToArray()
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var append = Assert.Single(bound.Functions);
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(append.Body[1]).Expression);
        var array = Assert.IsAssignableFrom<IArrayTypeSymbol>(call.ResolvedMetadataMethod!.ReturnType);
        var parameter = Assert.IsAssignableFrom<ITypeParameterSymbol>(array.ElementType);
        Assert.Equal("System.IDisposable", Assert.Single(parameter.ConstraintTypes).ToDisplayString());
        var type = CompileAssembly(result.GeneratedCode).GetTypes().Single(type => type.Name == "Repository`1")
            .MakeGenericType(typeof(MemoryStream));
        using var item = new MemoryStream();
        Assert.Same(item, Assert.Single(Assert.IsType<MemoryStream[]>(
            type.GetMethod("Append")!.Invoke(Activator.CreateInstance(type), [new List<MemoryStream>(), item]))));
    }

    [Fact]
    public void SourceGenericList_DoesNotSubstituteObjectForAnInvalidArgument()
    {
        const string source = """
            module GenericCollection
              func Append<T> (List<T>:items) → void
                ! mut
                items.Add("not an arbitrary T")
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(Compile(source).HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeTryParseOut_RetainsLocalWritesAndDiscard(bool discard)
    {
        var source = $$"""
            module NativeOut
              func ParseNumber (string:text) → int
                !
                var int:n = 0
                if int.TryParse(text, out {{(discard ? "_" : "n")}})
                  return {{(discard ? "1" : "n")}}
                return -1
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "ParseNumber");
        Assert.Equal(discard ? 1 : 42, method.Invoke(null, ["42"]));
        Assert.Equal(-1, method.Invoke(null, ["invalid"]));
    }

    [Fact]
    public void NativeRefAndIn_RetainStorageAndRejectNonLvalues()
    {
        const string source = """
            module NativeByRef
              func Increment (ref int:x) → void
                ! mut
                set x x + 1
              func Read (in int:x) → int
                !
                return x
              func Run () → int
                ! mut
                var int:n = 41
                Increment(ref n)
                return Read(in n)
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var method = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Run");
        Assert.Equal(42, method.Invoke(null, null));
        var invalid = Compile(source.Replace("Increment(ref n)", "Increment(ref (n + 1))", StringComparison.Ordinal));
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidByReferenceOperand);
    }

    [Fact]
    public void NativeCallDiscard_DoesNotHideEscapedReferencesOrInvalidRefOperands()
    {
        const string source = """
            module NativeDiscard
              func ParseNumber (string:text) → bool
                !
                return int.TryParse(text, out _)
            """;
        var escaped = Compile(source.Replace("out _", "out @_", StringComparison.Ordinal));
        Assert.Contains(escaped.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        var invalidRef = Compile(source.Replace("out _", "ref _", StringComparison.Ordinal));
        Assert.Contains(invalidRef.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidByReferenceOperand);
    }

    [Fact]
    public void NativeRecordClone_PreservesTheOriginalAndActualCopy()
    {
        const string source = """
            module RecordClone
              record Item (int:X, int:Y)
              func Copy (Item:value) → Item
                ! alloc, mut
                return value with { X = value.X + 1 }
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var assembly = CompileAssembly(result.GeneratedCode);
        var type = assembly.GetTypes().Single(type => type.Name == "Item");
        var original = Activator.CreateInstance(type, 4, 8);
        var method = assembly.GetTypes().SelectMany(type => type.GetMethods()).Single(method => method.Name == "Copy");
        var clone = method.Invoke(null, [original]);
        Assert.NotSame(original, clone);
        Assert.Equal(4, type.GetProperty("X")!.GetValue(original));
        Assert.Equal(5, type.GetProperty("X")!.GetValue(clone));
        Assert.Equal(8, type.GetProperty("Y")!.GetValue(clone));
        var pure = Compile(source.Replace("! alloc, mut", "!", StringComparison.Ordinal));
        Assert.Contains(pure.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("System.Version")]
    public void NativeNonvirtualMetadata_UsesTheSelectedReferenceDeclarationContract(string receiverType)
    {
        var authored = $$"""
            module Nonvirtual nullable oblivious
              func Read public ({{receiverType}}:value) → System.Type
                !
                return value.GetType()
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(source));
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
            var method = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
            Assert.Equal(SpecialType.System_Object, method.ContainingType.SpecialType);
            Assert.Equal("GetType", method.Name);
            Assert.False(method.IsVirtual);
            Assert.Empty(method.Parameters);
            Assert.DoesNotContain(method.Locations, location => location.IsInSource);
            Assert.False(call.IsInaccessibleCall || call.HasOpaqueOverloads || call.HasUnresolvedArguments);
            var resolver = new Calor.Compiler.Effects.EffectResolver();
            Assert.Equal(Calor.Compiler.Effects.EffectResolutionStatus.Unknown, resolver.Resolve(
                Calor.Compiler.Effects.EffectResolverKey.FromBoundReceiver(
                    call.Receiver!.Type, method.Name, call.ResolvedParameterTypes)).Status);
            Assert.Equal(Calor.Compiler.Effects.EffectResolutionStatus.PureExplicit, resolver.Resolve(
                Calor.Compiler.Effects.EffectResolverKey.FromBoundReceiver(
                    Calor.Compiler.Binding.Metadata.MetadataBinderResult.ToNominalBoundType(method.ContainingType),
                    method.Name, call.ResolvedParameterTypes)).Status);
            var graph = Calor.Compiler.Analysis.CallGraphAnalysis.Build(Parse(source));
            var indexed = graph.ComputedMemberCall(Assert.Single(graph.Functions).Key, call.Span);
            Assert.Equal(method.ToDisplayString(), indexed!.ResolvedMetadataMethod!.ToDisplayString());
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
            var read = CompileAssembly(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
                .Single(candidate => candidate.Name == "Read");
            object value = receiverType == "object" ? new object() : new Version(1, 2);
            Assert.Equal(value.GetType(), read.Invoke(null, [value]));
            Assert.IsType<NullReferenceException>(
                Assert.Throws<TargetInvocationException>(() => read.Invoke(null, [null])).InnerException);
        }
    }

    [Fact]
    public void NativeNonvirtualMetadata_ConstructorUsesTheSameSelectedContract()
    {
        const string authored = """
            module Nonvirtual nullable oblivious
              class Box public
                ctor public (object:value)
                  value.GetType()
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            var box = CompileAssembly(result.GeneratedCode).GetTypes().Single(type => type.Name == "Box");
            Assert.NotNull(Activator.CreateInstance(box, [new object()]));
            Assert.IsType<NullReferenceException>(
                Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(box, [null])).InnerException);
        }
    }

    [Fact]
    public void NativeNonvirtualMetadata_StillChargesAndEvaluatesTheReceiverOnce()
    {
        const string source = """
            module Nonvirtual nullable oblivious
              class Probe public
                field int:Calls public static = 0
                func Receiver private static (object:value) → object
                  set Calls Calls + 1
                  return value
                func Read public static (object:value) → System.Type
                  ! mut
                  return Receiver(value).GetType()
            """;
        var result = Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var pure = Compile(source.Replace("func Read public static (object:value) → System.Type\n      ! mut",
            "func Read public static (object:value) → System.Type\n      !", StringComparison.Ordinal));
        Assert.Contains(pure.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'mut'", StringComparison.Ordinal));
        Assert.DoesNotContain(pure.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        var type = CompileAssembly(result.GeneratedCode).GetTypes().Single(candidate => candidate.Name == "Probe");
        var read = type.GetMethod("Read")!;
        Assert.Equal(typeof(object), read.Invoke(null, [new object()]));
        Assert.Equal(1, type.GetField("Calls")!.GetValue(null));
        Assert.IsType<NullReferenceException>(
            Assert.Throws<TargetInvocationException>(() => read.Invoke(null, [null])).InnerException);
        Assert.Equal(2, type.GetField("Calls")!.GetValue(null));
    }

    [Theory]
    [InlineData("cw", "System.Console.WriteLine(1)")]
    [InlineData("mut", "set Count Count + 1")]
    [InlineData("", "Missing()")]
    public void NativeNonvirtualMetadata_DoesNotReplaceASourceGetType(string effect, string body)
    {
        var source = $$"""
            module Nonvirtual nullable oblivious
              class Shadow public
                field int:Count
                func GetType public () → int
                  !
                  {{(effect.Length == 0 ? "" : "! " + effect)}}
                  {{body}}
                  return 7
              func Read (Shadow:value) → int
                !
                return value.GetType()
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(candidate => candidate.ResolvedSymbol?.Name == "Shadow.GetType");
        Assert.Null(call.ResolvedMetadataMethod);
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains($"'{(effect.Length == 0 ? "unknown" : effect)}'", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeNonvirtualMetadata_RetainsSourceOverloadAndArgumentEffects()
    {
        const string source = """
            module Nonvirtual nullable oblivious
              class Shadow public
                func GetType public (int:value) → int
                  ! cw
                  System.Console.WriteLine(value)
                  return value
              func Argument () → int
                ! cw
                System.Console.WriteLine(1)
                return 1
              func Read (Shadow:value) → int
                !
                return value.GetType(Argument())
            """;
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'Read'", StringComparison.Ordinal)
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
        Assert.False(Compile(source.Replace("func Read (Shadow:value) → int\n    !",
            "func Read (Shadow:value) → int\n    ! cw", StringComparison.Ordinal)).HasErrors);
    }

    [Theory]
    [InlineData("object", "Equals(value)", "bool")]
    [InlineData("object", "GetHashCode()", "int")]
    [InlineData("System.Collections.IEnumerable", "GetType()", "System.Type")]
    [InlineData("System.DayOfWeek", "GetType()", "System.Type")]
    [InlineData("Missing", "GetType()", "System.Type")]
    [InlineData("object", "Missing()", "System.Type")]
    [InlineData("object", "MemberwiseClone()", "object")]
    [InlineData("System.Type", "Name", "string")]
    public void NativeNonvirtualMetadata_DoesNotAuthorizeDispatchBoxingOrUnknownTargets(
        string type, string expression, string resultType)
    {
        var source = $$"""
            module Nonvirtual nullable oblivious
              func Read ({{type}}:value) → {{resultType}}
                !
                return value.{{expression}}
            """;
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Theory]
    [InlineData("", "GetType()", "System.Type")]
    [InlineData("class", "GetType()", "System.Type")]
    [InlineData("struct", "GetType()", "System.Type")]
    [InlineData("", "Equals(value)", "bool")]
    [InlineData("class", "Equals(value)", "bool")]
    [InlineData("System.IComparable", "Equals(value)", "bool")]
    public void NativeNonvirtualMetadata_DoesNotTransferObjectContractsToTypeParameters(
        string constraint, string expression, string resultType)
    {
        var source = $$"""
            module Nonvirtual nullable oblivious
              func Read<T> (T:value) → {{resultType}}
                {{(constraint.Length == 0 ? "" : "where T : " + constraint)}}
                !
                return value.{{expression}}
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal(SpecialType.System_Object, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeNonvirtualMetadata_DoesNotRepairAmbiguousSourceCalls()
    {
        const string source = """
            module Nonvirtual nullable oblivious
              class Shadow public
                func GetType public (string:value) → int
                  !
                  return 1
                func GetType public (System.Version:value) → int
                  !
                  return 2
              func Read (Shadow:value) → int
                !
                return value.GetType(null)
            """;
        Assert.Contains(Compile(source).Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
    }

    [Fact]
    public void NativeNonvirtualMetadata_DoesNotAuthorizeAnUnboundUnsafeCall()
    {
        const string source = """
            module Nonvirtual nullable oblivious
              func Read (object:value) → System.Type
                !
                unsafe
                  return value.GetType()
            """;
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    private static void AssertSelectedLiteralOverload(string source, string expected)
    {
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.NotNull(call.ResolvedSymbol);
        var selected = bound.Functions.Single(function => function.Symbol.Id == call.ResolvedSymbol.Id);
        var statement = Assert.IsType<BoundReturnStatement>(Assert.Single(selected.Body));
        Assert.Equal(expected, Assert.IsType<BoundStringLiteral>(statement.Expression).Value);
    }

    private static CompilationResult Compile(string source) => Program.Compile(source, "native-metadata.calr",
        new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return module;
    }

    private static Assembly CompileAssembly(string source)
    {
        var compilation = CSharpCompilation.Create("NativeMetadata_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, source + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    [Theory]
    [InlineData("System . Math . PI", "double", "System.Math", false)]
    [InlineData("System . Math . PI", "double", "System.Math", true)]
    [InlineData("System . Int32 . MaxValue", "int", "int", false)]
    [InlineData("System . Int32 . MaxValue", "int", "int", true)]
    public void QualifiedMetadataConstants_DoNotEvaluateTheirTypeQualifier(
        string expression, string type, string owner, bool typeChecking)
    {
        var source = $$"""
            module QualifiedConstant
              func Read public () → {{type}}
                !
                return {{expression}}
            """;
        foreach (var native in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(native));
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var read = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
            Assert.IsType<BoundTypeReferenceExpression>(read.Target);
            var field = Assert.IsAssignableFrom<IFieldSymbol>(read.ResolvedMetadataMember);
            Assert.True(field.IsStatic && field.IsConst);
            Assert.Equal(owner, field.ContainingType.ToDisplayString());

            var compiled = Program.Compile(native, "qualified-constant.calr", new CompilationOptions
            {
                StrictApi = true, StrictEffects = true, EnableTypeChecking = typeChecking
            });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Contains(expression.Replace(" . ", ".", StringComparison.Ordinal), compiled.GeneratedCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifiedMetadataReadonlyFields_StillNeedIndependentEffectProof(bool typeChecking)
    {
        const string source = """
            module QualifiedReadonly
              func Read public () → System.EventArgs
                !
                return System . EventArgs . Empty
            """;
        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var read = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        var field = Assert.IsAssignableFrom<IFieldSymbol>(read.ResolvedMetadataMember);
        Assert.True(field.IsStatic && field.IsReadOnly);
        Assert.False(field.IsConst);
        var effects = new DiagnosticBag();
        new EffectEnforcementPass(effects).Enforce(module);
        Assert.Contains(effects, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
            && diagnostic.Message.Contains(field.ToDisplayString(), StringComparison.Ordinal));
        var compiled = Program.Compile(source, "qualified-readonly.calr", new CompilationOptions
        {
            StrictApi = true, StrictEffects = true, EnableTypeChecking = typeChecking
        });
        Assert.True(compiled.HasErrors);
        Assert.Empty(compiled.GeneratedCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifiedConstantSpelling_DoesNotHideEvaluatedSourceReceiverEffects(bool typeChecking)
    {
        const string source = """
            module QualifiedShadow
              class Value
                field double:PI public
              class Holder
                field Value:Math public
              func Receiver (Holder:value) → Holder
                ! cw
                System.Console.WriteLine("receiver")
                return value
              func Read public (Holder:System) → double
                !
                return Receiver(System) . Math . PI
            """;
        var options = new CompilationOptions
        {
            StrictApi = true, StrictEffects = true, EnableTypeChecking = typeChecking
        };
        var rejected = Program.Compile(source, "qualified-shadow.calr", options);
        Assert.Contains(rejected.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Function 'Read'", StringComparison.Ordinal)
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
        Assert.Empty(rejected.GeneratedCode);
        var accepted = Program.Compile(source.Replace(
            "func Read public (Holder:System) → double\n    !",
            "func Read public (Holder:System) → double\n    ! cw", StringComparison.Ordinal),
            "qualified-shadow.calr", options);
        Assert.False(accepted.HasErrors, string.Join("\n", accepted.Diagnostics));
    }

    [Theory]
    [InlineData("System . EventArgs . Empty", "System.EventArgs", "System.EventArgs")]
    [InlineData("System . String . Empty", "string", "string")]
    public void QualifiedMetadataStaticFields_TransferTypesWithoutEffectPermission(
        string expression, string type, string owner)
    {
        var source = $$"""
            module QualifiedFieldType
              func Read public () → {{type}}
                !
                return {{expression}}
            """;
        foreach (var native in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(native));
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var read = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
            Assert.IsType<BoundTypeReferenceExpression>(read.Target);
            var field = Assert.IsAssignableFrom<IFieldSymbol>(read.ResolvedMetadataMember);
            Assert.True(field.IsStatic && field.IsReadOnly);
            Assert.False(field.IsConst);
            Assert.Equal(owner, field.ContainingType.ToDisplayString());

            var typed = Program.Compile(native, "qualified-field-type.calr", new CompilationOptions
            {
                EnableTypeChecking = true, EnforceEffects = false
            });
            Assert.False(typed.HasErrors, string.Join("\n", typed.Diagnostics));
            var tree = CSharpSyntaxTree.ParseText(typed.GeneratedCode);
            var compilation = CSharpCompilation.Create("QualifiedFieldType", [tree],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(compilation.GetDiagnostics(), item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            var access = Assert.Single(tree.GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>()
                .Where(member => member.Name.Identifier.ValueText == "Empty"));
            var emitted = Assert.IsAssignableFrom<IFieldSymbol>(compilation.GetSemanticModel(tree).GetSymbolInfo(access).Symbol);
            Assert.Equal(field.ToDisplayString(), emitted.ToDisplayString());
            Assert.Equal(field.Type.ToDisplayString(), emitted.Type.ToDisplayString());

            foreach (var typeChecking in new[] { false, true })
            {
                var strict = Program.Compile(native, "qualified-field-type.calr", new CompilationOptions
                {
                    StrictApi = true, StrictEffects = true, EnableTypeChecking = typeChecking
                });
                Assert.True(strict.HasErrors);
                Assert.Empty(strict.GeneratedCode);
                Assert.DoesNotContain(strict.Diagnostics.Errors, item => item.Code == DiagnosticCode.UndefinedReference);
                Assert.Contains(strict.Diagnostics, item => item.Code == DiagnosticCode.UnknownExternalCall
                    && item.Message.Contains(field.ToDisplayString(), StringComparison.Ordinal));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifiedMetadataStaticFieldSpelling_DoesNotHideAValueReceiver(bool typeChecking)
    {
        const string source = """
            module QualifiedFieldShadow
              class Value
                field string:Empty public
              class Holder
                field Value:String public
              func Receiver (Holder:value) → Holder
                ! cw
                System.Console.WriteLine("receiver")
                return value
              func Read public (Holder:System) → string
                !
                return Receiver(System) . String . Empty
            """;
        var result = Program.Compile(source, "qualified-field-shadow.calr", new CompilationOptions
        {
            StrictApi = true, StrictEffects = true, EnableTypeChecking = typeChecking
        });
        Assert.Contains(result.Diagnostics.Errors, item => item.Code == DiagnosticCode.ForbiddenEffect
            && item.Message.Contains("Function 'Read'", StringComparison.Ordinal)
            && item.Message.Contains("'cw'", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedCode);
    }

    [Fact]
    public void QualifiedMetadataStaticFields_DoNotResolveMissingTypeQualifiers()
    {
        const string source = """
            module MissingFieldType
              func Read public () → object
                !
                return Missing . EventArgs . Empty
            """;
        var result = Program.Compile(source, "missing-field-type.calr", new CompilationOptions
        {
            EnableTypeChecking = true, EnforceEffects = false
        });
        Assert.Contains(result.Diagnostics.Errors, item => item.Code == DiagnosticCode.UndefinedReference
            && item.Message.Contains("'Missing'", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedCode);
    }

    [Fact]
    public void QualifiedMetadataStaticFields_DoNotAddWarningsToExistingEnumConstants()
    {
        const string source = """
            module ExistingConstantType
              func Compare () → bool
                !
                return StringComparison.Ordinal == StringComparison.Ordinal
            """;
        var result = Program.Compile(source, "existing-constant-type.calr", new CompilationOptions
        {
            EnableTypeChecking = true, EnforceEffects = true
        });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == DiagnosticCode.UndefinedReference);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NonnegativeLengthFormatting_RequiresTheSelectedCoreGetterAndParameterlessIntegerMethod(
        bool conditional, bool typeChecking)
    {
        var source = $$"""
            module LengthFormatting
              func Read public ({{(conditional ? "string?" : "string")}}:value) → string
                !
                return {{(conditional ? "value?.Length.ToString() ?? \"null\"" : "value.Length.ToString()")}}
            """;
        foreach (var native in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(native));
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
            var method = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
            Assert.Equal(SpecialType.System_Int32, method.ContainingType.SpecialType);
            Assert.Equal("ToString", method.Name);
            Assert.False(method.IsStatic || method.IsGenericMethod);
            Assert.Empty(method.Parameters);
            Assert.True(SymbolEqualityComparer.Default.Equals(method.ContainingType, call.ResolvedMetadataReceiverType));
            var length = Assert.IsType<BoundFieldAccessExpression>(call.Receiver);
            var property = Assert.IsAssignableFrom<IPropertySymbol>(length.ResolvedMetadataMember);
            Assert.Equal("Length", property.Name);
            Assert.Equal(SpecialType.System_String, property.ContainingType.SpecialType);
            Assert.True(SymbolEqualityComparer.Default.Equals(property.Type, method.ContainingType));
            Assert.True(SymbolEqualityComparer.Default.Equals(property.ContainingAssembly, method.ContainingAssembly));
            Assert.DoesNotContain(property.Locations.Concat(method.Locations), location => location.IsInSource);
            if (conditional)
                Assert.Empty(Assert.IsType<BoundStructuralExpression>(length.Target).Children);
            else
                Assert.IsType<BoundVariableExpression>(length.Target);
            // The restored 0.29 System.Int32 contract makes every Int32.ToString overload
            // pure, so the selected method resolves on its own; the nonnegative-length
            // proof is no longer the only thing admitting it.
            Assert.Equal(EffectResolutionStatus.PureExplicit, new EffectResolver().Resolve(
                EffectResolverKey.FromBoundReceiver(length.Type, method.Name, call.ResolvedParameterTypes)).Status);

            var compiled = Program.Compile(native, "length-formatting.calr", new CompilationOptions
            {
                StrictApi = true, StrictEffects = true, EnableTypeChecking = typeChecking
            });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
            var read = CompileAssembly(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "Read");
            foreach (var count in new[] { 0, 1, 299, 300, 65535 })
                Assert.Equal(count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    read.Invoke(null, [new string('x', count)]));
            if (conditional)
                Assert.Equal("null", read.Invoke(null, [null]));
        }
    }

    [Theory]
    [InlineData("Receiver(Argument(value)).Length.ToString()")]
    [InlineData("Receiver(Argument(value))?.Length.ToString() ?? \"null\"")]
    public void NonnegativeLengthFormatting_StillChargesEvaluatedReceiverAndArgumentEffects(string expression)
    {
        var source = $$"""
            module LengthEvaluation
              func Receiver (string:value) → string
                ! cw
                System.Console.WriteLine("receiver")
                return value
              func Argument (string:value) → string
                ! cr
                let string?:line = System.Console.ReadLine()
                return value
              func Read (string:value) → string
                !
                return {{expression}}
            """;
        foreach (var typed in new[] { false, true })
        {
            var rejected = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            foreach (var effect in new[] { "cw", "cr" })
                Assert.Contains(rejected.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("Function 'Read'", StringComparison.Ordinal)
                    && diagnostic.Message.Contains($"'{effect}'", StringComparison.Ordinal));
            Assert.Empty(rejected.GeneratedCode);
            var admitted = Program.Compile(source.Replace("    !\n", "    ! cw, cr\n", StringComparison.Ordinal),
                null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.False(admitted.HasErrors, string.Join("\n", admitted.Diagnostics));
        }
    }

    // Object.ToString and IFormattable.ToString dispatch still have no effect contract.
    // The Int32.ToString and System.Convert rows that used to live here pinned contracts
    // withdrawn on 2026-11-11 and restored on 2026-11-14; they moved to
    // NonnegativeLengthFormatting_NeighboringBuiltInFormattingUsesTheRestoredContracts.
    [Theory]
    [InlineData("((object)value.Length).ToString()")]
    [InlineData("((System.IFormattable)value.Length).ToString(null, null)")]
    public void NonnegativeLengthFormatting_DoesNotGrantUnsafeNeighboringCalls(string expression)
    {
        var source = $$"""
            module LengthBoundary
              func Read (string:value, int:number, int[]:values, int?:nullable, System.IFormatProvider:provider) → string?
                !
                return {{expression}}
            """;
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    // The restored System.Int32 and System.Convert contracts (0.29) make these neighbors
    // pure whatever the receiver's range. Formatting a negative value or with a caller
    // provider can read ambient culture state; that gap is accepted by the 2026-11-14
    // maintainer decision and tracked separately.
    [Theory]
    [InlineData("number.ToString()")]
    [InlineData("(-1).ToString()")]
    [InlineData("(1).ToString()")]
    [InlineData("values.Length.ToString()")]
    [InlineData("(value.Length + 0).ToString()")]
    [InlineData("value.Length.ToString(\"D\")")]
    [InlineData("value.Length.ToString(provider)")]
    [InlineData("System.Convert.ToString(value.Length)")]
    public void NonnegativeLengthFormatting_NeighboringBuiltInFormattingUsesTheRestoredContracts(string expression)
    {
        var source = $$"""
            module LengthBoundary
              func Read (string:value, int:number, int[]:values, int?:nullable, System.IFormatProvider:provider) → string?
                !
                return {{expression}}
            """;
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
            Assert.NotEmpty(result.GeneratedCode);
        }
    }

    // Int32.ToString is pure again (restored 0.29 contract), so neither a local copy nor a
    // source result needs the nonnegative-length proof; both functions compile as pure.
    [Fact]
    public void NonnegativeLengthFormatting_DoesNotTransferTheRangeThroughLocalsOrSourceResults()
    {
        const string source = """
            module LengthFlowBoundary
              func Length (string:value) → int
                !
                return value.Length
              func Local (string:value) → string
                !
                let int:length = value.Length
                return length.ToString()
              func Result (string:value) → string
                !
                return Length(value).ToString()
            """;
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
            Assert.NotEmpty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("field int:Length public")]
    [InlineData("property int:Length public\n      get\n        return -1")]
    public void NonnegativeLengthFormatting_DoesNotTrustSourceMembersNamedLength(string member)
    {
        var source = $$"""
            module LengthLookalike
              class Text public
                {{member}}
              func Read (Text:value) → string
                !
                return value.Length.ToString()
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var length = Assert.IsType<BoundFieldAccessExpression>(call.Receiver);
        Assert.Null(length.ResolvedMetadataMember);
        // The source member still gets no metadata identity (above). The program now
        // compiles because the restored 0.29 contract makes every Int32.ToString pure,
        // not because the lookalike is trusted as a nonnegative String.Length.
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        }
    }

    // The oblivious row moved to NonnegativeLengthFormatting_ObliviousModuleAcceptsAnObliviousReceiver:
    // a `nullable oblivious` module accepts oblivious values at non-null boundaries (2026-11-14).
    [Theory]
    [InlineData("", "string?")]
    [InlineData(" nullable oblivious", "string?")]
    public void NonnegativeLengthFormatting_DoesNotRelaxTheStringReceiverBoundary(string context, string type)
    {
        var source = $$"""
            module LengthNullability{{context}}
              func Read ({{type}}:value) → string
                !
                return value.Length.ToString()
            """;
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Fact]
    public void NonnegativeLengthFormatting_ObliviousModuleAcceptsAnObliviousReceiver()
    {
        const string source = """
            module LengthNullability nullable oblivious
              func Read (string:value) → string
                !
                return value.Length.ToString()
            """;
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Contains("value.Length.ToString()", result.GeneratedCode);
        }
    }

    [Fact]
    public void NonnegativeLengthFormatting_MissingBindingStaysUnknown()
    {
        const string source = """
            module LengthMissingProof
              func Read (string:value) → string
                !
                unsafe
                  return value.Length.ToString()
            """;
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Fact]
    public void NonnegativeLengthFormatting_DoesNotReinterpretNullableIntegerDispatch()
    {
        const string source = """
            module NullableLengthBoundary
              func Read (int?:value) → string
                !
                return value.ToString()
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var method = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.Equal(SpecialType.System_Nullable_T, method.ContainingType.OriginalDefinition.SpecialType);
        Assert.IsType<BoundVariableExpression>(call.Receiver);
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Fact]
    public void NonnegativeLengthFormatting_DoesNotBypassSourceInitializerAuthority()
    {
        const string source = """
            module LengthInitializerAuthority
              class Holder
                field string:Value public = "abc".Length.ToString()
                ctor ()
              func Make () → Holder
                ! alloc
                return new Holder()
            """;
        foreach (var typed in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typed });
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("int.ToString()", StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }
}
