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
