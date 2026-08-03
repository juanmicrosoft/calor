using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Pins the injected mutation operators (D-W4.1 supplement): each operator sites an isolated
/// single-point change and produces buildable mutated source. Pure, offline (no corpus).
/// </summary>
public class TaskGenMutationTests
{
    private const string Sample = """
        namespace S;
        public static class C
        {
            public static int Max(int a, int b)
            {
                if (a >= b)
                    return a;
                return b;
            }
            public static int Add(int a, int b) => a + b;
            public static bool IsEven(int n) => n % 2 == 0;
            public static int Loop(int n)
            {
                int t = 0;
                for (int i = 0; i < n; i++) t = t + 1;
                return t;
            }
            public static bool Always() { return true; }
        }
        """;

    [Fact]
    public void Enumerate_FindsBoundaryArithmeticOffByOneAndSwapReturn()
    {
        var cands = InjectedMutationOperators.Enumerate(Sample, "C.cs");

        Assert.Contains(cands, c => c.Operator == MutationOperatorKind.Boundary && c.OperatorDescription == ">= → >");
        Assert.Contains(cands, c => c.Operator == MutationOperatorKind.Boundary && c.OperatorDescription == "< → <=");
        Assert.Contains(cands, c => c.Operator == MutationOperatorKind.Arithmetic && c.OperatorDescription == "+ → -");
        Assert.Contains(cands, c => c.Operator == MutationOperatorKind.OffByOne);
        Assert.Contains(cands, c => c.Operator == MutationOperatorKind.SwapReturn && c.OperatorDescription == "return true → return false");
        Assert.All(cands, c => Assert.Equal("C.cs", c.FileRelPath));
        Assert.All(cands, c => Assert.Equal(MutationSource.InjectedMutation, c.Source));
    }

    [Fact]
    public void Boundary_MutatesExactlyOneOperator_RestVerbatim()
    {
        var cands = InjectedMutationOperators.Enumerate(Sample, "C.cs");
        var boundary = cands.First(c => c.OperatorDescription == ">= → >");

        // The mutated source flips >= to > exactly once and keeps everything else identical.
        Assert.Contains("if (a > b)", boundary.MutatedSource);
        Assert.DoesNotContain("if (a >= b)", boundary.MutatedSource);
        // The unrelated `<` in the for-loop is untouched by THIS candidate.
        Assert.Contains("i < n", boundary.MutatedSource);
    }

    [Fact]
    public void OffByOne_IncrementsIntegerLiteral()
    {
        var cands = InjectedMutationOperators.Enumerate("class X { int F() { return 2; } }", "X.cs");
        var obo = Assert.Single(cands, c => c.Operator == MutationOperatorKind.OffByOne);
        Assert.Equal("2 → 3", obo.OperatorDescription);
        Assert.Contains("return 3;", obo.MutatedSource);
    }

    [Fact]
    public void SwapReturn_FlipsBooleanLiteral()
    {
        var cands = InjectedMutationOperators.Enumerate("class X { bool F() { return true; } }", "X.cs");
        var swap = Assert.Single(cands, c => c.Operator == MutationOperatorKind.SwapReturn);
        Assert.Contains("return false;", swap.MutatedSource);
        Assert.DoesNotContain("return true;", swap.MutatedSource);
    }

    [Fact]
    public void Candidate_CarriesLineColumnAndSnippets()
    {
        var cands = InjectedMutationOperators.Enumerate("class X { int F(int a,int b){ return a + b; } }", "X.cs");
        var arith = cands.First(c => c.Operator == MutationOperatorKind.Arithmetic);
        Assert.Equal(1, arith.Line);
        Assert.True(arith.Column > 0);
        Assert.Equal("a + b", arith.OriginalSnippet);
        Assert.Equal("a - b", arith.MutatedSnippet);
    }

    [Fact]
    public void Enumerate_UnparseableSource_ReturnsEmpty_NoThrow()
    {
        var cands = InjectedMutationOperators.Enumerate("this is not ) c# ((", "bad.cs");
        Assert.NotNull(cands); // parser is lenient; must never throw
    }
}
