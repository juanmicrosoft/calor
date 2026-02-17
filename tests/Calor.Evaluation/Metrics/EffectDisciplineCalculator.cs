using Calor.Evaluation.Core;
using Calor.Evaluation.LlmTasks;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Providers;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Calculates effect discipline metrics by measuring side effect management quality.
/// This wraps the EffectDisciplineBenchmarkRunner to produce MetricResult compatible
/// with the main benchmark dashboard.
///
/// This measures how well code prevents real-world bugs caused by hidden side effects:
/// - Flaky tests (non-determinism)
/// - Security violations (unauthorized I/O)
/// - Side effect transparency (hidden effects)
/// - Cache safety (memoization correctness)
///
/// IMPORTANT: This metric requires an LLM provider. It cannot be used
/// without configuring a provider (e.g., Claude API).
/// </summary>
public class EffectDisciplineCalculator : IMetricCalculator
{
    public string Category => "EffectDiscipline";

    public string Description =>
        "Measures side effect management quality and bug prevention for effect-related issues";

    private readonly ILlmProvider _provider;
    private readonly LlmResponseCache? _cache;
    private readonly LlmTaskManifest _manifest;
    private readonly EffectDisciplineOptions _options;
    private EffectDisciplineResults? _lastResults;

    /// <summary>
    /// Creates a calculator with specified provider and manifest.
    /// </summary>
    /// <param name="provider">The LLM provider to use for code generation.</param>
    /// <param name="manifest">The task manifest containing effect discipline task definitions.</param>
    /// <param name="options">Optional benchmark configuration.</param>
    /// <param name="cache">Optional response cache for reducing API costs.</param>
    public EffectDisciplineCalculator(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        EffectDisciplineOptions? options = null,
        LlmResponseCache? cache = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _cache = cache;
        _options = options ?? new EffectDisciplineOptions();
    }

    /// <summary>
    /// Gets the results from the last calculation.
    /// </summary>
    public EffectDisciplineResults? LastResults => _lastResults;

    public async Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Run actual effect discipline benchmark
        using var runner = new EffectDisciplineBenchmarkRunner(_provider, _cache);
        _lastResults = await runner.RunAllAsync(_manifest, _options);

        var summary = _lastResults.Summary;

        var details = new Dictionary<string, object>
        {
            ["totalTasks"] = summary.TotalTasks,
            ["calorWins"] = summary.CalorWins,
            ["csharpWins"] = summary.CSharpWins,
            ["ties"] = summary.Ties,
            ["calorBugPreventionRate"] = summary.CalorBugPreventionRate,
            ["csharpBugPreventionRate"] = summary.CSharpBugPreventionRate,
            ["calorCorrectnessRate"] = summary.CalorCorrectnessRate,
            ["csharpCorrectnessRate"] = summary.CSharpCorrectnessRate,
            ["provider"] = _lastResults.Provider ?? "unknown",
            ["isDryRun"] = _options.DryRun,
            ["byCategory"] = summary.ByCategory
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "DisciplineScore",
            summary.AverageCalorDisciplineScore,
            summary.AverageCSharpDisciplineScore,
            details);
    }

    /// <summary>
    /// Creates a calculator configured for LLM-based effect discipline evaluation.
    /// </summary>
    public static EffectDisciplineCalculator CreateWithProvider(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        EffectDisciplineOptions? options = null)
    {
        return new EffectDisciplineCalculator(provider, manifest, options);
    }

    /// <summary>
    /// Creates a calculator using the Claude API (requires ANTHROPIC_API_KEY).
    /// </summary>
    public static EffectDisciplineCalculator CreateWithClaude(
        LlmTaskManifest manifest,
        EffectDisciplineOptions? options = null)
    {
        var provider = new ClaudeProvider();
        if (!provider.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Claude provider unavailable: {provider.UnavailabilityReason}");
        }

        return new EffectDisciplineCalculator(provider, manifest, options);
    }
}
