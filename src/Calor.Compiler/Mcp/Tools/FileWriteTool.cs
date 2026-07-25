using System.Collections.Concurrent;
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
/// schema v1.1 diagnostics and the file untouched. Auto-heal (WS2 D2.5)
/// fixes common serialization slips before checking; healed content always
/// caps the verdict at safe_with_warnings because healing is not guaranteed
/// semantics-preserving.
///
/// Write confinement: every write must land, after symlink resolution, under
/// the session root (when a sessionId is given) or under the server's
/// working directory (when not). Writes are serialized per canonical path
/// and the on-disk content is revalidated against what was checked before
/// the rename; a concurrent external writer can still race the final rename,
/// which only OS-level file locking could close.
/// </summary>
public sealed class FileWriteTool : McpToolBase
{
    private readonly ProjectSessionManager _sessions;
    private readonly string _defaultWriteRoot;

    // One gate per canonical path, process-wide: check→apply must not
    // interleave for the same file across concurrent tool calls.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathGates = new(StringComparer.Ordinal);

    internal FileWriteTool(ProjectSessionManager sessions, string? defaultWriteRoot = null)
    {
        _sessions = sessions;
        _defaultWriteRoot = CanonicalPath.Resolve(defaultWriteRoot ?? Environment.CurrentDirectory);
    }

    public override string Name => "calor_file_write";

    public override string Description =>
        "Write a .calr file transactionally: the content is auto-healed (indentation, " +
        "forbidden closers), checked (compile, contracts, effects, references), then " +
        "applied atomically — or rejected with diagnostics and the file left untouched " +
        "when the edit is breaking. Creates the file if it does not exist. Writes are " +
        "confined to the session root (or the server's working directory without a " +
        "session). Pass a sessionId from calor_session_open to check references across " +
        "the whole project.";

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
        try
        {
            return await ExecuteCoreAsync(arguments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The tool contract is result-or-error, never a protocol crash:
            // IO races (file deleted mid-check), invalid path characters, and
            // permission failures all surface as tool errors.
            return McpToolResult.Error($"calor_file_write failed: {ex.Message}");
        }
    }

    private async Task<McpToolResult> ExecuteCoreAsync(JsonElement? arguments, CancellationToken cancellationToken)
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

        // Confinement runs on the canonical (symlink-resolved) path: a
        // lexical check would let a symlinked subdirectory point outside the
        // boundary. The suffix is checked on the canonical name too, so a
        // .calr symlink to a non-.calr target does not slip through.
        var canonicalPath = CanonicalPath.Resolve(path);
        if (!canonicalPath.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            return McpToolResult.Error("calor_file_write only writes .calr files");

        ProjectSession? session = null;
        var sessionId = GetString(arguments, "sessionId");
        if (!string.IsNullOrEmpty(sessionId))
        {
            session = _sessions.Get(sessionId);
            if (session == null)
                return McpToolResult.Error($"Unknown sessionId '{sessionId}' — open one with calor_session_open");
            if (!CanonicalPath.IsUnder(canonicalPath, session.RootDirectory))
                return McpToolResult.Error($"Path is outside the session root '{session.RootDirectory}'");
            session.Refresh();
        }
        else if (!CanonicalPath.IsUnder(canonicalPath, _defaultWriteRoot))
        {
            return McpToolResult.Error(
                $"Path is outside the server's write root '{_defaultWriteRoot}' — " +
                "open a session over the target directory with calor_session_open, or write within the working directory");
        }

        var gate = PathGates.GetOrAdd(canonicalPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await CheckAndApplyAsync(canonicalPath, content, session, arguments, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<McpToolResult> CheckAndApplyAsync(string canonicalPath, string content,
        ProjectSession? session, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (runCompile, runContracts, runEffects, runReferences) = ParseCheckSelection(arguments);

        var originalSource = File.Exists(canonicalPath)
            ? await File.ReadAllTextAsync(canonicalPath, cancellationToken)
            : "";

        // Auto-heal before checking (D2.5). The healed text is authoritative:
        // it is what gets checked and, on a green verdict, what gets written.
        var heal = GetBool(arguments, "heal", defaultValue: true);
        var healer = new SourceHealer();
        var finalContent = heal ? healer.Heal(content) : content;
        var healApplied = heal && !string.Equals(finalContent, content, StringComparison.Ordinal);

        var originalParse = originalSource.Length > 0
            ? CalorSourceHelper.Parse(originalSource, canonicalPath)
            : null;
        var modifiedParse = CalorSourceHelper.Parse(finalContent, canonicalPath);

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
            CheckProjectReferences(session, canonicalPath, originalParse, modifiedParse, referenceResult);
        }

        var verdict = EditPreviewTool.DetermineVerdict(compilationResult, contractResult, effectResult, referenceResult);

        // Healing is not guaranteed semantics-preserving (SourceHealer's
        // contract) — an auto-written healed file is never plain "safe".
        if (healApplied && verdict == "safe")
            verdict = "safe_with_warnings";

        var applied = verdict != "breaking";

        if (applied)
        {
            // Revalidate before the rename: the checks above ran against
            // originalSource, and an external writer may have changed the
            // file since it was read (the per-path gate only serializes
            // in-process callers).
            if (File.Exists(canonicalPath))
            {
                var current = await File.ReadAllTextAsync(canonicalPath, cancellationToken);
                if (!string.Equals(current, originalSource, StringComparison.Ordinal))
                    return McpToolResult.Error(
                        "File changed on disk while the edit was being checked — re-read it and retry");
            }

            await WriteAtomicAsync(canonicalPath, finalContent, cancellationToken);
            session?.UpdateFile(canonicalPath, finalContent, modifiedParse);
        }

        var editSummary = originalParse != null
            ? EditPreviewTool.ComputeEditSummary(originalSource, finalContent, originalParse, modifiedParse)
            : null;

        var recommendations = EditPreviewTool.GenerateRecommendations(compilationResult, contractResult, effectResult, referenceResult);
        if (healApplied)
            recommendations.Insert(0, "Content was auto-healed — review writtenContent; healing is not guaranteed semantics-preserving");

        return McpToolResult.Json(new FileWriteOutput
        {
            Success = true,
            Applied = applied,
            Verdict = verdict,
            Path = canonicalPath,
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
            Recommendations = recommendations
        });
    }

    /// <summary>
    /// Project-wide half of the reference check (D2.1 + D2.4): declarations
    /// this edit removes must not still be referenced by other files in the
    /// session. Functions are matched against actual call targets in each
    /// file's AST; types fall back to a whole-word text match — the same
    /// strictness as the in-file check.
    /// </summary>
    private static void CheckProjectReferences(ProjectSession? session, string editedPath,
        ParseResult originalParse, ParseResult modifiedParse, EditPreviewTool.ReferenceCheckResult result)
    {
        if (session == null)
            return;

        var removedFunctions = EditPreviewTool.CollectFunctionNames(originalParse.Ast!)
            .Except(EditPreviewTool.CollectFunctionNames(modifiedParse.Ast!))
            .ToList();
        var removedTypes = EditPreviewTool.CollectTypeNames(originalParse.Ast!)
            .Except(EditPreviewTool.CollectTypeNames(modifiedParse.Ast!))
            .ToList();
        if (removedFunctions.Count == 0 && removedTypes.Count == 0)
            return;

        foreach (var file in session.SnapshotFiles())
        {
            if (string.Equals(file.Path, editedPath, StringComparison.Ordinal))
                continue;

            var relative = Path.GetRelativePath(session.RootDirectory, file.Path);

            if (removedFunctions.Count > 0)
            {
                // Parsed files are checked against real call targets; files
                // that do not parse fall back to whole-word text so a broken
                // neighbor cannot hide a dangling call entirely.
                if (file.Parse.IsSuccess)
                {
                    var callTargets = EditPreviewTool.CollectCallTargets(file.Parse.Ast!);
                    foreach (var name in removedFunctions.Where(callTargets.Contains))
                    {
                        result.DanglingReferences.Add(
                            $"Function '{name}' was removed but is still called in {relative}");
                    }
                }
                else
                {
                    foreach (var name in removedFunctions.Where(n => EditPreviewTool.ContainsWholeWord(file.Source, n)))
                    {
                        result.DanglingReferences.Add(
                            $"Function '{name}' was removed but may still be referenced in {relative} (file does not parse)");
                    }
                }
            }

            foreach (var name in removedTypes.Where(n => EditPreviewTool.ContainsWholeWord(file.Source, n)))
            {
                result.DanglingReferences.Add(
                    $"Type '{name}' was removed but is still referenced in {relative}");
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
    /// content, and a pre-existing file keeps its permission bits.
    /// </summary>
    private static async Task WriteAtomicAsync(string absolutePath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(directory);

        UnixFileMode? existingMode = null;
        if (!OperatingSystem.IsWindows() && File.Exists(absolutePath))
            existingMode = File.GetUnixFileMode(absolutePath);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(absolutePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken);
            if (!OperatingSystem.IsWindows() && existingMode.HasValue)
                File.SetUnixFileMode(tempPath, existingMode.Value);
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
