using Calor.Compiler.Evaluation.Benchmarks;
using Calor.Compiler.Evaluation.Core;
using Calor.Compiler.Evaluation.Reports;

namespace Calor.Compiler.Migration;

/// <summary>
/// Integrates with the full evaluation framework to provide comprehensive benchmark metrics.
/// Wraps the BenchmarkRunner to provide all 7 metric categories.
/// </summary>
public sealed class BenchmarkIntegration
{
    /// <summary>
    /// All available benchmark categories.
    /// </summary>
    public static readonly string[] AllCategories = new[]
    {
        "TokenEconomics",
        "GenerationAccuracy",
        "Comprehension",
        "EditPrecision",
        "ErrorDetection",
        "InformationDensity",
        "TaskCompletion"
    };

    /// <summary>
    /// Runs the full 7-metric benchmark on the provided source code.
    /// </summary>
    public static async Task<FullBenchmarkResult> RunFullBenchmarkAsync(
        string csharpSource,
        string calorSource,
        string? category = null,
        bool verbose = false)
    {
        var options = new BenchmarkRunnerOptions
        {
            Verbose = verbose,
            Categories = category != null ? new List<string> { category } : new List<string>()
        };

        var runner = new BenchmarkRunner(options);
        var caseResult = await runner.RunFromSourceAsync(calorSource, csharpSource, "inline");

        // Build the full result
        var result = new FullBenchmarkResult
        {
            CaseResult = caseResult,
            Summary = CalculateSummaryFromCase(caseResult)
        };

        return result;
    }

    /// <summary>
    /// Runs benchmarks for all paired files in a directory.
    /// </summary>
    public static async Task<FullBenchmarkResult> RunProjectBenchmarkAsync(
        string directory,
        string? category = null,
        bool verbose = false)
    {
        var options = new BenchmarkRunnerOptions
        {
            Verbose = verbose,
            Categories = category != null ? new List<string> { category } : new List<string>()
        };

        var runner = new BenchmarkRunner(options);

        // Discover paired files
        var calorFiles = Directory.GetFiles(directory, "*.calr", SearchOption.AllDirectories);
        var projectResults = new List<BenchmarkCaseResult>();

        foreach (var calorPath in calorFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(calorPath);
            var dir = Path.GetDirectoryName(calorPath)!;

            // Look for matching C# file
            var csharpPath = Path.Combine(dir, baseName + ".cs");
            if (!File.Exists(csharpPath))
            {
                csharpPath = Path.Combine(dir, baseName + ".g.cs");
                if (!File.Exists(csharpPath))
                    continue;
            }

            try
            {
                var caseResult = await runner.RunFromFilesAsync(calorPath, csharpPath, baseName);
                projectResults.Add(caseResult);

                if (verbose)
                {
                    Console.WriteLine($"  Benchmarked: {baseName}");
                }
            }
            catch (Exception ex)
            {
                if (verbose)
                {
                    Console.Error.WriteLine($"  Warning: Failed to benchmark {baseName}: {ex.Message}");
                }
            }
        }

        if (projectResults.Count == 0)
        {
            return new FullBenchmarkResult
            {
                CaseResult = null,
                Summary = new EvaluationSummary(),
                ProjectResults = new List<BenchmarkCaseResult>()
            };
        }

        var summary = CalculateSummaryFromCases(projectResults);

        return new FullBenchmarkResult
        {
            CaseResult = projectResults.FirstOrDefault(),
            Summary = summary,
            ProjectResults = projectResults
        };
    }

    /// <summary>
    /// Generates a markdown report from the benchmark result.
    /// </summary>
    public static string GenerateMarkdownReport(FullBenchmarkResult result, string? calorName = null, string? csharpName = null)
    {
        var generator = new MarkdownReportGenerator();

        // Build an EvaluationResult from the FullBenchmarkResult
        var evalResult = new EvaluationResult
        {
            BenchmarkCount = result.ProjectResults?.Count ?? (result.CaseResult != null ? 1 : 0),
            Summary = result.Summary
        };

        if (result.ProjectResults != null && result.ProjectResults.Count > 0)
        {
            foreach (var caseResult in result.ProjectResults)
            {
                evalResult.CaseResults.Add(caseResult);
                evalResult.Metrics.AddRange(caseResult.Metrics);
            }
        }
        else if (result.CaseResult != null)
        {
            evalResult.CaseResults.Add(result.CaseResult);
            evalResult.Metrics.AddRange(result.CaseResult.Metrics);
        }

        return generator.Generate(evalResult);
    }

    /// <summary>
    /// Generates a JSON report from the benchmark result.
    /// </summary>
    public static string GenerateJsonReport(FullBenchmarkResult result)
    {
        var generator = new JsonReportGenerator();

        // Build an EvaluationResult from the FullBenchmarkResult
        var evalResult = new EvaluationResult
        {
            BenchmarkCount = result.ProjectResults?.Count ?? (result.CaseResult != null ? 1 : 0),
            Summary = result.Summary
        };

        if (result.ProjectResults != null && result.ProjectResults.Count > 0)
        {
            foreach (var caseResult in result.ProjectResults)
            {
                evalResult.CaseResults.Add(caseResult);
                evalResult.Metrics.AddRange(caseResult.Metrics);
            }
        }
        else if (result.CaseResult != null)
        {
            evalResult.CaseResults.Add(result.CaseResult);
            evalResult.Metrics.AddRange(result.CaseResult.Metrics);
        }

        return generator.Generate(evalResult);
    }

    /// <summary>
    /// Formats a console output with the full 7-metric table.
    /// </summary>
    public static string FormatConsoleOutput(FullBenchmarkResult result, string calorName, string csharpName, bool verbose = false)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"Benchmark: {csharpName} vs {calorName}");
        sb.AppendLine();

        if (result.CaseResult == null || result.CaseResult.Metrics.Count == 0)
        {
            sb.AppendLine("No metrics calculated.");
            return sb.ToString();
        }

        // Build category table
        sb.AppendLine("+-------------------------+--------+--------+-----------+");
        sb.AppendLine("| Category                | C#     | Calor   | Advantage |");
        sb.AppendLine("+-------------------------+--------+--------+-----------+");

        // Group metrics by category and calculate averages
        var byCategory = result.CaseResult.Metrics
            .GroupBy(m => m.Category)
            .OrderByDescending(g => g.Average(m => m.AdvantageRatio));

        foreach (var category in byCategory)
        {
            var avgCalor = category.Average(m => m.CalorScore);
            var avgCSharp = category.Average(m => m.CSharpScore);
            var avgAdvantage = category.Average(m => m.AdvantageRatio);

            // Normalize scores to 0-1 range for display
            var calorNorm = NormalizeScore(avgCalor, avgCSharp, category.Key);
            var csharpNorm = NormalizeScore(avgCSharp, avgCalor, category.Key);

            sb.AppendLine($"| {category.Key,-23} | {csharpNorm,6:F2} | {calorNorm,6:F2} | {avgAdvantage,7:F2}x |");
        }

        sb.AppendLine("+-------------------------+--------+--------+-----------+");
        sb.AppendLine();

        // Overall advantage
        var overallAdvantage = result.Summary.OverallCalorAdvantage > 0
            ? result.Summary.OverallCalorAdvantage
            : CalculateGeometricMean(byCategory.Select(g => g.Average(m => m.AdvantageRatio)));

        sb.AppendLine($"Overall Calor Advantage: {overallAdvantage:F2}x (geometric mean)");

        // Verbose mode: show per-metric breakdown
        if (verbose)
        {
            sb.AppendLine();
            sb.AppendLine("Detailed Metrics:");
            sb.AppendLine();

            foreach (var category in byCategory)
            {
                sb.AppendLine($"  {category.Key}:");
                foreach (var metric in category)
                {
                    var indicator = metric.AdvantageRatio > 1 ? "+" : "";
                    sb.AppendLine($"    {metric.MetricName}: Calor={metric.CalorScore:F0}, C#={metric.CSharpScore:F0} ({indicator}{(metric.AdvantageRatio - 1) * 100:F0}%)");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats project-level console output.
    /// </summary>
    public static string FormatProjectConsoleOutput(FullBenchmarkResult result, string projectName, bool verbose = false)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"Project Benchmark: {projectName}");
        sb.AppendLine($"Files compared: {result.ProjectResults?.Count ?? 0}");
        sb.AppendLine();

        if (result.ProjectResults == null || result.ProjectResults.Count == 0)
        {
            sb.AppendLine("No paired .calr and .cs files found.");
            return sb.ToString();
        }

        // Summary table by category
        sb.AppendLine("Category Summary:");
        sb.AppendLine("+-------------------------+-----------+-----------+-----------+");
        sb.AppendLine("| Category                | Avg Calor  | Avg C#    | Advantage |");
        sb.AppendLine("+-------------------------+-----------+-----------+-----------+");

        // Aggregate all metrics from all cases
        var allMetrics = result.ProjectResults.SelectMany(c => c.Metrics).ToList();
        var byCategory = allMetrics
            .GroupBy(m => m.Category)
            .OrderByDescending(g => g.Average(m => m.AdvantageRatio));

        foreach (var category in byCategory)
        {
            var avgCalor = category.Average(m => m.CalorScore);
            var avgCSharp = category.Average(m => m.CSharpScore);
            var avgAdvantage = category.Average(m => m.AdvantageRatio);

            sb.AppendLine($"| {category.Key,-23} | {avgCalor,9:F1} | {avgCSharp,9:F1} | {avgAdvantage,7:F2}x |");
        }

        sb.AppendLine("+-------------------------+-----------+-----------+-----------+");
        sb.AppendLine();

        sb.AppendLine($"Overall Calor Advantage: {result.Summary.OverallCalorAdvantage:F2}x");
        sb.AppendLine();

        // Per-file breakdown
        sb.AppendLine("By File:");
        foreach (var caseResult in result.ProjectResults.OrderByDescending(c => c.AverageAdvantage))
        {
            var indicator = caseResult.AverageAdvantage > 1 ? "+" : "";
            sb.AppendLine($"  {caseResult.FileName}: {indicator}{(caseResult.AverageAdvantage - 1) * 100:F0}% advantage ({caseResult.Metrics.Count} metrics)");
        }

        if (verbose)
        {
            sb.AppendLine();
            sb.AppendLine("Top Calor Advantages:");
            foreach (var cat in result.Summary.TopCalorCategories.Take(3))
            {
                sb.AppendLine($"  - {cat}");
            }

            if (result.Summary.CSharpAdvantageCategories.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("C# Advantages:");
                foreach (var cat in result.Summary.CSharpAdvantageCategories)
                {
                    sb.AppendLine($"  - {cat}");
                }
            }
        }

        return sb.ToString();
    }

    // ========== Legacy methods for backward compatibility ==========

    /// <summary>
    /// Calculates token and line metrics for source code comparison.
    /// Kept for backward compatibility with the migration tool.
    /// </summary>
    public static FileMetrics CalculateMetrics(string originalSource, string convertedSource)
    {
        var originalTokens = TokenizeSource(originalSource);
        var convertedTokens = TokenizeSource(convertedSource);

        return new FileMetrics
        {
            OriginalLines = CountLines(originalSource),
            OutputLines = CountLines(convertedSource),
            OriginalTokens = originalTokens.Count,
            OutputTokens = convertedTokens.Count,
            OriginalCharacters = CountNonWhitespaceChars(originalSource),
            OutputCharacters = CountNonWhitespaceChars(convertedSource)
        };
    }

    /// <summary>
    /// Calculates the advantage ratio (higher = more compact Calor).
    /// </summary>
    public static double CalculateAdvantageRatio(FileMetrics metrics)
    {
        if (metrics.OutputTokens == 0)
            return 1.0;

        // Calculate geometric mean of ratios
        var tokenRatio = (double)metrics.OriginalTokens / metrics.OutputTokens;
        var lineRatio = metrics.OutputLines > 0 ? (double)metrics.OriginalLines / metrics.OutputLines : 1.0;
        var charRatio = metrics.OutputCharacters > 0 ? (double)metrics.OriginalCharacters / metrics.OutputCharacters : 1.0;

        return Math.Pow(tokenRatio * lineRatio * charRatio, 1.0 / 3.0);
    }

    /// <summary>
    /// Formats a benchmark comparison for console output (legacy format).
    /// </summary>
    public static string FormatComparison(FileMetrics metrics)
    {
        var advantage = CalculateAdvantageRatio(metrics);

        return $"""
            Token Economics:
              Before: {metrics.OriginalTokens:N0} tokens, {metrics.OriginalLines:N0} lines
              After:  {metrics.OutputTokens:N0} tokens, {metrics.OutputLines:N0} lines
              Token Savings: {metrics.TokenReduction:F1}%
              Line Savings: {metrics.LineReduction:F1}%
              Overall Advantage: {advantage:F2}x
            """;
    }

    /// <summary>
    /// Creates a benchmark summary from multiple file metrics.
    /// </summary>
    public static BenchmarkSummary CreateSummary(IEnumerable<FileMetrics> metricsCollection)
    {
        var metricsList = metricsCollection.ToList();

        return new BenchmarkSummary
        {
            TotalOriginalTokens = metricsList.Sum(m => m.OriginalTokens),
            TotalOutputTokens = metricsList.Sum(m => m.OutputTokens),
            TotalOriginalLines = metricsList.Sum(m => m.OriginalLines),
            TotalOutputLines = metricsList.Sum(m => m.OutputLines)
        };
    }

    /// <summary>
    /// Runs a quick benchmark comparison between C# and Calor source.
    /// </summary>
    public static BenchmarkResult RunQuickBenchmark(string csharpSource, string calorSource)
    {
        var metrics = CalculateMetrics(csharpSource, calorSource);
        var advantage = CalculateAdvantageRatio(metrics);

        return new BenchmarkResult
        {
            Metrics = metrics,
            AdvantageRatio = advantage,
            Summary = FormatComparison(metrics)
        };
    }

    // ========== Private helper methods ==========

    private static List<string> TokenizeSource(string source)
    {
        var tokens = new List<string>();
        var currentToken = "";

        foreach (var ch in source)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!string.IsNullOrEmpty(currentToken))
                {
                    tokens.Add(currentToken);
                    currentToken = "";
                }
            }
            else if (char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                if (!string.IsNullOrEmpty(currentToken))
                {
                    tokens.Add(currentToken);
                    currentToken = "";
                }
                tokens.Add(ch.ToString());
            }
            else
            {
                currentToken += ch;
            }
        }

        if (!string.IsNullOrEmpty(currentToken))
        {
            tokens.Add(currentToken);
        }

        return tokens;
    }

    private static int CountLines(string source)
    {
        if (string.IsNullOrEmpty(source))
            return 0;

        var lines = source.Split('\n');
        // Count non-empty lines
        return lines.Count(line => !string.IsNullOrWhiteSpace(line));
    }

    private static int CountNonWhitespaceChars(string source)
    {
        return source.Count(c => !char.IsWhiteSpace(c));
    }

    private static EvaluationSummary CalculateSummaryFromCase(BenchmarkCaseResult caseResult)
    {
        var summary = new EvaluationSummary();

        // Group metrics by category
        var byCategory = caseResult.Metrics
            .GroupBy(m => m.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Calculate average advantage per category
        foreach (var (category, metrics) in byCategory)
        {
            var validMetrics = metrics.Where(m => m.AdvantageRatio > 0).ToList();
            if (validMetrics.Count > 0)
            {
                var product = validMetrics.Aggregate(1.0, (acc, m) => acc * m.AdvantageRatio);
                var geoMean = Math.Pow(product, 1.0 / validMetrics.Count);
                summary.CategoryAdvantages[category] = Math.Round(geoMean, 2);
            }
        }

        // Calculate overall advantage
        if (summary.CategoryAdvantages.Count > 0)
        {
            var product = summary.CategoryAdvantages.Values.Aggregate(1.0, (acc, v) => acc * v);
            summary.OverallCalorAdvantage = Math.Round(
                Math.Pow(product, 1.0 / summary.CategoryAdvantages.Count), 2);
        }

        summary.CalorPassCount = caseResult.CalorSuccess ? 1 : 0;
        summary.CSharpPassCount = caseResult.CSharpSuccess ? 1 : 0;

        summary.TopCalorCategories = summary.CategoryAdvantages
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

    private static EvaluationSummary CalculateSummaryFromCases(List<BenchmarkCaseResult> caseResults)
    {
        var summary = new EvaluationSummary();

        if (caseResults.Count == 0)
            return summary;

        // Aggregate all metrics
        var allMetrics = caseResults.SelectMany(c => c.Metrics).ToList();

        // Group metrics by category
        var byCategory = allMetrics
            .GroupBy(m => m.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Calculate average advantage per category
        foreach (var (category, metrics) in byCategory)
        {
            var validMetrics = metrics.Where(m => m.AdvantageRatio > 0).ToList();
            if (validMetrics.Count > 0)
            {
                var product = validMetrics.Aggregate(1.0, (acc, m) => acc * m.AdvantageRatio);
                var geoMean = Math.Pow(product, 1.0 / validMetrics.Count);
                summary.CategoryAdvantages[category] = Math.Round(geoMean, 2);
            }
        }

        // Calculate overall advantage
        if (summary.CategoryAdvantages.Count > 0)
        {
            var product = summary.CategoryAdvantages.Values.Aggregate(1.0, (acc, v) => acc * v);
            summary.OverallCalorAdvantage = Math.Round(
                Math.Pow(product, 1.0 / summary.CategoryAdvantages.Count), 2);
        }

        summary.CalorPassCount = caseResults.Count(c => c.CalorSuccess);
        summary.CSharpPassCount = caseResults.Count(c => c.CSharpSuccess);

        summary.TopCalorCategories = summary.CategoryAdvantages
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

    private static double NormalizeScore(double score, double otherScore, string category)
    {
        // For token economics, lower is better, so invert
        if (category == "TokenEconomics" || category == "EditPrecision")
        {
            var maxScore = Math.Max(score, otherScore);
            return maxScore > 0 ? 1.0 - (score / maxScore) : 0.5;
        }

        // For other categories, higher is better
        var max = Math.Max(score, otherScore);
        return max > 0 ? score / max : 0.5;
    }

    private static double CalculateGeometricMean(IEnumerable<double> values)
    {
        var list = values.Where(v => v > 0).ToList();
        if (list.Count == 0)
            return 1.0;

        var product = list.Aggregate(1.0, (acc, v) => acc * v);
        return Math.Pow(product, 1.0 / list.Count);
    }
}

/// <summary>
/// Result of running the full 7-metric benchmark.
/// </summary>
public sealed class FullBenchmarkResult
{
    /// <summary>
    /// Result for a single benchmark case.
    /// </summary>
    public BenchmarkCaseResult? CaseResult { get; init; }

    /// <summary>
    /// Summary statistics.
    /// </summary>
    public EvaluationSummary Summary { get; init; } = new();

    /// <summary>
    /// Results for all files in a project benchmark.
    /// </summary>
    public List<BenchmarkCaseResult>? ProjectResults { get; init; }
}

/// <summary>
/// Result of a benchmark comparison (legacy).
/// </summary>
public sealed class BenchmarkResult
{
    public required FileMetrics Metrics { get; init; }
    public required double AdvantageRatio { get; init; }
    public required string Summary { get; init; }
}
