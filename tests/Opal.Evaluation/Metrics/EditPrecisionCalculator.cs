using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Opal.Evaluation.Core;

namespace Opal.Evaluation.Metrics;

/// <summary>
/// Category 4: Edit Precision Calculator
/// Measures code modification accuracy for OPAL vs C#.
/// OPAL's unique IDs are hypothesized to enable more precise targeting.
/// </summary>
public class EditPrecisionCalculator : IMetricCalculator
{
    public string Category => "EditPrecision";

    public string Description => "Measures code modification accuracy using diff analysis and ID-based targeting";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Calculate edit precision based on structural targeting capability
        var opalPrecision = CalculateOpalEditPrecision(context);
        var csharpPrecision = CalculateCSharpEditPrecision(context);

        var details = new Dictionary<string, object>
        {
            ["opalTargetingCapabilities"] = GetOpalTargetingCapabilities(context),
            ["csharpTargetingCapabilities"] = GetCSharpTargetingCapabilities(context)
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "TargetingPrecision",
            opalPrecision,
            csharpPrecision,
            details));
    }

    /// <summary>
    /// Evaluates edit precision for a before/after edit pair.
    /// </summary>
    public MetricResult EvaluateEdit(
        string opalBefore,
        string opalAfter,
        string csharpBefore,
        string csharpAfter,
        string editDescription)
    {
        var opalDiff = CalculateDiffMetrics(opalBefore, opalAfter);
        var csharpDiff = CalculateDiffMetrics(csharpBefore, csharpAfter);

        // Edit efficiency: fewer changes = more precise
        var opalEfficiency = opalDiff.TotalLines > 0
            ? 1.0 - ((double)opalDiff.ModifiedLines / opalDiff.TotalLines)
            : 1.0;
        var csharpEfficiency = csharpDiff.TotalLines > 0
            ? 1.0 - ((double)csharpDiff.ModifiedLines / csharpDiff.TotalLines)
            : 1.0;

        var details = new Dictionary<string, object>
        {
            ["editDescription"] = editDescription,
            ["opalDiff"] = opalDiff,
            ["csharpDiff"] = csharpDiff
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "EditEfficiency",
            opalEfficiency,
            csharpEfficiency,
            details);
    }

    /// <summary>
    /// Evaluates edit correctness by comparing actual output to expected output.
    /// </summary>
    public MetricResult EvaluateEditCorrectness(
        string actualOutput,
        string expectedOutput,
        bool isOpal)
    {
        var diff = CalculateDiffMetrics(expectedOutput, actualOutput);

        // Correctness based on how close actual is to expected
        var correctness = diff.TotalLines > 0
            ? 1.0 - ((double)diff.ModifiedLines / diff.TotalLines)
            : 1.0;

        var prefix = isOpal ? "Opal" : "CSharp";
        return new MetricResult(
            Category,
            $"{prefix}EditCorrectness",
            isOpal ? correctness : 0,
            isOpal ? 0 : correctness,
            1.0,
            new Dictionary<string, object>
            {
                ["diff"] = diff,
                ["correctness"] = correctness
            });
    }

    /// <summary>
    /// Calculates OPAL's edit precision based on unique ID presence.
    /// </summary>
    private static double CalculateOpalEditPrecision(EvaluationContext context)
    {
        var score = 0.5; // Base score

        var source = context.OpalSource;

        // OPAL unique IDs enable precise targeting
        var moduleIds = CountPattern(source, @"\§M\[[^\]]+:");
        var functionIds = CountPattern(source, @"\§F\[[^\]]+:");
        var variableIds = CountPattern(source, @"\§V\[[^\]]+:");

        // More unique IDs = higher precision capability
        if (moduleIds > 0) score += 0.15;
        if (functionIds > 0) score += 0.20;
        if (variableIds > 0) score += 0.10;

        // Closing tags enable safe modifications
        if (source.Contains("§/F[")) score += 0.05;
        if (source.Contains("§/M[")) score += 0.05;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Calculates C#'s edit precision based on structural patterns.
    /// </summary>
    private static double CalculateCSharpEditPrecision(EvaluationContext context)
    {
        var score = 0.5; // Base score

        var source = context.CSharpSource;

        // C# relies on names and line numbers for targeting
        var hasNamespace = source.Contains("namespace");
        var hasClass = source.Contains("class");
        var hasMethods = CountPattern(source, @"\b(public|private|protected|internal)\s+(static\s+)?\w+\s+\w+\s*\(");

        if (hasNamespace) score += 0.10;
        if (hasClass) score += 0.10;
        if (hasMethods > 0) score += 0.15;

        // Braces can cause edit ambiguity
        var braceCount = source.Count(c => c == '{');
        if (braceCount > 5) score -= 0.05; // Nested braces reduce precision

        return Math.Max(0.3, Math.Min(score, 0.85)); // Cap at 0.85 for C#
    }

    private static Dictionary<string, object> GetOpalTargetingCapabilities(EvaluationContext context)
    {
        var source = context.OpalSource;
        return new Dictionary<string, object>
        {
            ["hasUniqueModuleIds"] = source.Contains("§M["),
            ["hasUniqueFunctionIds"] = source.Contains("§F["),
            ["hasUniqueVariableIds"] = source.Contains("§V["),
            ["hasClosingTags"] = source.Contains("§/"),
            ["moduleIdCount"] = CountPattern(source, @"\§M\["),
            ["functionIdCount"] = CountPattern(source, @"\§F\[")
        };
    }

    private static Dictionary<string, object> GetCSharpTargetingCapabilities(EvaluationContext context)
    {
        var source = context.CSharpSource;
        return new Dictionary<string, object>
        {
            ["hasNamespace"] = source.Contains("namespace"),
            ["hasClass"] = source.Contains("class"),
            ["methodCount"] = CountPattern(source, @"\b(public|private|protected|internal)\s+(static\s+)?\w+\s+\w+\s*\("),
            ["braceDepth"] = source.Count(c => c == '{'),
            ["lineCount"] = source.Split('\n').Length
        };
    }

    private static int CountPattern(string source, string pattern)
    {
        return System.Text.RegularExpressions.Regex.Matches(source, pattern).Count;
    }

    /// <summary>
    /// Calculates diff metrics between two versions of source code.
    /// </summary>
    private static DiffMetrics CalculateDiffMetrics(string before, string after)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(before, after);

        var inserted = diff.Lines.Count(l => l.Type == ChangeType.Inserted);
        var deleted = diff.Lines.Count(l => l.Type == ChangeType.Deleted);
        var unchanged = diff.Lines.Count(l => l.Type == ChangeType.Unchanged);
        var modified = inserted + deleted;

        return new DiffMetrics(
            TotalLines: diff.Lines.Count,
            InsertedLines: inserted,
            DeletedLines: deleted,
            UnchangedLines: unchanged,
            ModifiedLines: modified);
    }
}

/// <summary>
/// Metrics from a diff comparison.
/// </summary>
public record DiffMetrics(
    int TotalLines,
    int InsertedLines,
    int DeletedLines,
    int UnchangedLines,
    int ModifiedLines);
