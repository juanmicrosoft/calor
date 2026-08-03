using System.Text.RegularExpressions;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// Held-out test extraction (C2): identify the covering test(s) for a mutation, remove them
/// from the visible suite, and synthesize the failing-behavior report BOTH arms receive.
/// Pure over <see cref="TestRunResult"/> — unit-testable without the corpus.
///
/// Theory-safe filtering (review [C]): xUnit theory rows report a TRX display name like
/// <c>N.C.Theory(x: 5)</c> whose <c>( ) : space</c> are VSTest <c>--filter</c> metacharacters.
/// Filtering by the verbatim row name hard-parse-errors → 0 tests run → silent 0. So every
/// filter is keyed on the metacharacter-free fully-qualified METHOD name (theory args stripped),
/// which also makes a theory's rows share ONE filter term (the whole theory is held out as a unit).
/// </summary>
public static partial class HeldOutExtraction
{
    /// <summary>
    /// Covering tests = tests that PASSED on the unmutated baseline but FAILED after the
    /// mutation. This is the mechanical "which test does the defect break" identification
    /// used for injected mutations (and the validation of a mined bug-fix's covering test).
    /// For theories, individual failing rows are returned; <see cref="ToHeldOut"/> groups them
    /// to the covering method.
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

    /// <summary>
    /// The fully-qualified METHOD name a VSTest <c>--filter FullyQualifiedName</c> clause matches:
    /// class-qualified and with any theory-argument suffix (<c>(...)</c>) stripped. Metacharacter-free.
    /// </summary>
    public static string MethodFqn(string testName, string className)
    {
        var baseName = !string.IsNullOrEmpty(className) && testName.StartsWith(className + ".", StringComparison.Ordinal)
            ? testName
            : string.IsNullOrEmpty(className) ? testName : $"{className}.{testName}";
        var paren = baseName.IndexOf('(');
        return paren >= 0 ? baseName[..paren] : baseName;
    }

    /// <summary>
    /// Project the covering <see cref="TestResult"/>s into held-out records, GROUPED by covering
    /// method (so a theory contributes one held-out entry, not one per row).
    /// </summary>
    public static List<HeldOutTest> ToHeldOut(IEnumerable<TestResult> covering) =>
        covering
            .GroupBy(t => MethodFqn(t.TestName, t.ClassName), StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                return new HeldOutTest
                {
                    TestName = first.TestName,
                    ClassName = first.ClassName,
                    Assembly = first.Assembly,
                    Identity = first.Identity,
                    FilterName = g.Key,
                };
            })
            .ToList();

    /// <summary>
    /// dotnet <c>--filter</c> expression selecting the VISIBLE suite = full suite minus the
    /// held-out method(s). Uses <c>FullyQualifiedName!~METHOD</c> clauses (not-contains on the
    /// arg-stripped method name) ANDed together, so ALL rows of a held-out theory are excluded
    /// while remaining physically present as the regression net.
    /// </summary>
    public static string BuildVisibleFilter(IEnumerable<HeldOutTest> heldOut)
    {
        var clauses = FilterNames(heldOut)
            .Select(n => $"FullyQualifiedName!~{EscapeFilterValue(n)}")
            .ToList();
        return clauses.Count == 0 ? "" : string.Join("&", clauses);
    }

    /// <summary>dotnet <c>--filter</c> that selects ONLY the held-out method(s) — all their rows (the targeted run for clause (b)).</summary>
    public static string BuildHeldOutFilter(IEnumerable<HeldOutTest> heldOut)
    {
        var clauses = FilterNames(heldOut)
            .Select(n => $"FullyQualifiedName~{EscapeFilterValue(n)}")
            .ToList();
        return clauses.Count == 0 ? "" : string.Join("|", clauses);
    }

    /// <summary>
    /// Visible-filter round-trip guard (review residual-[C]): true iff a covering test is
    /// present-and-FAILING in a run of the VISIBLE suite — i.e. the visible filter did NOT exclude
    /// it (an oracle leak). Naming-agnostic: works for any test whose method FQN was unrecoverable
    /// from TRX (custom <c>[Theory/Fact(DisplayName=...)]</c>), because it checks the actual run
    /// rather than trying to parse the display name.
    /// </summary>
    public static bool VisibleSuiteLeaks(TestRunResult visibleRun, IEnumerable<string> coveringIdentities)
    {
        var ids = coveringIdentities as ISet<string> ?? coveringIdentities.ToHashSet(StringComparer.Ordinal);
        return visibleRun.Results.Any(t => t.Outcome == "Failed" && ids.Contains(t.Identity));
    }

    private static IEnumerable<string> FilterNames(IEnumerable<HeldOutTest> heldOut) =>
        heldOut
            .Select(h => string.IsNullOrEmpty(h.FilterName) ? MethodFqn(h.TestName, h.ClassName) : h.FilterName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Backslash-escape VSTest filter metacharacters in a value. Method FQNs are normally
    /// metacharacter-free, but this is defensive so an unexpected display shape can never inject
    /// an operator into the expression.
    /// </summary>
    public static string EscapeFilterValue(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            if (c is '\\' or '(' or ')' or '&' or '|' or '=' or '!' or '~')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
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
        foreach (var token in new[] { covering.TestName, covering.ClassName, MethodFqn(covering.TestName, covering.ClassName) })
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
