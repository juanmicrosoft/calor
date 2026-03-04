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

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to compile (single file mode)"
                },
                "files": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Array of .calr file paths to compile (batch mode, alternative to source)"
                },
                "projectPath": {
                    "type": "string",
                    "description": "Path to directory containing .calr files to compile (batch mode, alternative to source)"
                },
                "filePath": {
                    "type": "string",
                    "description": "Optional file path for diagnostic messages (single file mode)"
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

            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var source = GetString(arguments, "source");
        var projectPath = GetString(arguments, "projectPath");

        // Collect batch file paths from either 'files' array or 'projectPath' directory
        var filePaths = new List<string>();
        if (arguments.HasValue && arguments.Value.TryGetProperty("files", out var filesElement)
            && filesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in filesElement.EnumerateArray())
            {
                var path = item.GetString();
                if (!string.IsNullOrEmpty(path))
                    filePaths.Add(path);
            }
        }

        if (!string.IsNullOrEmpty(projectPath) && Directory.Exists(projectPath))
        {
            filePaths.AddRange(Directory.EnumerateFiles(projectPath, "*.calr", SearchOption.AllDirectories));
        }

        // Batch mode: compile multiple files
        if (filePaths.Count > 0)
        {
            return Task.FromResult(CompileBatch(filePaths, arguments, cancellationToken));
        }

        // Single-file mode
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: provide 'source', 'files', or 'projectPath'"));
        }

        var filePath = GetString(arguments, "filePath") ?? "mcp-input.calr";
        return Task.FromResult(CompileSingle(source, filePath, arguments, cancellationToken));
    }

    private McpToolResult CompileSingle(string source, string filePath, JsonElement? arguments, CancellationToken cancellationToken)
    {
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
                VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
                CancellationToken = cancellationToken
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

            return McpToolResult.Json(output, isError: result.HasErrors);
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Compilation failed: {ex.Message}");
        }
    }

    private static McpToolResult CompileBatch(List<string> filePaths, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var results = new List<BatchFileCompileResult>();
        var totalErrors = 0;
        var errorCategories = new Dictionary<string, int>();

        foreach (var path in filePaths)
        {
            try
            {
                if (!File.Exists(path))
                {
                    results.Add(new BatchFileCompileResult
                    {
                        FilePath = path,
                        Success = false,
                        ErrorCount = 1,
                        Errors = new List<string> { $"File not found: {path}" }
                    });
                    totalErrors++;
                    IncrementCategory(errorCategories, "file_not_found");
                    continue;
                }

                var source = File.ReadAllText(path);
                var compileOptions = new CompilationOptions
                {
                    ContractMode = ContractMode.Off,
                    UnknownCallPolicy = UnknownCallPolicy.Permissive,
                    VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
                    CancellationToken = cancellationToken
                };

                var result = Program.Compile(source, path, compileOptions);

                var errors = result.Diagnostics
                    .Where(d => d.IsError)
                    .Select(d => $"[{d.Code}] L{d.Span.Line}: {d.Message}")
                    .ToList();

                // Track error categories by error code
                foreach (var d in result.Diagnostics.Where(d => d.IsError))
                {
                    IncrementCategory(errorCategories, d.Code.ToString());
                }

                results.Add(new BatchFileCompileResult
                {
                    FilePath = path,
                    Success = !result.HasErrors,
                    ErrorCount = errors.Count,
                    WarningCount = result.Diagnostics.Count(d => !d.IsError),
                    Errors = errors.Count > 0 ? errors : null
                });

                if (result.HasErrors) totalErrors++;
            }
            catch (Exception ex)
            {
                results.Add(new BatchFileCompileResult
                {
                    FilePath = path,
                    Success = false,
                    ErrorCount = 1,
                    Errors = new List<string> { ex.Message }
                });
                totalErrors++;
                IncrementCategory(errorCategories, "exception");
            }
        }

        var output = new BatchCompileOutput
        {
            Success = totalErrors == 0,
            TotalFiles = filePaths.Count,
            SuccessfulFiles = filePaths.Count - totalErrors,
            FailedFiles = totalErrors,
            ErrorCategories = errorCategories.Count > 0 ? errorCategories : null,
            Files = results
        };

        return McpToolResult.Json(output, isError: totalErrors > 0);
    }

    private static void IncrementCategory(Dictionary<string, int> categories, string key)
    {
        categories[key] = categories.GetValueOrDefault(key) + 1;
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

    private sealed class BatchCompileOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("totalFiles")]
        public int TotalFiles { get; init; }

        [JsonPropertyName("successfulFiles")]
        public int SuccessfulFiles { get; init; }

        [JsonPropertyName("failedFiles")]
        public int FailedFiles { get; init; }

        [JsonPropertyName("errorCategories")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, int>? ErrorCategories { get; init; }

        [JsonPropertyName("files")]
        public required List<BatchFileCompileResult> Files { get; init; }
    }

    private sealed class BatchFileCompileResult
    {
        [JsonPropertyName("filePath")]
        public required string FilePath { get; init; }

        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; init; }

        [JsonPropertyName("warningCount")]
        public int WarningCount { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Errors { get; init; }
    }
}
