using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.CodeGen;
using Calor.Compiler.SelfCheck;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

[Collection("ProductionPipelineConsole")]
public sealed class ReceivingAndEffectDocumentationTests
{
    private const string Nullability = "guides/nullability-and-dotnet-interop";
    private const string CrossModule = "guides/cross-module-effect-propagation";
    private const string Effects = "syntax-reference/effects";

    [Theory]
    [InlineData(Nullability, 10, 3, 10)]
    [InlineData(CrossModule, 3, 1, 6)]
    [InlineData(Effects, 4, 0, 19)]
    public void CompleteExamples_PreserveCoverageAndExactNegativeContracts(string page, int programs, int negatives,
        int fences)
    {
        var coverage = new WebsiteExampleChecker.Coverage();
        var findings = WebsiteExampleChecker.Check([new DocFile(PagePath(page), Page(page))], coverage);
        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
        Assert.Equal(programs, coverage.CompletePrograms);
        Assert.Equal(negatives, coverage.NegativePrograms);
        Assert.Equal(fences, Fences(page).Count());
        Assert.Equal(programs, Fences(page).Count(source => ExemplarCompileChecker.StartsWithModule(source.Split('\n'))));
    }

    public static IEnumerable<object[]> NullabilityExamples() =>
        Fences(Nullability).Select(source => new object[]
            {
                Regex.Match(source, @"\Amodule (\w+)").Groups[1].Value, source
            });

    [Theory]
    [MemberData(nameof(NullabilityExamples))]
    public void NullabilityExamples_UseTheActualCliAndFreshOutput(string name, string source)
    {
        var directory = TemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "example.calr"), source);
            var result = CliTestHarness.RunCli(directory, "--input", "example.calr", "--output", "example.g.cs",
                "--format", "json");
            var expected = name switch
            {
                "InitRejected" => "Calor0272",
                "ReturnRejected" => "Calor0273",
                "ArgumentRejected" => "Calor0274",
                _ => null
            };
            Assert.Equal(expected is null ? 0 : 1, result.ExitCode);
            Assert.Equal(expected is null ? [] : new[] { expected }, Codes(result.StdOut));
            Assert.Equal(expected is null, File.Exists(Path.Combine(directory, "example.g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NullabilityDefaultsPatternsNamedArgumentsAndThrow_PreserveRuntimeBehavior()
    {
        foreach (var module in new[] { "ReturnSafe", "PatternSafe" })
        {
            var method = NativeMethod(module, "Name");
            Assert.Equal("anonymous", method.Invoke(null, [null]));
            Assert.Equal("Ada", method.Invoke(null, ["Ada"]));
        }
        Assert.Equal("ready", NativeMethod("NamedSafe", "Send").Invoke(null, [null]));
        foreach (var (module, name) in new[] { ("InitSafe", "Copy"), ("ArgumentSafe", "Send") })
        {
            var method = NativeMethod(module, name);
            Assert.Null(method.Invoke(null, [null]));
            Assert.Null(method.Invoke(null, ["Ada"]));
        }

        var require = NativeMethod("ThrowSafe", "Require");
        Assert.Equal("Ada", require.Invoke(null, ["Ada"]));
        Assert.IsType<ArgumentNullException>(
            Assert.Throws<TargetInvocationException>(() => require.Invoke(null, [null])).InnerException);

        var previous = Environment.GetEnvironmentVariable("CALOR_NAME");
        var environmentName = NativeMethod("EnvironmentSafe", "Name");
        try
        {
            Environment.SetEnvironmentVariable("CALOR_NAME", null);
            Assert.Equal("anonymous", environmentName.Invoke(null, []));
            Environment.SetEnvironmentVariable("CALOR_NAME", "Ada");
            Assert.Equal("Ada", environmentName.Invoke(null, []));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CALOR_NAME", previous);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CrossModuleCli_RejectsOriginalCallerAndEmitsRunnableCorrection(bool qualified, bool reverse)
    {
        var programs = Fences(CrossModule).Where(source => ExemplarCompileChecker.StartsWithModule(source.Split('\n'))).ToArray();
        var callee = Assert.Single(programs.Where(source => source.StartsWith("module OrderService ",
            StringComparison.Ordinal)));
        var original = Assert.Single(programs.Where(source =>
            source.Contains("func HandleRequest", StringComparison.Ordinal)
            && !source.Contains("! db:w", StringComparison.Ordinal)));
        var corrected = qualified
            ? "module Handler\n" + Indent(Assert.Single(Fences(CrossModule).Where(source =>
                source.StartsWith("func HandleRequest", StringComparison.Ordinal)
                && source.Contains("OrderService.SaveOrder()", StringComparison.Ordinal))))
            : Assert.Single(programs.Where(source =>
                source.Contains("using OrderService", StringComparison.Ordinal)));
        var directory = TemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "OrderService.calr"), callee);
            File.WriteAllText(Path.Combine(directory, "Handler.calr"), original);
            string[] arguments = reverse
                ? ["--input", "Handler.calr", "--input", "OrderService.calr", "--format", "json"]
                : ["--input", "OrderService.calr", "--input", "Handler.calr", "--format", "json"];
            AssertOriginalCrossModuleRejection(CliTestHarness.RunCli(directory, arguments));
            if (qualified)
            {
                File.WriteAllText(Path.Combine(directory, "Handler.calr"),
                    original.Replace("SaveOrder()", "OrderService.SaveOrder()", StringComparison.Ordinal));
                AssertOriginalCrossModuleRejection(CliTestHarness.RunCli(directory, arguments));
            }

            File.WriteAllText(Path.Combine(directory, "Handler.calr"), corrected);
            foreach (var warm in new[] { false, true })
            {
                var accepted = CliTestHarness.RunCli(directory, arguments);
                Assert.True(accepted.ExitCode == 0, $"warm={warm}: {accepted.StdOut}\n{accepted.StdErr}");
                Assert.Empty(Codes(accepted.StdOut));
                var assembly = Compile(
                    File.ReadAllText(Path.Combine(directory, "OrderService.g.cs")),
                    File.ReadAllText(Path.Combine(directory, "Handler.g.cs")));
                Assert.Equal("OrderService", Method(assembly, "SaveOrder").DeclaringType!.Namespace);
                Assert.Null(Method(assembly, "HandleRequest").Invoke(null, []));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OriginalPromptFragment_RetainsItsNonNullReturnBoundaryFailure()
    {
        var fragment = Assert.Single(Fences(Effects).Where(source =>
            source.StartsWith("func Prompt ", StringComparison.Ordinal)));
        var result = Program.Compile("module PromptWitness\n" + Indent(fragment), "Prompt.calr",
            new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Equal("Calor0273", Assert.Single(result.Diagnostics.Errors).Code);
        Assert.True(string.IsNullOrEmpty(result.GeneratedCode));
    }

    [Fact]
    public void SeparateFieldRow_RetainsCalor0405RatherThanAParserError()
    {
        var fragment = Assert.Single(Fences(Effects).Where(source =>
            source.StartsWith("field int:count", StringComparison.Ordinal)));
        var result = Program.Compile("module BadRow\n  class Counter\n" + Indent(Indent(fragment)), "BadRow.calr",
            new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Equal("Calor0405", Assert.Single(result.Diagnostics.Errors).Code);
    }

    [Fact]
    public void CompleteEffectRowExamples_PreserveCallbackBehavior()
    {
        Func<int, int> increment = value => value + 1;
        Assert.Equal(4, NativeMethod("Callbacks", "Apply", Effects).Invoke(null, [increment, 3]));
        Assert.Equal(5, NativeMethod("HigherOrder", "Twice", Effects).Invoke(null, [increment, 3]));
        Assert.Same(increment, NativeMethod("Rows", "Use", Effects).Invoke(null, [increment, 3]));
    }

    private static string PagePath(string page) => $"website/content/{page}.mdx";
    private static string Page(string page) => File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), PagePath(page)));
    private static IEnumerable<string> Fences(string page) =>
        Regex.Matches(Page(page), @"```calor[^\r\n]*\r?\n(?<code>[\s\S]*?)\r?\n```")
            .Select(match => match.Groups["code"].Value.Replace("\r\n", "\n", StringComparison.Ordinal));
    private static string Indent(string source) =>
        string.Join("\n", source.Split('\n').Select(line => "  " + line)) + "\n";
    private static string[] Codes(string json) =>
        JsonNode.Parse(json)!["diagnostics"]!.AsArray()
            .Select(diagnostic => diagnostic!["code"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();

    private static void AssertOriginalCrossModuleRejection((int ExitCode, string StdOut, string StdErr) result)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(new[] { "Calor0410", "Calor0417" }, Codes(result.StdOut));
        var diagnostics = JsonNode.Parse(result.StdOut)!["diagnostics"]!.AsArray();
        Assert.Equal("error", Assert.Single(diagnostics.Where(diagnostic =>
            diagnostic!["code"]!.GetValue<string>() == "Calor0410"))!["severity"]!.GetValue<string>());
        Assert.Equal("warning", Assert.Single(diagnostics.Where(diagnostic =>
            diagnostic!["code"]!.GetValue<string>() == "Calor0417"))!["severity"]!.GetValue<string>());
        Assert.Contains("via cross-module call", result.StdOut);
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "calor-effect-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static MethodInfo NativeMethod(string module, string method, string page = Nullability)
    {
        var source = Assert.Single(ExemplarCompileChecker.ExtractCompletePrograms(Page(page))
            .Where(program => program.Source.StartsWith($"module {module}\n", StringComparison.Ordinal)
                || program.Source.StartsWith($"module {module}\r\n", StringComparison.Ordinal))).Source;
        var result = Program.Compile(source, module + ".calr",
            new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        return Method(Compile(result.GeneratedCode), method);
    }

    private static MethodInfo Method(Assembly assembly, string name) =>
        Assert.Single(assembly.GetTypes().SelectMany(type =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.Name == name));

    private static Assembly Compile(params string[] sources)
    {
        var compilation = CSharpCompilation.Create("EffectDocWitness_" + Guid.NewGuid().ToString("N"),
            sources.Select(source => CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + source)),
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(output.ToArray());
    }
}
