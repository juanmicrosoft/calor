using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 3: Comprehension Calculator
/// Measures structural clarity and explicit markers visibility.
/// Calor's explicit structure markers (§M, §F, §REQ) should aid comprehension.
/// </summary>
public class ComprehensionCalculator : IMetricCalculator
{
    public string Category => "Comprehension";

    public string Description => "Measures structural clarity and explicit markers for code understanding";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var calorScore = CalculateCalorComprehensionScore(context);
        var csharpScore = CalculateCSharpComprehensionScore(context);

        var details = new Dictionary<string, object>
        {
            ["calorExplicitMarkers"] = CountExplicitMarkers(context.CalorSource),
            ["csharpCommentDensity"] = CalculateCommentDensity(context.CSharpSource),
            ["calorStructureDepth"] = CalculateStructureDepth(context.CalorSource),
            ["csharpStructureDepth"] = CalculateStructureDepth(context.CSharpSource)
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "StructuralClarity",
            calorScore,
            csharpScore,
            details));
    }

    private static double CalculateCalorComprehensionScore(EvaluationContext context)
    {
        var source = context.CalorSource;
        var score = 0.0;

        // Explicit structure markers contribute to clarity
        var markers = new[]
        {
            ("§M[", 1.0),   // Module marker
            ("§F[", 1.0),   // Function marker
            ("§V[", 0.5),   // Variable marker
            ("§I[", 0.5),   // Input parameter
            ("§O[", 0.5),   // Output/return type
            ("§REQ", 1.0),  // Requires (precondition)
            ("§ENS", 1.0),  // Ensures (postcondition)
            ("§INV", 1.0),  // Invariant
            ("§E[", 0.8),   // Effects
            ("id:", 0.5)    // Explicit IDs for targeting
        };

        foreach (var (marker, weight) in markers)
        {
            if (source.Contains(marker))
            {
                score += weight;
            }
        }

        // Normalize to 0-1 range
        var maxPossible = markers.Sum(m => m.Item2);
        return Math.Min(1.0, score / maxPossible);
    }

    private static double CalculateCSharpComprehensionScore(EvaluationContext context)
    {
        var source = context.CSharpSource;
        var score = 0.0;

        // Comments contribute to comprehension
        var commentDensity = CalculateCommentDensity(source);
        score += commentDensity * 0.3;

        // XML documentation
        if (source.Contains("///") || source.Contains("/** "))
        {
            score += 0.2;
        }

        // Meaningful type annotations
        if (source.Contains("public") || source.Contains("private"))
        {
            score += 0.1;
        }

        // Named parameters usage
        if (source.Contains(": ") && (source.Contains("string ") || source.Contains("int ")))
        {
            score += 0.1;
        }

        // Attributes (metadata)
        if (source.Contains("[") && source.Contains("]"))
        {
            score += 0.1;
        }

        // Clear method signatures
        if (context.CSharpCompilation.Success && context.CSharpCompilation.Root != null)
        {
            score += 0.2;
        }

        return Math.Min(1.0, score);
    }

    private static Dictionary<string, int> CountExplicitMarkers(string source)
    {
        var markers = new Dictionary<string, int>
        {
            ["module"] = CountOccurrences(source, "§M["),
            ["function"] = CountOccurrences(source, "§F["),
            ["variable"] = CountOccurrences(source, "§V["),
            ["requires"] = CountOccurrences(source, "§REQ"),
            ["ensures"] = CountOccurrences(source, "§ENS"),
            ["invariant"] = CountOccurrences(source, "§INV"),
            ["effects"] = CountOccurrences(source, "§E["),
            ["ids"] = CountOccurrences(source, "id:")
        };
        return markers;
    }

    private static double CalculateCommentDensity(string source)
    {
        var lines = source.Split('\n');
        var commentLines = lines.Count(l =>
            l.Trim().StartsWith("//") ||
            l.Trim().StartsWith("/*") ||
            l.Trim().StartsWith("*") ||
            l.Trim().StartsWith("///"));

        return lines.Length > 0 ? (double)commentLines / lines.Length : 0;
    }

    private static int CalculateStructureDepth(string source)
    {
        var maxDepth = 0;
        var currentDepth = 0;

        foreach (var ch in source)
        {
            if (ch == '{')
            {
                currentDepth++;
                maxDepth = Math.Max(maxDepth, currentDepth);
            }
            else if (ch == '}')
            {
                currentDepth--;
            }
        }

        return maxDepth;
    }

    private static int CountOccurrences(string source, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
