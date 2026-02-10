using Calor.Compiler.Evaluation.Core;
using Calor.Compiler.Evaluation.Metrics;

namespace Calor.Compiler.Evaluation.Benchmarks;

/// <summary>
/// Orchestrates benchmark execution across all evaluation categories.
/// </summary>
public class BenchmarkRunner
{
    private readonly List<IMetricCalculator> _calculators;
    private readonly BenchmarkRunnerOptions _options;

    public BenchmarkRunner(BenchmarkRunnerOptions? options = null)
    {
        _options = options ?? new BenchmarkRunnerOptions();

        // Initialize all calculators (7 standard + 3 Calor-only)
        _calculators = new List<IMetricCalculator>
        {
            new TokenEconomicsCalculator(),
            new GenerationAccuracyCalculator(),
            new ComprehensionCalculator(),
            new EditPrecisionCalculator(),
            new ErrorDetectionCalculator(),
            new InformationDensityCalculator(),
            new TaskCompletionCalculator(),
            // Calor-only metrics (C# score always 0)
            new ContractVerificationCalculator(),
            new EffectSoundnessCalculator(),
            new InteropEffectCoverageCalculator()
        };
    }

    /// <summary>
    /// Runs benchmarks from source code strings.
    /// </summary>
    public async Task<BenchmarkCaseResult> RunFromSourceAsync(
        string calorSource,
        string csharpSource,
        string name = "inline")
    {
        var context = new EvaluationContext
        {
            CalorSource = calorSource,
            CSharpSource = csharpSource,
            FileName = name,
            Level = 1,
            Features = new List<string>()
        };

        return await RunSingleBenchmarkAsync(context, name);
    }

    /// <summary>
    /// Runs a single benchmark case.
    /// </summary>
    public async Task<BenchmarkCaseResult> RunSingleBenchmarkAsync(
        EvaluationContext context,
        string caseId)
    {
        var caseResult = new BenchmarkCaseResult
        {
            CaseId = caseId,
            FileName = context.FileName,
            Level = context.Level,
            Features = context.Features,
            CalorSuccess = context.CalorCompilation.Success,
            CSharpSuccess = context.CSharpCompilation.Success
        };

        // Run each calculator
        foreach (var calculator in _calculators)
        {
            // Skip calculators if filtering is enabled
            if (_options.Categories.Count > 0 &&
                !_options.Categories.Contains(calculator.Category, StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                var metric = await calculator.CalculateAsync(context);
                caseResult.Metrics.Add(metric);
            }
            catch (Exception ex)
            {
                if (_options.Verbose)
                    Console.Error.WriteLine($"Warning: Calculator {calculator.Category} failed: {ex.Message}");

                // Add a failed metric result
                caseResult.Metrics.Add(new MetricResult(
                    calculator.Category,
                    "Error",
                    0, 0, 1.0,
                    new Dictionary<string, object> { ["error"] = ex.Message }));
            }
        }

        return caseResult;
    }

    /// <summary>
    /// Runs benchmarks for paired files.
    /// </summary>
    public async Task<BenchmarkCaseResult> RunFromFilesAsync(
        string calorPath,
        string csharpPath,
        string? name = null)
    {
        var calorSource = await File.ReadAllTextAsync(calorPath);
        var csharpSource = await File.ReadAllTextAsync(csharpPath);

        var caseName = name ?? Path.GetFileNameWithoutExtension(calorPath);

        return await RunFromSourceAsync(calorSource, csharpSource, caseName);
    }

    /// <summary>
    /// Gets a specific calculator by category.
    /// </summary>
    public IMetricCalculator? GetCalculator(string category)
    {
        return _calculators.FirstOrDefault(c =>
            string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all registered calculators.
    /// </summary>
    public IReadOnlyList<IMetricCalculator> GetCalculators() => _calculators.AsReadOnly();

    /// <summary>
    /// Gets all available category names.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableCategories() => new[]
    {
        "TokenEconomics",
        "GenerationAccuracy",
        "Comprehension",
        "EditPrecision",
        "ErrorDetection",
        "InformationDensity",
        "TaskCompletion"
    };
}

/// <summary>
/// Options for configuring the benchmark runner.
/// </summary>
public class BenchmarkRunnerOptions
{
    /// <summary>
    /// Enable verbose logging.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Categories to run (empty = all).
    /// </summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// Maximum level to include.
    /// </summary>
    public int MaxLevel { get; set; } = 5;

    /// <summary>
    /// Timeout per benchmark in milliseconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Enable parallel execution.
    /// </summary>
    public bool Parallel { get; set; } = true;
}
