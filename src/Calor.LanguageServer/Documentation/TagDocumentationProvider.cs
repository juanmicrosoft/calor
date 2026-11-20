using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

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
    /// <param name="tag">The native keyword or operator.</param>
    /// <returns>Tag documentation if found, null otherwise.</returns>
    public TagDocumentation? GetTagDocumentation(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;

        if (_tagDocs.TryGetValue(tag, out var doc))
            return doc;

        return null;
    }

    /// <summary>
    /// Tries to extract a Calor tag from text at a given position.
    /// </summary>
    /// <param name="text">The source text</param>
    /// <param name="offset">The character offset</param>
    /// <returns>The native syntax token if found, null otherwise.</returns>
    public static string? ExtractTagAtPosition(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset >= text.Length)
            return null;

        Token? previous = null;
        foreach (var token in new Lexer(text, new DiagnosticBag()).Tokenize())
        {
            if (token.Span.Contains(offset))
                return GetSyntaxTag(token, previous);
            if (token.Span.Start > offset)
                return null;
            previous = token;
        }
        return null;
    }

    /// <summary>
    /// Returns the documented native keyword or operator <paramref name="token"/> spells, or null.
    /// A token glued to a lexer error (for example the rest of a retired pre-0.30 marker, which the
    /// lexer reports as an error token) is not native syntax and gets no documentation.
    /// </summary>
    internal static string? GetSyntaxTag(Token token, Token? previous)
        => !(previous is { Kind: TokenKind.Error } error && error.Span.End == token.Span.Start)
            && (token.IsKeyword || token.Kind is TokenKind.Arrow or TokenKind.FatArrow
                or TokenKind.Exclamation or TokenKind.RangeOp)
            && Instance._tagDocs.ContainsKey(token.Text)
            ? token.Text
            : null;

    /// <summary>
    /// Gets all available tags.
    /// </summary>
    public IReadOnlyCollection<string> GetAllTags() => _tagDocs.Keys;

    /// <summary>
    /// Checks if documentation was successfully loaded.
    /// </summary>
    public bool IsLoaded => _isLoaded;

    private bool LoadDocumentation()
    {
        var json = Calor.Compiler.Init.EmbeddedResourceHelper.ReadResource(
            "Calor.Compiler.Resources.calor-syntax-documentation.json");
        var doc = JsonSerializer.Deserialize<SyntaxDocumentation>(json, JsonOptions);
        if (doc?.Tags is not { Count: > 0 })
            throw new InvalidDataException("Packaged syntax documentation has no hover tags.");
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
        return true;
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
    /// The native keyword or operator (e.g., "new", "func").
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Human-readable name (e.g., "New Instance", "Function").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Full syntax pattern (e.g., "new ClassName(args)").
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
