using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.SelfTest;

namespace Calor.Compiler.Mcp.Tools;

public sealed class SelfTestTool : McpToolBase
{
    public override string Name => "calor_self_test";

    public override string Description =>
        "Run compiler self-test: compiles embedded reference .calr files and diffs output against golden .cs files. " +
        "Use this to verify the compiler is working correctly.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "scenario": {
                    "type": "string",
                    "description": "Optional: run only a specific scenario by name (e.g., '01_hello_world')"
                }
            },

            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var scenarioFilter = GetString(arguments, "scenario");
            var scenarios = SelfTestRunner.LoadScenarios();

            if (scenarioFilter != null)
            {
                scenarios = scenarios
                    .Where(s => s.Name.Equals(scenarioFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (scenarios.Count == 0)
                {
                    var available = SelfTestRunner.LoadScenarios().Select(s => s.Name).ToList();
                    return Task.FromResult(McpToolResult.Error(
                        $"Unknown scenario: '{scenarioFilter}'. Available: {string.Join(", ", available)}"));
                }
            }

            var results = scenarios.Select(SelfTestRunner.Run).ToList();
            var passed = results.Count(r => r.Passed);
            var failed = results.Count(r => !r.Passed);

            var output = new SelfTestOutput
            {
                Passed = passed,
                Failed = failed,
                Total = results.Count,
                Results = results.Select(r => new SelfTestResultOutput
                {
                    Scenario = r.ScenarioName,
                    Passed = r.Passed,
                    Diff = r.Passed ? null : r.Diff,
                    Error = r.Error
                }).ToList()
            };

            return Task.FromResult(McpToolResult.Json(output, isError: failed > 0));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Self-test failed: {ex.Message}"));
        }
    }

    private sealed class SelfTestOutput
    {
        [JsonPropertyName("passed")]
        public int Passed { get; init; }

        [JsonPropertyName("failed")]
        public int Failed { get; init; }

        [JsonPropertyName("total")]
        public int Total { get; init; }

        [JsonPropertyName("results")]
        public required List<SelfTestResultOutput> Results { get; init; }
    }

    private sealed class SelfTestResultOutput
    {
        [JsonPropertyName("scenario")]
        public required string Scenario { get; init; }

        [JsonPropertyName("passed")]
        public bool Passed { get; init; }

        [JsonPropertyName("diff")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Diff { get; init; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; init; }
    }
}
