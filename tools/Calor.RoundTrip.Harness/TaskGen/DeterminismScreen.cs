namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// The D-S5.1 determinism screen. Gates §0.2: "Held-out tests must be deterministic, verified at
/// authoring by <b>5 consecutive green runs against the reference solution</b>."
///
/// <para>Plan D-S5.1 applies that rule "to each eligible candidate's held-out test <b>on both
/// arms</b>", and both arms are graded, so both are screened here. The two arms have <i>different</i>
/// reference solutions: the C# arm's is the pristine corpus file; the Calor arm's is the
/// <b>converted, unmutated</b> program — through the emitter, under effect enforcement and contract
/// guards. Screening only the C# arm would leave the arm where novel nondeterminism is most likely
/// entirely unmeasured, while still being reported as passing "the" screen.</para>
///
/// <para><b>M-S3 counts only screened tasks</b> (gates A-1.5.1), so this is the gate between "the
/// generator produced N eligible" and "the epoch has N tasks".</para>
///
/// <para>A weaker property is measured and <b>reported only</b>: that the mutated arm fails the same
/// held-out set on all five runs. §0.2 constrains the reference, not the defect's manifestation rate
/// — gating on it would be inventing a threshold. But a defect that only sometimes manifests would
/// silently weaken any epoch built on it, so it is disclosed. Crucially, an arm that could not be run
/// at all is reported as <b>not available</b>, never as "intermittent".</para>
/// </summary>
public static class DeterminismScreen
{
    /// <summary>Gates §0.2's constant. Not a tunable.</summary>
    public const int RequiredConsecutiveGreenRuns = 5;

    /// <summary>Why a mutated-arm measurement is absent — so absence is never reported as intermittency.</summary>
    public enum ArmStatus { Ran, NotPresent, BuildFailed, NotAttempted }

    /// <summary>One test execution, recorded with enough detail to audit it after the fact.</summary>
    public sealed class RunRecord
    {
        public required int TotalTests { get; init; }
        public required int Passed { get; init; }
        public required int Failed { get; init; }
        public required long DurationMs { get; init; }

        /// <summary>True when the held-out set was green — the property §0.2 requires of a reference.</summary>
        public bool Green => Failed == 0 && TotalTests > 0;

        /// <summary>True when the defect manifested (mutated arm's reported-only column).</summary>
        public bool Failing => Failed > 0;
    }

    public sealed class TaskScreen
    {
        public required string TaskId { get; init; }
        public required string ProjectName { get; init; }
        public required string HeldOutFilter { get; init; }

        /// <summary>C#-arm reference: the pristine corpus.</summary>
        public required IReadOnlyList<RunRecord> CSharpReferenceRuns { get; init; }

        /// <summary>Calor-arm reference: the CONVERTED, UNMUTATED program.</summary>
        public required IReadOnlyList<RunRecord> CalorReferenceRuns { get; init; }

        /// <summary>Mutated arm — reported only.</summary>
        public required IReadOnlyList<RunRecord> MutatedRuns { get; init; }
        public required ArmStatus MutatedArm { get; init; }

        /// <summary>Set when the screen could not be executed. Screened out conservatively.</summary>
        public string? Inconclusive { get; init; }

        private static bool FiveGreen(IReadOnlyList<RunRecord> runs) =>
            runs.Count == RequiredConsecutiveGreenRuns && runs.All(r => r.Green);

        /// <summary>
        /// Gates §0.2 on BOTH arms. Inconclusive counts as FAIL, and fewer than five runs fails even
        /// if every observed run was green — the rule is five consecutive, not "all the ones we managed".
        /// </summary>
        public bool Passes => Inconclusive == null && FiveGreen(CSharpReferenceRuns) && FiveGreen(CalorReferenceRuns);

        /// <summary>Reported only, and only meaningful when the arm actually ran.</summary>
        public bool DefectAlwaysManifests =>
            MutatedArm == ArmStatus.Ran
            && MutatedRuns.Count == RequiredConsecutiveGreenRuns
            && MutatedRuns.All(r => r.Failing);

        public string MutatedNote => MutatedArm switch
        {
            ArmStatus.NotPresent => "n/a — arm not available",
            ArmStatus.BuildFailed => "n/a — arm did not build",
            ArmStatus.NotAttempted => "n/a — not attempted",
            _ when DefectAlwaysManifests => "manifested on all runs",
            _ => "defect INTERMITTENT",
        };

        public string Verdict
        {
            get
            {
                if (Inconclusive != null) return $"INCONCLUSIVE → screened out ({Inconclusive})";
                if (!Passes)
                {
                    var cs = $"C# {CSharpReferenceRuns.Count(r => r.Green)}/{CSharpReferenceRuns.Count}";
                    var cal = $"Calor {CalorReferenceRuns.Count(r => r.Green)}/{CalorReferenceRuns.Count}";
                    return $"FAIL — reference green {cs}, {cal} (need {RequiredConsecutiveGreenRuns}/{RequiredConsecutiveGreenRuns} on both)";
                }
                return MutatedArm == ArmStatus.Ran && !DefectAlwaysManifests
                    ? "PASS (both references stable; defect intermittent — reported)"
                    : "PASS";
            }
        }
    }

    public sealed class Result
    {
        public required IReadOnlyList<TaskScreen> Tasks { get; init; }

        /// <summary>Provenance, so the artifact is auditable without the console log.</summary>
        public required IReadOnlyDictionary<string, string> CorpusPins { get; init; }
        public required string HarnessCommit { get; init; }

        public int Screened => Tasks.Count(t => t.Passes);
        public int Rejected => Tasks.Count - Screened;
        public int PassingWithIntermittentDefect =>
            Tasks.Count(t => t.Passes && t.MutatedArm == ArmStatus.Ran && !t.DefectAlwaysManifests);
        public int MutatedArmUnavailable => Tasks.Count(t => t.MutatedArm != ArmStatus.Ran);
    }
}
