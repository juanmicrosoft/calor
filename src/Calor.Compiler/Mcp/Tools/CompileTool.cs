using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Ids;
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
        "Compile Calor source code to C#. Auto-fixes parser, ID, and effect errors by default. Follow with calor_verify for contracts.";

    // ReadOnlyHint: true — no disk writes (autoFix returns fixed source in response, doesn't write).
    // IdempotentHint: true — same input produces same output (autoFix is deterministic).
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
                        "keepProvenGuards": {
                            "type": "boolean",
                            "default": false,
                            "description": "Keep every runtime contract guard even when Z3 proves the contract (opts out of the v0.15 default elision; only matters with verify=true)"
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
                            "default": true,
                            "description": "Auto-fix high-confidence errors (parser, ID, effects). Set to false to skip auto-fix."
                        },
                        "enforceEffects": {
                            "type": "boolean",
                            "default": true,
                            "description": "Enforce effect declarations (the CLI's --enforce-effects; false is --no-enforce-effects)"
                        },
                        "requireDocs": {
                            "type": "boolean",
                            "default": false,
                            "description": "Require documentation on public functions and types (the CLI's --require-docs)"
                        },
                        "crossModule": {
                            "type": "boolean",
                            "default": false,
                            "description": "Batch mode only: compile the file set as ONE project the way `calor -i a.calr -i b.calr` does — cross-module effect enforcement, contractMode/effectMode/enforceEffects/requireDocs honored, every diagnostic returned as an envelope entry under each file's diagnostics[]. Without it, batch mode compiles each file alone with contracts off and effects permissive (migration triage)."
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
        var keepProvenGuards = GetBool(options, "keepProvenGuards");
        var analyze = GetBool(options, "analyze");
        var autoFix = GetBool(options, "autoFix", defaultValue: true);
        var (contractMode, unknownCallPolicy, strictEffects) = ParseModes(options);

        try
        {
            var compileOptions = new CompilationOptions
            {
                ContractMode = contractMode,
                UnknownCallPolicy = unknownCallPolicy,
                StrictEffects = strictEffects,
                EnforceEffects = GetBool(options, "enforceEffects", defaultValue: true),
                RequireDocs = GetBool(options, "requireDocs"),
                VerifyContracts = verify,
                ElideProvenGuards = !keepProvenGuards,
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

            // Envelope schema v1.1: diagnostics are the shared EnvelopeDiagnostic
            // entries; a resolver built from the parsed AST populates declarationId.
            DeclarationIdResolver? declarationIds = null;
            if (result.Ast != null)
            {
                declarationIds = new DeclarationIdResolver();
                declarationIds.AddFile(filePath, source, result.Ast);
            }

            var output = new CompileToolOutput
            {
                Success = !result.HasErrors,
                GeneratedCode = result.HasErrors ? null : result.GeneratedCode,
                Diagnostics = DiagnosticEnvelope.Build(result.Diagnostics, declarationIds)
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

    private static (ContractMode ContractMode, UnknownCallPolicy Policy, bool StrictEffects) ParseModes(JsonElement? options)
    {
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

        return (contractMode, unknownCallPolicy, strictEffects);
    }

    private static McpToolResult CompileBatch(List<string> filePaths, JsonElement? arguments, CancellationToken cancellationToken)
    {
        if (GetBool(GetOptions(arguments), "crossModule"))
            return CompileProject(filePaths, arguments, cancellationToken);

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
                    Errors = errors.Count > 0 ? errors : null,
                    Diagnostics = DiagnosticEnvelope.Build(result.Diagnostics, null)
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

    /// <summary>
    /// v0.16 E7 (roadmap gate 3, MCP leg) — batch mode with
    /// <c>options.crossModule</c>: the file set compiled as ONE project through
    /// the same driver <c>calor -i a.calr -i b.calr</c> uses, with cross-module
    /// effect enforcement and the CLI's option semantics, so the diagnostics an
    /// agent gets here are the diagnostics the CLI prints. Nothing is written to
    /// disk; every diagnostic is returned as an envelope entry on the file it
    /// belongs to (<c>projectDiagnostics</c> holds the few with no file).
    /// </summary>
    private static McpToolResult CompileProject(List<string> filePaths, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var options = GetOptions(arguments);
        var (contractMode, policy, strictEffects) = ParseModes(options);
        var enforceEffects = GetBool(options, "enforceEffects", defaultValue: true);
        var requireDocs = GetBool(options, "requireDocs");
        var verify = GetBool(options, "verify");
        var keepProvenGuards = GetBool(options, "keepProvenGuards");
        var analyze = GetBool(options, "analyze");

        var missing = filePaths.Where(path => !File.Exists(path)).ToList();
        if (missing.Count > 0)
            return McpToolResult.Error("File not found: " + string.Join(", ", missing));

        var sources = filePaths
            .Select(path => new FileInfo(Path.GetFullPath(path)))
            .ToList();
        var sink = new DiagnosticBag();
        var declarationIds = new DeclarationIdResolver();
        CompilationDriver.DriverResult driverResult;
        try
        {
            driverResult = CompilationDriver.CompileAll(
                sources,
                file => new CompilationOptions
                {
                    ContractMode = contractMode,
                    UnknownCallPolicy = policy,
                    StrictEffects = strictEffects,
                    EnforceEffects = enforceEffects,
                    RequireDocs = requireDocs,
                    VerifyContracts = verify,
                    ElideProvenGuards = !keepProvenGuards,
                    EnableVerificationAnalyses = analyze,
                    ProjectDirectory = Path.GetDirectoryName(file.FullName),
                    VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
                    CancellationToken = cancellationToken
                },
                crossModuleEnforcement: true,
                crossModulePolicy: policy,
                diagnosticSink: sink,
                onAst: (file, source, ast) => declarationIds.AddFile(file.FullName, source, ast));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Compilation failed: {ex.Message}");
        }

        var comparer = Incremental.BuildStateCache.GetPathComparer();
        var byFile = new Dictionary<string, List<Diagnostic>>(comparer);
        var unattributed = new List<Diagnostic>();
        foreach (var source in sources)
            byFile[source.FullName] = [];
        foreach (var diagnostic in sink)
        {
            if (diagnostic.FilePath is { Length: > 0 } path
                && byFile.TryGetValue(Path.GetFullPath(path), out var bucket))
            {
                bucket.Add(diagnostic);
            }
            else
            {
                unattributed.Add(diagnostic);
            }
        }

        var results = new List<BatchFileCompileResult>();
        var errorCategories = new Dictionary<string, int>();
        var failedFiles = 0;
        foreach (var source in sources)
        {
            var diagnostics = byFile[source.FullName];
            var errors = diagnostics
                .Where(d => d.IsError)
                .Select(d => $"[{d.Code}] L{d.Span.Line}: {d.Message}")
                .ToList();
            foreach (var d in diagnostics.Where(d => d.IsError))
                IncrementCategory(errorCategories, d.Code.ToString());
            if (errors.Count > 0)
                failedFiles++;
            results.Add(new BatchFileCompileResult
            {
                FilePath = source.FullName,
                Success = errors.Count == 0,
                ErrorCount = errors.Count,
                WarningCount = diagnostics.Count(d => !d.IsError),
                Errors = errors.Count > 0 ? errors : null,
                Diagnostics = diagnostics.Select(d => DiagnosticEnvelope.Build(d, declarationIds)).ToList()
            });
        }

        foreach (var d in unattributed.Where(d => d.IsError))
            IncrementCategory(errorCategories, d.Code.ToString());
        var anyErrors = driverResult.AnyErrors || sink.Any(d => d.IsError);

        var output = new BatchCompileOutput
        {
            Success = !anyErrors,
            TotalFiles = sources.Count,
            SuccessfulFiles = sources.Count - failedFiles,
            FailedFiles = failedFiles,
            ErrorCategories = errorCategories.Count > 0 ? errorCategories : null,
            Files = results,
            CrossModule = true,
            ProjectDiagnostics = unattributed.Count > 0
                ? unattributed.Select(d => DiagnosticEnvelope.Build(d, declarationIds)).ToList()
                : null
        };

        return McpToolResult.Json(output, isError: anyErrors);
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

        /// <summary>Envelope schema v1.1 diagnostic entries (shared EnvelopeDiagnostic shape).</summary>
        [JsonPropertyName("diagnostics")]
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }

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

        /// <summary>v0.16 E7: true when the set was compiled as one project (<c>options.crossModule</c>).</summary>
        [JsonPropertyName("crossModule")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool CrossModule { get; init; }

        /// <summary>v0.16 E7: diagnostics the driver attributed to no file (crossModule only).</summary>
        [JsonPropertyName("projectDiagnostics")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EnvelopeDiagnostic>? ProjectDiagnostics { get; init; }
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

        /// <summary>
        /// Every diagnostic on this file as an envelope entry (schema 2.0) —
        /// warnings and infos included, which the <c>errors</c> strings drop.
        /// </summary>
        [JsonPropertyName("diagnostics")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EnvelopeDiagnostic>? Diagnostics { get; init; }
    }

    private sealed class CompatCheckOutput
    {
        [JsonPropertyName("compatible")]
        public bool Compatible { get; init; }

        [JsonPropertyName("issues")]
        public required List<string> Issues { get; init; }
    }
}
