using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Mcp.Sessions;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for transactional whole-file writes of .calr sources (loop plan
/// WS2 D2.4): heal → check → apply-or-reject. Runs the calor_edit_preview
/// check set (compile, contracts, effects, references) against the current
/// on-disk content, applies atomically (temp file + rename) when the verdict
/// is safe or safe_with_warnings, and rejects breaking edits with envelope
/// schema v1.1 diagnostics and the file untouched. With a sessionId from
/// calor_session_open, reference checks widen to the session's whole file
/// set. Auto-heal (WS2 D2.5) fixes common serialization slips — forbidden
/// closer tags, indentation drift — before checking; the healed text is what
/// gets written.
/// </summary>
public sealed class FileWriteTool : McpToolBase
{
    private readonly ProjectSessionManager _sessions;

    internal FileWriteTool(ProjectSessionManager sessions) => _sessions = sessions;

    public override string Name => "calor_file_write";

    public override string Description =>
        "Write a .calr file transactionally: the content is auto-healed (indentation, " +
        "forbidden closers), checked (compile, contracts, effects, references), then " +
        "applied atomically — or rejected with diagnostics and the file left untouched " +
        "when the edit is breaking. Creates the file if it does not exist. Pass a " +
        "sessionId from calor_session_open to check references across the whole project.";

    public override McpToolAnnotations? Annotations => new() { DestructiveHint = true, IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Path of the .calr file to write (created if missing)"
                },
                "content": {
                    "type": "string",
                    "description": "The complete new file content"
                },
                "sessionId": {
                    "type": "string",
                    "description": "Optional session from calor_session_open; widens reference checks to the session's file set"
                },
                "checks": {
                    "type": "array",
                    "items": { "type": "string", "enum": ["compile", "contracts", "effects", "references"] },
                    "description": "Which checks to run (default: all)"
                },
                "heal": {
                    "type": "boolean",
                    "description": "Auto-heal indentation/closer slips before checking (default: true)"
                }
            },
            "required": ["path", "content"],
            "additionalProperties": false
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var path = GetString(arguments, "path");
        var content = GetString(arguments, "content");

        if (string.IsNullOrEmpty(path))
            return McpToolResult.Error("'path' is required");
        if (string.IsNullOrEmpty(content))
            return McpToolResult.Error("'content' is required and must not be empty");

        var sizeError = ValidateSourceSize(content, "content");
        if (sizeError != null)
            return sizeError;

        var pathError = ValidatePath(path);
        if (pathError != null)
            return pathError;

        if (!path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            return McpToolResult.Error("calor_file_write only writes .calr files");

        var absolutePath = Path.GetFullPath(path);

        // Resolve the session first so its state is fresh before we read the
        // original content (dirty-state invalidation, D2.1).
        ProjectSession? session = null;
        var sessionId = GetString(arguments, "sessionId");
        if (!string.IsNullOrEmpty(sessionId))
        {
            session = _sessions.Get(sessionId);
            if (session == null)
                return McpToolResult.Error($"Unknown sessionId '{sessionId}' — open one with calor_session_open");
            if (!session.ContainsPath(absolutePath))
                return McpToolResult.Error($"Path is outside the session root '{session.RootDirectory}'");
            session.Refresh();
        }

        var (runCompile, runContracts, runEffects, runReferences) = ParseCheckSelection(arguments);

        var originalSource = File.Exists(absolutePath)
            ? await File.ReadAllTextAsync(absolutePath, cancellationToken)
            : "";

        // Auto-heal before checking (D2.5). The healed text is authoritative:
        // it is what gets checked and, on a green verdict, what gets written.
        var heal = GetBool(arguments, "heal", defaultValue: true);
        var healer = new SourceHealer();
        var finalContent = heal ? healer.Heal(content) : content;
        var healApplied = heal && !string.Equals(finalContent, content, StringComparison.Ordinal);

        var originalParse = originalSource.Length > 0
            ? CalorSourceHelper.Parse(originalSource, absolutePath)
            : null;
        var modifiedParse = CalorSourceHelper.Parse(finalContent, absolutePath);

        var compilationResult = new EditPreviewTool.CompilationCheckResult { Checked = runCompile };
        if (runCompile)
        {
            compilationResult.OriginalCompiles = originalParse?.IsSuccess ?? true;
            compilationResult.ModifiedCompiles = modifiedParse.IsSuccess;
            compilationResult.Errors = modifiedParse.ToEnvelopeDiagnostics();
        }

        var contractResult = new EditPreviewTool.ContractCheckResult { Checked = runContracts };
        if (runContracts && originalParse != null && modifiedParse.IsSuccess)
        {
            EditPreviewTool.CheckContracts(originalParse, modifiedParse, contractResult);
        }

        var effectResult = new EditPreviewTool.EffectCheckResult { Checked = runEffects };
        if (runEffects && modifiedParse.IsSuccess)
        {
            EditPreviewTool.CheckEffects(modifiedParse, effectResult);
        }

        var referenceResult = new EditPreviewTool.ReferenceCheckResult { Checked = runReferences };
        if (runReferences && modifiedParse.IsSuccess && originalParse is { IsSuccess: true })
        {
            EditPreviewTool.CheckReferences(originalParse, modifiedParse, referenceResult);
            CheckProjectReferences(session, absolutePath, originalParse, modifiedParse, referenceResult);
        }

        var verdict = EditPreviewTool.DetermineVerdict(compilationResult, contractResult, effectResult, referenceResult);
        var applied = verdict != "breaking";

        if (applied)
        {
            await WriteAtomicAsync(absolutePath, finalContent, cancellationToken);
            session?.UpdateFile(absolutePath, finalContent, modifiedParse);
        }

        var editSummary = originalParse != null
            ? EditPreviewTool.ComputeEditSummary(originalSource, finalContent, originalParse, modifiedParse)
            : null;

        return McpToolResult.Json(new FileWriteOutput
        {
            Success = true,
            Applied = applied,
            Verdict = verdict,
            Path = absolutePath,
            Created = applied && originalSource.Length == 0,
            HealApplied = healApplied,
            HealNotes = healer.Ambiguities
                .Select(a => $"line {a.Line}: {a.Message}")
                .ToList(),
            WrittenContent = applied && healApplied ? finalContent : null,
            EditSummary = editSummary,
            CompilationResult = compilationResult,
            ContractVerification = contractResult,
            EffectAnalysis = effectResult,
            ReferenceIntegrity = referenceResult,
            Recommendations = EditPreviewTool.GenerateRecommendations(compilationResult, contractResult, effectResult, referenceResult)
        });
    }

    /// <summary>
    /// Project-wide half of the reference check (D2.1 + D2.4): symbols this
    /// edit removes must not still be referenced by other files in the
    /// session. Uses the same textual-containment heuristic as the in-file
    /// check so both halves have identical strictness.
    /// </summary>
    private static void CheckProjectReferences(ProjectSession? session, string editedPath,
        ParseResult originalParse, ParseResult modifiedParse, EditPreviewTool.ReferenceCheckResult result)
    {
        if (session == null)
            return;

        var removedSymbols = EditPreviewTool.CollectSymbolIds(originalParse.Ast!)
            .Except(EditPreviewTool.CollectSymbolIds(modifiedParse.Ast!))
            .ToList();
        if (removedSymbols.Count == 0)
            return;

        foreach (var file in session.SnapshotFiles())
        {
            if (string.Equals(file.Path, editedPath, StringComparison.Ordinal))
                continue;

            foreach (var symbol in removedSymbols)
            {
                if (file.Source.Contains(symbol, StringComparison.Ordinal))
                {
                    var relative = Path.GetRelativePath(session.RootDirectory, file.Path);
                    result.DanglingReferences.Add(
                        $"Symbol '{symbol}' was removed but is still referenced in {relative}");
                }
            }
        }

        result.HasDanglingReferences = result.DanglingReferences.Count > 0;
    }

    private static (bool Compile, bool Contracts, bool Effects, bool References) ParseCheckSelection(JsonElement? arguments)
    {
        var requested = GetStringArray(arguments, "checks");
        if (requested.Count == 0)
            return (true, true, true, true);

        return (requested.Contains("compile"),
                requested.Contains("contracts"),
                requested.Contains("effects"),
                requested.Contains("references"));
    }

    /// <summary>
    /// Atomic apply: write to a temp file in the target directory, then
    /// rename over the destination. The destination never holds partial
    /// content, even on a crash mid-write.
    /// </summary>
    private static async Task WriteAtomicAsync(string absolutePath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(absolutePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken);
            File.Move(tempPath, absolutePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private sealed class FileWriteOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("applied")] public bool Applied { get; init; }
        [JsonPropertyName("verdict")] public string? Verdict { get; init; }
        [JsonPropertyName("path")] public string? Path { get; init; }
        [JsonPropertyName("created")] public bool Created { get; init; }
        [JsonPropertyName("healApplied")] public bool HealApplied { get; init; }
        [JsonPropertyName("healNotes")] public List<string> HealNotes { get; init; } = new();
        /// <summary>The content actually written when healing changed it — the caller's view of the file is stale otherwise.</summary>
        [JsonPropertyName("writtenContent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? WrittenContent { get; init; }
        [JsonPropertyName("editSummary")] public EditPreviewTool.EditSummaryInfo? EditSummary { get; init; }
        [JsonPropertyName("compilationResult")] public EditPreviewTool.CompilationCheckResult? CompilationResult { get; init; }
        [JsonPropertyName("contractVerification")] public EditPreviewTool.ContractCheckResult? ContractVerification { get; init; }
        [JsonPropertyName("effectAnalysis")] public EditPreviewTool.EffectCheckResult? EffectAnalysis { get; init; }
        [JsonPropertyName("referenceIntegrity")] public EditPreviewTool.ReferenceCheckResult? ReferenceIntegrity { get; init; }
        [JsonPropertyName("recommendations")] public List<string> Recommendations { get; init; } = new();
    }
}
