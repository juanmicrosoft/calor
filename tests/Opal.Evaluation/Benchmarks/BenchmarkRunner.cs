using Opal.Evaluation.Core;
using Opal.Evaluation.Metrics;

namespace Opal.Evaluation.Benchmarks;

/// <summary>
/// Orchestrates benchmark execution across all evaluation categories.
/// </summary>
public class BenchmarkRunner
{
    private readonly List<IMetricCalculator> _calculators;
    private readonly TestDataAdapter _adapter;
    private readonly BenchmarkRunnerOptions _options;

    public BenchmarkRunner(BenchmarkRunnerOptions? options = null)
    {
        _options = options ?? new BenchmarkRunnerOptions();

        var testDataPath = TestDataAdapter.GetTestDataPath();
        var benchmarkPath = TestDataAdapter.GetBenchmarkPath();
        _adapter = new TestDataAdapter(testDataPath, benchmarkPath);

        // Initialize all calculators
        _calculators = new List<IMetricCalculator>
        {
            new TokenEconomicsCalculator(),
            new GenerationAccuracyCalculator(),
            new ComprehensionCalculator(),
            new EditPrecisionCalculator(),
            new ErrorDetectionCalculator(),
            new InformationDensityCalculator(),
            new TaskCompletionCalculator()
        };
    }

    /// <summary>
    /// Runs all benchmarks and returns aggregated results.
    /// </summary>
    public async Task<EvaluationResult> RunAllAsync(BenchmarkManifest manifest)
    {
        var result = new EvaluationResult
        {
            BenchmarkCount = manifest.Benchmarks.Count
        };

        var benchmarks = await _adapter.LoadAllBenchmarksAsync(manifest);

        foreach (var (entry, context) in benchmarks)
        {
            if (_options.Verbose)
                Console.WriteLine($"Running benchmark: {entry.DisplayName}");

            var caseResult = await RunSingleBenchmarkAsync(entry, context);
            result.CaseResults.Add(caseResult);
            result.Metrics.AddRange(caseResult.Metrics);
        }

        // Calculate summary statistics
        result.Summary = CalculateSummary(result);

        return result;
    }

    /// <summary>
    /// Runs benchmarks for a specific category only.
    /// </summary>
    public async Task<EvaluationResult> RunCategoryAsync(
        BenchmarkManifest manifest,
        string category)
    {
        var filteredManifest = new BenchmarkManifest
        {
            Version = manifest.Version,
            Description = $"{manifest.Description} (filtered: {category})",
            Benchmarks = manifest.GetByCategory(category).ToList()
        };

        return await RunAllAsync(filteredManifest);
    }

    /// <summary>
    /// Runs a single benchmark case.
    /// </summary>
    public async Task<BenchmarkCaseResult> RunSingleBenchmarkAsync(
        BenchmarkEntry entry,
        EvaluationContext context)
    {
        var caseResult = new BenchmarkCaseResult
        {
            CaseId = entry.Id,
            FileName = entry.Name ?? entry.Id,
            Level = entry.Level,
            Features = entry.Features,
            OpalSuccess = context.OpalCompilation.Success,
            CSharpSuccess = context.CSharpCompilation.Success
        };

        // Run each calculator
        foreach (var calculator in _calculators)
        {
            // Skip calculators if filtering is enabled
            if (_options.Categories.Count > 0 &&
                !_options.Categories.Contains(calculator.Category))
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
    /// Runs benchmarks from source code strings (for testing).
    /// </summary>
    public async Task<BenchmarkCaseResult> RunFromSourceAsync(
        string opalSource,
        string csharpSource,
        string name = "inline")
    {
        var context = new EvaluationContext
        {
            OpalSource = opalSource,
            CSharpSource = csharpSource,
            FileName = name,
            Level = 1,
            Features = new List<string>()
        };

        var entry = new BenchmarkEntry
        {
            Id = name,
            Name = name,
            OpalFile = "",
            CSharpFile = ""
        };

        return await RunSingleBenchmarkAsync(entry, context);
    }

    /// <summary>
    /// Calculates summary statistics from all benchmark results.
    /// </summary>
    private static EvaluationSummary CalculateSummary(EvaluationResult result)
    {
        var summary = new EvaluationSummary();

        // Group metrics by category
        var byCategory = result.Metrics
            .GroupBy(m => m.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Calculate average advantage per category
        foreach (var (category, metrics) in byCategory)
        {
            var validMetrics = metrics.Where(m => m.AdvantageRatio > 0).ToList();
            if (validMetrics.Count > 0)
            {
                // Use geometric mean for ratios
                var product = validMetrics.Aggregate(1.0, (acc, m) => acc * m.AdvantageRatio);
                var geoMean = Math.Pow(product, 1.0 / validMetrics.Count);
                summary.CategoryAdvantages[category] = Math.Round(geoMean, 2);
            }
        }

        // Calculate overall advantage (geometric mean of category advantages)
        if (summary.CategoryAdvantages.Count > 0)
        {
            var product = summary.CategoryAdvantages.Values.Aggregate(1.0, (acc, v) => acc * v);
            summary.OverallOpalAdvantage = Math.Round(
                Math.Pow(product, 1.0 / summary.CategoryAdvantages.Count), 2);
        }

        // Count successes
        summary.OpalPassCount = result.CaseResults.Count(c => c.OpalSuccess);
        summary.CSharpPassCount = result.CaseResults.Count(c => c.CSharpSuccess);

        // Identify top categories
        summary.TopOpalCategories = summary.CategoryAdvantages
            .Where(kv => kv.Value > 1.0)
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => kv.Key)
            .ToList();

        summary.CSharpAdvantageCategories = summary.CategoryAdvantages
            .Where(kv => kv.Value < 1.0)
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        return summary;
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
