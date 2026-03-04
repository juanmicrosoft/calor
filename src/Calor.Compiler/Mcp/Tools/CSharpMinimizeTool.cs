using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Calor.Compiler.Migration;
using CSharpExtensions = Microsoft.CodeAnalysis.CSharpExtensions;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool that analyzes §CSHARP interop blocks and suggests which parts
/// could be converted to native Calor syntax.
/// </summary>
public sealed class CSharpMinimizeTool : McpToolBase
{
    public override string Name => "calor_csharp_minimize";

    public override string Description =>
        "Analyze §CSHARP interop blocks in Calor source and suggest which C# constructs " +
        "could be replaced with native Calor syntax. Helps minimize raw C# in converted files.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code containing §CSHARP blocks to analyze"
                },
                "csharpCode": {
                    "type": "string",
                    "description": "Raw C# code from an interop block to analyze directly"
                },
                "filePath": {
                    "type": "string",
                    "description": "Path to a .calr file to analyze for §CSHARP blocks"
                }
            },

            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
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
                // Analyze raw C# directly
                suggestions.AddRange(AnalyzeCSharpCode(csharpCode));
            }
            else if (!string.IsNullOrEmpty(source))
            {
                // Extract §CSHARP blocks from Calor source and analyze each
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
