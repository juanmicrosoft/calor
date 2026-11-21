namespace Calor.Compiler.Parsing;

/// <summary>
/// Provides diagnostic suggestions for canonical syntax words.
/// Includes typo correction using Levenshtein distance.
/// </summary>
public static class SectionMarkerSuggestions
{
    /// <summary>
    /// Registered section markers eligible for canonical-syntax suggestions.
    /// </summary>
    public static readonly IReadOnlyList<string> AllMarkers = Lexer.KeywordNames.ToArray();

    /// <summary>
    /// Common expansions/descriptions for section markers.
    /// Used to help users understand what each marker means.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> MarkerDescriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["module"] = "Module",
        ["func"] = "Function",
        ["let"] = "Immutable binding",
        ["var"] = "Mutable binding",
        ["return"] = "Return",
        ["set"] = "Assignment statement",
        ["if"] = "Conditional branch",
        ["else"] = "Alternative branch",
        ["for"] = "For loop",
        ["each"] = "Iteration",
        ["while"] = "While condition",
        ["goto"] = "Jump to a label or match case",
        ["yield"] = "Iterator value or termination",
        ["do"] = "Do-while loop",
        ["match"] = "Pattern match",
        ["case"] = "Match case",
        ["requires"] = "Precondition",
        ["invariant"] = "Invariant",
        ["class"] = "Class",
        ["record"] = "Record",
        ["interface"] = "Interface",
        ["ctor"] = "Constructor",
        ["property"] = "Property",
        ["op"] = "Operator overload",
        ["pp"] = "Preprocessor conditional",
        ["try"] = "Try",
        ["catch"] = "Catch",
        ["finally"] = "Finally",
        ["throw"] = "Throw",
        ["enum"] = "Enum",
        ["new"] = "Object construction",
        ["raw"] = "Exact UTF-8 byte-counted C# payload",
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
        if (string.IsNullOrEmpty(unknown) || unknown.StartsWith('/') || unknown.StartsWith('§'))
            return null;

        string? bestMatch = null;
        var bestDistance = int.MaxValue;
        var bestLength = int.MaxValue;

        foreach (var marker in AllMarkers)
        {
            var distance = LevenshteinDistance(unknown, marker);
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
        return "module, func, let, var, if, for, each, match";
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
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
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
