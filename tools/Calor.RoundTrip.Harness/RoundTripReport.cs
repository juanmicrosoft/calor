using System.Text.Json.Serialization;
using Calor.Compiler.Migration;

namespace Calor.RoundTrip.Harness;

/// <summary>
/// Complete round-trip verification report for a project.
/// </summary>
public sealed class RoundTripReport
{
    [JsonIgnore]
    internal IReadOnlyDictionary<string, ProjectFileParseContext>
        EvaluatedParseContexts { get; set; } =
            new Dictionary<string, ProjectFileParseContext>();
    [JsonIgnore]
    internal IReadOnlyDictionary<string, ProjectFileParseResolution>
        EvaluatedParseResolutions { get; set; } =
            new Dictionary<string, ProjectFileParseResolution>();
    public required string ProjectName { get; init; }
    public string CalorVersion { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public TimeSpan Duration => FinishedAt - StartedAt;

    public TestRunResult? Baseline { get; set; }
    public BuildResult? BaselineBuildResult { get; set; }
    public List<FileConversionResult> FileResults { get; set; } = [];

    /// <summary>Count of candidate .cs files skipped by exclude patterns. Exclusions remain in the coverage denominator.</summary>
    public int ExcludedFileCount { get; set; }

    public BuildResult? BuildResult { get; set; }
    public TestRunResult? RoundTripTests { get; set; }
    public TestComparison? Comparison { get; set; }

    /// <summary>Separated verdict dimensions (#776): coverage, build, tests — the substrate the A-1.4 fidelity gate thresholds on.</summary>
    public ProjectFidelity? Fidelity { get; set; }
    public double MinimumCoverageFraction { get; set; }
    public double MinimumNativeFraction { get; set; }

    /// <summary>
    /// True when the run could NOT be adjudicated: the post-conversion build failed but
    /// the failure could not be attributed to any file (build recovery extracted zero
    /// error files — e.g. a recovery-build timeout). In that state no file was reverted,
    /// so the coverage fraction would be spuriously inflated; it MUST NOT be trusted or
    /// emitted as a fidelity number. Distinct from an honest low-coverage run.
    /// </summary>
    public bool Inconclusive { get; set; }

    /// <summary>Why the run is inconclusive (null when it is not).</summary>
    public string? InconclusiveReason { get; set; }

    /// <summary>Optional bisect results mapping culprit file → test names.</summary>
    public Dictionary<string, List<string>>? BisectResults { get; set; }

    [JsonIgnore]
    public string Verdict => Comparison?.Status.ToString() ?? "Incomplete";
}

public sealed class FileConversionResult
{
    public required string FilePath { get; init; }
    public FileStatus Status { get; set; }
    public bool ConversionSuccess { get; set; }
    public double ConversionRate { get; set; }
    public string? PreprocessorMode { get; set; }
    public string? Configuration { get; set; }
    public string? TargetFramework { get; set; }
    public string? LanguageVersion { get; set; }
    public List<string> DefinedSymbols { get; set; } = [];
    public string? ContextSelectionMode { get; set; }
    public List<FileContextDetail> ValidatedContexts { get; set; } = [];

    /// <summary>Total structured losses recorded by the conversion loss ledger (#770) for this file.</summary>
    public int LossCount { get; set; }

    /// <summary>Loss-ledger entry counts by <see cref="ConversionLossKind"/> name.</summary>
    public Dictionary<string, int> LossKindCounts { get; set; } = [];

    /// <summary>
    /// Distinct semantic gaps (feature names whose behavior was NOT preserved natively:
    /// FallbackTodo / Dropped / PreprocessorStripped losses). Populated from the
    /// conversion loss ledger — never defaulted to a false zero.
    /// </summary>
    public List<string> Gaps { get; set; } = [];

    /// <summary>
    /// Count of raw C# preserved verbatim in the output (InteropPreserved +
    /// EmitterFallback ledger entries). Populated from the conversion loss ledger.
    /// </summary>
    public int InteropBlocks { get; set; }

    /// <summary>Structured per-loss detail from the ledger (kind, feature, line).</summary>
    public List<FileLossDetail> Losses { get; set; } = [];

    /// <summary>Why the file was reverted, when Status == Reverted (e.g. build-recovery round).</summary>
    public string? RevertReason { get; set; }

    public List<string> Errors { get; set; } = [];

    /// <summary>Stored for debugging; not serialized to JSON reports.</summary>
    [JsonIgnore]
    public string? EmittedCSharp { get; set; }

    [JsonIgnore]
    internal IReadOnlyList<ProjectFileParseContext> ObservedContexts
        { get; set; } = [];

    /// <summary>True when the file was converted and KEPT in the built project.</summary>
    [JsonIgnore]
    public bool ConvertedAndKept => Status == FileStatus.Replaced;

    /// <summary>True when the file was converted, kept, and recorded zero losses.</summary>
    [JsonIgnore]
    public bool ConvertedNative => ConvertedAndKept && LossCount == 0;

    /// <summary>
    /// True when the file is safe to use as native benchmark supply. Replaced
    /// project candidates have already compiled in every observed build context.
    /// </summary>
    [JsonIgnore]
    public bool EligibleNativeSource =>
        ConvertedNative && InteropBlocks == 0;

    /// <summary>
    /// Populate the loss-derived metrics (LossCount, LossKindCounts, Gaps,
    /// InteropBlocks, Losses) from the Slice-3 conversion loss ledger.
    /// InteropPreserved and EmitterFallback entries are raw-C#-in-output;
    /// everything else is a semantic gap.
    /// </summary>
    public void ApplyLossLedger(IReadOnlyList<ConversionLoss> losses)
    {
        LossCount = losses.Count;
        LossKindCounts = losses
            .GroupBy(l => l.Kind.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        InteropBlocks = losses.Count(l =>
            l.Kind is ConversionLossKind.InteropPreserved or ConversionLossKind.EmitterFallback);
        Gaps = losses
            .Where(l => l.Kind is not (ConversionLossKind.InteropPreserved or ConversionLossKind.EmitterFallback))
            .Select(l => l.Feature)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Losses = losses.Select(l => new FileLossDetail
        {
            Kind = l.Kind.ToString(),
            Feature = l.Feature,
            Description = l.Description,
            Line = l.Line,
        }).ToList();
    }
}

public sealed record FileContextDetail
{
    public required string ProjectFile { get; init; }
    public required string Configuration { get; init; }
    public required string Platform { get; init; }
    public string? TargetFramework { get; init; }
    public required string LanguageVersion { get; init; }
    public required string DocumentationMode { get; init; }
    public required string SourceCodeKind { get; init; }
    public List<string> DefinedSymbols { get; init; } = [];
    public List<string> Provenance { get; init; } = [];
    public List<FileBuildStateDetail> BuildStates { get; init; } = [];
}

public sealed record FileBuildStateDetail
{
    public required string ProjectFile { get; init; }
    public required string Configuration { get; init; }
    public required string Platform { get; init; }
    public string? TargetFramework { get; init; }
    public Dictionary<string, string> GlobalProperties { get; init; } = [];
    public List<string> ProjectGraphPath { get; init; } = [];
}

/// <summary>One conversion-ledger loss, serialized into the harness report.</summary>
public sealed class FileLossDetail
{
    public required string Kind { get; init; }
    public required string Feature { get; init; }
    public string Description { get; init; } = "";
    public int? Line { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileStatus
{
    Replaced,
    ConversionFailed,
    ConversionTimedOut,
    EmitSyntaxError,
    EmitCompilationError,
    CompileError,
    Crashed,
    Excluded,

    /// <summary>
    /// The file WAS converted and replaced, then reverted to its original by a
    /// recovery pass (build recovery or test-failure recovery). A reverted file is
    /// a coverage FAILURE: it stays in the denominator and never counts as converted.
    /// </summary>
    Reverted,
}

public sealed class BuildResult
{
    public bool Succeeded { get; init; }
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
    public List<string> Errors { get; init; } = [];
}

public sealed class TestRunResult
{
    public int ExitCode { get; init; }
    public int TotalTests { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public List<TestResult> Results { get; init; } = [];

    /// <summary>Every TRX file parsed for this run (all of them — never newest-only).</summary>
    public List<string> TrxFiles { get; init; } = [];

    /// <summary>TRX files that could not be parsed; any entry makes the run incomplete.</summary>
    public List<string> ParseErrors { get; init; } = [];

    /// <summary>True when counts came from console-output parsing because no structured TRX results were found.</summary>
    public bool UsedConsoleFallback { get; init; }

    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
}

public sealed class TestResult
{
    public string TestName { get; init; } = "";

    /// <summary>Test assembly file name (from the TRX TestDefinitions storage attribute).</summary>
    public string Assembly { get; init; } = "";

    /// <summary>Project identity inferred from the TRX path relative to the harness working directory.</summary>
    public string Project { get; init; } = "";

    /// <summary>Fully qualified class name (from the TRX TestMethod className attribute).</summary>
    public string ClassName { get; init; } = "";

    /// <summary>Fully qualified test name from the TRX definition.</summary>
    public string FullyQualifiedName { get; init; } = "";

    /// <summary>Stable adapter-provided test-case identifier, including theory row identity.</summary>
    public string TestCaseId { get; init; } = "";

    /// <summary>Executor URI (adapter identity) from the TRX definition, when present.</summary>
    public string ExecutorUri { get; init; } = "";

    public string Outcome { get; init; } = "Unknown";
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public string? StackTrace { get; init; }

    /// <summary>
    /// Robust identity for cross-run matching: assembly + executor + class + display
    /// name (the display name carries theory data-row identity). Two tests with the
    /// same display name in different assemblies/classes never collide.
    /// </summary>
    [JsonIgnore]
    public string Identity =>
        $"{Project}::{Assembly}::{ExecutorUri}::{FullyQualifiedName}::{TestCaseId}::{TestName}";
}

public sealed class TestComparison
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ComparisonStatus Status { get; set; }

    public int BaselineTotal { get; set; }
    public int BaselinePassed { get; set; }
    public int RoundTripTotal { get; set; }
    public int RoundTripPassed { get; set; }
    public List<TestResult> Regressions { get; set; } = [];
    public int PreExistingFailures { get; set; }
    public List<string> NewPasses { get; set; } = [];

    /// <summary>
    /// Regressions that landed on tests listed in
    /// <see cref="RoundTripConfig.ExpectedFlakyTestFullyQualifiedNames"/> and
    /// were therefore excluded from <see cref="Regressions"/> and the
    /// block/warn verdict. Kept in the report so the drift is visible even
    /// when the gate treats it as expected.
    /// </summary>
    public List<TestResult> IgnoredFlakyRegressions { get; set; } = [];

    /// <summary>
    /// Baseline-run failures that landed on allowlisted flaky tests. When these
    /// are the ONLY baseline failures, the baseline's non-zero <c>dotnet test</c>
    /// exit code is explained and the comparison proceeds instead of going
    /// <see cref="ComparisonStatus.Incomplete"/>.
    /// </summary>
    public List<TestResult> IgnoredFlakyBaselineFailures { get; set; } = [];

    /// <summary>
    /// Round-trip-run failures that landed on allowlisted flaky tests, whether or
    /// not they also failed in baseline (so a superset of
    /// <see cref="IgnoredFlakyRegressions"/>). Used to explain the round-trip
    /// leg's non-zero exit code in <see cref="RoundTripExitPolicy"/>.
    /// </summary>
    public List<TestResult> IgnoredFlakyRoundTripFailures { get; set; } = [];

    /// <summary>
    /// "0 (1 ignored upstream flake)"-style count for the summary lines, so an
    /// ignored flake is never printed as a plain "Regressions: 1".
    /// </summary>
    public string FormatRegressionCount()
    {
        var ignored = IgnoredFlakyRegressions.Count;
        return ignored == 0
            ? Regressions.Count.ToString()
            : $"{Regressions.Count} ({ignored} ignored upstream flake{(ignored == 1 ? "" : "s")})";
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComparisonStatus
{
    Pass,
    MinorRegressions,
    MajorRegressions,
    BuildFailed,
    Incomplete,
}

/// <summary>
/// Separated per-project verdict dimensions (#776 / D-W1.4). Each dimension is
/// reported independently so a green test run can never mask partial conversion
/// coverage, and vice versa. This is the machine-readable substrate the future
/// A-1.4 fidelity gate thresholds on (thresholding itself is Slice 5b).
/// </summary>
public sealed class ProjectFidelity
{
    public required ConversionCoverage Coverage { get; init; }
    public required BuildOutcomeSummary Build { get; init; }
    public required TestOutcomeSummary Tests { get; init; }

    /// <summary>Compute all fidelity dimensions from a completed report.</summary>
    public static ProjectFidelity Compute(RoundTripReport report)
    {
        return new ProjectFidelity
        {
            Coverage = ConversionCoverage.Compute(report.FileResults, report.ExcludedFileCount),
            Build = BuildOutcomeSummary.Compute(
                report.BaselineBuildResult,
                report.BuildResult,
                report.FileResults),
            Tests = TestOutcomeSummary.Compute(report.Baseline, report.RoundTripTests, report.Comparison),
        };
    }
}

/// <summary>
/// Per-project conversion coverage. Reverted files are counted in the denominator
/// as failures — recovery must never silently improve the coverage fraction.
/// </summary>
public sealed class ConversionCoverage
{
    /// <summary>All candidate files, including excluded, reverted, and failed files. The denominator.</summary>
    public int TotalConvertibleFiles { get; init; }

    /// <summary>Converted, kept, zero ledger losses.</summary>
    public int ConvertedNative { get; init; }

    /// <summary>Converted and kept, but the ledger recorded at least one loss (interop/fallback/drop).</summary>
    public int ConvertedWithLosses { get; init; }

    /// <summary>Converted then reverted by a recovery pass. A coverage failure.</summary>
    public int Reverted { get; init; }

    /// <summary>Never replaced: conversion / emit / compile failures and crashes.</summary>
    public int FailedConversion { get; init; }

    /// <summary>Files skipped by exclude patterns. They remain coverage failures in the denominator.</summary>
    public int ExcludedFiles { get; init; }

    /// <summary>(ConvertedNative + ConvertedWithLosses) / TotalConvertibleFiles.</summary>
    public double CoverageFraction { get; init; }

    /// <summary>ConvertedNative / TotalConvertibleFiles.</summary>
    public double NativeFraction { get; init; }

    /// <summary>Aggregated ledger loss counts by kind across all files.</summary>
    public Dictionary<string, int> LossKindCounts { get; init; } = [];

    /// <summary>Total raw-C#-preserved blocks across all files (from the ledger).</summary>
    public int TotalInteropBlocks { get; init; }

    /// <summary>Distinct semantic gap features across all files (from the ledger).</summary>
    public List<string> DistinctGaps { get; init; } = [];

    public static ConversionCoverage Compute(IReadOnlyList<FileConversionResult> files, int excludedFileCount)
    {
        var counted = files.Where(f => f.Status != FileStatus.Excluded).ToList();
        var native = counted.Count(f => f.ConvertedNative);
        var withLosses = counted.Count(f =>
            f.ConvertedAndKept && f.LossCount > 0);
        var reverted = counted.Count(f => f.Status == FileStatus.Reverted);
        var excluded = excludedFileCount + files.Count(f => f.Status == FileStatus.Excluded);
        var total = counted.Count + excluded;

        return new ConversionCoverage
        {
            TotalConvertibleFiles = total,
            ConvertedNative = native,
            ConvertedWithLosses = withLosses,
            Reverted = reverted,
            FailedConversion = counted.Count - native - withLosses - reverted,
            ExcludedFiles = excluded,
            CoverageFraction = total > 0 ? (double)(native + withLosses) / total : 0.0,
            NativeFraction = total > 0 ? (double)native / total : 0.0,
            LossKindCounts = counted
                .SelectMany(f => f.LossKindCounts)
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value)),
            TotalInteropBlocks = counted.Sum(f => f.InteropBlocks),
            DistinctGaps = counted
                .SelectMany(f => f.Gaps)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(g => g, StringComparer.Ordinal)
                .ToList(),
        };
    }
}

/// <summary>Post-conversion build state, including how much recovery it took.</summary>
public sealed class BuildOutcomeSummary
{
    public bool BaselineSucceeded { get; init; }
    public bool Succeeded { get; init; }
    public int ExitCode { get; init; }

    /// <summary>Files reverted by build recovery to reach this outcome. Nonzero means the build only succeeds WITHOUT those conversions.</summary>
    public int RecoveryRevertedFiles { get; init; }

    public int ErrorCount { get; init; }

    public static BuildOutcomeSummary Compute(
        BuildResult? baselineBuild,
        BuildResult? build,
        IReadOnlyList<FileConversionResult> files)
    {
        return new BuildOutcomeSummary
        {
            BaselineSucceeded = baselineBuild?.Succeeded ?? false,
            Succeeded = build?.Succeeded ?? false,
            ExitCode = build?.ExitCode ?? -1,
            RecoveryRevertedFiles = files.Count(f => f.Status == FileStatus.Reverted),
            ErrorCount = build?.Errors.Count ?? 0,
        };
    }
}

/// <summary>Baseline vs post-conversion test outcome, aggregated across ALL TRX files.</summary>
public sealed class TestOutcomeSummary
{
    public int BaselineTotal { get; init; }
    public int BaselinePassed { get; init; }
    public int BaselineFailed { get; init; }
    public int BaselineTrxFiles { get; init; }
    public bool BaselineUsedConsoleFallback { get; init; }

    public int RoundTripTotal { get; init; }
    public int RoundTripPassed { get; init; }
    public int RoundTripFailed { get; init; }
    public int RoundTripTrxFiles { get; init; }
    public bool RoundTripUsedConsoleFallback { get; init; }

    /// <summary>RoundTripTotal − BaselineTotal. Negative = shrunk test inventory (a red flag; enforcement is Slice 5b).</summary>
    public int InventoryDelta { get; init; }

    public int Regressions { get; init; }
    public int NewPasses { get; init; }

    /// <summary>Regressions excluded from the verdict because the test is on the upstream-flake allowlist.</summary>
    public int IgnoredFlakyRegressions { get; init; }

    /// <summary>Baseline failures excluded from the baseline exit-code gate because the test is on the upstream-flake allowlist.</summary>
    public int IgnoredFlakyBaselineFailures { get; init; }

    /// <summary>Round-trip failures on allowlisted tests (regressed or pre-existing).</summary>
    public int IgnoredFlakyRoundTripFailures { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ComparisonStatus ComparisonStatus { get; init; }

    public static TestOutcomeSummary Compute(
        TestRunResult? baseline, TestRunResult? roundTrip, TestComparison? comparison)
    {
        return new TestOutcomeSummary
        {
            BaselineTotal = baseline?.TotalTests ?? 0,
            BaselinePassed = baseline?.Passed ?? 0,
            BaselineFailed = baseline?.Failed ?? 0,
            BaselineTrxFiles = baseline?.TrxFiles.Count ?? 0,
            BaselineUsedConsoleFallback = baseline?.UsedConsoleFallback ?? false,
            RoundTripTotal = roundTrip?.TotalTests ?? 0,
            RoundTripPassed = roundTrip?.Passed ?? 0,
            RoundTripFailed = roundTrip?.Failed ?? 0,
            RoundTripTrxFiles = roundTrip?.TrxFiles.Count ?? 0,
            RoundTripUsedConsoleFallback = roundTrip?.UsedConsoleFallback ?? false,
            InventoryDelta = (roundTrip?.TotalTests ?? 0) - (baseline?.TotalTests ?? 0),
            Regressions = comparison?.Regressions.Count ?? 0,
            NewPasses = comparison?.NewPasses.Count ?? 0,
            IgnoredFlakyRegressions = comparison?.IgnoredFlakyRegressions.Count ?? 0,
            IgnoredFlakyBaselineFailures = comparison?.IgnoredFlakyBaselineFailures.Count ?? 0,
            IgnoredFlakyRoundTripFailures = comparison?.IgnoredFlakyRoundTripFailures.Count ?? 0,
            ComparisonStatus = comparison?.Status ?? ComparisonStatus.Incomplete,
        };
    }
}
