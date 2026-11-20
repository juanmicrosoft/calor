using Calor.Compiler.Evaluation.Core;
using Calor.Compiler.Ids;

namespace Calor.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 4: Edit Precision Calculator
/// Measures the ability to make targeted edits via unique IDs.
/// Calor gives every declaration a stable ID (kept in the <c>.calor/ids.json</c> sidecar in native
/// source), which enables precise edits without context ambiguity.
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

    /// <summary>
    /// Calculates Calor's edit precision from the presence of declaration IDs. Same rule as
    /// Calor.Evaluation's copy of this metric: a 0.5 base, plus presence bonuses for a module ID,
    /// a callable ID, and indented body lines. Local bindings, returns and call sites have no IDs
    /// in native Calor, so they earn nothing.
    /// </summary>
    private static double CalculateCalorEditPrecision(EvaluationContext context)
    {
        var score = 0.5; // Base score

        var source = context.CalorSource;
        var declarations = ReadDeclarations(source);

        // Sidecar IDs enable precise targeting.
        if (declarations.Any(d => d.Kind == IdKind.Module)) score += 0.15;
        if (declarations.Any(d => IsCallable(d.Kind))) score += 0.20;

        // Each opener paired with an indented body marks a safe-modification scope.
        if (IndentedBodyLine.IsMatch(source)) score += 0.10;

        return Math.Min(score, 1.0);
    }

    private static readonly System.Text.RegularExpressions.Regex IndentedBodyLine = new(
        @"^[ \t]+\S",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);

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

    private static int CountUniqueIds(string source) => ReadDeclarations(source).Count;

    private static int CountAddressableElements(string source) =>
        ReadDeclarations(source).Count(d => d.Kind == IdKind.Module || IsCallable(d.Kind));

    private static bool IsCallable(IdKind kind) =>
        kind is IdKind.Function or IdKind.Method or IdKind.Constructor or IdKind.OperatorOverload;

    /// <summary>
    /// The declarations the identity store gives IDs to (what <c>calor ids sync</c> would record
    /// for this source). Source that does not parse and bind has none, as the store cannot
    /// assign IDs to it.
    /// </summary>
    private static IReadOnlyList<DeclarationIdentity> ReadDeclarations(string source)
    {
        try
        {
            return DeclarationIdentityStore.Describe("input.calr", source);
        }
        catch (DeclarationIdentityParseException)
        {
            return [];
        }
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
