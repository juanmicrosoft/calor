using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>Pins held-out extraction, theory-safe visible/held-out filtering, and failing-behavior synthesis (C2, review [C]).</summary>
public class TaskGenHeldOutTests
{
    // Bare-method-name fixture (synthetic-style TestName).
    private static TestResult T(string name, string outcome, string? error = null) => new()
    {
        TestName = name,
        ClassName = "S.CalculatorTests",
        Assembly = "S.Tests.dll",
        ExecutorUri = "executor://xunit",
        Outcome = outcome,
        ErrorMessage = error,
    };

    // Already-fully-qualified fixture (real TRX shape): TestName includes the class.
    private static TestResult Trx(string fullName, string outcome, string? error = null) => new()
    {
        TestName = fullName,
        ClassName = "GeoLib.Tests.GridTests",
        Assembly = "geolib.tests.dll",
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
    public void BuildVisibleFilter_ExcludesHeldOut_ByMethodFqn_NotContains()
    {
        var heldOut = HeldOutExtraction.ToHeldOut([T("Max_ReturnsLarger", "Failed")]);
        var filter = HeldOutExtraction.BuildVisibleFilter(heldOut);
        Assert.Equal("FullyQualifiedName!~S.CalculatorTests.Max_ReturnsLarger", filter);
    }

    [Fact]
    public void BuildHeldOutFilter_SelectsHeldOut_ByMethodFqn_Contains()
    {
        var heldOut = HeldOutExtraction.ToHeldOut([T("Max_ReturnsLarger", "Failed")]);
        var filter = HeldOutExtraction.BuildHeldOutFilter(heldOut);
        Assert.Equal("FullyQualifiedName~S.CalculatorTests.Max_ReturnsLarger", filter);
    }

    // ---- review [C]: theory rows must not leak metacharacters into the filter ----

    [Fact]
    public void Theory_HeldOut_GroupsRowsToOneMethod_AndFilterHasNoMetacharacters()
    {
        const string t = "GeoLib.Tests.GridTests.SumOfSquares_Theory";
        var baseline = Run(
            Trx($"{t}(a: 3, b: 4, expected: 25)", "Passed"),
            Trx($"{t}(a: 2, b: 2, expected: 8)", "Passed"),
            Trx("GeoLib.Tests.GridTests.Area_Multiplies", "Passed"));
        var mutated = Run(
            Trx($"{t}(a: 3, b: 4, expected: 25)", "Failed", "Assert.Equal() Failure: 25 != 1"),
            Trx($"{t}(a: 2, b: 2, expected: 8)", "Failed", "Assert.Equal() Failure: 8 != 1"),
            Trx("GeoLib.Tests.GridTests.Area_Multiplies", "Passed"));

        var covering = HeldOutExtraction.IdentifyCoveringTests(baseline, mutated);
        Assert.Equal(2, covering.Count); // two theory rows failed

        var heldOut = HeldOutExtraction.ToHeldOut(covering);
        var single = Assert.Single(heldOut); // grouped to ONE method
        Assert.Equal(t, single.FilterName);

        var visible = HeldOutExtraction.BuildVisibleFilter(heldOut);
        var held = HeldOutExtraction.BuildHeldOutFilter(heldOut);
        Assert.Equal($"FullyQualifiedName!~{t}", visible);
        Assert.Equal($"FullyQualifiedName~{t}", held);
        // The catastrophic case: no theory-arg metacharacters survive into the filter expression.
        foreach (var f in new[] { visible, held })
            Assert.DoesNotContain('(', f);
    }

    [Fact]
    public void MethodFqn_TrxShape_AlreadyQualified_NoDoublePrefix()
    {
        // Real TRX TestName is already Namespace.Class.Method — must NOT be re-prefixed by ClassName.
        var fqn = HeldOutExtraction.MethodFqn("GeoLib.Tests.GridTests.Area_Multiplies", "GeoLib.Tests.GridTests");
        Assert.Equal("GeoLib.Tests.GridTests.Area_Multiplies", fqn);

        var heldOut = HeldOutExtraction.ToHeldOut([Trx("GeoLib.Tests.GridTests.Area_Multiplies", "Failed")]);
        Assert.Equal("FullyQualifiedName!~GeoLib.Tests.GridTests.Area_Multiplies",
            HeldOutExtraction.BuildVisibleFilter(heldOut));
    }

    [Fact]
    public void MethodFqn_StripsTheoryArgs()
        => Assert.Equal("N.C.T", HeldOutExtraction.MethodFqn("N.C.T(x: 5, y: 7)", "N.C"));

    [Fact]
    public void EscapeFilterValue_EscapesMetacharacters()
        => Assert.Equal(@"a\(b\)\&c", HeldOutExtraction.EscapeFilterValue("a(b)&c"));

    // ---- residual-[C]: visible-filter round-trip guard (custom DisplayName oracle-leak) ----

    [Fact]
    public void VisibleSuiteLeaks_True_WhenCustomDisplayCoveringTestStillFailingInVisibleSuite()
    {
        // A custom [Theory(DisplayName=...)] whose method FQN is unrecoverable — its !~garbage term
        // fails to exclude it, so it survives the visible suite present-and-failing (the leak).
        var custom = Trx("Nasty, name (with) \"quotes\" & pipes|here", "Failed", "Assert.Equal() Failure");
        var visibleRun = Run(custom, Trx("GeoLib.Tests.GridTests.Area_Multiplies", "Passed"));
        Assert.True(HeldOutExtraction.VisibleSuiteLeaks(visibleRun, new[] { custom.Identity }));
    }

    [Fact]
    public void VisibleSuiteLeaks_False_WhenCoveringTestSuccessfullyExcluded()
    {
        var custom = Trx("Nasty, name (with) \"quotes\" & pipes|here", "Failed");
        // The visible run does NOT contain the covering test at all (the filter excluded it).
        var visibleRun = Run(Trx("GeoLib.Tests.GridTests.Area_Multiplies", "Passed"));
        Assert.False(HeldOutExtraction.VisibleSuiteLeaks(visibleRun, new[] { custom.Identity }));
    }

    [Fact]
    public void VisibleSuiteLeaks_False_WhenCoveringTestPresentButPassing()
    {
        // Present but passing is not a leak (nothing failing reveals the answer).
        var t = Trx("GeoLib.Tests.GridTests.Area_Multiplies", "Passed");
        Assert.False(HeldOutExtraction.VisibleSuiteLeaks(Run(t), new[] { t.Identity }));
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
