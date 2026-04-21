using Calor.Compiler.Experiments;
using Xunit;

namespace Calor.Compiler.Tests.Experiments;

/// <summary>
/// Tests for the <see cref="AbEvaluator"/> decision protocol (§5.0c).
/// Each scenario exercises a specific combination of primary metric outcome and
/// guard outcomes, then asserts the decision memo's <c>Decision</c> and
/// <c>Recommendation</c> match the rules from §4.4 three-state lifecycle.
/// </summary>
public class AbEvaluatorTests
{
    // ========================================================================
    // Fixture helpers
    // ========================================================================

    /// <summary>
    /// Build a synthetic AbRun with N programs, each carrying the supplied metric values.
    /// The same value is applied to every program, which gives deterministic tests.
    /// </summary>
    private static AbRun RunWithMetric(string metricName, IEnumerable<double> perProgramValues, string runId = "test")
    {
        var run = new AbRun { RunId = runId };
        int i = 0;
        foreach (var v in perProgramValues)
        {
            run.Programs.Add(new ProgramMetrics
            {
                ProgramId = $"p{i:D3}",
                Metrics = new Dictionary<string, double> { [metricName] = v }
            });
            i++;
        }
        return run;
    }

    /// <summary>
    /// Combine a metric from one run with a metric from another run (same program IDs).
    /// Used when tests need multiple metrics per program.
    /// </summary>
    private static AbRun MergeMetrics(params AbRun[] runs)
    {
        var merged = new AbRun { RunId = runs[0].RunId };
        var programIds = runs[0].Programs.Select(p => p.ProgramId).ToList();

        foreach (var pid in programIds)
        {
            var mp = new ProgramMetrics { ProgramId = pid };
            foreach (var run in runs)
            {
                var src = run.Programs.FirstOrDefault(x => x.ProgramId == pid);
                if (src != null)
                {
                    foreach (var kv in src.Metrics)
                        mp.Metrics[kv.Key] = kv.Value;
                }
            }
            merged.Programs.Add(mp);
        }
        return merged;
    }

    private static IEnumerable<double> Constant(double value, int n) =>
        Enumerable.Repeat(value, n);

    // ========================================================================
    // PASS / PROMOTE
    // ========================================================================

    [Fact]
    public void PrimaryPasses_AllGuardsHold_PromoteDecision()
    {
        // 30 programs where candidate is +20% on primary and unchanged on guards.
        var baseline = RunWithMetric("accuracy", Constant(0.75, 30));
        var candidate = RunWithMetric("accuracy", Constant(0.90, 30)); // +20% relative

        var spec = new PrimaryMetricSpec("accuracy", EffectDirection.Up, ThresholdRelative: 0.10);
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, Array.Empty<GuardSpec>(), seed: 42);

        Assert.Equal("PASS", memo.Decision);
        Assert.Equal("PROMOTE", memo.Recommendation);
        Assert.True(memo.Primary.Passes);
    }

    // ========================================================================
    // FAIL / DROP — primary didn't hit threshold, guards hold
    // ========================================================================

    [Fact]
    public void PrimaryMissesThreshold_GuardsHold_DropDecision()
    {
        // +5% effect is significant but below +10% threshold.
        var baseline = RunWithMetric("accuracy", Constant(0.80, 30));
        var candidate = RunWithMetric("accuracy", Constant(0.84, 30)); // +5% relative

        var spec = new PrimaryMetricSpec("accuracy", EffectDirection.Up, ThresholdRelative: 0.10);
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, Array.Empty<GuardSpec>(), seed: 42);

        Assert.Equal("FAIL", memo.Decision);
        Assert.Equal("DROP", memo.Recommendation);
        Assert.False(memo.Primary.Passes);
    }

    // ========================================================================
    // FAIL / HOLD — primary passes but a guard regresses
    // ========================================================================

    [Fact]
    public void PrimaryPasses_GuardRegresses_HoldDecision()
    {
        // Primary +20%, but guard `comprehension` regresses 10% (beyond 3% tolerance).
        var baseAcc = RunWithMetric("accuracy", Constant(0.75, 30));
        var baseComp = RunWithMetric("comprehension", Constant(0.80, 30));
        var candAcc = RunWithMetric("accuracy", Constant(0.90, 30)); // +20%
        var candComp = RunWithMetric("comprehension", Constant(0.72, 30)); // -10%

        var baseline = MergeMetrics(baseAcc, baseComp);
        var candidate = MergeMetrics(candAcc, candComp);

        var spec = new PrimaryMetricSpec("accuracy", EffectDirection.Up, 0.10);
        var guards = new[] { new GuardSpec("comprehension", 0.03) };
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, guards, seed: 42);

        Assert.Equal("FAIL", memo.Decision);
        Assert.Equal("HOLD", memo.Recommendation);
        Assert.True(memo.Primary.Passes);
        Assert.False(memo.Guards[0].NonInferior);
    }

    // ========================================================================
    // Guards within tolerance
    // ========================================================================

    [Fact]
    public void GuardWithinTolerance_NonInferiorPasses()
    {
        // Primary passes (+20%), guard is -1% (within ±3% tolerance).
        var baseline = MergeMetrics(
            RunWithMetric("accuracy", Constant(0.75, 30)),
            RunWithMetric("comprehension", Constant(0.80, 30)));
        var candidate = MergeMetrics(
            RunWithMetric("accuracy", Constant(0.90, 30)),
            RunWithMetric("comprehension", Constant(0.792, 30))); // -1%

        var spec = new PrimaryMetricSpec("accuracy", EffectDirection.Up, 0.10);
        var guards = new[] { new GuardSpec("comprehension", 0.03) };
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, guards, seed: 42);

        Assert.Equal("PASS", memo.Decision);
        Assert.Equal("PROMOTE", memo.Recommendation);
        Assert.True(memo.Guards[0].NonInferior);
    }

    // ========================================================================
    // Direction: down (lower metric = better)
    // ========================================================================

    [Fact]
    public void Direction_Down_DetectsImprovement()
    {
        // "Cost" metric — lower is better. Candidate is -20% → meets -10% threshold (down).
        var baseline = RunWithMetric("cost", Constant(100, 30));
        var candidate = RunWithMetric("cost", Constant(80, 30)); // -20%

        var spec = new PrimaryMetricSpec("cost", EffectDirection.Down, 0.10);
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, Array.Empty<GuardSpec>(), seed: 42);

        Assert.Equal("PASS", memo.Decision);
        Assert.True(memo.Primary.Passes);
    }

    [Fact]
    public void Direction_Down_WrongDirectionFails()
    {
        // Down direction but candidate went up — must fail.
        var baseline = RunWithMetric("cost", Constant(100, 30));
        var candidate = RunWithMetric("cost", Constant(120, 30)); // +20% (wrong direction)

        var spec = new PrimaryMetricSpec("cost", EffectDirection.Down, 0.10);
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, Array.Empty<GuardSpec>(), seed: 42);

        Assert.Equal("FAIL", memo.Decision);
        Assert.False(memo.Primary.Passes);
    }

    // ========================================================================
    // Data-shape edges
    // ========================================================================

    [Fact]
    public void PrimaryMetricMissing_ReportsNoPairedData()
    {
        var baseline = RunWithMetric("other", Constant(1.0, 10));
        var candidate = RunWithMetric("other", Constant(1.0, 10));

        var spec = new PrimaryMetricSpec("accuracy", EffectDirection.Up, 0.10);
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, Array.Empty<GuardSpec>(), seed: 42);

        Assert.Equal(0, memo.NPrograms);
        Assert.False(memo.Primary.Passes);
        Assert.Contains("no paired data", memo.Primary.PassReason);
    }

    [Fact]
    public void ProgramsPresentInOnlyOneRun_Skipped()
    {
        var baseline = new AbRun();
        baseline.Programs.Add(new ProgramMetrics
        {
            ProgramId = "p1",
            Metrics = new() { ["accuracy"] = 0.80 }
        });
        baseline.Programs.Add(new ProgramMetrics
        {
            ProgramId = "p2",
            Metrics = new() { ["accuracy"] = 0.80 }
        });

        var candidate = new AbRun();
        candidate.Programs.Add(new ProgramMetrics
        {
            ProgramId = "p1",
            Metrics = new() { ["accuracy"] = 0.96 } // only p1 in candidate
        });

        var spec = new PrimaryMetricSpec("accuracy", EffectDirection.Up, 0.10);
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, Array.Empty<GuardSpec>(), seed: 42);

        Assert.Equal(1, memo.NPrograms); // only p1 is paired
    }

    [Fact]
    public void NoGuards_GuardListEmpty()
    {
        var baseline = RunWithMetric("x", Constant(1.0, 30));
        var candidate = RunWithMetric("x", Constant(1.0, 30));

        var spec = new PrimaryMetricSpec("x", EffectDirection.Up, 0.10);
        var memo = AbEvaluator.Evaluate(baseline, candidate, spec, Array.Empty<GuardSpec>(), seed: 42);

        Assert.Empty(memo.Guards);
    }
}
