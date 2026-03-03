using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Init;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for getting machine-readable diagnostics from Calor source code.
/// </summary>
public sealed class DiagnoseTool : McpToolBase
{
    public override string Name => "calor_diagnose";

    public override string Description =>
        "Get machine-readable diagnostics from Calor source code. Returns errors and warnings with precise locations.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to diagnose"
                },
                "apply": {
                    "type": "boolean",
                    "description": "When true, automatically apply all available fix edits and return the fixed source alongside diagnostics (default: false)"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "strictApi": {
                            "type": "boolean",
                            "default": false,
                            "description": "Enable strict API checking"
                        },
                        "requireDocs": {
                            "type": "boolean",
                            "default": false,
                            "description": "Require documentation on public functions"
                        }
                    }
                }
            },
            "required": ["source"]
        ,

        "additionalProperties": false

        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));
        }

        var options = GetOptions(arguments);
        var strictApi = GetBool(options, "strictApi");
        var requireDocs = GetBool(options, "requireDocs");
        var applyFixes = GetBool(arguments, "apply");

        try
        {
            var compileOptions = new CompilationOptions
            {
                StrictApi = strictApi,
                RequireDocs = requireDocs
            };

            var result = Program.Compile(source, "mcp-input.calr", compileOptions);

            // Build lookup from DiagnosticsWithFixes to populate fix info
            // Include message in key to differentiate between different constructs at same location
            var fixLookup = result.Diagnostics.DiagnosticsWithFixes
                .GroupBy(dwf => (dwf.Span.Line, dwf.Span.Column, dwf.Code, dwf.Message))
                .ToDictionary(g => g.Key, g => g.First());

            var diagnostics = result.Diagnostics.Select(d =>
            {
                var diagOutput = new DiagnosticOutput
                {
                    Severity = d.IsError ? "error" : "warning",
                    Code = d.Code.ToString(),
                    Message = d.Message,
                    Line = d.Span.Line,
                    Column = d.Span.Column
                };

                // Check if this diagnostic has an associated fix
                var key = (d.Span.Line, d.Span.Column, d.Code, d.Message);
                if (fixLookup.TryGetValue(key, out var diagnosticWithFix))
                {
                    diagOutput.Suggestion = diagnosticWithFix.Fix.Description;
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

                // Enrich with common mistake suggestion if no compiler fix was found
                if (diagOutput.Suggestion == null)
                {
                    diagOutput.CommonMistake = FindCommonMistake(d.Message, d.Code.ToString());
                }

                return diagOutput;
            }).ToList();

            // Auto-apply fixes if requested
            string? fixedSource = null;
            var fixesApplied = 0;
            if (applyFixes)
            {
                fixedSource = ApplyFixes(source, result.Diagnostics.DiagnosticsWithFixes, out fixesApplied);
            }

            var output = new DiagnoseToolOutput
            {
                Success = !result.HasErrors,
                ErrorCount = diagnostics.Count(d => d.Severity == "error"),
                WarningCount = diagnostics.Count(d => d.Severity == "warning"),
                Diagnostics = diagnostics,
                FixedSource = fixedSource,
                FixesApplied = applyFixes ? fixesApplied : null
            };

            return Task.FromResult(McpToolResult.Json(output, isError: result.HasErrors));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Diagnose failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Applies fix edits to the source code in reverse position order to avoid offset invalidation.
    /// </summary>
    private static string ApplyFixes(string source, IReadOnlyList<DiagnosticWithFix> diagnosticsWithFixes, out int fixesApplied)
    {
        // Collect all edits and sort in reverse position order
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
            // Convert 1-based line/column to 0-based indices
            var startLine = edit.StartLine - 1;
            var startCol = edit.StartColumn - 1;
            var endLine = edit.EndLine - 1;
            var endCol = edit.EndColumn - 1;

            if (startLine < 0 || startLine >= lines.Length) continue;
            if (endLine < 0 || endLine >= lines.Length) continue;

            // Build the text before and after the edit range
            var beforeEdit = startCol >= 0 && startCol <= lines[startLine].Length
                ? lines[startLine][..startCol]
                : lines[startLine];
            var afterEdit = endCol >= 0 && endCol <= lines[endLine].Length
                ? lines[endLine][endCol..]
                : "";

            // Replace the affected lines
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

    private static readonly Lazy<JsonDocument?> ErrorSuggestions = new(LoadErrorSuggestions);

    private static JsonDocument? LoadErrorSuggestions()
    {
        try
        {
            var json = EmbeddedResourceHelper.ReadResource(
                "Calor.Compiler.Resources.error-suggestions.json");
            return JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    private static CommonMistakeOutput? FindCommonMistake(string message, string code)
    {
        var doc = ErrorSuggestions.Value;
        if (doc == null) return null;

        var normalizedMessage = message.ToLowerInvariant();

        foreach (var pattern in doc.RootElement.GetProperty("patterns").EnumerateArray())
        {
            // Check match codes
            foreach (var mc in pattern.GetProperty("matchCodes").EnumerateArray())
            {
                var codeStr = mc.GetString();
                if (codeStr != null && code.Contains(codeStr, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildCommonMistake(pattern);
                }
            }

            // Check match patterns against the error message
            foreach (var mp in pattern.GetProperty("matchPatterns").EnumerateArray())
            {
                var patternStr = mp.GetString();
                if (patternStr != null && normalizedMessage.Contains(patternStr.ToLowerInvariant()))
                {
                    return BuildCommonMistake(pattern);
                }
            }
        }

        return null;
    }

    private static CommonMistakeOutput BuildCommonMistake(JsonElement pattern)
    {
        return new CommonMistakeOutput
        {
            Id = pattern.GetProperty("id").GetString() ?? "",
            Title = pattern.GetProperty("title").GetString() ?? "",
            Suggestion = pattern.GetProperty("description").GetString() ?? "",
            CorrectExample = pattern.GetProperty("correctExample").GetString() ?? ""
        };
    }

    private sealed class DiagnoseToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; init; }

        [JsonPropertyName("warningCount")]
        public int WarningCount { get; init; }

        [JsonPropertyName("diagnostics")]
        public required List<DiagnosticOutput> Diagnostics { get; init; }

        [JsonPropertyName("fixedSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FixedSource { get; init; }

        [JsonPropertyName("fixesApplied")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? FixesApplied { get; init; }
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

        [JsonPropertyName("suggestion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Suggestion { get; set; }

        [JsonPropertyName("fix")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FixOutput? Fix { get; set; }

        [JsonPropertyName("commonMistake")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CommonMistakeOutput? CommonMistake { get; set; }
    }

    private sealed class CommonMistakeOutput
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("suggestion")]
        public required string Suggestion { get; init; }

        [JsonPropertyName("correctExample")]
        public required string CorrectExample { get; init; }
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
}
