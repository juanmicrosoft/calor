namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// The D-S5.1 determinism screen. Gates §0.2: "Held-out tests must be deterministic, verified at
/// authoring by <b>5 consecutive green runs against the reference solution</b>."
///
/// <para>For a generated task the "reference solution" is the <b>pristine</b> corpus file — the
/// unmutated original. The screen therefore runs each task's held-out filter against the pristine
/// project five times and requires 5/5 green. A held-out test that is flaky on correct code cannot
/// distinguish "the agent fixed the defect" from "the suite blinked", so a task that fails this
/// screen is not epoch-eligible however real its defect is.</para>
///
/// <para><b>M-S3 counts only screened tasks</b> (gates A-1.5.1), so this is the gate between "the
/// generator produced 8 eligible" and "the epoch has 8 tasks".</para>
///
/// <para>A second, weaker property is measured and <b>reported only</b>: that the mutated arm fails
/// the same held-out set on all five runs. Gates §0.2 does not require it — it is the task's
/// discriminating power rather than the suite's determinism — but a defect that only sometimes
/// manifests would silently weaken any epoch built on it, so it is disclosed rather than assumed.</para>
/// </summary>
public static class DeterminismScreen
{
    /// <summary>Gates §0.2's constant. Not a tunable.</summary>
    public const int RequiredConsecutiveGreenRuns = 5;

    public sealed class TaskScreen
    {
        public required string TaskId { get; init; }
        public required string ProjectName { get; init; }
        public required string HeldOutFilter { get; init; }

        /// <summary>Per-run outcome on the PRISTINE reference: true = the held-out set was green.</summary>
        public required IReadOnlyList<bool> ReferenceRuns { get; init; }

        /// <summary>Per-run outcome on the MUTATED arm: true = the held-out set failed (the defect manifested).</summary>
        public required IReadOnlyList<bool> MutatedRuns { get; init; }

        /// <summary>Any run that could not be executed (build failure, no results) — screened out conservatively.</summary>
        public string? Inconclusive { get; init; }

        /// <summary>Gates §0.2: 5 consecutive green runs against the reference. Inconclusive counts as FAIL.</summary>
        public bool Passes =>
            Inconclusive == null
            && ReferenceRuns.Count == RequiredConsecutiveGreenRuns
            && ReferenceRuns.All(g => g);

        /// <summary>Reported only: did the defect manifest on every mutated run?</summary>
        public bool DefectAlwaysManifests =>
            MutatedRuns.Count == RequiredConsecutiveGreenRuns && MutatedRuns.All(f => f);

        public string Verdict =>
            Inconclusive != null ? $"INCONCLUSIVE → screened out ({Inconclusive})"
            : Passes ? (DefectAlwaysManifests ? "PASS" : "PASS (reference stable; defect intermittent — reported)")
            : $"FAIL — reference green on {ReferenceRuns.Count(g => g)}/{ReferenceRuns.Count} runs";
    }

    public sealed class Result
    {
        public required IReadOnlyList<TaskScreen> Tasks { get; init; }

        public int Screened => Tasks.Count(t => t.Passes);
        public int Rejected => Tasks.Count - Screened;

        /// <summary>Tasks that pass the gate but whose defect did not manifest on every run.</summary>
        public int PassingWithIntermittentDefect => Tasks.Count(t => t.Passes && !t.DefectAlwaysManifests);
    }
}
