using System.Text.Json;
using System.Text.Json.Serialization;
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
        // Parse the source
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("mcp-input.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = diagnostics.Errors.Select(e => e.Message).ToList()
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
                Errors = diagnostics.Errors.Select(e => e.Message).ToList()
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
            Errors = new List<string>()
        };
    }

    private static McpToolResult CheckIds(string source, bool allowTestIds)
    {
        var entries = ScanSource(source);
        if (entries == null)
        {
            return McpToolResult.Error("Failed to parse source code");
        }

        var result = IdChecker.Check(entries, allowTestIds);

        var issues = new List<IdIssueOutput>();

        foreach (var entry in result.MissingIds)
        {
            issues.Add(new IdIssueOutput
            {
                Type = "missing",
                Line = entry.Span.Line,
                Kind = entry.Kind.ToString(),
                Name = entry.Name,
                Message = $"Missing ID for {entry.Kind} '{entry.Name}'"
            });
        }

        foreach (var entry in result.InvalidFormatIds)
        {
            issues.Add(new IdIssueOutput
            {
                Type = "invalid_format",
                Line = entry.Span.Line,
                Kind = entry.Kind.ToString(),
                Name = entry.Name,
                Id = entry.Id,
                Message = $"Invalid ID format: '{entry.Id}'"
            });
        }

        foreach (var entry in result.WrongPrefixIds)
        {
            issues.Add(new IdIssueOutput
            {
                Type = "wrong_prefix",
                Line = entry.Span.Line,
                Kind = entry.Kind.ToString(),
                Name = entry.Name,
                Id = entry.Id,
                Message = $"Wrong prefix for {entry.Kind}: '{entry.Id}'"
            });
        }

        foreach (var entry in result.TestIdsInProduction)
        {
            issues.Add(new IdIssueOutput
            {
                Type = "test_id",
                Line = entry.Span.Line,
                Kind = entry.Kind.ToString(),
                Name = entry.Name,
                Id = entry.Id,
                Message = $"Test ID in production code: '{entry.Id}'"
            });
        }

        foreach (var group in result.DuplicateGroups)
        {
            foreach (var entry in group.Skip(1)) // First one is the original
            {
                issues.Add(new IdIssueOutput
                {
                    Type = "duplicate",
                    Line = entry.Span.Line,
                    Kind = entry.Kind.ToString(),
                    Name = entry.Name,
                    Id = entry.Id,
                    Message = $"Duplicate ID: '{entry.Id}' (first used at line {group.First().Span.Line})"
                });
            }
        }

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
            var entries = ScanSource(source);
            if (entries != null)
            {
                foreach (var entry in entries.Where(e => !string.IsNullOrEmpty(e.Id)))
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

    private static IReadOnlyList<IdEntry>? ScanSource(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("mcp-input.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (diagnostics.HasErrors)
            return null;

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        if (diagnostics.HasErrors)
            return null;

        var scanner = new IdScanner();
        return scanner.Scan(module, "mcp-input.calr");
    }

    // Format output types
    private sealed class FormatResult
    {
        public bool Success { get; init; }
        public required string Original { get; init; }
        public required string Formatted { get; init; }
        public required List<string> Errors { get; init; }
    }

    private sealed class FormatToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("formattedCode")]
        public required string FormattedCode { get; init; }

        [JsonPropertyName("isChanged")]
        public bool IsChanged { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Errors { get; init; }
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

        [JsonPropertyName("issues")]
        public required List<IdIssueOutput> Issues { get; init; }
    }

    private sealed class IdIssueOutput
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }
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
