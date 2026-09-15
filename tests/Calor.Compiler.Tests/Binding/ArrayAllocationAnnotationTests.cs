using Assembly = System.Reflection.Assembly;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using OutputKind = Microsoft.CodeAnalysis.OutputKind;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class ArrayAllocationAnnotationTests
{
    [Fact]
    public void ProductionFizzBuzzRange_ConvertsCompilesAndRuns()
    {
        var path = Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests", "Calor.RoundTrip.Synthetic", "SyntheticLib", "FizzBuzz.cs");
        var conversion = new CSharpToCalorConverter().Convert(File.ReadAllText(path), "FizzBuzz.cs");
        Assert.NotNull(conversion.CalorSource);
        Assert.Contains("§ARR{", conversion.CalorSource);
        Assert.Contains("§PUT{", conversion.CalorSource);
        var result = Program.Compile(conversion.CalorSource, "FizzBuzz.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.True(conversion.Success);
        var type = Emit(result.GeneratedCode).GetTypes().Single(type => type.Name == "FizzBuzz");
        var values = Assert.IsType<string[]>(type.GetMethod("Range")!.Invoke(null, [1, 15]));
        Assert.Equal(15, values.Length);
        Assert.Equal("1", values[0]);
        Assert.Equal("Fizz", values[2]);
        Assert.Equal("Buzz", values[4]);
        Assert.Equal("FizzBuzz", values[14]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConvertedSizedArray_PreservesDeclaredElementAnnotation(bool nullableElements, bool rectangular)
    {
        var suffix = rectangular ? "[,]" : "[]";
        var dimensions = rectangular ? "count, 2" : "count";
        var source = $$"""
            #nullable enable
            public static class Allocator {
                public static string{{(nullableElements ? "?" : "")}}{{suffix}} Allocate(int count) {
                    return new string{{(nullableElements ? "?" : "")}}[{{dimensions}}];
                }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source, "Allocator.cs");
        Assert.NotNull(conversion.CalorSource);
        var result = Program.Compile(conversion.CalorSource, "Allocator.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var type = Emit(result.GeneratedCode).GetTypes().Single(type => type.Name == "Allocator");
        var array = Assert.IsAssignableFrom<Array>(type.GetMethod("Allocate")!.Invoke(null, [3]));
        Assert.Equal(rectangular ? 2 : 1, array.Rank);
        Assert.Equal(3, array.GetLength(0));
        Assert.Null(rectangular ? array.GetValue(0, 0) : array.GetValue(0));
    }

    [Fact]
    public void ConvertedNominalArrayWithConstructors_CompilesAndRuns()
    {
        var conversion = new CSharpToCalorConverter().Convert("""
            #nullable enable
            public class Box {
                public int Value;
                public Box(int value) { Value = value; }
                public static Box[] Allocate(int count) {
                    var results = new Box[count];
                    for (int i = 0; i < count; i++) results[i] = new Box(i);
                    return results;
                }
            }
            """, "Box.cs");
        Assert.NotNull(conversion.CalorSource);
        var result = Program.Compile(conversion.CalorSource, "Box.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var type = Emit(result.GeneratedCode).GetTypes().Single(type => type.Name == "Box");
        var values = Assert.IsAssignableFrom<Array>(type.GetMethod("Allocate")!.Invoke(null, [3]));
        Assert.Equal(2, type.GetField("Value")!.GetValue(values.GetValue(2)));
    }

    public static IEnumerable<object[]> AllocationCases()
    {
        foreach (var nullableElements in new[] { false, true })
        foreach (var inferred in new[] { false, true })
        foreach (var size in new[] { "0", "3", "count" })
        foreach (var boundary in new[] { "binding", "return", "input" })
            yield return [nullableElements, inferred, size, boundary];
    }

    [Theory]
    [MemberData(nameof(AllocationCases))]
    public void NativeAllocation_PreservesStaticShape(
        bool nullableElements, bool inferred, string size, string boundary)
    {
        var element = nullableElements ? "?str" : "str";
        var binding = inferred ? "values" : $"values:[{element}]";
        var sink = boundary switch
        {
            "binding" => "§B{required:[str]} values",
            "return" => "§R values",
            "input" => "§C{Take} §A values §/C",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        var source = $$"""
            §M{m:AllocatedArray}
              §F{take:Take:pub} ([str]:value) -> void
                §E{}
              §F{probe:Probe:pub} (i32:count) -> {{(boundary == "return" ? "[str]" : "void")}}
                §E{alloc}
                §B{ {{binding}} } §ARR{ {{element}} :values:{{size}} }
                {{sink}}
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Binder(diagnostics).Bind(new Parser(tokens, diagnostics).Parse());
        var function = module.Functions.Single(function => function.Symbol.Name == "Probe");
        var bound = Assert.IsType<BoundBindStatement>(function.Body[0]);
        var creation = Assert.IsType<BoundArrayCreation>(bound.Initializer);
        var created = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(creation));
        Assert.Equal(NullableAnnotation.NotAnnotated, NullabilityChecker.GetAnnotation(creation.Type));
        Assert.Equal(NullableAnnotation.NotAnnotated, created.NullableAnnotation);
        Assert.Equal(nullableElements ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(created.ElementType).NullableAnnotation);
        Assert.Equal(created, bound.Variable.MethodInputArrayType);
        var expected = boundary switch
        {
            "binding" => DiagnosticCode.NullableToNonNullableBinding,
            "return" => DiagnosticCode.NullableReturnFromNonNullable,
            _ => DiagnosticCode.NullableArgumentToNonNullableParameter
        };
        Assert.DoesNotContain(diagnostics.Errors, diagnostic => diagnostic.Code != expected);
        Assert.Equal(nullableElements, diagnostics.Any(diagnostic =>
            diagnostic.Code == expected && BindingDiagnosticPolicy.IsCompilationError(diagnostic)));
        foreach (var enableTypeChecking in new[] { true, false })
        {
            var result = Program.Compile(source, "allocated-array.calr",
                new CompilationOptions { EnableTypeChecking = enableTypeChecking });
            Assert.True(nullableElements == result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(nullableElements, string.IsNullOrEmpty(result.GeneratedCode));
            Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic => diagnostic.Code != expected);
            Assert.Equal(nullableElements, result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == expected && BindingDiagnosticPolicy.IsCompilationError(diagnostic)));
        }
    }

    [Theory]
    [InlineData("?[str]", "binding")]
    [InlineData("?[str]", "return")]
    [InlineData("?[str]", "input")]
    [InlineData("[?str]", "binding")]
    [InlineData("[?str]", "return")]
    [InlineData("[?str]", "input")]
    public void NullableParameterArray_RemainsRejected(string type, string boundary)
        => AssertNullableArrayRejected(type, boundary, initializedLocal: false);

    [Theory]
    [InlineData("?[str]", "binding")]
    [InlineData("?[str]", "return")]
    [InlineData("?[str]", "input")]
    [InlineData("[?str]", "binding")]
    [InlineData("[?str]", "return")]
    [InlineData("[?str]", "input")]
    public void ExplicitNullableLocalAnnotation_RemainsRejected(string type, string boundary)
        => AssertNullableArrayRejected(type, boundary, initializedLocal: true);

    private static void AssertNullableArrayRejected(string type, string boundary, bool initializedLocal)
    {
        var sink = boundary switch
        {
            "binding" => "§B{required:[str]} values",
            "return" => "§R values",
            _ => "§C{Take} §A values §/C"
        };
        var source = $$"""
            §M{m:NullableArray}
              §F{take:Take:pub} ([str]:value) -> void
                §E{}
              §F{probe:Probe:pub} ({{(initializedLocal ? "" : $"{type}:values")}}) -> {{(boundary == "return" ? "[str]" : "void")}}
                §E{alloc}
            {{(initializedLocal ? $"    §B{{values:{type}}} §ARR{{str:values:3}}" : "")}}
                {{sink}}
            """;
        var result = Program.Compile(source, "nullable-array.calr");
        var expected = boundary switch
        {
            "binding" => DiagnosticCode.NullableToNonNullableBinding,
            "return" => DiagnosticCode.NullableReturnFromNonNullable,
            _ => DiagnosticCode.NullableArgumentToNonNullableParameter
        };
        Assert.True(result.HasErrors);
        Assert.True(string.IsNullOrEmpty(result.GeneratedCode));
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == expected
            && BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    private static Assembly Emit(string source)
    {
        var compilation = CSharpCompilation.Create("Allocated_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
