using System.Text.RegularExpressions;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Analyzes §CSHARP interop block content to detect C# constructs that have
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
            "foreach", "§L{item:collection}",
            "foreach loops are supported natively via §L{item:collection} — consider converting §CSHARP blocks containing foreach"),

        (new Regex(@"\bswitch\s*[\(\{]", RegexOptions.Compiled),
            "switch", "§W{expr} §K{pattern}",
            "switch/match is supported via §W{expr} §K{pattern} — consider converting switch statements"),

        (new Regex(@"\basync\s+", RegexOptions.Compiled),
            "async", "§AMT/§AF/§AWAIT",
            "async/await is supported via §AMT (async method), §AF (async function), and §AWAIT — consider converting async methods"),

        (new Regex(@"\byield\s+(return|break)", RegexOptions.Compiled),
            "yield", "§YIELD/§YBRK",
            "yield return/break is supported via §YIELD and §YBRK — consider converting iterator methods"),

        (new Regex(@"\busing\s*\(", RegexOptions.Compiled),
            "using-statement", "§USE{var}=expr",
            "using statements are supported via §USE{var}=expr ... §/USE — consider converting resource management blocks"),

        (new Regex(@"\bdelegate\s+", RegexOptions.Compiled),
            "delegate", "§DEL{id:name:vis}",
            "delegate definitions are supported via §DEL — consider converting delegate type declarations"),

        (new Regex(@"\bevent\s+", RegexOptions.Compiled),
            "event", "§EVT{id:name:vis}",
            "events are supported via §EVT — consider converting event declarations"),

        // Match both conversion operators and regular operators as one hint
        (new Regex(@"\boperator\s+[+\-*/=<>!~%&|^]|\b(?:implicit|explicit)\s+operator\b", RegexOptions.Compiled),
            "operator", "§OP{id:operator:vis}",
            "operator overloading (including implicit/explicit conversions) is supported via §OP — consider converting operator definitions"),

        (new Regex(@"#if\s+\w", RegexOptions.Compiled),
            "preprocessor", "§PP{CONDITION}",
            "preprocessor directives are supported via §PP{CONDITION} ... §/PP — consider converting #if/#else/#endif blocks"),

        (new Regex(@"\bstruct\s+\w", RegexOptions.Compiled),
            "struct", "§CL{id:name:vis:struct}",
            "structs are supported via §CL with struct modifier — consider converting struct definitions"),

        (new Regex(@"\bwhere\s+\w+\s*:", RegexOptions.Compiled),
            "generic-constraint", "§WHERE{T:constraint}",
            "generic type constraints are supported via §WHERE — consider converting where clauses"),
    ];

    /// <summary>
    /// Analyzes Calor source for §CSHARP interop blocks and returns hints about
    /// C# constructs that could be expressed natively.
    /// </summary>
    /// <param name="calorSource">The generated Calor source code.</param>
    /// <returns>List of feature hints, deduplicated by feature.</returns>
    public static List<string> AnalyzeInteropBlocks(string? calorSource)
    {
        if (string.IsNullOrWhiteSpace(calorSource))
            return [];

        // Extract content from §CSHARP{...}§/CSHARP blocks
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
    /// Extracts and concatenates the content of all §CSHARP{...}§/CSHARP blocks.
    /// The actual format emitted by CalorEmitter is: §CSHARP{code}§/CSHARP
    /// where the end marker is }§/CSHARP.
    /// </summary>
    private static string ExtractInteropContent(string source)
    {
        // Match §CSHARP{ ... }§/CSHARP — the actual emit format from CalorEmitter
        var pattern = new Regex(@"§CSHARP\{(.*?)\}§/CSHARP", RegexOptions.Singleline);
        var matches = pattern.Matches(source);
        if (matches.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        foreach (Match match in matches)
        {
            // Group 1 is the content inside the braces
            sb.AppendLine(match.Groups[1].Value);
        }
        return sb.ToString();
    }
}
