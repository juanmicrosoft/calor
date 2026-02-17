using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.LanguageServer.Documentation;

/// <summary>
/// Provides documentation for Calor syntax tags, loaded from the shared JSON documentation.
/// </summary>
public sealed class TagDocumentationProvider
{
    private static readonly Lazy<TagDocumentationProvider> _instance = new(() => new TagDocumentationProvider());

    public static TagDocumentationProvider Instance => _instance.Value;

    private readonly Dictionary<string, TagDocumentation> _tagDocs;
    private readonly bool _isLoaded;

    private TagDocumentationProvider()
    {
        _tagDocs = new Dictionary<string, TagDocumentation>(StringComparer.Ordinal);
        _isLoaded = LoadDocumentation();
    }

    /// <summary>
    /// Gets documentation for a specific Calor tag.
    /// </summary>
    /// <param name="tag">The tag without braces (e.g., "§NEW", "§F", "§L")</param>
    /// <returns>Tag documentation if found, null otherwise.</returns>
    public TagDocumentation? GetTagDocumentation(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;

        // Normalize the tag - remove any trailing content after the tag name
        var normalizedTag = NormalizeTag(tag);

        if (_tagDocs.TryGetValue(normalizedTag, out var doc))
            return doc;

        return null;
    }

    /// <summary>
    /// Tries to extract a Calor tag from text at a given position.
    /// </summary>
    /// <param name="text">The source text</param>
    /// <param name="offset">The character offset</param>
    /// <returns>The tag if found (e.g., "§NEW"), null otherwise.</returns>
    public static string? ExtractTagAtPosition(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset >= text.Length)
            return null;

        // Find the start of the tag (§ character)
        var tagStart = -1;
        for (var i = offset; i >= 0; i--)
        {
            if (text[i] == '§')
            {
                tagStart = i;
                break;
            }
            // Stop if we hit whitespace or delimiters that indicate we're not on a tag
            // Braces, parentheses, etc. mean we've moved past the tag name
            if (char.IsWhiteSpace(text[i]) || text[i] == '{' || text[i] == '}' ||
                text[i] == '(' || text[i] == ')' || text[i] == '[' || text[i] == ']')
            {
                break;
            }
        }

        if (tagStart < 0)
            return null;

        // Extract the tag name (letters after §, optionally with / for closing tags)
        var tagEnd = tagStart + 1;
        var hasSlash = false;

        // Check for closing tag
        if (tagEnd < text.Length && text[tagEnd] == '/')
        {
            hasSlash = true;
            tagEnd++;
        }

        // Collect letters (and some special chars like ?)
        while (tagEnd < text.Length && (char.IsLetter(text[tagEnd]) || text[tagEnd] == '?' || text[tagEnd] == '!'))
        {
            tagEnd++;
        }

        if (tagEnd <= tagStart + 1 + (hasSlash ? 1 : 0))
            return null;

        // Check that the cursor position is within the tag itself
        // (between tagStart and tagEnd), not after it
        if (offset > tagEnd)
            return null;

        var tag = text[tagStart..tagEnd];

        // For closing tags, return the opening tag equivalent
        if (hasSlash && tag.Length > 2)
        {
            return "§" + tag[2..];
        }

        return tag;
    }

    /// <summary>
    /// Gets all available tags.
    /// </summary>
    public IReadOnlyCollection<string> GetAllTags() => _tagDocs.Keys;

    /// <summary>
    /// Checks if documentation was successfully loaded.
    /// </summary>
    public bool IsLoaded => _isLoaded;

    private static string NormalizeTag(string tag)
    {
        // Remove closing tag slash
        if (tag.StartsWith("§/"))
            return "§" + tag[2..];

        return tag;
    }

    private bool LoadDocumentation()
    {
        try
        {
            string? json = null;

            // Try embedded resource from Calor.Compiler
            var compilerAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Calor.Compiler");

            if (compilerAssembly != null)
            {
                var resourceName = "Calor.Compiler.Resources.calor-syntax-documentation.json";
                using var stream = compilerAssembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    json = reader.ReadToEnd();
                }
            }

            // Fall back to file system (for development)
            if (json == null)
            {
                var projectRoot = FindProjectRoot();
                if (projectRoot != null)
                {
                    var filePath = Path.Combine(projectRoot, "src", "Calor.Compiler", "Resources", "calor-syntax-documentation.json");
                    if (File.Exists(filePath))
                    {
                        json = File.ReadAllText(filePath);
                    }
                }
            }

            if (json == null)
                return false;

            var doc = JsonSerializer.Deserialize<SyntaxDocumentation>(json, JsonOptions);
            if (doc?.Tags == null)
                return false;

            foreach (var (tag, info) in doc.Tags)
            {
                _tagDocs[tag] = new TagDocumentation
                {
                    Tag = tag,
                    Name = info.Name,
                    Syntax = info.Syntax,
                    Description = info.Description,
                    CSharpEquivalent = info.CSharpEquivalent
                };
            }

            return _tagDocs.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindProjectRoot()
    {
        var startDirs = new[]
        {
            Directory.GetCurrentDirectory(),
            AppDomain.CurrentDomain.BaseDirectory
        };

        foreach (var startDir in startDirs)
        {
            var dir = startDir;
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
        [JsonPropertyName("tags")]
        public Dictionary<string, TagInfo>? Tags { get; set; }
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
}

/// <summary>
/// Documentation for a single Calor tag.
/// </summary>
public sealed class TagDocumentation
{
    /// <summary>
    /// The tag itself (e.g., "§NEW", "§F").
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Human-readable name (e.g., "New Instance", "Function").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Full syntax pattern (e.g., "§NEW{ClassName}(args)§/NEW").
    /// </summary>
    public required string Syntax { get; init; }

    /// <summary>
    /// Description of what the tag does.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Equivalent C# syntax.
    /// </summary>
    public required string CSharpEquivalent { get; init; }

    /// <summary>
    /// Formats the documentation as Markdown for hover display.
    /// </summary>
    public string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"## {Name}");
        sb.AppendLine();
        sb.AppendLine("```calor");
        sb.AppendLine(Syntax);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine(Description);
        sb.AppendLine();
        sb.AppendLine("**C# equivalent:**");
        sb.AppendLine($"```csharp");
        sb.AppendLine(CSharpEquivalent);
        sb.AppendLine("```");

        return sb.ToString();
    }
}
