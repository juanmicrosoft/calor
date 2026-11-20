using System.Reflection;
using System.Text.Json;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.SelfTest;
using Calor.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

[Collection("SelfTestConsole")]
public sealed class NativeSelfTestScenarioTests
{
    [Fact]
    public void AllTenInputs_MatchCanonicalEmbeddedAndRegisteredCopiesWithoutRawFallback()
    {
        var root = CliTestHarness.FindRepoRoot();
        var scenarios = SelfTestRunner.LoadScenarios();
        Assert.Equal(10, scenarios.Count);
        foreach (var scenario in scenarios)
        {
            Assert.StartsWith("module ", scenario.Input);
            Assert.DoesNotContain("§", scenario.Input);
            RawFrameAssertions.AssertFramed(scenario.Input, present: false);
            Assert.True(CalorSourceHelper.Parse(scenario.Input).IsSuccess);
            var canonical = Path.Combine(root, "tests/E2E/scenarios", scenario.Name, "input.calr");
            var mirror = ActiveSyntaxInputs.ForCurrentCompiler(root,
                $"src/Calor.Compiler/Resources/SelfTest/{scenario.Name}.calr");
            Assert.Equal(File.ReadAllText(canonical), scenario.Input);
            Assert.Equal(File.ReadAllBytes(canonical), File.ReadAllBytes(mirror));
            Load(scenario.ExpectedOutput);
        }
    }

    // #1618: custom precondition messages are gone; Divide's precondition is the refinement on b,
    // so the contracts scenario now round-trips through the native emitter like the others.
    [Fact]
    public void ContractsScenario_RefinementRoundTripsThroughNativeEmitter()
    {
        var scenario = Scenario("03_contracts");
        var ast = CalorSourceHelper.Parse(scenario.Input).Ast!;
        var divide = Assert.Single(Assert.Single(ast.Classes).Methods, method => method.Name == "Divide");
        Assert.Empty(divide.Preconditions);
        Assert.NotNull(divide.Parameters[1].InlineRefinement);
        var emitted = new CalorEmitter().Emit(ast);
        Assert.Contains("int:b | (# != 0)", emitted);
        var reparsed = CalorSourceHelper.Parse(emitted);
        Assert.True(reparsed.IsSuccess, string.Join("\n", reparsed.Errors));
        Assert.NotNull(Assert.Single(Assert.Single(reparsed.Ast!.Classes).Methods, method => method.Name == "Divide")
            .Parameters[1].InlineRefinement);
        Assert.DoesNotContain("raw ", scenario.Input);
        Assert.DoesNotContain("throw ", scenario.Input);
    }

    [Theory]
    [InlineData("01_hello_world")]
    [InlineData("02_fizzbuzz")]
    [InlineData("04_option_result")]
    [InlineData("05_skill_syntax")]
    [InlineData("06_pattern_matching")]
    [InlineData("07_collections")]
    [InlineData("07_quantifiers")]
    [InlineData("08_contract_inheritance_z3")]
    [InlineData("09_codegen_bugfixes")]
    public void OtherNineScenarios_RoundTripThroughNativeEmitterWithoutPreservation(string name)
    {
        var scenario = Scenario(name);
        var emitted = new CalorEmitter().Emit(CalorSourceHelper.Parse(scenario.Input).Ast!);
        Assert.DoesNotContain("§", emitted);
        RawFrameAssertions.AssertFramed(emitted, present: false);
        var result = Program.Compile(emitted, name + ".calr", new CompilationOptions
        {
            EmitLineDirectives = false, StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Load(result.GeneratedCode);
    }

    [Theory]
    [InlineData("01_hello_world", "Hello.HelloModule")]
    [InlineData("02_fizzbuzz", "FizzBuzz.FizzBuzzModule")]
    [InlineData("03_contracts", "Contracts.ContractsModule")]
    [InlineData("04_option_result", "TypeSystem.TypeSystemModule")]
    [InlineData("05_skill_syntax", "SkillSyntax.SkillSyntaxModule")]
    [InlineData("07_collections", "Collections.CollectionsModule")]
    public void EntryPoints_PreserveCompleteOutput(string name, string type)
    {
        var expected = name switch
        {
            "01_hello_world" => new[] { "Hello from Calor E2E Test!" },
            "02_fizzbuzz" => Enumerable.Range(1, 100).Select(number =>
                number % 15 == 0 ? "FizzBuzz" : number % 3 == 0 ? "Fizz"
                    : number % 5 == 0 ? "Buzz" : number.ToString()).ToArray(),
            "03_contracts" => ["Testing contracts..."],
            "04_option_result" => ["Testing nullable values and exceptions...", "42", "-1", "100", "Something went wrong"],
            "05_skill_syntax" => ["=== Calor Skill Syntax Validation ===", "", "Testing arithmetic operators...",
                "Testing comparison operators...", "Testing control flow...", "1", "2", "3", "4", "5"],
            "07_collections" => ["Created list with elements", "Created dictionary with entries",
                "Created set with fruits", "All collection tests passed!"],
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var assembly = Load(Scenario(name).ExpectedOutput);
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Invoke(assembly, type, "Main", []);
        }
        finally
        {
            Console.SetOut(previous);
        }
        Assert.Equal(string.Join(Environment.NewLine, expected) + Environment.NewLine, output.ToString());
    }

    [Theory]
    [InlineData("GetGrade", 100, "A")]
    [InlineData("GetGrade", 90, "A")]
    [InlineData("GetGrade", 89, "B")]
    [InlineData("GetGrade", 80, "B")]
    [InlineData("GetGrade", 79, "C")]
    [InlineData("GetGrade", 70, "C")]
    [InlineData("GetGrade", 69, "D")]
    [InlineData("GetGrade", 60, "D")]
    [InlineData("GetGrade", 59, "F")]
    [InlineData("GetHttpStatus", 200, "OK")]
    [InlineData("GetHttpStatus", 404, "Not Found")]
    [InlineData("GetHttpStatus", 500, "Server Error")]
    [InlineData("GetHttpStatus", 201, "Unknown")]
    [InlineData("Describe", 101, "large")]
    [InlineData("Describe", 100, "normal")]
    [InlineData("Describe", -1, "negative")]
    [InlineData("Describe", 0, "zero")]
    [InlineData("Describe", 1, "normal")]
    public void ScalarPatterns_KeepEveryArmAndBoundary(string method, int input, string expected) =>
        Assert.Equal(expected, Invoke(Load(Scenario("06_pattern_matching").ExpectedOutput),
            "PatternDemo.PatternDemoModule", method, [input]));

    [Fact]
    public void ArithmeticAndCodegen_RetainExecutableTypesAndCheckedOverflow()
    {
        var arithmetic = Load(Scenario("05_skill_syntax").ExpectedOutput);
        Assert.Equal(7, Invoke(arithmetic, "SkillSyntax.SkillSyntaxModule", "Add", [3, 4]));
        Assert.Equal(-1, Invoke(arithmetic, "SkillSyntax.SkillSyntaxModule", "Subtract", [3, 4]));
        Assert.Equal(12, Invoke(arithmetic, "SkillSyntax.SkillSyntaxModule", "Multiply", [3, 4]));
        Assert.Equal(true, Invoke(arithmetic, "SkillSyntax.SkillSyntaxModule", "IsPositive", [1]));
        Assert.Equal(false, Invoke(arithmetic, "SkillSyntax.SkillSyntaxModule", "IsPositive", [0]));
        Assert.Equal(true, Invoke(arithmetic, "SkillSyntax.SkillSyntaxModule", "IsZero", [0]));
        Assert.Equal(false, Invoke(arithmetic, "SkillSyntax.SkillSyntaxModule", "IsZero", [-1]));
        Assert.IsType<OverflowException>(ObserveException(arithmetic, "SkillSyntax.SkillSyntaxModule", "Add", [int.MaxValue, 1]));
        var codegen = Load(Scenario("09_codegen_bugfixes").ExpectedOutput);
        var type = codegen.GetType("Transliterator.CyrillicToLatin", throwOnError: true)!;
        var instance = Activator.CreateInstance(type, ["mapping"])!;
        Assert.Equal("mapping", type.GetField("_mappings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance));
        Assert.True(type.GetProperty("Name")!.GetMethod!.GetBaseDefinition().IsAbstract);
        // The original method reads an interactive key and indexes an inclusive
        // upper bound: shape coverage is not a successful transliteration.
        Assert.NotNull(type.GetMethod("Transliterate"));
    }

    [Theory]
    [InlineData("03_contracts", "Contracts.ContractsModule", "Square", -1, null)]
    [InlineData("03_contracts", "Contracts.ContractsModule", "Square", 46341, null)]
    [InlineData("03_contracts", "Contracts.ContractsModule", "Divide", 4, 0)]
    [InlineData("03_contracts", "Contracts.ContractsModule", "Divide", int.MinValue, -1)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "ExistsTarget", -1, null)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "ExistsTarget", 10, null)]
    [InlineData("08_contract_inheritance_z3", "ContractZ3Test.ValidService", "Process", -11, null)]
    [InlineData("08_contract_inheritance_z3", "ContractZ3Test.ValidService", "Process", -1, null)]
    [InlineData("08_contract_inheritance_z3", "ContractZ3Test.AnotherValidService", "Process", -1, null)]
    public void Exceptions_MatchFrozenRuntimeAndCurrentParsedMetadata(
        string name, string type, string method, int first, int? second)
    {
        var scenario = Scenario(name);
        object?[] arguments = second.HasValue ? [first, second.Value] : [first];
        var frozen = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests/TestData/self-test-metadata", name + ".g.cs.txt"));
        var original = ObserveException(Load(frozen), type, method, arguments);
        var actual = ObserveException(Load(scenario.ExpectedOutput), type, method, arguments);
        Assert.Equal(original.GetType(), actual.GetType());
        Assert.Equal(original.Message, actual.Message);
        if (actual is not ContractViolationException violation)
            return;
        var old = Assert.IsType<ContractViolationException>(original);
        Assert.Equal(old.Kind, violation.Kind);
        Assert.Equal(old.Condition, violation.Condition);
        var ast = CalorSourceHelper.Parse(scenario.Input).Ast!;
        var owner = Assert.Single(ast.Classes.SelectMany(item => item.Methods),
            item => item.Id == violation.FunctionId);
        var span = violation.Kind == ContractKind.Requires
            ? Assert.Single(owner.Preconditions).Span : Assert.Single(owner.Postconditions).Span;
        Assert.Equal(span.Start, violation.StartOffset);
        Assert.Equal(span.Length, violation.Length);
        Assert.Equal(span.Line, violation.Line);
        Assert.Equal(span.Column, violation.Column);
        Assert.Null(violation.SourceFile);
    }

    [Theory]
    [InlineData("03_contracts", "Contracts.ContractsModule", "Square", 0, null)]
    [InlineData("03_contracts", "Contracts.ContractsModule", "Square", 46340, null)]
    [InlineData("03_contracts", "Contracts.ContractsModule", "Divide", -4, 2)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "ExistsTarget", 0, null)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "ExistsTarget", 9, null)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "AllNonNegative", 0, null)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "AllNonNegative", -1, null)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "AllNonNegative", 3, null)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "ImplicationTest", -1, null)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "ImplicationTest", 0, null)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "ImplicationTest", 1, null)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "MatrixSymmetry", 0, 3)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "MatrixSymmetry", 3, 0)]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "MatrixSymmetry", 2, 3)]
    [InlineData("08_contract_inheritance_z3", "ContractZ3Test.ValidService", "Process", 0, null)]
    [InlineData("08_contract_inheritance_z3", "ContractZ3Test.AnotherValidService", "Process", 0, null)]
    public void SuccessfulContractCalls_MatchFrozenIndependentOracle(
        string name, string type, string method, int first, int? second)
    {
        object?[] arguments = second.HasValue ? [first, second.Value] : [first];
        var frozen = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests/TestData/self-test-metadata", name + ".g.cs.txt"));
        Assert.Equal(Invoke(Load(frozen), type, method, arguments),
            Invoke(Load(Scenario(name).ExpectedOutput), type, method, arguments));
    }

    [Theory]
    [InlineData(ContractMode.Debug)]
    [InlineData(ContractMode.Release)]
    [InlineData(ContractMode.Off)]
    public void ContractModes_KeepRefinementChecksAndOffStripsContractGuards(ContractMode mode)
    {
        var scenario = Scenario("03_contracts");
        var result = Program.Compile(scenario.Input, "03_contracts.calr", new CompilationOptions
        {
            ContractMode = mode, EmitLineDirectives = false, StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var assembly = Load(result.GeneratedCode);
        Assert.Equal(9, Invoke(assembly, "Contracts.ContractsModule", "Square", [3]));
        Assert.Equal(2, Invoke(assembly, "Contracts.ContractsModule", "Divide", [4, 2]));
        // #1618: the preconditions are refinements (x >= 0 on Square, b != 0 on Divide). Their
        // entry checks throw ArgumentOutOfRangeException naming the parameter in every contract
        // mode, Off included; Off strips only the ContractViolationException guards.
        var error = ObserveException(assembly, "Contracts.ContractsModule", "Divide", [4, 0]);
        Assert.Equal("b", Assert.IsType<ArgumentOutOfRangeException>(error).ParamName);
        Assert.Equal("x", Assert.IsType<ArgumentOutOfRangeException>(
            ObserveException(assembly, "Contracts.ContractsModule", "Square", [-1])).ParamName);
        if (mode == ContractMode.Off)
            Assert.DoesNotContain("ContractViolationException", result.GeneratedCode);
        else
            Assert.Contains("ContractViolationException", result.GeneratedCode);
    }

    [Fact]
    public void ActualCli_SucceedsForAllTenAndRejectsAnUnknownScenario()
    {
        var root = CliTestHarness.FindRepoRoot();
        var success = CliTestHarness.RunCli(root, "--no-telemetry", "self-test");
        Assert.True(success.ExitCode == 0, success.StdOut + success.StdErr);
        Assert.Contains("Results: 10/10 passed", success.StdOut);
        var failure = CliTestHarness.RunCli(root, "--no-telemetry", "self-test", "--scenario", "missing-native-scenario");
        Assert.Equal(1, failure.ExitCode);
        Assert.Contains("Unknown scenario", failure.StdErr);
        Assert.DoesNotContain("Results:", failure.StdOut);
    }

    // #1618: a refinement entry check carries the parameter, not an owner id or location, so
    // those rows assert the parameter; the owner/location row is a postcondition violation.
    [Theory]
    [InlineData("03_contracts", "Contracts.ContractsModule", "Divide", 4, 0, "b")]
    [InlineData("07_quantifiers", "QuantifierDemo.QuantifierDemoModule", "ExistsTarget", -1, null, "target")]
    [InlineData("08_contract_inheritance_z3", "ContractZ3Test.ValidService", "Process", -10, null, null)]
    public void PhysicalCli_ExceptionsRetainCommittedSidecarOwnersAndCurrentLocations(
        string name, string type, string method, int first, int? second, string? refinedParameter)
    {
        var root = CliTestHarness.FindRepoRoot();
        var canonical = Path.Combine(root, "tests/E2E/scenarios", name);
        var temporary = Path.Combine(Path.GetTempPath(), "calor-selftest-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporary, ".calor"));
        try
        {
            File.Copy(Path.Combine(canonical, "input.calr"), Path.Combine(temporary, "input.calr"));
            var sidecar = File.ReadAllText(Path.Combine(canonical, ".calor/ids.json"));
            File.WriteAllText(Path.Combine(temporary, ".calor/ids.json"), sidecar);
            var cli = CliTestHarness.RunCli(temporary, "--no-telemetry", "-i", "input.calr", "-o", "actual.g.cs");
            Assert.True(cli.ExitCode == 0, cli.StdOut + cli.StdErr);
            using var identities = JsonDocument.Parse(sidecar);
            var owner = Assert.Single(identities.RootElement.GetProperty("Declarations").EnumerateArray(),
                declaration => declaration.GetProperty("Name").GetString() == method
                    && declaration.GetProperty("Container").GetString()!.Contains(type, StringComparison.Ordinal));
            object?[] arguments = second.HasValue ? [first, second.Value] : [first];
            var observed = ObserveException(
                Load(File.ReadAllText(Path.Combine(temporary, "actual.g.cs"))), type, method, arguments);
            if (refinedParameter != null)
            {
                Assert.Equal(refinedParameter, Assert.IsType<ArgumentOutOfRangeException>(observed).ParamName);
                return;
            }
            var exception = Assert.IsType<ContractViolationException>(observed);
            Assert.Equal(ContractKind.Ensures, exception.Kind);
            Assert.Equal(owner.GetProperty("Id").GetString(), exception.FunctionId);
            var source = File.ReadAllText(Path.Combine(temporary, "input.calr"));
            var parsedType = Assert.Single(CalorSourceHelper.Parse(source).Ast!.Classes,
                item => type.EndsWith("." + item.Name, StringComparison.Ordinal));
            var parsedOwner = Assert.Single(parsedType.Methods, item => item.Name == method);
            var span = Assert.Single(parsedOwner.Postconditions).Span;
            Assert.Equal((span.Start, span.Length, span.Line, span.Column),
                (exception.StartOffset, exception.Length, exception.Line, exception.Column));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static SelfTestRunner.SelfTestScenario Scenario(string name) =>
        Assert.Single(SelfTestRunner.LoadScenarios(), item => item.Name == name);

    private static Assembly Load(string source)
    {
        var compilation = CSharpCompilation.Create("SelfTest_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Assembly assembly, string name, string method, object?[] arguments)
    {
        var type = assembly.GetType(name, throwOnError: true)!;
        var callable = type.GetMethod(method)!;
        return callable.Invoke(callable.IsStatic ? null : Activator.CreateInstance(type), arguments);
    }

    private static Exception ObserveException(Assembly assembly, string type, string method, object?[] arguments) =>
        Assert.Throws<TargetInvocationException>(() => Invoke(assembly, type, method, arguments)).InnerException!;
}
