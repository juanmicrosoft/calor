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

    private static (BoundExpression Expr, DiagnosticBag Diagnostics) BindReturn(
        ExpressionNode expr,
        params (string Name, string Type)[] parameters)
    {
        var func = new FunctionNode(S, "f001", "Probe", Visibility.Public,
            parameters.Select(parameter =>
                new ParameterNode(
                    S,
                    parameter.Name,
                    parameter.Type,
                    new AttributeCollection())).ToArray(),
            new OutputNode(S, "OBJECT"), null,
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
        var nc = Assert.IsType<BoundStructuralExpression>(expr);
        Assert.Equal("INT", nc.TypeName);
        Assert.Equal(2, BoundChildren.Of(nc).Count());
        Assert.Equal([nc.Children[1]], BoundChildren.DeferredOf(nc));
        AssertComplete(diags);
    }

    [Fact]
    public void NullConditional_BindsTarget_MemberRetained()
    {
        var (expr, diags) = BindReturn(new NullConditionalNode(S,
            new ReferenceNode(S, "obj"), "Length"));
        var ncond = Assert.IsType<BoundStructuralExpression>(expr);
        Assert.Equal("Length", ncond.Metadata["MemberName"]);
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
        Assert.Equal(nameof(ConstantPatternNode), match.Cases[0].Pattern.Kind);
        Assert.Equal(2, BoundChildren.DeferredOf(match).Count()); // guard + arm result
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
            .OfType<BoundLambdaExpression>().Single();
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
            new ReferenceNode(S, "t"), configureAwait: false), ("t", "Task<i32>"));
        var aw = Assert.IsType<BoundStructuralExpression>(expr);
        Assert.Equal("i32", aw.TypeName);
        Assert.Equal(false, aw.Metadata["ConfigureAwait"]);
        AssertComplete(diags);

        var (voidAwait, _) = BindReturn(
            new AwaitExpressionNode(S, new ReferenceNode(S, "t")),
            ("t", "Task"));
        Assert.Equal("VOID", voidAwait.TypeName);
    }

    [Fact]
    public void BoundChildren_EnumeratesEveryB6NodeChild()
    {
        var i = new BoundIntLiteral(S, 1);
        var j = new BoundIntLiteral(S, 2);
        Assert.Equal([i, j], BoundChildren.Of(
            new BoundStructuralExpression(
                S,
                nameof(NullCoalesceNode),
                "INT",
                [i, j],
                deferredChildren: [j])));
        Assert.Equal([i], BoundChildren.Of(
            new BoundStructuralExpression(S, nameof(NullConditionalNode), "OBJECT", [i])));
        var guard = new BoundBoolLiteral(S, true);
        var match = new BoundMatchExpression(S, "m", i,
            [new BoundMatchCase(
                S,
                new BoundPattern(S, nameof(ConstantPatternNode)),
                isDefault: false,
                guard,
                [])],
            new AttributeCollection(),
            "OBJECT");
        Assert.Equal(new BoundExpression[] { i, guard }, BoundChildren.Of(match));
        var lam = new BoundLambdaExpression(
            S,
            "l",
            [],
            null,
            [],
            new AttributeCollection(),
            isAsync: false,
            isStatic: false,
            j,
            null,
            "INT");
        Assert.Equal([j], BoundChildren.Of(lam));
        Assert.Equal([i], BoundChildren.Of(
            new BoundStructuralExpression(S, nameof(AwaitExpressionNode), "INT", [i])));
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

    // ---- #908 adversarial-review pins (F1, F2, F4, F5, F3) ----

    private static MatchCaseNode Arm(PatternNode pattern, ExpressionNode? guard, params StatementNode[] body)
        => new(S, pattern, guard, body);

    [Fact]
    public void Match_ArmsHaveIndependentScopes_SameLocalNameLegal_NoOutwardLeak()
    {
        // #908 F1: at most one arm executes — two arms declaring the same local is
        // valid (statement-match parity), and arm locals must not resolve after the
        // match. Pre-fix: false Calor0201 on arm 2 and the leaked `tmp` bound cleanly.
        StatementNode DeclareTmp() => new BindStatementNode(S, "tmp", "i32",
            false, new IntLiteralNode(S, 7), new AttributeCollection());
        var match = new MatchExpressionNode(S, "mx1", new ReferenceNode(S, "x"),
            new[]
            {
                Arm(new ConstantPatternNode(S, new IntLiteralNode(S, 0)), null, DeclareTmp()),
                Arm(new WildcardPatternNode(S), null, DeclareTmp()),
            }, new AttributeCollection());
        var func = new FunctionNode(S, "f001", "Probe", Visibility.Public,
            new[] { new ParameterNode(S, "x", "i32", new AttributeCollection()) },
            new OutputNode(S, "OBJECT"), null,
            new StatementNode[]
            {
                new ExpressionStatementNode(S, match),
                new ReturnStatementNode(S, new ReferenceNode(S, "tmp")), // leak probe
            },
            new AttributeCollection());
        var module = new ModuleNode(S, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(), new[] { func }, new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(module);

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.DuplicateDefinition);
        Assert.Contains(diagnostics,
            d => d.Code == DiagnosticCode.UndefinedReference && d.Message.Contains("'tmp'"));
    }

    [Fact]
    public void Match_VariablePattern_CaptureResolvesInGuardAndBody()
    {
        // #908 F1: a variable pattern declares its capture in the arm scope (typed by
        // the scrutinee). Pre-fix: hard Calor0200 Errors on every use of the capture.
        var match = new MatchExpressionNode(S, "mx1", new IntLiteralNode(S, 42),
            new[]
            {
                Arm(new VariablePatternNode(S, "captured"),
                    new BinaryOperationNode(S, BinaryOperator.GreaterThan,
                        new ReferenceNode(S, "captured"), new IntLiteralNode(S, 0)),
                    new ReturnStatementNode(S, new ReferenceNode(S, "captured"))),
            }, new AttributeCollection());
        var (expr, diags) = BindReturn(match);
        Assert.IsType<BoundMatchExpression>(expr);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.UndefinedReference);
    }

    [Fact]
    public void Lambda_DuplicateParameterNames_ReportDuplicateDefinition()
    {
        // #908 F5: function parameters report Calor0201 on duplicates; lambda
        // parameters must match (pre-fix: silent, body resolved to the first).
        var lambda = new LambdaExpressionNode(S, "lam1",
            new[]
            {
                new LambdaParameterNode(S, "y", "i32"),
                new LambdaParameterNode(S, "y", "str"),
            }, null,
            isAsync: false, new ReferenceNode(S, "y"), null, new AttributeCollection());
        var (_, diags) = BindReturn(lambda);
        Assert.Contains(diags, d => d.Code == DiagnosticCode.DuplicateDefinition);
    }

    [Fact]
    public void Lambda_EffectsRetained()
    {
        // #908 F4: per-family contract — every AST property retained. Effects ride
        // along as AST until 0.15 effect-rows gives them checking semantics.
        var effects = new EffectsNode(S, new Dictionary<string, string> { ["cw"] = "cw" });
        var lambda = new LambdaExpressionNode(S, "lam1",
            new[] { new LambdaParameterNode(S, "y", "i32") }, effects,
            isAsync: false, new ReferenceNode(S, "y"), null, new AttributeCollection());
        var (expr, _) = BindReturn(lambda);
        var bound = Assert.IsType<BoundLambdaExpression>(expr);
        Assert.Same(effects, bound.Effects);
    }

    [Fact]
    public void Lambda_OwnParameterUse_NoUninitializedFalsePositive_EndToEnd()
    {
        // #908 F2: a lambda's own parameter is always initialized when its body runs.
        // Binding lambda bodies made their parameter uses visible to the name-keyed
        // uninitialized-variables analysis, which defaulted unknown names to
        // Uninitialized — an Error-severity Calor0900 on EVERY expression-body lambda.
        // The fix skips IsParameter symbols in use detection (function parameters are
        // seeded Initialized at entry, so this drops no true finding).
        const string source = """
            §M{m001:Test}
              §F{f001:Probe:pub} (List<i32>:items) -> i32
                §B{n:i32} §C{items.Count} §A §LAM{lam001:y:i32} (> y 0) §/LAM{lam001} §/C
                §R n
            """;

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
        });

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.UninitializedVariable);
    }

    [Fact]
    public void Match_ArmBodyDivision_NotYetVisibleToCheckers_CurrentStatePin()
    {
        // #908 F3 (current-state pin, NOT desired behavior): match-arm BODIES are
        // bound statements reachable only through BoundMatchExpression — no checker
        // traversal walks them yet (statement-match parity; consumer gap owned by
        // #786). This pin documents the blindness so closing the gap flips a test.
        var match = new MatchExpressionNode(S, "mx1", new ReferenceNode(S, "x"),
            new[]
            {
                Arm(new WildcardPatternNode(S), null,
                    new ReturnStatementNode(S,
                        new BinaryOperationNode(S, BinaryOperator.Divide,
                            new IntLiteralNode(S, 10), new IntLiteralNode(S, 0)))),
            }, new AttributeCollection());
        var (expr, diags) = BindReturn(match);
        Assert.IsType<BoundMatchExpression>(expr);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.DivisionByZero);
    }
}
