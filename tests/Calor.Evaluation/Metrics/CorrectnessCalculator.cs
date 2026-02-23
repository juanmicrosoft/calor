using Calor.Evaluation.Core;
using Calor.Evaluation.LlmTasks;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Providers;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Calculates code correctness metrics by measuring bug prevention through edge case handling.
/// This is a fair, unbiased comparison - both languages can achieve 100% through correct code.
///
/// Unlike feature-specific benchmarks, this measures outcomes not mechanisms:
/// - A bug prevented is a bug prevented, regardless of HOW
/// - Calor contracts catching bugs = good
/// - C# guard clauses catching bugs = equally good
/// - Test pass/fail is the only metric
/// </summary>
public class CorrectnessCalculator : IMetricCalculator
{
    public string Category => "Correctness";

    public string Description =>
        "Measures code correctness through edge case handling - pure pass/fail, no language bias";

    private readonly ILlmProvider? _provider;
    private readonly LlmResponseCache? _cache;
    private readonly LlmTaskManifest? _manifest;
    private readonly CorrectnessBenchmarkOptions _options;
    private CorrectnessBenchmarkResults? _lastResults;

    /// <summary>
    /// Creates a calculator with default settings (uses estimation if no provider configured).
    /// </summary>
    public CorrectnessCalculator()
    {
        _options = new CorrectnessBenchmarkOptions
        {
            DryRun = true,
            UseCache = true
        };
    }

    /// <summary>
    /// Creates a calculator with specified provider and options.
    /// </summary>
    public CorrectnessCalculator(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        CorrectnessBenchmarkOptions? options = null,
        LlmResponseCache? cache = null)
    {
        _provider = provider;
        _manifest = manifest;
        _cache = cache;
        _options = options ?? new CorrectnessBenchmarkOptions();
    }

    /// <summary>
    /// Gets the results from the last calculation.
    /// </summary>
    public CorrectnessBenchmarkResults? LastResults => _lastResults;

    public async Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // If no provider or manifest configured, return estimation based on context
        if (_provider == null || _manifest == null)
        {
            return CalculateEstimatedMetric(context);
        }

        // Run actual correctness benchmark
        using var runner = new CorrectnessBenchmarkRunner(_provider, _cache);
        _lastResults = await runner.RunAllAsync(_manifest, _options);

        var summary = _lastResults.Summary;

        var details = new Dictionary<string, object>
        {
            ["totalTasks"] = summary.TotalTasks,
            ["calorWins"] = summary.CalorWins,
            ["csharpWins"] = summary.CSharpWins,
            ["ties"] = summary.Ties,
            ["calorTestPassRate"] = summary.AverageCalorScore,
            ["csharpTestPassRate"] = summary.AverageCSharpScore,
            ["calorEdgeCaseScore"] = summary.CalorEdgeCaseScore,
            ["csharpEdgeCaseScore"] = summary.CSharpEdgeCaseScore,
            ["totalTestsPassed"] = summary.TotalTestsPassed,
            ["totalTests"] = summary.TotalTests,
            ["provider"] = _lastResults.Provider ?? "unknown",
            ["isDryRun"] = _options.DryRun,
            ["byCategory"] = summary.ByCategory
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "CorrectnessScore",
            summary.AverageCalorScore,
            summary.AverageCSharpScore,
            details);
    }

    /// <summary>
    /// Calculates an estimated correctness metric based on code characteristics when
    /// actual LLM-based evaluation is not available.
    /// </summary>
    private MetricResult CalculateEstimatedMetric(EvaluationContext context)
    {
        var calorScore = EstimateCalorCorrectnessScore(context);
        var csharpScore = EstimateCSharpCorrectnessScore(context);

        var source = context.CalorSource;
        var details = new Dictionary<string, object>
        {
            ["estimated"] = true,
            ["reason"] = "No LLM provider configured - using structural estimation",
            ["calorFactors"] = new Dictionary<string, object>
            {
                ["compiles"] = context.CalorCompilation.Success,
                ["hasPreconditions"] = source.Contains("§Q") || source.Contains("§REQ"),
                ["hasPostconditions"] = source.Contains("§S ") || source.Contains("§S(") ||
                                       source.Contains("§ENS"),
                ["hasOptionHandling"] = source.Contains("Option<") || source.Contains("§SM") ||
                                        source.Contains("§NN") || source.Contains("is_some") ||
                                        source.Contains("is_none") || source.Contains("unwrap_or"),
                ["hasBoundsChecks"] = HasCalorBoundsChecks(source),
                ["hasConditionalReturns"] = source.Contains("→ §R") || source.Contains("§EL"),
                ["hasAnalysisSignal"] = context.CalorCompilation.EdgeCaseCoverage?.CoverageScore > 0
            },
            ["csharpFactors"] = new Dictionary<string, object>
            {
                ["compiles"] = context.CSharpCompilation.Success,
                ["hasNullChecks"] = context.CSharpSource.Contains("== null") ||
                                    context.CSharpSource.Contains("is null") ||
                                    context.CSharpSource.Contains("?."),
                ["hasArgumentValidation"] = context.CSharpSource.Contains("ArgumentNullException") ||
                                            context.CSharpSource.Contains("ArgumentException"),
                ["hasBoundsChecks"] = context.CSharpSource.Contains("Length") ||
                                      context.CSharpSource.Contains("Count")
            }
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "EstimatedCorrectness",
            calorScore,
            csharpScore,
            details);
    }

    private static double EstimateCalorCorrectnessScore(EvaluationContext context)
    {
        var score = 0.5; // Base score

        if (!context.CalorCompilation.Success)
            return 0.0;

        var source = context.CalorSource;

        // Preconditions indicate input validation
        if (source.Contains("§Q") || source.Contains("§REQ"))
            score += 0.15;

        // Postconditions indicate output validation
        // Use "§S " (with trailing space) to avoid matching §SM, §SB, §SET, etc.
        if (source.Contains("§S ") || source.Contains("§S(") || source.Contains("§ENS"))
            score += 0.10;

        // Option type handling (Calor uses Option<T>/Some/None, not null)
        if (source.Contains("Option<") || source.Contains("§SM") || source.Contains("§NN") ||
            source.Contains("is_some") || source.Contains("is_none") || source.Contains("unwrap_or"))
            score += 0.10;

        // Bounds checking — support both infix (>=, <) and lisp-style ((<=, (>=, (==)
        if (HasCalorBoundsChecks(source))
            score += 0.10;

        // Edge case returns — conditional returns (→ §R) and else branches (§EL)
        if (source.Contains("→ §R") || source.Contains("§EL"))
            score += 0.05;

        // Analysis signal — AST analyzer detected defensive programming patterns
        var coverage = context.CalorCompilation.EdgeCaseCoverage;
        if (coverage != null && coverage.CoverageScore > 0)
            score += 0.05;

        return Math.Min(score, 1.0);
    }

    private static bool HasCalorBoundsChecks(string source)
    {
        // Infix operators
        if (source.Contains(">=") && source.Contains("<"))
            return true;

        // Lisp-style comparisons against boundary values 0 or 1
        // Patterns like (<= n 1), (== n 0), (< n 0), (>= n 0)
        if ((source.Contains("(<= ") || source.Contains("(>= ") || source.Contains("(== ")) &&
            (source.Contains(" 0)") || source.Contains(" 1)")))
            return true;

        return false;
    }

    private static double EstimateCSharpCorrectnessScore(EvaluationContext context)
    {
        var score = 0.5; // Base score

        if (!context.CSharpCompilation.Success)
            return 0.0;

        var source = context.CSharpSource;

        // Null checks
        if (source.Contains("== null") || source.Contains("is null") || source.Contains("?."))
            score += 0.15;

        // Argument validation
        if (source.Contains("ArgumentNullException") || source.Contains("ArgumentException"))
            score += 0.10;

        // Bounds checking
        if (source.Contains(".Length") || source.Contains(".Count"))
            score += 0.10;

        // Conditional returns for edge cases
        if (source.Contains("return 0") || source.Contains("return -1") ||
            source.Contains("return null") || source.Contains("return \"\""))
            score += 0.10;

        // Try-catch for error handling
        if (source.Contains("try") && source.Contains("catch"))
            score += 0.05;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Creates a calculator configured for actual LLM-based correctness evaluation.
    /// </summary>
    public static CorrectnessCalculator CreateWithProvider(
        ILlmProvider provider,
        LlmTaskManifest manifest,
        CorrectnessBenchmarkOptions? options = null)
    {
        return new CorrectnessCalculator(provider, manifest, options);
    }

    /// <summary>
    /// Creates a calculator using the Claude API (requires ANTHROPIC_API_KEY).
    /// </summary>
    public static CorrectnessCalculator CreateWithClaude(
        LlmTaskManifest manifest,
        CorrectnessBenchmarkOptions? options = null)
    {
        var provider = new ClaudeProvider();
        if (!provider.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Claude provider unavailable: {provider.UnavailabilityReason}");
        }

        return new CorrectnessCalculator(provider, manifest, options);
    }
}
