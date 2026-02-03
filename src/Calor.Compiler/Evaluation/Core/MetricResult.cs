namespace Calor.Compiler.Evaluation.Core;

/// <summary>
/// Represents the result of a single metric calculation comparing Calor to C#.
/// </summary>
/// <param name="Category">The evaluation category (e.g., "TokenEconomics", "GenerationAccuracy").</param>
/// <param name="MetricName">Specific metric name within the category (e.g., "TokenCount", "CharacterCount").</param>
/// <param name="CalorScore">The raw score for Calor (interpretation depends on metric).</param>
/// <param name="CSharpScore">The raw score for C# (interpretation depends on metric).</param>
/// <param name="AdvantageRatio">Ratio indicating Calor advantage. >1 means Calor is better, &lt;1 means C# is better.</param>
/// <param name="Details">Additional metric-specific details for debugging and analysis.</param>
public record MetricResult(
    string Category,
    string MetricName,
    double CalorScore,
    double CSharpScore,
    double AdvantageRatio,
    Dictionary<string, object> Details)
{
    /// <summary>
    /// Creates a metric result where lower values are better (e.g., token count).
    /// Advantage ratio is calculated as CSharpScore / CalorScore.
    /// </summary>
    public static MetricResult CreateLowerIsBetter(
        string category,
        string metricName,
        double calorScore,
        double csharpScore,
        Dictionary<string, object>? details = null)
    {
        var ratio = calorScore > 0 ? csharpScore / calorScore : 1.0;
        return new MetricResult(category, metricName, calorScore, csharpScore, ratio, details ?? new());
    }

    /// <summary>
    /// Creates a metric result where higher values are better (e.g., accuracy).
    /// Advantage ratio is calculated as CalorScore / CSharpScore.
    /// </summary>
    public static MetricResult CreateHigherIsBetter(
        string category,
        string metricName,
        double calorScore,
        double csharpScore,
        Dictionary<string, object>? details = null)
    {
        var ratio = csharpScore > 0 ? calorScore / csharpScore : 1.0;
        return new MetricResult(category, metricName, calorScore, csharpScore, ratio, details ?? new());
    }

    /// <summary>
    /// Returns true if Calor has an advantage (ratio > 1).
    /// </summary>
    public bool CalorHasAdvantage => AdvantageRatio > 1.0;

    /// <summary>
    /// Returns the percentage advantage for Calor (positive) or C# (negative).
    /// </summary>
    public double AdvantagePercentage => (AdvantageRatio - 1.0) * 100;
}
