using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.SelfTest;

namespace Calor.Compiler.Mcp.Tools;

public sealed class GetExampleTool : McpToolBase
{
    public override string Name => "calor_get_example";

    public override string Description =>
        "Get working Calor examples from self-test reference files. " +
        "Use 'list' to see available examples, or 'name' to search for a specific example by keyword.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "name": {
                    "type": "string",
                    "description": "Name or keyword to search for (e.g., 'foreach', 'async', 'generic', 'hello_world')"
                },
                "list": {
                    "type": "boolean",
                    "description": "If true, list all available examples with descriptions",
                    "default": false
                }
            }
        }
        """;

    private static readonly Dictionary<string, string> ScenarioDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["01_hello_world"] = "Basic hello world program with Console.WriteLine",
        ["02_fizzbuzz"] = "FizzBuzz implementation with loops and conditionals",
        ["03_contracts"] = "Function contracts with requires/ensures clauses",
        ["04_option_result"] = "Option and Result types for safe error handling",
        ["05_skill_syntax"] = "Skill (class) syntax with methods and properties",
        ["06_pattern_matching"] = "Pattern matching with match expressions",
        ["07_collections"] = "Collection types and operations (lists, arrays)",
        ["07_quantifiers"] = "Quantifier expressions (forall, exists) for contracts",
        ["08_contract_inheritance_z3"] = "Contract inheritance with Z3 verification",
        ["09_codegen_bugfixes"] = "Miscellaneous codegen edge cases and bug fixes",
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        try
        {
            var listMode = GetBool(arguments, "list");
            var nameFilter = GetString(arguments, "name");

            var scenarios = SelfTestRunner.LoadScenarios();

            if (listMode)
            {
                return Task.FromResult(McpToolResult.Json(new ExampleListOutput
                {
                    Examples = scenarios.Select(s => new ExampleSummary
                    {
                        Name = s.Name,
                        Description = GetDescription(s.Name)
                    }).ToList()
                }));
            }

            if (string.IsNullOrWhiteSpace(nameFilter))
            {
                return Task.FromResult(McpToolResult.Error(
                    "Provide 'name' to search for an example, or set 'list' to true to see all available examples."));
            }

            // Exact match first
            var match = scenarios.FirstOrDefault(s =>
                s.Name.Equals(nameFilter, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return Task.FromResult(McpToolResult.Json(new ExampleOutput
                {
                    Name = match.Name,
                    Description = GetDescription(match.Name),
                    CalorSource = match.Input,
                    ExpectedCSharp = match.ExpectedOutput
                }));
            }

            // Keyword search: match against name and description
            var keyword = nameFilter.ToLowerInvariant();
            var matches = scenarios
                .Where(s => s.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                         || GetDescription(s.Name).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1)
            {
                var single = matches[0];
                return Task.FromResult(McpToolResult.Json(new ExampleOutput
                {
                    Name = single.Name,
                    Description = GetDescription(single.Name),
                    CalorSource = single.Input,
                    ExpectedCSharp = single.ExpectedOutput
                }));
            }

            if (matches.Count > 1)
            {
                return Task.FromResult(McpToolResult.Json(new ExampleListOutput
                {
                    Examples = matches.Select(s => new ExampleSummary
                    {
                        Name = s.Name,
                        Description = GetDescription(s.Name)
                    }).ToList()
                }));
            }

            // No match — suggest closest names
            var available = scenarios.Select(s => new ExampleSummary
            {
                Name = s.Name,
                Description = GetDescription(s.Name)
            }).ToList();

            return Task.FromResult(McpToolResult.Json(new NoMatchOutput
            {
                Error = $"No example found matching '{nameFilter}'.",
                AvailableExamples = available
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Failed to load examples: {ex.Message}"));
        }
    }

    private static string GetDescription(string scenarioName)
    {
        return ScenarioDescriptions.TryGetValue(scenarioName, out var desc)
            ? desc
            : FormatNameAsDescription(scenarioName);
    }

    private static string FormatNameAsDescription(string name)
    {
        // Strip leading number prefix like "01_" and convert underscores to spaces
        var trimmed = name;
        if (trimmed.Length > 3 && char.IsDigit(trimmed[0]) && char.IsDigit(trimmed[1]) && trimmed[2] == '_')
            trimmed = trimmed[3..];
        return trimmed.Replace('_', ' ');
    }

    private sealed class ExampleListOutput
    {
        [JsonPropertyName("examples")]
        public required List<ExampleSummary> Examples { get; init; }
    }

    private sealed class ExampleSummary
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }
    }

    private sealed class ExampleOutput
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("calorSource")]
        public required string CalorSource { get; init; }

        [JsonPropertyName("expectedCSharp")]
        public required string ExpectedCSharp { get; init; }
    }

    private sealed class NoMatchOutput
    {
        [JsonPropertyName("error")]
        public required string Error { get; init; }

        [JsonPropertyName("availableExamples")]
        public required List<ExampleSummary> AvailableExamples { get; init; }
    }
}
