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

    // ---- Stratum + verification-addressability disclosure (W4 expressible stratum) ----

    /// <summary>The defect stratum (logic vs expressible).</summary>
    public DefectStratum Stratum { get; init; } = DefectStratum.Logic;

    /// <summary>Expressible only: the Calor check the operator predicted (e.g. Calor0410). Null for logic.</summary>
    public string? ExpectedCheck { get; init; }

    /// <summary>Expressible only: whether the differential addressability probe was run at all.</summary>
    public bool AddressabilityProbed { get; init; }

    /// <summary>Expressible only: whether the differential probe could be resolved (both conversions compiled).</summary>
    public bool AddressabilityDeterminable { get; init; }

    /// <summary>Expressible only: whether the predicted check was INTRODUCED by the mutation.</summary>
    public bool VerificationAddressable { get; init; }

    /// <summary>Expressible only: human-readable addressability disclosure.</summary>
    public string AddressabilityNote { get; init; } = "";
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

    /// <summary>Expressible stratum: real-defect candidates whose Calor check did not fire (not verification-addressable).</summary>
    public int ExcludedNotAddressable => _dispositions.Count(d => d.Reason == ExclusionReason.NotVerificationAddressable);

    /// <summary>Revert-supply exclusions: fix commit touches >1 source file (cannot map to the single-file predicate).</summary>
    public int ExcludedMultipleSourceFiles => _dispositions.Count(d => d.Reason == ExclusionReason.MultipleSourceFiles);

    /// <summary>Revert-supply exclusions: the fix's source hunk could not be cleanly reverse-applied onto the pinned tree.</summary>
    public int ExcludedInseparableRevert => _dispositions.Count(d => d.Reason == ExclusionReason.InseparableRevert);

    public double EligibilityRate => Considered == 0 ? 0.0 : (double)Eligible / Considered;

    // ---- Verification-addressability base rate (W4 expressible-stratum honesty number) ----

    /// <summary>Expressible candidates whose differential addressability probe RAN and was determinable.</summary>
    public int AddressabilityProbedDeterminable =>
        _dispositions.Count(d => d.Stratum == DefectStratum.Expressible && d.AddressabilityProbed && d.AddressabilityDeterminable);

    /// <summary>Expressible candidates the probe confirmed as verification-addressable (check INTRODUCED by the mutation).</summary>
    public int AddressableConfirmed =>
        _dispositions.Count(d => d.Stratum == DefectStratum.Expressible && d.VerificationAddressable);

    /// <summary>
    /// The verification-addressability BASE RATE: of the expressible sites we could probe determinably,
    /// the fraction whose Calor check the mutation actually made fire. This bounds how often real
    /// defects of these shapes would be Calor-catchable, and MUST be reported so the epoch's claim is
    /// not overstated. Zero-probed → 0.0 (disclosed as "no determinable probes").
    /// </summary>
    public double AddressabilityBaseRate =>
        AddressabilityProbedDeterminable == 0 ? 0.0 : (double)AddressableConfirmed / AddressabilityProbedDeterminable;

    /// <summary>Per-expected-check tally: (probed-determinable, addressable) counts, keyed by Calor code.</summary>
    public Dictionary<string, (int ProbedDeterminable, int Addressable)> AddressabilityByCheck()
    {
        var map = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var d in _dispositions.Where(x => x.Stratum == DefectStratum.Expressible && x.ExpectedCheck != null))
        {
            var key = d.ExpectedCheck!;
            var (probed, addr) = map.TryGetValue(key, out var cur) ? cur : (0, 0);
            if (d.AddressabilityProbed && d.AddressabilityDeterminable) probed++;
            if (d.VerificationAddressable) addr++;
            map[key] = (probed, addr);
        }
        return map;
    }

    public Dictionary<string, int> CountByReason() =>
        _dispositions
            .GroupBy(d => d.Reason.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
}
