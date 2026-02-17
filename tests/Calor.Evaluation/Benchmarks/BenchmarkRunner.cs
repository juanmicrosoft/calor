using Calor.Evaluation.Core;
using Calor.Evaluation.Metrics;

namespace Calor.Evaluation.Benchmarks;

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
            // Static metrics
            new TokenEconomicsCalculator(),
            new GenerationAccuracyCalculator(),
            new ComprehensionCalculator(),
            new EditPrecisionCalculator(),
            new ErrorDetectionCalculator(),
            new InformationDensityCalculator(),
            new RefactoringStabilityCalculator(),
            // LLM-based metrics (use estimation mode by default if no provider configured)
            new TaskCompletionCalculator(),
            new SafetyCalculator(),
            new EffectDisciplineCalculator(),
            // Calor-only metric (C# score always 0)
            new InteropEffectCoverageCalculator()
        };
    }

    /// <summary>
    /// Runs all benchmarks and returns aggregated results.
    /// </summary>
    public async Task<EvaluationResult> RunAllAsync(BenchmarkManifest manifest)
    {
        if (_options.StatisticalMode)
        {
            return await RunWithStatisticalAnalysisAsync(manifest);
        }

        var result = new EvaluationResult
        {
            BenchmarkCount = manifest.Benchmarks.Count,
            CommitHash = GetGitCommitHash()
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
    /// Runs benchmarks multiple times for statistical analysis.
    /// </summary>
    public async Task<EvaluationResult> RunWithStatisticalAnalysisAsync(BenchmarkManifest manifest)
    {
        var runs = _options.StatisticalRuns;
        if (_options.Verbose)
            Console.WriteLine($"Running statistical analysis with {runs} runs...");

        var allRunResults = new List<EvaluationResult>();

        for (var i = 0; i < runs; i++)
        {
            if (_options.Verbose)
                Console.WriteLine($"Run {i + 1}/{runs}...");

            var runResult = new EvaluationResult
            {
                BenchmarkCount = manifest.Benchmarks.Count
            };

            var benchmarks = await _adapter.LoadAllBenchmarksAsync(manifest);

            foreach (var (entry, context) in benchmarks)
            {
                var caseResult = await RunSingleBenchmarkAsync(entry, context);
                runResult.CaseResults.Add(caseResult);
                runResult.Metrics.AddRange(caseResult.Metrics);
            }

            runResult.Summary = CalculateSummary(runResult);
            allRunResults.Add(runResult);
        }

        // Aggregate results with statistical analysis
        return AggregateStatisticalResults(allRunResults, manifest);
    }

    /// <summary>
    /// Aggregates multiple run results into a single result with statistical summaries.
    /// </summary>
    private EvaluationResult AggregateStatisticalResults(
        List<EvaluationResult> runResults,
        BenchmarkManifest manifest)
    {
        var firstRun = runResults[0];

        // Collect all metric results grouped by category and name
        var metricGroups = runResults
            .SelectMany(r => r.Metrics)
            .GroupBy(m => (m.Category, m.MetricName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var statisticalSummaries = new List<StatisticalSummary>();

        foreach (var ((category, metricName), metrics) in metricGroups)
        {
            var calorScores = metrics.Select(m => m.CalorScore).ToList();
            var csharpScores = metrics.Select(m => m.CSharpScore).ToList();
            var advantageRatios = metrics.Select(m => m.AdvantageRatio).ToList();

            var summary = new StatisticalSummary
            {
                Category = category,
                MetricName = metricName,
                SampleCount = metrics.Count,
                CalorMean = StatisticalAnalysis.Mean(calorScores),
                CalorStdDev = StatisticalAnalysis.StandardDeviation(calorScores),
                CalorCI = StatisticalAnalysis.CalculateConfidenceInterval(calorScores, _options.ConfidenceLevel),
                CSharpMean = StatisticalAnalysis.Mean(csharpScores),
                CSharpStdDev = StatisticalAnalysis.StandardDeviation(csharpScores),
                CSharpCI = StatisticalAnalysis.CalculateConfidenceInterval(csharpScores, _options.ConfidenceLevel),
                AdvantageRatioMean = StatisticalAnalysis.Mean(advantageRatios),
                AdvantageRatioCI = StatisticalAnalysis.CalculateConfidenceInterval(advantageRatios, _options.ConfidenceLevel),
                CohensD = StatisticalAnalysis.CohensD(calorScores, csharpScores),
                EffectSizeInterpretation = StatisticalAnalysis.InterpretEffectSize(
                    StatisticalAnalysis.CohensD(calorScores, csharpScores)),
                TTest = StatisticalAnalysis.PairedTTest(calorScores, csharpScores)
            };

            statisticalSummaries.Add(summary);
        }

        // Create aggregated result using means, preserving isCalorOnly flag
        var aggregatedMetrics = metricGroups.Select(g =>
        {
            var details = new Dictionary<string, object>
            {
                ["statisticalSampleCount"] = g.Value.Count
            };

            // Preserve isCalorOnly flag from original metrics
            if (g.Value.Any(m => m.IsCalorOnly))
            {
                details["isCalorOnly"] = true;
            }

            return new MetricResult(
                g.Key.Category,
                g.Key.MetricName,
                StatisticalAnalysis.Mean(g.Value.Select(m => m.CalorScore)),
                StatisticalAnalysis.Mean(g.Value.Select(m => m.CSharpScore)),
                StatisticalAnalysis.Mean(g.Value.Select(m => m.AdvantageRatio)),
                details);
        }).ToList();

        var result = new EvaluationResult
        {
            BenchmarkCount = manifest.Benchmarks.Count,
            Metrics = aggregatedMetrics,
            CaseResults = firstRun.CaseResults, // Use first run for case details
            StatisticalSummaries = statisticalSummaries,
            StatisticalRunCount = runResults.Count,
            CommitHash = GetGitCommitHash()
        };

        result.Summary = CalculateSummary(result);

        // Add confidence intervals to summary
        foreach (var catSummary in statisticalSummaries.GroupBy(s => s.Category))
        {
            var avgCI = StatisticalAnalysis.CalculateConfidenceInterval(
                catSummary.Select(s => s.AdvantageRatioMean));
            result.Summary.CategoryConfidenceIntervals[catSummary.Key] = avgCI;
        }

        return result;
    }

    /// <summary>
    /// Gets the current git commit hash, if available.
    /// </summary>
    private static string? GetGitCommitHash()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --short HEAD",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
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
            CalorSuccess = context.CalorCompilation.Success,
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

        var entry = new BenchmarkEntry
        {
            Id = name,
            Name = name,
            CalorFile = "",
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
            summary.OverallCalorAdvantage = Math.Round(
                Math.Pow(product, 1.0 / summary.CategoryAdvantages.Count), 2);
        }

        // Count successes
        summary.CalorPassCount = result.CaseResults.Count(c => c.CalorSuccess);
        summary.CSharpPassCount = result.CaseResults.Count(c => c.CSharpSuccess);

        // Identify Calor-only categories (where C# has no equivalent)
        var calorOnlyCategories = result.Metrics
            .Where(m => m.IsCalorOnly)
            .Select(m => m.Category)
            .Distinct()
            .ToHashSet();

        // Identify top categories (all categories where Calor wins, including Calor-only)
        summary.TopCalorCategories = summary.CategoryAdvantages
            .Where(kv => kv.Value > 1.0 || calorOnlyCategories.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        summary.CSharpAdvantageCategories = summary.CategoryAdvantages
            .Where(kv => kv.Value < 1.0 && !calorOnlyCategories.Contains(kv.Key))
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

    /// <summary>
    /// Enable statistical analysis mode with multiple runs.
    /// </summary>
    public bool StatisticalMode { get; set; }

    /// <summary>
    /// Number of runs for statistical analysis (default: 30).
    /// </summary>
    public int StatisticalRuns { get; set; } = 30;

    /// <summary>
    /// Confidence level for intervals (default: 0.95 = 95%).
    /// </summary>
    public double ConfidenceLevel { get; set; } = 0.95;
}
