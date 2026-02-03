using System.Text.Json.Serialization;

namespace Calor.Evaluation.Core;

/// <summary>
/// Unified result structure containing all evaluation metrics across categories.
/// </summary>
public class EvaluationResult
{
    /// <summary>
    /// Timestamp when the evaluation was run.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Version of the evaluation framework.
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// Number of benchmark cases evaluated.
    /// </summary>
    public int BenchmarkCount { get; init; }

    /// <summary>
    /// Individual metric results from all calculators.
    /// </summary>
    public List<MetricResult> Metrics { get; init; } = new();

    /// <summary>
    /// Summary statistics aggregated by category.
    /// </summary>
    public EvaluationSummary Summary { get; set; } = new();

    /// <summary>
    /// Detailed results per benchmark case.
    /// </summary>
    public List<BenchmarkCaseResult> CaseResults { get; init; } = new();

    /// <summary>
    /// Calculates and returns the overall Calor advantage ratio (geometric mean of all category ratios).
    /// </summary>
    public double CalculateOverallAdvantage()
    {
        if (Summary.CategoryAdvantages.Count == 0)
            return 1.0;

        var product = Summary.CategoryAdvantages.Values
            .Where(v => v > 0)
            .Aggregate(1.0, (acc, v) => acc * v);

        return Math.Pow(product, 1.0 / Summary.CategoryAdvantages.Count);
    }
}

/// <summary>
/// Summary statistics for the evaluation.
/// </summary>
public class EvaluationSummary
{
    /// <summary>
    /// Overall Calor advantage ratio across all categories.
    /// </summary>
    public double OverallCalorAdvantage { get; set; }

    /// <summary>
    /// Advantage ratios by category.
    /// </summary>
    public Dictionary<string, double> CategoryAdvantages { get; set; } = new();

    /// <summary>
    /// Total benchmarks that passed for Calor.
    /// </summary>
    public int CalorPassCount { get; set; }

    /// <summary>
    /// Total benchmarks that passed for C#.
    /// </summary>
    public int CSharpPassCount { get; set; }

    /// <summary>
    /// Categories where Calor has the largest advantage.
    /// </summary>
    public List<string> TopCalorCategories { get; set; } = new();

    /// <summary>
    /// Categories where C# has an advantage (if any).
    /// </summary>
    public List<string> CSharpAdvantageCategories { get; set; } = new();
}

/// <summary>
/// Detailed results for a single benchmark case.
/// </summary>
public class BenchmarkCaseResult
{
    /// <summary>
    /// Identifier for this benchmark case.
    /// </summary>
    public required string CaseId { get; init; }

    /// <summary>
    /// File name or description.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Complexity level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Features tested by this case.
    /// </summary>
    public List<string> Features { get; init; } = new();

    /// <summary>
    /// All metric results for this case.
    /// </summary>
    public List<MetricResult> Metrics { get; init; } = new();

    /// <summary>
    /// Whether Calor compilation succeeded.
    /// </summary>
    public bool CalorSuccess { get; init; }

    /// <summary>
    /// Whether C# compilation succeeded.
    /// </summary>
    public bool CSharpSuccess { get; init; }

    /// <summary>
    /// Average Calor advantage ratio for this case.
    /// </summary>
    [JsonIgnore]
    public double AverageAdvantage => Metrics.Count > 0
        ? Metrics.Average(m => m.AdvantageRatio)
        : 1.0;
}
