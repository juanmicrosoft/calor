using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Effects;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for compiling Calor source code to C#.
/// </summary>
public sealed class CompileTool : McpToolBase
{
    public override string Name => "calor_compile";

    public override string Description =>
        "Compile Calor source code to C#. Returns generated C# code and any compilation diagnostics.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to compile"
                },
                "filePath": {
                    "type": "string",
                    "description": "Optional file path for diagnostic messages"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "verify": {
                            "type": "boolean",
                            "default": false,
                            "description": "Enable Z3 contract verification"
                        },
                        "analyze": {
                            "type": "boolean",
                            "default": false,
                            "description": "Enable advanced analyses (dataflow, bug patterns, taint)"
                        },
                        "contractMode": {
                            "type": "string",
                            "enum": ["off", "debug", "release"],
                            "default": "debug",
                            "description": "Contract enforcement mode"
                        },
                        "effectMode": {
                            "type": "string",
                            "enum": ["strict", "default", "permissive"],
                            "default": "default",
                            "description": "Effect enforcement mode: strict (errors for unknown calls), default (warnings), permissive (suppress all effect errors, for converted code)"
                        }
                    }
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

        var filePath = GetString(arguments, "filePath") ?? "mcp-input.calr";
        var options = GetOptions(arguments);

        var verify = GetBool(options, "verify");
        var analyze = GetBool(options, "analyze");
        var contractModeStr = GetString(options, "contractMode") ?? "debug";
        var effectModeStr = GetString(options, "effectMode") ?? "default";

        var contractMode = contractModeStr.ToLowerInvariant() switch
        {
            "off" => ContractMode.Off,
            "release" => ContractMode.Release,
            _ => ContractMode.Debug
        };

        var (unknownCallPolicy, strictEffects) = effectModeStr.ToLowerInvariant() switch
        {
            "strict" => (UnknownCallPolicy.Strict, true),
            "permissive" => (UnknownCallPolicy.Permissive, false),
            _ => (UnknownCallPolicy.Strict, false)
        };

        try
        {
            var compileOptions = new CompilationOptions
            {
                ContractMode = contractMode,
                UnknownCallPolicy = unknownCallPolicy,
                StrictEffects = strictEffects,
                VerifyContracts = verify,
                EnableVerificationAnalyses = analyze,
                VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
            };

            var result = Program.Compile(source, filePath, compileOptions);

            var output = new CompileToolOutput
            {
                Success = !result.HasErrors,
                GeneratedCode = result.HasErrors ? null : result.GeneratedCode,
                Diagnostics = result.Diagnostics.Select(d => new DiagnosticOutput
                {
                    Severity = d.IsError ? "error" : "warning",
                    Code = d.Code.ToString(),
                    Message = d.Message,
                    Line = d.Span.Line,
                    Column = d.Span.Column
                }).ToList()
            };

            if (verify && compileOptions.VerificationResults != null)
            {
                var summary = compileOptions.VerificationResults.GetSummary();
                output.VerificationSummary = new VerificationSummaryOutput
                {
                    Proven = summary.Proven,
                    Unproven = summary.Unproven,
                    Disproven = summary.Disproven,
                    Unsupported = summary.Unsupported
                };
            }

            if (analyze && compileOptions.VerificationAnalysisResult != null)
            {
                var analysisResult = compileOptions.VerificationAnalysisResult;
                output.AnalysisSummary = new AnalysisSummaryOutput
                {
                    FunctionsAnalyzed = analysisResult.FunctionsAnalyzed,
                    BugPatternsFound = analysisResult.BugPatternsFound,
                    TaintVulnerabilities = analysisResult.TaintVulnerabilities,
                    DataflowIssues = analysisResult.DataflowIssues
                };
            }

            return Task.FromResult(McpToolResult.Json(output, isError: result.HasErrors));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Compilation failed: {ex.Message}"));
        }
    }

    private sealed class CompileToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("generatedCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GeneratedCode { get; init; }

        [JsonPropertyName("diagnostics")]
        public required List<DiagnosticOutput> Diagnostics { get; init; }

        [JsonPropertyName("verificationSummary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public VerificationSummaryOutput? VerificationSummary { get; set; }

        [JsonPropertyName("analysisSummary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AnalysisSummaryOutput? AnalysisSummary { get; set; }
    }

    private sealed class DiagnosticOutput
    {
        [JsonPropertyName("severity")]
        public required string Severity { get; init; }

        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }
    }

    private sealed class VerificationSummaryOutput
    {
        [JsonPropertyName("proven")]
        public int Proven { get; init; }

        [JsonPropertyName("unproven")]
        public int Unproven { get; init; }

        [JsonPropertyName("disproven")]
        public int Disproven { get; init; }

        [JsonPropertyName("unsupported")]
        public int Unsupported { get; init; }
    }

    private sealed class AnalysisSummaryOutput
    {
        [JsonPropertyName("functionsAnalyzed")]
        public int FunctionsAnalyzed { get; init; }

        [JsonPropertyName("bugPatternsFound")]
        public int BugPatternsFound { get; init; }

        [JsonPropertyName("taintVulnerabilities")]
        public int TaintVulnerabilities { get; init; }

        [JsonPropertyName("dataflowIssues")]
        public int DataflowIssues { get; init; }
    }
}
