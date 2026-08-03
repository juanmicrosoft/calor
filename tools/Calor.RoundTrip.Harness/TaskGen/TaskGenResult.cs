namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>Per-project result of a task-generation run.</summary>
public sealed class TaskGenProjectResult
{
    public required string ProjectName { get; init; }
    public required double NativeFraction { get; init; }
    public required bool Inconclusive { get; init; }
    public required int NativeSourceFiles { get; init; }
    public required int TotalConvertibleFiles { get; init; }

    /// <summary>All mutation candidates sited in native regions BEFORE the per-project cap / early-stop
    /// truncated the evaluated set. The honest denominator context for the eligibility rate (minor).</summary>
    public required int TotalEnumeratedCandidates { get; init; }

    public required int BaselineTestsPassed { get; init; }
    public required List<TaskBundle> Bundles { get; init; }
    public required ExclusionAccounting Accounting { get; init; }

    public ProjectFidelityInput ToFidelityInput() => new(ProjectName, NativeFraction, Inconclusive);
}

/// <summary>Aggregate result across all projects in a generation run.</summary>
public sealed class TaskGenRunResult
{
    public required List<TaskGenProjectResult> Projects { get; init; }
    public required FidelityGateResult FidelityGate { get; init; }

    public int TotalEligible => Projects.Sum(p => p.Bundles.Count);
    public int ProjectsWithEligibleTasks => Projects.Count(p => p.Bundles.Count > 0);
    public int TotalConsidered => Projects.Sum(p => p.Accounting.Considered);

    /// <summary>
    /// The Slice-C definition of done: ≥3 eligible bundles across ≥2 projects that pass the fidelity gate.
    /// </summary>
    public bool MeetsDefinitionOfDone =>
        TotalEligible >= 3
        && Projects.Count(p => p.Bundles.Count > 0 && FidelityGate.Decisions.Any(d => d.ProjectName == p.ProjectName && d.Passed)) >= 2;
}
