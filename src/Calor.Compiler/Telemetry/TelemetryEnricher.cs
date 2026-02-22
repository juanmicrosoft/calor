using System.Text.RegularExpressions;

namespace Calor.Compiler.Telemetry;

/// <summary>
/// Enriches telemetry events with input profile and diagnostic co-occurrence data.
/// All analysis is metadata-only — no PII or source code is captured.
/// </summary>
public static class TelemetryEnricher
{
    /// <summary>
    /// Analyzes source input to produce a metadata-only profile.
    /// No source code or PII is stored.
    /// </summary>
    public static InputProfile AnalyzeInput(string source)
    {
        var lines = source.Split('\n', StringSplitOptions.None);
        // Don't count trailing empty line from final newline
        var lineCount = lines.Length;
        if (lineCount > 0 && string.IsNullOrEmpty(lines[^1]))
            lineCount--;

        // Rough token estimate: ~4 chars per token (common heuristic)
        var estimatedTokens = source.Length / 4;

        var hasContracts = Regex.IsMatch(source, @"\b(requires|ensures|invariant|contract)\b", RegexOptions.IgnoreCase);
        var hasEffects = Regex.IsMatch(source, @"\b(effect|performs|IO|FileSystem|Network)\b");
        var hasModules = Regex.IsMatch(source, @"\bmodule\b", RegexOptions.IgnoreCase);

        var sizeCategory = lineCount switch
        {
            <= 50 => "small",
            <= 200 => "medium",
            <= 500 => "large",
            _ => "xlarge"
        };

        return new InputProfile
        {
            LineCount = lineCount,
            EstimatedTokenCount = estimatedTokens,
            HasContracts = hasContracts,
            HasEffects = hasEffects,
            HasModules = hasModules,
            SizeCategory = sizeCategory
        };
    }

    /// <summary>
    /// Analyzes diagnostic codes for co-occurrence patterns within a single compilation.
    /// Pair weight = min(countA, countB), reflecting how many times both codes appeared together.
    /// Returns top 10 pairs by weight.
    /// </summary>
    public static Dictionary<string, int> AnalyzeCoOccurrence(IReadOnlyList<string> diagnosticCodes)
    {
        // Count occurrences of each code
        var codeCounts = new Dictionary<string, int>();
        foreach (var code in diagnosticCodes)
        {
            codeCounts[code] = codeCounts.GetValueOrDefault(code) + 1;
        }

        // Build weighted pairs: weight = min(countA, countB)
        var sortedCodes = codeCounts.Keys.OrderBy(c => c).ToList();
        var pairs = new Dictionary<string, int>();

        for (int i = 0; i < sortedCodes.Count; i++)
        {
            for (int j = i + 1; j < sortedCodes.Count; j++)
            {
                var key = $"{sortedCodes[i]}+{sortedCodes[j]}";
                pairs[key] = Math.Min(codeCounts[sortedCodes[i]], codeCounts[sortedCodes[j]]);
            }
        }

        return pairs
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}

/// <summary>
/// Metadata-only profile of a source input. Contains no PII or source code.
/// </summary>
public sealed class InputProfile
{
    public int LineCount { get; init; }
    public int EstimatedTokenCount { get; init; }
    public bool HasContracts { get; init; }
    public bool HasEffects { get; init; }
    public bool HasModules { get; init; }
    public string SizeCategory { get; init; } = "small";
}
