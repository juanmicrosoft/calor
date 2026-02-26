using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Verification.Obligations;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool: calor_discover_guards
/// Discovers guards that would discharge failed obligations.
/// </summary>
public sealed class GuardDiscoveryTool : McpToolBase
{
    public override string Name => "calor_discover_guards";

    public override string Description =>
        "Analyze failed obligations and discover the simplest guards (preconditions, if-guards) " +
        "that would discharge them. Returns ranked guard suggestions with Calor syntax.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code with refinement types or proof obligations"
                },
                "obligation_id": {
                    "type": "string",
                    "description": "Optional: specific obligation ID to discover guards for"
                }
            },
            "required": ["source"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));

        var obligationId = GetString(arguments, "obligation_id");

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                EnableTypeChecking = true
            };

            var result = Program.Compile(source, "mcp_guards.calr", options);

            var tracker = options.ObligationResults;
            if (tracker == null)
            {
                return Task.FromResult(McpToolResult.Json(new GuardDiscoveryOutput
                {
                    Success = true,
                    Guards = new List<GuardOutput>()
                }));
            }

            var discovery = new GuardDiscovery();
            var guards = obligationId != null
                ? tracker.Obligations
                    .Where(o => o.Id == obligationId)
                    .SelectMany(o => discovery.DiscoverForObligation(o))
                    .ToList()
                : discovery.DiscoverGuards(tracker);

            // Validate guards with Z3 if available
            if (result.Ast != null && result.Ast.Functions.Count > 0)
            {
                // Collect all parameters across functions for Z3 validation
                var allParams = result.Ast.Functions
                    .SelectMany(f => f.Parameters.Select(p => (p.Name, p.TypeName)))
                    .Distinct()
                    .ToList();
                discovery.ValidateWithZ3(guards, tracker.Obligations, allParams);
            }

            return Task.FromResult(McpToolResult.Json(new GuardDiscoveryOutput
            {
                Success = true,
                Guards = guards.Select(g => new GuardOutput
                {
                    ObligationId = g.ObligationId,
                    Description = g.Description,
                    CalorExpression = g.CalorExpression,
                    InsertionKind = g.InsertionKind,
                    Confidence = g.Confidence,
                    Validated = g.Validated
                }).ToList()
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Guard discovery failed: {ex.Message}"));
        }
    }

    private sealed class GuardDiscoveryOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("guards")]
        public required List<GuardOutput> Guards { get; init; }
    }

    private sealed class GuardOutput
    {
        [JsonPropertyName("obligation_id")]
        public required string ObligationId { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("calor_expression")]
        public required string CalorExpression { get; init; }

        [JsonPropertyName("insertion_kind")]
        public required string InsertionKind { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; init; }

        [JsonPropertyName("validated")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Validated { get; init; }
    }
}
