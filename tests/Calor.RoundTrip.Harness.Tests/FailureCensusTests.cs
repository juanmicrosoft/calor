using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Pins the WS-S1 census gate. The rule is pre-committed (gates A-1.6(b)): top-3 causes ≥ 50% →
/// continue WS-S1; otherwise PP-S1 = miss. It is encoded rather than applied by hand precisely so it
/// cannot be softened once the numbers are visible — these tests are what make that true.
/// </summary>
public class FailureCensusTests
{
    private static (string, string, IReadOnlyList<string>) F(string status, string path, params string[] errors) =>
        (status, path, errors);

    [Fact]
    public void Compiler_codes_are_the_bucket_key()
    {
        Assert.Equal("CompileError:CS0246",
            FailureCensus.NormalizeCause("CompileError", ["/x/A.cs(3,5): error CS0246: type not found"]));
        Assert.Equal("EmitSyntaxError:Calor0118",
            FailureCensus.NormalizeCause("EmitSyntaxError", ["Calor0118: nesting too deep"]));
    }

    [Fact]
    public void Messages_without_a_code_collapse_to_a_stable_shape()
    {
        // Same defect, different files/positions/identifiers → one bucket, not three.
        var a = FailureCensus.NormalizeCause("Reverted", ["/w/A.cs(1,2): the member 'Foo' broke"]);
        var b = FailureCensus.NormalizeCause("Reverted", ["/w/B.cs(99,4): the member 'Bar' broke"]);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Token_names_and_column_numbers_collapse_so_one_bug_is_one_cause()
    {
        // Nothing pinned the normalization before, which is the axis the verdict is most sensitive
        // to — the regex could have been changed and all tests still passed. These are the two real
        // cases: one parser bug had been split 10+3+1 and one indent bug 4+1+1.
        var a = FailureCensus.NormalizeCause("CompileError", ["Expected EXT, METHOD but found Class"]);
        var b = FailureCensus.NormalizeCause("CompileError", ["Expected EXT, METHOD but found Interface"]);
        Assert.Equal(a, b);

        var c = FailureCensus.NormalizeCause("CompileError", ["Dedent to column 4 does not match"]);
        var d = FailureCensus.NormalizeCause("CompileError", ["Dedent to column 6 does not match"]);
        Assert.Equal(c, d);
    }

    [Fact]
    public void Distinct_diagnostics_do_NOT_collapse_together()
    {
        // The other direction: over-collapsing would manufacture concentration and flip the gate.
        var a = FailureCensus.NormalizeCause("CompileError", ["Expected EXT, METHOD but found Class"]);
        var b = FailureCensus.NormalizeCause("CompileError", ["Dedent to column 4 does not match"]);
        Assert.NotEqual(a, b);
        Assert.NotEqual(
            FailureCensus.NormalizeCause("Reverted", ["x.cs(1,2): error CS0103: no name"]),
            FailureCensus.NormalizeCause("Reverted", ["x.cs(1,2): error CS0246: no type"]));
    }

    [Fact]
    public void First_error_wins_is_the_pinned_tie_break()
    {
        // A file with several diagnostics is bucketed by its FIRST — a proxy for earliest source
        // position. Switching to last-wins would move the shares, so the choice is pinned.
        var cause = FailureCensus.NormalizeCause("Reverted",
            ["a.cs(1,1): error CS0246: type", "a.cs(9,9): error CS0103: name"]);
        Assert.Equal("Reverted:CS0246", cause);
    }

    [Fact]
    public void The_verdict_string_is_culture_invariant()
    {
        // The Verdict is serialized into the committed record. `:P1` renders "40,4 %" under de-DE —
        // the same reproducibility defect fixed in the sibling report writer one change earlier.
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var r = FailureCensus.Analyse(
                Enumerable.Range(1, 10).Select(i => F("CompileError", $"f{i}", $"error CS{i:0000}:")).ToList());
            Assert.Contains("30.0%", r.Verdict);
            Assert.DoesNotContain("30,0", r.Verdict);   // de-DE decimal comma must not reach the record
            Assert.DoesNotContain("50,0", r.Verdict);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void A_failure_with_no_errors_is_counted_as_unattributed_not_dropped()
    {
        var r = FailureCensus.Analyse([F("Reverted", "p/A.cs")]);
        Assert.Equal(1, r.TotalFailures);
        Assert.Equal(1, r.Unattributed);
        Assert.Contains(r.Causes, c => c.Cause.EndsWith(":unattributed"));
    }

    [Fact]
    public void Top3_at_or_above_the_threshold_continues()
    {
        // 3 causes covering 5 of 8 = 62.5%.
        var r = FailureCensus.Analyse([
            F("CompileError", "a1", "error CS0001:"), F("CompileError", "a2", "error CS0001:"),
            F("CompileError", "b1", "error CS0002:"), F("CompileError", "b2", "error CS0002:"),
            F("CompileError", "c1", "error CS0003:"),
            F("CompileError", "d1", "error CS0004:"), F("CompileError", "e1", "error CS0005:"),
            F("CompileError", "f1", "error CS0006:"),
        ]);
        Assert.True(r.Top3Share >= FailureCensus.Top3ContinueThreshold);
        Assert.StartsWith("CONTINUE WS-S1", r.Verdict);
    }

    [Fact]
    public void A_long_tail_resolves_PP_S1_miss()
    {
        // 10 distinct causes, one file each: top-3 = 30% < 50%.
        var r = FailureCensus.Analyse(
            Enumerable.Range(1, 10).Select(i => F("CompileError", $"f{i}", $"error CS{i:0000}:")).ToList());
        Assert.Equal(0.3, r.Top3Share, 3);
        Assert.StartsWith("PP-S1 = MISS", r.Verdict);
    }

    [Fact]
    public void The_gap_case_an_earlier_wording_left_unrouted_resolves_to_MISS()
    {
        // top-3 = 40% (<50%) but top-10 = 100% (≥50%). The first wording of the rule routed neither
        // way here; the exhaustive form makes it a miss.
        var r = FailureCensus.Analyse([
            F("CompileError", "a1", "error CS0001:"), F("CompileError", "a2", "error CS0001:"),
            F("CompileError", "b1", "error CS0002:"),
            F("CompileError", "c1", "error CS0003:"),
            F("CompileError", "d1", "error CS0004:"), F("CompileError", "e1", "error CS0005:"),
            F("CompileError", "f1", "error CS0006:"), F("CompileError", "g1", "error CS0007:"),
            F("CompileError", "h1", "error CS0008:"), F("CompileError", "i1", "error CS0009:"),
        ]);
        Assert.True(r.Top3Share < FailureCensus.Top3ContinueThreshold);
        Assert.True(r.Top10Share >= 0.5);
        Assert.StartsWith("PP-S1 = MISS", r.Verdict);
    }

    [Fact]
    public void No_failures_is_undecidable_not_a_pass()
    {
        var r = FailureCensus.Analyse([]);
        Assert.StartsWith("UNDECIDABLE", r.Verdict);
    }

    [Fact]
    public void Causes_are_ranked_by_file_count_with_a_stable_tiebreak()
    {
        var r = FailureCensus.Analyse([
            F("CompileError", "z", "error CS0002:"),
            F("CompileError", "a", "error CS0001:"), F("CompileError", "b", "error CS0001:"),
        ]);
        Assert.Equal("CompileError:CS0001", r.Causes[0].Cause);
        Assert.Equal(2, r.Causes[0].Files);
    }
}
