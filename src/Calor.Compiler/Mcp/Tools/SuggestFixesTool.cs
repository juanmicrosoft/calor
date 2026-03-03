using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Obligations;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool: calor_suggest_fixes
/// Given a source with failed obligations, returns ranked fix templates.
/// </summary>
public sealed class SuggestFixesTool : McpToolBase
{
    public override string Name => "calor_suggest_fixes";

    public override string Description =>
        "Analyze failed obligations and suggest ranked fixes. " +
        "Returns fix templates: add precondition, add guard, refine parameter, mark unsafe.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code with refinement types"
                },
                "obligation_id": {
                    "type": "string",
                    "description": "Optional: specific obligation ID to suggest fixes for. If omitted, suggests fixes for all failed obligations."
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

        var obligationId = GetString(arguments, "obligation_id");

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                EnableTypeChecking = true
            };

            var result = Program.Compile(source, "mcp_suggest.calr", options);

            var tracker = options.ObligationResults;
            if (tracker == null)
            {
                return Task.FromResult(McpToolResult.Json(new SuggestFixesOutput
                {
                    Success = true,
                    Fixes = new List<FixSuggestion>()
                }));
            }

            var targetObligations = obligationId != null
                ? tracker.Obligations.Where(o => o.Id == obligationId).ToList()
                : tracker.GetFailed();

            var fixes = new List<FixSuggestion>();

            foreach (var obl in targetObligations)
            {
                fixes.AddRange(GenerateFixSuggestions(obl));
            }

            return Task.FromResult(McpToolResult.Json(new SuggestFixesOutput
            {
                Success = true,
                Fixes = fixes
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Fix suggestion failed: {ex.Message}"));
        }
    }

    private static List<FixSuggestion> GenerateFixSuggestions(Obligation obligation)
    {
        var fixes = new List<FixSuggestion>();
        var paramName = ExtractParameterName(obligation.Description);

        switch (obligation.Kind)
        {
            case ObligationKind.RefinementEntry:
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "add_precondition",
                    Description = $"Add a §Q precondition to require callers satisfy the refinement constraint",
                    Confidence = "high",
                    Template = paramName != null
                        ? $"§Q (>= {paramName} INT:0)"
                        : "§Q (condition)"
                });
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "add_guard",
                    Description = "Add a runtime guard at the start of the function body",
                    Confidence = "medium",
                    Template = paramName != null
                        ? $"§IF{{g1}} (< {paramName} INT:0) → §R (err \"Invalid {paramName}\")"
                        : "§IF{g1} (not condition) → §R (err \"constraint violated\")"
                });
                break;

            case ObligationKind.ProofObligation:
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "strengthen_precondition",
                    Description = "Add or strengthen preconditions so the proof obligation can be discharged",
                    Confidence = "high",
                    Template = "§Q (condition-that-implies-proof)"
                });
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "add_guard",
                    Description = "Wrap the code path with a guard that ensures the condition",
                    Confidence = "medium",
                    Template = "§IF{g1} (not condition) → §R (err \"condition not met\")"
                });
                break;

            case ObligationKind.Subtype:
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "refine_parameter",
                    Description = "Change the source parameter to use the refined type",
                    Confidence = "high",
                    Template = "§I{RefinedType:param}"
                });
                break;

            default:
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "mark_unsafe",
                    Description = "Suppress this obligation (not recommended — obligation remains unverified)",
                    Confidence = "low",
                    Template = "// SUPPRESS: " + obligation.Id
                });
                break;
        }

        return fixes;
    }

    private static string? ExtractParameterName(string description)
    {
        var start = description.IndexOf('\'');
        var end = description.IndexOf('\'', start + 1);
        if (start >= 0 && end > start)
            return description[(start + 1)..end];
        return null;
    }

    private sealed class SuggestFixesOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("fixes")]
        public required List<FixSuggestion> Fixes { get; init; }
    }

    private sealed class FixSuggestion
    {
        [JsonPropertyName("obligation_id")]
        public required string ObligationId { get; init; }

        [JsonPropertyName("strategy")]
        public required string Strategy { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; init; }

        [JsonPropertyName("template")]
        public required string Template { get; init; }
    }
}
