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

    // Striped path gates, process-wide: check→apply must not interleave for
    // the same file across concurrent tool calls. A fixed stripe array stays
    // bounded no matter how many distinct paths are written (a per-path
    // dictionary would grow forever); two paths sharing a stripe merely
    // serialize, which is harmless.
    private static readonly SemaphoreSlim[] PathGates =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static SemaphoreSlim GateFor(string canonicalPath)
        => PathGates[(canonicalPath.GetHashCode(StringComparison.Ordinal) & int.MaxValue) % PathGates.Length];

    private readonly string? _writeLogPath;
    private readonly string? _rejectDir;
    private static readonly object WriteLogSync = new();

    // Reject payloads carry full file content (≤512 KB each); cap the archive
    // so a reject-spamming run cannot grow it without bound. Records keep
    // flowing to the write log past the cap — only the payload is dropped
    // (Annex A adjudicates M-L4 at ≥20 rejects, far below this).
    private const int MaxRejectArchives = 200;
    private static int _rejectsArchived;

    internal FileWriteTool(ProjectSessionManager sessions, string? defaultWriteRoot = null,
        string? writeLogPath = null, string? rejectDir = null)
    {
        _sessions = sessions;
        _defaultWriteRoot = CanonicalPath.Resolve(defaultWriteRoot ?? Environment.CurrentDirectory);

        // Loop telemetry (plan D4.1/D4.6): the harness sets these env vars in
        // the MCP server registration so every write attempt is journaled
        // (M-L2 first-apply validity) and every reject's payload is archived
        // for the reject-replay harness (M-L4). Absent → no logging.
        _writeLogPath = writeLogPath ?? Environment.GetEnvironmentVariable("CALOR_MCP_WRITE_LOG");
        _rejectDir = rejectDir ?? Environment.GetEnvironmentVariable("CALOR_MCP_REJECT_DIR");
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

        var gate = GateFor(canonicalPath);
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

    private async Task<McpToolResult> CheckAndApplyAsync(string canonicalPath, string content,
        ProjectSession? session, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (runCompile, runContracts, runEffects, runReferences) = ParseCheckSelection(arguments);

        // Zero-length entries are never opened: FIFOs stat as size 0 and a
        // blocking read on one would hang the call (and the stripe gate).
        var originalInfo = new FileInfo(canonicalPath);
        var fileExistedAtRead = originalInfo.Exists;
        var originalSource = fileExistedAtRead && originalInfo.Length > 0
            ? await File.ReadAllTextAsync(canonicalPath, cancellationToken)
            : "";

        // Auto-heal before checking (D2.5). The healed text is authoritative:
        // it is what gets checked and, on a green verdict, what gets written.
        var heal = GetBool(arguments, "heal", defaultValue: true);
        var healer = new SourceHealer();
        var finalContent = heal ? healer.Heal(content) : content;
        var healApplied = heal && !string.Equals(finalContent, content, StringComparison.Ordinal);

        // Tolerant parses (D2.5): a failed parse still carries a best-effort
        // AST, so reject envelopes attribute errors to their enclosing
        // declaration and the edit summary survives a broken side. Verdict
        // logic keys off IsSuccess and is unaffected — a partial AST never
        // upgrades a breaking edit.
        var originalParse = originalSource.Length > 0
            ? WarmOriginalParse(session, canonicalPath, originalSource)
            : null;
        var modifiedParse = CalorSourceHelper.ParseTolerant(finalContent, canonicalPath);

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
            // originalSource, and an external writer may have changed —
            // or created — the file since it was read (the path gate only
            // serializes in-process callers).
            var currentInfo = new FileInfo(canonicalPath);
            if (fileExistedAtRead)
            {
                var current = currentInfo is { Exists: true, Length: > 0 }
                    ? await File.ReadAllTextAsync(canonicalPath, cancellationToken)
                    : "";
                if (!string.Equals(current, originalSource, StringComparison.Ordinal))
                    return McpToolResult.Error(
                        "File changed on disk while the edit was being checked — re-read it and retry");
            }
            else if (currentInfo.Exists)
            {
                return McpToolResult.Error(
                    "File was created on disk while the edit was being checked — re-read it and retry");
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

        LogWriteAttempt(canonicalPath, verdict, applied, healApplied,
            created: applied && !fileExistedAtRead, finalContent);

        return McpToolResult.Json(new FileWriteOutput
        {
            Success = true,
            Applied = applied,
            Verdict = verdict,
            Path = canonicalPath,
            Created = applied && !fileExistedAtRead,
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
    /// Warm-path original parse (WS3 D3.1): when the session already holds
    /// this file with identical content — hash-verified against the bytes
    /// just read, so a stat-preserving edit cannot slip a stale AST in —
    /// the cached ParseResult is reused instead of re-parsing. Falls back
    /// to a cold tolerant parse. Verdict-neutral either way: the cached
    /// result came from the same ParseTolerant over the same bytes.
    /// </summary>
    internal static ParseResult WarmOriginalParse(ProjectSession? session, string canonicalPath, string originalSource)
    {
        var cached = session?.TryGetFile(canonicalPath);
        if (cached != null && cached.ContentHash == SessionFileState.HashContent(originalSource))
            return cached.Parse;

        return CalorSourceHelper.ParseTolerant(originalSource, canonicalPath);
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
            // Case-insensitive platforms: a case-variant of the edited path
            // addresses the same file — without the platform-aware compare,
            // the edited file's own stale session entry would be treated as
            // a neighbor and veto its own edit as "breaking".
            var selfComparison = OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            if (string.Equals(file.Path, editedPath, selfComparison))
                continue;

            var relative = Path.GetRelativePath(session.RootDirectory, file.Path);

            if (removedFunctions.Count > 0)
            {
                // Files are checked against real call targets whenever an AST
                // exists — tolerant parsing (D2.5) gives broken neighbors a
                // best-effort AST too, so a string-literal mention in a broken
                // file does not veto the write. Only a file with no AST at
                // all falls back to whole-word text. The index is the
                // session's warm per-file cache (WS3 D3.1), not a fresh walk.
                if (file.Parse.Ast != null)
                {
                    var callTargets = file.CallTargets;
                    var qualifier = file.Parse.IsSuccess ? "" : " (file has parse errors)";
                    foreach (var name in removedFunctions.Where(callTargets.Contains))
                    {
                        result.DanglingReferences.Add(
                            $"Function '{name}' was removed but is still called in {relative}{qualifier}");
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
    /// Appends one JSONL record per write attempt to the harness-configured
    /// log (M-L2's per-attempt stream), archiving rejected content for
    /// reject-replay (M-L4/D4.6). Telemetry must never fail the write path —
    /// all IO errors are swallowed. Revalidation races (retry errors) are
    /// deliberately not logged: they carry no verdict information.
    /// </summary>
    private void LogWriteAttempt(string path, string verdict, bool applied, bool healApplied,
        bool created, string content)
    {
        if (_writeLogPath == null)
            return;

        try
        {
            string? rejectPayloadPath = null;
            if (!applied && _rejectDir != null && Interlocked.Increment(ref _rejectsArchived) <= MaxRejectArchives)
            {
                Directory.CreateDirectory(_rejectDir);
                rejectPayloadPath = System.IO.Path.Combine(_rejectDir,
                    $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}Z-{Guid.NewGuid().ToString("N")[..8]}.json");
                File.WriteAllText(rejectPayloadPath, JsonSerializer.Serialize(new
                {
                    ts = DateTime.UtcNow.ToString("o"),
                    path,
                    verdict,
                    rejectedContent = content
                }, McpJsonOptions.Default));
            }

            var record = JsonSerializer.Serialize(new
            {
                schema = "mcp-write/1",
                ts = DateTime.UtcNow.ToString("o"),
                path,
                verdict,
                applied,
                healApplied,
                created,
                rejectPayload = rejectPayloadPath
            }, McpJsonOptions.Default);

            lock (WriteLogSync)
            {
                File.AppendAllText(_writeLogPath, record + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallowed by design; see summary.
        }
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
