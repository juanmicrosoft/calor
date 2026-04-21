namespace Calor.Experimental.Tests.Framework;

/// <summary>
/// Evaluates whether a <see cref="MicroValidationSet"/> meets the quality bar
/// from §5.0e of <c>docs/plans/calor-native-type-system-v2.md</c>:
///
/// - Size: 15 ≤ total ≤ 50.
/// - Positive cases: ≥ 40% of total.
/// - Negative cases: ≥ 30% of total.
/// - Edge cases: ≥ 10% of total.
///
/// Fewer than 15 programs is under-coverage; more than 50 invites teach-to-the-test.
/// The three-way distribution enforces that the set tests both the feature's target
/// behavior (positives), its discipline against false positives (negatives), and its
/// boundary behavior (edge cases).
/// </summary>
public static class CoverageAudit
{
    public const int MinimumTotalPrograms = 15;
    public const int MaximumTotalPrograms = 50;
    public const double MinimumPositiveRatio = 0.40;
    public const double MinimumNegativeRatio = 0.30;
    public const double MinimumEdgeRatio = 0.10;

    public static Result Evaluate(MicroValidationSet set)
    {
        if (set is null)
            throw new ArgumentNullException(nameof(set));

        var violations = new List<string>();
        var total = set.TotalPrograms;

        if (total < MinimumTotalPrograms)
        {
            violations.Add(
                $"Under-coverage: {total} programs found, minimum required is {MinimumTotalPrograms}. " +
                "Fewer programs means the gate will rely on benchmark-only signal.");
        }
        else if (total > MaximumTotalPrograms)
        {
            violations.Add(
                $"Over-coverage: {total} programs found, maximum recommended is {MaximumTotalPrograms}. " +
                "Large test sets invite teach-to-the-test and dilute the signal of any single case.");
        }

        if (total > 0)
        {
            CheckRatio("positive", set.PositivePrograms.Count, total, MinimumPositiveRatio, violations);
            CheckRatio("negative", set.NegativePrograms.Count, total, MinimumNegativeRatio, violations);
            CheckRatio("edge", set.EdgePrograms.Count, total, MinimumEdgeRatio, violations);
        }

        return new Result(violations.Count == 0, set, violations);
    }

    private static void CheckRatio(
        string categoryName, int count, int total, double minimumRatio, List<string> violations)
    {
        var actual = (double)count / total;
        if (actual + 1e-9 < minimumRatio)
        {
            var required = (int)Math.Ceiling(total * minimumRatio);
            violations.Add(
                $"Insufficient {categoryName} coverage: {count}/{total} = {actual:P0}, " +
                $"need ≥ {minimumRatio:P0} ({required} programs).");
        }
    }

    /// <summary>
    /// Outcome of an audit: overall validity plus human-readable violation messages.
    /// </summary>
    public sealed record Result(bool IsValid, MicroValidationSet Set, IReadOnlyList<string> Violations);
}
