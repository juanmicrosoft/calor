using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B7: the quantifier family (3 classes). Quantifiers are SPEC expressions — the
/// CONTRACT verification pipeline (Z3 over §Q/§S/§IV) consumes their AST
/// (ExpressionSimplifier), never these bound nodes; contracts are never bound at all
/// (BindFunction binds parameters and body statements only), so a contract quantifier
/// cannot produce different diagnostics post-B7. The Verification.Tests suite is that
/// contract's baseline. Scope note (#910 review): the bug-pattern Z3 path DOES consume
/// bound quantifier bodies — that is the point of binding them (the division pin below
/// proves it) — and §PROOF is a bound spec position, so quantifiers there now surface
/// real scope diagnostics on the LSP live bag. Binding gives value-safety analyses
/// visibility into bodies, with quantifier variables declared as parameter-like
/// symbols in a child scope.
/// </summary>
public class BinderQuantifierFamilyTests
{
    private static readonly TextSpan S = new(0, 0, 1, 1);

    private static (BoundExpression Expr, DiagnosticBag Diagnostics) BindReturn(ExpressionNode expr)
    {
        var func = new FunctionNode(S, "f001", "Probe", Visibility.Public,
            Array.Empty<ParameterNode>(), new OutputNode(S, "BOOL"), null,
            new StatementNode[] { new ReturnStatementNode(S, expr) },
            new AttributeCollection());
        var module = new ModuleNode(S, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(), new[] { func }, new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        var ret = bound.Functions.Single().Body.OfType<BoundReturnStatement>().Single();
        return (ret.Expression!, diagnostics);
    }

    private static ForallExpressionNode Forall(string var, string type, ExpressionNode body)
        => new(S, new[] { new QuantifierVariableNode(S, var, type) }, body);

    private static ExpressionNode GreaterThanZero(string var)
        => new BinaryOperationNode(S, BinaryOperator.GreaterThan,
            new ReferenceNode(S, var), new IntLiteralNode(S, 0));

    [Fact]
    public void Forall_BodyBindsInChildScope_TypeBool_BodyVisibleAndDeferred()
    {
        var (expr, diags) = BindReturn(Forall("i", "i32", GreaterThanZero("i")));
        var fa = Assert.IsType<BoundQuantifierExpression>(expr);
        Assert.Equal(nameof(ForallExpressionNode), fa.NodeTypeName);
        Assert.Equal("BOOL", fa.TypeName);
        Assert.Single(fa.BoundVariables);
        Assert.Equal("i", fa.BoundVariables[0].Name);
        Assert.Equal("i32", fa.BoundVariables[0].TypeName);
        Assert.True(fa.BoundVariables[0].IsParameter); // bound BY the quantifier
        Assert.Equal([fa.Body], BoundChildren.Of(fa));
        Assert.Equal([fa.Body], BoundChildren.DeferredOf(fa)); // empty domain → 0 evals
        // `i` resolved inside the body:
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.UndefinedReference);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.AnalysisIncomplete);
    }

    [Fact]
    public void Exists_SameContractAsForall()
    {
        var node = new ExistsExpressionNode(S,
            new[] { new QuantifierVariableNode(S, "j", "i32") }, GreaterThanZero("j"));
        var (expr, diags) = BindReturn(node);
        var ex = Assert.IsType<BoundQuantifierExpression>(expr);
        Assert.Equal(nameof(ExistsExpressionNode), ex.NodeTypeName);
        Assert.Equal("BOOL", ex.TypeName);
        Assert.Equal([ex.Body], BoundChildren.Of(ex));
        Assert.Equal([ex.Body], BoundChildren.DeferredOf(ex));
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.UndefinedReference);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.AnalysisIncomplete);
    }

    [Fact]
    public void QuantifierVariable_DoesNotLeakToEnclosingScope()
    {
        // Same leak-probe shape as the B6 lambda test: `i` after the quantifier is an
        // UndefinedReference; `i` inside was not.
        var func = new FunctionNode(S, "f001", "Probe", Visibility.Public,
            Array.Empty<ParameterNode>(), new OutputNode(S, "BOOL"), null,
            new StatementNode[]
            {
                new ExpressionStatementNode(S, Forall("i", "i32", GreaterThanZero("i"))),
                new ReturnStatementNode(S, new ReferenceNode(S, "i")), // leak probe
            },
            new AttributeCollection());
        var module = new ModuleNode(S, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(), new[] { func }, new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(module);
        Assert.Contains(diagnostics,
            d => d.Code == DiagnosticCode.UndefinedReference && d.Message.Contains("'i'"));
    }

    [Fact]
    public void Quantifier_DuplicateBoundVariableNames_ReportDuplicateDefinition()
    {
        var node = new ForallExpressionNode(S,
            new[]
            {
                new QuantifierVariableNode(S, "i", "i32"),
                new QuantifierVariableNode(S, "i", "i64"),
            }, GreaterThanZero("i"));
        var (_, diags) = BindReturn(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCode.DuplicateDefinition);
    }

    [Fact]
    public void Implication_BothChildrenVisible_ConsequentDeferred_TypeBool()
    {
        var node = new ImplicationExpressionNode(S,
            new BoolLiteralNode(S, true), new BoolLiteralNode(S, false));
        var (expr, diags) = BindReturn(node);
        var imp = Assert.IsType<BoundStructuralExpression>(expr);
        Assert.Equal("BOOL", imp.TypeName);
        Assert.Equal(2, imp.Children.Count);
        Assert.Equal([imp.Children[1]], BoundChildren.DeferredOf(imp)); // !a || b short-circuits
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.AnalysisIncomplete);
    }

    [Fact]
    public void QuantifierVariableUse_NoUninitializedOrUndefinedFalsePositive_EndToEnd()
    {
        // Parser-surface e2e: the quantifier variable is a declared, parameter-like
        // symbol — no Calor0200 (undefined), no Calor0900 (uninitialized), and no
        // Calor0259 (the family now binds).
        // Routing caveat (#910 review): Program.Compile routes ONLY Calor0259 from the
        // binder's bag, so the 0200 assertion here cannot fail via binder scoping —
        // the real scope guards are the live-bag unit tests above.
        const string source = """
            §M{m001:Test}
              §F{f001:Probe:pub} () -> bool
                §R (forall ((i i32)) (> i 0))
            """;

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
        });

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UninitializedVariable);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.AnalysisIncomplete);
    }

    [Fact]
    public void DivisionInQuantifierBody_ProducesRealFinding_EndToEnd()
    {
        // A /0 inside a forall body is a bug in the SPEC — Of() exposes it and the
        // checker fires at the division's span (line 3). Same options discipline as
        // the B6 pin: ReportOnlyVerified set explicitly (CLI default true, API false).
        // This is the bug-pattern Z3 path consuming a BOUND quantifier body — the one
        // Z3-involving verdict B7 deliberately changes (contract Z3 is untouched).
        const string source = """
            §M{m001:Test}
              §F{f001:Trap:pub} (i32:x) -> bool
                §R (forall ((i i32)) (> (/ 10 0) i))
            """;

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
            d => d.Code == DiagnosticCode.DivisionByZero && d.Span.Line == 3);
    }

    [Fact]
    public void Exists_ParsedSource_BindsClean_EndToEnd()
    {
        // #910 review: parser→binder reachability pinned for all three spellings, not
        // just forall — `(exists ((v T)) body)` from real source.
        const string source = """
            §M{m001:Test}
              §F{f001:Probe:pub} (i32:x) -> bool
                §R (exists ((j i32)) (> j x))
            """;

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
        });

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.AnalysisIncomplete);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void Implication_ParsedSource_BindsClean_EndToEnd()
    {
        // `(-> a b)` is the ONLY implication spelling (==>/implies are parse errors);
        // `->` in Lisp operator position cannot collide with the return arrow.
        const string source = """
            §M{m001:Test}
              §F{f001:Probe:pub} (i32:x) -> bool
                §R (-> (> x 0) (> x -1))
            """;

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
        });

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.AnalysisIncomplete);
        Assert.False(result.Diagnostics.HasErrors);
    }
}
