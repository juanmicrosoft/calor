using System.Diagnostics;
using System.Text.Json;
using Calor.Enforcement.Tests;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.SelfTest;
using Xunit;

namespace Calor.Compiler.Tests;

[CollectionDefinition("SelfTestConsole", DisableParallelization = true)]
public sealed class SelfTestConsoleCollection;

[Collection("SelfTestConsole")]
public class SelfTestRunnerTests
{
    [Fact]
    public void SelfTestRunner_LoadScenarios_ReturnsExpectedScenarios()
    {
        var scenarios = SelfTestRunner.LoadScenarios();

        Assert.True(scenarios.Count >= 9, $"Expected at least 9 scenarios, got {scenarios.Count}");
        Assert.Contains(scenarios, s => s.Name == "01_hello_world");
        Assert.Contains(scenarios, s => s.Name == "02_fizzbuzz");
        Assert.Contains(scenarios, s => s.Name == "03_contracts");
        Assert.Contains(scenarios, s => s.Name == "04_option_result");
        Assert.Contains(scenarios, s => s.Name == "05_skill_syntax");
        Assert.Contains(scenarios, s => s.Name == "06_pattern_matching");
        Assert.Contains(scenarios, s => s.Name == "07_collections");
        Assert.Contains(scenarios, s => s.Name == "07_quantifiers");
        Assert.Contains(scenarios, s => s.Name == "08_contract_inheritance_z3");
    }

    [Fact]
    public void SelfTestRunner_LoadScenarios_AllHaveInputAndExpectedOutput()
    {
        var scenarios = SelfTestRunner.LoadScenarios();

        foreach (var scenario in scenarios)
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.Input),
                $"Scenario '{scenario.Name}' has empty input");
            Assert.False(string.IsNullOrWhiteSpace(scenario.ExpectedOutput),
                $"Scenario '{scenario.Name}' has empty expected output");
        }
    }

    [Fact]
    public void SelfTestRunner_RunAll_AllPass()
    {
        var scenarios = SelfTestRunner.LoadScenarios();
        var results = SelfTestRunner.RunAll();

        Assert.Equal(scenarios.Count, results.Count);

        foreach (var result in results)
        {
            Assert.True(result.Passed,
                $"Scenario '{result.ScenarioName}' failed. " +
                (result.Error ?? $"Diff:\n{result.Diff}"));
        }
    }

    [Fact]
    public void SelfTestRunner_Run_SingleScenario_Passes()
    {
        var scenarios = SelfTestRunner.LoadScenarios();
        var helloWorld = scenarios.First(s => s.Name == "01_hello_world");

        var result = SelfTestRunner.Run(helloWorld);

        Assert.True(result.Passed);
        Assert.Equal("01_hello_world", result.ScenarioName);
        Assert.Null(result.Error);
        Assert.Null(result.Diff);
        Assert.NotNull(result.ActualOutput);
    }

    [Fact]
    public void SelfTestRunner_Run_BadInput_ReturnsError()
    {
        var badScenario = new SelfTestRunner.SelfTestScenario(
            "bad_input",
            "this is not valid calor syntax §§§GARBAGE{{{",
            "");

        var result = SelfTestRunner.Run(badScenario);

        Assert.False(result.Passed);
        Assert.Equal("bad_input", result.ScenarioName);
        Assert.NotNull(result.Error);
        Assert.Contains("Compilation failed", result.Error);
    }

    [Fact]
    public async Task SelfTestTool_Execute_ReturnsJsonResults()
    {
        var tool = new SelfTestTool();

        var result = await tool.ExecuteAsync(null);

        Assert.NotNull(result);
        Assert.Single(result.Content);
        Assert.Equal("text", result.Content[0].Type);

        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        var scenarioCount = SelfTestRunner.LoadScenarios().Count;

        Assert.True(root.TryGetProperty("total", out var total));
        Assert.Equal(scenarioCount, total.GetInt32());

        Assert.True(root.TryGetProperty("passed", out var passed));
        Assert.Equal(scenarioCount, passed.GetInt32());

        Assert.True(root.TryGetProperty("failed", out var failed));
        Assert.Equal(0, failed.GetInt32());

        Assert.True(root.TryGetProperty("results", out var results));
        Assert.Equal(scenarioCount, results.GetArrayLength());
    }

    [Fact]
    public async Task SelfTestTool_Execute_WithScenarioFilter_ReturnsSingleResult()
    {
        var tool = new SelfTestTool();

        var argsJson = JsonDocument.Parse("""{"scenario": "01_hello_world"}""");
        var result = await tool.ExecuteAsync(argsJson.RootElement);

        Assert.NotNull(result);
        Assert.False(result.IsError);

        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("passed").GetInt32());
        Assert.Equal(0, root.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task SelfTestTool_Execute_WithInvalidScenario_ReturnsError()
    {
        var tool = new SelfTestTool();

        var argsJson = JsonDocument.Parse("""{"scenario": "nonexistent"}""");
        var result = await tool.ExecuteAsync(argsJson.RootElement);

        Assert.True(result.IsError);
        Assert.Contains("Unknown scenario", result.Content[0].Text);
    }

    [Fact]
    public void SelfTestRunner_Run_MismatchedOutput_ReturnsDiff()
    {
        // Use a valid scenario but with wrong expected output to trigger diff
        var scenarios = SelfTestRunner.LoadScenarios();
        var helloWorld = scenarios.First(s => s.Name == "01_hello_world");

        var mismatchedScenario = new SelfTestRunner.SelfTestScenario(
            helloWorld.Name,
            helloWorld.Input,
            helloWorld.ExpectedOutput.Replace("Hello from Calor E2E Test!", "WRONG OUTPUT"));

        var result = SelfTestRunner.Run(mismatchedScenario);

        Assert.False(result.Passed);
        Assert.Null(result.Error);
        Assert.NotNull(result.Diff);
        Assert.Contains("--- expected", result.Diff);
        Assert.Contains("+++ actual", result.Diff);
        Assert.Contains("@@", result.Diff);
        Assert.Contains("-", result.Diff);
        Assert.Contains("+", result.Diff);
    }

    [Fact]
    public void SelfTestRunner_GenerateDiff_ProducesUnifiedFormat()
    {
        var expected = "line1\nline2\nline3\nline4\nline5";
        var actual = "line1\nline2\nchanged\nline4\nline5";

        var diff = SelfTestRunner.GenerateDiff(expected, actual);

        Assert.Contains("--- expected", diff);
        Assert.Contains("+++ actual", diff);
        Assert.Contains("@@", diff);
        Assert.Contains("-line3", diff);
        Assert.Contains("+changed", diff);
        // Context lines should be present
        Assert.Contains(" line2", diff);
        Assert.Contains(" line4", diff);
    }

    [Fact]
    public async Task McpMessageHandler_ToolsList_IncludesSelfTestTool()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("1").RootElement,
            Method = "tools/list"
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("calor_self_test", json);
    }

    [Fact]
    public async Task McpMessageHandler_ToolsCall_SelfTestTool_Success()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("10").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_self_test",
                    "arguments": {
                        "scenario": "01_hello_world"
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        // The result is a McpToolResult with content[0].text containing the inner JSON
        var resultJson = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        var resultDoc = JsonDocument.Parse(resultJson);
        var contentText = resultDoc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;

        var innerJson = JsonDocument.Parse(contentText);
        Assert.Equal(1, innerJson.RootElement.GetProperty("passed").GetInt32());
        Assert.Equal(1, innerJson.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("01_hello_world",
            innerJson.RootElement.GetProperty("results")[0].GetProperty("scenario").GetString());
    }

    [Fact]
    public void SelfTestRunner_ScenariosMatchE2EDirectory()
    {
        // Verify the embedded self-test scenarios exactly match the E2E scenario directories
        // that have both input.calr and output.g.cs. Synchronization is an explicit command,
        // never a build side effect.
        var scenariosPath = Path.Combine(CliTestHarness.FindRepoRoot(), "tests/E2E/scenarios");
        Assert.True(Directory.Exists(scenariosPath), $"Canonical scenarios missing: {scenariosPath}");

        var e2eScenarios = Directory.GetDirectories(scenariosPath)
            .Where(dir => File.Exists(Path.Combine(dir, "input.calr"))
                       && File.Exists(Path.Combine(dir, "output.g.cs")))
            .Select(dir => Path.GetFileName(dir))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var embeddedScenarios = SelfTestRunner.LoadScenarios()
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(e2eScenarios, embeddedScenarios);

        var assembly = typeof(SelfTestRunner).Assembly;
        foreach (var name in e2eScenarios)
        foreach (var (canonicalName, embeddedSuffix) in new[]
        {
            ("input.calr", ".calr"), ("output.g.cs", ".g.cs")
        })
        {
            using var resource = assembly.GetManifestResourceStream(
                $"Calor.Compiler.Resources.SelfTest.{name}{embeddedSuffix}");
            Assert.NotNull(resource);
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(scenariosPath, name, canonicalName)),
                bytes.ToArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableScenario_CanonicalAndEmbeddedProgramsExecuteTheCompleteLesson(bool embedded)
    {
        var scenario = LoadCanonicalNullableScenario();
        if (embedded)
            scenario = Assert.Single(SelfTestRunner.LoadScenarios(), item => item.Name == scenario.Name);
        var result = SelfTestRunner.Run(scenario);
        Assert.True(result.Passed, result.Error ?? result.Diff);
        Assert.DoesNotContain("#line", result.ActualOutput!);

        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var execution = TestHarness.Execute(scenario.Input, "Main", options: new CompilationOptions
            {
                EmitLineDirectives = false,
                StatusWriter = TextWriter.Null
            });
            Assert.Null(execution.Exception);
        }
        finally
        {
            Console.SetOut(previous);
        }
        Assert.Equal(string.Join(Environment.NewLine,
            "Testing nullable values and exceptions...", "42", "-1", "100", "Something went wrong", ""),
            output.ToString());
    }

    [Fact]
    public void NullableScenario_ActiveMirrorCannotHideADifferentLesson()
    {
        var root = CliTestHarness.FindRepoRoot();
        const string original = "src/Calor.Compiler/Resources/SelfTest/04_option_result.calr";
        Assert.Contains(original, ActiveSyntaxInputs.OriginalPaths);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(root, "tests/E2E/scenarios/04_option_result/input.calr")),
            File.ReadAllBytes(ActiveSyntaxInputs.ForCurrentCompiler(root, original)));
    }

    [Fact]
    public void NullableScenario_GoldenIsGeneratedIdenticallyUnderDifferentSourcePaths()
    {
        var scenario = LoadCanonicalNullableScenario();
        foreach (var path in new[]
        {
            Path.Combine(CliTestHarness.FindRepoRoot(), "tests/E2E/scenarios/04_option_result/input.calr"),
            Path.Combine(Path.GetTempPath(), "another-checkout", "04_option_result.calr")
        })
        {
            var result = Program.Compile(scenario.Input.Replace("\r\n", "\n"), path, new CompilationOptions
            {
                EnforceEffects = true,
                ContractMode = ContractMode.Debug,
                VerifyContracts = false,
                EmitLineDirectives = false
            });
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
            Assert.Equal(scenario.ExpectedOutput.Replace("\r\n", "\n"), result.GeneratedCode);
            Assert.DoesNotContain("#line", result.GeneratedCode);
            Assert.DoesNotContain(path, result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullableScenario_StaleInputOrExpectedOutputFailsTheActualRunner(bool mutateInput)
    {
        var scenario = LoadCanonicalNullableScenario();
        var mutated = mutateInput
            ? scenario with { Input = scenario.Input.Replace(" 42", " 43", StringComparison.Ordinal) }
            : scenario with { ExpectedOutput = scenario.ExpectedOutput.Replace(" 100;", " 101;", StringComparison.Ordinal) };
        Assert.NotEqual(scenario, mutated);
        var result = SelfTestRunner.Run(mutated);
        Assert.False(result.Passed);
        Assert.Null(result.Error);
        Assert.False(string.IsNullOrEmpty(result.Diff));
    }

    [Theory]
    [InlineData("input.calr", "04_option_result.calr", false)]
    [InlineData("input.calr", "04_option_result.calr", true)]
    [InlineData("output.g.cs", "04_option_result.g.cs", false)]
    [InlineData("output.g.cs", "04_option_result.g.cs", true)]
    public void SyncCheck_RejectsCanonicalOrEmbeddedDrift(string canonicalName, string embeddedName, bool mutateEmbedded)
    {
        var root = CliTestHarness.FindRepoRoot();
        var temporary = Path.Combine(Path.GetTempPath(), "calor-selftest-parity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var canonical = Path.Combine(temporary, "tests/E2E/scenarios/04_option_result");
            var embedded = Path.Combine(temporary, "src/Calor.Compiler/Resources/SelfTest");
            Directory.CreateDirectory(canonical);
            Directory.CreateDirectory(embedded);
            foreach (var (input, resource) in new[]
            {
                ("input.calr", "04_option_result.calr"), ("output.g.cs", "04_option_result.g.cs")
            })
            {
                var bytes = File.ReadAllBytes(Path.Combine(root, "tests/E2E/scenarios/04_option_result", input));
                File.WriteAllBytes(Path.Combine(canonical, input), bytes);
                File.WriteAllBytes(Path.Combine(embedded, resource), bytes);
            }
            Assert.Equal(0, CheckSync(root, temporary));
            File.AppendAllText(mutateEmbedded
                ? Path.Combine(embedded, embeddedName)
                : Path.Combine(canonical, canonicalName), "\n// stale parity mutation\n");
            Assert.Equal(1, CheckSync(root, temporary));
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
        }
    }

    private static int CheckSync(string root, string temporary)
    {
        var start = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            Path.Combine(root, "scripts/sync-self-test-resources.py"), "--check", "--repo-root", temporary
        })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the self-test synchronization check.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail("Self-test synchronization check timed out.");
        }
        Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        return process.ExitCode;
    }

    private static SelfTestRunner.SelfTestScenario LoadCanonicalNullableScenario()
    {
        var root = CliTestHarness.FindRepoRoot();
        var directory = Path.Combine(root, "tests/E2E/scenarios/04_option_result");
        return new("04_option_result",
            File.ReadAllText(Path.Combine(directory, "input.calr")),
            File.ReadAllText(Path.Combine(directory, "output.g.cs")));
    }
}
