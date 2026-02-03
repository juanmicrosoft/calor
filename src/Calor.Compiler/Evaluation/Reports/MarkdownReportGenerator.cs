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
        WriteSummary(sb, result.Summary);
        WriteCategoryBreakdown(sb, result);
        WriteDetailedResults(sb, result);
        WriteConclusions(sb, result);

        return sb.ToString();
    }

    /// <summary>
    /// Generates a summary-only Markdown report.
    /// </summary>
    public string GenerateSummary(EvaluationResult result)
    {
        var sb = new StringBuilder();

        WriteHeader(sb, result);
        WriteSummary(sb, result.Summary);

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

    private static void WriteSummary(StringBuilder sb, EvaluationSummary summary)
    {
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();

        // Overall result
        var overallAdvantage = summary.OverallCalorAdvantage;
        var advantagePercent = (overallAdvantage - 1.0) * 100;
        var winner = overallAdvantage > 1.0 ? "Calor" : (overallAdvantage < 1.0 ? "C#" : "Neither");

        sb.AppendLine($"**Overall Winner:** {winner}");
        sb.AppendLine($"**Overall Advantage Ratio:** {overallAdvantage:F2}x");

        if (overallAdvantage != 1.0)
        {
            sb.AppendLine($"**Advantage Percentage:** {Math.Abs(advantagePercent):F1}% in favor of {winner}");
        }

        sb.AppendLine();

        // Category breakdown table
        sb.AppendLine("### Category Advantages");
        sb.AppendLine();
        sb.AppendLine("| Category | Advantage Ratio | Winner |");
        sb.AppendLine("|----------|-----------------|--------|");

        foreach (var (category, ratio) in summary.CategoryAdvantages.OrderByDescending(kv => kv.Value))
        {
            var catWinner = ratio > 1.0 ? "Calor" : (ratio < 1.0 ? "C#" : "Tie");
            var indicator = ratio > 1.2 ? "+" : (ratio < 0.8 ? "-" : "=");
            sb.AppendLine($"| {category} | {ratio:F2}x | {indicator} {catWinner} |");
        }

        sb.AppendLine();

        // Pass counts
        sb.AppendLine("### Compilation Success");
        sb.AppendLine();
        sb.AppendLine($"- Calor passed: {summary.CalorPassCount}");
        sb.AppendLine($"- C# passed: {summary.CSharpPassCount}");
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
            var avgAdvantage = category.Average(m => m.AdvantageRatio);
            var calorWins = category.Count(m => m.AdvantageRatio > 1.0);
            var csharpWins = category.Count(m => m.AdvantageRatio < 1.0);

            sb.AppendLine($"### {category.Key}");
            sb.AppendLine();
            sb.AppendLine($"**Average Advantage:** {avgAdvantage:F2}x");
            sb.AppendLine($"**Calor wins:** {calorWins} | **C# wins:** {csharpWins}");
            sb.AppendLine();

            // Top metrics
            var topMetrics = category.OrderByDescending(m => m.AdvantageRatio).Take(5);
            sb.AppendLine("| Metric | Calor | C# | Ratio |");
            sb.AppendLine("|--------|------|-----|-------|");

            foreach (var metric in topMetrics)
            {
                sb.AppendLine($"| {metric.MetricName} | {metric.CalorScore:F2} | {metric.CSharpScore:F2} | {metric.AdvantageRatio:F2}x |");
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
            sb.AppendLine("| Benchmark | Calor OK | C# OK | Avg Advantage |");
            sb.AppendLine("|-----------|---------|-------|---------------|");

            foreach (var caseResult in level.OrderByDescending(c => c.AverageAdvantage))
            {
                var calorOk = caseResult.CalorSuccess ? "Yes" : "No";
                var csharpOk = caseResult.CSharpSuccess ? "Yes" : "No";
                sb.AppendLine($"| {caseResult.FileName} | {calorOk} | {csharpOk} | {caseResult.AverageAdvantage:F2}x |");
            }

            sb.AppendLine();
        }
    }

    private static void WriteConclusions(StringBuilder sb, EvaluationResult result)
    {
        sb.AppendLine("## Conclusions");
        sb.AppendLine();

        var summary = result.Summary;

        // Key findings
        sb.AppendLine("### Key Findings");
        sb.AppendLine();

        if (summary.TopCalorCategories.Count > 0)
        {
            sb.AppendLine($"1. **Calor excels in:** {string.Join(", ", summary.TopCalorCategories)}");
        }

        if (summary.CSharpAdvantageCategories.Count > 0)
        {
            sb.AppendLine($"2. **C# advantages:** {string.Join(", ", summary.CSharpAdvantageCategories)}");
        }

        // Token economics highlight
        if (summary.CategoryAdvantages.TryGetValue("TokenEconomics", out var tokenAdvantage))
        {
            var savings = (1 - 1.0 / tokenAdvantage) * 100;
            if (savings > 0)
            {
                sb.AppendLine($"3. **Token savings:** Calor uses approximately {savings:F0}% fewer tokens than equivalent C# code");
            }
        }

        // Information density highlight
        if (summary.CategoryAdvantages.TryGetValue("InformationDensity", out var densityAdvantage))
        {
            if (densityAdvantage > 1.0)
            {
                sb.AppendLine($"4. **Information density:** Calor carries {densityAdvantage:F1}x more semantic information per token");
            }
        }

        sb.AppendLine();

        // Recommendations
        sb.AppendLine("### Recommendations");
        sb.AppendLine();
        sb.AppendLine("Based on the evaluation results:");
        sb.AppendLine();

        if (summary.OverallCalorAdvantage > 1.2)
        {
            sb.AppendLine("- Consider using Calor for AI agent interactions to reduce token costs");
            sb.AppendLine("- Calor's explicit structure may improve AI code generation accuracy");
        }
        else if (summary.OverallCalorAdvantage < 0.8)
        {
            sb.AppendLine("- C# may be more suitable for the evaluated use cases");
            sb.AppendLine("- Consider the specific task requirements when choosing");
        }
        else
        {
            sb.AppendLine("- Results are similar; choose based on team familiarity and tooling");
            sb.AppendLine("- Consider category-specific advantages for specialized tasks");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*Report generated by Calor Evaluation Framework*");
    }
}
