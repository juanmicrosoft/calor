using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Mcp.Sessions;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool that opens a session-scoped project context over a directory
/// (loop plan WS2 D2.1). The session holds parse state for every .calr file
/// under the root and keeps it fresh via stat-on-access invalidation; pass
/// the returned sessionId to calor_file_write to widen its reference checks
/// to the whole project.
/// </summary>
public sealed class SessionOpenTool : McpToolBase
{
    private readonly ProjectSessionManager _sessions;

    internal SessionOpenTool(ProjectSessionManager sessions) => _sessions = sessions;

    public override string Name => "calor_session_open";

    public override string Description =>
        "Open a project session over a directory. Parses every .calr file under it and " +
        "returns a sessionId plus per-file parse status. Pass the sessionId to " +
        "calor_file_write so its checks see the whole project; the session re-checks " +
        "files changed on disk automatically.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "directory": {
                    "type": "string",
                    "description": "Root directory of the project (all .calr files under it, recursively)"
                }
            },
            "required": ["directory"],
            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var directory = GetString(arguments, "directory");
        if (string.IsNullOrEmpty(directory))
            return Task.FromResult(McpToolResult.Error("'directory' is required"));

        var pathError = ValidatePath(directory, "directory");
        if (pathError != null)
            return Task.FromResult(pathError);

        if (!Directory.Exists(directory))
            return Task.FromResult(McpToolResult.Error($"Directory not found: {directory}"));

        ProjectSession session;
        try
        {
            session = _sessions.Open(directory);
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(McpToolResult.Error(ex.Message));
        }

        var files = session.SnapshotFiles().OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        var diagnostics = files.SelectMany(f => f.Parse.ToEnvelopeDiagnostics()).ToList();

        return Task.FromResult(McpToolResult.Json(new SessionOpenOutput
        {
            Success = true,
            SessionId = session.Id,
            RootDirectory = session.RootDirectory,
            FileCount = files.Count,
            ParseErrorFileCount = files.Count(f => !f.Parse.IsSuccess),
            Files = files.Select(f => new SessionFileInfo
            {
                Path = Path.GetRelativePath(session.RootDirectory, f.Path),
                Parses = f.Parse.IsSuccess
            }).ToList(),
            Diagnostics = diagnostics
        }));
    }

    private sealed class SessionOpenOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("sessionId")] public string? SessionId { get; init; }
        [JsonPropertyName("rootDirectory")] public string? RootDirectory { get; init; }
        [JsonPropertyName("fileCount")] public int FileCount { get; init; }
        [JsonPropertyName("parseErrorFileCount")] public int ParseErrorFileCount { get; init; }
        [JsonPropertyName("files")] public List<SessionFileInfo> Files { get; init; } = new();
        /// <summary>Envelope schema v1.1 entries for files that fail to parse.</summary>
        [JsonPropertyName("diagnostics")] public List<EnvelopeDiagnostic> Diagnostics { get; init; } = new();
    }

    private sealed class SessionFileInfo
    {
        [JsonPropertyName("path")] public string? Path { get; init; }
        [JsonPropertyName("parses")] public bool Parses { get; init; }
    }
}

/// <summary>
/// MCP tool that closes a project session opened by calor_session_open,
/// releasing its in-memory parse state.
/// </summary>
public sealed class SessionCloseTool : McpToolBase
{
    private readonly ProjectSessionManager _sessions;

    internal SessionCloseTool(ProjectSessionManager sessions) => _sessions = sessions;

    public override string Name => "calor_session_close";

    public override string Description =>
        "Close a project session opened by calor_session_open and release its in-memory state.";

    public override McpToolAnnotations? Annotations => new() { IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "sessionId": {
                    "type": "string",
                    "description": "The session to close"
                }
            },
            "required": ["sessionId"],
            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var sessionId = GetString(arguments, "sessionId");
        if (string.IsNullOrEmpty(sessionId))
            return Task.FromResult(McpToolResult.Error("'sessionId' is required"));

        var closed = _sessions.Close(sessionId);
        return Task.FromResult(McpToolResult.Json(new SessionCloseOutput
        {
            Success = true,
            SessionId = sessionId,
            Closed = closed
        }));
    }

    private sealed class SessionCloseOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("sessionId")] public string? SessionId { get; init; }
        [JsonPropertyName("closed")] public bool Closed { get; init; }
    }
}
