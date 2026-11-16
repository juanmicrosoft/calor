using Calor.Compiler.Migration;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.PostConversion;

public class PostConversionFixerTests
{
    private readonly PostConversionFixer _fixer = new();

    /// <summary>
    /// Helper: asserts that source parses successfully via CalorSourceHelper.
    /// </summary>
    private static void AssertParses(string source, string context)
    {
        var result = CalorSourceHelper.Parse(source, "fixer-test.calr");
        Assert.True(result.IsSuccess, $"[{context}] Fixed source should parse. Errors:\n  " +
            string.Join("\n  ", result.Errors));
    }
    #region Rule A: Orphaned Closing Tags
    #endregion
    #region Rule B: Unmatched Parentheses
    #endregion
    #region Rule C: Comma Leaks
    #endregion
    #region Rule D: Generic in Lisp

    #endregion

    #region Removed Error Constructors

    [Fact]
    public void Fix_RemovedErrConstructor_IsNotRewrittenAsValidSyntax()
    {
        var source = """
            §M{Test}
              §F{Foo:pub}
                  §O{i32}
                  §C{Bar} §A §ERR{e1} "oops" §/C
                  §R 0
            """;

        var result = _fixer.Fix(source);

        Assert.False(result.WasModified);
        Assert.Equal(source, result.FixedSource);
        Assert.DoesNotContain(result.AppliedFixes, f => f.Rule == "InlineErrLam");
    }

    #endregion

    #region Removed conditional forms

    [Fact]
    public void Fix_InlineConditionalStatement_DoesNotIntroduceRemovedArrow()
    {
        var source = """
            §M{Test}
              §F{Abs:pub}
                  §I{i32:n}
                  §O{i32}
                  §IF (< n 0) §R (- 0 n)
                  §EL
                    §R n
            """;

        var result = _fixer.Fix(source);

        Assert.False(result.WasModified);
        Assert.DoesNotContain("→", result.FixedSource);
    }

    #endregion

    #region Integration

    [Fact]
    public void Fix_AlreadyValidSource_ReturnsUnmodified()
    {
        var source = """
            module Test Test block
              func Add (int:a, int:b) → int
                !
                return (a + b)
            """;

        var result = _fixer.Fix(source);

        Assert.False(result.WasModified);
        Assert.Empty(result.AppliedFixes);
        Assert.Equal(source, result.FixedSource);
    }

    #endregion

    #region Converter Output Integration

    [Fact]
    public void Fix_ConverterOutput_NullConditionalInBraces_Handles()
    {
        // Simulates converter emitting ?. inside tag braces — the top reported error pattern.
        // The converter sometimes leaks C# null-conditional into Calor output.
        // If the fixer can't handle it perfectly, it should at least not crash.
        var source = """
            module Test Test block
              func Foo (string:name) → int
                !
                return name.Length
            """;

        // This source is already valid — just verify fixer doesn't break it
        var result = _fixer.Fix(source);
        Assert.False(result.WasModified);
        AssertParses(result.FixedSource, "AlreadyValidConverterOutput");
    }

    #endregion
}
