using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 4: Edit Precision Calculator
/// Measures the ability to make targeted edits via unique IDs.
/// Calor's §F[id:name] enables precise edits without context ambiguity.
/// </summary>
public class EditPrecisionCalculator : IMetricCalculator
{
    public string Category => "EditPrecision";

    public string Description => "Measures targeting precision via unique IDs and structural markers";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var calorScore = CalculateCalorEditPrecision(context);
        var csharpScore = CalculateCSharpEditPrecision(context);

        var details = new Dictionary<string, object>
        {
            ["calorUniqueIds"] = CountUniqueIds(context.CalorSource),
            ["calorAddressableElements"] = CountAddressableElements(context.CalorSource),
            ["csharpNamedElements"] = CountNamedElements(context.CSharpSource),
            ["calorTargetingScore"] = calorScore,
            ["csharpTargetingScore"] = csharpScore
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "TargetingPrecision",
            calorScore,
            csharpScore,
            details));
    }

    private static double CalculateCalorEditPrecision(EvaluationContext context)
    {
        var source = context.CalorSource;
        var score = 0.0;

        // Count explicit IDs that enable precise targeting
        var idCount = CountUniqueIds(source);
        score += Math.Min(1.0, idCount * 0.15);

        // Function markers with IDs
        var functionIds = CountOccurrences(source, "§F[id:");
        score += Math.Min(0.3, functionIds * 0.1);

        // Variable markers
        var varMarkers = CountOccurrences(source, "§V[");
        score += Math.Min(0.2, varMarkers * 0.05);

        // Module markers
        var moduleMarkers = CountOccurrences(source, "§M[");
        score += Math.Min(0.2, moduleMarkers * 0.1);

        // Return markers (enable precise return targeting)
        var returnMarkers = CountOccurrences(source, "§RET");
        score += Math.Min(0.1, returnMarkers * 0.05);

        // Call markers (enable precise call site targeting)
        var callMarkers = CountOccurrences(source, "§C[");
        score += Math.Min(0.2, callMarkers * 0.05);

        return Math.Min(1.0, score);
    }

    private static double CalculateCSharpEditPrecision(EvaluationContext context)
    {
        var source = context.CSharpSource;
        var score = 0.0;

        // Named elements in C# require context to target
        var namedElements = CountNamedElements(source);

        // Base score from having named elements
        score += Math.Min(0.3, namedElements * 0.02);

        // Attributes can help with targeting
        var attributeCount = CountOccurrences(source, "[");
        score += Math.Min(0.1, attributeCount * 0.02);

        // XML comments with references can help
        var xmlRefs = CountOccurrences(source, "<see cref=");
        score += Math.Min(0.1, xmlRefs * 0.05);

        // Regions help organize for targeting
        var regions = CountOccurrences(source, "#region");
        score += Math.Min(0.1, regions * 0.05);

        // Partial classes enable splitting
        if (source.Contains("partial class") || source.Contains("partial struct"))
        {
            score += 0.1;
        }

        // Method overloads require more context to target correctly (penalty)
        var methodMatches = CountMethodDeclarations(source);
        var duplicateNames = FindDuplicateMethodNames(source);
        if (duplicateNames > 0)
        {
            score -= duplicateNames * 0.05;
        }

        return Math.Max(0, Math.Min(1.0, score));
    }

    private static int CountUniqueIds(string source)
    {
        var count = 0;
        var index = 0;
        var pattern = "id:";

        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static int CountAddressableElements(string source)
    {
        var count = 0;

        // Count all major addressable markers
        count += CountOccurrences(source, "§M[");
        count += CountOccurrences(source, "§F[");
        count += CountOccurrences(source, "§V[");
        count += CountOccurrences(source, "§C[");

        return count;
    }

    private static int CountNamedElements(string source)
    {
        var count = 0;

        // Count class/struct/interface declarations
        count += CountOccurrences(source, "class ");
        count += CountOccurrences(source, "struct ");
        count += CountOccurrences(source, "interface ");
        count += CountOccurrences(source, "enum ");

        // Count method declarations (simplified)
        count += CountMethodDeclarations(source);

        // Count property declarations
        count += CountOccurrences(source, " get;");
        count += CountOccurrences(source, " set;");

        return count;
    }

    private static int CountMethodDeclarations(string source)
    {
        // Simplified method counting - look for common patterns
        var count = 0;

        var lines = source.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if ((trimmed.Contains("public ") || trimmed.Contains("private ") ||
                 trimmed.Contains("protected ") || trimmed.Contains("internal ")) &&
                trimmed.Contains("(") && trimmed.Contains(")") &&
                !trimmed.Contains("=") && !trimmed.StartsWith("//"))
            {
                count++;
            }
        }

        return count;
    }

    private static int FindDuplicateMethodNames(string source)
    {
        var methodNames = new List<string>();
        var lines = source.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if ((trimmed.Contains("public ") || trimmed.Contains("private ") ||
                 trimmed.Contains("protected ") || trimmed.Contains("internal ")) &&
                trimmed.Contains("("))
            {
                // Extract method name (simplified)
                var parenIndex = trimmed.IndexOf('(');
                if (parenIndex > 0)
                {
                    var beforeParen = trimmed.Substring(0, parenIndex);
                    var parts = beforeParen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        var methodName = parts[^1];
                        methodNames.Add(methodName);
                    }
                }
            }
        }

        return methodNames.Count - methodNames.Distinct().Count();
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
