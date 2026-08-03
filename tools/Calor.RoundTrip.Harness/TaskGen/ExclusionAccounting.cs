namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>One candidate's disposition, recorded for the exclusion-accounting report.</summary>
public sealed class CandidateDisposition
{
    public required string ProjectName { get; init; }
    public required string CandidateId { get; init; }
    public required string FileRelPath { get; init; }
    public required MutationOperatorKind Operator { get; init; }
    public required MutationSource Source { get; init; }
    public required bool Eligible { get; init; }
    public required ExclusionReason Reason { get; init; }
    public required string Explanation { get; init; }
}

/// <summary>
/// Exclusion accounting (D-W4.1): every candidate considered is counted and disclosed — no silent
/// corpus shrinkage. The eligibility rate this produces is itself a key Slice-E signal (whether
/// enough native-eligible surface exists to yield a decidable dry-run).
/// </summary>
public sealed class ExclusionAccounting
{
    private readonly List<CandidateDisposition> _dispositions = new();

    public IReadOnlyList<CandidateDisposition> Dispositions => _dispositions;

    public void Record(CandidateDisposition d) => _dispositions.Add(d);

    public int Considered => _dispositions.Count;
    public int Eligible => _dispositions.Count(d => d.Eligible);

    public int ExcludedClauseA => _dispositions.Count(d =>
        d.Reason is ExclusionReason.NotNativeRegion or ExclusionReason.MutatedFileReverted);

    public int ExcludedClauseB => _dispositions.Count(d =>
        d.Reason is ExclusionReason.NoObservableDefect
            or ExclusionReason.MutationDidNotSurviveConversion
            or ExclusionReason.ArmsDiverge);

    public int ExcludedAttribution => _dispositions.Count(d => d.Reason == ExclusionReason.ConverterAttributed);
    public int ExcludedNoCoveringTest => _dispositions.Count(d => d.Reason == ExclusionReason.NoCoveringTest);
    public int ExcludedDidNotCompile => _dispositions.Count(d => d.Reason == ExclusionReason.DidNotCompile);
    public int ExcludedHeldOutFilterLeak => _dispositions.Count(d => d.Reason == ExclusionReason.HeldOutFilterLeak);
    public int ExcludedFidelityGate => _dispositions.Count(d => d.Reason == ExclusionReason.FidelityGateBelowBar);

    /// <summary>Revert-supply exclusions: fix commit touches >1 source file (cannot map to the single-file predicate).</summary>
    public int ExcludedMultipleSourceFiles => _dispositions.Count(d => d.Reason == ExclusionReason.MultipleSourceFiles);

    /// <summary>Revert-supply exclusions: the fix's source hunk could not be cleanly reverse-applied onto the pinned tree.</summary>
    public int ExcludedInseparableRevert => _dispositions.Count(d => d.Reason == ExclusionReason.InseparableRevert);

    public double EligibilityRate => Considered == 0 ? 0.0 : (double)Eligible / Considered;

    public Dictionary<string, int> CountByReason() =>
        _dispositions
            .GroupBy(d => d.Reason.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
}
