using System.Text;
using System.Text.Json;

namespace Calor.Compiler.Migration;

/// <summary>
/// Generates migration reports in various formats.
/// </summary>
public sealed class MigrationReportGenerator
{
    private readonly MigrationReport _report;

    public MigrationReportGenerator(MigrationReport report)
    {
        _report = report;
    }

    /// <summary>
    /// Generates a Markdown report.
    /// </summary>
    public string GenerateMarkdown()
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("# Migration Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {_report.GeneratedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"**Direction:** {FormatDirection(_report.Direction)}");
        sb.AppendLine($"**Report ID:** `{_report.ReportId}`");
        sb.AppendLine();

        // Summary
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Total Files | {_report.Summary.TotalFiles} |");
        sb.AppendLine($"| Successful | {_report.Summary.SuccessfulFiles} |");
        sb.AppendLine($"| Partial | {_report.Summary.PartialFiles} |");
        sb.AppendLine($"| Failed | {_report.Summary.FailedFiles} |");
        sb.AppendLine($"| Success Rate | {_report.Summary.SuccessRate:F1}% |");
        sb.AppendLine($"| Total Errors | {_report.Summary.TotalErrors} |");
        sb.AppendLine($"| Total Warnings | {_report.Summary.TotalWarnings} |");
        sb.AppendLine($"| Duration | {FormatDuration(_report.Summary.TotalDuration)} |");
        sb.AppendLine();

        // Benchmark results
        if (_report.Benchmark != null)
        {
            sb.AppendLine("## Benchmark Results");
            sb.AppendLine();
            sb.AppendLine("### Token Economics");
            sb.AppendLine();
            sb.AppendLine("| Metric | Before | After | Savings |");
            sb.AppendLine("|--------|--------|-------|---------|");
            sb.AppendLine($"| Tokens | {_report.Benchmark.TotalOriginalTokens:N0} | {_report.Benchmark.TotalOutputTokens:N0} | {_report.Benchmark.TokenSavingsPercent:F1}% |");
            sb.AppendLine($"| Lines | {_report.Benchmark.TotalOriginalLines:N0} | {_report.Benchmark.TotalOutputLines:N0} | {_report.Benchmark.LineSavingsPercent:F1}% |");
            sb.AppendLine();
            sb.AppendLine($"**Overall Calor Advantage:** {_report.Benchmark.OverallAdvantage:F2}x");
            sb.AppendLine();

            if (_report.Benchmark.CategoryAdvantages.Count > 0)
            {
                sb.AppendLine("### Category Advantages");
                sb.AppendLine();
                foreach (var (category, advantage) in _report.Benchmark.CategoryAdvantages.OrderByDescending(kv => kv.Value))
                {
                    var indicator = advantage > 1 ? "+" : "";
                    sb.AppendLine($"- **{category}:** {indicator}{(advantage - 1) * 100:F1}%");
                }
                sb.AppendLine();
            }
        }

        // File results
        if (_report.FileResults.Count > 0)
        {
            sb.AppendLine("## File Results");
            sb.AppendLine();

            var successFiles = _report.FileResults.Where(f => f.Status == FileMigrationStatus.Success).ToList();
            var partialFiles = _report.FileResults.Where(f => f.Status == FileMigrationStatus.Partial).ToList();
            var failedFiles = _report.FileResults.Where(f => f.Status == FileMigrationStatus.Failed).ToList();

            if (successFiles.Count > 0)
            {
                sb.AppendLine("### Successful");
                sb.AppendLine();
                foreach (var file in successFiles)
                {
                    sb.AppendLine($"- `{Path.GetFileName(file.SourcePath)}` → `{Path.GetFileName(file.OutputPath ?? "")}`");
                    if (file.Metrics != null)
                    {
                        sb.AppendLine($"  - Lines: {file.Metrics.OriginalLines} → {file.Metrics.OutputLines} ({file.Metrics.LineReduction:F1}% reduction)");
                    }
                }
                sb.AppendLine();
            }

            if (partialFiles.Count > 0)
            {
                sb.AppendLine("### Partial (Needs Review)");
                sb.AppendLine();
                foreach (var file in partialFiles)
                {
                    sb.AppendLine($"- `{Path.GetFileName(file.SourcePath)}`");
                    foreach (var issue in file.Issues.Where(i => i.Severity == ConversionIssueSeverity.Warning))
                    {
                        sb.AppendLine($"  - ⚠️ {issue.Message}");
                    }
                }
                sb.AppendLine();
            }

            if (failedFiles.Count > 0)
            {
                sb.AppendLine("### Failed");
                sb.AppendLine();
                foreach (var file in failedFiles)
                {
                    sb.AppendLine($"- `{Path.GetFileName(file.SourcePath)}`");
                    foreach (var issue in file.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error))
                    {
                        sb.AppendLine($"  - ❌ {issue.Message}");
                    }
                }
                sb.AppendLine();
            }
        }

        // Common issues
        if (_report.Summary.MostCommonIssues.Count > 0)
        {
            sb.AppendLine("## Common Issues");
            sb.AppendLine();
            foreach (var issue in _report.Summary.MostCommonIssues.Take(10))
            {
                sb.AppendLine($"- {issue}");
            }
            sb.AppendLine();
        }

        // Unsupported features
        if (_report.Summary.UnsupportedFeatures.Count > 0)
        {
            sb.AppendLine("## Unsupported Features");
            sb.AppendLine();
            sb.AppendLine("The following C# features were encountered but are not fully supported:");
            sb.AppendLine();
            foreach (var feature in _report.Summary.UnsupportedFeatures)
            {
                var info = FeatureSupport.GetFeatureInfo(feature);
                var workaround = info?.Workaround ?? "Consider manual conversion";
                sb.AppendLine($"- **{feature}**: {workaround}");
            }
            sb.AppendLine();
        }

        // Recommendations
        if (_report.Recommendations.Count > 0)
        {
            sb.AppendLine("## Recommendations");
            sb.AppendLine();
            foreach (var rec in _report.Recommendations)
            {
                sb.AppendLine($"1. {rec}");
            }
            sb.AppendLine();
        }

        // Footer
        sb.AppendLine("---");
        sb.AppendLine("*Generated by Calor Migration Tool*");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a JSON report.
    /// </summary>
    public string GenerateJson()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(_report, options);
    }

    /// <summary>
    /// Generates a concise console summary.
    /// </summary>
    public string GenerateConsoleSummary()
    {
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine($"Migration {(_report.Summary.SuccessRate >= 100 ? "✓ Complete" : _report.Summary.SuccessRate > 0 ? "⚠ Partial" : "✗ Failed")}");
        sb.AppendLine();
        sb.AppendLine($"  Files: {_report.Summary.SuccessfulFiles}/{_report.Summary.TotalFiles} successful");

        if (_report.Summary.PartialFiles > 0)
            sb.AppendLine($"         {_report.Summary.PartialFiles} need review");
        if (_report.Summary.FailedFiles > 0)
            sb.AppendLine($"         {_report.Summary.FailedFiles} failed");

        if (_report.Benchmark != null)
        {
            sb.AppendLine();
            sb.AppendLine("Token Economics:");
            sb.AppendLine($"  Before: {_report.Benchmark.TotalOriginalTokens:N0} tokens");
            sb.AppendLine($"  After:  {_report.Benchmark.TotalOutputTokens:N0} tokens");
            sb.AppendLine($"  Savings: {_report.Benchmark.TokenSavingsPercent:F1}%");
        }

        if (_report.Summary.TotalErrors > 0 || _report.Summary.TotalWarnings > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Issues: {_report.Summary.TotalErrors} error(s), {_report.Summary.TotalWarnings} warning(s)");
        }

        sb.AppendLine();
        sb.AppendLine($"Duration: {FormatDuration(_report.Summary.TotalDuration)}");

        return sb.ToString();
    }

    /// <summary>
    /// Saves the report to a file.
    /// </summary>
    public async Task SaveAsync(string path, ReportFormat format = ReportFormat.Markdown)
    {
        var content = format switch
        {
            ReportFormat.Markdown => GenerateMarkdown(),
            ReportFormat.Json => GenerateJson(),
            _ => GenerateMarkdown()
        };

        await File.WriteAllTextAsync(path, content);
    }

    private static string FormatDirection(MigrationDirection direction)
    {
        return direction switch
        {
            MigrationDirection.CSharpToCalor => "C# → Calor",
            MigrationDirection.CalorToCSharp => "Calor → C#",
            _ => "Unknown"
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1)
            return $"{duration.TotalMilliseconds:F0}ms";
        if (duration.TotalMinutes < 1)
            return $"{duration.TotalSeconds:F1}s";
        return $"{duration.TotalMinutes:F1}m";
    }
}

/// <summary>
/// Report output format.
/// </summary>
public enum ReportFormat
{
    Markdown,
    Json
}

/// <summary>
/// Builder for creating migration reports.
/// </summary>
public sealed class MigrationReportBuilder
{
    private readonly List<FileMigrationResult> _fileResults = new();
    private readonly List<string> _recommendations = new();
    private MigrationDirection _direction = MigrationDirection.CSharpToCalor;
    private bool _includeBenchmark;

    public MigrationReportBuilder SetDirection(MigrationDirection direction)
    {
        _direction = direction;
        return this;
    }

    public MigrationReportBuilder IncludeBenchmark(bool include = true)
    {
        _includeBenchmark = include;
        return this;
    }

    public MigrationReportBuilder AddFileResult(FileMigrationResult result)
    {
        _fileResults.Add(result);
        return this;
    }

    public MigrationReportBuilder AddRecommendation(string recommendation)
    {
        _recommendations.Add(recommendation);
        return this;
    }

    public MigrationReport Build()
    {
        var summary = BuildSummary();
        var benchmark = _includeBenchmark ? BuildBenchmark() : null;

        return new MigrationReport
        {
            ReportId = Guid.NewGuid().ToString("N")[..8],
            GeneratedAt = DateTime.UtcNow,
            Direction = _direction,
            Summary = summary,
            FileResults = _fileResults.ToList(),
            Benchmark = benchmark,
            Recommendations = _recommendations.ToList()
        };
    }

    private MigrationSummary BuildSummary()
    {
        var allIssues = _fileResults.SelectMany(f => f.Issues).ToList();

        var issueGroups = allIssues
            .GroupBy(i => i.Message)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => $"{g.Key} ({g.Count()}x)")
            .ToList();

        var unsupportedFeatures = allIssues
            .Where(i => i.Feature != null && !FeatureSupport.IsFullySupported(i.Feature))
            .Select(i => i.Feature!)
            .Distinct()
            .ToList();

        return new MigrationSummary
        {
            TotalFiles = _fileResults.Count,
            SuccessfulFiles = _fileResults.Count(f => f.Status == FileMigrationStatus.Success),
            PartialFiles = _fileResults.Count(f => f.Status == FileMigrationStatus.Partial),
            FailedFiles = _fileResults.Count(f => f.Status == FileMigrationStatus.Failed),
            TotalErrors = allIssues.Count(i => i.Severity == ConversionIssueSeverity.Error),
            TotalWarnings = allIssues.Count(i => i.Severity == ConversionIssueSeverity.Warning),
            TotalDuration = TimeSpan.FromTicks(_fileResults.Sum(f => f.Duration.Ticks)),
            MostCommonIssues = issueGroups,
            UnsupportedFeatures = unsupportedFeatures
        };
    }

    private BenchmarkSummary BuildBenchmark()
    {
        var filesWithMetrics = _fileResults.Where(f => f.Metrics != null).ToList();

        return new BenchmarkSummary
        {
            TotalOriginalTokens = filesWithMetrics.Sum(f => f.Metrics!.OriginalTokens),
            TotalOutputTokens = filesWithMetrics.Sum(f => f.Metrics!.OutputTokens),
            TotalOriginalLines = filesWithMetrics.Sum(f => f.Metrics!.OriginalLines),
            TotalOutputLines = filesWithMetrics.Sum(f => f.Metrics!.OutputLines)
        };
    }
}
