using Calor.Evaluation.Core;
using Calor.Evaluation.LlmTasks;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Providers;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Calculates LLM-based task completion metrics by having AI agents
/// generate code in both Calor and C#, then verifying correctness.
///
/// This provides empirical evidence for whether Calor's explicit structure
/// and contracts help AI agents write more correct code.
///
/// IMPORTANT: This metric requires an LLM provider. It cannot be used
/// without configuring a provider (e.g., Claude API).
/// </summary>
public class TaskCompletionCalculator : IMetricCalculator
{
    public string Category => "TaskCompletion";

    public string Description =>
        "Measures AI agent task completion rates by generating and executing code in both languages";

    private readonly ILlmProvider _provider;
    private readonly LlmResponseCache? _cache;
    private readonly LlmTaskManifest _manifest;
    private readonly LlmTaskRunnerOptions _runnerOptions;
    private LlmTaskRunResults? _lastResults;

    /// <summary>
    /// Creates a calculator with specified provider and manifest.
    /// </summary>
    /// <param name="provider">The LLM provider to use for code generation.</param>
    /// <param name="manifest">The task manifest containing task definitions.</param>
    /// <param name="options">Optional runner configuration.</param>
    /// <param name="cache">Optional response cache for reducing API costs.</param>
    public TaskCompletionCalculator(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        LlmTaskRunnerOptions? options = null,
        LlmResponseCache? cache = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _cache = cache;
        _runnerOptions = options ?? new LlmTaskRunnerOptions();
    }

    /// <summary>
    /// Gets the results from the last calculation.
    /// </summary>
    public LlmTaskRunResults? LastResults => _lastResults;

    public async Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Run actual LLM tasks
        using var runner = new LlmTaskRunner(_provider, _cache);
        _lastResults = await runner.RunAllAsync(_manifest, _runnerOptions);

        var summary = _lastResults.Summary;

        var details = new Dictionary<string, object>
        {
            ["totalTasks"] = summary.TotalTasks,
            ["calorWins"] = summary.CalorWins,
            ["csharpWins"] = summary.CSharpWins,
            ["ties"] = summary.Ties,
            ["calorCompilationRate"] = summary.CalorCompilationRate,
            ["csharpCompilationRate"] = summary.CSharpCompilationRate,
            ["calorTestPassRate"] = summary.CalorTestPassRate,
            ["csharpTestPassRate"] = summary.CSharpTestPassRate,
            ["totalCost"] = _lastResults.TotalCost,
            ["provider"] = _lastResults.Provider ?? "unknown",
            ["isDryRun"] = _lastResults.IsDryRun,
            ["byCategory"] = summary.ByCategory
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "TaskCompletion",
            summary.AverageCalorScore,
            summary.AverageCSharpScore,
            details);
    }

    /// <summary>
    /// Creates a calculator configured for LLM evaluation.
    /// </summary>
    public static TaskCompletionCalculator CreateWithProvider(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        LlmTaskRunnerOptions? options = null)
    {
        return new TaskCompletionCalculator(provider, manifest, options);
    }

    /// <summary>
    /// Creates a calculator using the Claude API (requires ANTHROPIC_API_KEY).
    /// </summary>
    public static TaskCompletionCalculator CreateWithClaude(
        LlmTaskManifest manifest,
        LlmTaskRunnerOptions? options = null)
    {
        var provider = new ClaudeProvider();
        if (!provider.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Claude provider unavailable: {provider.UnavailabilityReason}");
        }

        return new TaskCompletionCalculator(provider, manifest, options);
    }

    /// <summary>
    /// Creates a calculator using the mock provider (for testing).
    /// </summary>
    public static TaskCompletionCalculator CreateWithMock(
        LlmTaskManifest manifest,
        LlmTaskRunnerOptions? options = null)
    {
        var provider = MockProvider.WithWorkingImplementations();
        return new TaskCompletionCalculator(provider, manifest, options);
    }
}
