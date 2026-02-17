using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for linting Calor source code for agent-optimal format compliance.
/// </summary>
public sealed class LintTool : McpToolBase
{
    public override string Name => "calor_lint";

    public override string Description =>
        "Check Calor source code for agent-optimal format compliance. Returns lint issues and optionally the fixed code.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to lint"
                },
                "fix": {
                    "type": "boolean",
                    "default": false,
                    "description": "Return auto-fixed code in the response"
                }
            },
            "required": ["source"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));
        }

        var fix = GetBool(arguments, "fix");

        try
        {
            var result = LintSource(source);

            var output = new LintToolOutput
            {
                Success = result.ParseSuccess && result.Issues.Count == 0,
                ParseSuccess = result.ParseSuccess,
                IssueCount = result.Issues.Count,
                Issues = result.Issues.Select(i => new LintIssueOutput
                {
                    Line = i.Line,
                    Message = i.Message
                }).ToList(),
                ParseErrors = result.ParseErrors,
                FixedCode = fix ? result.FixedContent : null
            };

            return Task.FromResult(McpToolResult.Json(output, isError: !result.ParseSuccess));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Lint failed: {ex.Message}"));
        }
    }

    private static LintResult LintSource(string source)
    {
        var issues = new List<LintIssue>();

        // Check source-level issues before parsing
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // Check for leading whitespace (indentation)
            if (line.Length > 0 && char.IsWhiteSpace(line[0]) && line.TrimStart().Length > 0)
            {
                issues.Add(new LintIssue(lineNum, "Line has leading whitespace (indentation not allowed)"));
            }

            // Check for trailing whitespace
            if (line.Length > 0 && line.TrimEnd('\r') != line.TrimEnd('\r').TrimEnd())
            {
                issues.Add(new LintIssue(lineNum, "Line has trailing whitespace"));
            }

            // Check for non-abbreviated IDs: m001, f001, etc.
            var paddedIdMatch = Regex.Match(line, @"§[A-Z/]+\{([a-zA-Z]+)(0+)(\d+)");
            if (paddedIdMatch.Success)
            {
                var prefix = paddedIdMatch.Groups[1].Value;
                var zeros = paddedIdMatch.Groups[2].Value;
                var number = paddedIdMatch.Groups[3].Value;
                var oldId = prefix + zeros + number;
                var newId = prefix + number;
                issues.Add(new LintIssue(lineNum, $"ID should be abbreviated: use '{newId}' instead of '{oldId}'"));
            }

            // Check for verbose loop/condition IDs: for1, if1, while1, do1
            var verboseIdPatterns = new[]
            {
                (@"§L\{(for)(\d+)", "l"),
                (@"§/L\{(for)(\d+)", "l"),
                (@"§IF\{(if)(\d+)", "i"),
                (@"§/I\{(if)(\d+)", "i"),
                (@"§WHILE\{(while)(\d+)", "w"),
                (@"§/WHILE\{(while)(\d+)", "w"),
                (@"§DO\{(do)(\d+)", "d"),
                (@"§/DO\{(do)(\d+)", "d")
            };

            foreach (var (pattern, replacement) in verboseIdPatterns)
            {
                var match = Regex.Match(line, pattern);
                if (match.Success)
                {
                    var oldId = match.Groups[1].Value + match.Groups[2].Value;
                    var newId = replacement + match.Groups[2].Value;
                    issues.Add(new LintIssue(lineNum, $"ID should be abbreviated: use '{newId}' instead of '{oldId}'"));
                }
            }

            // Check for blank lines (empty or whitespace-only)
            if (string.IsNullOrWhiteSpace(line.TrimEnd('\r')))
            {
                issues.Add(new LintIssue(lineNum, "Blank lines not allowed in agent-optimized format"));
            }
        }

        // Parse the file to generate fixed content
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("mcp-input.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (diagnostics.HasErrors)
        {
            return new LintResult
            {
                ParseSuccess = false,
                ParseErrors = diagnostics.Errors.Select(e => e.Message).ToList(),
                Issues = issues,
                OriginalContent = source,
                FixedContent = source
            };
        }

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (diagnostics.HasErrors)
        {
            return new LintResult
            {
                ParseSuccess = false,
                ParseErrors = diagnostics.Errors.Select(e => e.Message).ToList(),
                Issues = issues,
                OriginalContent = source,
                FixedContent = source
            };
        }

        // Format the AST to get the canonical (fixed) version
        var formatter = new CalorFormatter();
        var fixedContent = formatter.Format(ast);

        return new LintResult
        {
            ParseSuccess = true,
            ParseErrors = new List<string>(),
            Issues = issues,
            OriginalContent = source,
            FixedContent = fixedContent
        };
    }

    private sealed class LintResult
    {
        public bool ParseSuccess { get; init; }
        public required List<string> ParseErrors { get; init; }
        public required List<LintIssue> Issues { get; init; }
        public required string OriginalContent { get; init; }
        public required string FixedContent { get; init; }
    }

    private sealed record LintIssue(int Line, string Message);

    private sealed class LintToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("parseSuccess")]
        public bool ParseSuccess { get; init; }

        [JsonPropertyName("issueCount")]
        public int IssueCount { get; init; }

        [JsonPropertyName("issues")]
        public required List<LintIssueOutput> Issues { get; init; }

        [JsonPropertyName("parseErrors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? ParseErrors { get; init; }

        [JsonPropertyName("fixedCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FixedCode { get; init; }
    }

    private sealed class LintIssueOutput
    {
        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }
    }
}
