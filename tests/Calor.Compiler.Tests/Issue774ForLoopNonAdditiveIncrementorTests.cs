using Calor.Compiler.Ast;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Regression pin for #774: C# for-loop non-additive compound incrementors
/// (<c>*=</c>, <c>/=</c>, <c>&lt;&lt;=</c>, <c>&gt;&gt;=</c>, <c>-=</c>)
/// currently drop the raw RHS of the compound assignment into the
/// <c>§L{...}</c> additive-step field, silently changing loop semantics —
/// e.g. <c>for (int i = 1; i &lt; 100; i *= 2)</c> becomes a linear loop
/// with additive step <c>2</c> rather than a doubling loop.
///
/// Documented in <c>src/Calor.Compiler/Migration/FeatureSupport.cs:121-126</c>
/// and in the <c>AssignmentExpressionSyntax</c> branch of
/// <c>ConvertForStatements</c> in <c>src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs</c>.
///
/// The pin lives in <c>Calor.Compiler.Tests</c> next to
/// <c>ConverterImprovementTests</c> (the other converter regression suite)
/// so a fixer working #774 sees it on the local test run rather than in
/// the snapshot-focused <c>Calor.Conversion.Tests</c> project. When #774
/// is fixed, remove the <c>Skip</c> attribute below — the assertion is
/// structural (asserts the converted loop is NOT a <c>ForStatementNode</c>)
/// rather than shape-of-emitted-text, so any correct fix
/// (<c>§WH</c> fallback, <c>§CSHARP</c> interop preservation, or a future
/// semantically-modeled non-additive §L) passes.
/// </summary>
public class Issue774ForLoopNonAdditiveIncrementorTests
{
    private readonly CSharpToCalorConverter _converter =
        new(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });

    // NOTE ON DISCRIMINATION: today's converter DOES emit a ForStatementNode
    // with the raw compound-assignment RHS as the additive step for every row
    // below, so removing the Skip attribute today makes every row fail with
    // "expected Empty forNodes, actual 1" — the pin is discriminating.
    //
    // NOTE ON POSITIVE ASSERTIONS: the pin deliberately does NOT assert what
    // the fixed converter should emit (§WH shape, §ASSIGN body, §CSHARP block,
    // etc.) because the fix's chosen strategy is not decided yet. Asserting
    // "shape X is present" would force a rewrite when the reviewer chooses a
    // different strategy. Asserting "shape X (the broken shape) is absent" is
    // sufficient to prove the bug is gone.
    [Theory(Skip = "#774 known issue: for-loop non-additive compound incrementors " +
        "are silently converted to additive §L steps. Remove Skip when the converter " +
        "routes these to §WH fallback or §CSHARP interop preservation; see " +
        "src/Calor.Compiler/Migration/FeatureSupport.cs:121-126 and the " +
        "AssignmentExpressionSyntax branch of ConvertForStatements in " +
        "RoslynSyntaxVisitor.cs.")]
    [InlineData("i *= 2",   "for (int i = 1; i < 100; i *= 2)",     "multiplicative")]
    [InlineData("i /= 2",   "for (int i = 100; i > 1; i /= 2)",     "divide")]
    [InlineData("i <<= 1",  "for (int i = 1; i < 1024; i <<= 1)",   "left-shift")]
    [InlineData("i >>= 1",  "for (int j = 1024; j > 0; j >>= 1)",   "right-shift")]
    [InlineData("i -= 2",   "for (int i = 10; i > 0; i -= 2)",      "subtract-assign")]
    public void NonAdditiveIncrementor_MustNotBecomeAdditiveForLoop(
        string incrementor,
        string forHeader,
        string operatorLabel)
    {
        _ = incrementor;
        _ = operatorLabel;
        var csharp = $$"""
            public class Test
            {
                public void Run()
                {
                    {{forHeader}}
                    {
                        System.Console.WriteLine(0);
                    }
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success,
            "Conversion should succeed. Issues: " +
            string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.Ast);

        // The single method body must NOT contain a top-level ForStatementNode
        // for a non-additive incrementor input. A correct fix produces either
        // a WhileStatementNode (§WH fallback), a CSharpInteropBlockNode
        // (§CSHARP preservation), or some other non-ForStatementNode shape.
        // What it cannot be is a bare ForStatementNode with the raw RHS as
        // the step — that's the silent-semantics-change bug this pins.
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var forNodes = method.Body.OfType<ForStatementNode>().ToList();

        Assert.Empty(forNodes);
    }
}
