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
/// Note: Previously there was a static TaskCompletionCalculator that estimated
/// completion potential based on code structure. This has been replaced with
/// this LLM-based implementation that actually generates and tests code.
/// </summary>
public class TaskCompletionCalculator : IMetricCalculator
{
    public string Category => "TaskCompletion";

    public string Description =>
        "Measures AI agent task completion rates by generating and executing code in both languages";

    private readonly ILlmProvider? _provider;
    private readonly LlmResponseCache? _cache;
    private readonly LlmTaskManifest? _manifest;
    private readonly LlmTaskRunnerOptions _runnerOptions;
    private LlmTaskRunResults? _lastResults;

    /// <summary>
    /// Creates a calculator with default settings (uses mock provider if no API key).
    /// </summary>
    public TaskCompletionCalculator()
    {
        _runnerOptions = new LlmTaskRunnerOptions
        {
            DryRun = true, // Default to dry run to avoid API costs
            UseCache = true
        };
    }

    /// <summary>
    /// Creates a calculator with specified provider and options.
    /// </summary>
    public TaskCompletionCalculator(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        LlmTaskRunnerOptions? options = null,
        LlmResponseCache? cache = null)
    {
        _provider = provider;
        _manifest = manifest;
        _cache = cache;
        _runnerOptions = options ?? new LlmTaskRunnerOptions();
    }

    /// <summary>
    /// Gets the results from the last calculation.
    /// </summary>
    public LlmTaskRunResults? LastResults => _lastResults;

    public async Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // If no provider or manifest configured, return estimation based on context
        if (_provider == null || _manifest == null)
        {
            return CalculateEstimatedMetric(context);
        }

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
            "ActualCompletion",
            summary.AverageCalorScore,
            summary.AverageCSharpScore,
            details);
    }

    /// <summary>
    /// Calculates an estimated metric based on code characteristics when
    /// actual LLM evaluation is not available.
    /// </summary>
    private MetricResult CalculateEstimatedMetric(EvaluationContext context)
    {
        // Estimate completion potential based on structural characteristics
        var calorScore = EstimateCalorCompletionScore(context);
        var csharpScore = EstimateCSharpCompletionScore(context);

        var details = new Dictionary<string, object>
        {
            ["estimated"] = true,
            ["reason"] = "No LLM provider configured - using structural estimation",
            ["calorFactors"] = new Dictionary<string, object>
            {
                ["compiles"] = context.CalorCompilation.Success,
                ["hasContracts"] = context.CalorSource.Contains("§REQ") ||
                                   context.CalorSource.Contains("§ENS"),
                ["hasExplicitTypes"] = context.CalorSource.Contains("§O{")
            },
            ["csharpFactors"] = new Dictionary<string, object>
            {
                ["compiles"] = context.CSharpCompilation.Success,
                ["hasReturnStatements"] = context.CSharpSource.Contains("return ")
            }
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "EstimatedCompletion",
            calorScore,
            csharpScore,
            details);
    }

    private static double EstimateCalorCompletionScore(EvaluationContext context)
    {
        var score = 0.0;

        // Base compilation score
        if (context.CalorCompilation.Success)
            score += 0.4;

        // Structure completeness
        var source = context.CalorSource;
        if (source.Contains("§M{") && source.Contains("§/M{"))
            score += 0.1;
        if (source.Contains("§F{") && source.Contains("§/F{"))
            score += 0.1;
        if (source.Contains("§B{") && source.Contains("§/B{"))
            score += 0.1;

        // Contracts provide additional correctness guarantees
        if (source.Contains("§REQ") || source.Contains("§REQUIRE"))
            score += 0.15;
        if (source.Contains("§ENS") || source.Contains("§ENSURE"))
            score += 0.15;

        return Math.Min(score, 1.0);
    }

    private static double EstimateCSharpCompletionScore(EvaluationContext context)
    {
        var score = 0.0;

        // Base compilation score
        if (context.CSharpCompilation.Success)
            score += 0.4;

        // Structure completeness
        var source = context.CSharpSource;
        if (source.Contains("class ") || source.Contains("struct "))
            score += 0.1;
        if (source.Contains("public ") || source.Contains("private "))
            score += 0.05;
        if (source.Contains("return "))
            score += 0.15;

        // Method completeness
        if (source.Contains("(") && source.Contains(")") && source.Contains("{"))
            score += 0.1;

        // Exception handling (equivalent to contracts)
        if (source.Contains("throw ") || source.Contains("ArgumentException"))
            score += 0.1;

        // Documentation
        if (source.Contains("///") || source.Contains("//"))
            score += 0.05;

        return Math.Min(score, 0.95); // Cap lower than Calor to reflect lack of contracts
    }

    /// <summary>
    /// Creates a calculator configured for actual LLM evaluation.
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
