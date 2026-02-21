using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Evaluation.Scorecard;

public static class ScorecardReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string GenerateJson(ConversionScorecard scorecard)
    {
        return JsonSerializer.Serialize(scorecard, JsonOptions);
    }

    public static string GenerateMarkdown(ConversionScorecard scorecard)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Calor Conversion Scorecard");
        sb.AppendLine();

        if (scorecard.CommitHash != null)
            sb.AppendLine($"**Commit:** `{scorecard.CommitHash}`");
        sb.AppendLine($"**Date:** {scorecard.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count | Rate |");
        sb.AppendLine("|--------|-------|------|");
        sb.AppendLine($"| Fully converted | {scorecard.FullyConverted}/{scorecard.Total} | {Pct(scorecard.FullyConverted, scorecard.Total)} |");
        sb.AppendLine($"| Partially converted | {scorecard.PartiallyConverted}/{scorecard.Total} | {Pct(scorecard.PartiallyConverted, scorecard.Total)} |");
        sb.AppendLine($"| Blocked | {scorecard.Blocked}/{scorecard.Total} | {Pct(scorecard.Blocked, scorecard.Total)} |");
        sb.AppendLine($"| Crashed | {scorecard.Crashed}/{scorecard.Total} | {Pct(scorecard.Crashed, scorecard.Total)} |");
        sb.AppendLine($"| **Round-trip verified** | **{scorecard.RoundTripPassing}/{scorecard.Total}** | **{Pct(scorecard.RoundTripPassing, scorecard.Total)}** |");
        sb.AppendLine();

        // By level
        sb.AppendLine("## By Level");
        sb.AppendLine();
        sb.AppendLine("| Level | Passing | Total | Rate |");
        sb.AppendLine("|-------|---------|-------|------|");
        foreach (var (level, breakdown) in scorecard.ByLevel.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"| Level {level} | {breakdown.Passed} | {breakdown.Total} | {breakdown.Rate:P0} |");
        }
        sb.AppendLine();

        // By feature (top failures)
        sb.AppendLine("## By C# Feature (lowest conversion rates)");
        sb.AppendLine();
        sb.AppendLine("| Feature | Passing | Total | Rate |");
        sb.AppendLine("|---------|---------|-------|------|");
        foreach (var (feature, breakdown) in scorecard.ByFeature
            .OrderBy(kv => kv.Value.Rate)
            .ThenByDescending(kv => kv.Value.Total)
            .Take(20))
        {
            sb.AppendLine($"| {feature} | {breakdown.Passed} | {breakdown.Total} | {breakdown.Rate:P0} |");
        }
        sb.AppendLine();

        // Failed snippets
        var failed = scorecard.Results
            .Where(r => !r.RoundTripSuccess)
            .OrderBy(r => r.Id)
            .ToList();

        if (failed.Count > 0)
        {
            sb.AppendLine("## Failed Snippets");
            sb.AppendLine();
            sb.AppendLine("| ID | File | Status | Top Issue |");
            sb.AppendLine("|----|------|--------|-----------|");
            foreach (var r in failed)
            {
                var topIssue = r.ConversionIssues.FirstOrDefault()
                    ?? r.CompilationDiagnostics.FirstOrDefault()
                    ?? "-";
                // Truncate long issues for table readability
                if (topIssue.Length > 80)
                    topIssue = topIssue[..77] + "...";
                // Escape pipe characters in issues
                topIssue = topIssue.Replace("|", "\\|");
                sb.AppendLine($"| {r.Id} | {r.FileName} | {r.Status} | {topIssue} |");
            }
        }

        return sb.ToString();
    }

    public static async Task SaveAsync(ConversionScorecard scorecard, string basePath, string format)
    {
        switch (format.ToLowerInvariant())
        {
            case "json":
                await File.WriteAllTextAsync(basePath + ".json", GenerateJson(scorecard));
                break;
            case "markdown":
            case "md":
                await File.WriteAllTextAsync(basePath + ".md", GenerateMarkdown(scorecard));
                break;
            case "both":
                await File.WriteAllTextAsync(basePath + ".json", GenerateJson(scorecard));
                await File.WriteAllTextAsync(basePath + ".md", GenerateMarkdown(scorecard));
                break;
            default:
                throw new ArgumentException($"Unknown format: {format}");
        }
    }

    private static string Pct(int count, int total)
        => total > 0 ? $"{(double)count / total:P0}" : "0%";
}
