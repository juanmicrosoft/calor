using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for formatting Calor source code and managing declaration IDs.
/// </summary>
public sealed class FormatTool : McpToolBase
{
    public override string Name => "calor_format";

    public override string Description =>
        "Format Calor source code or manage declaration IDs. Use action='format' to format code, action='ids' to check/assign IDs.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to format or check"
                },
                "action": {
                    "type": "string",
                    "enum": ["format", "ids"],
                    "description": "Action to perform. format=format code, ids=check/assign declaration IDs",
                    "default": "format"
                },
                "idsAction": {
                    "type": "string",
                    "enum": ["check", "assign"],
                    "default": "check",
                    "description": "When action=ids: 'check' validates IDs, 'assign' adds missing IDs"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "allowTestIds": {
                            "type": "boolean",
                            "default": false,
                            "description": "Allow test IDs (f001, m001) without flagging as issues"
                        },
                        "fixDuplicates": {
                            "type": "boolean",
                            "default": false,
                            "description": "When assigning, also fix duplicate IDs"
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

        var action = GetString(arguments, "action") ?? "format";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "ids" => HandleIds(source, arguments),
                _ => HandleFormat(source)
            };
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Operation failed: {ex.Message}"));
        }
    }

    private static Task<McpToolResult> HandleFormat(string source)
    {
        var result = FormatSource(source);

        var output = new FormatToolOutput
        {
            Success = result.Success,
            FormattedCode = result.Formatted,
            IsChanged = result.Original != result.Formatted,
            Errors = result.Errors.Count > 0 ? result.Errors : null
        };

        return Task.FromResult(McpToolResult.Json(output, isError: !result.Success));
    }

    private static Task<McpToolResult> HandleIds(string source, JsonElement? arguments)
    {
        var idsAction = GetString(arguments, "idsAction") ?? "check";
        var options = GetOptions(arguments);
        var allowTestIds = GetBool(options, "allowTestIds");
        var fixDuplicates = GetBool(options, "fixDuplicates");

        return idsAction.ToLowerInvariant() switch
        {
            "assign" => Task.FromResult(AssignIds(source, allowTestIds, fixDuplicates)),
            _ => Task.FromResult(CheckIds(source, allowTestIds))
        };
    }

    private static FormatResult FormatSource(string source)
    {
        // Parse the source; errors are surfaced as envelope schema v1.1
        // entries built from the real lexer/parser diagnostics.
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("mcp-input.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = BuildErrorEnvelope(diagnostics)
            };
        }

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = BuildErrorEnvelope(diagnostics)
            };
        }

        // Format the AST
        var formatter = new CalorFormatter();
        var formatted = formatter.Format(ast);

        return new FormatResult
        {
            Success = true,
            Original = source,
            Formatted = formatted,
            Errors = new List<EnvelopeDiagnostic>()
        };
    }

    private static List<EnvelopeDiagnostic> BuildErrorEnvelope(DiagnosticBag diagnostics)
    {
        return DiagnosticEnvelope.Build(diagnostics)
            .Where(e => e.Severity == "error")
            .ToList();
    }

    private static McpToolResult CheckIds(string source, bool allowTestIds)
    {
        var scan = ScanSource(source);
        if (scan == null)
        {
            return McpToolResult.Error("Failed to parse source code");
        }

        var (entries, module) = scan.Value;
        var result = IdChecker.Check(entries, allowTestIds);

        // Real Calor0800-band diagnostics as envelope schema v1.1 entries; a
        // resolver built from the parsed module populates declarationId
        // (mirrors the `calor ids check --format json` CLI adoption).
        var declarationIds = new DeclarationIdResolver();
        declarationIds.AddFile("mcp-input.calr", source, module);

        var issues = IdChecker.GenerateDiagnostics(result)
            .OrderBy(d => d.Span.Line)
            .Select(d => DiagnosticEnvelope.Build(d, declarationIds))
            .ToList();

        var output = new IdsCheckOutput
        {
            Success = result.IsValid,
            TotalIds = entries.Count,
            IssueCount = result.TotalIssues,
            Issues = issues
        };

        return McpToolResult.Json(output, isError: !result.IsValid);
    }

    private static McpToolResult AssignIds(string source, bool allowTestIds, bool fixDuplicates)
    {
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!fixDuplicates)
        {
            var scan = ScanSource(source);
            if (scan != null)
            {
                foreach (var entry in scan.Value.Entries.Where(e => !string.IsNullOrEmpty(e.Id)))
                {
                    if (allowTestIds && entry.IsTestId)
                        continue;
                    existingIds.Add(entry.Id);
                }
            }
        }

        var (newContent, assignments) = IdAssigner.AssignIds(source, "mcp-input.calr", fixDuplicates, existingIds);

        // Update closing tags
        if (assignments.Count > 0)
        {
            newContent = IdAssigner.UpdateClosingTags(newContent, assignments);
        }

        var output = new IdsAssignOutput
        {
            Success = true,
            AssignedCount = assignments.Count(a => string.IsNullOrEmpty(a.OldId)),
            DuplicatesFixedCount = assignments.Count(a => !string.IsNullOrEmpty(a.OldId)),
            ModifiedCode = newContent,
            Assignments = assignments.Select(a => new IdAssignmentOutput
            {
                Line = a.Line,
                Kind = a.Kind.ToString(),
                Name = a.Name,
                OldId = string.IsNullOrEmpty(a.OldId) ? null : a.OldId,
                NewId = a.NewId
            }).ToList()
        };

        return McpToolResult.Json(output);
    }

    private static (IReadOnlyList<IdEntry> Entries, ModuleNode Module)? ScanSource(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("mcp-input.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        if (diagnostics.HasErrors)
            return null;

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        if (diagnostics.HasErrors)
            return null;

        var scanner = new IdScanner();
        return (scanner.Scan(module, "mcp-input.calr"), module);
    }

    // Format output types
    private sealed class FormatResult
    {
        public bool Success { get; init; }
        public required string Original { get; init; }
        public required string Formatted { get; init; }
        public required List<EnvelopeDiagnostic> Errors { get; init; }
    }

    private sealed class FormatToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("formattedCode")]
        public required string FormattedCode { get; init; }

        [JsonPropertyName("isChanged")]
        public bool IsChanged { get; init; }

        /// <summary>Envelope schema v1.1 diagnostic entries (real parser diagnostics).</summary>
        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EnvelopeDiagnostic>? Errors { get; init; }
    }

    // IDs check output types
    private sealed class IdsCheckOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("totalIds")]
        public int TotalIds { get; init; }

        [JsonPropertyName("issueCount")]
        public int IssueCount { get; init; }

        /// <summary>Envelope schema v1.1 diagnostic entries (Calor0800-band, with declarationId).</summary>
        [JsonPropertyName("issues")]
        public required List<EnvelopeDiagnostic> Issues { get; init; }
    }

    // IDs assign output types
    private sealed class IdsAssignOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("assignedCount")]
        public int AssignedCount { get; init; }

        [JsonPropertyName("duplicatesFixedCount")]
        public int DuplicatesFixedCount { get; init; }

        [JsonPropertyName("modifiedCode")]
        public required string ModifiedCode { get; init; }

        [JsonPropertyName("assignments")]
        public required List<IdAssignmentOutput> Assignments { get; init; }
    }

    private sealed class IdAssignmentOutput
    {
        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("oldId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OldId { get; init; }

        [JsonPropertyName("newId")]
        public required string NewId { get; init; }
    }
}
