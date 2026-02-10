using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Reports;

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
            Summary = MapSummary(result.Summary),
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
                overallCalorAdvantage = result.Summary.OverallCalorAdvantage,
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

    private static JsonSummary MapSummary(EvaluationSummary summary)
    {
        return new JsonSummary
        {
            OverallCalorAdvantage = summary.OverallCalorAdvantage,
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
                g =>
                {
                    var isCalorOnly = g.Any(m => m.Details.TryGetValue("isCalorOnly", out var v) && v is bool b && b);
                    return new JsonCategoryResult
                    {
                        MetricCount = g.Count(),
                        AverageAdvantage = g.Average(m => m.AdvantageRatio),
                        CalorWins = g.Count(m => m.AdvantageRatio > 1.0),
                        CSharpWins = g.Count(m => m.AdvantageRatio < 1.0),
                        Ties = g.Count(m => Math.Abs(m.AdvantageRatio - 1.0) < 0.01),
                        IsCalorOnly = isCalorOnly,
                        AverageScore = isCalorOnly ? g.Average(m => m.CalorScore) * 100 : null,
                        Metrics = g.Select(m => new JsonMetric
                        {
                            Name = m.MetricName,
                            CalorScore = m.CalorScore,
                            CSharpScore = m.CSharpScore,
                            AdvantageRatio = m.AdvantageRatio,
                            AdvantagePercent = Math.Round(m.AdvantagePercentage, 1),
                            IsCalorOnly = m.Details.TryGetValue("isCalorOnly", out var v) && v is bool b && b
                        }).ToList()
                    };
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
            AverageAdvantage = Math.Round(c.AverageAdvantage, 2),
            MetricCount = c.Metrics.Count
        }).ToList();
    }
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
    public double OverallCalorAdvantage { get; set; }
    public Dictionary<string, double> CategoryAdvantages { get; set; } = new();
    public int CalorPassCount { get; set; }
    public int CSharpPassCount { get; set; }
    public List<string> TopCalorCategories { get; set; } = new();
    public List<string> CSharpAdvantageCategories { get; set; } = new();
}

internal class JsonCategoryResult
{
    public int MetricCount { get; set; }
    public double AverageAdvantage { get; set; }
    public int CalorWins { get; set; }
    public int CSharpWins { get; set; }
    public int Ties { get; set; }
    public bool IsCalorOnly { get; set; }
    public double? AverageScore { get; set; }
    public List<JsonMetric> Metrics { get; set; } = new();
}

internal class JsonMetric
{
    public string Name { get; set; } = "";
    public double CalorScore { get; set; }
    public double CSharpScore { get; set; }
    public double AdvantageRatio { get; set; }
    public double AdvantagePercent { get; set; }
    public bool IsCalorOnly { get; set; }
}

internal class JsonCaseResult
{
    public string CaseId { get; set; } = "";
    public string FileName { get; set; } = "";
    public int Level { get; set; }
    public List<string> Features { get; set; } = new();
    public bool CalorSuccess { get; set; }
    public bool CSharpSuccess { get; set; }
    public double AverageAdvantage { get; set; }
    public int MetricCount { get; set; }
}
