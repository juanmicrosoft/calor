namespace Opal.Evaluation.Core;

/// <summary>
/// Base interface for all metric calculators in the evaluation framework.
/// Each calculator measures a specific aspect of OPAL vs C# code effectiveness.
/// </summary>
public interface IMetricCalculator
{
    /// <summary>
    /// The evaluation category this calculator measures.
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Human-readable description of what this calculator measures.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Calculates metrics for the given evaluation context.
    /// </summary>
    /// <param name="context">The evaluation context containing OPAL and C# source code.</param>
    /// <returns>A metric result containing scores and comparison data.</returns>
    Task<MetricResult> CalculateAsync(EvaluationContext context);
}
