using Calor.Compiler.Diagnostics;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

[Collection("ProductionPipelineConsole")]
public class MarkdownReferenceTests
{
    [Theory]
    [InlineData("binding", 10, 8,
        new[] { "GlobalModule.Main()", "GlobalModule.Main()" }, new[] { "150", "16" })]
    [InlineData("calls", 3, 1,
        new[] { "GlobalModule.Main()", "GlobalModule.Main()" }, new[] { "9", "42" })]
    [InlineData("contracts", 8, 0,
        new[] { "GlobalModule.Divide(9, 2)", "GlobalModule.Abs(-5)", "GlobalModule.Main()",
            "GlobalModule.First(new int[] { 42 })", "GlobalModule.Identity(1)",
            "GlobalModule.CheckSymmetric(new int[,] { { 1, 2 }, { 2, 3 } }, 2)",
            "GlobalModule.ProcessArray(new int[] { 0, 3 }, 2)", "GlobalModule.Find(new int[] { 4, 9 }, 9)" },
        new[] { "4", "5", "15", "42", "1", "True", "True", "1" })]
    [InlineData("control-flow", 11, 0,
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()",
            "GlobalModule.Main()",
            "GlobalModule.Classify(15) + \":\" + GlobalModule.Classify(3) + \":\" + GlobalModule.Classify(5) + \":\" + GlobalModule.Classify(7)",
            "GlobalModule.CountEvens(6)", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Word(2)",
            "GlobalModule.Main()" },
        new[] { "33", "4", "31", "55", "5", "FizzBuzz:Fizz:Buzz:7", "3", "120",
            "Not Found:A:large positive", "two", "3" })]
    [InlineData("effects", 4, 0,
        new[] { "GlobalModule.Apply(x => x + 1, 41)", "GlobalModule.Use(x => x + 1, 41)(1)",
            "GlobalModule.Twice(x => x + 1, 40)", "GlobalModule.Encode(\"a b&c\")" },
        new[] { "42", "2", "42", "a+b%26c" })]
    [InlineData("expressions", 6, 0,
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()",
            "GlobalModule.Main()", "GlobalModule.Main()" },
        new[] { "14:20:5:4:8:2:5:5:True:11:2", "42:-2:-1", "True:False:True:False:False:True",
            "False:True:True:True:5:7:8:100:6:3:4:5:8:9:10", "3", "True:True:True:1:5" })]
    [InlineData("index", 3, 0,
        new[] { "Demo.DemoModule.Main()", "GlobalModule.Add(20, 22)",
            "GlobalModule.Classify(15) + \":\" + GlobalModule.Classify(3) + \":\" + GlobalModule.Classify(5) + \":\" + GlobalModule.Classify(7)" },
        new[] { "42", "42", "FizzBuzz:Fizz:Buzz:7" })]
    [InlineData("refinement-types", 6, 0,
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()",
            "GlobalModule.Transfer(10, 3)", "GlobalModule.At(new int[] { 4, 9 }, 1)" },
        new[] { "443", "5", "7", "-1", "7", "9" })]
    [InlineData("string-operations", 5, 1,
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()" },
        new[] { "11:HELLO WORLD:ell:hello Calor", "True:True:6:True", "True:True:Cat",
            "coffee  |      3.50|7.0:${x}" })]
    [InlineData("structure-tags", 22, 1,
        new[] { "Demo.DemoModule.Main()", "new Demo.Tools.Box().Read()", "GlobalModule.Main()",
            "GlobalModule.Read().GetAwaiter().GetResult() + \":\" + GlobalModule.ExplicitTask().GetAwaiter().GetResult() + \":\" + GlobalModule.ExplicitValueTask().GetAwaiter().GetResult()",
            "GlobalModule.Main().GetAwaiter().GetResult()", "GlobalModule.Main().GetAwaiter().GetResult()",
            "GlobalModule.Read().GetAwaiter().GetResult()", "GlobalModule.Main()",
            "GlobalModule.Main().GetAwaiter().GetResult()", "GlobalModule.Use(2)", "GlobalModule.Main()",
            "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Receiver()", "GlobalModule.Main()",
            "new SubscriptionCounter().Count", "GlobalModule.Main()",
            "new DataProcessor().ProcessData(new string[] { \"a\", \"b\" }) + \":\" + GlobalModule.First(new int[][] { new int[] { 7 } })",
            "GlobalModule.Main()", "GlobalModule.Main()", "new Data().Read()" },
        new[] { "42", "42", "10", "21:7:9", "42", "42", "42", "24", "42", "3", "5", "True",
            "#FF0000:True", "#FF0000", "7", "0", "7", "2:7", "15", "42", "7" })]
    [InlineData("types", 6, 2,
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()" },
        new[] { "6", "anonymous", "42", "51" })]
    public void Pages_CompileExactExamplesAndExecuteEveryPositiveProgram(
        string page, int completeCount, int negativeCount, string[] expressions, string[] expected)
    {
        var doc = ReadPage(page);
        Assert.DoesNotContain("§", doc.Content);
        Assert.DoesNotContain("drift:ignore", doc.Content);
        Assert.DoesNotContain("<SupportLevel", doc.Content);
        Assert.Contains("layout: default", doc.Content);
        var coverage = new WebsiteExampleChecker.Coverage();
        var diagnostics = WebsiteExampleChecker.Check([doc], coverage);
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
        Assert.Equal(completeCount, coverage.CompletePrograms);
        Assert.Equal(negativeCount, coverage.NegativePrograms);
        var programs = ExemplarCompileChecker.ExtractCompletePrograms(doc.Content);
        Assert.Equal(completeCount - negativeCount, programs.Count);
        Assert.Equal(programs.Count, expressions.Length);
        Assert.Equal(programs.Count, expected.Length);
        for (var index = 0; index < programs.Count; index++)
        {
            var compiled = Program.Compile(programs[index].Source, doc.Path);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(expected[index], Run(compiled.GeneratedCode, expressions[index]));
        }
    }

    [Fact]
    public void OwnedPages_HaveNoDriftUnderGlobalProductionChecks()
    {
        var errors = new List<Diagnostic>();
        var inputs = DocDriftChecker.LoadFromRepository(CliTestHarness.FindRepoRoot(), errors);
        Assert.Empty(errors);
        var pages = new HashSet<string>(StringComparer.Ordinal)
        {
            "binding", "calls", "contracts", "control-flow", "effects", "expressions",
            "index", "refinement-types", "string-operations", "structure-tags", "types"
        };
        var diagnostics = DocDriftChecker.Check(inputs).Where(diagnostic =>
        {
            var path = diagnostic.FilePath?.Replace('\\', '/');
            return path?.StartsWith("docs/syntax-reference/", StringComparison.Ordinal) == true
                && pages.Contains(Path.GetFileNameWithoutExtension(path));
        });
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("contracts", "ContractDivide", "GlobalModule.Divide(9, 0)",
        "!ContractViolationException", "!DivideByZeroException")]
    [InlineData("contracts", "ContractDivide", "GlobalModule.Divide(-1, 1)",
        "!ContractViolationException", "-1")]
    [InlineData("contracts", "ContractClamp", "GlobalModule.Clamp(4, 10, 0)",
        "!ContractViolationException", "10")]
    [InlineData("contracts", "NonEmptyCollection", "GlobalModule.First(System.Array.Empty<int>())",
        "!ContractViolationException", "!IndexOutOfRangeException")]
    [InlineData("contracts", "AbsoluteValue", "GlobalModule.Abs(int.MinValue)",
        "!OverflowException", "!OverflowException")]
    [InlineData("contracts", "FunctionCallBoundary", "GlobalModule.Identity(0)",
        "!ContractViolationException", "0")]
    [InlineData("contracts", "NonNegativeArray", "GlobalModule.ProcessArray(new int[] { 0, -1 }, 2)",
        "!ContractViolationException", "True")]
    [InlineData("contracts", "NonNegativeArray", "GlobalModule.ProcessArray(System.Array.Empty<int>(), 0)",
        "True", "True")]
    [InlineData("contracts", "ArraySearch", "GlobalModule.Find(new int[] { 4, 9 }, 3)",
        "!ContractViolationException", "-1")]
    [InlineData("contracts", "ArraySearch", "GlobalModule.Find(System.Array.Empty<int>(), 3)",
        "!ContractViolationException", "-1")]
    [InlineData("refinement-types", "PortConstraint", "GlobalModule.SelectPort(0)",
        "!ContractViolationException", "0")]
    [InlineData("refinement-types", "PortConstraint", "GlobalModule.SelectPort(65536)",
        "!ContractViolationException", "65536")]
    [InlineData("refinement-types", "TextConstraint", "GlobalModule.Length(\"\")",
        "!ContractViolationException", "0")]
    [InlineData("refinement-types", "TextConstraint", "GlobalModule.Length(null!)",
        "!ContractViolationException", "!NullReferenceException")]
    [InlineData("refinement-types", "TransferConstraint", "GlobalModule.Transfer(3, 5)",
        "!ContractViolationException", "-2")]
    [InlineData("refinement-types", "ConstraintBoundary", "GlobalModule.Reset(5)", "-1", "-1")]
    [InlineData("refinement-types", "TransferProof", "GlobalModule.Transfer(3, 5)",
        "!ContractViolationException", "!InvalidOperationException")]
    [InlineData("refinement-types", "ArrayIndexConstraint", "GlobalModule.At(new int[] { 4, 9 }, 2)",
        "!ContractViolationException", "!IndexOutOfRangeException")]
    public void Contracts_DistinguishEnabledGuardsFromBodyBehavior(
        string page, string module, string expression, string enabled, string disabled)
    {
        var doc = ReadPage(page);
        var source = ExemplarCompileChecker.ExtractCompletePrograms(doc.Content)
            .Single(program => program.Source.StartsWith("module " + module + "\n", StringComparison.Ordinal)).Source;
        foreach (var mode in new[] { ContractMode.Debug, ContractMode.Off })
        {
            var compiled = Program.Compile(source, doc.Path, new CompilationOptions { ContractMode = mode });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(mode == ContractMode.Debug ? enabled : disabled, Run(compiled.GeneratedCode, expression));
        }
    }

    [Theory]
    [InlineData("StructureModules", """
        typeof(Demo.Arithmetic).IsAbstract && typeof(Demo.Arithmetic).IsSealed
        && typeof(Demo.Arithmetic).GetMethod("Twice")!.IsStatic
        && typeof(Demo.DemoModule).Assembly.GetType("Demo.StructureModules") == null
        """)]
    [InlineData("BlockNamespace", """
        typeof(Demo.Tools.Box).GetField("Builder")!.FieldType == typeof(System.Text.StringBuilder)
        """)]
    [InlineData("AsyncFunctions", """
        typeof(GlobalModule).GetMethod("Read")!.ReturnType == typeof(System.Threading.Tasks.Task<int>)
        && typeof(GlobalModule).GetMethod("ExplicitTask")!.ReturnType == typeof(System.Threading.Tasks.Task<int>)
        && typeof(GlobalModule).GetMethod("ExplicitValueTask")!.ReturnType == typeof(System.Threading.Tasks.ValueTask<int>)
        && typeof(GlobalModule).GetMethod("Wait")!.ReturnType == typeof(System.Threading.Tasks.Task)
        """)]
    [InlineData("DelegateDefinitions", """
        typeof(Calculator).GetMethod("Invoke")!.ReturnType == typeof(int)
        && typeof(Calculator).GetMethod("Invoke")!.GetParameters().Length == 2
        && typeof(Logger).GetMethod("Invoke")!.GetParameters()[0].ParameterType == typeof(string)
        """)]
    [InlineData("EnumDefinitions", """
        System.Enum.GetUnderlyingType(typeof(Color)) == typeof(int)
        && (int)Color.Red == 0 && (int)StatusCode.NotFound == 404
        && System.Enum.GetUnderlyingType(typeof(Access)) == typeof(byte)
        """)]
    [InlineData("EnumExtensions", """
        typeof(ColorExtensions).GetMethod("ToHex")!.IsStatic
        && typeof(ColorExtensions).GetMethod("ToHex")!.IsDefined(
            typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)
        && typeof(ColorExtensions).GetMethod("ToHex")!.GetParameters()[0].ParameterType == typeof(Color)
        """)]
    [InlineData("GenericDeclarations", """
        typeof(Box<>).GetGenericArguments()[0].GenericParameterAttributes.HasFlag(
            System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint)
        && typeof(IRepository<>).GetGenericArguments()[0].GenericParameterAttributes.HasFlag(
            System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint)
        """)]
    [InlineData("AttributeDeclarations", """
        typeof(Data).IsDefined(typeof(System.SerializableAttribute), false)
        && ((System.ComponentModel.DescriptionAttribute)typeof(Data).GetCustomAttributes(
            typeof(System.ComponentModel.DescriptionAttribute), false)[0]).Description == "payload"
        && ((System.ComponentModel.DescriptionAttribute)typeof(Data).GetProperty("Email")!.GetCustomAttributes(
            typeof(System.ComponentModel.DescriptionAttribute), false)[0]).Description == "address"
        && ((System.ComponentModel.DescriptionAttribute)typeof(Data).GetMethod("Read")!.GetParameters()[0]
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)[0]).Description == "input"
        """)]
    public void Structure_PreservesPublishedClrIdentity(string module, string expression)
    {
        var doc = ReadPage("structure-tags");
        var source = ExemplarCompileChecker.ExtractCompletePrograms(doc.Content)
            .Single(program => program.Source.StartsWith("module " + module, StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, doc.Path);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal("True", Run(compiled.GeneratedCode, expression));
    }

    [Fact]
    public void Events_PreserveActualClrAccessorEffectsWithoutClaimingNativeSubscription()
    {
        var doc = ReadPage("structure-tags");
        var source = ExemplarCompileChecker.ExtractCompletePrograms(doc.Content)
            .Single(program => program.Source.StartsWith("module EventDefinitions\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, doc.Path);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal("11", Run(compiled.GeneratedCode, """
            ((System.Func<int>)(() => {
                Handler handler = value => { };
                var counter = new SubscriptionCounter();
                var changed = typeof(SubscriptionCounter).GetEvent("Changed")!;
                changed.AddEventHandler(counter, handler);
                changed.RemoveEventHandler(counter, handler);
                return counter.Count;
            }))()
            """));
    }

    [Theory]
    [InlineData("index", "FizzBuzz", "GlobalModule.Main()", "fizzbuzz")]
    [InlineData("control-flow", "FizzBuzz", "GlobalModule.Main()", "fizzbuzz")]
    [InlineData("control-flow", "Example", "GlobalModule.PrintEvens(6)", "evens")]
    [InlineData("structure-tags", "FunctionHeaders", "GlobalModule.PrintSum(2, 3)", "sum")]
    public void ConsoleExamples_ExecuteOriginalOutputSequences(
        string page, string module, string expression, string oracle)
    {
        var doc = ReadPage(page);
        var source = ExemplarCompileChecker.ExtractCompletePrograms(doc.Content)
            .Single(program => program.Source.StartsWith("module " + module + "\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, doc.Path);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var expected = oracle switch
        {
            "fizzbuzz" => string.Join("|", Enumerable.Range(1, 100).Select(value =>
                value % 15 == 0 ? "FizzBuzz" : value % 3 == 0 ? "Fizz" : value % 5 == 0 ? "Buzz" : value.ToString())) + "|",
            "evens" => "2|4|6|",
            "sum" => "5|",
            _ => throw new ArgumentOutOfRangeException(nameof(oracle))
        };
        Assert.Equal(expected, Run(compiled.GeneratedCode, $$"""
            ((System.Func<string>)(() => {
                var previous = System.Console.Out;
                using var output = new System.IO.StringWriter();
                try {
                    System.Console.SetOut(output);
                    {{expression}};
                    return output.ToString().Replace(System.Environment.NewLine, "|");
                }
                finally { System.Console.SetOut(previous); }
            }))()
            """));
    }

    [Theory]
    [InlineData("en-US", "coffee  |      3.50|7.0:${x}")]
    [InlineData("de-DE", "coffee  |      3,50|7,0:${x}")]
    public void Interpolation_PreservesCultureAndLiteralPlaceholders(string culture, string expected)
    {
        var doc = ReadPage("string-operations");
        var source = ExemplarCompileChecker.ExtractCompletePrograms(doc.Content)
            .Single(program => program.Source.StartsWith("module Receipt\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, doc.Path);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(expected, Run(compiled.GeneratedCode, $$"""
            ((System.Func<string>)(() => {
                var previous = System.Globalization.CultureInfo.CurrentCulture;
                try {
                    System.Globalization.CultureInfo.CurrentCulture =
                        System.Globalization.CultureInfo.GetCultureInfo("{{culture}}");
                    return GlobalModule.Main();
                }
                finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
            }))()
            """));
    }

    private static DocFile ReadPage(string page)
    {
        var path = "docs/syntax-reference/" + page + ".md";
        return new DocFile(path, File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)));
    }

    private static string Run(string generatedCode, string expression) =>
        InteropScope.RefOutImplementationTests.Run(generatedCode + $$"""

            public static class MarkdownReferenceProbe
            {
                public static object? Run()
                {
                    var previous = System.Globalization.CultureInfo.CurrentCulture;
                    try
                    {
                        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                        return {{expression}};
                    }
                    finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
                }
            }
            """, "MarkdownReferenceProbe");
}
