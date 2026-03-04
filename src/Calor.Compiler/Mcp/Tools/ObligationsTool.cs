using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool: calor_obligations
/// Generates and verifies refinement type obligations for Calor source code.
/// </summary>
public sealed class ObligationsTool : McpToolBase
{
    public override string Name => "calor_obligations";

    public override string Description =>
        "Generate and verify refinement type obligations. Returns structured obligation data " +
        "with status (discharged, failed, timeout, boundary, unsupported), counterexamples, " +
        "and suggested fixes.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code containing refinement types and/or proof obligations"
                },
                "timeout": {
                    "type": "integer",
                    "default": 5000,
                    "description": "Z3 solver timeout per obligation in milliseconds"
                },
                "function_id": {
                    "type": "string",
                    "description": "Optional: filter obligations to a specific function ID"
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

        var timeout = (uint)GetInt(arguments, "timeout", 5000);
        var functionId = GetString(arguments, "function_id");

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                VerificationTimeoutMs = timeout,
                EnableTypeChecking = true,
                CancellationToken = cancellationToken
            };

            var result = Program.Compile(source, "mcp_obligations.calr", options);

            var tracker = options.ObligationResults;
            if (tracker == null)
            {
                return Task.FromResult(McpToolResult.Json(new ObligationsOutput
                {
                    Success = true,
                    Summary = new SummaryOutput(),
                    Obligations = new List<ObligationOutput>(),
                    CompilationErrors = result.Diagnostics.Errors.Select(d => d.Message).ToList()
                }));
            }

            var obligations = functionId != null
                ? tracker.GetByFunction(functionId)
                : tracker.Obligations;

            var summary = tracker.GetSummary();

            var output = new ObligationsOutput
            {
                Success = !result.Diagnostics.HasErrors && summary.Failed == 0,
                Summary = new SummaryOutput
                {
                    Total = summary.Total,
                    Discharged = summary.Discharged,
                    Failed = summary.Failed,
                    Timeout = summary.Timeout,
                    Boundary = summary.Boundary,
                    Pending = summary.Pending,
                    Unsupported = summary.Unsupported
                },
                Obligations = obligations.Select(o => new ObligationOutput
                {
                    Id = o.Id,
                    Kind = o.Kind.ToString(),
                    FunctionId = o.FunctionId,
                    Description = o.Description,
                    Status = o.Status.ToString(),
                    Line = o.Span.Line,
                    Column = o.Span.Column,
                    Counterexample = o.CounterexampleDescription,
                    SuggestedFix = o.SuggestedFix,
                    SolverDurationMs = o.SolverDuration?.TotalMilliseconds
                }).ToList(),
                CompilationErrors = result.Diagnostics.Errors.Select(d => d.Message).ToList()
            };

            return Task.FromResult(McpToolResult.Json(output, isError: !output.Success));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Obligation verification failed: {ex.Message}"));
        }
    }

    private sealed class ObligationsOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("summary")]
        public required SummaryOutput Summary { get; init; }

        [JsonPropertyName("obligations")]
        public required List<ObligationOutput> Obligations { get; init; }

        [JsonPropertyName("compilationErrors")]
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

        [JsonPropertyName("pending")]
        public int Pending { get; init; }

        [JsonPropertyName("unsupported")]
        public int Unsupported { get; init; }
    }

    private sealed class ObligationOutput
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("function_id")]
        public required string FunctionId { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }

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
