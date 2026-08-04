using Calor.RoundTrip.Harness.TaskGen;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// The v0.12 S1 enumeration-only pre-pass (kickoff D-1 / D-5).
///
/// <para>This answers two questions <b>without evaluating eligibility</b>, which is what makes it
/// usable before the A-1.5 freeze (plan §3 constraint (a)): how many expressible-stratum candidates
/// the corpus can supply at all, and how much of that supply is locked behind converter fidelity.</para>
///
/// <para><b>Cost.</b> One conversion pass per project. No builds, no test runs, no recovery, no
/// bundles — so minutes, not hours, versus the funnel probe's per-candidate build/test cycles.</para>
///
/// <para><b>The honest caveat, and it matters for how the numbers may be used.</b> Because the pass
/// deliberately skips build + <c>RecoverBuildAsync</c>, a file that converted but does not
/// <i>compile</i> is still counted as native here — recovery is exactly what would revert it. So
/// <c>S_native</c> is an <b>upper bound</b> on native supply, and the ceiling it yields is an upper
/// bound on eligibility (every later clause only removes candidates). That is the correct direction
/// for a go/no-go that asks "is there conceivably enough supply?" — an upper bound cannot make an
/// unreachable venue look reachable in the other direction. It must not be quoted as realized supply.</para>
/// </summary>
public static class SupplyEnumeration
{
    /// <summary>Per-project result of the pre-pass.</summary>
    public sealed class ProjectSupply
    {
        public required string ProjectName { get; init; }

        /// <summary>Files converted with a zero-loss ledger (upper bound — no build/recovery run).</summary>
        public required int NativeFiles { get; init; }

        /// <summary>Files converted but carrying ≥1 structured loss.</summary>
        public required int WithLossFiles { get; init; }

        /// <summary>Expressible candidates sited in native files — today's reachable supply.</summary>
        public required int SupplyNative { get; init; }

        /// <summary>
        /// Expressible candidates sited in converted-with-losses files. These are the candidates that
        /// file-granularity clause (a) discards wholesale and that site-level clause (a) would reach —
        /// the D-5 decision statistic.
        /// </summary>
        public required int SupplyWithLoss { get; init; }

        /// <summary>Candidate counts by operator kind, over native files.</summary>
        public required IReadOnlyDictionary<string, int> NativeByOperator { get; init; }

        /// <summary>Candidate counts by operator kind, over with-loss files.</summary>
        public required IReadOnlyDictionary<string, int> WithLossByOperator { get; init; }

        /// <summary>Native files carrying ≥1 candidate, with their counts — exposes clustering.</summary>
        public required IReadOnlyDictionary<string, int> NativeByFile { get; init; }

        /// <summary>
        /// D-5's decision statistic. Null when <see cref="SupplyNative"/> is 0, because the ratio is
        /// undefined there and must not be silently reported as 0 or ∞.
        /// </summary>
        public double? WithLossOverNativeRatio =>
            SupplyNative == 0 ? null : (double)SupplyWithLoss / SupplyNative;
    }

    /// <summary>The pre-pass verdict over all projects.</summary>
    public sealed class Result
    {
        public required IReadOnlyList<ProjectSupply> Projects { get; init; }

        public int TotalSupplyNative => Projects.Sum(p => p.SupplyNative);
        public int TotalSupplyWithLoss => Projects.Sum(p => p.SupplyWithLoss);

        /// <summary>
        /// Pooled D-5 statistic. Pooled deliberately: D-5's threshold is a single corpus-level
        /// decision about whether to build site-level clause (a) at all, not a per-project gate.
        /// Per-project ratios are reported alongside so a pooled number cannot hide a split.
        /// </summary>
        public double? PooledRatio =>
            TotalSupplyNative == 0 ? null : (double)TotalSupplyWithLoss / TotalSupplyNative;

        /// <summary>
        /// D-5, pre-committed in the S1 kickoff: ratio ≥ 0.5 accepts site-level clause (a);
        /// below 0.5 rejects it, finally, for v0.12. Undefined when native supply is 0 — in which
        /// case the decision is not "reject", it is "undecidable by this statistic".
        /// </summary>
        public const double D5AcceptThreshold = 0.5;

        public string D5Verdict => PooledRatio switch
        {
            null => "UNDECIDABLE (native supply is 0 — the ratio has no denominator)",
            var r when r >= D5AcceptThreshold => $"ACCEPT site-level clause (a) — ratio {r:F2} ≥ {D5AcceptThreshold:F2}",
            var r => $"REJECT site-level clause (a), final for v0.12 — ratio {r:F2} < {D5AcceptThreshold:F2}",
        };
    }

    /// <summary>
    /// Run the pre-pass. <paramref name="convertProject"/> performs a conversion-only pass and returns
    /// the per-file ledger; it is injected so this stays unit-testable without a corpus.
    /// </summary>
    public static async Task<Result> RunAsync(
        IReadOnlyList<RoundTripConfig> projects,
        Func<RoundTripConfig, Task<IReadOnlyList<FileConversionResult>>> convertProject,
        Func<RoundTripConfig, string, Task<string?>> readOriginalSource)
    {
        var results = new List<ProjectSupply>();

        foreach (var project in projects)
        {
            var files = await convertProject(project);

            var native = files.Where(f => f.Status == FileStatus.Replaced && f.LossCount == 0)
                              .Select(f => f.FilePath).ToList();
            var withLoss = files.Where(f => f.Status == FileStatus.Replaced && f.LossCount > 0)
                                .Select(f => f.FilePath).ToList();

            var (nativeCount, nativeByOp, nativeByFile) = await EnumerateOver(project, native, readOriginalSource);
            var (lossCount, lossByOp, _) = await EnumerateOver(project, withLoss, readOriginalSource);

            results.Add(new ProjectSupply
            {
                ProjectName = project.ProjectName,
                NativeFiles = native.Count,
                WithLossFiles = withLoss.Count,
                SupplyNative = nativeCount,
                SupplyWithLoss = lossCount,
                NativeByOperator = nativeByOp,
                WithLossByOperator = lossByOp,
                NativeByFile = nativeByFile,
            });
        }

        return new Result { Projects = results };
    }

    private static async Task<(int Total, Dictionary<string, int> ByOperator, Dictionary<string, int> ByFile)>
        EnumerateOver(
            RoundTripConfig project,
            IReadOnlyList<string> relPaths,
            Func<RoundTripConfig, string, Task<string?>> readOriginalSource)
    {
        var byOperator = new Dictionary<string, int>(StringComparer.Ordinal);
        var byFile = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;

        foreach (var rel in relPaths)
        {
            var source = await readOriginalSource(project, rel);
            if (source == null) continue;

            var candidates = ExpressibleMutationOperators.Enumerate(source, rel);
            if (candidates.Count == 0) continue;

            total += candidates.Count;
            byFile[rel] = candidates.Count;
            foreach (var c in candidates)
            {
                var key = c.Operator.ToString();
                byOperator[key] = byOperator.GetValueOrDefault(key) + 1;
            }
        }

        return (total, byOperator, byFile);
    }
}
