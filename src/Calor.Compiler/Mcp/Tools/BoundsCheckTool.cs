using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Verification.Obligations;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool: calor_bounds_check
/// Analyzes Calor source for index access sites on indexed types and reports
/// whether each access is provably safe (discharged), fails with a counterexample,
/// or is at a public boundary.
/// </summary>
public sealed class BoundsCheckTool : McpToolBase
{
    public override string Name => "calor_bounds_check";

    public override string Description =>
        "Analyze index access sites on indexed types (§ITYPE) and verify bounds safety using Z3. " +
        "Reports which accesses are proven safe, which fail with counterexamples, and boundary status.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code containing §ITYPE definitions and array accesses"
                },
                "function_id": {
                    "type": "string",
                    "description": "Optional: filter results to a specific function ID"
                }
            },
            "required": ["source"]
        ,

        "additionalProperties": false

        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));

        var functionId = GetString(arguments, "function_id");

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                EnableTypeChecking = true
            };

            Program.Compile(source, "mcp_bounds.calr", options);

            var tracker = options.ObligationResults;

            var accessSites = new List<AccessSiteOutput>();

            if (tracker != null)
            {
                var obligations = tracker.Obligations
                    .Where(o => o.Kind == ObligationKind.IndexBounds);

                if (!string.IsNullOrEmpty(functionId))
                    obligations = obligations.Where(o => o.FunctionId == functionId);

                foreach (var obl in obligations)
                {
                    accessSites.Add(new AccessSiteOutput
                    {
                        ObligationId = obl.Id,
                        FunctionId = obl.FunctionId,
                        ArrayName = obl.ParameterName ?? "unknown",
                        Status = obl.Status.ToString(),
                        Description = obl.Description,
                        Counterexample = obl.CounterexampleDescription,
                        SuggestedFix = obl.SuggestedFix,
                        SolverDurationMs = obl.SolverDuration?.TotalMilliseconds
                    });
                }
            }

            var summary = tracker?.GetSummary();
            var indexBoundsCount = tracker?.Obligations.Count(o => o.Kind == ObligationKind.IndexBounds) ?? 0;
            var indexBoundsDischarged = tracker?.Obligations.Count(o => o.Kind == ObligationKind.IndexBounds && o.Status == ObligationStatus.Discharged) ?? 0;
            var indexBoundsFailed = tracker?.Obligations.Count(o => o.Kind == ObligationKind.IndexBounds && o.Status == ObligationStatus.Failed) ?? 0;

            return Task.FromResult(McpToolResult.Json(new BoundsCheckOutput
            {
                Safe = indexBoundsFailed == 0,
                TotalAccessSites = indexBoundsCount,
                Discharged = indexBoundsDischarged,
                Failed = indexBoundsFailed,
                AccessSites = accessSites
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Bounds check failed: {ex.Message}"));
        }
    }

    private sealed class BoundsCheckOutput
    {
        [JsonPropertyName("safe")]
        public bool Safe { get; init; }

        [JsonPropertyName("total_access_sites")]
        public int TotalAccessSites { get; init; }

        [JsonPropertyName("discharged")]
        public int Discharged { get; init; }

        [JsonPropertyName("failed")]
        public int Failed { get; init; }

        [JsonPropertyName("access_sites")]
        public required List<AccessSiteOutput> AccessSites { get; init; }
    }

    private sealed class AccessSiteOutput
    {
        [JsonPropertyName("obligation_id")]
        public required string ObligationId { get; init; }

        [JsonPropertyName("function_id")]
        public required string FunctionId { get; init; }

        [JsonPropertyName("array_name")]
        public required string ArrayName { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("counterexample")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Counterexample { get; init; }

        [JsonPropertyName("suggested_fix")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SuggestedFix { get; init; }

        [JsonPropertyName("solver_duration_ms")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? SolverDurationMs { get; init; }
    }
}
