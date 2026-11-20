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

    /// <summary>
    /// Calculates C#'s edit precision from its structural patterns. Same rule as
    /// Calor.Evaluation's copy of this metric: a 0.5 base, plus presence bonuses for a namespace,
    /// a class and method declarations, less 0.05 for heavy brace nesting, clamped to 0.3-0.85.
    /// </summary>
    private static double CalculateCSharpEditPrecision(EvaluationContext context)
    {
        var score = 0.5; // Base score

        var source = context.CSharpSource;

        // C# relies on names and line numbers for targeting
        var hasNamespace = source.Contains("namespace");
        var hasClass = source.Contains("class");
        var hasMethods = MethodDeclaration.Matches(source).Count;

        if (hasNamespace) score += 0.10;
        if (hasClass) score += 0.10;
        if (hasMethods > 0) score += 0.15;

        // Braces can cause edit ambiguity
        var braceCount = source.Count(c => c == '{');
        if (braceCount > 5) score -= 0.05; // Nested braces reduce precision

        return Math.Max(0.3, Math.Min(score, 0.85)); // Cap at 0.85 for C#
    }

    private static readonly System.Text.RegularExpressions.Regex MethodDeclaration = new(
        @"\b(public|private|protected|internal)\s+(static\s+)?\w+\s+\w+\s*\(",
        System.Text.RegularExpressions.RegexOptions.Compiled);

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
