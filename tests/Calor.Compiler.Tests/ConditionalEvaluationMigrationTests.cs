using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class ConditionalEvaluationMigrationTests
{
    [Fact]
    public void FalseAndPostfix_PreservesOriginalResult()
    {
        AssertRoundTrip(
            "int i = 0; bool gate = false; bool ignored = gate && i++ > 0; return i;",
            0, expectInterop: true);
    }

    [Theory]
    [InlineData("true && i++ > 0", 1)]
    [InlineData("false && i++ > 0", 0)]
    [InlineData("true || i++ > 0", 0)]
    [InlineData("false || i++ > 0", 1)]
    [InlineData("false && (i++ > 0 || ++i > 0)", 0)]
    [InlineData("true && (i++ > 0 || ++i > 0)", 2)]
    public void LogicalOperands_PreserveEvaluationCount(string expression, int expected)
    {
        AssertRoundTrip($"int i = 0; bool ignored = {expression}; return i;", expected, true);
    }

    [Theory]
    [InlineData("7", 0)]
    [InlineData("null", 1)]
    public void Coalescing_PreservesConditionalIncrement(string value, int expected)
    {
        AssertRoundTrip($"int i = 0; int? value = {value}; int ignored = value ?? i++; return i;",
            expected, true);
    }

    [Theory]
    [InlineData("true", 12)]
    [InlineData("false", 22)]
    public void ConditionalArms_PreserveOrderAndResult(string gate, int expected)
    {
        AssertRoundTrip($"int i = 1; bool gate = {gate}; int value = gate ? i++ : ++i; return value * 10 + i;",
            expected, true);
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("new int[] { 42 }", 1)]
    public void ConditionalIndex_PreservesSkippedIndex(string target, int expected)
    {
        AssertRoundTrip($"int i = 0; int[] target = {target}; int? ignored = target?[i++]; return i;",
            expected, true);
    }

    [Theory]
    [InlineData("false && 1 / i++ > 0", 0)]
    [InlineData("true && 1 / i++ > 0", 101)]
    [InlineData("true || 1 / i++ > 0", 0)]
    [InlineData("false || 1 / i++ > 0", 101)]
    public void LogicalOperands_PreserveExceptions(string expression, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; try {{ bool ignored = {expression}; }} catch (System.DivideByZeroException) {{ return 100 + i; }} return i;",
            expected, true);
    }

    [Theory]
    [InlineData("7", 0)]
    [InlineData("null", 101)]
    public void Coalescing_PreservesExceptions(string value, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; int? value = {value}; try {{ int ignored = value ?? 1 / i++; }} catch (System.DivideByZeroException) {{ return 100 + i; }} return i;",
            expected, true);
    }

    [Theory]
    [InlineData("true", 101)]
    [InlineData("false", 0)]
    public void ConditionalArms_PreserveExceptions(string gate, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; bool gate = {gate}; try {{ int ignored = gate ? 1 / i++ : 42; }} catch (System.DivideByZeroException) {{ return 100 + i; }} return i;",
            expected, true);
    }

    [Theory]
    [InlineData("null", 2)]
    [InlineData("new int[] { 42 }", 103)]
    public void ConditionalIndex_PreservesExceptions(string target, int expected)
    {
        AssertRoundTrip(
            $"int i = 2; int[] target = {target}; try {{ int? ignored = target?[i++]; }} catch (System.IndexOutOfRangeException) {{ return 100 + i; }} return i;",
            expected, true);
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("\"abc\"", 1)]
    public void ConditionalCall_PreservesSkippedArguments(string target, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; string target = {target}; string ignored = target?.Substring(i++); return i;",
            expected, true);
    }

    [Theory]
    [InlineData("null", 4)]
    [InlineData("\"abc\"", 105)]
    public void ConditionalCall_PreservesExceptions(string target, int expected)
    {
        AssertRoundTrip(
            $"int i = 4; string target = {target}; try {{ string ignored = target?.Substring(i++); }} catch (System.ArgumentOutOfRangeException) {{ return 100 + i; }} return i;",
            expected, true);
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("\"abc\"", 1)]
    public void ConditionalCallStatement_PreservesSkippedArguments(string target, int expected)
    {
        AssertRoundTrip($"int i = 0; string target = {target}; target?.Substring(i++); return i;",
            expected, true);
    }

    [Fact]
    public void ConditionalCallStatement_EvaluatesPropertyReceiverOnce()
    {
        AssertRoundTrip("Target?.ToString(); return Calls;", 1, true,
            members: "private static int Calls; public static string Target { get { Calls++; return \"abc\"; } }");
    }

    [Theory]
    [InlineData("\"yes\"", 0)]
    [InlineData("null", 101)]
    public void CoalescingThrow_PreservesExceptionConstruction(string value, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; string value = {value}; try {{ string ignored = value ?? throw new System.Exception((i++).ToString()); }} catch (System.Exception) {{ return 100 + i; }} return i;",
            expected, true);
    }

    [Theory]
    [InlineData("true", 0)]
    [InlineData("false", 101)]
    public void ConditionalThrow_PreservesExceptionConstruction(string gate, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; bool gate = {gate}; try {{ int ignored = gate ? 42 : throw new System.Exception((i++).ToString()); }} catch (System.Exception) {{ return 100 + i; }} return i;",
            expected, true);
    }

    [Theory]
    [InlineData(ConversionMode.Standard)]
    [InlineData(ConversionMode.Interop)]
    public void HoistingPreservesWholeMemberInEveryMode(ConversionMode mode)
    {
        AssertRoundTrip(
            "int i = 0; bool ignored = false && i++ > 0; return ignored ? 9 : i;",
            0, true, mode);
    }

    [Fact]
    public void FailedMember_DoesNotLeakEarlierOperandPreludes()
    {
        AssertRoundTrip("return Other();", 1, true,
            members: "public static int Other() { int i = 0; bool gate = false; return i++ + (gate ? i++ : i++); }");
    }

    [Theory]
    [InlineData("int i = 0; int saved = i++; return saved * 10 + i;", 1)]
    [InlineData("int i = 1; bool gate = true; bool ignored = gate && i > 0; return i;", 1)]
    [InlineData("int i = 0; bool gate = true; int value = i++ + (gate ? 7 : 9); return value * 10 + i;", 71)]
    public void AlreadyFaithfulExpressions_RemainNative(string body, int expected)
    {
        AssertRoundTrip(body, expected, expectInterop: false);
    }

    private static void AssertRoundTrip(
        string body, int expected, bool expectInterop,
        ConversionMode mode = ConversionMode.Standard, string members = "")
    {
        var original = $$"""
            public static class Probe
            {
                {{members}}
                public static int Run()
                {
                    {{body}}
                }
            }
            """;
        Assert.Equal(expected, Execute(original));
        var conversion = new CSharpToCalorConverter(new ConversionOptions { Mode = mode }).Convert(original);
        Assert.True(conversion.Success, string.Join(Environment.NewLine, conversion.Issues));
        Assert.NotNull(conversion.CalorSource);
        // Migration does not synthesize effect declarations; keep the production
        // type checker and generated-C# backstop, but do not require effect rows.
        var compilation = Program.Compile(conversion.CalorSource, "conditional-migration.calr",
            new Calor.Compiler.CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        Assert.Equal(expected, Execute(compilation.GeneratedCode));
        Assert.Equal(expectInterop,
            conversion.Losses.Any(loss => loss.Kind == ConversionLossKind.InteropPreserved));
        if (expectInterop)
        {
            Assert.Contains(conversion.Issues, issue =>
                issue.Feature is "conditional-expression-hoisting" or "conditional-access-shape");
        }
    }

    private static int Execute(string source)
    {
        var compilation = CSharpCompilation.Create("ConditionalMigration_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var run = assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Single(method => method.Name == "Run");
            return Assert.IsType<int>(run.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }
}
