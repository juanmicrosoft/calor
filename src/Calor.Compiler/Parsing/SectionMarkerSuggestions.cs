namespace Calor.Compiler.Parsing;

/// <summary>
/// Provides suggestions for unknown section markers (§-prefixed keywords).
/// Includes typo correction using Levenshtein distance.
/// </summary>
public static class SectionMarkerSuggestions
{
    /// <summary>
    /// All valid section markers in Calor.
    /// </summary>
    public static readonly IReadOnlyList<string> AllMarkers = new[]
    {
        // Core section markers
        "M", "F", "C", "B", "R", "I", "O", "A", "E", "L", "W", "K", "Q", "S", "T", "D", "V", "U",

        // Closing tags
        "/M", "/F", "/C", "/I", "/L", "/W", "/K", "/T", "/D",

        // Control flow
        "IF", "EI", "EL", "WH", "/WH", "DO", "/DO", "SW", "/SW", "BK", "CN", "BODY", "END_BODY",

        // Option/Result types
        "SM", "NN", "OK", "ERR", "FL", "IV",

        // Arrays and collections
        "ARR", "LIST", "DICT", "SET", "TUPLE", "SPAN", "RANGE", "NEW", "/NEW", "ADD", "DEL", "IDX", "ITER",

        // Async/Threading
        "ASYNC", "/ASYNC", "AWAIT", "TASK", "LOCK", "/LOCK", "SEM", "ATOMIC",

        // Access modifiers
        "PUB", "PRIV", "INT", "PROT", "STAT", "RO", "CONST",

        // Generics
        "WR", "WHERE",

        // Classes and interfaces
        "CL", "/CL", "IFACE", "/IFACE", "IMPL", "EXT", "MT", "/MT", "VR", "OV", "AB", "SD",
        "THIS", "/THIS", "BASE", "CTOR", "/CTOR", "DTOR", "/DTOR", "PROP", "/PROP", "GET", "SET", "INIT",

        // Patterns
        "MATCH", "/MATCH", "ARM", "/ARM", "GUARD", "DEFAULT",

        // Try/Catch
        "TR", "/TR", "CA", "FI", "TH", "RT", "WHEN",

        // Lambdas and delegates
        "LAM", "/LAM", "DEL", "/DEL", "EVENT", "/EVENT", "FIRE", "SUBSCRIBE", "/SUBSCRIBE",

        // Documentation
        "DOC", "/DOC", "PARAM", "RETURNS", "THROWS", "SEE", "LINK", "NOTE", "WARN", "REF", "VER", "ID",

        // Assembly
        "ASM", "/ASM", "NAME", "DEPS", "/DEPS", "TESTS", "/TESTS", "SIG", "REST",

        // Enums
        "EN", "ENUM", "/EN", "/ENUM", "EEXT", "/EEXT",

        // Quick wins
        "EX", "TD", "FX", "HK",

        // Core features
        "US", "/US", "UB", "/UB", "AS",

        // Enhanced contracts
        "CX", "SN", "DP", "BR", "XP", "SB",

        // Future extensions
        "DC", "/DC", "CHOSEN", "REJECTED", "REASON", "CT", "/CT", "VS", "/VS", "HD", "/HD",
        "FC", "FILE", "PT", "LK", "AU", "TASK", "DATE",

        // Print aliases
        "P", "Pf",
    };

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
        ["T"] = "Type",
        ["D"] = "Record (data)",
        ["V"] = "Variant",
        ["U"] = "Using",

        // Control flow
        ["IF"] = "If",
        ["EI"] = "ElseIf",
        ["EL"] = "Else",
        ["WH"] = "While",
        ["DO"] = "Do-while",
        ["SW"] = "Switch/Match",
        ["BK"] = "Break",
        ["CN"] = "Continue",

        // Types
        ["SM"] = "Some (Option)",
        ["NN"] = "None (Option)",
        ["OK"] = "Ok (Result)",
        ["ERR"] = "Error (Result)",
        ["FL"] = "Field",
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
        ["PROP"] = "Property",

        // Try/Catch
        ["TR"] = "Try",
        ["CA"] = "Catch",
        ["FI"] = "Finally",
        ["TH"] = "Throw",

        // Misc
        ["LAM"] = "Lambda",
        ["DOC"] = "Documentation",
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
        ["/FUNC"] = "/F",
        ["/FUNCTION"] = "/F",
        ["/MOD"] = "/M",
        ["/MODULE"] = "/M",
        ["/CALL"] = "/C",
        ["/LOOP"] = "/L",
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
            return expandedMatch;

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
