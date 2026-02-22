using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.RoundTrip.Harness;

/// <summary>
/// Generates Markdown and JSON reports from round-trip results.
/// </summary>
public static class ReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string GenerateMarkdown(RoundTripReport report)
    {
        var sb = new StringBuilder();
        var verdict = GetVerdict(report);

        sb.AppendLine($"# {report.ProjectName} — Round-Trip Verification Report");
        sb.AppendLine();
        sb.AppendLine($"**Calor Version:** {report.CalorVersion}");
        sb.AppendLine($"**Date:** {report.StartedAt:yyyy-MM-dd}");
        sb.AppendLine($"**Duration:** {report.Duration.TotalSeconds:F1}s");
        sb.AppendLine($"**Verdict:** {verdict}");
        sb.AppendLine();

        // Pipeline Summary
        sb.AppendLine("## Pipeline Summary");
        sb.AppendLine();
        sb.AppendLine("| Stage | Result |");
        sb.AppendLine("|-------|--------|");

        if (report.Baseline != null)
            sb.AppendLine($"| Baseline tests | {report.Baseline.Passed} passed, {report.Baseline.Failed} failed, {report.Baseline.Skipped} skipped |");

        var replaced = report.FileResults.Count(f => f.Status == FileStatus.Replaced);
        var totalFiles = report.FileResults.Count;
        var pct = totalFiles > 0 ? (double)replaced / totalFiles * 100 : 0;
        sb.AppendLine($"| Files converted | {replaced}/{totalFiles} ({pct:F1}%) |");

        var interop = report.FileResults.Sum(f => f.InteropBlocks);
        sb.AppendLine($"| Files with interop blocks | {report.FileResults.Count(f => f.InteropBlocks > 0)} |");

        if (report.BuildResult != null)
            sb.AppendLine($"| Build after replacement | {(report.BuildResult.Succeeded ? "Success" : "FAILED")} |");

        if (report.RoundTripTests != null)
            sb.AppendLine($"| Round-trip tests | {report.RoundTripTests.Passed} passed, {report.RoundTripTests.Failed} failed, {report.RoundTripTests.Skipped} skipped |");

        if (report.Comparison != null)
            sb.AppendLine($"| Regressions | **{report.Comparison.Regressions.Count}** |");

        sb.AppendLine();

        // File-by-file results
        sb.AppendLine("## File-by-File Results");
        sb.AppendLine();
        sb.AppendLine("| File | Status | Conv. Rate | Errors |");
        sb.AppendLine("|------|--------|-----------|--------|");

        foreach (var file in report.FileResults.OrderBy(f => f.FilePath))
        {
            var statusEmoji = file.Status switch
            {
                FileStatus.Replaced => "Replaced",
                FileStatus.ConversionFailed => "Conv. Failed",
                FileStatus.EmitSyntaxError => "Emit Error",
                FileStatus.CompileError => "Compile Error",
                FileStatus.Crashed => "Crashed",
                FileStatus.Excluded => "Excluded",
                _ => "Unknown",
            };
            var errors = file.Errors.Count > 0 ? file.Errors.First().Truncate(80) : "-";
            sb.AppendLine($"| {file.FilePath} | {statusEmoji} | {file.ConversionRate:F0}% | {errors} |");
        }

        sb.AppendLine();

        // Regression analysis
        if (report.Comparison is { Regressions.Count: > 0 })
        {
            sb.AppendLine("## Regressions");
            sb.AppendLine();
            sb.AppendLine($"{report.Comparison.Regressions.Count} test(s) that passed in baseline now fail after round-trip:");
            sb.AppendLine();

            foreach (var reg in report.Comparison.Regressions.Take(50))
            {
                sb.AppendLine($"- **{reg.TestName}**");
                if (reg.ErrorMessage != null)
                    sb.AppendLine($"  > {reg.ErrorMessage.Truncate(200)}");
            }

            if (report.Comparison.Regressions.Count > 50)
                sb.AppendLine($"\n... and {report.Comparison.Regressions.Count - 50} more");
        }
        else if (report.Comparison is { Status: ComparisonStatus.Pass })
        {
            sb.AppendLine("## Regression Analysis");
            sb.AppendLine();
            sb.AppendLine("No regressions detected. All previously-passing tests continue to pass.");
        }

        sb.AppendLine();

        // Pre-existing failures
        if (report.Comparison is { PreExistingFailures: > 0 })
        {
            sb.AppendLine("## Pre-Existing Failures (not caused by conversion)");
            sb.AppendLine();
            sb.AppendLine($"{report.Comparison.PreExistingFailures} test(s) were already failing in the unmodified project.");
        }

        // Build errors
        if (report.BuildResult is { Succeeded: false, Errors.Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Build Errors");
            sb.AppendLine();
            foreach (var error in report.BuildResult.Errors.Take(30))
            {
                sb.AppendLine($"- {error.Truncate(200)}");
            }
        }

        // Bisect results
        if (report.BisectResults is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Bisect Results");
            sb.AppendLine();
            sb.AppendLine("Files identified as causing regressions:");
            sb.AppendLine();

            foreach (var (file, tests) in report.BisectResults)
            {
                sb.AppendLine($"- **{file}** caused {tests.Count} regression(s):");
                foreach (var test in tests.Take(10))
                    sb.AppendLine($"  - {test}");
            }
        }

        return sb.ToString();
    }

    public static string GenerateJson(RoundTripReport report)
    {
        var summary = new
        {
            project = report.ProjectName,
            calor_version = report.CalorVersion,
            timestamp = report.StartedAt.ToString("o"),
            duration_seconds = report.Duration.TotalSeconds,
            verdict = report.Comparison?.Status.ToString().ToLowerInvariant() ?? "incomplete",
            baseline = report.Baseline != null ? new
            {
                total = report.Baseline.TotalTests,
                passed = report.Baseline.Passed,
                failed = report.Baseline.Failed,
                skipped = report.Baseline.Skipped,
            } : null,
            round_trip = report.RoundTripTests != null ? new
            {
                total = report.RoundTripTests.TotalTests,
                passed = report.RoundTripTests.Passed,
                failed = report.RoundTripTests.Failed,
                skipped = report.RoundTripTests.Skipped,
            } : null,
            regressions = report.Comparison?.Regressions.Count ?? -1,
            files = new
            {
                total = report.FileResults.Count,
                replaced = report.FileResults.Count(f => f.Status == FileStatus.Replaced),
                conversion_failed = report.FileResults.Count(f => f.Status == FileStatus.ConversionFailed),
                emit_error = report.FileResults.Count(f => f.Status == FileStatus.EmitSyntaxError),
                compile_error = report.FileResults.Count(f => f.Status == FileStatus.CompileError),
                crashed = report.FileResults.Count(f => f.Status == FileStatus.Crashed),
            },
            avg_conversion_rate = report.FileResults.Count > 0
                ? report.FileResults.Average(f => f.ConversionRate) / 100.0
                : 0.0,
            build_succeeded = report.BuildResult?.Succeeded ?? false,
        };

        return JsonSerializer.Serialize(summary, JsonOptions);
    }

    private static string GetVerdict(RoundTripReport report)
    {
        if (report.Comparison == null) return "INCOMPLETE";
        return report.Comparison.Status switch
        {
            ComparisonStatus.Pass => $"PASS — 0 regressions",
            ComparisonStatus.MinorRegressions => $"MINOR — {report.Comparison.Regressions.Count} regressions (<5%)",
            ComparisonStatus.MajorRegressions => $"FAIL — {report.Comparison.Regressions.Count} regressions",
            ComparisonStatus.BuildFailed => "FAIL — build failed after conversion",
            ComparisonStatus.Incomplete => "INCOMPLETE",
            _ => "UNKNOWN",
        };
    }
}

internal static class StringExtensions
{
    public static string Truncate(this string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength] + "...";
}
