using Calor.Compiler.Ast;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for decomposing deeply nested ternary expressions into if/else statements.
/// Depth ≤ 2 stays inline as (? cond true false); depth > 2 decomposes.
/// </summary>
public class TernaryDecompositionTests
{
    private readonly CSharpToCalorConverter _converter = new();

    private string ConvertToCalor(string csharp)
    {
        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));
        var emitter = new CalorEmitter();
        return emitter.Emit(result.Ast!);
    }

    [Fact]
    public void SimpleTernary_Depth1_StaysInline()
    {
        var csharp = """
            public class C
            {
                public int M(bool a) => a ? 1 : 2;
            }
            """;

        var calor = ConvertToCalor(csharp);

        Assert.Contains("(? a 1 2)", calor);
        Assert.DoesNotContain("_ternResult", calor);
    }

    [Fact]
    public void DoubleNested_Depth2_StaysInline()
    {
        var csharp = """
            public class C
            {
                public int M(bool a, bool b) => a ? (b ? 1 : 2) : 3;
            }
            """;

        var calor = ConvertToCalor(csharp);

        Assert.Contains("(? a (? b 1 2) 3)", calor);
        Assert.DoesNotContain("_ternResult", calor);
    }

    [Fact]
    public void TripleNested_Depth3_DecomposesToIfElse()
    {
        var csharp = """
            public class C
            {
                public int M(bool a, bool b, bool c)
                {
                    return a ? (b ? (c ? 1 : 2) : 3) : 4;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);

        // Should have: bind, if/else, return
        Assert.True(method.Body.Count >= 3, $"Expected ≥3 statements, got {method.Body.Count}");

        var bind = Assert.IsType<BindStatementNode>(method.Body[0]);
        Assert.Contains("_tern", bind.Name);
        Assert.True(bind.IsMutable);

        Assert.IsType<IfStatementNode>(method.Body[1]);

        var ret = Assert.IsType<ReturnStatementNode>(method.Body[^1]);
        var refNode = Assert.IsType<ReferenceNode>(ret.Expression);
        Assert.Equal(bind.Name, refNode.Name);
    }

    [Fact]
    public void TripleNested_Depth3_EmitsIfElseSyntax()
    {
        var csharp = """
            public class C
            {
                public int M(bool a, bool b, bool c)
                {
                    return a ? (b ? (c ? 1 : 2) : 3) : 4;
                }
            }
            """;

        var calor = ConvertToCalor(csharp);

        Assert.Contains("§B{~_ternResult", calor);
        Assert.Contains("§IF", calor);
        Assert.Contains("§EL", calor);
        Assert.DoesNotContain("(? a (? b (? c", calor);
    }

    [Fact]
    public void MixedTernaryWithMethodCalls_DecomposesCorrectly()
    {
        var csharp = """
            public class C
            {
                public string M(bool a, bool b, bool c)
                {
                    return a ? (b ? (c ? "x" : "y") : "z") : "w";
                }
            }
            """;

        var calor = ConvertToCalor(csharp);

        Assert.Contains("§B{~_ternResult", calor);
        Assert.Contains("§IF", calor);
        Assert.DoesNotContain("(? a (? b", calor);
    }

    [Fact]
    public void TernaryInReturnStatement_DecomposesWithCorrectReturn()
    {
        var csharp = """
            public class C
            {
                public int Compute(int x)
                {
                    return x > 0 ? (x > 10 ? (x > 100 ? 3 : 2) : 1) : 0;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);

        // Last statement should be a return referencing the result variable
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[^1]);
        var refNode = Assert.IsType<ReferenceNode>(ret.Expression);
        Assert.Contains("_tern", refNode.Name);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
            return string.Join("\n", result.Issues.Select(i => i.Message));
        return "Conversion failed with no specific error message";
    }
}
