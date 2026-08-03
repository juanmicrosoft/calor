using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>Pins held-out extraction, visible-suite filtering, and failing-behavior synthesis (C2).</summary>
public class TaskGenHeldOutTests
{
    private static TestResult T(string name, string outcome, string? error = null) => new()
    {
        TestName = name,
        ClassName = "S.CalculatorTests",
        Assembly = "S.Tests.dll",
        ExecutorUri = "executor://xunit",
        Outcome = outcome,
        ErrorMessage = error,
    };

    private static TestRunResult Run(params TestResult[] results) => new()
    {
        ExitCode = results.Any(r => r.Outcome == "Failed") ? 1 : 0,
        TotalTests = results.Length,
        Passed = results.Count(r => r.Outcome == "Passed"),
        Failed = results.Count(r => r.Outcome == "Failed"),
        Results = results.ToList(),
    };

    [Fact]
    public void IdentifyCoveringTests_IsPassedInBaseline_FailedAfterMutation()
    {
        var baseline = Run(T("Max_ReturnsLarger", "Passed"), T("Add_ReturnsSum", "Passed"));
        var mutated = Run(T("Max_ReturnsLarger", "Failed", "Assert.Equal() Failure"), T("Add_ReturnsSum", "Passed"));

        var covering = HeldOutExtraction.IdentifyCoveringTests(baseline, mutated);

        var one = Assert.Single(covering);
        Assert.Equal("Max_ReturnsLarger", one.TestName);
    }

    [Fact]
    public void IdentifyCoveringTests_IgnoresPreExistingFailures()
    {
        var baseline = Run(T("A", "Failed"), T("B", "Passed"));
        var mutated = Run(T("A", "Failed"), T("B", "Failed"));

        var covering = HeldOutExtraction.IdentifyCoveringTests(baseline, mutated);

        Assert.Equal("B", Assert.Single(covering).TestName); // A was already failing; only B is a covering test
    }

    [Fact]
    public void BuildVisibleFilter_ExcludesHeldOut()
    {
        var heldOut = HeldOutExtraction.ToHeldOut([T("Max_ReturnsLarger", "Failed")]);
        var filter = HeldOutExtraction.BuildVisibleFilter(heldOut);
        Assert.Equal("FullyQualifiedName!=S.CalculatorTests.Max_ReturnsLarger", filter);
    }

    [Fact]
    public void BuildHeldOutFilter_SelectsHeldOut()
    {
        var heldOut = HeldOutExtraction.ToHeldOut([T("Max_ReturnsLarger", "Failed")]);
        var filter = HeldOutExtraction.BuildHeldOutFilter(heldOut);
        Assert.Equal("FullyQualifiedName~Max_ReturnsLarger", filter);
    }

    [Fact]
    public void SynthesizeFailingBehavior_ScrubsTestIdentity_KeepsSymptom()
    {
        var covering = T("Max_ReturnsLarger", "Failed",
            "Max_ReturnsLarger : Assert.Equal() Failure\nExpected: 5\nActual: 3");
        var report = HeldOutExtraction.SynthesizeFailingBehavior(covering);

        Assert.DoesNotContain("Max_ReturnsLarger", report.Symptom); // the test name is scrubbed
        Assert.Contains("Assert.Equal()", report.Symptom);          // the symptom survives
        Assert.Equal("Calculator", report.SubjectHint);             // "CalculatorTests" → "Calculator"
    }

    [Fact]
    public void SynthesizeFailingBehavior_NoError_FallsBackToGenericSymptom()
    {
        var report = HeldOutExtraction.SynthesizeFailingBehavior(T("X", "Failed", error: null));
        Assert.False(string.IsNullOrWhiteSpace(report.Symptom));
        Assert.Null(report.Observed);
    }
}
