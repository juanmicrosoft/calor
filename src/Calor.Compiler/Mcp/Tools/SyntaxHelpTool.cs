using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Init;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for getting Calor syntax help.
/// </summary>
public sealed class SyntaxHelpTool : McpToolBase
{
    /// <summary>
    /// Environment variable to override the skill file path.
    /// </summary>
    public const string SkillFilePathEnvVar = "CALOR_SKILL_FILE";

    private static Lazy<string> SkillContent = new(LoadSkillContent);

    /// <summary>
    /// Resets the cached skill content. Used for testing environment variable overrides.
    /// </summary>
    internal static void ResetCacheForTesting()
    {
        SkillContent = new Lazy<string>(LoadSkillContent);
    }

    private static string LoadSkillContent()
    {
        // 1. Check environment variable first (highest priority)
        var envPath = Environment.GetEnvironmentVariable(SkillFilePathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            try
            {
                return File.ReadAllText(envPath);
            }
            catch
            {
                // Fall through to other methods
            }
        }

        // 2. Look for project root by walking up directories
        var projectRoot = FindProjectRoot();
        if (projectRoot != null)
        {
            var skillPath = Path.Combine(projectRoot, "tests", "Calor.Evaluation", "Skills", "calor-language-skills.md");
            if (File.Exists(skillPath))
            {
                try
                {
                    return File.ReadAllText(skillPath);
                }
                catch
                {
                    // Fall through
                }
            }
        }

        // 3. Try NuGet tool installation path
        var nugetSkillPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Skills", "calor-language-skills.md");
        if (File.Exists(nugetSkillPath))
        {
            try
            {
                return File.ReadAllText(nugetSkillPath);
            }
            catch
            {
                // Fall through
            }
        }

        // 4. Fall back to embedded resource
        try
        {
            return EmbeddedResourceHelper.ReadSkill("calor.md");
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Find the project root by walking up directories looking for Calor.sln or .git
    /// </summary>
    private static string? FindProjectRoot()
    {
        // Start from current directory and base directory
        var startDirs = new[]
        {
            Directory.GetCurrentDirectory(),
            AppDomain.CurrentDomain.BaseDirectory
        };

        foreach (var startDir in startDirs)
        {
            var dir = startDir;
            for (var i = 0; i < 10 && dir != null; i++) // Max 10 levels up
            {
                // Check for markers that indicate project root
                if (File.Exists(Path.Combine(dir, "Calor.sln")) ||
                    Directory.Exists(Path.Combine(dir, ".git")))
                {
                    return dir;
                }

                var parent = Directory.GetParent(dir);
                dir = parent?.FullName;
            }
        }

        return null;
    }

    private static readonly Dictionary<string, string[]> FeatureAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["async"] = ["async", "await", "§AF", "§AMT", "§AWAIT", "§ASYNC"],
        ["contracts"] = ["contract", "§Q", "§S", "precondition", "postcondition", "requires", "ensures"],
        ["effects"] = ["effect", "§E", "side effect", "cw", "cr", "fs:"],
        ["loops"] = ["loop", "for", "while", "§L{", "§WH{", "§DO{", "§BK", "§CN"],
        ["conditionals"] = ["if", "else", "conditional", "§IF{", "§EI", "§EL", "ternary"],
        ["functions"] = ["function", "§F{", "§I{", "§O{", "§R", "return", "parameter"],
        ["classes"] = ["class", "§CL{", "§EXT{", "§IMPL{", "inheritance", "interface"],
        ["generics"] = ["generic", "<T>", "§WHERE", "type parameter", "constraint"],
        ["collections"] = ["list", "dict", "array", "§LIST{", "§DICT{", "§ARR", "§IDX"],
        ["patterns"] = ["pattern", "match", "switch", "§W{", "§K", "§SW{"],
        ["exceptions"] = ["try", "catch", "throw", "exception", "§TR{", "§CA", "§TH"],
        ["lambdas"] = ["lambda", "delegate", "§LAM{", "§DEL{"],
        ["strings"] = ["string", "str", "concat", "substr", "interpolation"],
        ["types"] = ["type", "i32", "i64", "f32", "f64", "bool", "void"],
        ["records"] = ["record", "§D{", "union", "§T{", "§V{"],
        ["enums"] = ["enum", "§EN{", "§ENUM{"],
        ["constructors"] = ["constructor", "§CTOR{", "§BASE", "§THIS"],
        ["properties"] = ["property", "§PROP{", "§GET", "§SET", "field", "§FLD{"]
    };

    public override string Name => "calor_syntax_help";

    public override string Description =>
        "Get Calor syntax documentation for a specific feature. Returns relevant syntax examples and explanations.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "feature": {
                    "type": "string",
                    "description": "Feature to get help for (e.g., 'async', 'contracts', 'effects', 'loops', 'patterns')"
                }
            },
            "required": ["feature"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var feature = GetString(arguments, "feature");
        if (string.IsNullOrEmpty(feature))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: feature"));
        }

        try
        {
            var content = SkillContent.Value;
            if (string.IsNullOrEmpty(content))
            {
                return Task.FromResult(McpToolResult.Error("Syntax documentation not available"));
            }

            var sections = ExtractRelevantSections(content, feature);

            if (sections.Count == 0)
            {
                var availableFeatures = string.Join(", ", FeatureAliases.Keys.OrderBy(k => k));
                return Task.FromResult(McpToolResult.Text(
                    $"No documentation found for '{feature}'. Available features: {availableFeatures}"));
            }

            var output = new SyntaxHelpOutput
            {
                Feature = feature,
                Sections = sections,
                AvailableFeatures = FeatureAliases.Keys.OrderBy(k => k).ToList()
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Failed to get syntax help: {ex.Message}"));
        }
    }

    private static List<SyntaxSection> ExtractRelevantSections(string content, string feature)
    {
        var sections = new List<SyntaxSection>();
        var searchTerms = GetSearchTerms(feature);

        // Split content by ## headers
        var headerPattern = new Regex(@"^## (.+)$", RegexOptions.Multiline);
        var matches = headerPattern.Matches(content);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var title = match.Groups[1].Value.Trim();
            var startIndex = match.Index;
            var endIndex = (i + 1 < matches.Count) ? matches[i + 1].Index : content.Length;
            var sectionContent = content[startIndex..endIndex].Trim();

            // Check if this section is relevant
            if (IsSectionRelevant(title, sectionContent, searchTerms))
            {
                sections.Add(new SyntaxSection
                {
                    Title = title,
                    Content = sectionContent
                });
            }
        }

        // If no sections found by header, try to find template sections
        if (sections.Count == 0)
        {
            var templatePattern = new Regex(@"### Template: (.+?)\n(```calor\n[\s\S]+?```)", RegexOptions.Multiline);
            var templateMatches = templatePattern.Matches(content);

            foreach (Match match in templateMatches)
            {
                var title = match.Groups[1].Value.Trim();
                var templateContent = match.Groups[2].Value.Trim();

                if (searchTerms.Any(term =>
                    title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    templateContent.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    sections.Add(new SyntaxSection
                    {
                        Title = $"Template: {title}",
                        Content = templateContent
                    });
                }
            }
        }

        return sections;
    }

    private static string[] GetSearchTerms(string feature)
    {
        // Check for direct alias match
        foreach (var (key, aliases) in FeatureAliases)
        {
            if (key.Equals(feature, StringComparison.OrdinalIgnoreCase) ||
                aliases.Any(a => a.Equals(feature, StringComparison.OrdinalIgnoreCase)))
            {
                return aliases;
            }
        }

        // Fall back to the feature itself as search term
        return [feature];
    }

    private static bool IsSectionRelevant(string title, string content, string[] searchTerms)
    {
        return searchTerms.Any(term =>
            title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            content.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class SyntaxHelpOutput
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("sections")]
        public required List<SyntaxSection> Sections { get; init; }

        [JsonPropertyName("availableFeatures")]
        public required List<string> AvailableFeatures { get; init; }
    }

    private sealed class SyntaxSection
    {
        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }
}
