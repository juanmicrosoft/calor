using Calor.Compiler.Migration;
using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Pins the W1 Slice 5a fidelity-core semantics (#776 subset): reverted files stay
/// in the coverage denominator, Gaps/InteropBlocks come from the real conversion
/// loss ledger, and verdict dimensions are reported independently.
/// </summary>
public class FidelityTests
{
    // ---- Coverage: reverted-file-in-denominator accounting ----

    [Fact]
    public void Coverage_RevertedFile_StaysInDenominator_AsFailure()
    {
        var files = new List<FileConversionResult>
        {
            Converted("Lib/A.cs"),                       // native
            ConvertedWithLoss("Lib/B.cs"),               // kept, with losses
            Reverted("Lib/C.cs"),                        // build-recovery revert
            new() { FilePath = "Lib/D.cs", Status = FileStatus.ConversionFailed },
        };

        var cov = ConversionCoverage.Compute(files, excludedFileCount: 0);

        Assert.Equal(4, cov.TotalConvertibleFiles);      // reverted file IS in the denominator
        Assert.Equal(1, cov.ConvertedNative);
        Assert.Equal(1, cov.ConvertedWithLosses);
        Assert.Equal(1, cov.Reverted);
        Assert.Equal(1, cov.FailedConversion);
        Assert.Equal(0.5, cov.CoverageFraction);         // 2 kept / 4 total — revert reduces coverage
        Assert.Equal(0.25, cov.NativeFraction);
    }

    [Fact]
    public void Coverage_AllReverted_IsZero_NotHundred()
    {
        var files = new List<FileConversionResult>
        {
            Reverted("Lib/A.cs"),
            Reverted("Lib/B.cs"),
        };

        var cov = ConversionCoverage.Compute(files, excludedFileCount: 0);

        Assert.Equal(2, cov.TotalConvertibleFiles);
        Assert.Equal(0.0, cov.CoverageFraction);
        Assert.Equal(2, cov.Reverted);
    }

    [Fact]
    public void Coverage_ExcludedFiles_OutsideDenominator_ButReported()
    {
        var files = new List<FileConversionResult>
        {
            Converted("Lib/A.cs"),
            new() { FilePath = "Lib/Gen.cs", Status = FileStatus.Excluded },
        };

        var cov = ConversionCoverage.Compute(files, excludedFileCount: 3);

        Assert.Equal(1, cov.TotalConvertibleFiles);
        Assert.Equal(1.0, cov.CoverageFraction);
        Assert.Equal(4, cov.ExcludedFiles); // 3 pattern-skipped + 1 explicit Excluded status
    }

    // ---- Gaps / InteropBlocks from the Slice-3 loss ledger ----

    [Fact]
    public void ApplyLossLedger_PopulatesGapsAndInteropBlocks()
    {
        var file = new FileConversionResult { FilePath = "Lib/A.cs", Status = FileStatus.Replaced };
        var losses = new List<ConversionLoss>
        {
            Loss(ConversionLossKind.InteropPreserved, "unsafe-block"),
            Loss(ConversionLossKind.InteropPreserved, "operator-overload"),
            Loss(ConversionLossKind.EmitterFallback, "raw-csharp"),
            Loss(ConversionLossKind.FallbackTodo, "goto"),
            Loss(ConversionLossKind.Dropped, "extern-alias"),
            Loss(ConversionLossKind.FallbackTodo, "goto"), // duplicate feature
        };

        file.ApplyLossLedger(losses);

        Assert.Equal(6, file.LossCount);
        Assert.Equal(3, file.InteropBlocks);                       // 2 InteropPreserved + 1 EmitterFallback
        Assert.Equal(["extern-alias", "goto"], file.Gaps);         // distinct, sorted, non-interop kinds
        Assert.Equal(2, file.LossKindCounts["InteropPreserved"]);
        Assert.Equal(2, file.LossKindCounts["FallbackTodo"]);
        Assert.Equal(1, file.LossKindCounts["Dropped"]);
        Assert.Equal(1, file.LossKindCounts["EmitterFallback"]);
        Assert.Equal(6, file.Losses.Count);
        Assert.False(file.ConvertedNative); // losses => not native
    }

    [Fact]
    public void ApplyLossLedger_EmptyLedger_MeansNativeConversion()
    {
        var file = new FileConversionResult { FilePath = "Lib/A.cs", Status = FileStatus.Replaced };

        file.ApplyLossLedger([]);

        Assert.Equal(0, file.LossCount);
        Assert.Equal(0, file.InteropBlocks);
        Assert.Empty(file.Gaps);
        Assert.True(file.ConvertedNative);
    }

    [Fact]
    public void Coverage_AggregatesLedgerMetrics_AcrossFiles()
    {
        var a = Converted("Lib/A.cs");
        a.ApplyLossLedger([
            Loss(ConversionLossKind.InteropPreserved, "unsafe-block"),
            Loss(ConversionLossKind.FallbackTodo, "goto"),
        ]);
        var b = Converted("Lib/B.cs");
        b.ApplyLossLedger([
            Loss(ConversionLossKind.InteropPreserved, "pointer-arith"),
            Loss(ConversionLossKind.FallbackTodo, "goto"),
            Loss(ConversionLossKind.PreprocessorStripped, "if-directive"),
        ]);

        var cov = ConversionCoverage.Compute([a, b], excludedFileCount: 0);

        Assert.Equal(0, cov.ConvertedNative);
        Assert.Equal(2, cov.ConvertedWithLosses);
        Assert.Equal(2, cov.TotalInteropBlocks);
        Assert.Equal(["goto", "if-directive"], cov.DistinctGaps); // deduped across files
        Assert.Equal(2, cov.LossKindCounts["InteropPreserved"]);
        Assert.Equal(2, cov.LossKindCounts["FallbackTodo"]);
        Assert.Equal(1, cov.LossKindCounts["PreprocessorStripped"]);
    }

    // ---- Separated verdict dimensions ----

    [Fact]
    public void Fidelity_DimensionsAreIndependent_BuildFailureDoesNotMaskCoverage()
    {
        var report = new RoundTripReport
        {
            ProjectName = "P",
            FileResults =
            [
                Converted("Lib/A.cs"),
                Reverted("Lib/B.cs"),
            ],
            BuildResult = new BuildResult { Succeeded = false, ExitCode = 1, Errors = ["x.cs(1,1): error CS0000: boom"] },
            Baseline = Run(passed: 10, failed: 0, trxFiles: 2),
            RoundTripTests = null,
            Comparison = new TestComparison { Status = ComparisonStatus.BuildFailed, BaselineTotal = 10, BaselinePassed = 10 },
        };

        var fidelity = ProjectFidelity.Compute(report);

        // Coverage dimension still computed despite build failure
        Assert.Equal(2, fidelity.Coverage.TotalConvertibleFiles);
        Assert.Equal(0.5, fidelity.Coverage.CoverageFraction);
        // Build dimension carries the revert count
        Assert.False(fidelity.Build.Succeeded);
        Assert.Equal(1, fidelity.Build.RecoveryRevertedFiles);
        Assert.Equal(1, fidelity.Build.ErrorCount);
        // Test dimension reports incompleteness rather than a fake pass
        Assert.Equal(ComparisonStatus.BuildFailed, fidelity.Tests.ComparisonStatus);
        Assert.Equal(10, fidelity.Tests.BaselineTotal);
        Assert.Equal(0, fidelity.Tests.RoundTripTotal);
    }

    [Fact]
    public void Fidelity_InventoryShrink_IsReportedAsNegativeDelta()
    {
        var report = new RoundTripReport
        {
            ProjectName = "P",
            FileResults = [Converted("Lib/A.cs")],
            BuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            Baseline = Run(passed: 100, failed: 0, trxFiles: 3),
            RoundTripTests = Run(passed: 60, failed: 0, trxFiles: 1),
            Comparison = new TestComparison
            {
                Status = ComparisonStatus.Pass,
                BaselineTotal = 100, BaselinePassed = 100,
                RoundTripTotal = 60, RoundTripPassed = 60,
            },
        };

        var fidelity = ProjectFidelity.Compute(report);

        Assert.Equal(-40, fidelity.Tests.InventoryDelta); // shrink is visible, never silent
        Assert.Equal(3, fidelity.Tests.BaselineTrxFiles);
        Assert.Equal(1, fidelity.Tests.RoundTripTrxFiles);
    }

    [Fact]
    public void Fidelity_ConsoleFallback_IsFlagged()
    {
        var report = new RoundTripReport
        {
            ProjectName = "P",
            FileResults = [Converted("Lib/A.cs")],
            BuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            Baseline = new TestRunResult { ExitCode = 0, TotalTests = 5, Passed = 5, UsedConsoleFallback = true },
            RoundTripTests = Run(passed: 5, failed: 0, trxFiles: 1),
            Comparison = new TestComparison { Status = ComparisonStatus.Pass },
        };

        var fidelity = ProjectFidelity.Compute(report);

        Assert.True(fidelity.Tests.BaselineUsedConsoleFallback);
        Assert.False(fidelity.Tests.RoundTripUsedConsoleFallback);
        Assert.Equal(0, fidelity.Tests.BaselineTrxFiles);
    }

    // ---- helpers ----

    private static FileConversionResult Converted(string path) =>
        new() { FilePath = path, Status = FileStatus.Replaced, ConversionSuccess = true };

    private static FileConversionResult ConvertedWithLoss(string path)
    {
        var f = Converted(path);
        f.ApplyLossLedger([Loss(ConversionLossKind.InteropPreserved, "unsafe-block")]);
        return f;
    }

    private static FileConversionResult Reverted(string path) =>
        new()
        {
            FilePath = path,
            Status = FileStatus.Reverted,
            ConversionSuccess = true,
            RevertReason = "build-recovery round 1: build error in round-tripped output",
        };

    private static ConversionLoss Loss(ConversionLossKind kind, string feature) =>
        new() { Kind = kind, Feature = feature, Description = $"lost: {feature}" };

    private static TestRunResult Run(int passed, int failed, int trxFiles) =>
        new()
        {
            ExitCode = failed > 0 ? 1 : 0,
            TotalTests = passed + failed,
            Passed = passed,
            Failed = failed,
            TrxFiles = Enumerable.Range(1, trxFiles).Select(i => $"TestResults/r{i}.trx").ToList(),
        };
}

/// <summary>
/// #837 review M-1: `**/*.g.cs`-style exclude patterns compared the `*`
/// literally and matched nothing — generated files entered the coverage
/// denominator the fidelity gate thresholds on. Pins the star-suffix branch.
/// </summary>
public class GlobExcludeTests
{
    private static bool MatchGlob(string path, string pattern)
    {
        var method = typeof(RoundTripPipeline).GetMethod(
            "MatchGlob",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object[] { path, pattern })!;
    }

    [Theory]
    [InlineData("src/Lib/Skip.g.cs", "**/*.g.cs", true)]
    [InlineData("src/Lib/Model.generated.cs", "**/*.generated.cs", true)]
    [InlineData("src/Lib/Form.Designer.cs", "**/*.Designer.cs", true)]
    [InlineData("src/Lib/Native.cs", "**/*.g.cs", false)]
    [InlineData("src/Lib/regular.cs", "**/*.Designer.cs", false)]
    [InlineData("src/obj/Debug/net10.0/x.cs", "**/obj/**", true)]
    [InlineData("src/Lib/AssemblyInfo.cs", "**/AssemblyInfo.cs", true)]
    public void MatchGlob_StarSuffixPatterns_MatchByExtension(string path, string pattern, bool expected)
        => Assert.Equal(expected, MatchGlob(path, pattern));
}
