using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.SelfTest;
using Xunit;

namespace Calor.Compiler.Tests;

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
        var scenariosPath = FindE2EScenariosPath();
        if (scenariosPath == null)
        {
            // Running from a context where E2E directory isn't available (e.g., CI package)
            return;
        }

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
    }

    private static string? FindE2EScenariosPath()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(SelfTestRunnerTests).Assembly.Location)!;
        var current = new DirectoryInfo(assemblyDir);
        while (current != null)
        {
            var scenariosPath = Path.Combine(current.FullName, "tests", "E2E", "scenarios");
            if (Directory.Exists(scenariosPath))
                return scenariosPath;
            current = current.Parent;
        }
        return null;
    }
}
