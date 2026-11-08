namespace Calor.Compiler.Parsing;

/// <summary>
/// Provides suggestions for unknown section markers (§-prefixed keywords).
/// Includes typo correction using Levenshtein distance.
/// </summary>
public static class SectionMarkerSuggestions
{
    /// <summary>
    /// Registered section markers eligible for canonical-syntax suggestions.
    /// </summary>
    public static readonly IReadOnlyList<string> AllMarkers = Lexer.KeywordNames
        .Where(marker => !marker.StartsWith('/') || marker is "/C" or "/LAM" or "/RAW" or "/CSHARP")
        .ToArray();

    /// <summary>
    /// Common expansions/descriptions for section markers.
    /// Used to help users understand what each marker means.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> MarkerDescriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Core
        ["M"] = "Module",
        ["F"] = "Function",
        ["C"] = "Call",
        ["B"] = "Bind (let)",
        ["R"] = "Return",
        ["I"] = "Input parameter",
        ["O"] = "Output",
        ["A"] = "Argument",
        ["E"] = "Effects",
        ["L"] = "Loop (for)",
        ["W"] = "Match (switch)",
        ["K"] = "Case",
        ["Q"] = "Requires (precondition)",
        ["S"] = "Ensures (postcondition)",
        ["U"] = "Using",

        // Control flow
        ["IF"] = "If",
        ["EI"] = "ElseIf",
        ["EL"] = "Else",
        ["WH"] = "While",
        ["DO"] = "Do-while",
        ["BK"] = "Break",
        ["CN"] = "Continue",

        // Types
        ["FLD"] = "Field",
        ["IV"] = "Invariant",

        // Collections
        ["ARR"] = "Array",
        ["LIST"] = "List",
        ["DICT"] = "Dictionary",
        ["NEW"] = "New object",

        // Classes
        ["CL"] = "Class",
        ["IFACE"] = "Interface",
        ["MT"] = "Method",
        ["CTOR"] = "Constructor",
        ["OP"] = "Operator overload",
        ["PROP"] = "Property",

        // Try/Catch
        ["TR"] = "Try",
        ["CA"] = "Catch",
        ["FI"] = "Finally",
        ["TH"] = "Throw",

        // Misc
        ["LAM"] = "Lambda",
        ["EN"] = "Enum",
        ["P"] = "Print (Console.WriteLine)",
    };

    /// <summary>
    /// Common expanded forms that should map to their short equivalents.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExpandedForms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["FUNC"] = "F",
        ["FUNCTION"] = "F",
        ["MOD"] = "M",
        ["MODULE"] = "M",
        ["CALL"] = "C",
        ["BIND"] = "B",
        ["LET"] = "B",
        ["RETURN"] = "R",
        ["RET"] = "R",
        ["INPUT"] = "I",
        ["OUTPUT"] = "O",
        ["ARG"] = "A",
        ["ARGUMENT"] = "A",
        ["EFFECTS"] = "E",
        ["LOOP"] = "L",
        ["FOREACH"] = "L",
        ["FOR"] = "L",
        ["SWITCH"] = "W",
        ["SW"] = "W",
        ["ENUM"] = "EN",
        ["FL"] = "FLD",
        ["WR"] = "WHERE",
        ["ADD"] = "PUSH",
        ["CASE"] = "K",
        ["REQUIRES"] = "Q",
        ["PRECONDITION"] = "Q",
        ["PRE"] = "Q",
        ["ENSURES"] = "S",
        ["POSTCONDITION"] = "S",
        ["POST"] = "S",
        ["TYPE"] = "T",
        ["RECORD"] = "D",
        ["DATA"] = "D",
        ["VARIANT"] = "V",
        ["USING"] = "U",
        // Closing tags
        ["/IF"] = "/I",
        ["/FUNC"] = "/F",
        ["/FUNCTION"] = "/F",
        ["/MOD"] = "/M",
        ["/MODULE"] = "/M",
        ["/CALL"] = "/C",
        ["/LOOP"] = "/L",
        ["OPERATOR"] = "OP",
        ["OVERLOAD"] = "OP",
    };

    /// <summary>
    /// Finds the most similar section marker using Levenshtein distance.
    /// Returns null if no sufficiently similar marker is found.
    /// </summary>
    /// <param name="unknown">The unknown marker text (without §).</param>
    /// <param name="maxDistance">Maximum edit distance to consider a match (default: 2).</param>
    /// <returns>The most similar marker, or null if none found within distance threshold.</returns>
    public static string? FindSimilarMarker(string unknown, int maxDistance = 2)
    {
        if (string.IsNullOrEmpty(unknown))
            return null;

        var upperUnknown = unknown.ToUpperInvariant();

        // First, check for common expanded forms
        if (ExpandedForms.TryGetValue(upperUnknown, out var expandedMatch))
            return AllMarkers.Contains(expandedMatch, StringComparer.Ordinal) ? expandedMatch : null;

        string? bestMatch = null;
        var bestDistance = int.MaxValue;
        var bestLength = int.MaxValue;

        foreach (var marker in AllMarkers)
        {
            var distance = LevenshteinDistance(upperUnknown, marker.ToUpperInvariant());
            if (distance <= maxDistance)
            {
                // Prefer matches with lower distance
                // When distances are equal, prefer shorter markers (they're more common)
                if (distance < bestDistance ||
                    (distance == bestDistance && marker.Length < bestLength))
                {
                    bestDistance = distance;
                    bestMatch = marker;
                    bestLength = marker.Length;
                }
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Gets a description of the most common section markers.
    /// </summary>
    public static string GetCommonMarkers()
    {
        return "§M (Module), §F (Function), §B (Bind), §C (Call), §IF, §L (Loop), §W (Match)";
    }

    /// <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// </summary>
    private static int LevenshteinDistance(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1))
            return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
        if (string.IsNullOrEmpty(s2))
            return s1.Length;

        var m = s1.Length;
        var n = s2.Length;

        // Use two rows instead of full matrix for memory efficiency
        var prev = new int[n + 1];
        var curr = new int[n + 1];

        // Initialize first row
        for (var j = 0; j <= n; j++)
            prev[j] = j;

        for (var i = 1; i <= m; i++)
        {
            curr[0] = i;

            for (var j = 1; j <= n; j++)
            {
                var cost = char.ToUpperInvariant(s1[i - 1]) == char.ToUpperInvariant(s2[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            // Swap rows
            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }
}
