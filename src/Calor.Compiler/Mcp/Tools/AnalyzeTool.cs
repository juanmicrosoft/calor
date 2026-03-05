using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Migration;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpExtensions = Microsoft.CodeAnalysis.CSharpExtensions;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for security/bug pattern analysis, migration assessment scoring,
/// and C# interop block minimization.
/// </summary>
public sealed class AnalyzeTool : McpToolBase
{
    public override string Name => "calor_analyze";

    public override string Description =>
        "Analyze Calor code — security/bug patterns, migration assessment scoring, or C# interop block minimization";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["security", "assess", "minimize"],
                    "description": "Analysis type. security=security/bug pattern analysis, assess=8-dimension migration scoring, minimize=analyze C# interop blocks for native Calor equivalents",
                    "default": "security"
                },
                "source": {
                    "type": "string",
                    "description": "Source code to analyze (Calor for security/minimize, C# for assess)"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "enableDataflow": {
                            "type": "boolean",
                            "default": true,
                            "description": "Enable dataflow analysis (security action)"
                        },
                        "enableBugPatterns": {
                            "type": "boolean",
                            "default": true,
                            "description": "Enable bug pattern detection (security action)"
                        },
                        "enableTaintAnalysis": {
                            "type": "boolean",
                            "default": true,
                            "description": "Enable security taint analysis (security action)"
                        },
                        "threshold": {
                            "type": "integer",
                            "default": 0,
                            "description": "Minimum score (0-100) to include in results (assess action)"
                        }
                    }
                },
                "files": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "path": { "type": "string", "description": "File path/name for identification" },
                            "source": { "type": "string", "description": "C# source code content" }
                        },
                        "required": ["path", "source"]
                    },
                    "description": "Multiple C# files to assess (assess action, multi-file mode)"
                },
                "csharpCode": {
                    "type": "string",
                    "description": "Raw C# code from an interop block to analyze directly (minimize action)"
                },
                "filePath": {
                    "type": "string",
                    "description": "Path to a .calr file to analyze for §CSHARP blocks (minimize action)"
                }
            },

            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var action = GetString(arguments, "action") ?? "security";

        return action switch
        {
            "security" => HandleSecurity(arguments, cancellationToken),
            "assess" => HandleAssess(arguments),
            "minimize" => HandleMinimize(arguments),
            _ => Task.FromResult(McpToolResult.Error($"Unknown action: {action}. Use 'security', 'assess', or 'minimize'."))
        };
    }

    // ── Security analysis ──────────────────────────────────────────────

    private Task<McpToolResult> HandleSecurity(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));
        }

        var options = GetOptions(arguments);
        var enableDataflow = GetBool(options, "enableDataflow", defaultValue: true);
        var enableBugPatterns = GetBool(options, "enableBugPatterns", defaultValue: true);
        var enableTaintAnalysis = GetBool(options, "enableTaintAnalysis", defaultValue: true);

        try
        {
            var analysisOptions = new VerificationAnalysisOptions
            {
                EnableDataflow = enableDataflow,
                EnableBugPatterns = enableBugPatterns,
                EnableTaintAnalysis = enableTaintAnalysis,
                EnableKInduction = false,
                UseZ3Verification = true
            };

            var compileOptions = new CompilationOptions
            {
                EnableVerificationAnalyses = true,
                VerificationAnalysisOptions = analysisOptions,
                VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
                CancellationToken = cancellationToken
            };

            var result = Program.Compile(source, "mcp-analyze.calr", compileOptions);

            var analysisResult = compileOptions.VerificationAnalysisResult;

            var diagnosticsByCategory = result.Diagnostics
                .GroupBy(d => CategorizeIssue(d.Code.ToString()))
                .ToDictionary(g => g.Key, g => g.Select(d => new IssueOutput
                {
                    Code = d.Code.ToString(),
                    Message = d.Message,
                    Severity = d.IsError ? "error" : "warning",
                    Line = d.Span.Line,
                    Column = d.Span.Column
                }).ToList());

            var output = new SecurityOutput
            {
                Success = !result.HasErrors,
                Summary = new AnalysisSummaryOutput
                {
                    FunctionsAnalyzed = analysisResult?.FunctionsAnalyzed ?? 0,
                    DataflowIssues = analysisResult?.DataflowIssues ?? 0,
                    BugPatternsFound = analysisResult?.BugPatternsFound ?? 0,
                    TaintVulnerabilities = analysisResult?.TaintVulnerabilities ?? 0,
                    DurationMs = (int)(analysisResult?.Duration.TotalMilliseconds ?? 0)
                },
                SecurityIssues = diagnosticsByCategory.GetValueOrDefault("security", []),
                BugPatterns = diagnosticsByCategory.GetValueOrDefault("bugpattern", []),
                DataflowIssues = diagnosticsByCategory.GetValueOrDefault("dataflow", []),
                OtherIssues = diagnosticsByCategory.GetValueOrDefault("other", [])
            };

            return Task.FromResult(McpToolResult.Json(output, isError: result.HasErrors));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Analysis failed: {ex.Message}"));
        }
    }

    private static string CategorizeIssue(string code)
    {
        if (code.Contains("Taint") || code.Contains("Security") ||
            code.Contains("Injection") || code.Contains("Xss"))
            return "security";

        if (code.Contains("DivideByZero") || code.Contains("NullDeref") ||
            code.Contains("Overflow") || code.Contains("OutOfBounds") ||
            code.Contains("BugPattern"))
            return "bugpattern";

        if (code.Contains("Uninitialized") || code.Contains("DeadStore") ||
            code.Contains("DeadCode") || code.Contains("Dataflow"))
            return "dataflow";

        return "other";
    }

    // ── Assess (migration scoring) ─────────────────────────────────────

    private Task<McpToolResult> HandleAssess(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var filesElement = GetArray(arguments, "files");
        var options = GetOptions(arguments);
        var threshold = GetInt(options, "threshold", 0);

        if (string.IsNullOrEmpty(source) && filesElement == null)
        {
            return Task.FromResult(McpToolResult.Error(
                "Missing required parameter: provide either 'source' (single file) or 'files' (multi-file)"));
        }

        try
        {
            var analyzer = new MigrationAnalyzer();
            var files = new List<AssessFileResult>();

            if (!string.IsNullOrEmpty(source))
            {
                var result = analyzer.AnalyzeSource(source, "input.cs", "input.cs");
                if (!result.WasSkipped && result.TotalScore >= threshold)
                {
                    files.Add(CreateFileResult(result));
                }
            }
            else if (filesElement != null)
            {
                foreach (var fileElement in filesElement.Value.EnumerateArray())
                {
                    var path = fileElement.TryGetProperty("path", out var pathProp)
                        ? pathProp.GetString() ?? "unknown.cs"
                        : "unknown.cs";
                    var fileSource = fileElement.TryGetProperty("source", out var sourceProp)
                        ? sourceProp.GetString() ?? ""
                        : "";

                    if (string.IsNullOrEmpty(fileSource)) continue;

                    var result = analyzer.AnalyzeSource(fileSource, path, path);
                    if (!result.WasSkipped && result.TotalScore >= threshold)
                    {
                        files.Add(CreateFileResult(result));
                    }
                }
            }

            files.Sort((a, b) => b.Score.CompareTo(a.Score));

            var summary = new AssessSummary
            {
                TotalFiles = files.Count,
                AverageScore = files.Count > 0 ? Math.Round(files.Average(f => f.Score), 1) : 0,
                PriorityBreakdown = new PriorityBreakdown
                {
                    Critical = files.Count(f => f.Priority == "critical"),
                    High = files.Count(f => f.Priority == "high"),
                    Medium = files.Count(f => f.Priority == "medium"),
                    Low = files.Count(f => f.Priority == "low")
                }
            };

            var output = new AssessOutput
            {
                Success = true,
                Summary = summary,
                Files = files
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Assessment failed: {ex.Message}"));
        }
    }

    private static AssessFileResult CreateFileResult(FileMigrationScore score)
    {
        var dimensions = new Dictionary<string, DimensionResult>();
        foreach (var (dimension, dimScore) in score.Dimensions)
        {
            dimensions[dimension.ToString()] = new DimensionResult
            {
                Score = (int)Math.Round(dimScore.RawScore),
                Patterns = dimScore.PatternCount
            };
        }

        var unsupported = score.UnsupportedConstructs.Select(c => new UnsupportedConstructResult
        {
            Name = c.Name,
            Count = c.Count,
            Description = c.Description
        }).ToList();

        return new AssessFileResult
        {
            Path = score.RelativePath,
            Score = (int)Math.Round(score.TotalScore),
            Priority = FileMigrationScore.GetPriorityLabel(score.Priority).ToLowerInvariant(),
            Dimensions = dimensions,
            UnsupportedConstructs = unsupported,
            LineCount = score.LineCount,
            MethodCount = score.MethodCount,
            TypeCount = score.TypeCount
        };
    }

    private static JsonElement? GetArray(JsonElement? arguments, string propertyName)
    {
        if (arguments == null || arguments.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (arguments.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
            return prop;

        return null;
    }

    // ── Minimize (C# interop block analysis) ───────────────────────────

    private Task<McpToolResult> HandleMinimize(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        var csharpCode = GetString(arguments, "csharpCode");
        var filePath = GetString(arguments, "filePath");

        if (!string.IsNullOrEmpty(filePath))
        {
            if (!File.Exists(filePath))
            {
                return Task.FromResult(McpToolResult.Error($"File not found: {filePath}"));
            }
            source = File.ReadAllText(filePath);
        }

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(csharpCode))
        {
            return Task.FromResult(McpToolResult.Error("Provide 'source' (Calor with §CSHARP blocks) or 'csharpCode' (raw C#)"));
        }

        try
        {
            var suggestions = new List<MinimizeSuggestion>();

            if (!string.IsNullOrEmpty(csharpCode))
            {
                suggestions.AddRange(AnalyzeCSharpCode(csharpCode));
            }
            else if (!string.IsNullOrEmpty(source))
            {
                var blocks = ExtractCSharpBlocks(source);
                foreach (var block in blocks)
                {
                    var blockSuggestions = AnalyzeCSharpCode(block.Code);
                    foreach (var s in blockSuggestions)
                    {
                        s.BlockLine = block.Line;
                    }
                    suggestions.AddRange(blockSuggestions);
                }
            }

            var output = new MinimizeOutput
            {
                TotalBlocks = !string.IsNullOrEmpty(source)
                    ? ExtractCSharpBlocks(source).Count
                    : 1,
                ConvertibleConstructs = suggestions.Count(s => s.Confidence == "high"),
                PartialConstructs = suggestions.Count(s => s.Confidence == "medium"),
                Suggestions = suggestions
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Analysis failed: {ex.Message}"));
        }
    }

    private static List<CSharpBlock> ExtractCSharpBlocks(string source)
    {
        var blocks = new List<CSharpBlock>();
        var lines = source.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("§CSHARP", StringComparison.Ordinal))
            {
                var codeLines = new List<string>();
                int j = i + 1;
                while (j < lines.Length && !lines[j].TrimStart().StartsWith("§/CSHARP", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[j]);
                    j++;
                }
                blocks.Add(new CSharpBlock { Code = string.Join('\n', codeLines), Line = i + 1 });
                i = j;
            }
        }

        return blocks;
    }

    private static List<MinimizeSuggestion> AnalyzeCSharpCode(string code)
    {
        var suggestions = new List<MinimizeSuggestion>();

        try
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case MethodDeclarationSyntax method:
                        var asyncMod = method.Modifiers.Any(m => CSharpExtensions.IsKind(m, SyntaxKind.AsyncKeyword));
                        var info = FeatureSupport.GetFeatureInfo("method");
                        if (info?.Support == SupportLevel.Full)
                        {
                            suggestions.Add(new MinimizeSuggestion
                            {
                                Feature = asyncMod ? "async method" : "method",
                                Construct = $"Method '{method.Identifier.Text}'",
                                Confidence = "high",
                                CalorEquivalent = asyncMod
                                    ? $"§AF{{id:{method.Identifier.Text}:pub}}"
                                    : $"§MT{{id:{method.Identifier.Text}:pub}}"
                            });
                        }
                        break;

                    case PropertyDeclarationSyntax prop:
                        var propInfo = FeatureSupport.GetFeatureInfo("property");
                        if (propInfo?.Support == SupportLevel.Full)
                        {
                            suggestions.Add(new MinimizeSuggestion
                            {
                                Feature = "property",
                                Construct = $"Property '{prop.Identifier.Text}'",
                                Confidence = "high",
                                CalorEquivalent = $"§PROP{{id:{prop.Identifier.Text}:{prop.Type}}}"
                            });
                        }
                        break;

                    case FieldDeclarationSyntax field:
                        var fieldInfo = FeatureSupport.GetFeatureInfo("field");
                        if (fieldInfo?.Support == SupportLevel.Full)
                        {
                            foreach (var variable in field.Declaration.Variables)
                            {
                                suggestions.Add(new MinimizeSuggestion
                                {
                                    Feature = "field",
                                    Construct = $"Field '{variable.Identifier.Text}'",
                                    Confidence = "high",
                                    CalorEquivalent = $"§FLD{{id:{variable.Identifier.Text}:{field.Declaration.Type}}}"
                                });
                            }
                        }
                        break;

                    case ConstructorDeclarationSyntax ctor:
                        var ctorInfo = FeatureSupport.GetFeatureInfo("constructor");
                        if (ctorInfo?.Support == SupportLevel.Full)
                        {
                            suggestions.Add(new MinimizeSuggestion
                            {
                                Feature = "constructor",
                                Construct = $"Constructor '{ctor.Identifier.Text}'",
                                Confidence = "high",
                                CalorEquivalent = "§CTOR{id:pub}"
                            });
                        }
                        break;

                    case EnumDeclarationSyntax enumDecl:
                        suggestions.Add(new MinimizeSuggestion
                        {
                            Feature = "enum",
                            Construct = $"Enum '{enumDecl.Identifier.Text}'",
                            Confidence = "high",
                            CalorEquivalent = $"§EN{{id:{enumDecl.Identifier.Text}}}"
                        });
                        break;

                    case OperatorDeclarationSyntax op:
                        var opInfo = FeatureSupport.GetFeatureInfo("operator-overload");
                        if (opInfo?.Support == SupportLevel.Full)
                        {
                            suggestions.Add(new MinimizeSuggestion
                            {
                                Feature = "operator overload",
                                Construct = $"Operator '{op.OperatorToken.Text}'",
                                Confidence = "medium",
                                CalorEquivalent = $"§OP{{id:{op.OperatorToken.Text}}}"
                            });
                        }
                        break;

                    case EventFieldDeclarationSyntax:
                        suggestions.Add(new MinimizeSuggestion
                        {
                            Feature = "event",
                            Construct = "Event declaration",
                            Confidence = "medium",
                            CalorEquivalent = "§EVT{id:name:type}"
                        });
                        break;
                }
            }
        }
        catch
        {
            // If parsing fails, the C# is too complex or incomplete — skip analysis
        }

        return suggestions;
    }

    // ── DTOs: Security ─────────────────────────────────────────────────

    private sealed class SecurityOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("summary")]
        public required AnalysisSummaryOutput Summary { get; init; }

        [JsonPropertyName("securityIssues")]
        public required List<IssueOutput> SecurityIssues { get; init; }

        [JsonPropertyName("bugPatterns")]
        public required List<IssueOutput> BugPatterns { get; init; }

        [JsonPropertyName("dataflowIssues")]
        public required List<IssueOutput> DataflowIssues { get; init; }

        [JsonPropertyName("otherIssues")]
        public required List<IssueOutput> OtherIssues { get; init; }
    }

    private sealed class AnalysisSummaryOutput
    {
        [JsonPropertyName("functionsAnalyzed")]
        public int FunctionsAnalyzed { get; init; }

        [JsonPropertyName("dataflowIssues")]
        public int DataflowIssues { get; init; }

        [JsonPropertyName("bugPatternsFound")]
        public int BugPatternsFound { get; init; }

        [JsonPropertyName("taintVulnerabilities")]
        public int TaintVulnerabilities { get; init; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }
    }

    private sealed class IssueOutput
    {
        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("severity")]
        public required string Severity { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }
    }

    // ── DTOs: Assess ───────────────────────────────────────────────────

    private sealed class AssessOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("summary")]
        public required AssessSummary Summary { get; init; }

        [JsonPropertyName("files")]
        public required List<AssessFileResult> Files { get; init; }
    }

    private sealed class AssessSummary
    {
        [JsonPropertyName("totalFiles")]
        public int TotalFiles { get; init; }

        [JsonPropertyName("averageScore")]
        public double AverageScore { get; init; }

        [JsonPropertyName("priorityBreakdown")]
        public required PriorityBreakdown PriorityBreakdown { get; init; }
    }

    private sealed class PriorityBreakdown
    {
        [JsonPropertyName("critical")]
        public int Critical { get; init; }

        [JsonPropertyName("high")]
        public int High { get; init; }

        [JsonPropertyName("medium")]
        public int Medium { get; init; }

        [JsonPropertyName("low")]
        public int Low { get; init; }
    }

    private sealed class AssessFileResult
    {
        [JsonPropertyName("path")]
        public required string Path { get; init; }

        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("priority")]
        public required string Priority { get; init; }

        [JsonPropertyName("dimensions")]
        public required Dictionary<string, DimensionResult> Dimensions { get; init; }

        [JsonPropertyName("unsupportedConstructs")]
        public required List<UnsupportedConstructResult> UnsupportedConstructs { get; init; }

        [JsonPropertyName("lineCount")]
        public int LineCount { get; init; }

        [JsonPropertyName("methodCount")]
        public int MethodCount { get; init; }

        [JsonPropertyName("typeCount")]
        public int TypeCount { get; init; }
    }

    private sealed class DimensionResult
    {
        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("patterns")]
        public int Patterns { get; init; }
    }

    private sealed class UnsupportedConstructResult
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }
    }

    // ── DTOs: Minimize ─────────────────────────────────────────────────

    private sealed class CSharpBlock
    {
        public required string Code { get; init; }
        public int Line { get; init; }
    }

    private sealed class MinimizeOutput
    {
        [JsonPropertyName("totalBlocks")]
        public int TotalBlocks { get; init; }

        [JsonPropertyName("convertibleConstructs")]
        public int ConvertibleConstructs { get; init; }

        [JsonPropertyName("partialConstructs")]
        public int PartialConstructs { get; init; }

        [JsonPropertyName("suggestions")]
        public required List<MinimizeSuggestion> Suggestions { get; init; }
    }

    private sealed class MinimizeSuggestion
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("construct")]
        public required string Construct { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; set; }

        [JsonPropertyName("calorEquivalent")]
        public required string CalorEquivalent { get; init; }

        [JsonPropertyName("blockLine")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int BlockLine { get; set; }
    }
}
