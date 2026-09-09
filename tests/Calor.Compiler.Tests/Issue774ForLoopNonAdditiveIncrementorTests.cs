using Calor.Compiler.Ast;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Active regression pin for #774/#996, resolved by #1194.
/// Compound incrementors retain their C# operations instead of becoming
/// additive range steps. Runtime controls live in ForLoopConditionSemanticsTests.
/// </summary>
public class Issue774ForLoopNonAdditiveIncrementorTests
{
    private readonly CSharpToCalorConverter _converter =
        new(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });

    [Theory]
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
        Assert.Contains("§WH{", result.CalorSource);
    }
}
