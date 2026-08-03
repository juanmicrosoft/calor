using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>Pins the D-W4.3 fidelity gate (configurable bar, not frozen) and exclusion accounting.</summary>
public class TaskGenGateTests
{
    [Fact]
    public void FidelityGate_UsesConfigurableBar_DefaultProvisional070()
    {
        var config = new FidelityGateConfig();
        Assert.Equal(0.70, config.NativeFractionBar);
        Assert.True(config.BarIsProvisional);
        Assert.Contains("PROVISIONAL", config.BarLabel);
    }

    [Fact]
    public void FidelityGate_PassesAtOrAboveBar_ExcludesBelow_Disclosed()
    {
        var result = FidelityGate.Evaluate(
        [
            new ProjectFidelityInput("A", 0.72, false),
            new ProjectFidelityInput("B", 0.55, false),
        ], new FidelityGateConfig { NativeFractionBar = 0.70 });

        Assert.True(result.Decisions.First(d => d.ProjectName == "A").Passed);
        Assert.False(result.Decisions.First(d => d.ProjectName == "B").Passed);
        Assert.Contains("disclosed", result.Decisions.First(d => d.ProjectName == "B").Reason);
    }

    [Fact]
    public void FidelityGate_ConfigurableBar_ChangesOutcome()
    {
        var input = new[] { new ProjectFidelityInput("A", 0.50, false) };
        Assert.False(FidelityGate.Evaluate(input, new FidelityGateConfig { NativeFractionBar = 0.70 }).Decisions[0].Passed);
        Assert.True(FidelityGate.Evaluate(input, new FidelityGateConfig { NativeFractionBar = 0.40 }).Decisions[0].Passed);
    }

    [Fact]
    public void FidelityGate_Inconclusive_IsExcluded()
    {
        var result = FidelityGate.Evaluate(
            [new ProjectFidelityInput("A", 0.99, true)], new FidelityGateConfig());
        Assert.False(result.Decisions[0].Passed);
        Assert.Contains("INCONCLUSIVE", result.Decisions[0].Reason);
    }

    [Fact]
    public void FidelityGate_FewerThanTwoPassing_SignalsPpW2NotAdjudicated()
    {
        var result = FidelityGate.Evaluate(
        [
            new ProjectFidelityInput("A", 0.80, false),
            new ProjectFidelityInput("B", 0.30, false),
        ], new FidelityGateConfig { NativeFractionBar = 0.70 });

        Assert.Equal(1, result.PassedCount);
        Assert.False(result.PpW2Adjudicable);
        Assert.Contains("NOT ADJUDICATED", result.Signal);
    }

    [Fact]
    public void FidelityGate_TwoPassing_Adjudicable()
    {
        var result = FidelityGate.Evaluate(
        [
            new ProjectFidelityInput("A", 0.80, false),
            new ProjectFidelityInput("B", 0.75, false),
        ], new FidelityGateConfig { NativeFractionBar = 0.70 });

        Assert.True(result.PpW2Adjudicable);
        Assert.Contains("adjudicable", result.Signal);
    }

    [Fact]
    public void ExclusionAccounting_CountsByClause_AndEligibilityRate()
    {
        var acc = new ExclusionAccounting();
        acc.Record(Disp(true, ExclusionReason.None));
        acc.Record(Disp(false, ExclusionReason.NotNativeRegion));      // clause a
        acc.Record(Disp(false, ExclusionReason.MutatedFileReverted));  // clause a
        acc.Record(Disp(false, ExclusionReason.MutationDidNotSurviveConversion)); // clause b
        acc.Record(Disp(false, ExclusionReason.NoObservableDefect));   // clause b
        acc.Record(Disp(false, ExclusionReason.ConverterAttributed));  // attribution
        acc.Record(Disp(false, ExclusionReason.NoCoveringTest));       // no covering test

        Assert.Equal(7, acc.Considered);
        Assert.Equal(1, acc.Eligible);
        Assert.Equal(2, acc.ExcludedClauseA);
        Assert.Equal(2, acc.ExcludedClauseB);
        Assert.Equal(1, acc.ExcludedAttribution);
        Assert.Equal(1, acc.ExcludedNoCoveringTest);
        Assert.Equal(1.0 / 7.0, acc.EligibilityRate, 3);
    }

    private static CandidateDisposition Disp(bool eligible, ExclusionReason reason) => new()
    {
        ProjectName = "P",
        CandidateId = "c",
        FileRelPath = "Lib/A.cs",
        Operator = MutationOperatorKind.Boundary,
        Source = MutationSource.InjectedMutation,
        Eligible = eligible,
        Reason = reason,
        Explanation = "x",
    };
}
