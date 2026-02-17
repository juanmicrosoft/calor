using Calor.Evaluation.Core;
using Calor.Evaluation.LlmTasks;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Providers;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Calculates safety metrics by measuring contract enforcement quality.
/// This wraps the SafetyBenchmarkRunner to produce MetricResult compatible
/// with the main benchmark dashboard.
///
/// Unlike static metrics, this uses LLM-generated code to test whether
/// Calor contracts catch more bugs with better error messages than C# guard clauses.
///
/// IMPORTANT: This metric requires an LLM provider. It cannot be used
/// without configuring a provider (e.g., Claude API).
/// </summary>
public class SafetyCalculator : IMetricCalculator
{
    public string Category => "Safety";

    public string Description =>
        "Measures contract enforcement effectiveness and error quality for catching bugs";

    private readonly ILlmProvider _provider;
    private readonly LlmResponseCache? _cache;
    private readonly LlmTaskManifest _manifest;
    private readonly SafetyBenchmarkOptions _options;
    private SafetyBenchmarkResults? _lastResults;

    /// <summary>
    /// Creates a calculator with specified provider and manifest.
    /// </summary>
    /// <param name="provider">The LLM provider to use for code generation.</param>
    /// <param name="manifest">The task manifest containing safety task definitions.</param>
    /// <param name="options">Optional benchmark configuration.</param>
    /// <param name="cache">Optional response cache for reducing API costs.</param>
    public SafetyCalculator(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        SafetyBenchmarkOptions? options = null,
        LlmResponseCache? cache = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _cache = cache;
        _options = options ?? new SafetyBenchmarkOptions();
    }

    /// <summary>
    /// Gets the results from the last calculation.
    /// </summary>
    public SafetyBenchmarkResults? LastResults => _lastResults;

    public async Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Run actual safety benchmark
        using var runner = new SafetyBenchmarkRunner(_provider, _cache);
        _lastResults = await runner.RunAllAsync(_manifest, _options);

        var summary = _lastResults.Summary;

        var details = new Dictionary<string, object>
        {
            ["totalTasks"] = summary.TotalTasks,
            ["calorWins"] = summary.CalorWins,
            ["csharpWins"] = summary.CSharpWins,
            ["ties"] = summary.Ties,
            ["calorViolationDetectionRate"] = summary.CalorViolationDetectionRate,
            ["csharpViolationDetectionRate"] = summary.CSharpViolationDetectionRate,
            ["calorErrorQuality"] = summary.CalorAverageErrorQuality,
            ["csharpErrorQuality"] = summary.CSharpAverageErrorQuality,
            ["provider"] = _lastResults.Provider ?? "unknown",
            ["isDryRun"] = _options.DryRun,
            ["byCategory"] = summary.ByCategory
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "SafetyScore",
            summary.AverageCalorSafetyScore,
            summary.AverageCSharpSafetyScore,
            details);
    }

    /// <summary>
    /// Creates a calculator configured for LLM-based safety evaluation.
    /// </summary>
    public static SafetyCalculator CreateWithProvider(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        SafetyBenchmarkOptions? options = null)
    {
        return new SafetyCalculator(provider, manifest, options);
    }

    /// <summary>
    /// Creates a calculator using the Claude API (requires ANTHROPIC_API_KEY).
    /// </summary>
    public static SafetyCalculator CreateWithClaude(
        LlmTaskManifest manifest,
        SafetyBenchmarkOptions? options = null)
    {
        var provider = new ClaudeProvider();
        if (!provider.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Claude provider unavailable: {provider.UnavailabilityReason}");
        }

        return new SafetyCalculator(provider, manifest, options);
    }
}
