using Calor.Compiler.Experiments;
using Xunit;

namespace Calor.Compiler.Tests.Experiments;

/// <summary>
/// Unit tests for the statistical primitives used by the AB evaluator (§4.2).
/// Known-input/known-output tests — each one validates a specific mathematical
/// property that downstream decision logic depends on.
/// </summary>
public class StatsTests
{
    // ========================================================================
    // Wilcoxon signed-rank
    // ========================================================================

    [Fact]
    public void Wilcoxon_IdenticalSamples_PValueOne()
    {
        var data = new double[] { 1, 2, 3, 4, 5 };
        Assert.Equal(1.0, Stats.WilcoxonSignedRankPValue(data, data), precision: 3);
    }

    [Fact]
    public void Wilcoxon_LargePositiveShift_RejectsNull()
    {
        // 30 paired observations with a clear +2 shift.
        var rng = new Random(42);
        var baseline = new List<double>();
        var candidate = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            double b = rng.NextDouble() * 10;
            baseline.Add(b);
            candidate.Add(b + 2); // deterministic +2 shift
        }

        double p = Stats.WilcoxonSignedRankPValue(baseline, candidate);
        Assert.True(p < 0.01, $"Large consistent positive shift should reject null; got p={p}");
    }

    [Fact]
    public void Wilcoxon_LargeNegativeShift_RejectsNull()
    {
        var baseline = Enumerable.Range(0, 30).Select(i => (double)i).ToList();
        var candidate = baseline.Select(v => v - 5).ToList();

        double p = Stats.WilcoxonSignedRankPValue(baseline, candidate);
        Assert.True(p < 0.01, $"Large consistent negative shift should reject null; got p={p}");
    }

    [Fact]
    public void Wilcoxon_MismatchedLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Stats.WilcoxonSignedRankPValue(new double[] { 1, 2 }, new double[] { 3 }));
    }

    [Fact]
    public void Wilcoxon_AllZeroDifferences_PValueOne()
    {
        var data = new double[] { 1, 2, 3 };
        Assert.Equal(1.0, Stats.WilcoxonSignedRankPValue(data, data), precision: 3);
    }

    [Fact]
    public void Wilcoxon_HandlesTiedRanks()
    {
        // Differences of equal absolute value — rank averaging must not crash.
        var baseline = new double[] { 0, 0, 0, 0 };
        var candidate = new double[] { 1, 1, -1, -1 }; // differences: +1, +1, -1, -1 — all |1|

        // With 2 positive and 2 negative ties, W+ ≈ E[W+], so p ≈ 1.
        double p = Stats.WilcoxonSignedRankPValue(baseline, candidate);
        Assert.True(p > 0.5, $"Balanced tied signs should have high p-value; got {p}");
    }

    // ========================================================================
    // Bootstrap CI
    // ========================================================================

    [Fact]
    public void Bootstrap_IdenticalSamples_CiNearZero()
    {
        var data = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var (lower, upper) = Stats.PairedRelativeBootstrapCI(data, data, resamples: 1000, seed: 42);
        // Candidate identical to baseline → relative effect = 0 on every bootstrap sample.
        Assert.Equal(0, lower, precision: 6);
        Assert.Equal(0, upper, precision: 6);
    }

    [Fact]
    public void Bootstrap_PositiveShift_CiContainsTrueEffect()
    {
        // True relative effect on a scalar shift is not straightforward (depends on
        // baseline mean), but for a fixed multiplicative effect the true relative is exact.
        var baseline = Enumerable.Range(1, 30).Select(i => (double)i).ToList();
        var candidate = baseline.Select(v => v * 1.10).ToList(); // +10% everywhere
        var (lower, upper) = Stats.PairedRelativeBootstrapCI(baseline, candidate, resamples: 2000, seed: 42);
        // The true effect is +0.10; CI should contain it.
        Assert.InRange(0.10, lower - 0.01, upper + 0.01);
    }

    [Fact]
    public void Bootstrap_MismatchedLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Stats.PairedRelativeBootstrapCI(new double[] { 1 }, new double[] { 1, 2 }));
    }

    [Fact]
    public void Bootstrap_SeededRunsAreReproducible()
    {
        var baseline = new double[] { 1, 2, 3, 4, 5 };
        var candidate = new double[] { 2, 3, 4, 5, 6 };
        var r1 = Stats.PairedRelativeBootstrapCI(baseline, candidate, resamples: 500, seed: 99);
        var r2 = Stats.PairedRelativeBootstrapCI(baseline, candidate, resamples: 500, seed: 99);
        Assert.Equal(r1, r2);
    }

    // ========================================================================
    // TOST non-inferiority
    // ========================================================================

    [Fact]
    public void TOST_IdenticalData_ConfirmsNonInferior()
    {
        var data = Enumerable.Range(1, 30).Select(i => (double)i).ToList();
        Assert.True(Stats.PairedIsNonInferior(data, data, toleranceRelative: 0.05, seed: 42));
    }

    [Fact]
    public void TOST_LargeRegression_NotNonInferior()
    {
        var baseline = Enumerable.Range(1, 30).Select(i => (double)i).ToList();
        var candidate = baseline.Select(v => v * 0.50).ToList(); // -50% regression
        Assert.False(Stats.PairedIsNonInferior(baseline, candidate, toleranceRelative: 0.05, seed: 42));
    }

    // ========================================================================
    // Holm-Bonferroni
    // ========================================================================

    [Fact]
    public void HolmBonferroni_EmptyInput_Empty()
    {
        Assert.Empty(Stats.HolmBonferroni(Array.Empty<double>()));
    }

    [Fact]
    public void HolmBonferroni_AllLargePValues_AllAccepted()
    {
        var result = Stats.HolmBonferroni(new double[] { 0.5, 0.3, 0.7 });
        Assert.All(result, r => Assert.False(r));
    }

    [Fact]
    public void HolmBonferroni_OneStrongRejection_StepDown()
    {
        // p = [0.001, 0.5, 0.5] with m=3 at α=0.05:
        //   rank 0: 0.001 ≤ 0.05/3 = 0.0167 → reject
        //   rank 1: 0.5 ≤ 0.05/2 = 0.025 → fail; stop-reject kicks in
        //   rank 2: stays accepted
        var result = Stats.HolmBonferroni(new double[] { 0.001, 0.5, 0.5 });
        Assert.True(result[0]);
        Assert.False(result[1]);
        Assert.False(result[2]);
    }

    [Fact]
    public void HolmBonferroni_OriginalOrderPreserved()
    {
        // Sort-by-p internally, but must return results in the input order.
        var result = Stats.HolmBonferroni(new double[] { 0.9, 0.001, 0.05 });
        // Only index 1 (p=0.001) should pass: 0.001 ≤ 0.05/3. Next step: 0.05 ≤ 0.05/2 = 0.025? No → stop.
        Assert.False(result[0]);
        Assert.True(result[1]);
        Assert.False(result[2]);
    }

    // ========================================================================
    // NormalCdf
    // ========================================================================

    [Fact]
    public void NormalCdf_AtZero_Half()
    {
        Assert.Equal(0.5, Stats.NormalCdf(0), precision: 6);
    }

    [Fact]
    public void NormalCdf_Symmetric()
    {
        for (double x = 0.5; x <= 3; x += 0.5)
        {
            Assert.Equal(1.0, Stats.NormalCdf(x) + Stats.NormalCdf(-x), precision: 5);
        }
    }

    [Fact]
    public void NormalCdf_KnownValues()
    {
        // Standard table values.
        Assert.Equal(0.8413, Stats.NormalCdf(1.0), precision: 3);
        Assert.Equal(0.9772, Stats.NormalCdf(2.0), precision: 3);
        Assert.Equal(0.9987, Stats.NormalCdf(3.0), precision: 3);
    }
}
