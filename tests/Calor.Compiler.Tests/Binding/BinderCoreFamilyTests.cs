using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B2: the core-9 family binds structurally — bound node type, explicit non-null
/// type string, retained children, and NO Calor0259 (each construct leaves the
/// incomplete set). Direct-AST construction (the VerifierTests pattern): several of these
/// nodes are converter-produced and have no native syntax, and binder unit tests should
/// not depend on parser routes regardless.
/// </summary>
public class BinderCoreFamilyTests
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

    private static void AssertComplete(DiagnosticBag diagnostics) =>
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AnalysisIncomplete);

    [Fact]
    public void Some_BindsWithComposedOptionType()
    {
        var (expr, diags) = BindReturn(new SomeExpressionNode(S, new IntLiteralNode(S, 42)));
        var some = Assert.IsType<BoundSomeExpression>(expr);
        Assert.Equal("Option<INT>", some.TypeName);
        Assert.IsType<BoundIntLiteral>(some.Value);
        AssertComplete(diags);
    }

    [Fact]
    public void Ok_BindsWithComposedResultType()
    {
        var (expr, diags) = BindReturn(new OkExpressionNode(S, new IntLiteralNode(S, 1)));
        var ok = Assert.IsType<BoundOkExpression>(expr);
        Assert.Equal("Result<INT, OBJECT>", ok.TypeName);
        AssertComplete(diags);
    }

    [Fact]
    public void Err_BindsWithComposedResultType()
    {
        var (expr, diags) = BindReturn(new ErrExpressionNode(S, new StringLiteralNode(S, "boom")));
        var err = Assert.IsType<BoundErrExpression>(expr);
        Assert.Equal("Result<OBJECT, STRING>", err.TypeName);
        Assert.IsType<BoundStringLiteral>(err.Error);
        AssertComplete(diags);
    }

    [Fact]
    public void ExpressionCall_BindsTargetAndArguments()
    {
        var node = new ExpressionCallNode(S,
            new ReferenceNode(S, "handler"),
            new ExpressionNode[] { new IntLiteralNode(S, 7), new StringLiteralNode(S, "x") });
        var (expr, _) = BindReturn(node);
        var call = Assert.IsType<BoundExpressionCall>(expr);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal("OBJECT", call.TypeName);
        // The DEEP payoff: a division nested in an argument is now analyzable — the
        // pre-B2 fallback erased it (the #762 evidence bullet).
        var nested = new ExpressionCallNode(S, new ReferenceNode(S, "f"),
            new ExpressionNode[] { new BinaryOperationNode(S, BinaryOperator.Divide,
                new IntLiteralNode(S, 1), new IntLiteralNode(S, 0)) });
        var (deep, _) = BindReturn(nested);
        Assert.IsType<BoundBinaryExpression>(
            Assert.IsType<BoundExpressionCall>(deep).Arguments.Single());
    }

    [Fact]
    public void AnonymousObject_BindsInitializerValues()
    {
        var node = new AnonymousObjectCreationNode(S, new[]
        {
            new ObjectInitializerAssignment("Name", new StringLiteralNode(S, "a")),
            new ObjectInitializerAssignment("Count", new IntLiteralNode(S, 3)),
        });
        var (expr, diags) = BindReturn(node);
        var anon = Assert.IsType<BoundAnonymousObjectCreation>(expr);
        Assert.Equal(new[] { "Name", "Count" }, anon.Initializers.Select(i => i.Name));
        Assert.IsType<BoundIntLiteral>(anon.Initializers[1].Value);
        AssertComplete(diags);
    }

    [Fact]
    public void RecordCreation_BindsFieldsThroughBrokenAcceptHelper()
    {
        // FieldAssignmentNode has a no-op Accept (#762 item 8) — the binder must reach
        // its Value through properties, never visitor dispatch.
        var node = new RecordCreationNode(S, "Point", new[]
        {
            new FieldAssignmentNode(S, "X", new IntLiteralNode(S, 1)),
            new FieldAssignmentNode(S, "Y", new IntLiteralNode(S, 2)),
        });
        var (expr, diags) = BindReturn(node);
        var rec = Assert.IsType<BoundRecordCreation>(expr);
        Assert.Equal("Point", rec.TypeName);
        Assert.Equal(2, rec.Fields.Count);
        AssertComplete(diags);
    }

    [Fact]
    public void With_BindsTargetAndAssignments_KeepsTargetType()
    {
        var node = new WithExpressionNode(S,
            new SomeExpressionNode(S, new IntLiteralNode(S, 5)),
            new[] { new WithPropertyAssignmentNode(S, "Value", new IntLiteralNode(S, 9)) });
        var (expr, diags) = BindReturn(node);
        var with = Assert.IsType<BoundWithExpression>(expr);
        Assert.Equal("Option<INT>", with.TypeName);
        Assert.Single(with.Assignments);
        AssertComplete(diags);
    }

    [Fact]
    public void Throw_BindsExceptionWithNeverType()
    {
        var (expr, diags) = BindReturn(
            new ThrowExpressionNode(S, new StringLiteralNode(S, "bad")));
        var thrown = Assert.IsType<BoundThrowExpression>(expr);
        Assert.Equal("NEVER", thrown.TypeName);
        AssertComplete(diags);
    }

    [Fact]
    public void NestedDivision_InsideBoundFamilyNode_ProducesRealFinding_EndToEnd()
    {
        // The review-of-B2 CRITICAL: "analyzable again" was false — checkers' closed
        // traversal switches skipped the new node types' children (a nested /0 produced
        // NO finding through the full pipeline). The BoundChildren default arms fix it;
        // this pins the claim END TO END: a division by literal zero inside a Some
        // payload must produce the actual Calor0920, not merely a bound subtree.
        const string source = @"
§M{m001:Test}
  §F{f001:Trap:pub} () -> void
    §E{cw}
    §B{b} §SM (/ 10 0)
    §P b";

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
            d => d.Code == DiagnosticCode.DivisionByZero ||
                 (d.Code?.StartsWith("Calor0920") ?? false) ||
                 d.Message.Contains("Division by"));
    }

    [Fact]
    public void BoundChildren_EnumeratesEveryB2NodeChild()
    {
        // Locks the shared enumeration the traversal default-arms depend on: every
        // B2 node yields exactly its expression children.
        var i = new BoundIntLiteral(S, 1);
        var j = new BoundIntLiteral(S, 2);
        Assert.Equal([i], BoundChildren.Of(new BoundSomeExpression(S, i)));
        Assert.Equal([i], BoundChildren.Of(new BoundOkExpression(S, i)));
        Assert.Equal([i], BoundChildren.Of(new BoundErrExpression(S, i)));
        Assert.Equal([i, j], BoundChildren.Of(new BoundExpressionCall(S, i, [j])));
        Assert.Equal([i], BoundChildren.Of(
            new BoundAnonymousObjectCreation(S, [new BoundNamedValue("a", i, S)])));
        Assert.Equal([i], BoundChildren.Of(
            new BoundRecordCreation(S, "R", [new BoundNamedValue("x", i, S)])));
        Assert.Equal([i, j], BoundChildren.Of(
            new BoundWithExpression(S, i, [new BoundNamedValue("x", j, S)])));
        Assert.Equal([i], BoundChildren.Of(new BoundThrowExpression(S, i)));
        Assert.Empty(BoundChildren.Of(i));
    }

    [Fact]
    public void SelfRef_StaysExplicitlyIncomplete_WithDormancyReason()
    {
        // F-1 dormant rule: no legal program reaches the binder with a SelfRefNode, so a
        // binder for it would be vacuous. Pinned as EXPLICIT incompleteness with the
        // registered reason — not silently absent.
        var (expr, diags) = BindReturn(new SelfRefNode(S));
        var inc = Assert.IsType<BoundIncompleteExpression>(expr);
        Assert.Contains("dormant", inc.Reason);
        Assert.Contains(diags, d => d.Code == DiagnosticCode.AnalysisIncomplete);
    }
}
