namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>Options for a task-generation run.</summary>
public sealed class TaskGenOptions
{
    /// <summary>Output directory for task bundles and the exclusion-accounting report.</summary>
    public required string OutputDir { get; init; }

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
