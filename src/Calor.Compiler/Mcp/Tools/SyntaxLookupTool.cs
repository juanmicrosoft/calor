using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for looking up C# → Calor syntax mappings with fuzzy matching.
/// </summary>
public sealed class SyntaxLookupTool : McpToolBase
{
    private static readonly Lazy<SyntaxDocumentation?> _documentation = new(LoadDocumentation);

    public override string Name => "calor_syntax_lookup";

    public override string Description =>
        "Look up Calor syntax for a C# construct. Supports fuzzy matching for queries like 'object instantiation', 'for loop', 'async method', etc.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "C# construct to look up (e.g., 'object instantiation', 'for loop', 'async method', 'try catch')"
                }
            },
            "required": ["query"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var query = GetString(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: query"));
        }

        var doc = _documentation.Value;
        if (doc == null)
        {
            return Task.FromResult(McpToolResult.Error("Syntax documentation not available"));
        }

        var matches = FindMatches(doc, query);

        if (matches.Count == 0)
        {
            var availableConstructs = doc.Constructs
                .Select(c => c.CSharpConstruct)
                .OrderBy(c => c)
                .ToList();

            return Task.FromResult(McpToolResult.Json(new LookupResult
            {
                Found = false,
                Query = query,
                Message = $"No matches found for '{query}'",
                AvailableConstructs = availableConstructs
            }));
        }

        var bestMatch = matches[0];
        var result = new LookupResult
        {
            Found = true,
            Query = query,
            Construct = bestMatch.CSharpConstruct,
            CalorSyntax = bestMatch.CalorSyntax,
            Description = bestMatch.Description,
            Examples = bestMatch.Examples.Select(e => new ExampleOutput
            {
                CSharp = e.CSharp,
                Calor = e.Calor
            }).ToList(),
            OtherMatches = matches.Skip(1).Take(3).Select(m => m.CSharpConstruct).ToList()
        };

        return Task.FromResult(McpToolResult.Json(result));
    }

    private static List<SyntaxConstruct> FindMatches(SyntaxDocumentation doc, string query)
    {
        var queryLower = query.ToLowerInvariant();
        var queryTerms = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var scored = new List<(SyntaxConstruct Construct, int Score)>();

        foreach (var construct in doc.Constructs)
        {
            var score = CalculateMatchScore(construct, queryLower, queryTerms);
            if (score > 0)
            {
                scored.Add((construct, score));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Select(s => s.Construct)
            .ToList();
    }

    private static int CalculateMatchScore(SyntaxConstruct construct, string queryLower, string[] queryTerms)
    {
        var score = 0;

        // Exact match on construct name (highest priority)
        if (construct.CSharpConstruct.Equals(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        // Construct name contains query
        if (construct.CSharpConstruct.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }

        // Query contains construct name
        if (queryLower.Contains(construct.CSharpConstruct.ToLowerInvariant()))
        {
            score += 400;
        }

        // Keyword matches
        foreach (var keyword in construct.Keywords)
        {
            var keywordLower = keyword.ToLowerInvariant();

            // Exact keyword match
            if (queryTerms.Contains(keywordLower))
            {
                score += 100;
            }
            // Query contains keyword
            else if (queryLower.Contains(keywordLower))
            {
                score += 50;
            }
            // Keyword contains query term
            else if (queryTerms.Any(t => keywordLower.Contains(t)))
            {
                score += 25;
            }
        }

        // Description matches (lower priority)
        foreach (var term in queryTerms)
        {
            if (construct.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }
        }

        // Calor syntax tag match
        if (queryLower.StartsWith("§") && construct.CalorSyntax.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        return score;
    }

    private static SyntaxDocumentation? LoadDocumentation()
    {
        try
        {
            // Try embedded resource first
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Calor.Compiler.Resources.calor-syntax-documentation.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                return JsonSerializer.Deserialize<SyntaxDocumentation>(json, JsonOptions);
            }

            // Fall back to file system (for development)
            var projectRoot = FindProjectRoot();
            if (projectRoot != null)
            {
                var filePath = Path.Combine(projectRoot, "src", "Calor.Compiler", "Resources", "calor-syntax-documentation.json");
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<SyntaxDocumentation>(json, JsonOptions);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindProjectRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 10 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Calor.sln")) ||
                Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #region JSON Models

    private sealed class SyntaxDocumentation
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("constructs")]
        public List<SyntaxConstruct> Constructs { get; set; } = new();

        [JsonPropertyName("tags")]
        public Dictionary<string, TagInfo> Tags { get; set; } = new();
    }

    private sealed class SyntaxConstruct
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("csharpConstruct")]
        public string CSharpConstruct { get; set; } = "";

        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();

        [JsonPropertyName("calorSyntax")]
        public string CalorSyntax { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("examples")]
        public List<SyntaxExample> Examples { get; set; } = new();
    }

    private sealed class SyntaxExample
    {
        [JsonPropertyName("csharp")]
        public string CSharp { get; set; } = "";

        [JsonPropertyName("calor")]
        public string Calor { get; set; } = "";
    }

    private sealed class TagInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("syntax")]
        public string Syntax { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("csharpEquivalent")]
        public string CSharpEquivalent { get; set; } = "";
    }

    #endregion

    #region Output Models

    private sealed class LookupResult
    {
        [JsonPropertyName("found")]
        public bool Found { get; init; }

        [JsonPropertyName("query")]
        public required string Query { get; init; }

        [JsonPropertyName("construct")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Construct { get; init; }

        [JsonPropertyName("calorSyntax")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalorSyntax { get; init; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; init; }

        [JsonPropertyName("examples")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ExampleOutput>? Examples { get; init; }

        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; init; }

        [JsonPropertyName("otherMatches")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? OtherMatches { get; init; }

        [JsonPropertyName("availableConstructs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? AvailableConstructs { get; init; }
    }

    private sealed class ExampleOutput
    {
        [JsonPropertyName("csharp")]
        public required string CSharp { get; init; }

        [JsonPropertyName("calor")]
        public required string Calor { get; init; }
    }

    #endregion
}
