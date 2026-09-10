using System.Text;
using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Reports;

/// <summary>
/// Generates human-readable Markdown reports from evaluation results.
/// </summary>
public class MarkdownReportGenerator
{
    /// <summary>
    /// Generates a complete Markdown report.
    /// </summary>
    public string Generate(EvaluationResult result)
    {
        var sb = new StringBuilder();

        WriteHeader(sb, result);
        WriteSummary(sb, result);
        WriteCategoryBreakdown(sb, result);
        WriteDetailedResults(sb, result);
        WriteInterpretationLimits(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Generates a summary-only Markdown report.
    /// </summary>
    public string GenerateSummary(EvaluationResult result)
    {
        var sb = new StringBuilder();

        WriteHeader(sb, result);
        WriteSummary(sb, result);

        return sb.ToString();
    }

    /// <summary>
    /// Saves the report to a file.
    /// </summary>
    public async Task SaveAsync(EvaluationResult result, string path)
    {
        var markdown = Generate(result);
        await File.WriteAllTextAsync(path, markdown);
    }

    private static void WriteHeader(StringBuilder sb, EvaluationResult result)
    {
        sb.AppendLine("# Calor vs C# Evaluation Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {result.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Framework Version:** {result.Version}");
        sb.AppendLine($"**Benchmarks Evaluated:** {result.BenchmarkCount}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void WriteSummary(StringBuilder sb, EvaluationResult result)
    {
        var summary = result.Summary;
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();

        if (summary.CategoryAdvantages.Count > 0)
        {
            sb.AppendLine($"**Legacy Composite Direction-Normalized Ratio:** {summary.OverallCalorAdvantage:F2}x");
            sb.AppendLine("Each metric is normalized so values above 1 favor Calor, including lower-is-better metrics whose raw score ratio is inverted.");
            sb.AppendLine("This static calculator output is not a measured language, agent-productivity, correctness, or safety advantage.");
        }
        else
        {
            sb.AppendLine("**Comparable Direction-Normalized Ratios:** none");
            sb.AppendLine("All reported metrics are Calor-only and have no C# comparator.");
        }

        sb.AppendLine();

        sb.AppendLine("### Category Direction-Normalized Ratios");
        sb.AppendLine();
        sb.AppendLine("| Category | Direction-normalized ratio | Favored language |");
        sb.AppendLine("|----------|----------------------------|------------------|");

        var calorOnlyCategories = result.Metrics
            .Where(IsCalorOnly)
            .Select(metric => metric.Category)
            .ToHashSet();

        foreach (var (category, ratio) in summary.CategoryAdvantages.OrderByDescending(kv => kv.Value))
        {
            if (calorOnlyCategories.Contains(category))
            {
                sb.AppendLine($"| {category} | No comparison | Calor-only |");
                continue;
            }

            var favoredLanguage = ratio > 1.0 ? "Calor" : (ratio < 1.0 ? "C#" : "Tie");
            sb.AppendLine($"| {category} | {ratio:F2}x | {favoredLanguage} |");
        }
        foreach (var category in calorOnlyCategories.OrderBy(category => category))
        {
            sb.AppendLine($"| {category} | No comparison | Calor-only |");
        }

        sb.AppendLine();

        sb.AppendLine("### Recorded Success Flags");
        sb.AppendLine();
        sb.AppendLine($"- Calor inputs marked successful: {summary.CalorPassCount}");
        sb.AppendLine($"- C# inputs marked successful: {summary.CSharpPassCount}");
        sb.AppendLine("- These flags do not by themselves establish build success or runtime correctness.");
        sb.AppendLine();
    }

    private static void WriteCategoryBreakdown(StringBuilder sb, EvaluationResult result)
    {
        sb.AppendLine("## Category Breakdown");
        sb.AppendLine();

        var byCategory = result.Metrics
            .GroupBy(m => m.Category)
            .OrderByDescending(g => g.Average(m => m.AdvantageRatio));

        foreach (var category in byCategory)
        {
            var isCalorOnly = category.Any(IsCalorOnly);
            var avgAdvantage = category.Average(m => m.AdvantageRatio);
            var calorWins = category.Count(m => m.AdvantageRatio > 1.0);
            var csharpWins = category.Count(m => m.AdvantageRatio < 1.0);

            sb.AppendLine($"### {category.Key}");
            sb.AppendLine();
            if (isCalorOnly)
            {
                sb.AppendLine("**Calor-only metric:** no C# comparison");
            }
            else
            {
                sb.AppendLine($"**Average Direction-Normalized Ratio:** {avgAdvantage:F2}x");
                sb.AppendLine($"**Calor-favoring:** {calorWins} | **C#-favoring:** {csharpWins}");
            }
            sb.AppendLine();

            // Top metrics
            var topMetrics = category.OrderByDescending(m => m.AdvantageRatio).Take(5);
            sb.AppendLine("| Metric | Calor raw score | C# raw score | Direction-normalized ratio |");
            sb.AppendLine("|--------|-----------------|--------------|----------------------------|");

            foreach (var metric in topMetrics)
            {
                var ratio = IsCalorOnly(metric) ? "No comparison" : $"{metric.AdvantageRatio:F2}x";
                sb.AppendLine($"| {metric.MetricName} | {metric.CalorScore:F2} | {metric.CSharpScore:F2} | {ratio} |");
            }

            sb.AppendLine();
        }
    }

    private static void WriteDetailedResults(StringBuilder sb, EvaluationResult result)
    {
        if (result.CaseResults.Count == 0)
            return;

        sb.AppendLine("## Detailed Results by Benchmark");
        sb.AppendLine();

        // Group by level
        var byLevel = result.CaseResults.GroupBy(c => c.Level).OrderBy(g => g.Key);

        foreach (var level in byLevel)
        {
            sb.AppendLine($"### Level {level.Key}");
            sb.AppendLine();
            sb.AppendLine("| Benchmark | Calor success flag | C# success flag | Average Direction-Normalized Ratio |");
            sb.AppendLine("|-----------|--------------------|-----------------|------------------------------------|");

            foreach (var caseResult in level.OrderByDescending(c => c.AverageAdvantage))
            {
                var calorOk = caseResult.CalorSuccess ? "Yes" : "No";
                var csharpOk = caseResult.CSharpSuccess ? "Yes" : "No";
                var comparable = caseResult.Metrics.Where(metric => !IsCalorOnly(metric)).ToList();
                var ratio = comparable.Count == 0
                    ? "No comparison"
                    : $"{comparable.Average(metric => metric.AdvantageRatio):F2}x";
                sb.AppendLine($"| {caseResult.FileName} | {calorOk} | {csharpOk} | {ratio} |");
            }

            sb.AppendLine();
        }
    }

    private static void WriteInterpretationLimits(StringBuilder sb)
    {
        sb.AppendLine("## Interpretation Limits");
        sb.AppendLine();
        sb.AppendLine("- Direction normalization describes which side a metric's rule favors; it is not a raw Calor/C# score ratio.");
        sb.AppendLine("- Static calculator outputs are not observed coding-agent outcomes.");
        sb.AppendLine("- Source pairs may not be behaviorally equivalent.");
        sb.AppendLine("- Success flags do not establish generated-code build success or runtime correctness.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*Report generated by Calor Evaluation Framework*");
    }

    private static bool IsCalorOnly(MetricResult metric) =>
        metric.Details.TryGetValue("isCalorOnly", out var value) && value is true;
}
