namespace Calor.Evaluation.Core;

/// <summary>
/// Provides statistical analysis for benchmark results including confidence intervals,
/// effect sizes, and significance testing.
/// </summary>
public static class StatisticalAnalysis
{
    /// <summary>
    /// Calculates the mean of a sequence of values.
    /// </summary>
    public static double Mean(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : list.Average();
    }

    /// <summary>
    /// Calculates the sample standard deviation.
    /// </summary>
    public static double StandardDeviation(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count < 2) return 0;

        var mean = list.Average();
        var sumSquares = list.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquares / (list.Count - 1));
    }

    /// <summary>
    /// Calculates the standard error of the mean.
    /// </summary>
    public static double StandardError(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count < 2) return 0;

        return StandardDeviation(list) / Math.Sqrt(list.Count);
    }

    /// <summary>
    /// Calculates a 95% confidence interval for the mean.
    /// </summary>
    public static ConfidenceInterval CalculateConfidenceInterval(IEnumerable<double> values, double confidenceLevel = 0.95)
    {
        var list = values.ToList();
        if (list.Count < 2)
        {
            var val = list.Count == 1 ? list[0] : 0;
            return new ConfidenceInterval(val, val, val);
        }

        var mean = list.Average();
        var stdErr = StandardError(list);

        // Use t-distribution critical value for 95% CI
        // For large samples (n > 30), use z = 1.96
        // For smaller samples, approximate with t-value
        var criticalValue = list.Count > 30 ? 1.96 : GetTCriticalValue(list.Count - 1, confidenceLevel);

        var margin = criticalValue * stdErr;

        return new ConfidenceInterval(
            mean - margin,
            mean,
            mean + margin);
    }

    /// <summary>
    /// Calculates Cohen's d effect size between two groups.
    /// </summary>
    public static double CohensD(IEnumerable<double> group1, IEnumerable<double> group2)
    {
        var list1 = group1.ToList();
        var list2 = group2.ToList();

        if (list1.Count < 2 || list2.Count < 2) return 0;

        var mean1 = list1.Average();
        var mean2 = list2.Average();

        var var1 = list1.Sum(v => (v - mean1) * (v - mean1)) / (list1.Count - 1);
        var var2 = list2.Sum(v => (v - mean2) * (v - mean2)) / (list2.Count - 1);

        // Pooled standard deviation
        var pooledStd = Math.Sqrt(((list1.Count - 1) * var1 + (list2.Count - 1) * var2) / (list1.Count + list2.Count - 2));

        return pooledStd > 0 ? (mean1 - mean2) / pooledStd : 0;
    }

    /// <summary>
    /// Performs a paired t-test and returns the p-value.
    /// </summary>
    public static PairedTTestResult PairedTTest(IEnumerable<double> group1, IEnumerable<double> group2)
    {
        var list1 = group1.ToList();
        var list2 = group2.ToList();

        if (list1.Count != list2.Count)
            throw new ArgumentException("Groups must have the same size for paired t-test");

        if (list1.Count < 2)
            return new PairedTTestResult(0, 1.0, false);

        // Calculate differences
        var differences = list1.Zip(list2, (a, b) => a - b).ToList();
        var meanDiff = differences.Average();
        var stdDiff = StandardDeviation(differences);

        if (stdDiff == 0)
            return new PairedTTestResult(0, meanDiff == 0 ? 1.0 : 0.0, meanDiff != 0);

        // t-statistic
        var tStat = meanDiff / (stdDiff / Math.Sqrt(differences.Count));

        // Calculate p-value (two-tailed) using approximation
        var df = differences.Count - 1;
        var pValue = CalculatePValue(Math.Abs(tStat), df);

        return new PairedTTestResult(tStat, pValue, pValue < 0.05);
    }

    /// <summary>
    /// Calculates the geometric mean of a sequence of positive values.
    /// </summary>
    public static double GeometricMean(IEnumerable<double> values)
    {
        var list = values.Where(v => v > 0).ToList();
        if (list.Count == 0) return 0;

        var logSum = list.Sum(v => Math.Log(v));
        return Math.Exp(logSum / list.Count);
    }

    /// <summary>
    /// Calculates the median of a sequence of values.
    /// </summary>
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }

    /// <summary>
    /// Interprets Cohen's d effect size.
    /// </summary>
    public static string InterpretEffectSize(double cohensD)
    {
        var absD = Math.Abs(cohensD);
        return absD switch
        {
            < 0.2 => "negligible",
            < 0.5 => "small",
            < 0.8 => "medium",
            _ => "large"
        };
    }

    /// <summary>
    /// Gets t-distribution critical value for given degrees of freedom and confidence level.
    /// Uses pre-computed values for common cases.
    /// </summary>
    private static double GetTCriticalValue(int degreesOfFreedom, double confidenceLevel)
    {
        // Common t-values for 95% confidence (two-tailed)
        var tValues = new Dictionary<int, double>
        {
            [1] = 12.706,
            [2] = 4.303,
            [3] = 3.182,
            [4] = 2.776,
            [5] = 2.571,
            [6] = 2.447,
            [7] = 2.365,
            [8] = 2.306,
            [9] = 2.262,
            [10] = 2.228,
            [15] = 2.131,
            [20] = 2.086,
            [25] = 2.060,
            [30] = 2.042
        };

        if (tValues.TryGetValue(degreesOfFreedom, out var value))
            return value;

        // Interpolate or use large-sample approximation
        if (degreesOfFreedom > 30)
            return 1.96;

        // Find nearest values and interpolate
        var lower = tValues.Keys.Where(k => k < degreesOfFreedom).DefaultIfEmpty(1).Max();
        var upper = tValues.Keys.Where(k => k > degreesOfFreedom).DefaultIfEmpty(30).Min();

        var ratio = (double)(degreesOfFreedom - lower) / (upper - lower);
        return tValues[lower] + ratio * (tValues[upper] - tValues[lower]);
    }

    /// <summary>
    /// Approximates the two-tailed p-value from a t-statistic.
    /// Uses a simple approximation suitable for benchmark analysis.
    /// </summary>
    private static double CalculatePValue(double tStat, int degreesOfFreedom)
    {
        // Use approximation based on Student's t-distribution
        // For |t| > 4, p is essentially 0
        if (Math.Abs(tStat) > 4)
            return 0.0001;

        // Simple approximation using the normal CDF for large df
        if (degreesOfFreedom > 30)
        {
            return 2 * (1 - NormalCDF(Math.Abs(tStat)));
        }

        // For smaller df, use a rougher approximation
        // This is sufficient for benchmark purposes
        var x = Math.Abs(tStat);
        var p = Math.Exp(-0.717 * x - 0.416 * x * x);
        return Math.Min(1.0, 2 * p);
    }

    /// <summary>
    /// Approximates the standard normal CDF using Abramowitz and Stegun.
    /// </summary>
    private static double NormalCDF(double x)
    {
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var sign = x < 0 ? -1 : 1;
        x = Math.Abs(x) / Math.Sqrt(2);

        var t = 1.0 / (1.0 + p * x);
        var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return 0.5 * (1.0 + sign * y);
    }
}

/// <summary>
/// Represents a confidence interval with lower bound, mean, and upper bound.
/// </summary>
public record ConfidenceInterval(double Lower, double Mean, double Upper)
{
    /// <summary>
    /// The margin of error (half-width of the interval).
    /// </summary>
    public double MarginOfError => (Upper - Lower) / 2;

    /// <summary>
    /// Returns true if the interval excludes a given value (typically 0 or 1).
    /// </summary>
    public bool Excludes(double value) => value < Lower || value > Upper;

    public override string ToString() => $"[{Lower:F3}, {Upper:F3}]";
}

/// <summary>
/// Result of a paired t-test.
/// </summary>
public record PairedTTestResult(double TStatistic, double PValue, bool IsSignificant)
{
    public override string ToString() => $"t={TStatistic:F3}, p={PValue:F4}, significant={IsSignificant}";
}

/// <summary>
/// Complete statistical summary for a metric across multiple runs.
/// </summary>
public record StatisticalSummary
{
    public required string Category { get; init; }
    public required string MetricName { get; init; }
    public int SampleCount { get; init; }

    // Calor statistics
    public double CalorMean { get; init; }
    public double CalorStdDev { get; init; }
    public ConfidenceInterval CalorCI { get; init; } = new(0, 0, 0);

    // C# statistics
    public double CSharpMean { get; init; }
    public double CSharpStdDev { get; init; }
    public ConfidenceInterval CSharpCI { get; init; } = new(0, 0, 0);

    // Comparison statistics
    public double AdvantageRatioMean { get; init; }
    public ConfidenceInterval AdvantageRatioCI { get; init; } = new(0, 0, 0);
    public double CohensD { get; init; }
    public string EffectSizeInterpretation { get; init; } = "unknown";
    public PairedTTestResult? TTest { get; init; }

    /// <summary>
    /// Returns true if Calor has a statistically significant advantage.
    /// </summary>
    public bool CalorSignificantlyBetter => TTest?.IsSignificant == true && AdvantageRatioMean > 1.0;

    /// <summary>
    /// Returns true if C# has a statistically significant advantage.
    /// </summary>
    public bool CSharpSignificantlyBetter => TTest?.IsSignificant == true && AdvantageRatioMean < 1.0;
}
