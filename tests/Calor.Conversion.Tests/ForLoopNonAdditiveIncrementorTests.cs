using Xunit;

namespace Calor.Conversion.Tests;

/// <summary>
/// Pins the known-bad conversion of C# for-loop non-additive compound
/// incrementors (<c>*=</c>, <c>/=</c>, <c>&lt;&lt;=</c>, <c>&gt;&gt;=</c>,
/// <c>-=</c>) into Calor.
///
/// Background: Calor's <c>§L{id:var:from:to:step}</c> loop takes an ADDITIVE
/// step. The current C# → Calor converter, however, extracts the RHS of any
/// compound assignment in the incrementor slot and drops it into the §L step
/// field verbatim — so <c>for (int i = 1; i &lt; 100; i *= 2)</c> becomes a
/// loop with additive step <c>2</c> (linear) instead of a multiplicative
/// doubling loop. See #774 and the note at
/// <c>src/Calor.Compiler/Migration/FeatureSupport.cs:121-126</c>.
///
/// This test is intentionally marked <c>Skip</c> so it does not fail the
/// suite today, but it documents the CORRECT expected behavior. When #774
/// is fixed, the reviewer removes the <c>Skip</c> attribute rather than
/// hunting through unrelated snapshots to notice the semantic change.
/// </summary>
public class ForLoopNonAdditiveIncrementorTests
{
    /// <summary>
    /// The intended fix (per <c>FeatureSupport.cs:121-126</c> and the note in
    /// <c>Migration/RoslynSyntaxVisitor.cs</c> around
    /// <c>ConvertForStatements</c>) is to route non-additive compound
    /// incrementors to the existing while-loop fallback rather than
    /// pretending they are additive §L steps. When that lands, the emitted
    /// Calor should express the loop as a <c>§WH</c> block whose body
    /// updates <c>i</c> multiplicatively — NOT a §L header with
    /// <c>:1:99:2</c>.
    /// </summary>
    [Fact(Skip = "#774 known issue: for-loop non-additive compound incrementors take raw RHS as additive step")]
    public void MultiplicativeIncrementor_ConvertsToWhileLoopFallback()
    {
        // #774 pin — see src/Calor.Compiler/Migration/FeatureSupport.cs:121-126
        // and the KNOWN LIMITATION comment in
        // src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs in
        // ConvertForStatements where the AssignmentExpressionSyntax branch
        // unconditionally uses ConvertExpression(assignment.Right) as the §L
        // step.
        var csharp = """
            public class Doubler
            {
                public void Run()
                {
                    for (int i = 1; i < 100; i *= 2)
                    {
                        System.Console.WriteLine(i);
                    }
                }
            }
            """;

        var result = TestHelpers.ConvertCSharp(csharp, "Test_Issue774_Multiplicative");

        Assert.True(result.Success,
            "Conversion should succeed: " +
            string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);

        var calor = result.CalorSource!;

        // Correct behavior: `i *= 2` is NOT an additive step. The converter
        // must fall back to a while loop (§WH) rather than dropping `2` into
        // a §L header as if it were an additive step.
        Assert.DoesNotContain("§L{", calor);
        Assert.Contains("§WH", calor);

        // And the body must actually perform the multiplicative update, so
        // the loop still doubles `i` on each iteration.
        Assert.Contains("*=", calor);
    }

    /// <summary>
    /// Right-shift compound assignment is another non-additive form covered
    /// by the same #774 limitation — <c>j &gt;&gt;= 1</c> halves <c>j</c>,
    /// but today's converter drops <c>1</c> into the §L additive step.
    /// </summary>
    [Fact(Skip = "#774 known issue: for-loop non-additive compound incrementors take raw RHS as additive step")]
    public void RightShiftIncrementor_ConvertsToWhileLoopFallback()
    {
        // #774 pin — see src/Calor.Compiler/Migration/FeatureSupport.cs:121-126.
        var csharp = """
            public class Halver
            {
                public void Run()
                {
                    for (int j = 1024; j > 0; j >>= 1)
                    {
                        System.Console.WriteLine(j);
                    }
                }
            }
            """;

        var result = TestHelpers.ConvertCSharp(csharp, "Test_Issue774_RightShift");

        Assert.True(result.Success,
            "Conversion should succeed: " +
            string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);

        var calor = result.CalorSource!;

        // Correct behavior: `j >>= 1` is NOT an additive step. Must fall
        // back to §WH so the body can preserve the shift semantics.
        Assert.DoesNotContain("§L{", calor);
        Assert.Contains("§WH", calor);
        Assert.Contains(">>=", calor);
    }

    /// <summary>
    /// Negative compound assignment (<c>-=</c>) is technically additive but
    /// the current converter uses the RAW RHS as the step, producing a
    /// POSITIVE step of 2 for <c>i -= 2</c> instead of <c>-2</c>. When #774
    /// is fixed, the converter should either negate the RHS for <c>-=</c>
    /// or fall back to a while loop — either way the emitted loop must
    /// actually count down.
    /// </summary>
    [Fact(Skip = "#774 known issue: for-loop non-additive compound incrementors take raw RHS as additive step")]
    public void SubtractAssignIncrementor_ProducesCountDownLoop()
    {
        // #774 pin — see src/Calor.Compiler/Migration/FeatureSupport.cs:121-126.
        var csharp = """
            public class CountDown
            {
                public void Run()
                {
                    for (int i = 10; i > 0; i -= 2)
                    {
                        System.Console.WriteLine(i);
                    }
                }
            }
            """;

        var result = TestHelpers.ConvertCSharp(csharp, "Test_Issue774_MinusEquals");

        Assert.True(result.Success,
            "Conversion should succeed: " +
            string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);

        var calor = result.CalorSource!;

        // Correct behavior for `i -= 2`: either a §L with a NEGATIVE additive
        // step (e.g. `:-2` in the header) or a §WH fallback whose body
        // subtracts 2 from `i`. A bare positive `2` in the §L step field is
        // WRONG because it turns the loop into an infinite loop (i grows
        // while the condition is `i > 0`).
        var isWhileFallback = calor.Contains("§WH");
        var isNegativeStep = calor.Contains(":-2") || calor.Contains(":-2:") || calor.Contains(":-2\n");
        Assert.True(isWhileFallback || isNegativeStep,
            "Expected either §WH fallback or a negative §L step. Actual Calor:\n" + calor);
    }
}
