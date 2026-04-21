namespace Calor.Compiler.Experiments;

/// <summary>
/// Statistical primitives for the AB evaluator (§4.2 of the Calor-native type-system
/// research plan). All functions operate on per-program paired samples — the <i>unit
/// of analysis is the program</i>, not the run — because most benchmark metrics are
/// deterministic (same program yields the same number on repeated runs).
///
/// Implementations here are deliberately self-contained and validated by
/// known-input/known-output unit tests. They do NOT reuse the parametric t-test in
/// <c>tests/Calor.Evaluation/Core/StatisticalAnalysis.cs</c> (that module is a test
/// project and not referenced by the compiler).
/// </summary>
public static class Stats
{
    // ========================================================================
    // Paired Wilcoxon signed-rank test
    // ========================================================================

    /// <summary>
    /// Paired Wilcoxon signed-rank test on per-program differences
    /// (<c>candidate_i − baseline_i</c>). Returns the two-sided p-value under the null
    /// hypothesis that the median difference is zero. Robust to non-normality —
    /// appropriate for deterministic metrics where the distribution of differences
    /// across the corpus is unknown.
    ///
    /// For large N (≥ 20) uses the normal approximation with continuity correction.
    /// For smaller N, still uses the normal approximation — accepted trade-off since
    /// our minimum Tier 1 corpus sizes (25+ programs) are always above 20.
    ///
    /// Zero differences are excluded ("zero-exclusion" method, the most common
    /// convention). Ties in absolute-value ranks receive average ranks.
    /// </summary>
    public static double WilcoxonSignedRankPValue(IReadOnlyList<double> baseline, IReadOnlyList<double> candidate)
    {
        if (baseline.Count != candidate.Count)
            throw new ArgumentException("baseline and candidate must have the same length.");

        // Compute non-zero absolute differences + their signs.
        var absDiffs = new List<(double abs, int sign, int origIndex)>();
        for (int i = 0; i < baseline.Count; i++)
        {
            var d = candidate[i] - baseline[i];
            if (d == 0) continue;
            absDiffs.Add((Math.Abs(d), Math.Sign(d), i));
        }

        int n = absDiffs.Count;
        if (n == 0) return 1.0; // all differences zero → no evidence against null

        // Sort by absolute difference and assign ranks (averaging ties).
        var sorted = absDiffs.OrderBy(x => x.abs).ToList();
        var ranks = new double[n];
        int k = 0;
        while (k < n)
        {
            int j = k;
            while (j + 1 < n && sorted[j + 1].abs == sorted[k].abs) j++;
            // Indices [k..j] all share the same abs value; assign average rank.
            double avgRank = (k + j + 2) / 2.0; // ranks are 1-based; (k+1 + j+1) / 2
            for (int m = k; m <= j; m++) ranks[m] = avgRank;
            k = j + 1;
        }

        // W+ = sum of positive-signed ranks.
        double wPlus = 0;
        for (int i = 0; i < n; i++)
        {
            if (sorted[i].sign > 0) wPlus += ranks[i];
        }

        // Normal approximation.
        double meanW = n * (n + 1) / 4.0;
        double sdW = Math.Sqrt(n * (n + 1) * (2 * n + 1) / 24.0);

        // Two-sided p-value with continuity correction.
        double z = (Math.Abs(wPlus - meanW) - 0.5) / sdW;
        if (z < 0) z = 0;
        return 2 * (1 - NormalCdf(z));
    }

    // ========================================================================
    // Bootstrap percentile CI on relative mean difference
    // ========================================================================

    /// <summary>
    /// Percentile bootstrap confidence interval on the <b>relative</b> mean difference
    /// <c>(mean(candidate) - mean(baseline)) / mean(baseline)</c>. Returns the
    /// (lower, upper) bounds at the given confidence level (default 0.95 = 95% CI).
    ///
    /// Nonparametric — makes no distributional assumption. Uses per-program paired
    /// resampling with replacement.
    /// </summary>
    public static (double Lower, double Upper) PairedRelativeBootstrapCI(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        int resamples = 2000,
        double confidenceLevel = 0.95,
        int? seed = null)
    {
        if (baseline.Count != candidate.Count)
            throw new ArgumentException("baseline and candidate must have the same length.");
        if (baseline.Count == 0)
            throw new ArgumentException("baseline must be non-empty.");

        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        int n = baseline.Count;
        var relativeEffects = new double[resamples];

        for (int r = 0; r < resamples; r++)
        {
            double baseSum = 0, candSum = 0;
            for (int i = 0; i < n; i++)
            {
                int idx = rng.Next(n);
                baseSum += baseline[idx];
                candSum += candidate[idx];
            }
            double baseMean = baseSum / n;
            double candMean = candSum / n;
            relativeEffects[r] = baseMean == 0 ? 0 : (candMean - baseMean) / baseMean;
        }

        Array.Sort(relativeEffects);
        double tail = (1 - confidenceLevel) / 2.0;
        int lowerIdx = (int)Math.Floor(tail * resamples);
        int upperIdx = (int)Math.Ceiling((1 - tail) * resamples) - 1;
        lowerIdx = Math.Clamp(lowerIdx, 0, resamples - 1);
        upperIdx = Math.Clamp(upperIdx, 0, resamples - 1);
        return (relativeEffects[lowerIdx], relativeEffects[upperIdx]);
    }

    // ========================================================================
    // TOST (two one-sided tests) for non-inferiority
    // ========================================================================

    /// <summary>
    /// Two One-Sided Tests (TOST) for non-inferiority on the <b>relative</b>
    /// difference. Tests the null hypothesis that the true relative effect falls
    /// outside the interval [-tolerance, +tolerance]. Returns the p-value against
    /// the non-inferiority null — if &lt; α the guard is confirmed non-inferior
    /// (within tolerance).
    ///
    /// Simplification: uses the bootstrap CI. If the (1 - 2α) two-sided CI lies
    /// entirely within [-tolerance, +tolerance], non-inferiority is confirmed at
    /// level α. That's the equivalent of the TOST result but easier to implement
    /// atop the existing bootstrap.
    /// </summary>
    public static bool PairedIsNonInferior(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        double toleranceRelative,
        double alpha = 0.05,
        int resamples = 2000,
        int? seed = null)
    {
        // (1 - 2α) two-sided CI → TOST at α.
        double confidence = 1 - 2 * alpha;
        var (lower, upper) = PairedRelativeBootstrapCI(
            baseline, candidate, resamples, confidence, seed);
        return lower >= -toleranceRelative && upper <= toleranceRelative;
    }

    // ========================================================================
    // Holm-Bonferroni multiple-comparisons correction
    // ========================================================================

    /// <summary>
    /// Holm-Bonferroni step-down procedure. Controls family-wise error rate (FWER)
    /// at the specified α. Returns an array of booleans indicating whether each
    /// hypothesis's p-value is rejected (significant) after correction, in the
    /// original input order.
    ///
    /// Sort p-values ascending. Reject p_(1) if ≤ α/m; reject p_(2) if ≤ α/(m-1);
    /// continue until a non-rejection. After the first non-rejection, all remaining
    /// are accepted (null not rejected).
    /// </summary>
    public static bool[] HolmBonferroni(IReadOnlyList<double> pValues, double alpha = 0.05)
    {
        int m = pValues.Count;
        if (m == 0) return Array.Empty<bool>();

        // Pair p-values with original index, sort ascending by p-value.
        var indexed = pValues
            .Select((p, i) => (p, i))
            .OrderBy(x => x.p)
            .ToList();

        var rejected = new bool[m];
        bool stopReject = false;
        for (int rank = 0; rank < m; rank++)
        {
            var (p, origIdx) = indexed[rank];
            if (!stopReject && p <= alpha / (m - rank))
            {
                rejected[origIdx] = true;
            }
            else
            {
                stopReject = true; // subsequent hypotheses all accepted
            }
        }
        return rejected;
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    /// <summary>
    /// Standard normal CDF via Abramowitz &amp; Stegun 26.2.17 approximation.
    /// Accuracy ~7.5×10⁻⁸; good enough for test-calibration purposes.
    /// </summary>
    public static double NormalCdf(double x)
    {
        double sign = x < 0 ? -1 : 1;
        x = Math.Abs(x) / Math.Sqrt(2);
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;
        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return 0.5 * (1.0 + sign * y);
    }

    public static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < values.Count; i++) sum += values[i];
        return sum / values.Count;
    }
}
