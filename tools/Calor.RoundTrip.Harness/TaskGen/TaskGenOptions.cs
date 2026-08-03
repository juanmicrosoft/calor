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

    /// <summary>How many commits of upstream history to scan for fix-shaped commits (revert source only).</summary>
    public int RevertScanCommits { get; init; } = 2000;

    /// <summary>Upper bound on injected-mutation candidates considered per project (bounds run cost).</summary>
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
