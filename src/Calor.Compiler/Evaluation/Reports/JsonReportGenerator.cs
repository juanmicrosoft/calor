using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Reports;

/// <summary>
/// Generates JSON reports from evaluation results.
/// </summary>
public class JsonReportGenerator
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Generates a complete JSON report.
    /// </summary>
    public string Generate(EvaluationResult result)
    {
        var report = new JsonReport
        {
            Metadata = new ReportMetadata
            {
                GeneratedAt = result.Timestamp,
                Version = result.Version,
                BenchmarkCount = result.BenchmarkCount
            },
            Summary = MapSummary(result),
            CategoryResults = GroupByCategory(result),
            DetailedResults = MapDetailedResults(result)
        };

        return JsonSerializer.Serialize(report, DefaultOptions);
    }

    /// <summary>
    /// Generates a summary-only JSON report (smaller output).
    /// </summary>
    public string GenerateSummary(EvaluationResult result)
    {
        var report = new
        {
            summary = new
            {
                overallDirectionNormalizedRatio = result.Summary.CategoryAdvantages.Count > 0
                    ? result.Summary.OverallCalorAdvantage
                    : (double?)null,
                categoryDirectionNormalizedRatios = result.Summary.CategoryAdvantages,
                overallCalorAdvantage = result.Summary.CategoryAdvantages.Count > 0
                    ? result.Summary.OverallCalorAdvantage
                    : (double?)null,
                categoryAdvantages = result.Summary.CategoryAdvantages
            }
        };

        return JsonSerializer.Serialize(report, DefaultOptions);
    }

    /// <summary>
    /// Saves the report to a file.
    /// </summary>
    public async Task SaveAsync(EvaluationResult result, string path)
    {
        var json = Generate(result);
        await File.WriteAllTextAsync(path, json);
    }

    private static JsonSummary MapSummary(EvaluationResult result)
    {
        var summary = result.Summary;
        return new JsonSummary
        {
            OverallDirectionNormalizedRatio = summary.CategoryAdvantages.Count > 0
                ? summary.OverallCalorAdvantage
                : null,
            CategoryDirectionNormalizedRatios = summary.CategoryAdvantages,
            OverallCalorAdvantage = summary.CategoryAdvantages.Count > 0
                ? summary.OverallCalorAdvantage
                : null,
            CategoryAdvantages = summary.CategoryAdvantages,
            CalorPassCount = summary.CalorPassCount,
            CSharpPassCount = summary.CSharpPassCount,
            TopCalorCategories = summary.TopCalorCategories,
            CSharpAdvantageCategories = summary.CSharpAdvantageCategories
        };
    }

    private static Dictionary<string, JsonCategoryResult> GroupByCategory(EvaluationResult result)
    {
        return result.Metrics
            .GroupBy(m => m.Category)
            .ToDictionary(
                g => g.Key,
                g => new JsonCategoryResult
                {
                    MetricCount = g.Count(),
                    IsCalorOnly = g.All(IsCalorOnly),
                    AverageDirectionNormalizedRatio = g.All(IsCalorOnly)
                        ? null
                        : Math.Round(g.Where(m => !IsCalorOnly(m)).Average(m => m.AdvantageRatio), 2),
                    CalorFavoring = g.Count(m => !IsCalorOnly(m) && m.AdvantageRatio > 1.0),
                    CSharpFavoring = g.Count(m => !IsCalorOnly(m) && m.AdvantageRatio < 1.0),
                    Neutral = g.Count(m => !IsCalorOnly(m) && Math.Abs(m.AdvantageRatio - 1.0) < 0.01),
                    AverageAdvantage = g.All(IsCalorOnly)
                        ? null
                        : Math.Round(g.Where(m => !IsCalorOnly(m)).Average(m => m.AdvantageRatio), 2),
                    CalorWins = g.Count(m => !IsCalorOnly(m) && m.AdvantageRatio > 1.0),
                    CSharpWins = g.Count(m => !IsCalorOnly(m) && m.AdvantageRatio < 1.0),
                    Ties = g.Count(m => !IsCalorOnly(m) && Math.Abs(m.AdvantageRatio - 1.0) < 0.01),
                    Metrics = g.Select(m => new JsonMetric
                    {
                        Name = m.MetricName,
                        CalorScore = Math.Round(m.CalorScore, 2),
                        CSharpScore = Math.Round(m.CSharpScore, 2),
                        IsCalorOnly = IsCalorOnly(m),
                        DirectionNormalizedRatio = IsCalorOnly(m)
                            ? null
                            : Math.Round(m.AdvantageRatio, 2),
                        AdvantageRatio = IsCalorOnly(m)
                            ? null
                            : Math.Round(m.AdvantageRatio, 2),
                        AdvantagePercent = IsCalorOnly(m)
                            ? null
                            : Math.Round(m.AdvantagePercentage, 1)
                    }).ToList()
                });
    }

    private static List<JsonCaseResult> MapDetailedResults(EvaluationResult result)
    {
        return result.CaseResults.Select(c => new JsonCaseResult
        {
            CaseId = c.CaseId,
            FileName = c.FileName,
            Level = c.Level,
            Features = c.Features,
            CalorSuccess = c.CalorSuccess,
            CSharpSuccess = c.CSharpSuccess,
            AverageDirectionNormalizedRatio = c.Metrics.Any(m => !IsCalorOnly(m))
                ? Math.Round(c.Metrics.Where(m => !IsCalorOnly(m)).Average(m => m.AdvantageRatio), 2)
                : null,
            AverageAdvantage = c.Metrics.Any(m => !IsCalorOnly(m))
                ? Math.Round(c.Metrics.Where(m => !IsCalorOnly(m)).Average(m => m.AdvantageRatio), 2)
                : null,
            MetricCount = c.Metrics.Count
        }).ToList();
    }

    private static bool IsCalorOnly(MetricResult metric) =>
        metric.Details.TryGetValue("isCalorOnly", out var value) && value is true;
}

// JSON structure classes

internal class JsonReport
{
    public required ReportMetadata Metadata { get; set; }
    public required JsonSummary Summary { get; set; }
    public required Dictionary<string, JsonCategoryResult> CategoryResults { get; set; }
    public required List<JsonCaseResult> DetailedResults { get; set; }
}

internal class ReportMetadata
{
    public DateTime GeneratedAt { get; set; }
    public string Version { get; set; } = "";
    public int BenchmarkCount { get; set; }
}

internal class JsonSummary
{
    public double? OverallDirectionNormalizedRatio { get; set; }
    public Dictionary<string, double> CategoryDirectionNormalizedRatios { get; set; } = new();
    public double? OverallCalorAdvantage { get; set; }
    public Dictionary<string, double> CategoryAdvantages { get; set; } = new();
    public int CalorPassCount { get; set; }
    public int CSharpPassCount { get; set; }
    public List<string> TopCalorCategories { get; set; } = new();
    public List<string> CSharpAdvantageCategories { get; set; } = new();
}

internal class JsonCategoryResult
{
    public int MetricCount { get; set; }
    public bool IsCalorOnly { get; set; }
    public double? AverageDirectionNormalizedRatio { get; set; }
    public int CalorFavoring { get; set; }
    public int CSharpFavoring { get; set; }
    public int Neutral { get; set; }
    public double? AverageAdvantage { get; set; }
    public int CalorWins { get; set; }
    public int CSharpWins { get; set; }
    public int Ties { get; set; }
    public List<JsonMetric> Metrics { get; set; } = new();
}

internal class JsonMetric
{
    public string Name { get; set; } = "";
    public double CalorScore { get; set; }
    public double CSharpScore { get; set; }
    public bool IsCalorOnly { get; set; }
    public double? DirectionNormalizedRatio { get; set; }
    public double? AdvantageRatio { get; set; }
    public double? AdvantagePercent { get; set; }
}

internal class JsonCaseResult
{
    public string CaseId { get; set; } = "";
    public string FileName { get; set; } = "";
    public int Level { get; set; }
    public List<string> Features { get; set; } = new();
    public bool CalorSuccess { get; set; }
    public bool CSharpSuccess { get; set; }
    public double? AverageDirectionNormalizedRatio { get; set; }
    public double? AverageAdvantage { get; set; }
    public int MetricCount { get; set; }
}
