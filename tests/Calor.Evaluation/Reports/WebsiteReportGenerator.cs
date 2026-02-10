using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Reports;

/// <summary>
/// Generates benchmark results in the format expected by the website dashboard.
/// Output is written to website/public/data/benchmark-results.json
/// </summary>
public class WebsiteReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Generates the website benchmark results JSON.
    /// </summary>
    public WebsiteBenchmarkResults Generate(EvaluationResult result)
    {
        return new WebsiteBenchmarkResults
        {
            Version = "1.0",
            Timestamp = result.Timestamp,
            Commit = result.CommitHash,
            FrameworkVersion = result.Version,
            Summary = GenerateSummary(result),
            Metrics = GenerateMetricDetails(result),
            LlmEvaluation = null, // Will be populated when LLM eval is run
            Programs = GenerateProgramDetails(result),
            StatisticalAnalysis = result.HasStatisticalAnalysis
                ? GenerateStatisticalDetails(result)
                : null
        };
    }

    /// <summary>
    /// Saves the results to the website data directory.
    /// </summary>
    public async Task SaveAsync(EvaluationResult result, string outputPath)
    {
        var websiteResult = Generate(result);
        var json = JsonSerializer.Serialize(websiteResult, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json);
    }

    private static WebsiteSummary GenerateSummary(EvaluationResult result)
    {
        return new WebsiteSummary
        {
            OverallAdvantage = Math.Round(result.Summary.OverallCalorAdvantage, 3),
            ProgramCount = result.BenchmarkCount,
            MetricCount = result.Summary.CategoryAdvantages.Count,
            CalorWins = result.Summary.TopCalorCategories.Count,
            CSharpWins = result.Summary.CSharpAdvantageCategories.Count,
            StatisticalRunCount = result.StatisticalRunCount
        };
    }

    private static Dictionary<string, WebsiteMetricResult> GenerateMetricDetails(EvaluationResult result)
    {
        var metrics = new Dictionary<string, WebsiteMetricResult>();

        // Check which categories are Calor-only by looking at metric details
        var calorOnlyCategories = new HashSet<string>();
        foreach (var caseResult in result.CaseResults)
        {
            foreach (var metric in caseResult.Metrics)
            {
                if (metric.Details.TryGetValue("isCalorOnly", out var isCalorOnly) &&
                    isCalorOnly is true)
                {
                    calorOnlyCategories.Add(metric.Category);
                }
            }
        }

        foreach (var (category, advantage) in result.Summary.CategoryAdvantages)
        {
            var isCalorOnly = calorOnlyCategories.Contains(category);
            // Calor-only metrics always show Calor as winner
            var winner = isCalorOnly ? "calor" :
                (advantage > 1.0 ? "calor" : (advantage < 1.0 ? "csharp" : "tie"));

            double[]? ci95 = null;
            if (result.Summary.CategoryConfidenceIntervals.TryGetValue(category, out var ci))
            {
                ci95 = new[] { Math.Round(ci.Lower, 3), Math.Round(ci.Upper, 3) };
            }

            // Get statistical significance if available
            bool? significant = null;
            double? pValue = null;
            double? effectSize = null;
            string? effectInterpretation = null;

            var statSummary = result.StatisticalSummaries
                .FirstOrDefault(s => s.Category == category);

            if (statSummary != null)
            {
                significant = statSummary.TTest?.IsSignificant;
                pValue = statSummary.TTest?.PValue;
                effectSize = Math.Round(statSummary.CohensD, 3);
                effectInterpretation = statSummary.EffectSizeInterpretation;
            }

            metrics[category] = new WebsiteMetricResult
            {
                Ratio = Math.Round(advantage, 3),
                Winner = winner,
                Ci95 = ci95,
                Significant = significant,
                PValue = pValue.HasValue ? Math.Round(pValue.Value, 4) : null,
                EffectSize = effectSize,
                EffectInterpretation = effectInterpretation,
                IsCalorOnly = isCalorOnly ? true : null
            };
        }

        return metrics;
    }

    private static List<WebsiteProgramResult> GenerateProgramDetails(EvaluationResult result)
    {
        return result.CaseResults.Select(c => new WebsiteProgramResult
        {
            Id = c.CaseId,
            Name = c.FileName,
            Level = c.Level,
            Features = c.Features,
            CalorSuccess = c.CalorSuccess,
            CSharpSuccess = c.CSharpSuccess,
            Advantage = Math.Round(c.AverageAdvantage, 3),
            Metrics = c.Metrics.ToDictionary(
                m => m.Category,
                m => Math.Round(m.AdvantageRatio, 3))
        }).ToList();
    }

    private static WebsiteStatisticalAnalysis GenerateStatisticalDetails(EvaluationResult result)
    {
        return new WebsiteStatisticalAnalysis
        {
            RunCount = result.StatisticalRunCount,
            ConfidenceLevel = 0.95,
            SignificantCategories = result.StatisticalSummaries
                .Where(s => s.TTest?.IsSignificant == true)
                .Select(s => s.Category)
                .ToList(),
            CategoryDetails = result.StatisticalSummaries.ToDictionary(
                s => s.Category,
                s => new WebsiteCategoryStatistics
                {
                    Mean = Math.Round(s.AdvantageRatioMean, 3),
                    StdDev = Math.Round(StatisticalAnalysis.StandardDeviation(
                        new[] { s.CalorStdDev, s.CSharpStdDev }), 3),
                    Ci95Lower = Math.Round(s.AdvantageRatioCI.Lower, 3),
                    Ci95Upper = Math.Round(s.AdvantageRatioCI.Upper, 3),
                    CohensD = Math.Round(s.CohensD, 3),
                    PValue = s.TTest?.PValue != null ? Math.Round(s.TTest.PValue, 4) : null,
                    Significant = s.TTest?.IsSignificant ?? false
                })
        };
    }
}

#region Website JSON Schema

/// <summary>
/// Root structure for website benchmark results.
/// </summary>
public class WebsiteBenchmarkResults
{
    public required string Version { get; init; }
    public DateTime Timestamp { get; init; }
    public string? Commit { get; init; }
    public string? FrameworkVersion { get; init; }
    public required WebsiteSummary Summary { get; init; }
    public required Dictionary<string, WebsiteMetricResult> Metrics { get; init; }
    public WebsiteLlmEvaluation? LlmEvaluation { get; init; }
    public required List<WebsiteProgramResult> Programs { get; init; }
    public WebsiteStatisticalAnalysis? StatisticalAnalysis { get; init; }
}

/// <summary>
/// Summary statistics for the dashboard header.
/// </summary>
public class WebsiteSummary
{
    public double OverallAdvantage { get; init; }
    public int ProgramCount { get; init; }
    public int MetricCount { get; init; }
    public int CalorWins { get; init; }
    public int CSharpWins { get; init; }
    public int StatisticalRunCount { get; init; }
}

/// <summary>
/// Per-metric results for the dashboard.
/// </summary>
public class WebsiteMetricResult
{
    public double Ratio { get; init; }
    public required string Winner { get; init; }
    public double[]? Ci95 { get; init; }
    public bool? Significant { get; init; }
    public double? PValue { get; init; }
    public double? EffectSize { get; init; }
    public string? EffectInterpretation { get; init; }
    public bool? IsCalorOnly { get; init; }
}

/// <summary>
/// LLM evaluation results (Claude, GPT-4, etc.)
/// </summary>
public class WebsiteLlmEvaluation
{
    public WebsiteLlmScore? Claude { get; init; }
    public WebsiteLlmScore? Gpt4 { get; init; }
    public WebsiteLlmScore? Gemini { get; init; }
    public double? CrossModelAgreement { get; init; }
}

/// <summary>
/// Scores from a single LLM evaluation.
/// </summary>
public class WebsiteLlmScore
{
    public double CalorScore { get; init; }
    public double CSharpScore { get; init; }
    public int QuestionsAnswered { get; init; }
    public double TokensUsed { get; init; }
}

/// <summary>
/// Per-program results for detailed view.
/// </summary>
public class WebsiteProgramResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Level { get; init; }
    public List<string> Features { get; init; } = new();
    public bool CalorSuccess { get; init; }
    public bool CSharpSuccess { get; init; }
    public double Advantage { get; init; }
    public Dictionary<string, double> Metrics { get; init; } = new();
}

/// <summary>
/// Statistical analysis details.
/// </summary>
public class WebsiteStatisticalAnalysis
{
    public int RunCount { get; init; }
    public double ConfidenceLevel { get; init; }
    public List<string> SignificantCategories { get; init; } = new();
    public Dictionary<string, WebsiteCategoryStatistics> CategoryDetails { get; init; } = new();
}

/// <summary>
/// Statistical details for a single category.
/// </summary>
public class WebsiteCategoryStatistics
{
    public double Mean { get; init; }
    public double StdDev { get; init; }
    public double Ci95Lower { get; init; }
    public double Ci95Upper { get; init; }
    public double CohensD { get; init; }
    public double? PValue { get; init; }
    public bool Significant { get; init; }
}

#endregion
