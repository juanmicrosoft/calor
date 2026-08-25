namespace Calor.RoundTrip.Harness;

public static class RoundTripExitPolicy
{
    public static bool IsFailure(RoundTripReport report)
        => GetFailureReasons(report).Count > 0;

    public static IReadOnlyList<string> GetFailureReasons(RoundTripReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var reasons = new List<string>();
        if (report.Inconclusive)
            reasons.Add(report.InconclusiveReason ?? "run is inconclusive");
        if (report.BaselineBuildResult?.Succeeded != true)
            reasons.Add("baseline build failed or was not run");
        if (report.BuildResult?.Succeeded != true)
            reasons.Add("round-trip build failed or was not run");
        if (report.Comparison?.Status != ComparisonStatus.Pass)
            reasons.Add($"test comparison is {report.Comparison?.Status.ToString() ?? "missing"}");
        if (report.Fidelity is null)
        {
            reasons.Add("fidelity dimensions are missing");
            return reasons;
        }

        var tests = report.Fidelity.Tests;
        var coverage = report.Fidelity.Coverage;
        if (tests.InventoryDelta < 0)
            reasons.Add($"test inventory shrank by {-tests.InventoryDelta}");
        if (tests.BaselineUsedConsoleFallback || tests.RoundTripUsedConsoleFallback)
            reasons.Add("structured TRX results were missing");
        if (report.Baseline?.TrxFiles.Count == 0 ||
            report.RoundTripTests?.TrxFiles.Count == 0)
        {
            reasons.Add("one or more test runs produced no parseable TRX files");
        }
        if (report.Baseline?.ParseErrors.Count > 0 ||
            report.RoundTripTests?.ParseErrors.Count > 0)
        {
            reasons.Add("one or more TRX files were unparseable");
        }
        // `dotnet test` exits 1 whenever any test fails, so a leg whose only
        // failures are allowlisted upstream flakes would otherwise block here
        // even though the comparison already ignored them.
        if (report.Baseline?.ExitCode != 0 &&
            !IsTestExitExplainedByKnownFlakes(
                report.Baseline, report.Comparison?.IgnoredFlakyBaselineFailures))
        {
            reasons.Add($"baseline tests exited with {report.Baseline?.ExitCode ?? -1}");
        }
        if (report.RoundTripTests?.ExitCode != 0 &&
            !IsTestExitExplainedByKnownFlakes(
                report.RoundTripTests, report.Comparison?.IgnoredFlakyRoundTripFailures))
        {
            reasons.Add($"round-trip tests exited with {report.RoundTripTests?.ExitCode ?? -1}");
        }
        var timedOut = report.FileResults.Count(file =>
            file.Status == FileStatus.ConversionTimedOut);
        if (timedOut > 0)
            reasons.Add($"{timedOut} source conversion(s) timed out");
        var crashed = report.FileResults.Count(file => file.Status == FileStatus.Crashed);
        if (crashed > 0)
            reasons.Add($"{crashed} source conversion(s) crashed");
        var reverted = report.FileResults.Count(file => file.Status == FileStatus.Reverted);
        if (reverted > 0)
            reasons.Add($"{reverted} build-breaking conversion(s) were reverted");
        if (coverage.CoverageFraction < report.MinimumCoverageFraction)
        {
            reasons.Add(
                $"coverage {coverage.CoverageFraction:P1} is below " +
                $"{report.MinimumCoverageFraction:P1}");
        }
        if (coverage.NativeFraction < report.MinimumNativeFraction)
        {
            reasons.Add(
                $"native coverage {coverage.NativeFraction:P1} is below " +
                $"{report.MinimumNativeFraction:P1}");
        }
        return reasons;
    }

    /// <summary>
    /// True when a test leg's non-zero exit is fully accounted for by allowlisted
    /// upstream flakes: the exit code is 1 (the "some tests failed" code —
    /// timeouts, crashes and build failures exit differently), the run produced
    /// structured TRX results, and every recorded failure is on the allowlist.
    /// Fails closed on any other shape.
    /// </summary>
    public static bool IsTestExitExplainedByKnownFlakes(
        TestRunResult? run, IReadOnlyCollection<TestResult>? ignoredFlakyFailures)
    {
        if (run is null || ignoredFlakyFailures is null)
            return false;
        return run.ExitCode == 1
            && !run.UsedConsoleFallback
            && run.ParseErrors.Count == 0
            && run.Failed > 0
            && run.Failed == ignoredFlakyFailures.Count;
    }
}
