using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Init;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for running code quality checks on Calor source code.
/// Consolidates diagnose, lint, typecheck, and validate operations.
/// </summary>
public sealed class CheckTool : McpToolBase
{
    public override string Name => "calor_check";

    public override string Description =>
        "Run code quality checks on Calor source code — diagnostics, lint, typecheck, or snippet validation";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["diagnose", "lint", "typecheck", "validate"],
                    "description": "Quality check to perform. diagnose=machine-readable diagnostics, lint=format compliance, typecheck=type checking, validate=validate a code snippet",
                    "default": "diagnose"
                },
                "source": {
                    "type": "string",
                    "description": "Calor source code to check (used as snippet for validate action)"
                },
                "apply": {
                    "type": "boolean",
                    "description": "When true, automatically apply all available fix edits and return the fixed source alongside diagnostics (diagnose action, default: false)"
                },
                "strictApi": {
                    "type": "boolean",
                    "default": false,
                    "description": "Enable strict API checking (diagnose action)"
                },
                "requireDocs": {
                    "type": "boolean",
                    "default": false,
                    "description": "Require documentation on public functions (diagnose action)"
                },
                "fix": {
                    "type": "boolean",
                    "default": false,
                    "description": "Return auto-fixed code in the response (lint action)"
                },
                "filePath": {
                    "type": "string",
                    "description": "Optional file path for diagnostic messages (typecheck action)"
                },
                "context": {
                    "type": "object",
                    "description": "Context for wrapping the snippet (validate action)",
                    "properties": {
                        "location": {
                            "type": "string",
                            "enum": ["expression", "statement", "function_body", "module_body"],
                            "default": "statement",
                            "description": "Where in code structure this snippet appears"
                        },
                        "returnType": {
                            "type": "string",
                            "description": "Expected return type for the containing function"
                        },
                        "parameters": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": { "type": "string" },
                                    "type": { "type": "string" }
                                },
                                "required": ["name", "type"]
                            },
                            "description": "Variables in scope"
                        },
                        "surroundingCode": {
                            "type": "string",
                            "description": "Code that precedes the snippet"
                        }
                    }
                },
                "lexerOnly": {
                    "type": "boolean",
                    "default": false,
                    "description": "Stop after lexer - token validation only (validate action)"
                },
                "showTokens": {
                    "type": "boolean",
                    "default": false,
                    "description": "Include token stream in output (validate action)"
                }
            },
            "required": ["source"]
        ,

        "additionalProperties": false

        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var action = GetString(arguments, "action") ?? "diagnose";

        return action switch
        {
            "diagnose" => HandleDiagnose(arguments, cancellationToken),
            "lint" => HandleLint(arguments),
            "typecheck" => HandleTypecheck(arguments, cancellationToken),
            "validate" => HandleValidate(arguments),
            _ => Task.FromResult(McpToolResult.Error($"Unknown action: {action}. Valid actions: diagnose, lint, typecheck, validate"))
        };
    }

    // ===== Diagnose =====

    private static Task<McpToolResult> HandleDiagnose(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));
        }

        var strictApi = GetBool(arguments, "strictApi");
        var requireDocs = GetBool(arguments, "requireDocs");
        var applyFixes = GetBool(arguments, "apply");

        try
        {
            var compileOptions = new CompilationOptions
            {
                StrictApi = strictApi,
                RequireDocs = requireDocs,
                CancellationToken = cancellationToken
            };

            var result = Program.Compile(source, "mcp-input.calr", compileOptions);

            var fixLookup = result.Diagnostics.DiagnosticsWithFixes
                .GroupBy(dwf => (dwf.Span.Line, dwf.Span.Column, dwf.Code, dwf.Message))
                .ToDictionary(g => g.Key, g => g.First());

            var diagnostics = result.Diagnostics.Select(d =>
            {
                var diagOutput = new DiagnoseDiagnosticOutput
                {
                    Severity = d.IsError ? "error" : "warning",
                    Code = d.Code.ToString(),
                    Message = d.Message,
                    Line = d.Span.Line,
                    Column = d.Span.Column
                };

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

                if (diagOutput.Suggestion == null)
                {
                    diagOutput.CommonMistake = FindCommonMistake(d.Message, d.Code.ToString());
                }

                return diagOutput;
            }).ToList();

            string? fixedSource = null;
            var fixesApplied = 0;
            if (applyFixes)
            {
                fixedSource = ApplyFixes(source, result.Diagnostics.DiagnosticsWithFixes, out fixesApplied);
            }

            var output = new DiagnoseOutput
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

    private static string ApplyFixes(string source, IReadOnlyList<DiagnosticWithFix> diagnosticsWithFixes, out int fixesApplied)
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
            if (endLine < 0 || endLine >= lines.Length) continue;

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
            foreach (var mc in pattern.GetProperty("matchCodes").EnumerateArray())
            {
                var codeStr = mc.GetString();
                if (codeStr != null && code.Contains(codeStr, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildCommonMistake(pattern);
                }
            }

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

    // ===== Lint =====

    private static Task<McpToolResult> HandleLint(JsonElement? arguments)
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

            var output = new LintOutput
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

        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            if (line.Length > 0 && char.IsWhiteSpace(line[0]) && line.TrimStart().Length > 0)
            {
                issues.Add(new LintIssue(lineNum, "Line has leading whitespace (indentation not allowed)"));
            }

            if (line.Length > 0 && line.TrimEnd('\r') != line.TrimEnd('\r').TrimEnd())
            {
                issues.Add(new LintIssue(lineNum, "Line has trailing whitespace"));
            }

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

            if (string.IsNullOrWhiteSpace(line.TrimEnd('\r')))
            {
                issues.Add(new LintIssue(lineNum, "Blank lines not allowed in agent-optimized format"));
            }
        }

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

    // ===== Typecheck =====

    private static Task<McpToolResult> HandleTypecheck(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));
        }

        var filePath = GetString(arguments, "filePath") ?? "mcp-typecheck.calr";

        try
        {
            var options = new CompilationOptions
            {
                EnableTypeChecking = true,
                CancellationToken = cancellationToken
            };

            var result = Program.Compile(source, filePath, options);

            var typeErrors = new List<TypeErrorOutput>();
            foreach (var diag in result.Diagnostics)
            {
                typeErrors.Add(new TypeErrorOutput
                {
                    Code = diag.Code,
                    Message = diag.Message,
                    Line = diag.Span.Line,
                    Column = diag.Span.Column,
                    Severity = diag.Severity switch
                    {
                        DiagnosticSeverity.Error => "error",
                        DiagnosticSeverity.Warning => "warning",
                        _ => "info"
                    },
                    Category = CategorizeError(diag.Code)
                });
            }

            var output = new TypeCheckOutput
            {
                Success = !result.HasErrors,
                ErrorCount = typeErrors.Count(e => e.Severity == "error"),
                WarningCount = typeErrors.Count(e => e.Severity == "warning"),
                TypeErrors = typeErrors
            };

            return Task.FromResult(McpToolResult.Json(output, isError: result.HasErrors));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Type checking failed: {ex.Message}"));
        }
    }

    private static string CategorizeError(string code) => code switch
    {
        DiagnosticCode.TypeMismatch => "type_mismatch",
        DiagnosticCode.UndefinedReference => "undefined_reference",
        DiagnosticCode.DuplicateDefinition => "duplicate_definition",
        DiagnosticCode.InvalidReference => "invalid_reference",
        _ => "other"
    };

    // ===== Validate =====

    private static Task<McpToolResult> HandleValidate(JsonElement? arguments)
    {
        var snippet = GetString(arguments, "source");
        if (snippet == null)
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));
        }

        if (string.IsNullOrWhiteSpace(snippet))
        {
            var emptyResult = new ValidateOutput
            {
                Success = true,
                Valid = true,
                Snippet = snippet,
                Diagnostics = [],
                ValidationLevel = "none",
                Warnings = ["Snippet is empty or contains only whitespace"]
            };
            return Task.FromResult(McpToolResult.Json(emptyResult));
        }

        var context = ParseContext(arguments);
        var lexerOnly = GetBool(arguments, "lexerOnly");
        var showTokens = GetBool(arguments, "showTokens");

        try
        {
            var wrapper = SnippetWrapper.Wrap(snippet, context);
            var diagnostics = new DiagnosticBag();

            var lexer = new Lexer(wrapper.WrappedSource, diagnostics);
            var tokens = lexer.TokenizeAll();

            List<TokenOutput>? tokenOutput = null;
            if (showTokens)
            {
                tokenOutput = tokens
                    .Where(t => t.Kind != TokenKind.Whitespace && t.Kind != TokenKind.Newline && t.Kind != TokenKind.Eof)
                    .Where(t => wrapper.IsInSnippet(t.Span.Line, t.Span.Column))
                    .Select(t => new TokenOutput
                    {
                        Kind = t.Kind.ToString(),
                        Text = t.Text,
                        Line = wrapper.AdjustLine(t.Span.Line),
                        Column = wrapper.AdjustColumn(t.Span.Line, t.Span.Column)
                    })
                    .ToList();
            }

            var filteredDiagnostics = FilterAndAdjustDiagnostics(diagnostics, wrapper);

            var warnings = wrapper.Warnings.Count > 0 ? wrapper.Warnings : null;

            if (lexerOnly || diagnostics.HasErrors)
            {
                var output = new ValidateOutput
                {
                    Success = true,
                    Valid = !filteredDiagnostics.Any(d => d.Severity == "error"),
                    Snippet = snippet,
                    Diagnostics = filteredDiagnostics,
                    ValidationLevel = "lexer",
                    Tokens = tokenOutput,
                    Warnings = warnings
                };

                return Task.FromResult(McpToolResult.Json(output));
            }

            var parser = new Parser(tokens, diagnostics);
            parser.Parse();

            filteredDiagnostics = FilterAndAdjustDiagnostics(diagnostics, wrapper);

            var result = new ValidateOutput
            {
                Success = true,
                Valid = !filteredDiagnostics.Any(d => d.Severity == "error"),
                Snippet = snippet,
                Diagnostics = filteredDiagnostics,
                ValidationLevel = "parser",
                Tokens = tokenOutput,
                Warnings = warnings
            };

            return Task.FromResult(McpToolResult.Json(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Validation failed: {ex.Message}"));
        }
    }

    private static SnippetContext ParseContext(JsonElement? arguments)
    {
        if (arguments == null || !arguments.Value.TryGetProperty("context", out var contextElement))
        {
            return SnippetContext.Default;
        }

        if (contextElement.ValueKind != JsonValueKind.Object)
        {
            return SnippetContext.Default;
        }

        var location = SnippetLocation.Statement;
        if (contextElement.TryGetProperty("location", out var locationProp) &&
            locationProp.ValueKind == JsonValueKind.String)
        {
            location = locationProp.GetString() switch
            {
                "expression" => SnippetLocation.Expression,
                "statement" => SnippetLocation.Statement,
                "function_body" => SnippetLocation.FunctionBody,
                "module_body" => SnippetLocation.ModuleBody,
                _ => SnippetLocation.Statement
            };
        }

        string? returnType = null;
        if (contextElement.TryGetProperty("returnType", out var returnTypeProp) &&
            returnTypeProp.ValueKind == JsonValueKind.String)
        {
            returnType = returnTypeProp.GetString();
        }

        var parameters = new List<SnippetParameter>();
        if (contextElement.TryGetProperty("parameters", out var paramsProp) &&
            paramsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var param in paramsProp.EnumerateArray())
            {
                if (param.TryGetProperty("name", out var nameProp) &&
                    param.TryGetProperty("type", out var typeProp) &&
                    nameProp.ValueKind == JsonValueKind.String &&
                    typeProp.ValueKind == JsonValueKind.String)
                {
                    parameters.Add(new SnippetParameter(nameProp.GetString()!, typeProp.GetString()!));
                }
            }
        }

        string? surroundingCode = null;
        if (contextElement.TryGetProperty("surroundingCode", out var surroundingProp) &&
            surroundingProp.ValueKind == JsonValueKind.String)
        {
            surroundingCode = surroundingProp.GetString();
        }

        return new SnippetContext(location, returnType, parameters, surroundingCode);
    }

    private static List<ValidateDiagnosticOutput> FilterAndAdjustDiagnostics(DiagnosticBag diagnostics, SnippetWrapper wrapper)
    {
        var result = new List<ValidateDiagnosticOutput>();

        foreach (var d in diagnostics.Errors.Concat(diagnostics.Warnings))
        {
            if (wrapper.IsInSnippet(d.Span.Line, d.Span.Column))
            {
                result.Add(new ValidateDiagnosticOutput
                {
                    Severity = d.IsError ? "error" : "warning",
                    Code = d.Code,
                    Message = d.Message,
                    Line = wrapper.AdjustLine(d.Span.Line),
                    Column = wrapper.AdjustColumn(d.Span.Line, d.Span.Column)
                });
            }
        }

        return result;
    }

    // ===== Output DTOs =====

    // --- Diagnose ---

    private sealed class DiagnoseOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; init; }

        [JsonPropertyName("warningCount")]
        public int WarningCount { get; init; }

        [JsonPropertyName("diagnostics")]
        public required List<DiagnoseDiagnosticOutput> Diagnostics { get; init; }

        [JsonPropertyName("fixedSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FixedSource { get; init; }

        [JsonPropertyName("fixesApplied")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? FixesApplied { get; init; }
    }

    private sealed class DiagnoseDiagnosticOutput
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

    // --- Lint ---

    private sealed class LintResult
    {
        public bool ParseSuccess { get; init; }
        public required List<string> ParseErrors { get; init; }
        public required List<LintIssue> Issues { get; init; }
        public required string OriginalContent { get; init; }
        public required string FixedContent { get; init; }
    }

    private sealed record LintIssue(int Line, string Message);

    private sealed class LintOutput
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

    // --- TypeCheck ---

    private sealed class TypeCheckOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; init; }

        [JsonPropertyName("warningCount")]
        public int WarningCount { get; init; }

        [JsonPropertyName("typeErrors")]
        public required List<TypeErrorOutput> TypeErrors { get; init; }
    }

    private sealed class TypeErrorOutput
    {
        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }

        [JsonPropertyName("severity")]
        public required string Severity { get; init; }

        [JsonPropertyName("category")]
        public required string Category { get; init; }
    }

    // --- Validate ---

    private sealed class ValidateOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("valid")]
        public bool Valid { get; init; }

        [JsonPropertyName("snippet")]
        public required string Snippet { get; init; }

        [JsonPropertyName("diagnostics")]
        public required List<ValidateDiagnosticOutput> Diagnostics { get; init; }

        [JsonPropertyName("validationLevel")]
        public required string ValidationLevel { get; init; }

        [JsonPropertyName("tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TokenOutput>? Tokens { get; init; }

        [JsonPropertyName("warnings")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<string>? Warnings { get; init; }
    }

    private sealed class ValidateDiagnosticOutput
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
    }

    private sealed class TokenOutput
    {
        [JsonPropertyName("kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("text")]
        public required string Text { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }
    }
}

/// <summary>
/// Location type for snippet context.
/// </summary>
internal enum SnippetLocation
{
    Expression,
    Statement,
    FunctionBody,
    ModuleBody
}

/// <summary>
/// Parameter definition for snippet context.
/// </summary>
internal readonly record struct SnippetParameter(string Name, string Type);

/// <summary>
/// Context information for wrapping a snippet.
/// </summary>
internal sealed class SnippetContext
{
    public static readonly SnippetContext Default = new(SnippetLocation.Statement, null, [], null);

    public SnippetLocation Location { get; }
    public string? ReturnType { get; }
    public IReadOnlyList<SnippetParameter> Parameters { get; }
    public string? SurroundingCode { get; }

    public SnippetContext(
        SnippetLocation location,
        string? returnType,
        IReadOnlyList<SnippetParameter> parameters,
        string? surroundingCode)
    {
        Location = location;
        ReturnType = returnType;
        Parameters = parameters;
        SurroundingCode = surroundingCode;
    }
}

/// <summary>
/// Helper class that wraps a snippet in synthetic module/function structure
/// and tracks line/column offsets for diagnostic adjustment.
/// </summary>
internal sealed class SnippetWrapper
{
    public string WrappedSource { get; }
    public int SnippetStartLine { get; }
    public int SnippetEndLine { get; }

    /// <summary>
    /// Column offset for the first line of the snippet (used for expression wrapping).
    /// For multi-line snippets, only the first line has this offset.
    /// </summary>
    public int FirstLineColumnOffset { get; }

    /// <summary>
    /// Warnings about context configuration issues.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    private SnippetWrapper(string wrappedSource, int snippetStartLine, int snippetEndLine, int firstLineColumnOffset = 0, IReadOnlyList<string>? warnings = null)
    {
        WrappedSource = wrappedSource;
        SnippetStartLine = snippetStartLine;
        SnippetEndLine = snippetEndLine;
        FirstLineColumnOffset = firstLineColumnOffset;
        Warnings = warnings ?? [];
    }

    /// <summary>
    /// Wraps the snippet in appropriate synthetic code based on context.
    /// </summary>
    public static SnippetWrapper Wrap(string snippet, SnippetContext context)
    {
        var sb = new StringBuilder();
        var warnings = new List<string>();
        int currentLine = 1;
        int firstLineColumnOffset = 0;

        // Module header
        sb.AppendLine("§M{_:_snippet_}");
        currentLine++;

        if (context.Location == SnippetLocation.ModuleBody)
        {
            if (context.Parameters.Count > 0)
            {
                warnings.Add("Context 'parameters' ignored for module_body location");
            }
            if (context.ReturnType != null)
            {
                warnings.Add("Context 'returnType' ignored for module_body location");
            }
            if (context.SurroundingCode != null)
            {
                warnings.Add("Context 'surroundingCode' ignored for module_body location");
            }

            int snippetStart = currentLine;
            sb.Append(snippet);
            int snippetLines = CountLines(snippet);
            currentLine += snippetLines;
            int snippetEnd = currentLine - 1;

            if (!snippet.EndsWith('\n'))
            {
                sb.AppendLine();
                currentLine++;
            }

            sb.AppendLine("§/M{_}");

            return new SnippetWrapper(sb.ToString(), snippetStart, snippetEnd, 0, warnings);
        }

        // Function header
        sb.AppendLine("§F{_:_validate_:pri}");
        currentLine++;

        // Parameters
        foreach (var param in context.Parameters)
        {
            sb.AppendLine($"  §I{{{param.Type}:{param.Name}}}");
            currentLine++;
        }

        // Return type
        var returnType = context.ReturnType ?? "unit";
        sb.AppendLine($"  §O{{{returnType}}}");
        currentLine++;

        // Surrounding code if any
        if (!string.IsNullOrEmpty(context.SurroundingCode))
        {
            sb.Append("  ");
            sb.AppendLine(context.SurroundingCode);
            currentLine += CountLines(context.SurroundingCode);
        }

        int snippetStartLine = currentLine;

        switch (context.Location)
        {
            case SnippetLocation.Expression:
                const string expressionPrefix = "  §B{_result} ";
                sb.Append(expressionPrefix);
                sb.Append(snippet);
                firstLineColumnOffset = expressionPrefix.Length;
                break;

            case SnippetLocation.Statement:
            case SnippetLocation.FunctionBody:
            default:
                const string statementIndent = "  ";
                var lines = snippet.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    sb.Append(statementIndent);
                    sb.Append(lines[i]);
                    if (i < lines.Length - 1)
                    {
                        sb.AppendLine();
                    }
                }
                firstLineColumnOffset = statementIndent.Length;
                break;
        }

        int snippetLines2 = CountLines(snippet);
        int snippetEndLine = snippetStartLine + snippetLines2 - 1;
        currentLine += snippetLines2;

        if (!snippet.EndsWith('\n'))
        {
            sb.AppendLine();
            currentLine++;
        }

        // Close function and module
        sb.AppendLine("§/F{_}");
        sb.AppendLine("§/M{_}");

        return new SnippetWrapper(sb.ToString(), snippetStartLine, snippetEndLine, firstLineColumnOffset, warnings);
    }

    /// <summary>
    /// Checks if a line number (1-based) falls within the snippet bounds.
    /// </summary>
    public bool IsInSnippet(int line)
    {
        return line >= SnippetStartLine && line <= SnippetEndLine;
    }

    /// <summary>
    /// Checks if a position (line and column) falls within the snippet bounds.
    /// This handles expression wrapping where the first line has a prefix.
    /// </summary>
    public bool IsInSnippet(int line, int column)
    {
        if (line < SnippetStartLine || line > SnippetEndLine)
            return false;

        if (line == SnippetStartLine && FirstLineColumnOffset > 0)
        {
            return column > FirstLineColumnOffset;
        }

        return true;
    }

    /// <summary>
    /// Adjusts a line number from wrapped source to snippet-relative (1-based).
    /// </summary>
    public int AdjustLine(int line)
    {
        return line - SnippetStartLine + 1;
    }

    /// <summary>
    /// Adjusts a column number based on line position and location type.
    /// </summary>
    public int AdjustColumn(int line, int column)
    {
        if (FirstLineColumnOffset == 0)
        {
            return column;
        }

        if (line == SnippetStartLine)
        {
            return Math.Max(1, column - FirstLineColumnOffset);
        }

        if (FirstLineColumnOffset == 2)
        {
            return Math.Max(1, column - 2);
        }

        return column;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                count++;
        }
        return count;
    }
}
