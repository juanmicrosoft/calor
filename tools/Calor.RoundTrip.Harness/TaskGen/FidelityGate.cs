namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// The per-project conversion-fidelity gate (D-W4.3). The NativeFraction bar is CONFIGURABLE and
/// NOT frozen here — it is read from config/CLI, defaults to the provisional 0.70, and every
/// emission marks it provisional-pending-tranche-2. Reverted files stay in the denominator
/// (the #776 anti-masking property, already enforced by <see cref="ConversionCoverage"/>).
/// </summary>
public sealed class FidelityGateConfig
{
    /// <summary>The NativeFraction bar a project must meet to contribute tasks. Provisional 0.70.</summary>
    public double NativeFractionBar { get; init; } = 0.70;

    /// <summary>True while the bar is provisional (pending A-1.4 tranche-2 freeze). Always true in Slice C.</summary>
    public bool BarIsProvisional { get; init; } = true;

    public string BarLabel => BarIsProvisional
        ? $"NativeFraction ≥ {NativeFractionBar:0.00} (PROVISIONAL — pending A-1.4 tranche-2 freeze)"
        : $"NativeFraction ≥ {NativeFractionBar:0.00}";
}

/// <summary>Per-project fidelity decision.</summary>
public sealed class ProjectFidelityDecision
{
    public required string ProjectName { get; init; }
    public required double NativeFraction { get; init; }
    public required bool Passed { get; init; }
    public required bool Inconclusive { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Aggregate fidelity-gate result across projects, including the PP-W2 adjudicability signal.</summary>
public sealed class FidelityGateResult
{
    public required FidelityGateConfig Config { get; init; }
    public required List<ProjectFidelityDecision> Decisions { get; init; }

    public int PassedCount => Decisions.Count(d => d.Passed);

    /// <summary>Below 2 passing projects, PP-W2 is not adjudicated (wedge §6.1).</summary>
    public bool PpW2Adjudicable => PassedCount >= 2;

    public string Signal => PpW2Adjudicable
        ? $"PP-W2 adjudicable: {PassedCount} project(s) pass the fidelity gate."
        : $"PP-W2 NOT ADJUDICATED: only {PassedCount} project(s) pass the fidelity gate (< 2 required). "
        + "Reported only — the measurement half returns to substrate engineering (wedge §6.1).";
}

/// <summary>
/// One project's measured fidelity input to the gate.
/// </summary>
public readonly record struct ProjectFidelityInput(string ProjectName, double NativeFraction, bool Inconclusive);

public static class FidelityGate
{
    /// <summary>Apply the configurable per-project gate and compute the PP-W2 adjudicability signal.</summary>
    public static FidelityGateResult Evaluate(
        IEnumerable<ProjectFidelityInput> projects, FidelityGateConfig config)
    {
        var decisions = new List<ProjectFidelityDecision>();
        foreach (var p in projects)
        {
            bool passed;
            string reason;
            if (p.Inconclusive)
            {
                passed = false;
                reason = "excluded: fidelity run was INCONCLUSIVE (unattributable build failure — coverage unreliable).";
            }
            else if (p.NativeFraction >= config.NativeFractionBar)
            {
                passed = true;
                reason = $"passes: NativeFraction {p.NativeFraction:P1} ≥ bar {config.NativeFractionBar:P1}.";
            }
            else
            {
                passed = false;
                reason = $"excluded: NativeFraction {p.NativeFraction:P1} < bar {config.NativeFractionBar:P1} — disclosed, contributes no tasks.";
            }

            decisions.Add(new ProjectFidelityDecision
            {
                ProjectName = p.ProjectName,
                NativeFraction = p.NativeFraction,
                Passed = passed,
                Inconclusive = p.Inconclusive,
                Reason = reason,
            });
        }

        return new FidelityGateResult { Config = config, Decisions = decisions };
    }
}
