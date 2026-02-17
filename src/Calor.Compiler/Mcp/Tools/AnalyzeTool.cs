using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Verification.Z3.Cache;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for running security and bug pattern analyses on Calor code.
/// </summary>
public sealed class AnalyzeTool : McpToolBase
{
    public override string Name => "calor_analyze";

    public override string Description =>
        "Analyze Calor source code for security vulnerabilities, bug patterns, and dataflow issues.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to analyze"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "enableDataflow": {
                            "type": "boolean",
                            "default": true,
                            "description": "Enable dataflow analysis (uninitialized variables, dead code)"
                        },
                        "enableBugPatterns": {
                            "type": "boolean",
                            "default": true,
                            "description": "Enable bug pattern detection (div by zero, null deref)"
                        },
                        "enableTaintAnalysis": {
                            "type": "boolean",
                            "default": true,
                            "description": "Enable security taint analysis"
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

        var options = GetOptions(arguments);
        var enableDataflow = GetBool(options, "enableDataflow", defaultValue: true);
        var enableBugPatterns = GetBool(options, "enableBugPatterns", defaultValue: true);
        var enableTaintAnalysis = GetBool(options, "enableTaintAnalysis", defaultValue: true);

        try
        {
            var analysisOptions = new VerificationAnalysisOptions
            {
                EnableDataflow = enableDataflow,
                EnableBugPatterns = enableBugPatterns,
                EnableTaintAnalysis = enableTaintAnalysis,
                EnableKInduction = false,
                UseZ3Verification = true
            };

            var compileOptions = new CompilationOptions
            {
                EnableVerificationAnalyses = true,
                VerificationAnalysisOptions = analysisOptions,
                VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
            };

            var result = Program.Compile(source, "mcp-analyze.calr", compileOptions);

            var analysisResult = compileOptions.VerificationAnalysisResult;

            var diagnosticsByCategory = result.Diagnostics
                .GroupBy(d => CategorizeIssue(d.Code.ToString()))
                .ToDictionary(g => g.Key, g => g.Select(d => new IssueOutput
                {
                    Code = d.Code.ToString(),
                    Message = d.Message,
                    Severity = d.IsError ? "error" : "warning",
                    Line = d.Span.Line,
                    Column = d.Span.Column
                }).ToList());

            var output = new AnalyzeToolOutput
            {
                Success = !result.HasErrors,
                Summary = new AnalysisSummaryOutput
                {
                    FunctionsAnalyzed = analysisResult?.FunctionsAnalyzed ?? 0,
                    DataflowIssues = analysisResult?.DataflowIssues ?? 0,
                    BugPatternsFound = analysisResult?.BugPatternsFound ?? 0,
                    TaintVulnerabilities = analysisResult?.TaintVulnerabilities ?? 0,
                    DurationMs = (int)(analysisResult?.Duration.TotalMilliseconds ?? 0)
                },
                SecurityIssues = diagnosticsByCategory.GetValueOrDefault("security", []),
                BugPatterns = diagnosticsByCategory.GetValueOrDefault("bugpattern", []),
                DataflowIssues = diagnosticsByCategory.GetValueOrDefault("dataflow", []),
                OtherIssues = diagnosticsByCategory.GetValueOrDefault("other", [])
            };

            var hasIssues = output.SecurityIssues.Count > 0 ||
                           output.BugPatterns.Count > 0 ||
                           output.DataflowIssues.Count > 0;

            return Task.FromResult(McpToolResult.Json(output, isError: result.HasErrors));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Analysis failed: {ex.Message}"));
        }
    }

    private static string CategorizeIssue(string code)
    {
        // Taint/security issues
        if (code.Contains("Taint") || code.Contains("Security") ||
            code.Contains("Injection") || code.Contains("Xss"))
            return "security";

        // Bug patterns
        if (code.Contains("DivideByZero") || code.Contains("NullDeref") ||
            code.Contains("Overflow") || code.Contains("OutOfBounds") ||
            code.Contains("BugPattern"))
            return "bugpattern";

        // Dataflow issues
        if (code.Contains("Uninitialized") || code.Contains("DeadStore") ||
            code.Contains("DeadCode") || code.Contains("Dataflow"))
            return "dataflow";

        return "other";
    }

    private sealed class AnalyzeToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("summary")]
        public required AnalysisSummaryOutput Summary { get; init; }

        [JsonPropertyName("securityIssues")]
        public required List<IssueOutput> SecurityIssues { get; init; }

        [JsonPropertyName("bugPatterns")]
        public required List<IssueOutput> BugPatterns { get; init; }

        [JsonPropertyName("dataflowIssues")]
        public required List<IssueOutput> DataflowIssues { get; init; }

        [JsonPropertyName("otherIssues")]
        public required List<IssueOutput> OtherIssues { get; init; }
    }

    private sealed class AnalysisSummaryOutput
    {
        [JsonPropertyName("functionsAnalyzed")]
        public int FunctionsAnalyzed { get; init; }

        [JsonPropertyName("dataflowIssues")]
        public int DataflowIssues { get; init; }

        [JsonPropertyName("bugPatternsFound")]
        public int BugPatternsFound { get; init; }

        [JsonPropertyName("taintVulnerabilities")]
        public int TaintVulnerabilities { get; init; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }
    }

    private sealed class IssueOutput
    {
        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("severity")]
        public required string Severity { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }
    }
}
