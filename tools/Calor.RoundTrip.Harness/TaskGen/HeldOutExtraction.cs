using System.Text.RegularExpressions;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// Held-out test extraction (C2): identify the covering test(s) for a mutation, remove them
/// from the visible suite, and synthesize the failing-behavior report BOTH arms receive.
/// Pure over <see cref="TestRunResult"/> — unit-testable without the corpus.
/// </summary>
public static partial class HeldOutExtraction
{
    /// <summary>
    /// Covering tests = tests that PASSED on the unmutated baseline but FAILED after the
    /// mutation. This is the mechanical "which test does the defect break" identification
    /// used for injected mutations (and the validation of a mined bug-fix's covering test).
    /// </summary>
    public static List<TestResult> IdentifyCoveringTests(TestRunResult baseline, TestRunResult mutated)
    {
        var basePassed = baseline.Results
            .Where(t => t.Outcome == "Passed")
            .Select(t => t.Identity)
            .ToHashSet();

        return mutated.Results
            .Where(t => t.Outcome == "Failed" && basePassed.Contains(t.Identity))
            .ToList();
    }

    /// <summary>Project the covering <see cref="TestResult"/>s into held-out records.</summary>
    public static List<HeldOutTest> ToHeldOut(IEnumerable<TestResult> covering) =>
        covering.Select(t => new HeldOutTest
        {
            TestName = t.TestName,
            ClassName = t.ClassName,
            Assembly = t.Assembly,
            Identity = t.Identity,
        }).ToList();

    /// <summary>
    /// dotnet <c>--filter</c> expression selecting the VISIBLE suite = full suite minus the
    /// held-out tests. Uses <c>FullyQualifiedName!=</c> clauses ANDed together so the removed
    /// tests are excluded from the arm's visible run while remaining physically present as the
    /// regression net.
    /// </summary>
    public static string BuildVisibleFilter(IEnumerable<HeldOutTest> heldOut)
    {
        var clauses = heldOut
            .Select(h => h.FullyQualifiedName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .Select(n => $"FullyQualifiedName!={n}")
            .ToList();
        return clauses.Count == 0 ? "" : string.Join("&", clauses);
    }

    /// <summary>dotnet <c>--filter</c> that selects ONLY the held-out test(s) (the targeted run for clause (b)).</summary>
    public static string BuildHeldOutFilter(IEnumerable<HeldOutTest> heldOut)
    {
        var clauses = heldOut
            .Select(h => h.TestName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .Select(n => $"FullyQualifiedName~{n}")
            .ToList();
        return clauses.Count == 0 ? "" : string.Join("|", clauses);
    }

    /// <summary>
    /// Synthesize the failing-behavior report from the covering test's failure: the observable
    /// symptom, scrubbed of the test's identity, so neither arm receives the oracle. Falls back
    /// to a generic symptom when no structured error message is available.
    /// </summary>
    public static FailingBehaviorReport SynthesizeFailingBehavior(
        TestResult covering, string? subjectHint = null)
    {
        var raw = covering.ErrorMessage ?? "";
        var observed = ScrubIdentity(FirstMeaningfulLine(raw), covering);
        var symptom = string.IsNullOrWhiteSpace(observed)
            ? "A previously-correct behavior now produces an incorrect result under some input."
            : $"Observed incorrect behavior: {observed}";

        return new FailingBehaviorReport
        {
            Symptom = symptom,
            SubjectHint = subjectHint ?? DeriveSubjectHint(covering),
            Observed = string.IsNullOrWhiteSpace(observed) ? null : observed,
            Notes = "Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. "
                  + "The removed test is held out; the full suite remains as the regression net.",
        };
    }

    private static string FirstMeaningfulLine(string message)
    {
        foreach (var line in message.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 0) return t;
        }
        return "";
    }

    private static string ScrubIdentity(string text, TestResult covering)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var scrubbed = text;
        foreach (var token in new[] { covering.TestName, covering.ClassName })
        {
            if (!string.IsNullOrEmpty(token))
                scrubbed = scrubbed.Replace(token, "<test>", StringComparison.Ordinal);
        }
        return scrubbed;
    }

    /// <summary>
    /// A coarse subject hint (the class/method the defect lives in) derived from the test's
    /// class name by stripping a trailing "Tests" suffix. Never the test itself.
    /// </summary>
    private static string? DeriveSubjectHint(TestResult covering)
    {
        var cls = covering.ClassName;
        if (string.IsNullOrEmpty(cls)) return null;
        var shortName = cls.Contains('.') ? cls[(cls.LastIndexOf('.') + 1)..] : cls;
        var m = TestsSuffix().Match(shortName);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^(.*?)Tests?$")]
    private static partial Regex TestsSuffix();
}
