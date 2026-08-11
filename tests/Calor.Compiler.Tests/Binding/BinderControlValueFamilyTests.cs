using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B6: the control-value family (5 classes — the conversion-leg payload; lambda
/// alone was 46% of the original incomplete mass). The family introduces DEFERRED
/// evaluation semantics: BoundChildren.Of() sees everything (value-safety traversals
/// deliberately walk conditionally-executed subtrees), and DeferredOf() identifies
/// which children are not-necessarily-executed (for occurrence-sensitive analyses).
/// </summary>
public class BinderControlValueFamilyTests
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

    [Fact]
    public void NullCoalesce_TypeFollowsLeft_RightIsDeferred()
    {
        var (expr, diags) = BindReturn(new NullCoalesceNode(S,
            new IntLiteralNode(S, 1), new IntLiteralNode(S, 2)));
        var nc = Assert.IsType<BoundNullCoalesce>(expr);
        Assert.Equal("INT", nc.TypeName);
        Assert.Equal(2, BoundChildren.Of(nc).Count());
        Assert.Equal([nc.Right], BoundChildren.DeferredOf(nc));
        AssertComplete(diags);
    }

    [Fact]
    public void NullConditional_BindsTarget_MemberRetained()
    {
        var (expr, diags) = BindReturn(new NullConditionalNode(S,
            new ReferenceNode(S, "obj"), "Length"));
        var ncond = Assert.IsType<BoundNullConditional>(expr);
        Assert.Equal("Length", ncond.MemberName);
        Assert.Empty(BoundChildren.DeferredOf(ncond)); // target itself always evaluates
        AssertComplete(diags);
    }

    [Fact]
    public void Match_BindsScrutineeGuardsAndBodies_ArmsDeferred()
    {
        var node = new MatchExpressionNode(S, "mx1", new ReferenceNode(S, "x"),
            new[]
            {
                new MatchCaseNode(S,
                    new ConstantPatternNode(S, new IntLiteralNode(S, 0)),
                    new BinaryOperationNode(S, BinaryOperator.GreaterThan,
                        new ReferenceNode(S, "x"), new IntLiteralNode(S, 5)),
                    new StatementNode[] { new ReturnStatementNode(S, new IntLiteralNode(S, 1)) }),
            }, new AttributeCollection());
        var (expr, diags) = BindReturn(node);
        var match = Assert.IsType<BoundMatchExpression>(expr);
        Assert.Equal("mx1", match.Id);
        Assert.Single(match.Cases);
        Assert.NotNull(match.Cases[0].Guard);
        Assert.Single(match.Cases[0].Body);
        Assert.IsType<ConstantPatternNode>(match.Cases[0].Pattern); // AST-retained
        Assert.Single(BoundChildren.DeferredOf(match)); // the guard
        AssertComplete(diags);
    }

    [Fact]
    public void Lambda_ParametersScoped_BodyDeferred_OuterScopeClean()
    {
        // Lambda parameter `y` binds inside the body but must NOT leak to the outer
        // scope — the lambda after-use of `y` is the leak probe.
        var lambda = new LambdaExpressionNode(S, "lam1",
            new[] { new LambdaParameterNode(S, "y", "i32") }, null,
            isAsync: false,
            new BinaryOperationNode(S, BinaryOperator.Add,
                new ReferenceNode(S, "y"), new IntLiteralNode(S, 1)),
            null, new AttributeCollection());
        var use = new ReturnStatementNode(S, new ReferenceNode(S, "y"));
        var func = new FunctionNode(S, "f001", "Probe", Visibility.Public,
            Array.Empty<ParameterNode>(), new OutputNode(S, "OBJECT"), null,
            new StatementNode[] { new ExpressionStatementNode(S, lambda), use },
            new AttributeCollection());
        var module = new ModuleNode(S, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(), new[] { func }, new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);

        var boundLambda = bound.Functions.Single().Body
            .OfType<BoundExpressionStatement>().Select(s => s.Expression)
            .OfType<BoundLambda>().Single();
        Assert.Single(boundLambda.Parameters);
        Assert.NotNull(boundLambda.ExpressionBody);
        Assert.Equal([boundLambda.ExpressionBody!], BoundChildren.DeferredOf(boundLambda));
        // `y` inside the body bound cleanly; `y` OUTSIDE did not:
        Assert.Contains(diagnostics,
            d => d.Code == DiagnosticCode.UndefinedReference && d.Message.Contains("'y'"));
    }

    [Fact]
    public void Await_UnwrapsTaskTypeString_ConfigureAwaitRetained()
    {
        var (expr, diags) = BindReturn(new AwaitExpressionNode(S,
            new ReferenceNode(S, "t"), configureAwait: false));
        var aw = Assert.IsType<BoundAwaitExpression>(expr);
        Assert.False(aw.ConfigureAwait);
        AssertComplete(diags);

        // String-level unwrap pins (constructed directly — variable types are surface).
        Assert.Equal("i32",
            new BoundAwaitExpression(S,
                new BoundVariableExpression(S, new VariableSymbol("t", "Task<i32>", false)),
                null).TypeName);
        Assert.Equal("VOID",
            new BoundAwaitExpression(S,
                new BoundVariableExpression(S, new VariableSymbol("t", "Task", false)),
                null).TypeName);
    }

    [Fact]
    public void BoundChildren_EnumeratesEveryB6NodeChild()
    {
        var i = new BoundIntLiteral(S, 1);
        var j = new BoundIntLiteral(S, 2);
        Assert.Equal([i, j], BoundChildren.Of(new BoundNullCoalesce(S, i, j)));
        Assert.Equal([i], BoundChildren.Of(new BoundNullConditional(S, i, "M")));
        var guard = new BoundBoolLiteral(S, true);
        var match = new BoundMatchExpression(S, "m", i,
            [new BoundMatchExpressionCase(new ConstantPatternNode(S, new IntLiteralNode(S, 0)),
                guard, [], S)]);
        Assert.Equal(new BoundExpression[] { i, guard }, BoundChildren.Of(match));
        var lam = new BoundLambda(S, "l", [], false, false, j, null);
        Assert.Equal([j], BoundChildren.Of(lam));
        Assert.Equal([i], BoundChildren.Of(new BoundAwaitExpression(S, i, null)));
    }

    [Fact]
    public void NestedDivision_InsideCoalesceFallback_ProducesRealFinding_EndToEnd()
    {
        // Value-safety traversals deliberately walk DEFERRED children: a /0 in the ??
        // fallback is a real latent bug (it is about WHEN it executes, not WHETHER the
        // expression is wrong) — Of() includes it, and the finding fires.
        const string source = @"
§M{m001:Test}
  §F{f001:Trap:pub} (i32:x) -> i32
    §R (?? x (/ 10 0))";

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
            VerificationAnalysisOptions = new Compiler.Analysis.VerificationAnalysisOptions
            {
                BugPatternOptions = new Compiler.Analysis.BugPatterns.BugPatternOptions
                {
                    ReportOnlyVerified = true
                }
            }
        });

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.DivisionByZero && d.Span.Line == 4);
    }
}
