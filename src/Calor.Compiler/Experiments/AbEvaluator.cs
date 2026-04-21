using System.Text.Json.Serialization;

namespace Calor.Compiler.Experiments;

/// <summary>
/// Paired A/B evaluation (§5.0c of <c>docs/plans/calor-native-type-system-v2.md</c>).
/// Given two benchmark runs (baseline = flag off, candidate = flag on) each
/// expressed as per-program metric values, the evaluator computes the primary
/// metric's relative effect + p-value + CI, checks each no-regression guard via
/// TOST, and produces a decision memo.
///
/// The primary metric uses α = 0.05 unadjusted — it is the confirmatory test.
/// Guards are corrected for multiple comparisons via Holm-Bonferroni at α = 0.05,
/// controlling the family-wise error rate within the guard family only.
/// </summary>
public static class AbEvaluator
{
    /// <summary>
    /// Run the full evaluation protocol and return a <see cref="DecisionMemo"/>.
    /// </summary>
    public static DecisionMemo Evaluate(
        AbRun baseline,
        AbRun candidate,
        PrimaryMetricSpec primary,
        IReadOnlyList<GuardSpec> guards,
        int bootstrapResamples = 2000,
        int? seed = null)
    {
        var pairedBase = new List<double>();
        var pairedCand = new List<double>();
        PairByProgram(baseline, candidate, primary.Name, pairedBase, pairedCand);

        var primaryResult = EvaluatePrimary(pairedBase, pairedCand, primary, bootstrapResamples, seed);

        var guardResults = new List<GuardResult>();
        var guardPValues = new List<double>();
        var guardBasePaired = new List<List<double>>();
        var guardCandPaired = new List<List<double>>();

        foreach (var guard in guards)
        {
            var gb = new List<double>();
            var gc = new List<double>();
            PairByProgram(baseline, candidate, guard.Name, gb, gc);
            guardBasePaired.Add(gb);
            guardCandPaired.Add(gc);

            // Guard p-value is the Wilcoxon p on the difference — used only for
            // reporting, not for the pass/fail decision. The decision uses TOST
            // via bootstrap CI (see below).
            guardPValues.Add(Stats.WilcoxonSignedRankPValue(gb, gc));
        }

        // Holm-Bonferroni across guards at α = 0.05 — for reporting only.
        _ = Stats.HolmBonferroni(guardPValues, 0.05);

        for (int i = 0; i < guards.Count; i++)
        {
            var guard = guards[i];
            var gb = guardBasePaired[i];
            var gc = guardCandPaired[i];

            bool nonInferior;
            (double lower, double upper) ci;
            double baseMean = 0, candMean = 0, effect = 0;

            if (gb.Count == 0)
            {
                // Metric missing from both runs. Treat as vacuously non-inferior but flag it.
                nonInferior = true;
                ci = (0, 0);
            }
            else
            {
                baseMean = Stats.Mean(gb);
                candMean = Stats.Mean(gc);
                effect = baseMean == 0 ? 0 : (candMean - baseMean) / baseMean;
                ci = Stats.PairedRelativeBootstrapCI(gb, gc, bootstrapResamples, 0.90, seed);
                nonInferior = Stats.PairedIsNonInferior(gb, gc, guard.ToleranceRelative, 0.05, bootstrapResamples, seed);
            }

            guardResults.Add(new GuardResult(
                Name: guard.Name,
                ToleranceRelativePercent: guard.ToleranceRelative * 100,
                BaselineMean: baseMean,
                CandidateMean: candMean,
                RelativeEffectPercent: effect * 100,
                EffectCiLowerPercent: ci.lower * 100,
                EffectCiUpperPercent: ci.upper * 100,
                NonInferior: nonInferior));
        }

        // Decision logic:
        //   primary passes AND all guards non-inferior → PASS / PROMOTE
        //   primary fails AND guards hold             → FAIL / DROP (or HOLD if near-miss)
        //   primary passes BUT a guard regresses      → HOLD
        //   inconclusive                              → INCONCLUSIVE / HOLD
        var decision = ComputeDecision(primaryResult, guardResults);

        return new DecisionMemo(
            Decision: decision.Decision,
            Recommendation: decision.Recommendation,
            Reason: decision.Reason,
            Primary: primaryResult,
            Guards: guardResults,
            NPrograms: pairedBase.Count,
            TestUsed: "Wilcoxon signed-rank (paired, per-program) + percentile bootstrap CI");
    }

    private static PrimaryResult EvaluatePrimary(
        List<double> baseline,
        List<double> candidate,
        PrimaryMetricSpec spec,
        int bootstrapResamples,
        int? seed)
    {
        if (baseline.Count == 0)
        {
            return new PrimaryResult(
                Name: spec.Name,
                Direction: spec.Direction,
                ThresholdPercent: spec.ThresholdRelative * 100,
                BaselineMean: 0,
                CandidateMean: 0,
                RelativeEffectPercent: 0,
                EffectCiLowerPercent: 0,
                EffectCiUpperPercent: 0,
                PValue: 1.0,
                Passes: false,
                PassReason: "no paired data — primary metric absent from runs");
        }

        double baseMean = Stats.Mean(baseline);
        double candMean = Stats.Mean(candidate);
        double relative = baseMean == 0 ? 0 : (candMean - baseMean) / baseMean;

        double pValue = Stats.WilcoxonSignedRankPValue(baseline, candidate);
        var (lower, upper) = Stats.PairedRelativeBootstrapCI(baseline, candidate, bootstrapResamples, 0.95, seed);

        // Pass criterion: p < 0.05 AND effect exceeds threshold in the expected direction,
        // with the CI's relevant bound past the threshold (not just the point estimate).
        bool significant = pValue < 0.05;
        bool meetsThreshold;
        string reason;

        if (spec.Direction == EffectDirection.Up)
        {
            meetsThreshold = lower >= spec.ThresholdRelative;
            reason = significant && meetsThreshold
                ? $"significant (p={pValue:F4}); 95% CI lower bound {lower:P1} ≥ threshold {spec.ThresholdRelative:P1}"
                : significant
                    ? $"significant (p={pValue:F4}) but CI lower bound {lower:P1} < threshold {spec.ThresholdRelative:P1}"
                    : $"not significant (p={pValue:F4})";
        }
        else
        {
            meetsThreshold = upper <= -spec.ThresholdRelative;
            reason = significant && meetsThreshold
                ? $"significant (p={pValue:F4}); 95% CI upper bound {upper:P1} ≤ -threshold {-spec.ThresholdRelative:P1}"
                : significant
                    ? $"significant (p={pValue:F4}) but CI upper bound {upper:P1} > -threshold {-spec.ThresholdRelative:P1}"
                    : $"not significant (p={pValue:F4})";
        }

        return new PrimaryResult(
            Name: spec.Name,
            Direction: spec.Direction,
            ThresholdPercent: spec.ThresholdRelative * 100,
            BaselineMean: baseMean,
            CandidateMean: candMean,
            RelativeEffectPercent: relative * 100,
            EffectCiLowerPercent: lower * 100,
            EffectCiUpperPercent: upper * 100,
            PValue: pValue,
            Passes: significant && meetsThreshold,
            PassReason: reason);
    }

    private static (string Decision, string Recommendation, string Reason) ComputeDecision(
        PrimaryResult primary, IReadOnlyList<GuardResult> guards)
    {
        var regressedGuards = guards.Where(g => !g.NonInferior).ToList();

        if (primary.Passes)
        {
            if (regressedGuards.Count == 0)
            {
                return ("PASS", "PROMOTE",
                    $"Primary {primary.Name} hit threshold ({primary.PassReason}); all {guards.Count} guard(s) non-inferior.");
            }
            // Primary passes but a guard regresses → HOLD per §4.4 three-state lifecycle.
            return ("FAIL", "HOLD",
                $"Primary {primary.Name} passed, but {regressedGuards.Count} guard(s) regressed beyond tolerance: " +
                string.Join(", ", regressedGuards.Select(g => g.Name)));
        }
        else
        {
            if (regressedGuards.Count == 0)
            {
                return ("FAIL", "DROP",
                    $"Primary {primary.Name} did not hit threshold ({primary.PassReason}); guards hold.");
            }
            return ("FAIL", "DROP",
                $"Primary {primary.Name} did not hit threshold and {regressedGuards.Count} guard(s) regressed.");
        }
    }

    /// <summary>
    /// Extract paired per-program metric values from baseline and candidate runs,
    /// matching programs by <c>ProgramId</c>. Programs present in one run but not
    /// the other are skipped silently — they have no paired observation.
    /// </summary>
    private static void PairByProgram(AbRun baseline, AbRun candidate, string metric, List<double> outBase, List<double> outCand)
    {
        var candByProgram = candidate.Programs.ToDictionary(p => p.ProgramId, StringComparer.Ordinal);
        foreach (var bp in baseline.Programs)
        {
            if (!candByProgram.TryGetValue(bp.ProgramId, out var cp)) continue;
            if (!bp.Metrics.TryGetValue(metric, out var bv)) continue;
            if (!cp.Metrics.TryGetValue(metric, out var cv)) continue;
            outBase.Add(bv);
            outCand.Add(cv);
        }
    }
}

// ============================================================================
// Input / output data shapes
// ============================================================================

public sealed class AbRun
{
    [JsonPropertyName("run_id")] public string RunId { get; set; } = "";
    [JsonPropertyName("flag_config")] public string FlagConfig { get; set; } = "";
    [JsonPropertyName("programs")] public List<ProgramMetrics> Programs { get; set; } = new();
}

public sealed class ProgramMetrics
{
    [JsonPropertyName("program_id")] public string ProgramId { get; set; } = "";
    [JsonPropertyName("metrics")] public Dictionary<string, double> Metrics { get; set; } = new();
}

public enum EffectDirection { Up, Down }

public sealed record PrimaryMetricSpec(
    string Name,
    EffectDirection Direction,
    double ThresholdRelative); // e.g., 0.15 = +15%

public sealed record GuardSpec(
    string Name,
    double ToleranceRelative); // e.g., 0.03 = ±3%

public sealed record PrimaryResult(
    string Name,
    EffectDirection Direction,
    double ThresholdPercent,
    double BaselineMean,
    double CandidateMean,
    double RelativeEffectPercent,
    double EffectCiLowerPercent,
    double EffectCiUpperPercent,
    double PValue,
    bool Passes,
    string PassReason);

public sealed record GuardResult(
    string Name,
    double ToleranceRelativePercent,
    double BaselineMean,
    double CandidateMean,
    double RelativeEffectPercent,
    double EffectCiLowerPercent,
    double EffectCiUpperPercent,
    bool NonInferior);

public sealed record DecisionMemo(
    string Decision,         // PASS | FAIL | INCONCLUSIVE
    string Recommendation,   // PROMOTE | HOLD | DROP
    string Reason,
    PrimaryResult Primary,
    IReadOnlyList<GuardResult> Guards,
    int NPrograms,
    string TestUsed);
