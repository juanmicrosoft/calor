namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// One sited mutation: a single-point semantic change to a C# file, with the full mutated
/// source ready to write into a working copy. Construction order is mutate-then-convert
/// (D-W4.1 review C2): the mutation is applied in C# and the converter carries it into the
/// Calor arm mechanically.
/// </summary>
public sealed class MutationCandidate
{
    public required string FileRelPath { get; init; }
    public required MutationSource Source { get; init; }
    public required MutationOperatorKind Operator { get; init; }
    public required string OperatorDescription { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required string OriginalSnippet { get; init; }
    public required string MutatedSnippet { get; init; }

    /// <summary>Full file text with exactly this one mutation applied.</summary>
    public required string MutatedSource { get; init; }

    /// <summary>For revert-bugfix candidates: the upstream fix commit reverted to reintroduce the defect.</summary>
    public string? RevertedCommit { get; init; }
    public string? RevertedCommitSubject { get; init; }

    /// <summary>Which defect stratum this candidate belongs to (logic vs expressible).</summary>
    public DefectStratum Stratum { get; init; } = DefectStratum.Logic;

    /// <summary>
    /// Expressible stratum only: the Calor diagnostic code the operator PREDICTS the mutation will
    /// make fire on the mutated-converted code (e.g. <c>Calor0410</c>, <c>Calor0920</c>). The task
    /// generator mechanically verifies this prediction via the differential addressability probe; a
    /// candidate whose predicted check does not actually fire is excluded as
    /// <see cref="ExclusionReason.NotVerificationAddressable"/>. Null for logic-stratum candidates.
    /// </summary>
    public string? ExpectedCheck { get; init; }

    /// <summary>Stable identifier for the candidate within its file.</summary>
    public string Id =>
        Source == MutationSource.RevertBugfix
            ? $"revert-{RevertedCommit?[..Math.Min(8, RevertedCommit.Length)]}"
            : $"{Operator}-L{Line}C{Column}";
}
