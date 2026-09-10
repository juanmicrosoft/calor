using System.Text;
using System.Text.RegularExpressions;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Reports;

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
        if (!string.IsNullOrWhiteSpace(result.CommitHash))
        {
            sb.AppendLine($"**Source Commit:** `{result.CommitHash}`");
            var sourceVersion = TryGetSourceDeclaredVersion(result.CommitHash);
            if (sourceVersion != null)
                sb.AppendLine($"**Source-Declared Compiler Version:** {sourceVersion}");
        }
        if (result.StatisticalRunCount > 0)
            sb.AppendLine($"**Statistical Runs:** {result.StatisticalRunCount}");
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

        var overallAdvantage = summary.OverallCalorAdvantage;
        sb.AppendLine($"**Legacy Composite Direction-Normalized Ratio:** {overallAdvantage:F2}x");
        sb.AppendLine("This ratio combines deterministic metric ratios after normalizing each metric so values above 1 favor Calor. It is not a measured language, agent-productivity, correctness, or safety advantage.");

        sb.AppendLine();

        // Category breakdown table
        sb.AppendLine("### Category Direction-Normalized Ratios");
        sb.AppendLine();
        sb.AppendLine("| Category | Direction-normalized ratio | Favored language |");
        sb.AppendLine("|----------|----------------------------|------------------|");

        // Identify Calor-only categories for proper winner determination
        var calorOnlyCategories = result.Metrics
            .Where(m => m.IsCalorOnly)
            .Select(m => m.Category)
            .Distinct()
            .ToHashSet();

        foreach (var (category, ratio) in summary.CategoryAdvantages.OrderByDescending(kv => kv.Value))
        {
            var isCalorOnly = calorOnlyCategories.Contains(category);
            var favoredLanguage = (ratio > 1.0 || isCalorOnly) ? "Calor" : (ratio < 1.0 ? "C#" : "Tie");
            var suffix = isCalorOnly ? " (Calor-only)" : "";
            sb.AppendLine($"| {category} | {ratio:F2}x | {favoredLanguage}{suffix} |");
        }

        sb.AppendLine();

        // Pass counts
        sb.AppendLine("### Parse Check Results");
        sb.AppendLine();
        sb.AppendLine($"- Calor parser accepted: {summary.CalorPassCount}");
        sb.AppendLine($"- Roslyn syntax parser accepted: {summary.CSharpPassCount}");
        sb.AppendLine("- These checks do not build or execute the paired programs.");
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
            // Check if this is a Calor-only category
            var isCalorOnly = category.Any(m => m.Details.TryGetValue("isCalorOnly", out var v) && v is bool b && b);

            var avgAdvantage = category.Average(m => m.AdvantageRatio);
            var calorWins = category.Count(m => m.AdvantageRatio > 1.0);
            var csharpWins = category.Count(m => m.AdvantageRatio < 1.0);

            sb.AppendLine($"### {category.Key}");
            if (isCalorOnly)
            {
                sb.AppendLine("*(Calor-only metric - C# has no equivalent)*");
            }
            sb.AppendLine();

            if (isCalorOnly)
            {
                // For Calor-only metrics, show score as percentage instead of ratio
                var avgScore = category.Average(m => m.CalorScore) * 100;
                sb.AppendLine($"**Average Score:** {avgScore:F1}%");
            }
            else
            {
                sb.AppendLine($"**Average Direction-Normalized Ratio:** {avgAdvantage:F2}x");
                sb.AppendLine($"**Calor-favoring:** {calorWins} | **C#-favoring:** {csharpWins}");
            }
            sb.AppendLine();

            // Top metrics
            var topMetrics = category.OrderByDescending(m => m.AdvantageRatio).Take(5);

            if (isCalorOnly)
            {
                sb.AppendLine("| Metric | Score | Details |");
                sb.AppendLine("|--------|-------|---------|");

                foreach (var metric in topMetrics)
                {
                    var scorePercent = metric.CalorScore * 100;
                    var details = GetCalorOnlyDetails(metric);
                    sb.AppendLine($"| {metric.MetricName} | {scorePercent:F1}% | {details} |");
                }
            }
            else
            {
                sb.AppendLine("| Metric | Calor raw score | C# raw score | Direction-normalized ratio |");
                sb.AppendLine("|--------|-----------------|--------------|----------------------------|");

                foreach (var metric in topMetrics)
                {
                    sb.AppendLine($"| {metric.MetricName} | {metric.CalorScore:F2} | {metric.CSharpScore:F2} | {metric.AdvantageRatio:F2}x |");
                }
            }

            sb.AppendLine();
        }
    }

    private static string GetCalorOnlyDetails(MetricResult metric)
    {
        var parts = new List<string>();

        // For ContractVerification
        if (metric.Details.TryGetValue("proven", out var proven))
            parts.Add($"proven: {proven}");
        if (metric.Details.TryGetValue("disproven", out var disproven))
            parts.Add($"disproven: {disproven}");
        if (metric.Details.TryGetValue("unproven", out var unproven))
            parts.Add($"unproven: {unproven}");

        // For EffectSoundness
        if (metric.Details.TryGetValue("forbiddenEffectErrors", out var forbidden))
            parts.Add($"forbidden: {forbidden}");
        if (metric.Details.TryGetValue("unknownCallErrors", out var unknown))
            parts.Add($"unknown: {unknown}");

        // For InteropEffectCoverage
        if (metric.Details.TryGetValue("resolved", out var resolved))
            parts.Add($"resolved: {resolved}");
        if (metric.Details.TryGetValue("total", out var total) && !metric.Details.ContainsKey("proven"))
            parts.Add($"total: {total}");

        // Error/skip messages
        if (metric.Details.TryGetValue("error", out var error))
            parts.Add($"error: {error}");
        if (metric.Details.TryGetValue("skipped", out var skipped) && skipped is string s)
            parts.Add($"skipped: {s}");
        if (metric.Details.TryGetValue("noContracts", out var noContracts) && noContracts is bool nc && nc)
            parts.Add("no contracts");

        return parts.Count > 0 ? string.Join(", ", parts) : "-";
    }

    private static void WriteDetailedResults(StringBuilder sb, EvaluationResult result)
    {
        sb.AppendLine("## Detailed Results by Benchmark");
        sb.AppendLine();

        // Group by level
        var byLevel = result.CaseResults.GroupBy(c => c.Level).OrderBy(g => g.Key);

        foreach (var level in byLevel)
        {
            sb.AppendLine($"### Level {level.Key}");
            sb.AppendLine();
            sb.AppendLine("| Benchmark | Calor parse | C# parse | Average Direction-Normalized Ratio |");
            sb.AppendLine("|-----------|-------------|----------|------------------------------------|");

            foreach (var caseResult in level.OrderByDescending(c => c.AverageAdvantage))
            {
                var calorOk = caseResult.CalorSuccess ? "✓" : "✗";
                var csharpOk = caseResult.CSharpSuccess ? "✓" : "✗";
                sb.AppendLine($"| {caseResult.FileName} | {calorOk} | {csharpOk} | {caseResult.AverageAdvantage:F2}x |");
            }

            sb.AppendLine();
        }
    }

    private static void WriteConclusions(StringBuilder sb, EvaluationResult result)
    {
        sb.AppendLine("## Interpretation Limits");
        sb.AppendLine();

        sb.AppendLine("- The metrics are static calculator rules, not observed coding-agent outcomes.");
        if (result.StatisticalRunCount > 1)
            sb.AppendLine($"- {result.StatisticalRunCount} repetitions repeat deterministic observations over a fixed corpus; they are not independent corpus samples.");
        else
            sb.AppendLine("- A single deterministic observation does not establish sampling uncertainty over the corpus.");
        sb.AppendLine("- The paired sources are not all behaviorally equivalent, so the ratios do not establish a language advantage.");
        sb.AppendLine("- Parse acceptance does not establish generated-code build success or runtime correctness.");

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*Report generated by Calor Evaluation Framework*");
    }

    private static string? TryGetSourceDeclaredVersion(string commitHash)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"show {commitHash}:Directory.Build.props",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
                return null;

            var props = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return null;

            return Regex.Match(props, @"<Version>([^<]+)</Version>") is { Success: true } match
                ? match.Groups[1].Value
                : null;
        }
        catch
        {
            return null;
        }
    }
}
