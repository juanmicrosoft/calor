using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

    public override int TimeoutSeconds => 120;

    public override string Description =>
        "Compile Calor source code to C#. Use autoFix: true to auto-fix parser, ID, and effect errors (up to 3 passes). " +
        "Each diagnostic includes a fix field with concrete edits. " +
        "Typically the first tool called after writing .calr code. Follow with calor_verify for contract checking.";

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
                    "description": "Path to a .calr file on disk. If 'source' is omitted, the file is read and compiled. Also used for diagnostic messages."
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
                        },
                        "autoFix": {
                            "type": "boolean",
                            "default": false,
                            "description": "Auto-fix high-confidence errors (parser, ID, effects). Compile → apply fixes → recompile, up to 3 passes. Returns fixed source and fix history."
                        }
                    }
                },
                "checkCompat": {
                    "type": "boolean",
                    "default": false,
                    "description": "After compilation, verify generated C# is API-compatible (namespace preservation, pattern checks)"
                },
                "expectedNamespace": {
                    "type": "string",
                    "description": "Expected namespace in generated code when checkCompat is true (e.g., 'Calor.Runtime')"
                },
                "expectedPatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Patterns that must appear in generated code when checkCompat is true"
                },
                "forbiddenPatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Patterns that must NOT appear in generated code when checkCompat is true"
                }
            },

            "additionalProperties": false
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
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
            return CompileBatch(filePaths, arguments, cancellationToken);
        }

        // If filePath is provided without source, read the file from disk
        var filePath = GetString(arguments, "filePath");
        if (source == null && filePath != null)
        {
            if (!File.Exists(filePath))
            {
                return McpToolResult.Error($"File not found: {filePath}");
            }
            source = await File.ReadAllTextAsync(filePath, cancellationToken);
        }

        // Single-file mode
        if (string.IsNullOrEmpty(source))
        {
            return McpToolResult.Error("Missing required parameter: provide 'source', 'files', or 'projectPath'");
        }

        filePath ??= "mcp-input.calr";
        return CompileSingle(source, filePath, arguments, cancellationToken);
    }

    private McpToolResult CompileSingle(string source, string filePath, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var options = GetOptions(arguments);

        var verify = GetBool(options, "verify");
        var analyze = GetBool(options, "analyze");
        var autoFix = GetBool(options, "autoFix");
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

            cancellationToken.ThrowIfCancellationRequested();
            var result = Program.Compile(source, filePath, compileOptions);

            // AutoFix: multi-pass compile→fix→recompile loop
            var fixesApplied = new List<string>();
            var fixedSource = source;
            if (autoFix && result.HasErrors)
            {
                const int maxPasses = 3;
                for (var pass = 0; pass < maxPasses; pass++)
                {
                    var diagnosticsWithFixes = result.Diagnostics.DiagnosticsWithFixes;
                    if (diagnosticsWithFixes.Count == 0)
                        break; // No fixes available

                    // Apply high-confidence fixes (parser Calor01xx + ID Calor08xx)
                    // Medium-confidence (effects Calor04xx) also applied since we generate them
                    var applicableFixes = diagnosticsWithFixes.ToList();
                    if (applicableFixes.Count == 0)
                        break;

                    var previousSource = fixedSource;
                    fixedSource = ApplyFixes(fixedSource, applicableFixes, out var applied);

                    if (fixedSource == previousSource)
                        break; // No changes — bail to prevent infinite loop

                    foreach (var fix in applicableFixes.Take(applied))
                        fixesApplied.Add($"{fix.Code}: {fix.Fix.Description}");

                    // Recompile with fixed source
                    cancellationToken.ThrowIfCancellationRequested();
                    result = Program.Compile(fixedSource, filePath, compileOptions);

                    if (!result.HasErrors)
                        break; // Success!
                }

                // Update source for the output if fixes were applied
                if (fixesApplied.Count > 0)
                    source = fixedSource;
            }

            // Build fix lookup from diagnostics-with-fixes (same pattern as CheckTool)
            var fixLookup = result.Diagnostics.DiagnosticsWithFixes
                .GroupBy(dwf => (dwf.Span.Line, dwf.Span.Column, dwf.Code, dwf.Message))
                .ToDictionary(g => g.Key, g => g.First());

            var output = new CompileToolOutput
            {
                Success = !result.HasErrors,
                GeneratedCode = result.HasErrors ? null : result.GeneratedCode,
                Diagnostics = result.Diagnostics.Select(d =>
                {
                    var diagOutput = new DiagnosticOutput
                    {
                        Severity = d.IsError ? "error" : "warning",
                        Code = d.Code.ToString(),
                        Message = d.Message,
                        Line = d.Span.Line,
                        Column = d.Span.Column
                    };

                    // Attach fix if available
                    var key = (d.Span.Line, d.Span.Column, d.Code, d.Message);
                    if (fixLookup.TryGetValue(key, out var diagnosticWithFix))
                    {
                        diagOutput.Fix = new FixOutput
                        {
                            Description = diagnosticWithFix.Fix.Description,
                            Edits = diagnosticWithFix.Fix.Edits.Select(e => new EditOutput
                            {
                                StartLine = e.StartLine,
                                StartColumn = e.StartColumn,
                                EndLine = e.EndLine,
                                EndColumn = e.EndColumn,
                                NewText = e.NewText
                            }).ToList()
                        };
                    }

                    return diagOutput;
                }).ToList()
            };

            // Add autoFix results if fixes were applied
            if (fixesApplied.Count > 0)
            {
                output.FixedSource = fixedSource;
                output.FixesApplied = fixesApplied;
            }

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

            // Run compat check if requested and compilation succeeded
            if (!result.HasErrors && GetBool(arguments, "checkCompat"))
            {
                var compatResult = RunCompatCheck(
                    result.GeneratedCode ?? "",
                    GetString(arguments, "expectedNamespace"),
                    GetStringArray(arguments, "expectedPatterns"),
                    GetStringArray(arguments, "forbiddenPatterns"));

                output.CompatCheck = compatResult;
                if (!compatResult.Compatible)
                    return McpToolResult.Json(output, isError: true);
            }

            return McpToolResult.Json(output, isError: result.HasErrors);
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Compilation failed: {ex.Message}");
        }
    }

    private static CompatCheckOutput RunCompatCheck(
        string generatedCode,
        string? expectedNamespace,
        List<string> expectedPatterns,
        List<string> forbiddenPatterns)
    {
        var issues = new List<string>();

        if (!string.IsNullOrEmpty(expectedNamespace))
        {
            var namespacePattern = $@"namespace\s+{Regex.Escape(expectedNamespace)}\b";
            if (!Regex.IsMatch(generatedCode, namespacePattern))
            {
                issues.Add($"Expected namespace '{expectedNamespace}' not found in generated code");
            }
        }

        foreach (var pattern in expectedPatterns)
        {
            if (!generatedCode.Contains(pattern))
            {
                issues.Add($"Expected pattern '{pattern}' not found in generated code");
            }
        }

        foreach (var pattern in forbiddenPatterns)
        {
            if (generatedCode.Contains(pattern))
            {
                issues.Add($"Forbidden pattern '{pattern}' found in generated code");
            }
        }

        return new CompatCheckOutput
        {
            Compatible = issues.Count == 0,
            Issues = issues
        };
    }

    private static McpToolResult CompileBatch(List<string> filePaths, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var results = new List<BatchFileCompileResult>();
        var totalErrors = 0;
        var errorCategories = new Dictionary<string, int>();

        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    /// <summary>
    /// Applies fix edits to source in reverse line order (same pattern as CheckTool.ApplyFixes).
    /// </summary>
    private static string ApplyFixes(string source,
        IReadOnlyList<Diagnostics.DiagnosticWithFix> diagnosticsWithFixes, out int fixesApplied)
    {
        var allEdits = diagnosticsWithFixes
            .SelectMany(d => d.Fix.Edits)
            .OrderByDescending(e => e.StartLine)
            .ThenByDescending(e => e.StartColumn)
            .ToList();

        fixesApplied = 0;
        if (allEdits.Count == 0) return source;

        var lines = source.Split('\n');

        foreach (var edit in allEdits)
        {
            var startLine = edit.StartLine - 1;
            var startCol = edit.StartColumn - 1;
            var endLine = edit.EndLine - 1;
            var endCol = edit.EndColumn - 1;

            if (startLine < 0 || startLine >= lines.Length) continue;
            if (endLine < 0 || endLine >= lines.Length) endLine = startLine;

            var beforeEdit = startCol >= 0 && startCol <= lines[startLine].Length
                ? lines[startLine][..startCol]
                : lines[startLine];
            var afterEdit = endCol >= 0 && endCol <= lines[endLine].Length
                ? lines[endLine][endCol..]
                : "";

            var newContent = beforeEdit + edit.NewText + afterEdit;
            var newLines = newContent.Split('\n');

            var lineList = lines.ToList();
            lineList.RemoveRange(startLine, endLine - startLine + 1);
            lineList.InsertRange(startLine, newLines);
            lines = lineList.ToArray();

            fixesApplied++;
        }

        return string.Join('\n', lines);
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

        [JsonPropertyName("compatCheck")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CompatCheckOutput? CompatCheck { get; set; }

        [JsonPropertyName("fixedSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FixedSource { get; set; }

        [JsonPropertyName("fixesApplied")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? FixesApplied { get; set; }
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

        [JsonPropertyName("fix")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FixOutput? Fix { get; set; }
    }

    private sealed class FixOutput
    {
        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("edits")]
        public required List<EditOutput> Edits { get; init; }
    }

    private sealed class EditOutput
    {
        [JsonPropertyName("startLine")]
        public int StartLine { get; init; }

        [JsonPropertyName("startColumn")]
        public int StartColumn { get; init; }

        [JsonPropertyName("endLine")]
        public int EndLine { get; init; }

        [JsonPropertyName("endColumn")]
        public int EndColumn { get; init; }

        [JsonPropertyName("newText")]
        public required string NewText { get; init; }
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

    private sealed class CompatCheckOutput
    {
        [JsonPropertyName("compatible")]
        public bool Compatible { get; init; }

        [JsonPropertyName("issues")]
        public required List<string> Issues { get; init; }
    }
}
