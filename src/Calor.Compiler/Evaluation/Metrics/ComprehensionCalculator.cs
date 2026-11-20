using System.Text.RegularExpressions;
using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 3: Comprehension Calculator
/// Measures structural clarity and explicit markers visibility.
/// Calor's explicit declarations (<c>module</c>, <c>func</c>, <c>requires</c>) should aid comprehension.
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

        // Explicit structure contributes to clarity. Each entry is the native (0.30) spelling of
        // a construct the legacy scorer found by its section marker. Inline IDs (`id:`) are gone:
        // native source keeps declaration IDs in the .calor/ids.json sidecar, so a reader of the
        // source no longer sees them, and the term is dropped (as in Calor.Evaluation's copy).
        foreach (var (_, count, weight) in ExplicitMarkers)
        {
            if (count(source) > 0)
            {
                score += weight;
            }
        }

        // Normalize to 0-1 range
        var maxPossible = ExplicitMarkers.Sum(m => m.Weight);
        return Math.Min(1.0, score / maxPossible);
    }

    private static readonly (string Name, Func<string, int> Count, double Weight)[] ExplicitMarkers =
    [
        ("module", s => NativeCalorSyntax.Module.Matches(s).Count, 1.0),          // Module declaration
        ("function", s => NativeCalorSyntax.Function.Matches(s).Count, 1.0),      // Function declaration
        ("variable", s => NativeCalorSyntax.Binding.Matches(s).Count, 0.5),       // let/var binding
        ("input", NativeCalorSyntax.CountTypedParameters, 0.5),                   // Typed parameter (T:x)
        ("output", s => NativeCalorSyntax.TypedOutput.Matches(s).Count, 0.5),     // Return type (→ T)
        ("requires", s => NativeCalorSyntax.Requires.Matches(s).Count, 1.0),      // Precondition
        ("ensures", s => NativeCalorSyntax.Ensures.Matches(s).Count, 1.0),        // Postcondition (=> expr)
        ("invariant", s => NativeCalorSyntax.Invariant.Matches(s).Count, 1.0),    // Invariant
        ("effects", s => NativeCalorSyntax.EffectRow.Matches(s).Count, 0.8),      // Effect row (! ...)
    ];

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
        return ExplicitMarkers
            .Where(m => m.Name is not ("input" or "output"))
            .ToDictionary(m => m.Name, m => m.Count(source));
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
}

/// <summary>
/// Source patterns for native (0.30) Calor constructs, shared by the metric calculators in this
/// folder. Each pattern is the native spelling of a construct the calculators used to find by its
/// retired section marker: <c>module</c>, <c>func</c>, <c>requires</c> for a precondition, a
/// line-head <c>=&gt;</c> for a postcondition, a line-head <c>!</c> for an effect row, and so on.
/// Mirrors <c>tests/Calor.Evaluation/Metrics/CalorSyntax.cs</c>, so both copies of the metrics
/// count the same things.
/// </summary>
internal static class NativeCalorSyntax
{
    private const RegexOptions Line = RegexOptions.Compiled | RegexOptions.Multiline;

    public static readonly Regex Module = new(@"^[ \t]*module[ \t]+\S", Line);
    public static readonly Regex Function = new(@"^[ \t]*func[ \t]+\S", Line);
    /// <summary>A function header that declares a return type (<c>→ T</c>).</summary>
    public static readonly Regex TypedOutput = new(@"^[ \t]*func[ \t][^\n]*→", Line);
    public static readonly Regex Return = new(@"^[ \t]*return\b", Line);
    public static readonly Regex Requires = new(@"^[ \t]*requires\b", Line);
    public static readonly Regex Ensures = new(@"^[ \t]*=>[ \t]", Line);
    public static readonly Regex Invariant = new(@"^[ \t]*invariant\b", Line);
    /// <summary>An effect row; a bare <c>!</c> is the pure row.</summary>
    public static readonly Regex EffectRow = new(@"^[ \t]*!(?=[ \t]|\r?$)", Line);
    public static readonly Regex Binding = new(@"^[ \t]*(?:let|var)[ \t]", Line);
    public static readonly Regex MutableBinding = new(@"^[ \t]*var[ \t]", Line);
    public static readonly Regex If = new(@"^[ \t]*if\b", Line);
    public static readonly Regex ElseIf = new(@"^[ \t]*else[ \t]+if\b", Line);
    public static readonly Regex Else = new(@"^[ \t]*else[ \t]*\r?$", Line);
    public static readonly Regex Loop = new(@"^[ \t]*(?:for|each|while|do)\b", Line);
    public static readonly Regex Match = new(@"^[ \t]*match\b", Line);
    /// <summary>A call: a (possibly dotted) name followed by <c>(</c>, excluding keywords.</summary>
    public static readonly Regex Call = new(
        @"(?<![\w.])(?!(?:if|while|for|each|do|match|requires|return|func|module|invariant|let|var|set)\b)[A-Za-z_][\w.]*\(",
        RegexOptions.Compiled);
    /// <summary>Built-in type names, counted as type annotations.</summary>
    public static readonly Regex TypeName = new(@"\b(int|long|float|double|string|bool|void)\b", RegexOptions.Compiled);

    private static readonly Regex FunctionParameters = new(@"^[ \t]*func[ \t][^\n(]*\(([^\n]*)\)", Line);
    private static readonly Regex TypedParameter = new(@"(?<=[\w>\]?)]):~?[A-Za-z_]\w*", RegexOptions.Compiled);

    /// <summary>Typed parameters (<c>T:name</c>) across every function header.</summary>
    public static int CountTypedParameters(string source) =>
        FunctionParameters.Matches(source).Sum(header => TypedParameter.Matches(header.Groups[1].Value).Count);

    public static bool HasTypedParameters(string source) => CountTypedParameters(source) > 0;
}
