namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>Which defect source(s) a task-generation run draws candidates from.</summary>
[Flags]
public enum TaskSourceSelection
{
    /// <summary>Standard semantic mutation operators sited in natively-converting regions (the supplement).</summary>
    Injected = 1,

    /// <summary>Reverting a mined upstream bug-fix commit — the gold-standard REAL-defect source (D-W4.1 primary).</summary>
    Revert = 2,

    /// <summary>Both sources, merged into one candidate set (each still gated by the same eligibility predicate).</summary>
    Both = Injected | Revert,
}

/// <summary>
/// Which defect STRATUM (or strata) a task-generation run draws candidates from (W4 dry-run
/// disposition). Orthogonal to <see cref="TaskSourceSelection"/>: the logic stratum is the existing
/// conversion-penalty / PP-A2 measurement (arithmetic/off-by-one/boundary + revert), for which
/// Calor has no mechanical signal; the expressible stratum injects defect classes Calor's existing
/// checks CAN catch (effect discipline / div-by-zero / index-OOB / null-deref).
/// </summary>
[Flags]
public enum StratumSelection
{
    /// <summary>The pre-existing logic operators + revert source (governed by <see cref="TaskSourceSelection"/>).</summary>
    Logic = 1,

    /// <summary>The new verification-addressable operators (gated by the additional addressability clause).</summary>
    Expressible = 2,

    /// <summary>Both strata, merged into one candidate set; the addressability clause applies only to expressible tasks.</summary>
    Both = Logic | Expressible,
}

/// <summary>Options for a task-generation run.</summary>
public sealed class TaskGenOptions
{
    /// <summary>Output directory for task bundles and the exclusion-accounting report.</summary>
    public required string OutputDir { get; init; }

    /// <summary>
    /// Which defect source(s) to draw candidates from (default <see cref="TaskSourceSelection.Injected"/>
    /// — the pre-existing behavior). <see cref="TaskSourceSelection.Revert"/> enables the gold-standard
    /// revert-upstream-bugfix source; both feed the SAME eligibility predicate + exclusion accounting.
    /// </summary>
    public TaskSourceSelection Sources { get; init; } = TaskSourceSelection.Injected;

    /// <summary>
    /// Which defect stratum/strata to draw candidates from (default <see cref="StratumSelection.Both"/>).
    /// The logic stratum uses <see cref="Sources"/> (injected logic operators + revert); the expressible
    /// stratum adds the verification-addressable operators, each gated by the additional addressability
    /// clause. Keeping the logic operators unchanged preserves the conversion-penalty / PP-A2 measurement.
    /// </summary>
    public StratumSelection Strata { get; init; } = StratumSelection.Both;

    /// <summary>How many commits of upstream history to scan for fix-shaped commits (revert source only).</summary>
    public int RevertScanCommits { get; init; } = 2000;

    /// <summary>Upper bound on candidates EVALUATED per project (bounds run cost). <b>0 or less = UNBOUNDED</b> — gates A-1.5 pins 0 for the adjudication run; a positive cap takes a lexicographic prefix and is scoped to diagnostic probe passes.</summary>
    public int MaxCandidatesPerProject { get; init; } = 8;

    /// <summary>Stop siting new candidates for a project once this many eligible bundles are collected (0 = no early stop).</summary>
    public int TargetEligiblePerProject { get; init; } = 3;

    /// <summary>The configurable fidelity gate (D-W4.3). Bar defaults to the provisional 0.70.</summary>
    public FidelityGateConfig Fidelity { get; init; } = new();

    /// <summary>Whether clause (b) requires identical failure signatures on both arms (default true).</summary>
    public bool RequireIdenticalSignature { get; init; } = true;

    /// <summary>Root for throwaway working copies (defaults to a temp dir).</summary>
    public string? WorkRoot { get; init; }
}
