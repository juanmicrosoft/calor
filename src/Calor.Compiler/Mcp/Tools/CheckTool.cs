using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Ids;
using Calor.Compiler.Init;
using Calor.Compiler.Parsing;
using Calor.Compiler.Commands;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for running code quality checks on Calor source code.
/// Consolidates diagnose, lint, typecheck, and validate operations.
/// </summary>
public sealed class CheckTool : McpToolBase
{
    public override string Name => "calor_check";

    public override string Description =>
        "Run Calor quality checks and snippet validation: diagnose, lint, typecheck, validate, or loop (build, execute tests and verify a path snapshot; failures only). Loop runs arbitrary test code.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = false, IdempotentHint = false };

    public override int TimeoutSeconds => 1800;

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "implicitSystem": { "type": "boolean", "default": true, "description": "System-only compilation context; false selects standalone validation." },
                "action": {
                    "type": "string",
                    "enum": ["diagnose", "lint", "typecheck", "validate", "loop"],
                    "description": "diagnose=diagnostics, lint=format compliance, typecheck=types, validate=snippet, loop=build+tests+contracts for path",
                    "default": "diagnose"
                },
                "source": {
                    "type": "string",
                    "description": "Calor source code to check (used as snippet for validate action)"
                },
                "path": {
                    "type": "string",
                    "description": "File or directory to snapshot, build, test (tests/*.cs xUnit) and verify (loop only). Executes arbitrary tests; does not edit sources."
                },
                "timeoutSeconds": {
                    "type": "integer", "minimum": 1, "maximum": 600, "default": 600,
                    "description": "Per build/test process timeout (loop only)."
                },
                "verificationTimeoutMs": {
                    "type": "integer", "minimum": 1, "default": 5000,
                    "description": "Solver timeout per contract (loop only)."
                },
                "apply": {
                    "type": "boolean",
                    "description": "Apply deterministic safe patches and return fixed source with freshly computed diagnostics. Heuristic suggestions are not applied (diagnose action, default: false)."
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
            "anyOf": [
                { "required": ["source"] },
                { "required": ["action", "path"], "properties": { "action": { "const": "loop" } } }
            ]
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
            "loop" => HandleLoopAsync(arguments, cancellationToken),
            _ => Task.FromResult(McpToolResult.Error($"Unknown action: {action}. Valid actions: diagnose, lint, typecheck, validate, loop"))
        };
    }

    private static async Task<McpToolResult> HandleLoopAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var path = GetString(arguments, "path");
        if (string.IsNullOrWhiteSpace(path))
            return McpToolResult.Error("Missing required parameter: path");
        if (GetBool(arguments, "apply"))
            return McpToolResult.Error("loop does not apply fixes; use diagnose with apply explicitly.");
        var result = await TestCommand.RunLoopAsync(path, GetInt(arguments, "timeoutSeconds", 600),
            GetInt(arguments, "verificationTimeoutMs", 5000), cancellationToken,
            GetBool(arguments, "implicitSystem", true));
        return McpToolResult.Json(result, isError: !result.Success);
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
                IncludeImplicitSystemUsing = GetBool(arguments, "implicitSystem", true),
                StrictApi = strictApi,
                RequireDocs = requireDocs,
                CancellationToken = cancellationToken
            };

            var filePath = GetString(arguments, "filePath") ?? "mcp-input.calr";
            var result = Program.Compile(source, filePath, compileOptions);
            var (diagnostics, hints) = BuildDiagnoseDiagnostics(result, source);

            string? fixedSource = null;
            var fixesApplied = 0;
            if (applyFixes)
            {
                fixedSource = DiagnosticFixes.Repair(source, filePath, compileOptions,
                    out result, out fixesApplied, initialResult: result);
                (diagnostics, hints) = BuildDiagnoseDiagnostics(result, fixedSource);
            }

            var output = new DiagnoseOutput
            {
                Success = !result.HasErrors,
                ErrorCount = diagnostics.Count(d => d.Severity == "error"),
                WarningCount = diagnostics.Count(d => d.Severity == "warning"),
                Diagnostics = diagnostics,
                Hints = hints.Count > 0 ? hints : null,
                FixedSource = fixedSource,
                FixesApplied = applyFixes ? fixesApplied : null,
                Healed = null
            };

            return Task.FromResult(McpToolResult.Json(output, isError: result.HasErrors));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Diagnose failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Projects a compilation's diagnostics into envelope schema v1.1 entries
    /// (shared EnvelopeDiagnostic shape, machine-applicable fixes joined by the
    /// shared builder) plus a sibling hint list carrying common-mistake guidance
    /// for diagnostics that have no compiler-provided suggestion.
    /// </summary>
    private static (List<EnvelopeDiagnostic> Diagnostics, List<HintOutput> Hints) BuildDiagnoseDiagnostics(
        CompilationResult result, string source)
    {
        DeclarationIdResolver? declarationIds = null;
        if (result.Ast != null)
        {
            declarationIds = new DeclarationIdResolver();
            declarationIds.AddFile("mcp-input.calr", source, result.Ast);
        }

        var entries = DiagnosticEnvelope.Build(result.Diagnostics, declarationIds);

        var hints = new List<HintOutput>();
        foreach (var entry in entries)
        {
            if (entry.Suggestion != null)
                continue;

            var commonMistake = FindCommonMistake(entry.Message, entry.Code);
            if (commonMistake != null)
            {
                hints.Add(new HintOutput
                {
                    Line = entry.Location.Line,
                    Column = entry.Location.Column,
                    Code = entry.Code,
                    CommonMistake = commonMistake
                });
            }
        }

        return (entries, hints);
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
            var result = LintSource(source, fix);
            var fixHasErrors = result.FixDiagnostics.Any(
                diagnostic => diagnostic.Severity == "error");

            var output = new LintOutput
            {
                Success = result.ParseSuccess
                    && result.Issues.Count == 0
                    && result.FixSuppressedReason == null
                    && !fixHasErrors,
                ParseSuccess = result.ParseSuccess,
                IssueCount = result.Issues.Count,
                Issues = result.Issues.Select(i => new LintIssueOutput
                {
                    Line = i.Line,
                    Message = i.Message,
                    Code = i.Code,
                    Fix = i.Fix
                }).ToList(),
                ParseErrors = result.ParseErrors,
                FixedCode = fix ? result.FixedContent : null,
                FixDiagnostics = result.FixDiagnostics.Count > 0
                    ? result.FixDiagnostics
                    : null,
                FixSuppressedReason = result.FixSuppressedReason
            };

            return Task.FromResult(McpToolResult.Json(
                output,
                isError: !result.ParseSuccess
                    || fixHasErrors
                    || result.FixSuppressedReason != null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Lint failed: {ex.Message}"));
        }
    }

    private static LintResult LintSource(string source, bool attemptFix)
    {
        var diagnostics = LosslessSourceDocument.DiagnoseTrailingWhitespace(source, "mcp-input.calr");
        var issues = DiagnosticEnvelope.Build(diagnostics).Select(diagnostic =>
            new LintIssue(diagnostic.Location.Line, diagnostic.Message, diagnostic.Code, diagnostic.Fix)).ToList();

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        if (diagnostics.HasErrors)
        {
            return new LintResult
            {
                ParseSuccess = false,
                ParseErrors = BuildParseErrorEnvelope(diagnostics),
                Issues = issues,
                OriginalContent = source,
                FixedContent = source,
                FixDiagnostics = new List<EnvelopeDiagnostic>()
            };
        }

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (diagnostics.HasErrors)
        {
            return new LintResult
            {
                ParseSuccess = false,
                ParseErrors = BuildParseErrorEnvelope(diagnostics),
                Issues = issues,
                OriginalContent = source,
                FixedContent = source,
                FixDiagnostics = new List<EnvelopeDiagnostic>()
            };
        }

        diagnostics.AddRange(LosslessSourceDocument.DiagnoseCanonicalUsage(source, "mcp-input.calr", ast));
        issues = DiagnosticEnvelope.Build(diagnostics).Select(diagnostic =>
            new LintIssue(diagnostic.Location.Line, diagnostic.Message, diagnostic.Code, diagnostic.Fix)).ToList();

        if (!attemptFix)
        {
            return new LintResult
            {
                ParseSuccess = true,
                ParseErrors = new List<EnvelopeDiagnostic>(),
                Issues = issues,
                OriginalContent = source,
                FixedContent = source,
                FixDiagnostics = new List<EnvelopeDiagnostic>()
            };
        }

        var formatter = new CalorFormatter();
        var formatResult = formatter.FormatSource(source, "mcp-input.calr");
        if (!formatResult.Success)
        {
            var fixDiagnostics = formatResult.Diagnostics
                .Where(diagnostic => diagnostic.IsError)
                .Select(diagnostic => DiagnosticEnvelope.Build(diagnostic))
                .ToList();
            if (fixDiagnostics.Count == 0)
            {
                fixDiagnostics.Add(DiagnosticEnvelope.Build(new Diagnostic(
                    DiagnosticCode.LintProcessingError,
                    formatResult.Errors.FirstOrDefault()
                        ?? "Safe formatting failed.",
                    TextSpan.Empty,
                    DiagnosticSeverity.Error,
                    "mcp-input.calr")));
            }
            return new LintResult
            {
                ParseSuccess = true,
                ParseErrors = new List<EnvelopeDiagnostic>(),
                Issues = issues,
                OriginalContent = source,
                FixedContent = source,
                FixDiagnostics = fixDiagnostics
            };
        }

        string? fixSuppressedReason = null;
        var fallbackDiagnostics = new List<EnvelopeDiagnostic>();
        if (formatResult.UsedConservativeFallback)
        {
            fixSuppressedReason = formatResult.ConservativeFallbackReason
                ?? "Source is unsupported by the safe formatting gates.";
            fallbackDiagnostics.Add(DiagnosticEnvelope.Build(new Diagnostic(
                DiagnosticCode.FormatConservativeFallback,
                fixSuppressedReason,
                TextSpan.Empty,
                DiagnosticSeverity.Warning,
                "mcp-input.calr")));
        }

        var fixedContent = formatResult.Formatted;
        if (fixSuppressedReason == null)
            fixedContent = DiagnosticFixes.Repair(fixedContent, "mcp-input.calr",
                new CompilationOptions(), out _, out _);

        return new LintResult
        {
            ParseSuccess = true,
            ParseErrors = new List<EnvelopeDiagnostic>(),
            Issues = issues,
            OriginalContent = source,
            FixedContent = fixedContent,
            FixDiagnostics = fallbackDiagnostics,
            FixSuppressedReason = fixSuppressedReason
        };
    }

    /// <summary>Envelope schema v1.1 entries for a bag's errors (no AST, so declarationId stays null).</summary>
    private static List<EnvelopeDiagnostic> BuildParseErrorEnvelope(DiagnosticBag diagnostics)
    {
        return DiagnosticEnvelope.Build(diagnostics)
            .Where(e => e.Severity == "error")
            .ToList();
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
                IncludeImplicitSystemUsing = GetBool(arguments, "implicitSystem", true),
                EnableTypeChecking = CompilationOptions.TypeCheckingDefault,
                CancellationToken = cancellationToken
            };

            var result = Program.Compile(source, filePath, options);

            // Envelope schema v1.1 entries plus a parallel categories array
            // (categories[i] categorizes typeErrors[i]).
            DeclarationIdResolver? declarationIds = null;
            if (result.Ast != null)
            {
                declarationIds = new DeclarationIdResolver();
                declarationIds.AddFile(filePath, source, result.Ast);
            }

            var typeErrors = DiagnosticEnvelope.Build(result.Diagnostics, declarationIds);
            var categories = result.Diagnostics.Select(d => CategorizeError(d.Code)).ToList();

            var output = new TypeCheckOutput
            {
                Success = !result.HasErrors,
                ErrorCount = typeErrors.Count(e => e.Severity == "error"),
                WarningCount = typeErrors.Count(e => e.Severity == "warning"),
                TypeErrors = typeErrors,
                Categories = categories
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
            var tokens = lexer.TokenizeAllForParser();

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

    /// <summary>
    /// Filters diagnostics to the snippet region and rebases them to
    /// snippet-relative coordinates, as envelope schema v1.1 entries. The
    /// wrapped source is synthetic, so location.file and declarationId are null.
    /// </summary>
    private static List<EnvelopeDiagnostic> FilterAndAdjustDiagnostics(DiagnosticBag diagnostics, SnippetWrapper wrapper)
    {
        var result = new List<EnvelopeDiagnostic>();

        foreach (var d in diagnostics.Errors.Concat(diagnostics.Warnings))
        {
            if (wrapper.IsInSnippet(d.Span.Line, d.Span.Column))
            {
                result.Add(new EnvelopeDiagnostic
                {
                    Code = d.Code,
                    Message = d.Message,
                    Severity = d.Severity.ToString().ToLowerInvariant(),
                    Location = new EnvelopeLocation
                    {
                        File = null,
                        Line = wrapper.AdjustLine(d.Span.Line),
                        Column = wrapper.AdjustColumn(d.Span.Line, d.Span.Column),
                        Length = d.Span.Length
                    }
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

        /// <summary>Envelope schema v1.1 diagnostic entries (shared EnvelopeDiagnostic shape).</summary>
        [JsonPropertyName("diagnostics")]
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }

        /// <summary>
        /// Common-mistake guidance for diagnostics that carry no compiler
        /// suggestion, keyed by (line, column, code) of the matching entry.
        /// </summary>
        [JsonPropertyName("hints")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<HintOutput>? Hints { get; init; }

        [JsonPropertyName("fixedSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FixedSource { get; init; }

        [JsonPropertyName("fixesApplied")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? FixesApplied { get; init; }

        /// <summary>
        /// True when the source-level healer (calor format --heal) was applied
        /// on top of the targeted fix edits because errors remained. When true,
        /// success/errorCount/warningCount/diagnostics describe the post-heal
        /// recheck of fixedSource — coordinates match fixedSource, not the
        /// original input.
        /// </summary>
        [JsonPropertyName("healed")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Healed { get; init; }
    }

    private sealed class HintOutput
    {
        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }

        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("commonMistake")]
        public required CommonMistakeOutput CommonMistake { get; init; }
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

    // --- Lint ---

    private sealed class LintResult
    {
        public bool ParseSuccess { get; init; }
        public required List<EnvelopeDiagnostic> ParseErrors { get; init; }
        public required List<LintIssue> Issues { get; init; }
        public required string OriginalContent { get; init; }
        public required string FixedContent { get; init; }
        public required List<EnvelopeDiagnostic> FixDiagnostics { get; init; }
        public string? FixSuppressedReason { get; init; }
    }

    private sealed record LintIssue(int Line, string Message, string Code, EnvelopeFix? Fix);

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

        /// <summary>Envelope schema v1.1 diagnostic entries for parse errors.</summary>
        [JsonPropertyName("parseErrors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EnvelopeDiagnostic>? ParseErrors { get; init; }

        [JsonPropertyName("fixedCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FixedCode { get; init; }

        [JsonPropertyName("fixDiagnostics")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EnvelopeDiagnostic>? FixDiagnostics { get; init; }

        [JsonPropertyName("fixSuppressedReason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FixSuppressedReason { get; init; }
    }

    private sealed class LintIssueOutput
    {
        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("fix")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public EnvelopeFix? Fix { get; init; }
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

        /// <summary>Envelope schema v1.1 diagnostic entries (shared EnvelopeDiagnostic shape).</summary>
        [JsonPropertyName("typeErrors")]
        public required List<EnvelopeDiagnostic> TypeErrors { get; init; }

        /// <summary>Parallel to typeErrors: categories[i] categorizes typeErrors[i].</summary>
        [JsonPropertyName("categories")]
        public required List<string> Categories { get; init; }
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

        /// <summary>Envelope schema v1.1 entries with snippet-relative locations.</summary>
        [JsonPropertyName("diagnostics")]
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }

        [JsonPropertyName("validationLevel")]
        public required string ValidationLevel { get; init; }

        [JsonPropertyName("tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TokenOutput>? Tokens { get; init; }

        [JsonPropertyName("warnings")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<string>? Warnings { get; init; }
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
    /// Column offset for every snippet line after the first: the indent the wrapper added
    /// to place those lines in their block (0 when continuation lines are copied as is).
    /// </summary>
    public int ContinuationColumnOffset { get; }

    /// <summary>
    /// Warnings about context configuration issues.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    private SnippetWrapper(string wrappedSource, int snippetStartLine, int snippetEndLine, int firstLineColumnOffset,
        int continuationColumnOffset, IReadOnlyList<string>? warnings = null)
    {
        WrappedSource = wrappedSource;
        SnippetStartLine = snippetStartLine;
        SnippetEndLine = snippetEndLine;
        FirstLineColumnOffset = firstLineColumnOffset;
        ContinuationColumnOffset = continuationColumnOffset;
        Warnings = warnings ?? [];
    }

    private const string ModuleHeader = "module SnippetCheck";
    private const string ModuleIndent = "  ";
    private const string BodyIndent = "    ";
    private const string ExpressionPrefix = BodyIndent + "let _result = ";

    /// <summary>
    /// Wraps the snippet in native synthetic code based on context: a module, and for every
    /// location except <c>module_body</c> a private function whose header carries the context's
    /// parameters and return type. Block structure is indentation, so each snippet line is
    /// indented into its block (an expression's continuation lines are copied as is).
    /// </summary>
    public static SnippetWrapper Wrap(string snippet, SnippetContext context)
    {
        var sb = new StringBuilder();
        var warnings = new List<string>();
        var lines = snippet.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        sb.Append(ModuleHeader).Append('\n');
        var snippetStart = 2;

        if (context.Location == SnippetLocation.ModuleBody)
        {
            if (context.Parameters.Count > 0)
                warnings.Add("Context 'parameters' ignored for module_body location");
            if (context.ReturnType != null)
                warnings.Add("Context 'returnType' ignored for module_body location");
            if (context.SurroundingCode != null)
                warnings.Add("Context 'surroundingCode' ignored for module_body location");

            AppendIndented(sb, lines, ModuleIndent);
            return new SnippetWrapper(sb.ToString(), snippetStart, snippetStart + lines.Length - 1,
                ModuleIndent.Length, ModuleIndent.Length, warnings);
        }

        var parameters = string.Join(", ", context.Parameters.Select(p => $"{p.Type}:{p.Name}"));
        var returnType = context.ReturnType is null or "unit" ? "void" : context.ReturnType;
        sb.Append(ModuleIndent).Append($"func _validate_ private ({parameters}) → {returnType}").Append('\n');
        snippetStart++;

        if (!string.IsNullOrEmpty(context.SurroundingCode))
        {
            var surrounding = context.SurroundingCode.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            AppendIndented(sb, surrounding, BodyIndent);
            snippetStart += surrounding.Length;
        }

        if (context.Location == SnippetLocation.Expression)
        {
            sb.Append(ExpressionPrefix).Append(lines[0]).Append('\n');
            foreach (var line in lines.Skip(1))
                sb.Append(line).Append('\n');
            return new SnippetWrapper(sb.ToString(), snippetStart, snippetStart + lines.Length - 1,
                ExpressionPrefix.Length, 0, warnings);
        }

        AppendIndented(sb, lines, BodyIndent);
        return new SnippetWrapper(sb.ToString(), snippetStart, snippetStart + lines.Length - 1,
            BodyIndent.Length, BodyIndent.Length, warnings);
    }

    private static void AppendIndented(StringBuilder sb, IEnumerable<string> lines, string indent)
    {
        foreach (var line in lines)
            sb.Append(line.Length > 0 ? indent + line : line).Append('\n');
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
        if (line == SnippetStartLine)
        {
            return Math.Max(1, column - FirstLineColumnOffset);
        }

        return Math.Max(1, column - ContinuationColumnOffset);
    }
}
