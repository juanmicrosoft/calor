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
        var gateFailures = RoundTripExitPolicy.GetFailureReasons(report);
        if (gateFailures.Count > 0)
        {
            sb.AppendLine("**Blocking gate failures:**");
            foreach (var reason in gateFailures)
                sb.AppendLine($"- {reason}");
            sb.AppendLine();
        }

        // Pipeline Summary
        sb.AppendLine("## Pipeline Summary");
        sb.AppendLine();
        sb.AppendLine("| Stage | Result |");
        sb.AppendLine("|-------|--------|");

        if (report.Baseline != null)
            sb.AppendLine($"| Baseline tests | {report.Baseline.Passed} passed, {report.Baseline.Failed} failed, {report.Baseline.Skipped} skipped |");

        var replaced = report.FileResults.Count(f => f.Status == FileStatus.Replaced);
        var totalFiles = report.Fidelity?.Coverage.TotalConvertibleFiles
            ?? report.FileResults.Count + report.ExcludedFileCount;
        var pct = totalFiles > 0 ? (double)replaced / totalFiles * 100 : 0;
        sb.AppendLine($"| Files converted | {replaced}/{totalFiles} ({pct:F1}%) |");
        sb.AppendLine($"| Files reverted by recovery | {report.FileResults.Count(f => f.Status == FileStatus.Reverted)} |");
        sb.AppendLine($"| Files with interop blocks | {report.FileResults.Count(f => f.InteropBlocks > 0)} |");

        if (report.BuildResult != null)
            sb.AppendLine($"| Build after replacement | {(report.BuildResult.Succeeded ? "Success" : "FAILED")} |");

        if (report.RoundTripTests != null)
            sb.AppendLine($"| Round-trip tests | {report.RoundTripTests.Passed} passed, {report.RoundTripTests.Failed} failed, {report.RoundTripTests.Skipped} skipped |");

        if (report.Comparison != null)
            sb.AppendLine($"| Regressions | **{report.Comparison.Regressions.Count}** |");

        sb.AppendLine();

        // Fidelity dimensions — suppressed for an inconclusive (unattributable build
        // failure) run: the coverage fraction would be spuriously inflated, so we emit a
        // clear notice instead of a number.
        if (report.Inconclusive)
        {
            sb.AppendLine("## Fidelity — INCONCLUSIVE");
            sb.AppendLine();
            sb.AppendLine($"No coverage fraction is reported: {report.InconclusiveReason}");
            sb.AppendLine();
        }
        else if (report.Fidelity != null)
        {
            var cov = report.Fidelity.Coverage;
            var build = report.Fidelity.Build;
            var tests = report.Fidelity.Tests;

            sb.AppendLine("## Fidelity (separated verdict dimensions)");
            sb.AppendLine();
            sb.AppendLine("### Conversion Coverage");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|--------|-------|");
            sb.AppendLine($"| Coverage (converted-and-kept / total) | **{cov.CoverageFraction:P1}** ({cov.ConvertedNative + cov.ConvertedWithLosses}/{cov.TotalConvertibleFiles}) |");
            sb.AppendLine($"| Converted natively (zero losses) | {cov.ConvertedNative} ({cov.NativeFraction:P1}) |");
            sb.AppendLine($"| Converted with losses | {cov.ConvertedWithLosses} |");
            sb.AppendLine($"| Reverted by recovery (coverage failures) | {cov.Reverted} |");
            sb.AppendLine($"| Failed conversion | {cov.FailedConversion} |");
            sb.AppendLine($"| Excluded by pattern (coverage failures in denominator) | {cov.ExcludedFiles} |");
            sb.AppendLine($"| Minimum total coverage | {report.MinimumCoverageFraction:P1} |");
            sb.AppendLine($"| Minimum native coverage | {report.MinimumNativeFraction:P1} |");
            sb.AppendLine($"| Interop blocks (raw C# preserved) | {cov.TotalInteropBlocks} |");
            sb.AppendLine($"| Distinct semantic gaps | {cov.DistinctGaps.Count} |");
            if (cov.LossKindCounts.Count > 0)
            {
                var kinds = string.Join(", ", cov.LossKindCounts
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => $"{kv.Key}: {kv.Value}"));
                sb.AppendLine($"| Loss ledger by kind | {kinds} |");
            }
            if (cov.DistinctGaps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Gap features: " + string.Join(", ", cov.DistinctGaps.Take(30).Select(g => $"`{g}`")));
                if (cov.DistinctGaps.Count > 30)
                    sb.AppendLine($"... and {cov.DistinctGaps.Count - 30} more");
            }
            sb.AppendLine();
            sb.AppendLine("### Build Outcome");
            sb.AppendLine();
            sb.AppendLine($"- Baseline succeeded: {build.BaselineSucceeded}");
            sb.AppendLine($"- Round-trip succeeded: {build.Succeeded} (exit {build.ExitCode})");
            sb.AppendLine($"- Files reverted to reach this outcome: {build.RecoveryRevertedFiles}");
            sb.AppendLine($"- Build errors: {build.ErrorCount}");
            sb.AppendLine();
            sb.AppendLine("### Test Outcome");
            sb.AppendLine();
            sb.AppendLine($"- Baseline: {tests.BaselinePassed}/{tests.BaselineTotal} passed ({tests.BaselineTrxFiles} TRX file(s){(tests.BaselineUsedConsoleFallback ? ", console fallback" : "")})");
            sb.AppendLine($"- Round-trip: {tests.RoundTripPassed}/{tests.RoundTripTotal} passed ({tests.RoundTripTrxFiles} TRX file(s){(tests.RoundTripUsedConsoleFallback ? ", console fallback" : "")})");
            sb.AppendLine($"- Test inventory delta: {tests.InventoryDelta:+0;-0;0}");
            sb.AppendLine($"- Regressions: {tests.Regressions}; new passes: {tests.NewPasses}; status: {tests.ComparisonStatus}");
            sb.AppendLine();
        }

        // File-by-file results
        sb.AppendLine("## File-by-File Results");
        sb.AppendLine();
        sb.AppendLine("| File | Status | Context mode | Contexts | Conv. Rate | Losses | Interop | Gaps | Errors |");
        sb.AppendLine("|------|--------|--------------|----------|-----------|--------|---------|------|--------|");

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
                FileStatus.Reverted => "REVERTED",
                _ => "Unknown",
            };
            var errors = file.Errors.Count > 0 ? file.Errors.First().Truncate(80) : "-";
            var gaps = file.Gaps.Count > 0 ? string.Join("; ", file.Gaps.Take(3)) + (file.Gaps.Count > 3 ? $"; +{file.Gaps.Count - 3}" : "") : "-";
            sb.AppendLine($"| {file.FilePath} | {statusEmoji} | {file.ContextSelectionMode ?? "-"} | {file.ValidatedContexts.Count} | {file.ConversionRate:F0}% | {file.LossCount} | {file.InteropBlocks} | {gaps} | {errors} |");
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
            verdict = report.Inconclusive
                ? "inconclusive"
                : RoundTripExitPolicy.IsFailure(report) ? "fail" : "pass",
            gate_failures = RoundTripExitPolicy.GetFailureReasons(report),
            // When inconclusive, no coverage fraction is trustworthy — see below (fidelity is nulled).
            inconclusive = report.Inconclusive,
            inconclusive_reason = report.InconclusiveReason,
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
                total = report.Fidelity?.Coverage.TotalConvertibleFiles
                    ?? report.FileResults.Count + report.ExcludedFileCount,
                replaced = report.FileResults.Count(f => f.Status == FileStatus.Replaced),
                reverted = report.FileResults.Count(f => f.Status == FileStatus.Reverted),
                conversion_failed = report.FileResults.Count(f => f.Status == FileStatus.ConversionFailed),
                conversion_timed_out = report.FileResults.Count(f => f.Status == FileStatus.ConversionTimedOut),
                emit_error = report.FileResults.Count(f => f.Status == FileStatus.EmitSyntaxError),
                emit_compilation_error = report.FileResults.Count(f => f.Status == FileStatus.EmitCompilationError),
                compile_error = report.FileResults.Count(f => f.Status == FileStatus.CompileError),
                crashed = report.FileResults.Count(f => f.Status == FileStatus.Crashed),
                excluded_by_pattern = report.ExcludedFileCount,
            },
            avg_conversion_rate = report.FileResults.Count > 0
                ? report.FileResults.Average(f => f.ConversionRate) / 100.0
                : 0.0,
            build_succeeded = report.BuildResult?.Succeeded ?? false,
            // Do NOT emit a fidelity fraction for an inconclusive (unattributable build
            // failure) run — the coverage numbers would be spuriously inflated because no
            // file was reverted. Emit null so no downstream reader trusts a fraction.
            fidelity = (report.Inconclusive || report.Fidelity == null) ? null : new
            {
                coverage = new
                {
                    total_convertible_files = report.Fidelity.Coverage.TotalConvertibleFiles,
                    converted_native = report.Fidelity.Coverage.ConvertedNative,
                    converted_with_losses = report.Fidelity.Coverage.ConvertedWithLosses,
                    reverted = report.Fidelity.Coverage.Reverted,
                    failed_conversion = report.Fidelity.Coverage.FailedConversion,
                    excluded_files = report.Fidelity.Coverage.ExcludedFiles,
                    coverage_fraction = report.Fidelity.Coverage.CoverageFraction,
                    native_fraction = report.Fidelity.Coverage.NativeFraction,
                    loss_kind_counts = report.Fidelity.Coverage.LossKindCounts,
                    total_interop_blocks = report.Fidelity.Coverage.TotalInteropBlocks,
                    distinct_gaps = report.Fidelity.Coverage.DistinctGaps,
                    minimum_coverage_fraction = report.MinimumCoverageFraction,
                    minimum_native_fraction = report.MinimumNativeFraction,
                },
                build = new
                {
                    baseline_succeeded = report.Fidelity.Build.BaselineSucceeded,
                    succeeded = report.Fidelity.Build.Succeeded,
                    exit_code = report.Fidelity.Build.ExitCode,
                    recovery_reverted_files = report.Fidelity.Build.RecoveryRevertedFiles,
                    error_count = report.Fidelity.Build.ErrorCount,
                },
                tests = new
                {
                    baseline_total = report.Fidelity.Tests.BaselineTotal,
                    baseline_passed = report.Fidelity.Tests.BaselinePassed,
                    baseline_failed = report.Fidelity.Tests.BaselineFailed,
                    baseline_trx_files = report.Fidelity.Tests.BaselineTrxFiles,
                    baseline_console_fallback = report.Fidelity.Tests.BaselineUsedConsoleFallback,
                    round_trip_total = report.Fidelity.Tests.RoundTripTotal,
                    round_trip_passed = report.Fidelity.Tests.RoundTripPassed,
                    round_trip_failed = report.Fidelity.Tests.RoundTripFailed,
                    round_trip_trx_files = report.Fidelity.Tests.RoundTripTrxFiles,
                    round_trip_console_fallback = report.Fidelity.Tests.RoundTripUsedConsoleFallback,
                    inventory_delta = report.Fidelity.Tests.InventoryDelta,
                    regressions = report.Fidelity.Tests.Regressions,
                    new_passes = report.Fidelity.Tests.NewPasses,
                    comparison_status = report.Fidelity.Tests.ComparisonStatus.ToString(),
                },
            },
            file_detail = report.FileResults
                .OrderBy(f => f.FilePath, StringComparer.Ordinal)
                .Select(f => new
                {
                    path = f.FilePath,
                    status = f.Status.ToString(),
                    preprocessor_mode = f.PreprocessorMode,
                    configuration = f.Configuration,
                    target_framework = f.TargetFramework,
                    language_version = f.LanguageVersion,
                    defined_symbols = f.DefinedSymbols,
                    context_selection_mode = f.ContextSelectionMode,
                    validated_contexts = f.ValidatedContexts,
                    loss_count = f.LossCount,
                    loss_kinds = f.LossKindCounts,
                    losses = f.Losses,
                    interop_blocks = f.InteropBlocks,
                    gaps = f.Gaps,
                    revert_reason = f.RevertReason,
                    // Serialized so a failure-cause census is possible from the committed report
                    // alone; previously these lived in-model only and the record said only "how
                    // many" files failed, never "why".
                    errors = f.Errors,
                })
                .ToList(),
        };

        return JsonSerializer.Serialize(summary, JsonOptions);
    }

    private static string GetVerdict(RoundTripReport report)
    {
        var failures = RoundTripExitPolicy.GetFailureReasons(report);
        return failures.Count == 0
            ? "PASS — 0 regressions"
            : $"FAIL — {string.Join("; ", failures)}";
    }
}

internal static class StringExtensions
{
    public static string Truncate(this string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength] + "...";
}
