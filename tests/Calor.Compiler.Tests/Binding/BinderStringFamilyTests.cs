using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B4: the string family (4 classes) binds with PER-OPERATION result types derived
/// from the op enums' own semantics — the first family with genuinely typed results.
/// Every AST property retained (ComparisonMode, format/alignment clauses — the B3
/// ContainsMode lesson applied up front).
/// </summary>
public class BinderStringFamilyTests
{
    private static readonly TextSpan S = new(0, 0, 1, 1);

    private static (BoundExpression Expr, DiagnosticBag Diagnostics) BindReturn(ExpressionNode expr)
    {
        var func = new FunctionNode(S, "f001", "Probe", Visibility.Public,
            Array.Empty<ParameterNode>(), new OutputNode(S, "OBJECT"), null,
            new StatementNode[] { new ReturnStatementNode(S, expr) },
            new AttributeCollection());
        var module = new ModuleNode(S, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(), new[] { func }, new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        var ret = bound.Functions.Single().Body.OfType<BoundReturnStatement>().Single();
        return (ret.Expression!, diagnostics);
    }

    private static void AssertComplete(DiagnosticBag d) =>
        Assert.DoesNotContain(d, x => x.Code == DiagnosticCode.AnalysisIncomplete);

    private static StringLiteralNode Str(string s) => new(S, s);

    [Theory]
    [InlineData(StringOp.Length, "INT")]
    [InlineData(StringOp.IndexOf, "INT")]
    [InlineData(StringOp.Contains, "BOOL")]
    [InlineData(StringOp.StartsWith, "BOOL")]
    [InlineData(StringOp.IsNullOrEmpty, "BOOL")]
    [InlineData(StringOp.RegexTest, "BOOL")]
    [InlineData(StringOp.Split, "str[]")]
    [InlineData(StringOp.RegexSplit, "str[]")]
    [InlineData(StringOp.RegexMatch, "OBJECT")]
    [InlineData(StringOp.ToUpper, "STRING")]
    [InlineData(StringOp.Concat, "STRING")]
    public void StringOperation_DerivesResultType_PerOp(StringOp op, string expected)
    {
        var (expr, diags) = BindReturn(new StringOperationNode(S, op,
            new ExpressionNode[] { Str("a"), Str("b") }));
        var bound = Assert.IsType<BoundStringOperation>(expr);
        Assert.Equal(expected, bound.TypeName);
        Assert.Equal(2, bound.Arguments.Count);
        AssertComplete(diags);
    }

    [Fact]
    public void StringOperation_RetainsComparisonMode()
    {
        // The B3 ContainsMode lesson, applied up front: the mode selects the
        // StringComparison overload — a different operation, not decoration.
        var (expr, _) = BindReturn(new StringOperationNode(S, StringOp.Contains,
            new ExpressionNode[] { Str("a"), Str("b") }, StringComparisonMode.IgnoreCase));
        Assert.Equal(StringComparisonMode.IgnoreCase,
            Assert.IsType<BoundStringOperation>(expr).ComparisonMode);
    }

    [Fact]
    public void InterpolatedString_RetainsPartOrder_FormatAndAlignment()
    {
        var node = new InterpolatedStringNode(S, new InterpolatedStringPartNode[]
        {
            new InterpolatedStringTextNode(S, "total: "),
            new InterpolatedStringExpressionNode(S, new IntLiteralNode(S, 42), "N2", "8"),
        });
        var (expr, diags) = BindReturn(node);
        var bound = Assert.IsType<BoundInterpolatedString>(expr);
        Assert.Equal("STRING", bound.TypeName);
        Assert.Equal(2, bound.Parts.Count);
        Assert.Equal("total: ", bound.Parts[0].Text);
        Assert.NotNull(bound.Parts[1].Expression);
        Assert.Equal("N2", bound.Parts[1].FormatSpecifier);
        Assert.Equal("8", bound.Parts[1].AlignmentClause);
        AssertComplete(diags);
    }

    [Theory]
    [InlineData(StringBuilderOp.New, "StringBuilder")]
    [InlineData(StringBuilderOp.Append, "StringBuilder")]
    [InlineData(StringBuilderOp.ToString, "STRING")]
    [InlineData(StringBuilderOp.Length, "INT")]
    public void StringBuilderOperation_DerivesResultType_PerOp(StringBuilderOp op, string expected)
    {
        var (expr, diags) = BindReturn(new StringBuilderOperationNode(S, op,
            new ExpressionNode[] { Str("x") }));
        Assert.Equal(expected, Assert.IsType<BoundStringBuilderOperation>(expr).TypeName);
        AssertComplete(diags);
    }

    [Theory]
    [InlineData(CharOp.CharAt, "CHAR")]
    [InlineData(CharOp.CharCode, "INT")]
    [InlineData(CharOp.IsDigit, "BOOL")]
    [InlineData(CharOp.ToUpperChar, "CHAR")]
    public void CharOperation_DerivesResultType_PerOp(CharOp op, string expected)
    {
        var (expr, diags) = BindReturn(new CharOperationNode(S, op,
            new ExpressionNode[] { Str("a") }));
        Assert.Equal(expected, Assert.IsType<BoundCharOperation>(expr).TypeName);
        AssertComplete(diags);
    }

    [Fact]
    public void BoundChildren_EnumeratesEveryB4NodeChild()
    {
        var i = new BoundIntLiteral(S, 1);
        var j = new BoundIntLiteral(S, 2);
        Assert.Equal([i, j], BoundChildren.Of(
            new BoundStringOperation(S, StringOp.Contains, [i, j], null)));
        Assert.Equal([i], BoundChildren.Of(new BoundInterpolatedString(S,
        [
            new BoundInterpolationPart("t", null, null, null, S),
            new BoundInterpolationPart(null, i, null, null, S),
        ])));
        Assert.Equal([i], BoundChildren.Of(
            new BoundStringBuilderOperation(S, StringBuilderOp.Append, [i])));
        Assert.Equal([i], BoundChildren.Of(
            new BoundCharOperation(S, CharOp.CharAt, [i])));
    }

    [Fact]
    public void NestedDivision_InsideStringOperation_ProducesRealFinding_EndToEnd()
    {
        // The family's span-anchored e2e pin: /0 nested in a string-op argument must
        // produce the real Calor0920 pointing at the right line. ((str x) = ToString.)
        const string source = @"
§M{m001:Test}
  §F{f001:Trap:pub} () -> void
    §E{cw}
    §P (contains STR:""ab"" (str (/ 10 0)))";

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
            VerificationAnalysisOptions = new Compiler.Analysis.VerificationAnalysisOptions
            {
                BugPatternOptions = new Compiler.Analysis.BugPatterns.BugPatternOptions
                {
                    ReportOnlyVerified = false
                }
            }
        });

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.DivisionByZero && d.Span.Line == 5);
    }
}
