using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Pins the D-S5.1 screen's verdict logic. The rule is gates §0.2's — 5 consecutive green runs
/// against the reference — and the failure modes that matter are the ones where a task could slip
/// through: too few runs, a red run, or a run that could not be executed at all.
/// </summary>
public class DeterminismScreenTests
{
    private static DeterminismScreen.TaskScreen Screen(
        IReadOnlyList<bool> reference, IReadOnlyList<bool>? mutated = null, string? inconclusive = null) =>
        new()
        {
            TaskId = "t", ProjectName = "P", HeldOutFilter = "F",
            ReferenceRuns = reference,
            MutatedRuns = mutated ?? [true, true, true, true, true],
            Inconclusive = inconclusive,
        };

    private static readonly bool[] FiveGreen = [true, true, true, true, true];

    [Fact]
    public void Five_consecutive_green_reference_runs_pass()
    {
        var s = Screen(FiveGreen);
        Assert.True(s.Passes);
        Assert.Equal("PASS", s.Verdict);
    }

    [Fact]
    public void One_red_reference_run_fails_the_screen()
    {
        var s = Screen([true, true, false, true, true]);
        Assert.False(s.Passes);
        Assert.Contains("4/5", s.Verdict);
    }

    [Fact]
    public void Fewer_than_five_runs_fails_even_if_all_green()
    {
        // The rule is FIVE consecutive green, not "all the runs we managed were green" — a truncated
        // run must never be read as a pass.
        Assert.False(Screen([true, true, true, true]).Passes);
    }

    [Fact]
    public void Inconclusive_is_screened_out_conservatively_even_with_five_green()
    {
        // A build failure or a filter matching no tests cannot be evidence of determinism.
        var s = Screen(FiveGreen, inconclusive: "pristine reference did not build");
        Assert.False(s.Passes);
        Assert.StartsWith("INCONCLUSIVE", s.Verdict);
    }

    [Fact]
    public void An_intermittent_defect_still_passes_the_gate_but_is_reported()
    {
        // Gates §0.2 constrains the REFERENCE, not the defect's manifestation rate. A task whose
        // defect is intermittent is still screened in — but silently doing so would overstate the
        // epoch's discriminating power, so the verdict says it.
        var s = Screen(FiveGreen, mutated: [true, false, true, true, true]);
        Assert.True(s.Passes);
        Assert.False(s.DefectAlwaysManifests);
        Assert.Contains("defect intermittent", s.Verdict);
    }

    [Fact]
    public void Result_counts_screened_rejected_and_intermittent()
    {
        var r = new DeterminismScreen.Result
        {
            Tasks =
            [
                Screen(FiveGreen),
                Screen([true, false, true, true, true]),
                Screen(FiveGreen, mutated: [true, true, false, true, true]),
                Screen(FiveGreen, inconclusive: "no tests matched"),
            ]
        };

        Assert.Equal(2, r.Screened);
        Assert.Equal(2, r.Rejected);
        Assert.Equal(1, r.PassingWithIntermittentDefect);
    }

    [Fact]
    public void Report_states_the_rule_and_the_screened_count()
    {
        var md = DeterminismScreenReport.Render(new DeterminismScreen.Result { Tasks = [Screen(FiveGreen)] });
        Assert.Contains("5 consecutive green runs", md);
        Assert.Contains("**1 of 1 tasks pass.**", md);
        Assert.Contains("M-S3 counts only", md);
    }
}
