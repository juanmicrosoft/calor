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
/// </summary>
public class SafetyCalculator : IMetricCalculator
{
    public string Category => "Safety";

    public string Description =>
        "Measures contract enforcement effectiveness and error quality for catching bugs";

    private readonly ILlmProvider? _provider;
    private readonly LlmResponseCache? _cache;
    private readonly LlmTaskManifest? _manifest;
    private readonly SafetyBenchmarkOptions _options;
    private SafetyBenchmarkResults? _lastResults;

    /// <summary>
    /// Creates a calculator with default settings (uses estimation if no provider configured).
    /// </summary>
    public SafetyCalculator()
    {
        _options = new SafetyBenchmarkOptions
        {
            DryRun = true, // Default to dry run to avoid API costs
            UseCache = true
        };
    }

    /// <summary>
    /// Creates a calculator with specified provider and options.
    /// </summary>
    public SafetyCalculator(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        SafetyBenchmarkOptions? options = null,
        LlmResponseCache? cache = null)
    {
        _provider = provider;
        _manifest = manifest;
        _cache = cache;
        _options = options ?? new SafetyBenchmarkOptions();
    }

    /// <summary>
    /// Gets the results from the last calculation.
    /// </summary>
    public SafetyBenchmarkResults? LastResults => _lastResults;

    public async Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // If no provider or manifest configured, return estimation based on context
        if (_provider == null || _manifest == null)
        {
            return CalculateEstimatedMetric(context);
        }

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
    /// Calculates an estimated safety metric based on code characteristics when
    /// actual LLM-based safety evaluation is not available.
    /// </summary>
    private MetricResult CalculateEstimatedMetric(EvaluationContext context)
    {
        var calorScore = EstimateCalorSafetyScore(context);
        var csharpScore = EstimateCSharpSafetyScore(context);

        var details = new Dictionary<string, object>
        {
            ["estimated"] = true,
            ["reason"] = "No LLM provider configured - using structural estimation",
            ["calorFactors"] = new Dictionary<string, object>
            {
                ["compiles"] = context.CalorCompilation.Success,
                ["hasPreconditions"] = context.CalorSource.Contains("§REQ") ||
                                       context.CalorSource.Contains("§Q"),
                ["hasPostconditions"] = context.CalorSource.Contains("§ENS") ||
                                        context.CalorSource.Contains("§S"),
                ["hasInvariants"] = context.CalorSource.Contains("§INV")
            },
            ["csharpFactors"] = new Dictionary<string, object>
            {
                ["compiles"] = context.CSharpCompilation.Success,
                ["hasThrowStatements"] = context.CSharpSource.Contains("throw "),
                ["hasArgumentChecks"] = context.CSharpSource.Contains("ArgumentException") ||
                                        context.CSharpSource.Contains("ArgumentNullException")
            }
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "EstimatedSafety",
            calorScore,
            csharpScore,
            details);
    }

    private static double EstimateCalorSafetyScore(EvaluationContext context)
    {
        var score = 0.3; // Base score for having contracts available

        if (!context.CalorCompilation.Success)
            return 0.0;

        var source = context.CalorSource;

        // Preconditions (§REQ or §Q)
        if (source.Contains("§REQ") || source.Contains("§Q"))
            score += 0.25;

        // Postconditions (§ENS or §S)
        if (source.Contains("§ENS") || source.Contains("§S"))
            score += 0.20;

        // Invariants
        if (source.Contains("§INV"))
            score += 0.15;

        // Effects (help with catching side-effect bugs)
        if (source.Contains("§E{"))
            score += 0.10;

        return Math.Min(score, 1.0);
    }

    private static double EstimateCSharpSafetyScore(EvaluationContext context)
    {
        var score = 0.2; // Base score

        if (!context.CSharpCompilation.Success)
            return 0.0;

        var source = context.CSharpSource;

        // Exception throwing
        if (source.Contains("throw "))
            score += 0.15;

        // Argument validation
        if (source.Contains("ArgumentException") || source.Contains("ArgumentNullException"))
            score += 0.15;

        // Null checks
        if (source.Contains("== null") || source.Contains("is null"))
            score += 0.10;

        // Debug.Assert
        if (source.Contains("Debug.Assert"))
            score += 0.10;

        // Contract.Requires/Ensures (Code Contracts)
        if (source.Contains("Contract.Requires") || source.Contains("Contract.Ensures"))
            score += 0.20;

        return Math.Min(score, 0.85); // Cap lower than Calor - no postconditions
    }

    /// <summary>
    /// Creates a calculator configured for actual LLM-based safety evaluation.
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
