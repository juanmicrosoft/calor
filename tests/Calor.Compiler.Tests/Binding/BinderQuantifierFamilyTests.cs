using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B7: the quantifier family (3 classes). Quantifiers are SPEC expressions — the
/// Z3 verification pipeline consumes their AST (ExpressionSimplifier), never these
/// bound nodes ("no verification-pipeline interaction" is the family's checker
/// contract; the Verification.Tests suite is its baseline). Binding gives value-safety
/// analyses visibility into bodies, with quantifier variables declared as
/// parameter-like symbols in a child scope.
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
        var fa = Assert.IsType<BoundForallExpression>(expr);
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
        var ex = Assert.IsType<BoundExistsExpression>(expr);
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
        var imp = Assert.IsType<BoundImplicationExpression>(expr);
        Assert.Equal("BOOL", imp.TypeName);
        Assert.Equal([imp.Antecedent, imp.Consequent], BoundChildren.Of(imp));
        Assert.Equal([imp.Consequent], BoundChildren.DeferredOf(imp)); // !a || b short-circuits
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.AnalysisIncomplete);
    }

    [Fact]
    public void QuantifierVariableUse_NoUninitializedOrUndefinedFalsePositive_EndToEnd()
    {
        // Parser-surface e2e: the quantifier variable is a declared, parameter-like
        // symbol — no Calor0200 (undefined), no Calor0900 (uninitialized), and no
        // Calor0259 (the family now binds).
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
        // checker fires at the division's span (line 4). Same options discipline as
        // the B6 pin: ReportOnlyVerified set explicitly (CLI default true, API false).
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
}
