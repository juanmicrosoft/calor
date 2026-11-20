using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Calor.Compiler.CodeGen;
using Calor.Compiler.SelfCheck;
using Calor.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

[Collection("ProductionPipelineConsole")]
public sealed class BenchmarkWebsiteExamplesTests
{
    [Theory]
    [InlineData("comprehension", 1)]
    [InlineData("correctness", 1)]
    [InlineData("information-density", 2)]
    public void CurrentCompleteExamples_AreAllChecked(string page, int expectedPrograms)
    {
        var coverage = new WebsiteExampleChecker.Coverage();
        var findings = WebsiteExampleChecker.Check([new DocFile(PagePath(page), Page(page))], coverage);
        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
        Assert.Equal(expectedPrograms, coverage.CompletePrograms);
        Assert.Equal(expectedPrograms, ExemplarCompileChecker.ExtractCompletePrograms(Page(page)).Count);
    }

    [Fact]
    public void Divide_PreservesQuotientsAndBothOriginalContracts()
    {
        var calor = NativeMethod("comprehension", "Divide");
        var csharp = Method(Compile(CSharpFences("comprehension").Single()), "Divide");
        foreach (var (a, b, expected) in new[]
        {
            (12, 3, 4), (0, 5, 0), (-12, -3, 4), (7, 2, 3),
            (int.MaxValue, 1, int.MaxValue), (int.MinValue, -2, 1073741824)
        })
        {
            Assert.Equal(expected, calor.Invoke(null, [a, b]));
            Assert.Equal(expected, csharp.Invoke(null, [a, b]));
        }

        // #1618: b != 0 is the refinement on b; result >= 0 stays a postcondition.
        Assert.IsType<ArgumentOutOfRangeException>(
            Assert.Throws<TargetInvocationException>(() => calor.Invoke(null, [1, 0])).InnerException);
        Assert.IsType<ArgumentException>(
            Assert.Throws<TargetInvocationException>(() => csharp.Invoke(null, [1, 0])).InnerException);
        Assert.IsType<ContractViolationException>(
            Assert.Throws<TargetInvocationException>(() => calor.Invoke(null, [-12, 3])).InnerException);
    }

    [Fact]
    public void FindMax_PreservesArraysAndDistinctEdgeFailures()
    {
        var calor = NativeMethod("correctness", "FindMax");
        var csharp = Method(Compile("public static class Witness {\n"
            + CSharpFences("correctness").Single() + "\n}"), "FindMax");
        foreach (var values in new int[][]
        {
            [1, 5, 3, 9, 2], [42], [-5, -1, -10], [0, 0],
            [int.MinValue], [int.MinValue, 0, int.MaxValue]
        })
        {
            var unchanged = values.ToArray();
            Assert.Equal(values.Max(), calor.Invoke(null, [values]));
            Assert.Equal(values.Max(), csharp.Invoke(null, [values]));
            Assert.Equal(unchanged, values);
        }

        // #1618: the non-empty requirement is the refinement on arr.
        Assert.IsType<ArgumentOutOfRangeException>(
            Assert.Throws<TargetInvocationException>(() => calor.Invoke(null, [Array.Empty<int>()])).InnerException);
        Assert.IsType<ArgumentException>(
            Assert.Throws<TargetInvocationException>(() => csharp.Invoke(null, [Array.Empty<int>()])).InnerException);
        Assert.IsType<NullReferenceException>(
            Assert.Throws<TargetInvocationException>(() => calor.Invoke(null, [null])).InnerException);
        Assert.IsType<ArgumentNullException>(
            Assert.Throws<TargetInvocationException>(() => csharp.Invoke(null, [null])).InnerException);
    }

    [Fact]
    public void FizzBuzz_PreservesTheExactInclusiveOneThroughHundredTrace()
    {
        var calor = NativeMethod("information-density", "Main");
        var csharp = Method(Compile("public static class Witness { public static void Main() {\n"
            + CSharpFences("information-density")[1] + "\n} }"), "Main");
        var expected = Enumerable.Range(1, 100).Select(value =>
            value % 15 == 0 ? "FizzBuzz" : value % 3 == 0 ? "Fizz" :
            value % 5 == 0 ? "Buzz" : value.ToString(CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(expected, Capture(calor));
        Assert.Equal(expected, Capture(csharp));
    }

    [Fact]
    public void Add_PreservesThePublishedSignatureAndInRangeArithmetic()
    {
        var calor = NativeMethod("information-density", "Add");
        var csharp = Method(Compile("public static class Witness {\n"
            + CSharpFences("information-density")[0] + "\n}"), "Add");
        foreach (var method in new[] { calor, csharp })
        {
            Assert.Equal(typeof(int), method.ReturnType);
            Assert.Equal([typeof(int), typeof(int)], method.GetParameters().Select(parameter => parameter.ParameterType));
            Assert.Equal(3, method.Invoke(null, [1, 2]));
            Assert.Equal(-3, method.Invoke(null, [-1, -2]));
            Assert.Equal(int.MinValue, method.Invoke(null, [int.MinValue, 0]));
        }
    }

    private static string PagePath(string page) => $"website/content/benchmarking/metrics/{page}.mdx";

    private static string Page(string page) =>
        File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), PagePath(page)));

    private static string[] CSharpFences(string page) =>
        Regex.Matches(Page(page), @"```csharp\r?\n(?<code>[\s\S]*?)\r?\n```")
            .Select(match => match.Groups["code"].Value).ToArray();

    private static MethodInfo NativeMethod(string page, string method)
    {
        var source = Assert.Single(ExemplarCompileChecker.ExtractCompletePrograms(Page(page))
            .Where(program => program.Source.Contains($"func {method} ", StringComparison.Ordinal))).Source;
        var result = Program.Compile(source, PagePath(page), new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        return Method(Compile(result.GeneratedCode), method);
    }

    private static MethodInfo Method(Assembly assembly, string name) =>
        Assert.Single(assembly.GetTypes().SelectMany(type =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.Name == name));

    private static Assembly Compile(string source)
    {
        var compilation = CSharpCompilation.Create("WebsiteWitness_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble
                + "using System.Diagnostics;\n" + source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(output.ToArray());
    }

    private static string[] Capture(MethodInfo method)
    {
        var previous = Console.Out;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(output);
            method.Invoke(null, []);
        }
        finally
        {
            Console.SetOut(previous);
        }
        return output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
    }
}
