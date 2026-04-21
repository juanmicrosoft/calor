using Xunit;

namespace Calor.Experimental.Tests.Framework;

/// <summary>
/// Tests for the coverage audit quality bar. Each test constructs a synthetic
/// <see cref="MicroValidationSet"/> with known program counts and verifies the
/// audit's pass/fail verdict plus the specific violation messages.
/// </summary>
public class CoverageAuditTests
{
    private static MicroValidationSet BuildSet(int positive, int negative, int edge)
    {
        var manifest = new MicroValidationManifest
        {
            HypothesisId = "TEST",
            ExperimentalFlag = "test-flag",
            ExpectedDiagnosticCode = "Calor0000"
        };

        // Use synthetic file paths. The audit doesn't dereference them.
        return new MicroValidationSet(
            manifest,
            Enumerable.Range(0, positive).Select(i => $"pos_{i}.calr").ToList(),
            Enumerable.Range(0, negative).Select(i => $"neg_{i}.calr").ToList(),
            Enumerable.Range(0, edge).Select(i => $"edge_{i}.calr").ToList());
    }

    // ========================================================================
    // Happy path
    // ========================================================================

    [Fact]
    public void MeetsAllQualityBars_Passes()
    {
        // 7 positive (47%) + 5 negative (33%) + 3 edge (20%) = 15 total
        var set = BuildSet(positive: 7, negative: 5, edge: 3);
        var result = CoverageAudit.Evaluate(set);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void AtMinimumSize_Passes()
    {
        var set = BuildSet(positive: 7, negative: 5, edge: 3); // 15 total
        Assert.True(CoverageAudit.Evaluate(set).IsValid);
    }

    [Fact]
    public void AtMaximumSize_Passes()
    {
        // 20 positive (40%) + 15 negative (30%) + 15 edge (30%) = 50 total
        var set = BuildSet(positive: 20, negative: 15, edge: 15);
        Assert.True(CoverageAudit.Evaluate(set).IsValid);
    }

    [Fact]
    public void WellAboveMinimumRatios_Passes()
    {
        var set = BuildSet(positive: 10, negative: 10, edge: 5); // 25 total
        Assert.True(CoverageAudit.Evaluate(set).IsValid);
    }

    // ========================================================================
    // Under-coverage (too few programs)
    // ========================================================================

    [Fact]
    public void EmptySet_ReportsUnderCoverage()
    {
        var set = BuildSet(0, 0, 0);
        var result = CoverageAudit.Evaluate(set);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("Under-coverage"));
    }

    [Fact]
    public void FourteenPrograms_ReportsUnderCoverage()
    {
        var set = BuildSet(positive: 6, negative: 5, edge: 3); // 14 total — just under
        var result = CoverageAudit.Evaluate(set);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("Under-coverage"));
    }

    // ========================================================================
    // Over-coverage (too many programs)
    // ========================================================================

    [Fact]
    public void FiftyOnePrograms_ReportsOverCoverage()
    {
        var set = BuildSet(positive: 21, negative: 15, edge: 15); // 51 total
        var result = CoverageAudit.Evaluate(set);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("Over-coverage"));
    }

    // ========================================================================
    // Ratio violations
    // ========================================================================

    [Fact]
    public void InsufficientPositiveRatio_ReportsViolation()
    {
        // 5/15 = 33% positive, below 40% minimum
        var set = BuildSet(positive: 5, negative: 7, edge: 3);
        var result = CoverageAudit.Evaluate(set);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("positive"));
    }

    [Fact]
    public void InsufficientNegativeRatio_ReportsViolation()
    {
        // 3/15 = 20% negative, below 30% minimum
        var set = BuildSet(positive: 9, negative: 3, edge: 3);
        var result = CoverageAudit.Evaluate(set);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("negative"));
    }

    [Fact]
    public void InsufficientEdgeRatio_ReportsViolation()
    {
        // 1/15 = 7% edge, below 10% minimum
        var set = BuildSet(positive: 9, negative: 5, edge: 1);
        var result = CoverageAudit.Evaluate(set);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("edge"));
    }

    [Fact]
    public void MultipleViolations_AllReported()
    {
        // 1 positive + 1 negative + 13 edge = 15 total
        // positive ratio 7%, negative ratio 7% — both below their minimums
        var set = BuildSet(positive: 1, negative: 1, edge: 13);
        var result = CoverageAudit.Evaluate(set);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("positive"));
        Assert.Contains(result.Violations, v => v.Contains("negative"));
    }

    // ========================================================================
    // Error-message content — the messages should tell the reader how to fix
    // ========================================================================

    [Fact]
    public void Violation_IncludesRequiredCount()
    {
        var set = BuildSet(positive: 5, negative: 7, edge: 3); // 5/15 positive = 33%
        var result = CoverageAudit.Evaluate(set);
        var positiveViolation = result.Violations.Single(v => v.Contains("positive"));
        // Required = ceil(15 * 0.40) = 6
        Assert.Contains("6 programs", positiveViolation);
    }
}
