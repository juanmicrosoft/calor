using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Verification.Obligations;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool: calor_diagnose_refinement
/// The repair-loop tool. Takes source with failures, returns structured patches
/// with confidence and which obligations each patch discharges.
/// </summary>
public sealed class DiagnoseRefinementTool : McpToolBase
{
    public override string Name => "calor_diagnose_refinement";

    public override string Description =>
        "Diagnose refinement type failures and produce structured repair patches. " +
        "Combines obligation analysis, guard discovery, and type suggestions into " +
        "ranked patches with confidence scores and obligation discharge mapping.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code with refinement type failures"
                },
                "policy": {
                    "type": "string",
                    "enum": ["default", "strict", "permissive"],
                    "default": "default",
                    "description": "Obligation policy: default, strict (all errors), permissive (all guards)"
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

        var policyName = GetString(arguments, "policy") ?? "default";

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                EnableTypeChecking = true,
                CancellationToken = cancellationToken
            };

            var result = Program.Compile(source, "mcp_diagnose.calr", options);

            var tracker = options.ObligationResults;

            // Get type suggestions from usage analysis (reuse AST from compilation)
            var typeSuggestions = new List<TypeSuggestion>();
            if (result.Ast != null)
            {
                var typeSuggester = new TypeSuggester();
                typeSuggestions = typeSuggester.Suggest(result.Ast);
            }

            // Get guard discoveries
            var guardDiscovery = new GuardDiscovery();
            var guards = tracker != null
                ? guardDiscovery.DiscoverGuards(tracker)
                : new List<DiscoveredGuard>();

            // Build policy
            var policy = policyName switch
            {
                "strict" => ObligationPolicy.Strict,
                "permissive" => ObligationPolicy.Permissive,
                _ => ObligationPolicy.Default
            };

            // Build structured patches
            var patches = new List<PatchOutput>();

            if (tracker != null)
            {
                foreach (var obl in tracker.Obligations)
                {
                    var action = policy.GetAction(obl.Status);
                    if (action == ObligationAction.Ignore)
                        continue;

                    // Find guards for this obligation
                    var oblGuards = guards
                        .Where(g => g.ObligationId == obl.Id)
                        .ToList();

                    foreach (var guard in oblGuards)
                    {
                        patches.Add(new PatchOutput
                        {
                            ObligationId = obl.Id,
                            ObligationStatus = obl.Status.ToString(),
                            PolicyAction = action.ToString(),
                            PatchKind = guard.InsertionKind,
                            CalorCode = guard.CalorExpression,
                            Description = guard.Description,
                            Confidence = guard.Confidence,
                            DischargesObligations = new List<string> { obl.Id }
                        });
                    }
                }
            }

            // Add type suggestion patches
            foreach (var ts in typeSuggestions)
            {
                patches.Add(new PatchOutput
                {
                    ObligationId = null,
                    ObligationStatus = null,
                    PolicyAction = "suggest",
                    PatchKind = "refine_parameter",
                    CalorCode = ts.CalorSyntax,
                    Description = ts.Reason,
                    Confidence = ts.Confidence,
                    DischargesObligations = new List<string>()
                });
            }

            var summary = tracker?.GetSummary();

            return Task.FromResult(McpToolResult.Json(new DiagnoseOutput
            {
                Success = summary == null || summary.Failed == 0,
                Policy = policyName,
                Summary = summary != null ? new SummaryOutput
                {
                    Total = summary.Total,
                    Discharged = summary.Discharged,
                    Failed = summary.Failed,
                    Timeout = summary.Timeout,
                    Boundary = summary.Boundary
                } : null,
                Patches = patches,
                CompilationErrors = result.Diagnostics.Errors.Select(d => d.Message).ToList()
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Refinement diagnosis failed: {ex.Message}"));
        }
    }

    private sealed class DiagnoseOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("policy")]
        public required string Policy { get; init; }

        [JsonPropertyName("summary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SummaryOutput? Summary { get; init; }

        [JsonPropertyName("patches")]
        public required List<PatchOutput> Patches { get; init; }

        [JsonPropertyName("compilation_errors")]
        public required List<string> CompilationErrors { get; init; }
    }

    private sealed class SummaryOutput
    {
        [JsonPropertyName("total")]
        public int Total { get; init; }

        [JsonPropertyName("discharged")]
        public int Discharged { get; init; }

        [JsonPropertyName("failed")]
        public int Failed { get; init; }

        [JsonPropertyName("timeout")]
        public int Timeout { get; init; }

        [JsonPropertyName("boundary")]
        public int Boundary { get; init; }
    }

    private sealed class PatchOutput
    {
        [JsonPropertyName("obligation_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ObligationId { get; init; }

        [JsonPropertyName("obligation_status")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ObligationStatus { get; init; }

        [JsonPropertyName("policy_action")]
        public required string PolicyAction { get; init; }

        [JsonPropertyName("patch_kind")]
        public required string PatchKind { get; init; }

        [JsonPropertyName("calor_code")]
        public required string CalorCode { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; init; }

        [JsonPropertyName("discharges_obligations")]
        public required List<string> DischargesObligations { get; init; }
    }
}
