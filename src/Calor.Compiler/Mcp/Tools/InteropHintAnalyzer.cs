using System.Text.RegularExpressions;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Analyzes raw C# interop payloads to detect C# constructs that have
/// native Calor equivalents. Produces actionable hints for agents.
/// </summary>
internal static class InteropHintAnalyzer
{
    /// <summary>
    /// A detected C# construct and its native Calor equivalent.
    /// </summary>
    internal sealed record FeatureHint(string CSharpKeyword, string CalorSyntax, string Message);

    /// <summary>
    /// Mapping from C# keywords/patterns to Calor native syntax hints.
    /// Each entry: (regex pattern, C# feature name, Calor syntax tag, hint message).
    /// The conversion-operator rule is checked first so it can be skipped when the
    /// more general operator rule also matches.
    /// </summary>
    private static readonly (Regex Pattern, string Feature, string CalorTag, string HintTemplate)[] HintRules =
    [
        (new Regex(@"\bforeach\s*\(", RegexOptions.Compiled),
            "foreach", "each T:item in collection",
            "foreach loops are supported natively via each T:item in collection — consider converting raw C# blocks containing foreach"),

        (new Regex(@"\bswitch\s*[\(\{]", RegexOptions.Compiled),
            "switch", "match value / case pattern",
            "switch/match is supported via match value with case pattern arms — consider converting switch statements"),

        (new Regex(@"\basync\s+", RegexOptions.Compiled),
            "async", "func Name async / await",
            "async/await is supported via func Name async (...) → T and await expr — consider converting async methods"),

        (new Regex(@"\byield\s+(return|break)", RegexOptions.Compiled),
            "yield", "yield expr / yield break",
            "yield return/break is supported via yield expr and yield break — consider converting iterator methods"),

        (new Regex(@"\busing\s*\(", RegexOptions.Compiled),
            "using-statement", "using T:name = resource",
            "using statements are supported via using T:name = resource with an indented body — consider converting resource management blocks"),

        (new Regex(@"\bdelegate\s+", RegexOptions.Compiled),
            "delegate", "delegate Name (T:x) → R",
            "delegate definitions are supported via delegate Name (T:x) → R — consider converting delegate type declarations"),

        (new Regex(@"\bevent\s+", RegexOptions.Compiled),
            "event", "event DelegateType:Name",
            "events are supported via event DelegateType:Name — consider converting event declarations"),

        // Match both conversion operators and regular operators as one hint
        (new Regex(@"\boperator\s+[+\-*/=<>!~%&|^]|\b(?:implicit|explicit)\s+operator\b", RegexOptions.Compiled),
            "operator", "op + (T:left, T:right) → R",
            "operator overloading (including implicit/explicit conversions) is supported via op — consider converting operator definitions"),

        (new Regex(@"#if\s+\w", RegexOptions.Compiled),
            "preprocessor", "pp CONDITION",
            "preprocessor directives are supported via pp CONDITION, pp elif and pp else with indented bodies — consider converting #if/#else/#endif blocks"),

        (new Regex(@"\bstruct\s+\w", RegexOptions.Compiled),
            "struct", "class Name struct",
            "structs are supported via class Name struct — consider converting struct definitions"),

        (new Regex(@"\bwhere\s+\w+\s*:", RegexOptions.Compiled),
            "generic-constraint", "where T : constraint",
            "generic type constraints are supported via where T : constraint — consider converting where clauses"),

        (new Regex(@"\bthis\s*\[", RegexOptions.Compiled),
            "indexer", "this[int:index] → T",
            "indexers (this[]) are supported via this[int:index] → T — consider converting indexer definitions"),
    ];

    /// <summary>
    /// Analyzes Calor source for raw C# interop blocks and returns hints about
    /// C# constructs that could be expressed natively.
    /// </summary>
    /// <param name="calorSource">The generated Calor source code.</param>
    /// <returns>List of feature hints, deduplicated by feature.</returns>
    public static List<string> AnalyzeInteropBlocks(string? calorSource)
    {
        if (string.IsNullOrWhiteSpace(calorSource))
            return [];

        // Extract the payloads of raw C# frames
        var interopContent = ExtractInteropContent(calorSource);
        if (string.IsNullOrWhiteSpace(interopContent))
            return [];

        var seenFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hints = new List<string>();

        foreach (var (pattern, feature, _, hintMessage) in HintRules)
        {
            if (seenFeatures.Contains(feature))
                continue;

            if (pattern.IsMatch(interopContent))
            {
                seenFeatures.Add(feature);
                hints.Add(hintMessage);
            }
        }

        return hints;
    }

    /// <summary>
    /// Extracts interop payloads using the lexer, including legacy frames.
    /// </summary>
    private static string ExtractInteropContent(string source)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var token in Parsing.RawCSharpFrame.ReadTokens(source)
            .Where(t => t.Kind == Parsing.TokenKind.CSharpInterop))
            sb.AppendLine(token.Value is Parsing.RawCSharpFrame frame
                ? frame.Code : token.Value as string);
        return sb.ToString();
    }
}
