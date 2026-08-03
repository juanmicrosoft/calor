namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// Where a task's defect comes from (D-W4.1). The revert-bugfix source is the gold
/// standard (a real defect mined from upstream history); the injected mutation is the
/// supplement (a standard mutation operator applied to a natively-converting region).
/// </summary>
public enum MutationSource
{
    /// <summary>Reverting a mined upstream bug-fix commit reintroduces a real defect.</summary>
    RevertBugfix,

    /// <summary>A standard semantic mutation operator applied to C# source.</summary>
    InjectedMutation,
}

/// <summary>The standard semantic mutation operators (D-W4.1 supplement list).</summary>
public enum MutationOperatorKind
{
    /// <summary>Boundary flip: <c>&lt;</c>↔<c>&lt;=</c>, <c>&gt;</c>↔<c>&gt;=</c>.</summary>
    Boundary,

    /// <summary>Arithmetic swap: <c>+</c>↔<c>-</c>, <c>*</c>↔<c>/</c>.</summary>
    Arithmetic,

    /// <summary>Negate a branch condition.</summary>
    NegateCondition,

    /// <summary>Off-by-one: an integer literal ±1.</summary>
    OffByOne,

    /// <summary>Swap a boolean return value (<c>true</c>↔<c>false</c>).</summary>
    SwapReturn,

    /// <summary>Reverting an upstream fix hunk (revert-bugfix source; not an injected operator).</summary>
    Revert,
}

/// <summary>
/// Why a candidate was excluded from the eligible set (D-W4.1 exclusion accounting).
/// Every excluded candidate carries exactly one of these — no silent corpus shrinkage.
/// Clause-(a) reasons: <see cref="NotNativeRegion"/>, <see cref="MutatedFileReverted"/>.
/// Clause-(b) reasons: <see cref="NoObservableDefect"/>,
/// <see cref="MutationDidNotSurviveConversion"/>, <see cref="ArmsDiverge"/>.
/// D-W4.3 attribution: <see cref="ConverterAttributed"/>.
/// </summary>
public enum ExclusionReason
{
    /// <summary>The candidate is eligible (not excluded).</summary>
    None,

    /// <summary>Clause (a): the mutated file did not convert natively (LossCount &gt; 0 or not Replaced).</summary>
    NotNativeRegion,

    /// <summary>Clause (a): build recovery reverted the mutated file — the defect would sit in raw C# inside the Calor arm.</summary>
    MutatedFileReverted,

    /// <summary>Clause (b): the removed test still passes on the C# arm — the mutation produced no observable defect / no covering test.</summary>
    NoObservableDefect,

    /// <summary>Clause (b): the removed test fails on the C# arm but passes on the converted Calor arm — the mutation did not survive conversion.</summary>
    MutationDidNotSurviveConversion,

    /// <summary>Clause (b): the removed test fails on both arms but with non-identical failure signatures.</summary>
    ArmsDiverge,

    /// <summary>D-W4.3: the held-out failure is attributable to converter divergence in a different file, not to the injected mutation.</summary>
    ConverterAttributed,

    /// <summary>No covering test could be identified for the mutation (it compiled but broke nothing identifiable).</summary>
    NoCoveringTest,

    /// <summary>The mutation did not compile on the C# arm — an invalid candidate, distinct from "no covering test".</summary>
    DidNotCompile,

    /// <summary>
    /// The computed visible-suite filter fails to exclude a covering test (e.g. a custom
    /// <c>[Theory/Fact(DisplayName=...)]</c> whose method FQN is unrecoverable from TRX), so that
    /// test would stay VISIBLE and FAILING — an oracle leak (the agent would see the answer).
    /// Dropped conservatively rather than emitting a leaking bundle (review residual-[C]).
    /// </summary>
    HeldOutFilterLeak,

    /// <summary>The candidate's project was excluded by the fidelity gate (NativeFraction below bar).</summary>
    FidelityGateBelowBar,

    /// <summary>
    /// Revert source only: the mined fix commit touches more than one library-source file, so the
    /// single-file eligibility predicate cannot faithfully reintroduce the (multi-file) defect. A
    /// revert-supply exclusion, disclosed before any build/test cost is spent.
    /// </summary>
    MultipleSourceFiles,

    /// <summary>
    /// Revert source only: the fix commit's source-file hunk cannot be cleanly reverse-applied onto
    /// the pinned tree (a later commit changed the same region → 3-way conflict / no-op), so the
    /// defect cannot be reintroduced in isolation while keeping the covering test. A revert-supply
    /// exclusion, disclosed before any build/test cost is spent.
    /// </summary>
    InseparableRevert,
}

/// <summary>One held-out test removed from the visible suite and moved to held-out (C2).</summary>
public sealed class HeldOutTest
{
    /// <summary>The TRX display name (the full row name for a theory, e.g. <c>N.C.T(x: 5)</c>). For reporting.</summary>
    public required string TestName { get; init; }
    public required string ClassName { get; init; }
    public string Assembly { get; init; } = "";

    /// <summary>
    /// The metacharacter-free fully-qualified METHOD name used for VSTest <c>--filter</c> (theory
    /// args stripped) — see <see cref="HeldOutExtraction.MethodFqn"/>. This is what the visible/
    /// held-out filters key on, so a theory's rows are held out as one unit and the expression never
    /// hard-parse-errors on <c>( ) : space</c>.
    /// </summary>
    public string FilterName { get; init; } = "";

    public string Identity { get; init; } = "";
}

/// <summary>
/// The failing-behavior report BOTH arms receive (C2): the observable symptom, NOT the
/// test. Synthesized mechanically from the covering test's failure and scrubbed of the
/// test's identity so neither arm is handed the oracle.
/// </summary>
public sealed class FailingBehaviorReport
{
    public required string Symptom { get; init; }
    public string? SubjectHint { get; init; }
    public string? Observed { get; init; }
    public string Notes { get; init; } = "";
}

/// <summary>
/// The native-eligibility proof for clause (a): the mutated file's post-conversion
/// <see cref="FileConversionResult"/> state, plus the both-arm held-out outcomes that
/// prove clause (b).
/// </summary>
public sealed class EligibilityProof
{
    public required string MutatedFileRelPath { get; init; }
    public required bool MutatedFileConvertedNative { get; init; }
    public required int MutatedFileLossCount { get; init; }
    public required string MutatedFileStatus { get; init; }
    public required string CSharpArmHeldOutOutcome { get; init; }
    public required string CalorArmHeldOutOutcome { get; init; }
    public string? CSharpArmFailureSignature { get; init; }
    public string? CalorArmFailureSignature { get; init; }
    public string AttributionOutcome { get; init; } = "not-checked";
    public double ProjectNativeFraction { get; init; }
}

/// <summary>Provenance record for a task bundle (mutation source, region, native-eligibility proof).</summary>
public sealed class TaskProvenance
{
    public required MutationSource Source { get; init; }
    public required MutationOperatorKind Operator { get; init; }
    public required string OperatorDescription { get; init; }
    public required string MutatedFileRelPath { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required string OriginalSnippet { get; init; }
    public required string MutatedSnippet { get; init; }

    /// <summary>For <see cref="MutationSource.RevertBugfix"/>: the upstream fix commit that was reverted.</summary>
    public string? RevertedCommit { get; init; }
    public string? RevertedCommitSubject { get; init; }

    public required EligibilityProof NativeEligibility { get; init; }

    /// <summary>
    /// Presentation asymmetry, recorded (D-W4.1): the Calor arm works on machine-converted
    /// §-syntax while the C# arm keeps the idiomatic original — a bias AGAINST Calor, so a
    /// PP-W2 win is conservative and a loss is confounded with conversion idiom.
    /// </summary>
    public string PresentationAsymmetry { get; init; } =
        "Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.";
}

/// <summary>
/// A generated task bundle — the substrate the D-W4.4 dry-run (Slice E) consumes. It does
/// NOT run agents. Both arms carry the same mutation; the held-out test(s) are removed from
/// the visible suite; the full suite is the regression net; the failing-behavior report is
/// what an agent working the task would receive.
/// </summary>
public sealed class TaskBundle
{
    public required string TaskId { get; init; }
    public required string ProjectName { get; init; }
    public required TaskProvenance Provenance { get; init; }

    /// <summary>Working copy of the mutated idiomatic C# (the C# arm).</summary>
    public required string CSharpArmDir { get; init; }

    /// <summary>Working copy of the mutate-then-convert output (the converted Calor arm).</summary>
    public required string CalorArmDir { get; init; }

    /// <summary>The removed test(s) — held out from the visible suite.</summary>
    public required List<HeldOutTest> HeldOut { get; init; }

    /// <summary>
    /// dotnet <c>--filter</c> expression selecting the VISIBLE suite (full suite minus held-out).
    /// The held-out tests remain physically present as the regression net but are excluded here.
    /// </summary>
    public required string VisibleTestFilter { get; init; }

    /// <summary>The full test project — the regression net (held-out = removed tests + full suite).</summary>
    public required string RegressionNetProject { get; init; }

    public required FailingBehaviorReport FailingBehavior { get; init; }
    public required EligibilityProof EligibilityProof { get; init; }
}
