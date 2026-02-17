using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for verifying Calor contracts with Z3 SMT solver.
/// </summary>
public sealed class VerifyTool : McpToolBase
{
    public override string Name => "calor_verify";

    public override string Description =>
        "Verify Calor contracts using Z3 SMT solver. Returns verification results with counterexamples for failed contracts.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to verify"
                },
                "timeout": {
                    "type": "integer",
                    "default": 5000,
                    "description": "Z3 solver timeout per contract in milliseconds"
                }
            },
            "required": ["source"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));
        }

        var timeout = GetInt(arguments, "timeout", 5000);
        if (timeout <= 0) timeout = 5000;

        try
        {
            var diagnostics = new DiagnosticBag();
            var options = new CompilationOptions
            {
                VerifyContracts = true,
                VerificationTimeoutMs = (uint)timeout,
                VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
            };

            var result = Program.Compile(source, "mcp-verify.calr", options);

            var verificationResults = options.VerificationResults;
            var summary = verificationResults?.GetSummary() ?? new VerificationSummary(0, 0, 0, 0, 0);

            var functions = new List<FunctionVerificationOutput>();
            if (verificationResults != null)
            {
                foreach (var funcResult in verificationResults.Functions)
                {
                    var contracts = new List<ContractVerificationOutput>();

                    for (var i = 0; i < funcResult.PreconditionResults.Count; i++)
                    {
                        var r = funcResult.PreconditionResults[i];
                        contracts.Add(new ContractVerificationOutput
                        {
                            Type = "precondition",
                            Index = i,
                            Status = r.Status.ToString().ToLowerInvariant(),
                            Counterexample = r.CounterexampleDescription
                        });
                    }

                    for (var i = 0; i < funcResult.PostconditionResults.Count; i++)
                    {
                        var r = funcResult.PostconditionResults[i];
                        contracts.Add(new ContractVerificationOutput
                        {
                            Type = "postcondition",
                            Index = i,
                            Status = r.Status.ToString().ToLowerInvariant(),
                            Counterexample = r.CounterexampleDescription
                        });
                    }

                    functions.Add(new FunctionVerificationOutput
                    {
                        FunctionId = funcResult.FunctionId,
                        FunctionName = funcResult.FunctionName,
                        Contracts = contracts
                    });
                }
            }

            var output = new VerifyToolOutput
            {
                Success = !result.HasErrors && summary.Disproven == 0,
                Summary = new VerificationSummaryOutput
                {
                    Total = summary.Total,
                    Proven = summary.Proven,
                    Unproven = summary.Unproven,
                    Disproven = summary.Disproven,
                    Unsupported = summary.Unsupported,
                    Skipped = summary.Skipped
                },
                Functions = functions,
                CompilationErrors = result.Diagnostics.Errors.Select(d => d.Message).ToList()
            };

            return Task.FromResult(McpToolResult.Json(output, isError: !output.Success));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Verification failed: {ex.Message}"));
        }
    }

    private sealed class VerifyToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("summary")]
        public required VerificationSummaryOutput Summary { get; init; }

        [JsonPropertyName("functions")]
        public required List<FunctionVerificationOutput> Functions { get; init; }

        [JsonPropertyName("compilationErrors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? CompilationErrors { get; init; }
    }

    private sealed class VerificationSummaryOutput
    {
        [JsonPropertyName("total")]
        public int Total { get; init; }

        [JsonPropertyName("proven")]
        public int Proven { get; init; }

        [JsonPropertyName("unproven")]
        public int Unproven { get; init; }

        [JsonPropertyName("disproven")]
        public int Disproven { get; init; }

        [JsonPropertyName("unsupported")]
        public int Unsupported { get; init; }

        [JsonPropertyName("skipped")]
        public int Skipped { get; init; }
    }

    private sealed class FunctionVerificationOutput
    {
        [JsonPropertyName("functionId")]
        public required string FunctionId { get; init; }

        [JsonPropertyName("functionName")]
        public required string FunctionName { get; init; }

        [JsonPropertyName("contracts")]
        public required List<ContractVerificationOutput> Contracts { get; init; }
    }

    private sealed class ContractVerificationOutput
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("counterexample")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Counterexample { get; init; }
    }
}
