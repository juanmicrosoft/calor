using System.Text;
using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the docs-drift checker behind <c>calor self-check docs</c>
/// (Phase 1 item 6: spec single-sourcing). Each test injects a fake drifted
/// doc string and asserts the drift is detected — or that clean docs pass.
/// </summary>
[Collection("ProductionPipelineConsole")]
public class DocDriftCheckerTests
{
    private const string ValidTeachingProgram = "module Good\n  func Value () → int\n    return 1\n";

    [Fact]
    public void ConverterPreservationHelp_UsesOwnedFramesWithoutAnUnconditionalParseGuarantee()
    {
        var option = Commands.ConvertCommand.Create().Options.Single(option => option.HasAlias("--passthrough"));
        var description = Assert.IsType<string>(option.Description);
        Assert.Contains("converter-owned raw frames", description);
        Assert.DoesNotContain("§", description);
        Assert.DoesNotContain("always parses", description);
    }

    [Fact]
    public void PublicConvertGuide_RetainsExactOriginalInputAndCurrentCounterTranscript()
    {
        var root = CliTestHarness.FindRepoRoot();
        var content = File.ReadAllText(Path.Combine(root, "website/content/cli/convert.mdx")).Replace("\r\n", "\n");
        var input = System.Text.RegularExpressions.Regex.Match(content,
            @"\{/\* w1-source: W1-F6-rescue \*/\}\s*\n```csharp\n(?<body>.*?)\n```",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(input.Success);
        var fixture = File.ReadAllText(Path.Combine(root,
            "tests/TestData/InteropScope/W1/fixtures/W1-F6-rescue.cs.txt")).Replace("\r\n", "\n");
        Assert.Contains(input.Groups["body"].Value, fixture);
        var output = System.Text.RegularExpressions.Regex.Match(content,
            @"\{/\* w1-transcript: W1-F6-rescue \*/\}\s*\n```calor\n(?<body>.*?)\n```",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(output.Success);
        var transcript = ActiveSyntaxInputs.ForCurrentTranscript(root,
            "tests/TestData/InteropScope/W1/transcripts/W1-F6-rescue.json");
        using var expected = JsonDocument.Parse(File.ReadAllText(transcript));
        Assert.Equal(expected.RootElement.GetProperty("cli-convert").GetProperty("calor").GetString()!
            .Replace("\r\n", "\n").TrimEnd('\n'), output.Groups["body"].Value.TrimEnd('\n'));
    }

    [Theory]
    [InlineData("CLAUDE.md")]
    [InlineData("AGENTS.md")]
    [InlineData(".github/copilot-instructions.md")]
    public void RepositoryAgentManual_UsesNativeSyntaxAndCompilablePrograms(string relativePath)
    {
        var root = CliTestHarness.FindRepoRoot();
        var content = File.ReadAllText(Path.Combine(root, relativePath));
        Assert.DoesNotContain("§", content);
        Assert.DoesNotContain("INT:42", content);
        Assert.DoesNotContain("STR:\"", content);
        var programs = ExemplarCompileChecker.ExtractAgentTaskPrograms(content);
        Assert.NotEmpty(programs);
        foreach (var program in programs)
        {
            Assert.False(program.Wrapped, relativePath);
            var compiled = Program.Compile(program.Source, relativePath);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            var validated = CodeGen.GeneratedCSharpCompiler.Validate(compiled.GeneratedCode);
            Assert.True(validated.CompilationSuccess, string.Join("\n", validated.CompilationErrors));
        }
        Assert.Empty(ExemplarCompileChecker.CheckAgentTaskReference(new DocFile(relativePath, content)));
    }

    [Theory]
    [InlineData("calls.mdx", 3, 1, new[] { "CallExamples", "RefOutDemo" }, new[] { "9", "44" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()" })]
    [InlineData("binding.mdx", 8, 6, new[] { "BindingExamples", "ExplicitBindings" }, new[] { "150", "16" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()" })]
    [InlineData("types.mdx", 6, 2, new[] { "ArrayTypes", "NullableFallback", "Division", "CollectionTypes" },
        new[] { "6", "anonymous", "42", "51" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()" })]
    [InlineData("control-flow.mdx", 10, 0,
        new[] { "RangeSnapshots", "ReevaluatedLoops", "WhileAndDo", "DictionaryIteration", "LoopExits", "FizzBuzz",
            "Example", "EarlyReturn", "HttpStatus", "MatchBlocks" },
        new[] { "33", "4", "31", "55", "5", "FizzBuzz:Fizz:Buzz:7", "3", "120", "Not Found:A:large positive", "two" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()",
            "GlobalModule.Main()",
            "GlobalModule.Classify(15) + \":\" + GlobalModule.Classify(3) + \":\" + GlobalModule.Classify(5) + \":\" + GlobalModule.Classify(7)",
            "GlobalModule.CountEvens(6)", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Word(2)" })]
    [InlineData("inheritance.mdx", 8, 0,
        new[] { "ClassModifiers", "VirtualDispatch", "InterfaceInheritance", "InterfaceDispatch", "SealedOverride", "BaseMethods",
            "BaseConstructors", "ShapeHierarchy" },
        new[] { "59", "Woof!", "...:Rex:7", "9", "42", "15", "Rex:Collie", "Circle with area 78.53975" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()",
            "GlobalModule.Main()",
            "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Demo()" })]
    [InlineData("string-operations.mdx", 5, 1,
        new[] { "StringBasics", "StringComparisons", "TextTools", "Receipt" },
        new[] { "11:HELLO WORLD:ell:hello Calor", "True:True:6:True", "True:True:Cat", "coffee  |      3.50|7.0:${x}" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()" })]
    [InlineData("contracts.mdx", 4, 0,
        new[] { "ContractDivide", "AbsoluteValue", "ContractClamp", "NonEmptyCollection" },
        new[] { "4", "5", "15", "42" },
        new[] { "GlobalModule.Divide(9, 2)", "GlobalModule.Abs(-5)", "GlobalModule.Main()",
            "GlobalModule.First(new int[] { 42 })" })]
    [InlineData("index.mdx", 2, 0,
        new[] { "LabelExample", "FizzBuzz" }, new[] { "42", "FizzBuzz:Fizz:Buzz:7" },
        new[] { "Demo.DemoModule.Main()", "GlobalModule.Main()" })]
    [InlineData("expressions.mdx", 5, 0,
        new[] { "ExpressionPrecedence", "ArithmeticBehavior", "ComparisonExpressions", "ExpressionEvaluation",
            "CollectionExpressions" },
        new[] { "14:20:5:4:8:2:5:5:True:11:2", "42:-2:-1", "True:False:True:False:False:True",
            "False:True:True:True:5:7:8:100:6:3:4:5:8:9:10", "True:True:True:1:5" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()",
            "GlobalModule.Main()" })]
    [InlineData("refinement-types.mdx", 4, 0,
        new[] { "PortConstraint", "TextConstraint", "TransferConstraint", "ConstraintBoundary" },
        new[] { "443", "5", "7", "-1" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()" })]
    [InlineData("cli/convert.mdx", 4, 0,
        new[] { "Counter", "Probe", "Countdown", "Grids" }, new[] { "1", "3", "3,2,1", "13" },
        new[] { "new Counter().Next()", "Probe.UseShadow()", "System.String.Join(\",\", new Countdown().Steps)",
            "Grid.Total(3)" })]
    [InlineData("getting-started/claude-integration.mdx", 5, 1,
        new[] { "ContractReview", "LoopReview", "BoundedFactorial", "ExtractedCalculation" },
        new[] { "42", "15", "120", "25" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()" })]
    [InlineData("getting-started/index.mdx", 1, 0,
        new[] { "MyApp" }, new[] { "0" },
        new[] { "((System.Func<int>)(() => { GlobalModule.Main(); return 0; }))()" })]
    [InlineData("getting-started/installation.mdx", 1, 0,
        new[] { "InstalledHello" }, new[] { "0" },
        new[] { "((System.Func<int>)(() => { GlobalModule.Main(); return 0; }))()" })]
    [InlineData("getting-started/gemini-integration.mdx", 1, 0,
        new[] { "GeminiDivision" }, new[] { "4" }, new[] { "GlobalModule.Divide(9, 2)" })]
    [InlineData("cli/init.mdx", 1, 0,
        new[] { "InitializedExample" }, new[] { "42" }, new[] { "GlobalModule.Answer()" })]
    [InlineData("cli/compile.mdx", 2, 1,
        new[] { "CompileExample" }, new[] { "42" }, new[] { "GlobalModule.Increment(41)" })]
    [InlineData("cli/ids.mdx", 1, 0,
        new[] { "Calculator" }, new[] { "42" }, new[] { "GlobalModule.Double(21)" })]
    [InlineData("cli/lint.mdx", 2, 0,
        new[] { "LintLoops", "LintNames" }, new[] { "0", "42" },
        new[] { "((System.Func<int>)(() => { GlobalModule.Main(); return 0; }))()", "GlobalModule.f001(21)" })]
    [InlineData("guides/dependent-types-tutorial.mdx", 4, 0,
        new[] { "ServerContracts", "BankingContracts", "MissingFundsContract", "DivisionContracts" },
        new[] { "10", "70", "70", "-2" },
        new[] { "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()" })]
    [InlineData("benchmarking/metrics/effect-soundness.mdx", 7, 2,
        new[] { "OrderEffects", "HiddenNetwork", "DatabasePermissions", "PureComposition", "DeclaredPipeline" },
        new[] { "True", "200", "42", "42", "42" },
        new[] { "GlobalModule.ProcessOrder()", "GlobalModule.Calculate()", "GlobalModule.GetUser(42)",
            "GlobalModule.Calculate()", "GlobalModule.GetUser(42)" })]
    [InlineData("contributing/adding-benchmarks.mdx", 2, 0,
        new[] { "YourModule", "FactorialBenchmark" }, new[] { "0", "120" },
        new[] { "((System.Func<int>)(() => { GlobalModule.Main(); return 0; }))()",
            "global::Math.MathModule.Factorial(5)" })]
    [InlineData("benchmarking/metrics/error-detection.mdx", 5, 0,
        new[] { "DivisionSignals", "NullSignals", "BoundsSignals", "BuggyCaller", "NullableSignals" },
        new[] { "4", "3", "42", "Requires", "3" },
        new[] { "GlobalModule.Divide(9, 2)", "GlobalModule.Length(\"abc\")",
            "GlobalModule.At(new int[] { 42 }, 0)",
            "((System.Func<string>)(() => { try { GlobalModule.Trigger(9, 5); return \"not rejected\"; } catch (Calor.Runtime.ContractViolationException failure) { return failure.Kind.ToString(); } }))()",
            "GlobalModule.SafeDivide(6, 2)" })]
    [InlineData("benchmarking/metrics/task-completion.mdx", 2, 0,
        new[] { "TaskSafeDivide", "TaskNormalizeScore" }, new[] { "3", "75" },
        new[] { "GlobalModule.SafeDivide(6, 2)", "GlobalModule.NormalizeScore(75, 100)" })]
    [InlineData("philosophy/index.mdx", 2, 0,
        new[] { "DivisionObligations", "Calculator" }, new[] { "4", "42" },
        new[] { "GlobalModule.Divide(9, 2)", "GlobalModule.Add(20, 22)" })]
    [InlineData("philosophy/design-principles.mdx", 1, 0,
        new[] { "DesignContracts" }, new[] { "4" }, new[] { "GlobalModule.Divide(9, 2)" })]
    [InlineData("semantics/versioning.mdx", 1, 0,
        new[] { "InstalledSemantics" }, new[] { "42" }, new[] { "GlobalModule.Next(41)" })]
    [InlineData("semantics/core.mdx", 7, 0,
        new[] { "CoreScopes", "CoreNestedReturn", "CoreOverflow", "CoreConversions", "CoreRequires",
            "CoreEnsures", "CoreNullableValues" },
        new[] { "2:1", "42", "42", "45", "1", "1", "none" },
        new[] { "GlobalModule.Read(true) + \":\" + GlobalModule.Read(false)", "GlobalModule.Read(true)",
            "GlobalModule.Increment(41)", "GlobalModule.Value()", "GlobalModule.Positive(1)",
            "GlobalModule.Positive(1)", "GlobalModule.Fallback(null)" })]
    [InlineData("semantics/normal-form.mdx", 4, 0,
        new[] { "NormalBinary", "NormalAnd", "NormalOr", "NormalCalls" },
        new[] { "10", "False", "True", "123:123" },
        new[] { "GlobalModule.Value(2, 3, 4)", "GlobalModule.Both(false, true)", "GlobalModule.Either(true, false)",
            """
            ((System.Func<string>)(() => {
                var previousOutput = System.Console.Out;
                using var output = new System.IO.StringWriter();
                try {
                    System.Console.SetOut(output);
                    var value = GlobalModule.Main();
                    return output.ToString() + ":" + value;
                }
                finally { System.Console.SetOut(previousOutput); }
            }))()
            """ })]
    [InlineData("structure-tags.mdx", 22, 1,
        new[] { "StructureModules", "BlockNamespace", "FunctionHeaders", "AsyncFunctions", "AsyncMethods",
            "AwaitExpressions", "ConfigureAwaitLimit", "LambdaExamples", "AsyncLambdas", "LocalCallables", "DelegateDefinitions",
            "EnumDefinitions", "EnumExtensions", "EnumReceiverLimit", "EscapedEnumLimit", "EventDefinitions", "ParameterArrays", "ArrayDeclarations",
            "IndentedBlocks", "GenericDeclarations", "AttributeDeclarations" },
        new[] { "42", "42", "10", "21:7:9", "42", "42", "42", "24", "42", "3", "5", "True",
            "#FF0000:True", "#FF0000", "7", "0", "7", "2:7", "15", "42", "7" },
        new[] { "Demo.DemoModule.Main()", "new Demo.Tools.Box().Read()", "GlobalModule.Main()",
            "GlobalModule.Read().GetAwaiter().GetResult() + \":\" + GlobalModule.ExplicitTask().GetAwaiter().GetResult() + \":\" + GlobalModule.ExplicitValueTask().GetAwaiter().GetResult()",
            "GlobalModule.Main().GetAwaiter().GetResult()", "GlobalModule.Main().GetAwaiter().GetResult()",
            "GlobalModule.Read().GetAwaiter().GetResult()",
            "GlobalModule.Main()", "GlobalModule.Main().GetAwaiter().GetResult()", "GlobalModule.Use(2)",
            "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Main()", "GlobalModule.Receiver()", "GlobalModule.Main()",
            "new SubscriptionCounter().Count", "GlobalModule.Main()",
            "new DataProcessor().ProcessData(new string[] { \"a\", \"b\" }) + \":\" + GlobalModule.First(new int[][] { new int[] { 7 } })",
            "GlobalModule.Main()", "GlobalModule.Main()", "new Data().Read()" })]
    public void PublicNativeReference_CompilesPositiveAndExactNegativeExamples(
        string page, int completeCount, int negativeCount, string[] moduleNames, string[] expectedResults,
        string[] expressions)
    {
        var relativePath = "website/content/" + (page.Contains('/') ? page : "syntax-reference/" + page);
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), relativePath));
        Assert.DoesNotContain("§", content);
        var coverage = new WebsiteExampleChecker.Coverage();
        var diagnostics = WebsiteExampleChecker.Check([new DocFile(relativePath, content)], coverage);
        Assert.Equal(completeCount, coverage.CompletePrograms);
        Assert.Equal(negativeCount, coverage.NegativePrograms);
        var programs = ExemplarCompileChecker.ExtractCompletePrograms(content);
        Assert.Equal(moduleNames.Length, expectedResults.Length);
        Assert.Equal(moduleNames.Length, expressions.Length);
        Assert.Equal(moduleNames.Length, programs.Count);
        for (var index = 0; index < programs.Count; index++)
        {
            var program = programs[index];
            var compiled = Program.Compile(program.Source, relativePath);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(moduleNames[index], Assert.IsType<Ast.ModuleNode>(compiled.Ast).Name);
            var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

                public static class __CalorPublishedProgramProbe
                {
                    public static object? Run()
                    {
                        var previous = System.Globalization.CultureInfo.CurrentCulture;
                        try
                        {
                            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                            return {{expressions[index]}};
                        }
                        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
                    }
                }
                """, "__CalorPublishedProgramProbe");
            Assert.Equal(expectedResults[index], observed);
        }
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
    }

    [Fact]
    public void PublicStructureReference_PreservesPublishedClrMetadataAndEventAccessors()
    {
        const string path = "website/content/syntax-reference/structure-tags.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path));
        Assert.Empty(WebsiteExampleChecker.Check([new DocFile(path, content)]));
        var programs = ExemplarCompileChecker.ExtractCompletePrograms(content);
        foreach (var (module, observation, expected) in new[]
        {
            ("StructureModules", """
                return typeof(Demo.Arithmetic).IsAbstract && typeof(Demo.Arithmetic).IsSealed
                    && typeof(Demo.Arithmetic).GetMethod("Twice")!.IsStatic
                    && typeof(Demo.DemoModule).Assembly.GetType("Demo.StructureModules") == null;
                """, "True"),
            ("BlockNamespace", """
                return typeof(Demo.Tools.Box).GetField("Builder")!.FieldType == typeof(System.Text.StringBuilder);
                """, "True"),
            ("FunctionHeaders", """
                var previous = System.Console.Out;
                using var output = new System.IO.StringWriter();
                try
                {
                    System.Console.SetOut(output);
                    GlobalModule.PrintSum(2, 3);
                    return output.ToString() == "5" + System.Environment.NewLine;
                }
                finally { System.Console.SetOut(previous); }
                """, "True"),
            ("AsyncFunctions", """
                GlobalModule.Wait().GetAwaiter().GetResult();
                return typeof(GlobalModule).GetMethod("Read")!.ReturnType == typeof(System.Threading.Tasks.Task<int>)
                    && typeof(GlobalModule).GetMethod("ExplicitTask")!.ReturnType == typeof(System.Threading.Tasks.Task<int>)
                    && typeof(GlobalModule).GetMethod("ExplicitValueTask")!.ReturnType == typeof(System.Threading.Tasks.ValueTask<int>)
                    && typeof(GlobalModule).GetMethod("Wait")!.ReturnType == typeof(System.Threading.Tasks.Task);
                """, "True"),
            ("ConfigureAwaitLimit", """
                var method = typeof(GlobalModule).GetMethod("Read")!;
                return method.ReturnType == typeof(System.Threading.Tasks.Task<int>)
                    && method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false);
                """, "True"),
            ("DelegateDefinitions", """
                var calculator = typeof(Calculator).GetMethod("Invoke")!;
                var logger = typeof(Logger).GetMethod("Invoke")!;
                return calculator.ReturnType == typeof(int) && calculator.GetParameters().Length == 2
                    && calculator.GetParameters()[0].ParameterType == typeof(int)
                    && calculator.GetParameters()[1].ParameterType == typeof(int)
                    && logger.ReturnType == typeof(void) && logger.GetParameters().Length == 1
                    && logger.GetParameters()[0].ParameterType == typeof(string);
                """, "True"),
            ("EnumDefinitions", """
                return System.Enum.GetUnderlyingType(typeof(Color)) == typeof(int)
                    && (int)Color.Red == 0 && (int)StatusCode.NotFound == 404
                    && System.Enum.GetUnderlyingType(typeof(Access)) == typeof(byte);
                """, "True"),
            ("EnumExtensions", """
                var extension = typeof(ColorExtensions).GetMethod("ToHex")!;
                return extension.IsStatic && extension.GetParameters()[0].ParameterType == typeof(Color)
                    && extension.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);
                """, "True"),
            ("EventDefinitions", """
                Handler handler = value => { };
                var counter = new SubscriptionCounter();
                var changed = typeof(SubscriptionCounter).GetEvent("Changed")!;
                changed.AddEventHandler(counter, handler);
                changed.RemoveEventHandler(counter, handler);
                return typeof(Publisher).GetEvent("Changed")!.EventHandlerType == typeof(Handler)
                    && counter.Count == 11;
                """, "True"),
            ("GenericDeclarations", """
                return typeof(Box<>).GetGenericArguments()[0].GenericParameterAttributes
                        .HasFlag(System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint)
                    && typeof(IRepository<>).GetGenericArguments()[0].GenericParameterAttributes
                        .HasFlag(System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint);
                """, "True"),
            ("AttributeDeclarations", """
                var type = typeof(Data);
                var parameter = type.GetMethod("Read")!.GetParameters()[0];
                return type.IsDefined(typeof(System.SerializableAttribute), false)
                    && ((System.ComponentModel.DescriptionAttribute)type.GetCustomAttributes(
                        typeof(System.ComponentModel.DescriptionAttribute), false)[0]).Description == "payload"
                    && ((System.ComponentModel.DescriptionAttribute)type.GetProperty("Email")!.GetCustomAttributes(
                        typeof(System.ComponentModel.DescriptionAttribute), false)[0]).Description == "address"
                    && ((System.ComponentModel.DescriptionAttribute)parameter.GetCustomAttributes(
                        typeof(System.ComponentModel.DescriptionAttribute), false)[0]).Description == "input";
                """, "True"),
        })
        {
            var source = programs.Single(program => program.Source.StartsWith("module " + module,
                StringComparison.Ordinal)).Source;
            var compiled = Program.Compile(source, path);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.DoesNotContain("raw ", source);
            Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

                public static class Probe
                {
                    public static object? Run()
                    {
                        {{observation}}
                    }
                }
                """));
        }
    }

    [Theory]
    [InlineData("en-US", "coffee  |      3.50|7.0:${x}")]
    [InlineData("de-DE", "coffee  |      3,50|7,0:${x}")]
    public void PublicStringInterpolation_PreservesThePublishedCultureBehavior(string culture, string expected)
    {
        var path = "website/content/syntax-reference/string-operations.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path));
        var source = ExemplarCompileChecker.ExtractCompletePrograms(content)
            .Single(program => program.Source.StartsWith("module Receipt", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, path);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

            public static class Probe
            {
                public static object? Run()
                {
                    var previous = System.Globalization.CultureInfo.CurrentCulture;
                    try
                    {
                        System.Globalization.CultureInfo.CurrentCulture =
                            System.Globalization.CultureInfo.GetCultureInfo("{{culture}}");
                        return GlobalModule.Main();
                    }
                    finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
                }
            }
            """);
        Assert.Equal(expected, observed);
    }

    [Theory]
    [InlineData("ContractDivide", "GlobalModule.Divide(9, 0)", "!ContractViolationException", "!DivideByZeroException")]
    [InlineData("ContractDivide", "GlobalModule.Divide(-1, 1)", "!ContractViolationException", "-1")]
    [InlineData("ContractClamp", "GlobalModule.Clamp(4, 10, 0)", "!ContractViolationException", "10")]
    [InlineData("NonEmptyCollection", "GlobalModule.First(System.Array.Empty<int>())",
        "!ContractViolationException", "!IndexOutOfRangeException")]
    [InlineData("AbsoluteValue", "GlobalModule.Abs(int.MinValue)", "!OverflowException", "!OverflowException")]
    public void PublicContracts_SeparateEnabledGuardsFromBodyBehavior(
        string module, string expression, string enabled, string disabled)
    {
        var path = "website/content/syntax-reference/contracts.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path));
        var source = ExemplarCompileChecker.ExtractCompletePrograms(content)
            .Single(program => program.Source.StartsWith("module " + module + "\n", StringComparison.Ordinal)).Source;
        foreach (var (mode, expected) in new[] { (ContractMode.Debug, enabled), (ContractMode.Off, disabled) })
        {
            var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = mode });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

                public static class Probe
                {
                    public static object? Run() => {{expression}};
                }
                """);
            Assert.Equal(expected, observed);
        }
    }

    [Theory]
    [InlineData("GlobalModule.Increment(int.MaxValue)", "!OverflowException")]
    [InlineData("GlobalModule.Divide(1, 0)", "!DivideByZeroException")]
    [InlineData("GlobalModule.Divide(int.MinValue, -1)", "!OverflowException")]
    [InlineData("GlobalModule.Divide(-7, 3)", "-2")]
    [InlineData("GlobalModule.Remainder(-7, 3)", "-1")]
    public void PublicExpressions_PreserveCheckedArithmeticAndSignedDivision(string expression, string expected)
    {
        var path = "website/content/syntax-reference/expressions.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path));
        var source = ExemplarCompileChecker.ExtractCompletePrograms(content)
            .Single(program => program.Source.StartsWith("module ArithmeticBehavior\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, path);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

            public static class Probe
            {
                public static object? Run() => {{expression}};
            }
            """);
        Assert.Equal(expected, observed);
    }

    [Theory]
    [InlineData("PortConstraint", "GlobalModule.SelectPort(0)", "!ContractViolationException", "0")]
    [InlineData("PortConstraint", "GlobalModule.SelectPort(65536)", "!ContractViolationException", "65536")]
    [InlineData("TextConstraint", "GlobalModule.Length(\"\")", "!ContractViolationException", "0")]
    [InlineData("TextConstraint", "GlobalModule.Length(null!)", "!ContractViolationException", "!NullReferenceException")]
    [InlineData("TransferConstraint", "GlobalModule.Transfer(3, 5)", "!ContractViolationException", "-2")]
    [InlineData("ConstraintBoundary", "GlobalModule.Reset(5)", "-1", "-1")]
    public void PublicValueConstraints_SeparateEntryGuardsFromNominalAndAssignmentInvariants(
        string module, string expression, string enabled, string disabled)
    {
        var path = "website/content/syntax-reference/refinement-types.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path));
        var source = ExemplarCompileChecker.ExtractCompletePrograms(content)
            .Single(program => program.Source.StartsWith("module " + module + "\n", StringComparison.Ordinal)).Source;
        foreach (var (mode, expected) in new[] { (ContractMode.Debug, enabled), (ContractMode.Off, disabled) })
        {
            var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = mode });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

                public static class Probe
                {
                    public static object? Run() => {{expression}};
                }
                """);
            Assert.Equal(expected, observed);
        }
    }

    [Theory]
    [InlineData(-1, "!ContractViolationException")]
    [InlineData(0, "1")]
    [InlineData(12, "479001600")]
    [InlineData(13, "!ContractViolationException")]
    public void PublicClaudeGuide_FactorialUsesActualCheckedIntegerBounds(int value, string expected)
    {
        var path = "website/content/getting-started/claude-integration.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith("module BoundedFactorial\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = ContractMode.Debug });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

            public static class Probe
            {
                public static object? Run() => GlobalModule.Factorial({{value}});
            }
            """);
        Assert.Equal(expected, observed);
    }

    [Theory]
    [InlineData("ServerContracts", "GlobalModule.ConfigureServer(0, \"localhost\", 10)", "!ContractViolationException", "10")]
    [InlineData("ServerContracts", "GlobalModule.ConfigureServer(65536, \"localhost\", 10)", "!ContractViolationException", "10")]
    [InlineData("ServerContracts", "GlobalModule.ConfigureServer(443, \"\", 10)", "!ContractViolationException", "10")]
    [InlineData("ServerContracts", "GlobalModule.ConfigureServer(443, null!, 10)", "!ContractViolationException", "10")]
    [InlineData("ServerContracts", "GlobalModule.ConfigureServer(443, \"localhost\", 0)", "!ContractViolationException", "0")]
    [InlineData("BankingContracts", "GlobalModule.Transfer(3, 5)", "!ContractViolationException", "-2")]
    [InlineData("MissingFundsContract", "GlobalModule.Transfer(3, 5)", "!ContractViolationException", "-2")]
    [InlineData("DivisionContracts", "GlobalModule.Divide(1, 0)", "!ContractViolationException", "!DivideByZeroException")]
    [InlineData("DivisionContracts", "GlobalModule.Divide(int.MinValue, -1)", "!ContractViolationException", "!OverflowException")]
    public void PublicInputContractsTutorial_ObservesOriginalBoundaryCallsWithChecksEnabledAndOff(
        string module, string expression, string enabled, string disabled)
    {
        var path = "website/content/guides/dependent-types-tutorial.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith($"module {module}\n", StringComparison.Ordinal)).Source;
        foreach (var mode in new[] { ContractMode.Debug, ContractMode.Off })
        {
            var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = mode });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

                public static class Probe
                {
                    public static object? Run() => {{expression}};
                }
                """);
            Assert.Equal(mode == ContractMode.Debug ? enabled : disabled, observed);
        }
    }

    [Theory]
    [InlineData("BankingContracts", "Requires")]
    [InlineData("MissingFundsContract", "Ensures")]
    public void PublicInputContractsTutorial_IdentifiesActualFailedContractKind(string module, string expectedKind)
    {
        var path = "website/content/guides/dependent-types-tutorial.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith($"module {module}\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = ContractMode.Debug });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + """

            public static class Probe
            {
                public static object? Run()
                {
                    try { return GlobalModule.Transfer(3, 5); }
                    catch (Calor.Runtime.ContractViolationException failure) { return failure.Kind.ToString(); }
                }
            }
            """);
        Assert.Equal(expectedKind, observed);
    }

    [Theory]
    [InlineData("BankingContracts", "proven", false)]
    [InlineData("MissingFundsContract", "refuted", true)]
    public async Task PublicInputContractsTutorial_VerifiesExactPublishedTransferSources(
        string module, string expectedStatus, bool expectedError)
    {
        var path = "website/content/guides/dependent-types-tutorial.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith($"module {module}\n", StringComparison.Ordinal)).Source;
        var arguments = JsonSerializer.SerializeToElement(new { source, timeout = 5000 });
        var response = await new Calor.Compiler.Mcp.Tools.VerifyTool().ExecuteAsync(arguments);
        var text = Assert.IsType<string>(Assert.Single(response.Content).Text);
        Assert.Equal(expectedError, response.IsError);
        using var document = JsonDocument.Parse(text);
        Assert.Equal(!expectedError, document.RootElement.GetProperty("success").GetBoolean());
        var function = document.RootElement.GetProperty("functions").EnumerateArray()
            .Single(function => function.GetProperty("functionName").GetString() == "Transfer");
        var postcondition = Assert.Single(function.GetProperty("contracts").EnumerateArray()
            .Where(contract => contract.GetProperty("type").GetString() == "postcondition"));
        Assert.Equal(expectedStatus, postcondition.GetProperty("status").GetString());
        if (expectedError)
            Assert.NotEmpty(postcondition.GetProperty("counterexample").GetProperty("bindings").EnumerateArray());
    }

    [Theory]
    [InlineData("CoreOverflow", "GlobalModule.Increment(int.MaxValue)", "!OverflowException", "!OverflowException")]
    [InlineData("CoreRequires", "GlobalModule.Positive(0)", "!ContractViolationException", "0")]
    [InlineData("CoreRequires", "GlobalModule.Positive(-1)", "!ContractViolationException", "-1")]
    [InlineData("CoreEnsures", "GlobalModule.Positive(0)", "!ContractViolationException", "0")]
    [InlineData("CoreNullableValues", "GlobalModule.Fallback(null)", "none", "none")]
    [InlineData("CoreNullableValues", "GlobalModule.Fallback(\"abc\")", "abc", "abc")]
    [InlineData("CoreNullableValues",
        "((System.Func<int>)(() => { GlobalModule.Fail(); return 0; }))()",
        "!InvalidOperationException", "!InvalidOperationException")]
    public void PublicCoreSemantics_ExercisesEnabledReleaseAndOffBoundaries(
        string module, string expression, string enabled, string disabled)
    {
        const string path = "website/content/semantics/core.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith($"module {module}\n", StringComparison.Ordinal)).Source;
        foreach (var mode in new[] { ContractMode.Debug, ContractMode.Release, ContractMode.Off })
        {
            var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = mode });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

                public static class Probe
                {
                    public static object? Run() => {{expression}};
                }
                """);
            Assert.Equal(mode == ContractMode.Off ? disabled : enabled, observed);
        }
    }

    [Theory]
    [InlineData("NormalBinary")]
    [InlineData("NormalAnd")]
    [InlineData("NormalOr")]
    public void PublicNormalForm_ValidatesThePublishedPrimitiveIrSubsetSeparatelyFromProduction(
        string module)
    {
        const string path = "website/content/semantics/normal-form.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith($"module {module}\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, path);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var cnf = new IR.CnfLowering().LowerModule(Assert.IsType<Ast.ModuleNode>(compiled.Ast));
        var function = Assert.Single(cnf.Functions);
        var validator = new IR.CnfValidator();
        validator.ValidateFunction(function);
        Assert.True(validator.IsValid, string.Join("\n", validator.Errors));
        Assert.Equal(module == "NormalBinary" ? IR.CnfType.Int : IR.CnfType.Bool, function.ReturnType);
        if (module != "NormalBinary")
            Assert.Single(function.Body.Statements.OfType<IR.CnfBranch>());
    }

    [Fact]
    public void PublicNormalForm_SeparatesNativeExecutionFromThePrototypeNamedCallShape()
    {
        const string path = "website/content/semantics/normal-form.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith("module NormalCalls\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, path);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var cnf = new IR.CnfLowering().LowerModule(Assert.IsType<Ast.ModuleNode>(compiled.Ast));
        var main = cnf.Functions.Single(function => function.Name == "Main");
        var assignments = main.Body.Statements.OfType<IR.CnfAssign>()
            .Where(assignment => assignment.Value is IR.CnfCall).ToArray();
        var calls = assignments.Select(assignment => Assert.IsType<IR.CnfCall>(assignment.Value)).ToArray();
        Assert.Equal(new[] { "Mark", "Mark", "Mark", "Combine" }, calls.Select(call => call.FunctionName));
        Assert.Equal(new[] { "1", "2", "3" }, calls.Take(3).Select(call =>
            Assert.IsType<IR.CnfLiteral>(Assert.Single(call.Arguments)).Value.ToString()));
        Assert.Equal(assignments.Take(3).Select(assignment => assignment.Target),
            calls[3].Arguments.Select(argument => Assert.IsType<IR.CnfVariableRef>(argument).Name));
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "1")]
    [InlineData(5, "120")]
    [InlineData(12, "479001600")]
    public void PublicBenchmarkGuide_OriginalCSharpAndNativeFactorialAgreeOnDeclaredValidDomain(int input, string expected)
    {
        var path = "website/content/contributing/adding-benchmarks.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)).Replace("\r\n", "\n");
        var source = ExemplarCompileChecker.ExtractCompletePrograms(content)
            .Single(program => program.Source.StartsWith("module FactorialBenchmark Math\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = ContractMode.Debug });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var originalCSharp = System.Text.RegularExpressions.Regex.Matches(content,
            @"```csharp\n(?<body>.*?)\n```", System.Text.RegularExpressions.RegexOptions.Singleline)[1]
            .Groups["body"].Value;
        foreach (var (csharp, call) in new[]
        {
            (compiled.GeneratedCode, "global::Math.MathModule.Factorial"),
            ("global using System;\n" + originalCSharp, "global::Math.Program.Factorial")
        })
        {
            var observed = InteropScope.RefOutImplementationTests.Run(csharp + $$"""

                public static class Probe
                {
                    public static object? Run() => {{call}}({{input}});
                }
                """);
            Assert.Equal(expected, observed);
        }
    }

    [Fact]
    public void PublicBenchmarkGuide_UsesActualManifestEntrySchema()
    {
        var path = "website/content/contributing/adding-benchmarks.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)).Replace("\r\n", "\n");
        var json = System.Text.RegularExpressions.Regex.Match(content,
            @"```json\n(?<body>.*?)\n```", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(json.Success);
        var manifest = JsonSerializer.Deserialize<Calor.Evaluation.Benchmarks.BenchmarkManifest>(
            json.Groups["body"].Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var entry = Assert.Single(Assert.IsType<Calor.Evaluation.Benchmarks.BenchmarkManifest>(manifest).Benchmarks);
        Assert.Equal("your-benchmark", entry.Id);
        Assert.Equal("YourNewBenchmark/program.calr", entry.CalorFile);
        Assert.Equal("YourNewBenchmark/program.cs", entry.CSharpFile);
        Assert.Equal(1, entry.Level);
        Assert.Null(entry.ExpectedAdvantage);
    }

    [Fact]
    public void PublicTokenEconomics_ExecutesNativeProgramsAndExactOriginalConsoleExamples()
    {
        var path = "website/content/benchmarking/metrics/token-economics.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)).Replace("\r\n", "\n");
        var coverage = new WebsiteExampleChecker.Coverage();
        var diagnostics = WebsiteExampleChecker.Check([new DocFile(path, content)], coverage);
        Assert.Empty(diagnostics);
        Assert.Equal(3, coverage.CompletePrograms);
        Assert.Equal(0, coverage.NegativePrograms);
        var programs = ExemplarCompileChecker.ExtractCompletePrograms(content);
        Assert.Equal(new[] { "module TokenHello\n", "module TokenFizzBuzz\n", "module TokenContracts\n" },
            programs.Select(program => program.Source[..(program.Source.IndexOf('\n') + 1)]));
        var compiled = programs.Select(program => Program.Compile(program.Source, path,
            new CompilationOptions { ContractMode = ContractMode.Debug })).ToArray();
        Assert.All(compiled, result => Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics)));
        var csharp = System.Text.RegularExpressions.Regex.Matches(content,
            @"```csharp\n(?<body>.*?)\n```", System.Text.RegularExpressions.RegexOptions.Singleline);
        var originalHello = Observe(csharp[1].Groups["body"].Value,
            "typeof(Program).GetMethod(\"Main\", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null)");
        var originalFizzBuzz = Observe("public static class Original { public static void Run() {\n"
            + csharp[2].Groups["body"].Value + "\n} }", "Original.Run()");
        Assert.Equal("Hello World\n", originalHello);
        Assert.Equal(originalHello, Observe(compiled[0].GeneratedCode, "GlobalModule.Main()"));
        Assert.Equal(100, originalFizzBuzz.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(originalFizzBuzz, Observe(compiled[1].GeneratedCode, "GlobalModule.Main()"));
        Assert.Equal("3", InteropScope.RefOutImplementationTests.Run(compiled[2].GeneratedCode + """

            public static class Probe
            {
                public static object? Run() => GlobalModule.Divide(6, 2);
            }
            """));

        static string Observe(string source, string operation) =>
            InteropScope.RefOutImplementationTests.Run("global using System;\n" + source + $$"""

                public static class Probe
                {
                    public static object? Run()
                    {
                        var previous = Console.Out;
                        using var writer = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
                        writer.NewLine = "\n";
                        try
                        {
                            Console.SetOut(writer);
                            {{operation}};
                            return writer.ToString();
                        }
                        finally { Console.SetOut(previous); }
                    }
                }
                """);
    }

    [Theory]
    [InlineData("DivisionSignals", "GlobalModule.Divide(9, 0)", "!ContractViolationException", "!DivideByZeroException")]
    [InlineData("DivisionSignals", "GlobalModule.Divide(-1, 2)", "!ContractViolationException", "0")]
    [InlineData("DivisionSignals", "GlobalModule.Divide(9, -2)", "!ContractViolationException", "-4")]
    [InlineData("DivisionSignals", "GlobalModule.Divide(0, -1)", "0", "0")]
    [InlineData("NullSignals", "GlobalModule.Length(null!)", "!ContractViolationException", "!NullReferenceException")]
    [InlineData("BoundsSignals", "GlobalModule.At(new int[] { 42 }, -1)", "!ContractViolationException", "!IndexOutOfRangeException")]
    [InlineData("BoundsSignals", "GlobalModule.At(new int[] { 42 }, 1)", "!ContractViolationException", "!IndexOutOfRangeException")]
    [InlineData("BoundsSignals", "GlobalModule.At(null!, 0)", "!NullReferenceException", "!NullReferenceException")]
    [InlineData("BuggyCaller", "GlobalModule.Trigger(9, 5)", "!ContractViolationException", "!DivideByZeroException")]
    [InlineData("NullableSignals", "GlobalModule.SafeDivide(6, 0)", "!ContractViolationException", "<null>")]
    [InlineData("NullableSignals", "GlobalModule.SafeDivide(-7, 3)", "-2", "-2")]
    [InlineData("NullableSignals", "GlobalModule.SafeDivide(int.MinValue, -1)", "!OverflowException", "!OverflowException")]
    public void PublicErrorDetection_ObservesExactContractAndUncheckedBoundaries(
        string module, string expression, string enabled, string disabled)
    {
        const string path = "website/content/benchmarking/metrics/error-detection.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith($"module {module}\n", StringComparison.Ordinal)).Source;
        foreach (var mode in new[] { ContractMode.Debug, ContractMode.Off })
        {
            var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = mode });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(mode == ContractMode.Debug ? enabled : disabled,
                InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

                    public static class Probe
                    {
                        public static object? Run() => {{expression}};
                    }
                    """));
        }
    }

    [Theory]
    [InlineData("DivisionSignals", "GlobalModule.Divide(9, -2)", "Ensures")]
    [InlineData("BuggyCaller", "GlobalModule.Trigger(9, 5)", "Requires")]
    [InlineData("NullableSignals", "GlobalModule.SafeDivide(6, 0)", "Requires")]
    public void PublicErrorDetection_IdentifiesTheActualFailedContract(
        string module, string expression, string expectedKind)
    {
        const string path = "website/content/benchmarking/metrics/error-detection.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith($"module {module}\n", StringComparison.Ordinal)).Source;
        var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = ContractMode.Debug });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(expectedKind, InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

            public static class Probe
            {
                public static object? Run()
                {
                    try { return {{expression}}; }
                    catch (Calor.Runtime.ContractViolationException failure) { return failure.Kind.ToString(); }
                }
            }
            """));
    }

    [Fact]
    public async Task PublicErrorDetection_ReportsActualLegacyDetectorScoresInsteadOfTheHistoricalIllustration()
    {
        const string path = "website/content/benchmarking/metrics/error-detection.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)).Replace("\r\n", "\n");
        var programs = ExemplarCompileChecker.ExtractCompletePrograms(content);
        Assert.Equal(5, programs.Count);
        var csharp = System.Text.RegularExpressions.Regex.Matches(content,
            @"```csharp\n(?<body>.*?)\n```", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.Equal(4, csharp.Count);
        foreach (var program in programs)
        {
            var metric = await new Calor.Evaluation.Metrics.ErrorDetectionCalculator().CalculateAsync(
                new Calor.Evaluation.Core.EvaluationContext
                {
                    CalorSource = program.Source,
                    CSharpSource = csharp[3].Groups["body"].Value,
                    FileName = path
                });
            Assert.Equal(0.30, metric.CalorScore, 10);
            Assert.Equal(0.30, metric.CSharpScore, 10);
            Assert.Equal(1.0, metric.AdvantageRatio, 10);
            var factors = Assert.IsType<Dictionary<string, bool>>(metric.Details["calorDetectionFactors"]);
            Assert.Equal(6, factors.Count);
            Assert.All(factors, factor => Assert.False(factor.Value, factor.Key));
        }
        foreach (var (index, call, expected) in new[]
        {
            (0, "Original.Divide(9, 2)", "4"),
            (3, "Original.SafeDivide(6, 2)", "3"),
            (3, "Original.SafeDivide(6, 0)", "<null>")
        })
            Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(
                "global using System;\nglobal using System.Diagnostics;\npublic static class Original {\n"
                + csharp[index].Groups["body"].Value + "\n}\n" + $$"""

                public static class Probe
                {
                    public static object? Run() => {{call}};
                }
                """));
    }

    [Theory]
    [InlineData("cli/query.mdx")]
    [InlineData("cli/self-check.mdx")]
    [InlineData("cli/format.mdx")]
    [InlineData("cli/feature-check.mdx")]
    [InlineData("cli/static-analysis.mdx")]
    [InlineData("cli/analyze.mdx")]
    public void PublicNativeCliProse_DoesNotTeachRetiredSectionHeaders(string page)
    {
        var path = "website/content/" + page;
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path));
        Assert.DoesNotContain("§", content);
        Assert.Empty(WebsiteExampleChecker.Check([new DocFile(path, content)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicLintGuide_PreservesTheOriginalExclusiveLoopBound(bool native)
    {
        var path = "website/content/cli/lint.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path));
        string generated;
        if (native)
        {
            var source = ExemplarCompileChecker.ExtractCompletePrograms(content)
                .Single(program => program.Source.StartsWith("module LintLoops\n", StringComparison.Ordinal)).Source;
            var compiled = Program.Compile(source, path);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            generated = compiled.GeneratedCode;
        }
        else
        {
            var original = System.Text.RegularExpressions.Regex.Match(content,
                @"```csharp\r?\n(?<body>.*?)\r?\n```",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            Assert.True(original.Success);
            generated = "using System;\npublic class Original {\n" + original.Groups["body"].Value + "\n}\n";
        }
        var observed = InteropScope.RefOutImplementationTests.Run(generated + $$"""

            public static class Probe
            {
                public static object? Run()
                {
                    var previous = System.Console.Out;
                    using var output = new System.IO.StringWriter();
                    try
                    {
                        System.Console.SetOut(output);
                        {{(native ? "GlobalModule.Main()" : "new Original().Main()")}};
                        return output.ToString().Replace("\r\n", "\n");
                    }
                    finally { System.Console.SetOut(previous); }
                }
            }
            """);
        Assert.Equal("0\n2\n4\n6\n8\n", observed);
    }

    [Theory]
    [InlineData("lint", "--fix")]
    [InlineData("format", "--write")]
    public void PublicFormattingGuides_ActualWritesNeedNoExperimentalFlagAndPreserveNamesAndComments(
        string command, string writeOption)
    {
        var path = "website/content/cli/lint.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith("module LintNames\n", StringComparison.Ordinal)).Source;
        var dirty = source.Replace("return value * 2", "return value * 2  ", StringComparison.Ordinal);
        Assert.NotEqual(source, dirty);
        var directory = Path.Combine(Path.GetTempPath(), "calor-published-format-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "hello.calr");
            File.WriteAllText(input, dirty);
            var check = CliTestHarness.RunCli(directory, command, input, "--check", "--format", "json");
            Assert.Equal(1, check.ExitCode);
            Assert.Equal(dirty, File.ReadAllText(input));
            var fixedFile = CliTestHarness.RunCli(directory,
                new Dictionary<string, string> { ["CALOR_EXPERIMENTAL_FORMAT_WRITE"] = "0" },
                command, input, writeOption);
            Assert.True(fixedFile.ExitCode == 0, fixedFile.StdOut + fixedFile.StdErr);
            Assert.Equal(source, File.ReadAllText(input));
            var clean = CliTestHarness.RunCli(directory, command, input, "--check", "--format", "json");
            Assert.True(clean.ExitCode == 0, clean.StdOut + clean.StdErr);
            using var diagnostics = JsonDocument.Parse(clean.StdOut);
            Assert.Empty(diagnostics.RootElement.GetProperty("diagnostics").EnumerateArray());
            var compiled = Program.Compile(File.ReadAllText(input), path);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal("42", InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + """

                public static class Probe
                {
                    public static object? Run() => GlobalModule.f001(21);
                }
                """));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PublicLintGuide_ReportsSuppressedSemanticFailureWithoutRewritingSource()
    {
        var path = "website/content/cli/lint.mdx";
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith("module LintNames\n", StringComparison.Ordinal)).Source
            .Replace("return value * 2", "return missing  ", StringComparison.Ordinal);
        var directory = Path.Combine(Path.GetTempPath(), "calor-published-lint-refusal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "hello.calr");
            File.WriteAllText(input, source);
            var refused = CliTestHarness.RunCli(directory, "lint", input, "--fix", "--format", "json");
            Assert.Equal(1, refused.ExitCode);
            Assert.Equal(source, File.ReadAllText(input));
            using var diagnostics = JsonDocument.Parse(refused.StdOut);
            Assert.Contains(diagnostics.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == DiagnosticCode.FormatConservativeFallback);
            Assert.Contains("fix suppressed", refused.StdErr);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PublicIdsGuide_ActualCommandsPreserveSourceAndPersistedIdentity()
    {
        const string path = "website/content/cli/ids.mdx";
        var source = Assert.Single(ExemplarCompileChecker.ExtractCompletePrograms(
            File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))).Source;
        var directory = Path.Combine(Path.GetTempPath(), "calor-published-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            var input = Path.Combine(directory, "src", "Calculator.calr");
            File.WriteAllText(input, source);
            var original = File.ReadAllBytes(input);
            var sync = CliTestHarness.RunCli(directory, "ids", "sync", ".", "--register", "src/Calculator.calr");
            Assert.True(sync.ExitCode == 0, sync.StdOut + sync.StdErr);
            Assert.True(File.Exists(Path.Combine(directory, ".calor", "ids.json")));
            var check = CliTestHarness.RunCli(directory, "ids", "check", "src/Calculator.calr", "--format", "json");
            Assert.True(check.ExitCode == 0, check.StdOut + check.StdErr);
            using var diagnostics = JsonDocument.Parse(check.StdOut);
            Assert.Empty(diagnostics.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(2, diagnostics.RootElement.GetProperty("data").GetProperty("totalIds").GetInt32());
            var export = CliTestHarness.RunCli(directory, "ids", "index", "src/Calculator.calr",
                "--output", "declaration-index.json");
            Assert.True(export.ExitCode == 0, export.StdOut + export.StdErr);
            var report = File.ReadAllText(Path.Combine(directory, "declaration-index.json"));
            using var index = JsonDocument.Parse(report);
            Assert.Equal(new[] { "Calculator", "Double" },
                index.RootElement.EnumerateArray().Select(entry => entry.GetProperty("name").GetString()).Order());
            Assert.All(index.RootElement.EnumerateArray(),
                entry => Assert.False(string.IsNullOrEmpty(entry.GetProperty("id").GetString())));
            sync = CliTestHarness.RunCli(directory, "ids", "sync", ".");
            Assert.True(sync.ExitCode == 0, sync.StdOut + sync.StdErr);
            export = CliTestHarness.RunCli(directory, "ids", "index", "src/Calculator.calr",
                "--output", "declaration-index.json");
            Assert.True(export.ExitCode == 0, export.StdOut + export.StdErr);
            Assert.Equal(report, File.ReadAllText(Path.Combine(directory, "declaration-index.json")));
            var refused = CliTestHarness.RunCli(directory, "ids", "index", "src/Calculator.calr",
                "--output", "src/Calculator.calr");
            Assert.Equal(1, refused.ExitCode);
            Assert.Contains("must not overwrite", refused.StdErr);
            Assert.Equal(original, File.ReadAllBytes(input));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("philosophy/index.mdx", "DivisionObligations", "9, 2", "4", "4")]
    [InlineData("philosophy/index.mdx", "DivisionObligations", "-9, 2", "Ensures", "-4")]
    [InlineData("philosophy/index.mdx", "DivisionObligations", "9, 0", "Requires", "!DivideByZeroException")]
    [InlineData("philosophy/index.mdx", "DivisionObligations", "int.MinValue, -1", "!OverflowException", "!OverflowException")]
    [InlineData("philosophy/design-principles.mdx", "DesignContracts", "9, 2", "4", "4")]
    [InlineData("philosophy/design-principles.mdx", "DesignContracts", "-9, 2", "Requires", "-4")]
    [InlineData("philosophy/design-principles.mdx", "DesignContracts", "9, -2", "Requires", "-4")]
    [InlineData("philosophy/design-principles.mdx", "DesignContracts", "9, 0", "Requires", "!DivideByZeroException")]
    public void PublicPhilosophy_ObservesActualPreconditionPostconditionAndArithmeticBoundaries(
        string page, string module, string arguments, string enabled, string disabled)
    {
        var path = "website/content/" + page;
        var source = ExemplarCompileChecker.ExtractCompletePrograms(
                File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))
            .Single(program => program.Source.StartsWith("module " + module + "\n", StringComparison.Ordinal)).Source;
        foreach (var (mode, expected) in new[] { (ContractMode.Debug, enabled), (ContractMode.Off, disabled) })
        {
            var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = mode });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

                public static class Probe
                {
                    public static object? Run()
                    {
                        try { return GlobalModule.Divide({{arguments}}); }
                        catch (Calor.Runtime.ContractViolationException failure) { return failure.Kind.ToString(); }
                    }
                }
                """);
            Assert.Equal(expected, observed);
        }
    }

    [Theory]
    [InlineData("9, 2", "4", "4")]
    [InlineData("9, -2", "-4", "-4")]
    [InlineData("-9, 2", "-4", "-4")]
    [InlineData("9, 0", "!ContractViolationException", "!DivideByZeroException")]
    [InlineData("int.MinValue, -1", "!OverflowException", "!OverflowException")]
    public void PublicGeminiGuide_PreservesDivisionContractAndCheckedOverflow(
        string arguments, string enabled, string disabled)
    {
        var path = "website/content/getting-started/gemini-integration.mdx";
        var source = Assert.Single(ExemplarCompileChecker.ExtractCompletePrograms(
            File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)))).Source;
        foreach (var (mode, expected) in new[] { (ContractMode.Debug, enabled), (ContractMode.Off, disabled) })
        {
            var compiled = Program.Compile(source, path, new CompilationOptions { ContractMode = mode });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + $$"""

                public static class Probe
                {
                    public static object? Run() => GlobalModule.Divide({{arguments}});
                }
                """);
            Assert.Equal(expected, observed);
        }
    }

    [Theory]
    [InlineData("getting-started/index.mdx")]
    [InlineData("getting-started/installation.mdx")]
    public void PublicGettingStarted_ExecutesPublishedEntryPointWithNormalEnforcement(string page)
    {
        var path = "website/content/" + page;
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path));
        var source = Assert.Single(ExemplarCompileChecker.ExtractCompletePrograms(content)).Source;
        var directory = Path.Combine(Path.GetTempPath(), "calor-published-start-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "Program.calr");
            File.WriteAllText(input, source);
            var run = CliTestHarness.RunCli(directory, "run", input);
            Assert.True(run.ExitCode == 0, run.StdOut + run.StdErr);
            Assert.EndsWith("Hello from Calor!\n", run.StdOut.Replace("\r\n", "\n"));
            Assert.DoesNotContain("Hello, World!", run.StdOut);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PublicCompileGuide_UsesActualRootInvocationDiagnosticsAndNativeHelp()
    {
        const string path = "website/content/cli/compile.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)).Replace("\r\n", "\n");
        var positive = Assert.Single(ExemplarCompileChecker.ExtractCompletePrograms(content)).Source;
        var negative = System.Text.RegularExpressions.Regex.Match(content,
            @"```calor expect=Calor0200\n(?<body>.*?)\n```",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(negative.Success);
        var directory = Path.Combine(Path.GetTempPath(), "calor-published-compile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "Calculator.calr");
            var output = Path.Combine(directory, "Calculator.g.cs");
            File.WriteAllText(input, negative.Groups["body"].Value + "\n");
            var refused = CliTestHarness.RunCli(directory, "-i", input, "-o", output);
            Assert.Equal(1, refused.ExitCode);
            Assert.Contains("Calculator.calr:4:12 Calor0200 Undefined variable 'x'", refused.StdErr);
            Assert.False(File.Exists(output));

            input = Path.Combine(directory, "MyModule.calr");
            output = Path.Combine(directory, "MyModule.g.cs");
            File.WriteAllText(input, positive);
            var accepted = CliTestHarness.RunCli(directory, "-i", input, "-o", output, "--format", "json");
            Assert.True(accepted.ExitCode == 0, accepted.StdOut + accepted.StdErr);
            using var diagnostics = JsonDocument.Parse(accepted.StdOut);
            Assert.Empty(diagnostics.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("42", InteropScope.RefOutImplementationTests.Run(File.ReadAllText(output) + """

                public static class Probe
                {
                    public static object? Run() => GlobalModule.Increment(41);
                }
                """));
            var help = CliTestHarness.RunCli(directory, "--help");
            Assert.Equal(0, help.ExitCode);
            Assert.Contains("/// breaking", help.StdOut);
            Assert.DoesNotContain("§BREAKING", help.StdOut);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(0, "GlobalModule.SafeDivide(6, 2)", "Functions.SafeDivide(6, 2)", "3", "3", "3")]
    [InlineData(0, "GlobalModule.SafeDivide(6, 0)", "Functions.SafeDivide(6, 0)",
        "!ContractViolationException", "!DivideByZeroException", "!ArgumentException")]
    [InlineData(0, "GlobalModule.SafeDivide(int.MinValue, -1)", "Functions.SafeDivide(int.MinValue, -1)",
        "!OverflowException", "!OverflowException", "!OverflowException")]
    [InlineData(1, "GlobalModule.NormalizeScore(0, 100)", "Original.NormalizeScore(0, 100)", "0", "0", "0")]
    [InlineData(1, "GlobalModule.NormalizeScore(100, 100)", "Original.NormalizeScore(100, 100)", "100", "100", "100")]
    [InlineData(1, "GlobalModule.NormalizeScore(75, 100)", "Original.NormalizeScore(75, 100)", "75", "75", "75")]
    [InlineData(1, "GlobalModule.NormalizeScore(-1, 100)", "Original.NormalizeScore(-1, 100)",
        "!ContractViolationException", "-1", "!ArgumentException")]
    [InlineData(1, "GlobalModule.NormalizeScore(1, 0)", "Original.NormalizeScore(1, 0)",
        "!ContractViolationException", "!DivideByZeroException", "!ArgumentException")]
    [InlineData(1, "GlobalModule.NormalizeScore(101, 100)", "Original.NormalizeScore(101, 100)",
        "!ContractViolationException", "101", "!ArgumentException")]
    [InlineData(1, "GlobalModule.NormalizeScore(30000000, 30000000)", "Original.NormalizeScore(30000000, 30000000)",
        "!OverflowException", "!OverflowException", "-43")]
    [InlineData(1, "GlobalModule.NormalizeScore(int.MaxValue, int.MaxValue)", "Original.NormalizeScore(int.MaxValue, int.MaxValue)",
        "!OverflowException", "!OverflowException", "0")]
    public void PublicTaskCompletion_ObservesNativeAndOriginalCSharpBoundaries(
        int index, string nativeCall, string csharpCall, string enabled, string disabled, string original)
    {
        const string path = "website/content/benchmarking/metrics/task-completion.mdx";
        var content = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), path)).Replace("\r\n", "\n");
        var programs = ExemplarCompileChecker.ExtractCompletePrograms(content);
        Assert.Equal(2, programs.Count);
        var fragments = System.Text.RegularExpressions.Regex.Matches(content,
            @"```csharp\n(?<body>.*?)\n```", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.Equal(2, fragments.Count);
        foreach (var mode in new[] { ContractMode.Debug, ContractMode.Off })
        {
            var compiled = Program.Compile(programs[index].Source, path, new CompilationOptions { ContractMode = mode });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(mode == ContractMode.Debug ? enabled : disabled, Observe(compiled.GeneratedCode, nativeCall));
        }
        var csharp = fragments[index].Groups["body"].Value;
        if (index == 1) csharp = "public static class Original {\n" + csharp + "\n}";
        Assert.Equal(original, Observe("global using System;\n" + csharp, csharpCall));

        static string Observe(string source, string expression) =>
            InteropScope.RefOutImplementationTests.Run(source + $$"""

                public static class Probe
                {
                    public static object? Run() => {{expression}};
                }
                """);
    }

    [Fact]
    public async Task PublicTaskCompletion_DistinguishesNeutralDefinitionsFromTheFailedPublishedRun()
    {
        var root = CliTestHarness.FindRepoRoot();
        var manifest = await Calor.Evaluation.LlmTasks.LlmTaskManifest.LoadAsync(
            Path.Combine(root, "tests/Calor.Evaluation/Tasks/task-manifest-neutral.json"));
        Assert.Equal(50, manifest.Tasks.Count);
        Assert.All(manifest.Tasks, task =>
        {
            Assert.True(task.UsesNeutralPrompt);
            Assert.Equal(task.GetPrompt("calor"), task.GetPrompt("csharp"));
            Assert.Equal(0.4, task.Scoring.Compilation, 10);
            Assert.Equal(0.6, task.Scoring.TestCases, 10);
            Assert.Equal(0, task.Scoring.Contracts);
            Assert.NotEmpty(task.TestCases);
        });
        Assert.Equal(new[] { "basic-algorithms:15", "data-structures:10", "logic:15", "safety:10" },
            manifest.Tasks.GroupBy(task => task.Category).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}:{group.Count()}"));

        using var published = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "website/public/data/llm-results.json")));
        var record = published.RootElement;
        Assert.Equal("claude", record.GetProperty("provider").GetString());
        Assert.False(record.GetProperty("isDryRun").GetBoolean());
        Assert.Equal(0, record.GetProperty("totalCost").GetDecimal());
        var results = record.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(50, results.Length);
        foreach (var result in results)
        {
            var task = result.GetProperty("task");
            Assert.False(task.GetProperty("usesNeutralPrompt").GetBoolean());
            Assert.Equal(JsonValueKind.Null, task.GetProperty("prompt").ValueKind);
            Assert.Equal(0.3, task.GetProperty("scoring").GetProperty("compilation").GetDouble(), 10);
            var contractTask = task.GetProperty("category").GetString() == "contracts";
            Assert.Equal(contractTask ? 0.4 : 0.5, task.GetProperty("scoring").GetProperty("testCases").GetDouble(), 10);
            Assert.Equal(contractTask ? 0.3 : 0.2, task.GetProperty("scoring").GetProperty("contracts").GetDouble(), 10);
            foreach (var language in new[] { "calorResult", "cSharpResult" })
            {
                var response = result.GetProperty(language);
                Assert.Equal("", response.GetProperty("generatedCode").GetString());
                Assert.False(response.GetProperty("compilationSuccess").GetBoolean());
                Assert.Empty(response.GetProperty("testResults").EnumerateArray());
                Assert.Equal(0, response.GetProperty("score").GetDouble());
                Assert.Equal(JsonValueKind.Null, response.GetProperty("generationMetadata").GetProperty("model").ValueKind);
                Assert.Contains(response.GetProperty("compilationErrors").EnumerateArray(),
                    error => error.GetString()!.Contains("Unauthorized", StringComparison.Ordinal));
            }
        }
        Assert.Equal(new[] { "basic-algorithms:15", "contracts:10", "data-structures:10", "logic:15" },
            results.GroupBy(result => result.GetProperty("task").GetProperty("category").GetString())
                .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key}:{group.Count()}"));
        var summary = record.GetProperty("summary");
        Assert.Equal(50, summary.GetProperty("totalTasks").GetInt32());
        Assert.Equal(0, summary.GetProperty("averageCalorScore").GetDouble());
        Assert.Equal(0, summary.GetProperty("averageCSharpScore").GetDouble());
        Assert.Equal(1, summary.GetProperty("overallAdvantageRatio").GetDouble());
        Assert.Equal(new[] { "basic-algorithms:15", "contracts:10", "data-structures:10", "logic:15" },
            summary.GetProperty("byCategory").EnumerateObject().OrderBy(category => category.Name, StringComparer.Ordinal)
                .Select(category => $"{category.Name}:{category.Value.GetProperty("taskCount").GetInt32()}"));
    }

    [Fact]
    public void PublicInheritanceInterfaceCall_CompilesOriginalSourceWithoutPolicyOrRowChanges()
    {
        const string source = """
            module InterfaceDispatchLimit
              interface IQuiet
                func Read () → int
                  !

              func ReadThroughInterface (IQuiet:value) → int
                !
                return value.Read()
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.UnknownExternalCall or DiagnosticCode.AssumedEffects);
        var observed = InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + """

            public sealed class Quiet : IQuiet { public int Read() => 9; }
            public static class Probe
            {
                public static object? Run() => GlobalModule.ReadThroughInterface(new Quiet());
            }
            """);
        Assert.Equal("9", observed);
    }

    [Theory]
    [InlineData("module Good\n  func Value () → int\n    return 1\n", false)]
    [InlineData("// Header\nmodule Good\n  func Value () → int\n    return 1\n", false)]
    [InlineData("// Header\nfunc Value () → int\n  return 1\n", true)]
    public void NativeReferenceExtraction_PreservesCommentsContextAndDocumentLine(string source, bool wrapped)
    {
        var doc = new DocFile("reference.md", "# Example\n```calor\n" + source + "```\n");
        var program = Assert.Single(ExemplarCompileChecker.ExtractAgentTaskPrograms(doc.Content));
        Assert.Equal(wrapped, program.Wrapped);
        Assert.Equal(3, program.FirstContentLine);
        if (wrapped)
            Assert.StartsWith("module AgentReference0\n  // Header\n", program.Source);
        else
            Assert.Equal(source.TrimEnd(), program.Source);
        Assert.Empty(ExemplarCompileChecker.CheckAgentTaskReference(doc));
    }

    [Theory]
    [InlineData("module Bad\n  func Value () → int\n    return (\n")]
    [InlineData("module Bad\n  func Value () → int\n    return \"not an int\"\n")]
    [InlineData("func Value () → int\n  return 1\n")]
    [InlineData("This is not a complete module.")]
    [InlineData("module Injected\n  func Value () → int\n    return 1\n```\nThis is not Calor\n```calor\nmodule Hidden\n  func Value () → int\n    return 1\n")]
    public void TeachingRegeneration_RejectsInvalidProgramsBeforeWritingAnyMirror(string invalid)
    {
        var root = NewTempDir();
        try
        {
            var outputs = ExemplarCompileChecker.GenerateTeachingResources(
                TeachingSource(ValidTeachingProgram, ValidTeachingProgram));
            var previous = new Dictionary<string, byte[]?>();
            foreach (var (relative, _) in outputs)
            {
                var path = Path.Combine(root, relative);
                var bytes = previous.Count % 2 == 0 ? Encoding.UTF8.GetBytes("retain " + relative) : null;
                previous.Add(path, bytes);
                if (bytes != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, bytes);
                }
            }
            WriteTeachingSource(root, TeachingSource(ValidTeachingProgram, invalid));
            var diagnostics = new List<Diagnostic>();

            Assert.False(DocDriftChecker.RegenerateTeachingResources(root, diagnostics));
            Assert.NotEmpty(diagnostics);
            Assert.All(diagnostics, diagnostic => Assert.Contains(diagnostic.Code,
                new[] { DiagnosticCode.DocDriftExampleCompileError, DiagnosticCode.DocDriftMissingInput }));
            foreach (var (path, bytes) in previous)
            {
                if (bytes == null)
                    Assert.False(File.Exists(path), path);
                else
                    Assert.Equal(bytes, File.ReadAllBytes(path));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TeachingRegeneration_ValidatesWritesIdempotentlyAndRepairsStaleMirrors()
    {
        var root = NewTempDir();
        try
        {
            var source = TeachingSource(ValidTeachingProgram);
            WriteTeachingSource(root, source);
            var outputs = ExemplarCompileChecker.GenerateTeachingResources(source);
            var diagnostics = new List<Diagnostic>();
            Assert.True(DocDriftChecker.RegenerateTeachingResources(root, diagnostics), string.Join("\n", diagnostics));
            Assert.Empty(diagnostics);
            var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            foreach (var (relative, expected) in outputs)
            {
                var path = Path.Combine(root, relative);
                Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(path));
                File.SetLastWriteTimeUtc(path, timestamp);
            }

            Assert.True(DocDriftChecker.RegenerateTeachingResources(root, diagnostics));
            Assert.Empty(diagnostics);
            Assert.All(outputs.Keys, relative =>
                Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(root, relative))));

            var stale = "src/Calor.Compiler/Resources/Teaching/Generated/tags.json";
            File.WriteAllText(Path.Combine(root, stale), "stale");
            var inputs = DocDriftChecker.LoadTeachingFromRepository(root, diagnostics);
            Assert.Contains(DocDriftChecker.Check(inputs), diagnostic =>
                diagnostic.Code == DiagnosticCode.DocDriftMirrorOutOfSync && diagnostic.FilePath == stale);
            Assert.True(DocDriftChecker.RegenerateTeachingResources(root, diagnostics));
            Assert.Empty(diagnostics);
            Assert.Equal(outputs[stale], File.ReadAllText(Path.Combine(root, stale)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void WriteTeachingSource(string root, string source)
    {
        var path = Path.Combine(root, ExemplarCompileChecker.TeachingSourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    private static string TeachingSource(params string[] sources)
    {
        var ids = sources.Select((_, index) => "example" + index).ToArray();
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = "test",
            introduction = "",
            overview = "",
            agentInstructions = "Use Calor.\n",
            constructs = new[]
            {
                new
                {
                    id = "function", csharpConstruct = "Function", calorSyntax = "func Value () → int",
                    description = "Return a value.", csharpEquivalent = "int Value() => 1;",
                    keywords = new[] { "func" }, programs = ids
                }
            },
            programs = sources.Select((source, index) => new { id = ids[index], title = ids[index], calor = source }),
            tags = new Dictionary<string, object>(),
            primer = new { introduction = "", programs = ids, mistakes = "" },
            catalogs = new Dictionary<string, object>
            {
                ["effects.json"] = new { effects = EffectCodes.DocumentedCompactCodes.Select(code => new { code }) }
            }
        });
    }

    [Theory]
    [InlineData("// Header\n")]
    [InlineData("/// Header\n")]
    [InlineData("\n// Header\n\n")]
    public void NativeModuleAfterLeadingComments_IsChecked(string prefix)
    {
        var source = prefix + "module Good\n  func Value () → int\n    return 1\n";
        var doc = new DocFile("docs/example.md", $"```calor\n{source}```\n");
        Assert.Empty(DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc])));

        var invalid = doc with { Content = doc.Content.Replace("return 1", "return (", StringComparison.Ordinal) };
        Assert.Contains(DocDriftChecker.Check(BaseInputs(parseExampleDocs: [invalid])),
            diagnostic => diagnostic.Code == DiagnosticCode.DocDriftExampleParseError);
    }

    private static DocDriftInputs BaseInputs(
        DocFile[]? keywordDocs = null,
        DocFile[]? diagnosticCodeDocs = null,
        DocFile[]? parseExampleDocs = null,
        DocFile? effectsReferenceDoc = null,
        DocFile[]? effectDocsForwardOnly = null,
        DocFile? cliCodesDoc = null,
        DocFile[]? versionScanDocs = null,
        MirrorDoc[]? mirrorDocs = null,
        DocFile[]? semanticsVersionDocs = null)
    {
        return new DocDriftInputs
        {
            Version = "0.9.9",
            LexerKeywords = ["M", "F", "EACH", "/C", "IV", "Pf", "PP", "/PP"],
            DiagnosticCodes = ["Calor0001", "Calor0830", "Calor1300", "Calor1310", "Calor1320"],
            KnownEffectCodes = ["cw", "mut", "mut:col", "fw"],
            DocumentedEffectCodes = ["cw", "mut", "mut:col"],
            KeywordDocs = keywordDocs ?? [],
            DiagnosticCodeDocs = diagnosticCodeDocs ?? [],
            ParseExampleDocs = parseExampleDocs ?? [],
            EffectsReferenceDoc = effectsReferenceDoc,
            EffectDocsForwardOnly = effectDocsForwardOnly ?? [],
            CliCodesDoc = cliCodesDoc,
            VersionScanDocs = versionScanDocs ?? [],
            MirrorDocs = mirrorDocs ?? [],
            SemanticsVersionDocs = semanticsVersionDocs ?? [],
        };
    }

    [Theory]
    [InlineData("**Semantics Version: 1.0.0**")]
    [InlineData("Version: 1.0.0")]
    [InlineData("Semantics Version: 0.9.9")]
    [InlineData("## Semantics Version: invalid")]
    [InlineData("**Current Version:** 1.0.0")]
    [InlineData("**Semantics Version:** `1.0.0`")]
    [InlineData("Semantics Version:")]
    [InlineData("**Version:**")]
    [InlineData("__Current Version:__ 1.0.0")]
    [InlineData("__Semantics Version:__")]
    [InlineData("Semantics Version: 2.0.0_")]
    public void StaleNormativeSemanticsVersion_FailsIndependentlyOfPackageVersion(string claim)
    {
        var finding = Assert.Single(DocDriftChecker.Check(BaseInputs(
            semanticsVersionDocs: [new DocFile("docs/semantics/core.md", claim)])));
        Assert.Equal(DiagnosticCode.DocDriftHardcodedVersion, finding.Code);
        Assert.Contains($"implemented semantics '{SemanticsVersion.VersionString}'", finding.Message);
        Assert.Contains("not the compiler package version", finding.Message);
    }

    [Fact]
    public void CurrentSemanticsVersionAndHistoricalCompatibilityExample_Pass()
    {
        var doc = new DocFile("docs/semantics/versioning.md",
            $"**Semantics Version: {SemanticsVersion.VersionString}**\nEarlier §SEMVER{{1.0.0}} modules are rejected.\n");
        Assert.Empty(DocDriftChecker.Check(BaseInputs(semanticsVersionDocs: [doc])));
    }

    [Fact]
    public void NormativeOverview_CannotDeleteItsCurrentVersionClaim()
    {
        var finding = Assert.Single(DocDriftChecker.Check(BaseInputs(
            semanticsVersionDocs: [new DocFile("docs/semantics/README.md", "# Semantics\n")])));
        Assert.Equal(DiagnosticCode.DocDriftHardcodedVersion, finding.Code);
    }

    private const string InventorySchema = """
        {"schemaVersion":1,"nodes":[
          {"name":"SecondNode","source":"Statements.cs"},
          {"name":"FirstNode","source":"Expressions.cs"}
        ]}
        """;

    [Fact]
    public void MethodFragment_IsNotMisclassifiedAsModuleExample()
    {
        var doc = new DocFile("docs/semantics/inheritance.md",
            "```calor\n§MT{Example:pub} () -> i32\n  §R 1\n```\n");
        Assert.Empty(DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc])));
    }

    [Fact]
    public void InventoryGeneration_DerivesFullSortedContentsAndCountFromSchema()
    {
        var generated = DocDriftChecker.GenerateAstInventory(InventorySchema);
        Assert.Contains("**2** node types", generated);
        Assert.Contains("`FirstNode`", generated);
        Assert.Contains("`SecondNode`", generated);
        Assert.True(generated.IndexOf("`FirstNode`", StringComparison.Ordinal)
            < generated.IndexOf("`SecondNode`", StringComparison.Ordinal));
        Assert.Equal(generated, DocDriftChecker.GenerateAstInventory(InventorySchema));
    }

    [Theory]
    [InlineData("count")]
    [InlineData("node")]
    public void InventoryDrift_RejectsCountAndContentMutations(string mutation)
    {
        var expected = DocDriftChecker.GenerateAstInventory(InventorySchema);
        var changed = mutation == "count"
            ? expected.Replace("**2**", "**134**", StringComparison.Ordinal)
            : expected.Replace("FirstNode", "WrongNode", StringComparison.Ordinal);
        var findings = DocDriftChecker.Check(BaseInputs(mirrorDocs:
            [new MirrorDoc(DocDriftChecker.AstInventoryRelativePath, "eng/ast-schema.json", changed, expected)]));
        Assert.Equal(DiagnosticCode.DocDriftMirrorOutOfSync, Assert.Single(findings).Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":99,\"nodes\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"nodes\":[{\"name\":\"X\",\"source\":\"A.cs\"},{\"name\":\"X\",\"source\":\"B.cs\"}]}")]
    public void MalformedInventorySource_IsNotSilentlyRegenerated(string schema)
    {
        var root = CreateFakeRepo();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "eng"));
            Directory.CreateDirectory(Path.Combine(root, "docs", "semantics"));
            File.WriteAllText(Path.Combine(root, "eng", "ast-schema.json"), schema);
            var inventory = Path.Combine(root, DocDriftChecker.AstInventoryRelativePath);
            File.WriteAllText(inventory, "preserve existing document");
            var errors = new List<Diagnostic>();
            Assert.False(DocDriftChecker.RegenerateAstInventory(root, errors));
            Assert.Equal("preserve existing document", File.ReadAllText(inventory));
            Assert.Equal(DiagnosticCode.DocDriftMissingInput, Assert.Single(errors).Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InventoryRegeneration_IsIdempotentAndRestoresSchemaDerivedPage()
    {
        var root = CreateFakeRepo();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "eng"));
            File.WriteAllText(Path.Combine(root, "eng", "ast-schema.json"), InventorySchema);
            var errors = new List<Diagnostic>();
            Assert.True(DocDriftChecker.RegenerateAstInventory(root, errors));
            Assert.Empty(errors);
            var path = Path.Combine(root, DocDriftChecker.AstInventoryRelativePath);
            var timestamp = File.GetLastWriteTimeUtc(path);
            Assert.True(DocDriftChecker.RegenerateAstInventory(root, errors));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
            Assert.Equal(DocDriftChecker.GenerateAstInventory(InventorySchema), File.ReadAllText(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("core.md")]
    [InlineData("dotnet-backend.md")]
    public void RepositoryScan_RejectsStaleNormativeVersionKeywordAndExample(string name)
    {
        var root = CreateFakeRepo();
        Directory.CreateDirectory(Path.Combine(root, "docs", "semantics"));
        File.WriteAllText(Path.Combine(root, "docs", "semantics", name), """
            Semantics Version: 1.0.0
            Use §BOGUS.
            ```calor
            §M{BadExample}
              §F{Probe:pub} () -> i32
                §R (+ 1)
            ```
            """);
        var findings = CheckFakeRepo(root).Where(finding =>
            finding.FilePath == Path.Combine("docs", "semantics", name)).ToArray();
        Assert.Contains(findings, finding => finding.Code == DiagnosticCode.DocDriftHardcodedVersion);
        Assert.Contains(findings, finding => finding.Code == DiagnosticCode.DocDriftUnknownKeyword);
        Assert.Contains(findings, finding => finding.Code == DiagnosticCode.DocDriftExampleParseError);
    }

    [Fact]
    public void RepositoryScan_RejectsStaleInventoryAgainstLoadedSchema()
    {
        var root = CreateFakeRepo();
        Directory.CreateDirectory(Path.Combine(root, "eng"));
        Directory.CreateDirectory(Path.Combine(root, "docs", "semantics"));
        File.WriteAllText(Path.Combine(root, "eng", "ast-schema.json"), InventorySchema);
        File.WriteAllText(Path.Combine(root, DocDriftChecker.AstInventoryRelativePath),
            DocDriftChecker.GenerateAstInventory(InventorySchema).Replace("**2**", "**134**", StringComparison.Ordinal));
        Assert.Contains(CheckFakeRepo(root), finding =>
            finding.FilePath == DocDriftChecker.AstInventoryRelativePath
            && finding.Code == DiagnosticCode.DocDriftMirrorOutOfSync);
    }

    [Fact]
    public void RepositoryScan_ExcludesDatedAndNestedPlanningRecordsFromNormativeChecks()
    {
        var root = CreateFakeRepo();
        var directory = Path.Combine(root, "docs", "semantics");
        Directory.CreateDirectory(Path.Combine(directory, "plans"));
        const string historical = "Semantics Version: 1.0.0\nHistorical §BOGUS\n";
        File.WriteAllText(Path.Combine(directory, "2026-01-01-plan.md"), historical);
        File.WriteAllText(Path.Combine(directory, "plans", "future.md"), historical);
        Assert.DoesNotContain(CheckFakeRepo(root), finding =>
            finding.Code is DiagnosticCode.DocDriftUnknownKeyword or DiagnosticCode.DocDriftHardcodedVersion);
    }

    [Fact]
    public void RepositoryScan_MissingNormativeOverviewIsAnError()
    {
        var root = CreateFakeRepo();
        Directory.CreateDirectory(Path.Combine(root, "docs", "semantics"));
        Assert.Contains(CheckFakeRepo(root), finding =>
            finding.Code == DiagnosticCode.DocDriftMissingInput
            && finding.FilePath == Path.Combine("docs", "semantics", "README.md"));
    }

    // --- Mirror-doc drift (AGENTS.md single-sourced from CLAUDE.md, #708) ---

    private const string SampleClaude =
        "# CLAUDE.md — Calor Compiler\n\nBuild with `dotnet build`. Use `§F{Name:pub}`.\n";

    [Fact]
    public void AgentsMdTransform_SwapsTitle_AddsBanner_IsIdempotent()
    {
        var once = DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude);
        Assert.StartsWith("# AGENTS.md — Calor Compiler\n", once);
        Assert.Contains("Generated from CLAUDE.md", once);
        Assert.DoesNotContain("# CLAUDE.md — Calor Compiler", once);
        // Body is preserved verbatim after the swapped head.
        Assert.Contains("Build with `dotnet build`. Use `§F{Name:pub}`.", once);
        // Regenerating from CLAUDE.md yields the same bytes (deterministic).
        Assert.Equal(once, DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude));
    }

    [Fact]
    public void MirrorInSync_PassesClean()
    {
        var expected = DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude);
        var mirror = new MirrorDoc("AGENTS.md", "CLAUDE.md", expected, expected);
        Assert.Empty(DocDriftChecker.Check(BaseInputs(mirrorDocs: [mirror])));
    }

    [Fact]
    public void MirrorOutOfSync_IsDetected()
    {
        var expected = DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude);
        var mirror = new MirrorDoc("AGENTS.md", "CLAUDE.md", expected + "hand edit\n", expected);
        var finding = Assert.Single(DocDriftChecker.Check(BaseInputs(mirrorDocs: [mirror])));
        Assert.Equal(DiagnosticCode.DocDriftMirrorOutOfSync, finding.Code);
        Assert.Contains("out of sync", finding.Message);
    }

    [Fact]
    public void MirrorMissing_IsDetected()
    {
        var expected = DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude);
        var mirror = new MirrorDoc("AGENTS.md", "CLAUDE.md", Actual: null, expected);
        var finding = Assert.Single(DocDriftChecker.Check(BaseInputs(mirrorDocs: [mirror])));
        Assert.Equal(DiagnosticCode.DocDriftMirrorOutOfSync, finding.Code);
        Assert.Contains("is missing", finding.Message);
    }

    [Fact]
    public void AgentsMdTransform_ThrowsOnAnchorMismatch()
    {
        Assert.False(DocDriftChecker.TryAgentsMdFromClaudeMd("# Something Else\n\nbody\n", out _));
        Assert.Throws<InvalidOperationException>(() =>
            DocDriftChecker.AgentsMdFromClaudeMd("# Something Else\n"));
    }

    // --- RegenerateAgentsMd file-IO path (--fix) ---

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "calor-mirror-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Regenerate_WritesWhenMissingOrStale_ThenIsIdempotent()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), SampleClaude);
            var agents = Path.Combine(dir, "AGENTS.md");

            // Missing → written.
            Assert.Equal(DocDriftChecker.MirrorRegenResult.Written, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.Equal(DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude), File.ReadAllText(agents));

            // Already in sync → no write (idempotent).
            var before = File.GetLastWriteTimeUtc(agents);
            Assert.Equal(DocDriftChecker.MirrorRegenResult.AlreadyInSync, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.Equal(before, File.GetLastWriteTimeUtc(agents));

            // Stale → rewritten.
            File.WriteAllText(agents, "hand edited\n");
            Assert.Equal(DocDriftChecker.MirrorRegenResult.Written, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.Equal(DocDriftChecker.AgentsMdFromClaudeMd(SampleClaude), File.ReadAllText(agents));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Regenerate_SourceMissing_DoesNotWrite()
    {
        var dir = NewTempDir();
        try
        {
            Assert.Equal(DocDriftChecker.MirrorRegenResult.SourceMissing, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.False(File.Exists(Path.Combine(dir, "AGENTS.md")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Regenerate_AnchorMismatch_DoesNotWrite()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), "# Renamed Title\n\nbody\n");
            Assert.Equal(DocDriftChecker.MirrorRegenResult.AnchorMismatch, DocDriftChecker.RegenerateAgentsMd(dir));
            Assert.False(File.Exists(Path.Combine(dir, "AGENTS.md")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // --- Keyword drift (the §FOREACH-vs-§EACH class) ---

    [Fact]
    public void UnknownKeywordIsDetected()
    {
        var doc = new DocFile("fake.md", "Use `§FOREACH{id}` to loop over collections.");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUnknownKeyword, finding.Code);
        Assert.Contains("§FOREACH", finding.Message);
        Assert.Equal("fake.md", finding.FilePath);
        Assert.Equal(1, finding.Span.Line);
    }

    [Fact]
    public void KnownKeywordsPass()
    {
        var doc = new DocFile("fake.md",
            "`§M{Name}` opens a module, `§EACH` iterates, `§/C` closes calls,\n" +
            "`§IV (expr)` declares an invariant, `§Pf` prints, `§PP{X}`...`§/PP{X}` wraps.");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void UnknownClosingKeywordIsDetected()
    {
        var doc = new DocFile("fake.md", "Close with `§/FOREACH`.");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUnknownKeyword, finding.Code);
        Assert.Contains("§/FOREACH", finding.Message);
    }

    // --- Diagnostic-code drift (the Calor0820-vs-0830 class) ---

    [Fact]
    public void UnknownDiagnosticCodeIsDetected()
    {
        var doc = new DocFile("fake.md", "Closer tags raise `Calor0820`.");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUnknownDiagnosticCode, finding.Code);
        Assert.Contains("Calor0820", finding.Message);
    }

    [Fact]
    public void KnownDiagnosticCodeAndPopulatedRangePass()
    {
        var doc = new DocFile("fake.md",
            "Closer tags raise `Calor0830`. Bands: Calor0001–0099 (lexer), `Calor1300`–`Calor1399` (CLI).");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void EmptyDiagnosticRangeIsDetected()
    {
        var doc = new DocFile("fake.md", "Reserved: Calor5000–5099 (future).");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftEmptyDiagnosticRange, finding.Code);
    }

    [Fact]
    public void RangeEndpointsAreNotRequiredToExistIndividually()
    {
        // Calor1399 does not exist as a concrete code, but the 1300 band is populated.
        var doc = new DocFile("fake.md", "CLI codes are Calor1300-Calor1399.");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc]));

        Assert.Empty(findings);
    }

    // --- Effect-code drift (the undocumented-mut class) ---

    private const string EffectsDocHeader = "# Effects\n\n## Effect Codes\n\n| Code | Effect |\n|:-----|:-------|\n";

    [Fact]
    public void UndocumentedEffectCodeIsDetected()
    {
        // 'mut' and 'mut:col' are implemented but missing from the table.
        var doc = new DocFile("effects.md", EffectsDocHeader + "| `cw` | Console write |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(DiagnosticCode.DocDriftUndocumentedEffectCode, f.Code));
        Assert.Contains(findings, f => f.Message.Contains("'mut'"));
        Assert.Contains(findings, f => f.Message.Contains("'mut:col'"));
    }

    [Fact]
    public void UnknownEffectCodeIsDetected()
    {
        var doc = new DocFile("effects.md", EffectsDocHeader +
            "| `cw` | Console write |\n| `mut` | Mutation |\n| `mut:col` | Collection mutation |\n| `zap` | Not real |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUnknownEffectCode, finding.Code);
        Assert.Contains("'zap'", finding.Message);
    }

    [Fact]
    public void CompleteEffectTablePasses()
    {
        var doc = new DocFile("effects.md", EffectsDocHeader +
            "| `cw` | Console write |\n| `mut` | Mutation |\n| `mut:col` | Collection mutation |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        Assert.Empty(findings);
    }

    [Fact]
    public void LegacyEffectCodesAreAcceptedButNotRequired()
    {
        // 'fw' is legacy: allowed in the table, but its absence is not drift.
        var doc = new DocFile("effects.md", EffectsDocHeader +
            "| `cw` | Console write |\n| `mut` | Mutation |\n| `mut:col` | Collection mutation |\n| `fw` | Legacy file write |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        Assert.Empty(findings);
    }

    [Fact]
    public void ForwardOnlyEffectDocNeedNotBeComplete()
    {
        var doc = new DocFile("index.md", EffectsDocHeader + "| `cw` | Console write |\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectDocsForwardOnly: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void MissingEffectCodesSectionIsDetected()
    {
        var doc = new DocFile("effects.md", "# Effects\n\nNo table here.\n");
        var findings = DocDriftChecker.Check(BaseInputs(effectsReferenceDoc: doc));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftMissingInput, finding.Code);
    }

    // --- CLI code-table completeness ---

    [Fact]
    public void MissingCliCodeTableEntryIsDetected()
    {
        var doc = new DocFile("structured-output.md",
            "| Code | Meaning |\n|:-----|:--------|\n| `Calor1300` | Lint finding |\n| `Calor1310` | Input not found |\n");
        var findings = DocDriftChecker.Check(BaseInputs(cliCodesDoc: doc));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftUndocumentedCliCode, finding.Code);
        Assert.Contains("Calor1320", finding.Message);
    }

    [Fact]
    public void CompleteCliCodeTablePasses()
    {
        var doc = new DocFile("structured-output.md",
            "| `Calor1300` | Lint |\n| `Calor1310` | Input |\n| `Calor1320` | Drift |\n");
        var findings = DocDriftChecker.Check(BaseInputs(cliCodesDoc: doc));

        Assert.Empty(findings);
    }

    // --- Meta-notation escapes: foreign fences and the suppression marker ---

    [Fact]
    public void FencedBlocksWithForeignInfoStringAreNotScanned()
    {
        var doc = new DocFile("fake.md",
            "Prose.\n\n```text\n§FOREACH is hypothetical here.\n```\n\n" +
            "```csharp\n// mentions Calor9876 in C# code\nvar s = \"§BOGUS\";\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc], diagnosticCodeDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void BareAndCalorFencesAreStillScanned()
    {
        var doc = new DocFile("fake.md",
            "```\n§FOREACH{x} items\n```\n\n```calor\n§FOREACH{y} items\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(DiagnosticCode.DocDriftUnknownKeyword, f.Code));
        Assert.Equal(new[] { 2, 6 }, findings.Select(f => f.Span.Line).OrderBy(l => l).ToArray());
    }

    [Fact]
    public void SuppressionMarkerCoversTheNextLineOnly()
    {
        var doc = new DocFile("fake.md",
            "<!-- drift:ignore -->\nAn explicit `§/X` raises an error.\nBut `§/Y` here is drift.\n");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Contains("§/Y", finding.Message);
        Assert.Equal(3, finding.Span.Line);
    }

    [Fact]
    public void SuppressionMarkerMayTrailThePrecedingLine()
    {
        var doc = new DocFile("fake.md",
            "| Ops | `(+ a b)` | <!-- drift:ignore -->\n| Block end | _(no `§/X` needed)_ |\n");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void SuppressionMarkerAppliesToDiagnosticCodeAndVersionScans()
    {
        var doc = new DocFile("fake.md",
            "<!-- drift:ignore -->\nA hypothetical `Calor9876` code.\n" +
            "<!-- drift:ignore -->\nHistoric example: version 0.9.9 shipped it.\n");
        var findings = DocDriftChecker.Check(BaseInputs(diagnosticCodeDocs: [doc], versionScanDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void UnknownKeywordMessageMentionsTheSuppressionMarker()
    {
        var doc = new DocFile("fake.md", "Use `§BOGUS` here.");
        var findings = DocDriftChecker.Check(BaseInputs(keywordDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Contains(DocDriftChecker.SuppressionMarker, finding.Message);
    }

    // --- Parse-the-examples (the rotted-snippet class) ---

    private const string FizzBuzzExample =
        "module FizzBuzz\n" +
        "  func Main () → void\n" +
        "    ! cw\n" +
        "    for i from 1 to 100 step 1\n" +
        "      if i % 15 == 0\n" +
        "        Console.WriteLine(\"FizzBuzz\")\n" +
        "      else if i % 3 == 0\n" +
        "        Console.WriteLine(\"Fizz\")\n" +
        "      else\n" +
        "        Console.WriteLine(i)\n";

    [Fact]
    public void CompleteProgramExampleThatParsesPasses()
    {
        var doc = new DocFile("fake.md", "Example:\n\n```calor\n" + FizzBuzzExample + "```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void RottedCompleteProgramExampleIsDetected()
    {
        // §FOREACH does not lex; a doc example using it has rotted.
        var doc = new DocFile("fake.md",
            "Example:\n\n```calor\nmodule Loops\n  func Sum (int[]:items) → void\n    §FOREACH{e1:x} items\n      Console.WriteLine(x)\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(DiagnosticCode.DocDriftExampleParseError, f.Code));
        var finding = findings[0];
        Assert.Equal("fake.md", finding.FilePath);
        Assert.Equal(6, finding.Span.Line); // §FOREACH sits on doc line 6
    }

    [Fact]
    public void CalorFragmentsNotStartingWithModuleAreSkipped()
    {
        // No §M on the first non-blank line => deliberate fragment, not parsed.
        var doc = new DocFile("fake.md",
            "```calor\n// Before (inconsistent)\n§FOREACH{e1:x} items\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void SuppressionMarkerBeforeFenceExemptsTheExample()
    {
        var doc = new DocFile("fake.md",
            "<!-- drift:ignore -->\n```calor\n§M{Hypothetical}\n  §FUTURE syntax sketch\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.Empty(findings);
    }

    [Fact]
    public void NonCalorFencesAreNotParseChecked()
    {
        var doc = new DocFile("fake.md",
            "```\n§M{Bare}\n  §FOREACH{e1:x} items\n```\n\n```text\n§M{Text}\n  §FOREACH{e2:x} items\n```\n");
        var findings = DocDriftChecker.Check(BaseInputs(parseExampleDocs: [doc]));

        Assert.Empty(findings);
    }

    // --- Hardcoded version (the stale-0.3.5 class) ---

    [Fact]
    public void HardcodedCurrentVersionIsDetected()
    {
        var doc = new DocFile("fake.md", "Install calor 0.9.9 from NuGet.");
        var findings = DocDriftChecker.Check(BaseInputs(versionScanDocs: [doc]));

        var finding = Assert.Single(findings);
        Assert.Equal(DiagnosticCode.DocDriftHardcodedVersion, finding.Code);
    }

    [Fact]
    public void OtherVersionStringsAreNotFlagged()
    {
        var doc = new DocFile("fake.md",
            "Historic release 0.3.5; unrelated 10.9.9 and 0.9.9.1 and v0.9.90 are fine.");
        var findings = DocDriftChecker.Check(BaseInputs(versionScanDocs: [doc]));

        Assert.Empty(findings);
    }

    // --- Ground truth wiring: the real registries are enumerable ---

    [Fact]
    public void LexerExposesKeywordsIncludingSpecialForms()
    {
        Assert.Contains("module", Lexer.KeywordNames);
        Assert.Contains("each", Lexer.KeywordNames);
        Assert.Contains("invariant", Lexer.KeywordNames);
        Assert.Contains("pp", Lexer.KeywordNames);
        Assert.Contains("raw", Lexer.KeywordNames);
        foreach (var retired in new[] { "EACH", "IV", "PP", "/PP", "PPE", "CS", "RAW", "/RAW", "CSHARP", "/CSHARP" })
            Assert.DoesNotContain(retired, Lexer.KeywordNames);
        Assert.DoesNotContain("FOREACH", Lexer.KeywordNames);
        Assert.DoesNotContain("INV", Lexer.KeywordNames);
    }

    [Fact]
    public void EffectCodeRegistryPreservesToCompactBehavior()
    {
        Assert.Equal("cw", EffectCodes.ToCompact("io", "console_write"));
        Assert.Equal("fs:r", EffectCodes.ToCompact("IO", "Filesystem_Read"));
        Assert.Equal("mut:col", EffectCodes.ToCompact("mutation", "collection"));
        Assert.Equal("db:r", EffectCodes.ToCompact("io", "dbr"));
        Assert.Equal("something_else", EffectCodes.ToCompact("io", "something_else"));

        Assert.Contains("mut", EffectCodes.DocumentedCompactCodes);
        Assert.Contains("mut:col", EffectCodes.DocumentedCompactCodes);
        Assert.DoesNotContain("fw", EffectCodes.DocumentedCompactCodes);
        Assert.Contains("fw", EffectCodes.KnownCompactCodes);
    }

    [Fact]
    public void ImplementedDiagnosticCodesIncludeKnownConstants()
    {
        var codes = DocDriftChecker.GetImplementedDiagnosticCodes();
        Assert.Contains("Calor0830", codes);
        Assert.Contains("Calor0712", codes); // verification-pass code, registered via constant
        Assert.Contains("Calor1320", codes);
        Assert.DoesNotContain("Calor9876", codes);
    }

    // --- Coverage probes: LoadFromRepository must scan the full doc set.
    // Reviewer cases: §BOGUS planted in docs/syntax-reference/structure-tags.md
    // and Calor9876 planted in docs/syntax-reference/index.md must be detected.

    private static string CreateFakeRepo(
        string structureTagsContent = "# Structure Tags\n",
        string syntaxIndexContent = "# Syntax Reference\n")
    {
        var root = Path.Combine(AppContext.BaseDirectory, "calor-drift-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs", "syntax-reference"));
        Directory.CreateDirectory(Path.Combine(root, "docs", "cli"));
        Directory.CreateDirectory(Path.Combine(root, ".github"));
        File.WriteAllText(
            Path.Combine(root, "Directory.Build.props"),
            "<Project><PropertyGroup><Version>9.9.9</Version></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "# CLAUDE\n");
        File.WriteAllText(Path.Combine(root, ".github", "copilot-instructions.md"), "# Copilot Instructions\n");
        File.WriteAllText(Path.Combine(root, "docs", "syntax-reference", "index.md"), syntaxIndexContent);
        File.WriteAllText(Path.Combine(root, "docs", "syntax-reference", "structure-tags.md"), structureTagsContent);
        File.WriteAllText(Path.Combine(root, "docs", "syntax-reference", "effects.md"), "# Effects\n");
        File.WriteAllText(Path.Combine(root, "docs", "cli", "structured-output.md"), "# Structured Output\n");
        return root;
    }

    private static List<Diagnostic> CheckFakeRepo(string root)
    {
        try
        {
            var loadErrors = new List<Diagnostic>();
            var inputs = DocDriftChecker.LoadFromRepository(root, loadErrors);
            var findings = DocDriftChecker.Check(inputs);
            findings.AddRange(loadErrors);
            return findings;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepositoryScanDetectsUnknownKeywordPlantedInStructureTagsDoc()
    {
        var root = CreateFakeRepo(
            structureTagsContent: "# Structure Tags\n\nClose every block with `§BOGUS`.\n");
        var findings = CheckFakeRepo(root);

        var finding = Assert.Single(findings, f => f.Code == DiagnosticCode.DocDriftUnknownKeyword);
        Assert.Contains("§BOGUS", finding.Message);
        Assert.Contains("structure-tags.md", finding.FilePath);
        Assert.Equal(3, finding.Span.Line);
    }

    [Fact]
    public void RepositoryScanDetectsUnknownDiagnosticCodePlantedInSyntaxIndex()
    {
        var root = CreateFakeRepo(
            syntaxIndexContent: "# Syntax Reference\n\nViolations raise `Calor9876`.\n");
        var findings = CheckFakeRepo(root);

        var finding = Assert.Single(findings, f => f.Code == DiagnosticCode.DocDriftUnknownDiagnosticCode);
        Assert.Contains("Calor9876", finding.Message);
        Assert.Contains("index.md", finding.FilePath);
        Assert.Equal(3, finding.Span.Line);
    }
}
