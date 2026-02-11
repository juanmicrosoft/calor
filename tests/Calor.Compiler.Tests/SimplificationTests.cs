using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for contract simplification pass.
/// </summary>
public class SimplificationTests
{
    private static TextSpan Span => new(0, 0, 1, 1);

    #region Constant Folding Tests

    [Fact]
    public void ConstantFolding_Addition()
    {
        // (+ 1 2) → 3
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1),
            new IntLiteralNode(Span, 2));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(3, lit.Value);
    }

    [Fact]
    public void ConstantFolding_Subtraction()
    {
        // (- 5 3) → 2
        var expr = new BinaryOperationNode(Span, BinaryOperator.Subtract,
            new IntLiteralNode(Span, 5),
            new IntLiteralNode(Span, 3));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(2, lit.Value);
    }

    [Fact]
    public void ConstantFolding_Multiplication()
    {
        // (* 5 0) → 0
        var expr = new BinaryOperationNode(Span, BinaryOperator.Multiply,
            new IntLiteralNode(Span, 5),
            new IntLiteralNode(Span, 0));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(0, lit.Value);
    }

    [Fact]
    public void ConstantFolding_Division()
    {
        // (/ 10 2) → 5
        var expr = new BinaryOperationNode(Span, BinaryOperator.Divide,
            new IntLiteralNode(Span, 10),
            new IntLiteralNode(Span, 2));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(5, lit.Value);
    }

    [Fact]
    public void ConstantFolding_LessThan_True()
    {
        // (< 1 2) → true
        var expr = new BinaryOperationNode(Span, BinaryOperator.LessThan,
            new IntLiteralNode(Span, 1),
            new IntLiteralNode(Span, 2));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void ConstantFolding_LessThan_False()
    {
        // (< 5 2) → false
        var expr = new BinaryOperationNode(Span, BinaryOperator.LessThan,
            new IntLiteralNode(Span, 5),
            new IntLiteralNode(Span, 2));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.False(lit.Value);
    }

    [Fact]
    public void ConstantFolding_Equal()
    {
        // (== 5 5) → true
        var expr = new BinaryOperationNode(Span, BinaryOperator.Equal,
            new IntLiteralNode(Span, 5),
            new IntLiteralNode(Span, 5));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void ConstantFolding_NotNegation()
    {
        // (- INT:5) → INT:-5
        var expr = new UnaryOperationNode(Span, UnaryOperator.Negate,
            new IntLiteralNode(Span, 5));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(-5, lit.Value);
    }

    #endregion

    #region Boolean Identity Tests

    [Fact]
    public void BooleanIdentity_AndTrue_Left()
    {
        // (&& true x) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.And,
            new BoolLiteralNode(Span, true), x);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void BooleanIdentity_AndTrue_Right()
    {
        // (&& x true) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.And,
            x, new BoolLiteralNode(Span, true));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void BooleanIdentity_AndFalse_Left()
    {
        // (&& false x) → false
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.And,
            new BoolLiteralNode(Span, false), x);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.False(lit.Value);
    }

    [Fact]
    public void BooleanIdentity_AndFalse_Right()
    {
        // (&& x false) → false
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.And,
            x, new BoolLiteralNode(Span, false));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.False(lit.Value);
    }

    [Fact]
    public void BooleanIdentity_OrTrue_Left()
    {
        // (|| true x) → true
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Or,
            new BoolLiteralNode(Span, true), x);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void BooleanIdentity_OrTrue_Right()
    {
        // (|| x true) → true
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Or,
            x, new BoolLiteralNode(Span, true));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void BooleanIdentity_OrFalse_Left()
    {
        // (|| false x) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Or,
            new BoolLiteralNode(Span, false), x);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void BooleanIdentity_OrFalse_Right()
    {
        // (|| x false) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Or,
            x, new BoolLiteralNode(Span, false));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    #endregion

    #region Double Negation Tests

    [Fact]
    public void DoubleNegation_Removed()
    {
        // (! (! x)) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new UnaryOperationNode(Span, UnaryOperator.Not,
            new UnaryOperationNode(Span, UnaryOperator.Not, x));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void NotTrue_ToFalse()
    {
        // (! true) → false
        var expr = new UnaryOperationNode(Span, UnaryOperator.Not,
            new BoolLiteralNode(Span, true));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.False(lit.Value);
    }

    [Fact]
    public void NotFalse_ToTrue()
    {
        // (! false) → true
        var expr = new UnaryOperationNode(Span, UnaryOperator.Not,
            new BoolLiteralNode(Span, false));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    #endregion

    #region Tautology Detection Tests

    [Fact]
    public void Tautology_OrNotSelf()
    {
        // (|| x (! x)) → true
        var x = new ReferenceNode(Span, "x");
        var notX = new UnaryOperationNode(Span, UnaryOperator.Not, x);
        var expr = new BinaryOperationNode(Span, BinaryOperator.Or, x, notX);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Tautology_OrNotSelf_Reversed()
    {
        // (|| (! x) x) → true
        var x = new ReferenceNode(Span, "x");
        var notX = new UnaryOperationNode(Span, UnaryOperator.Not, x);
        var expr = new BinaryOperationNode(Span, BinaryOperator.Or, notX, x);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Tautology_ImplicationReflexive()
    {
        // (-> p p) → true
        var p = new ReferenceNode(Span, "p");
        var expr = new ImplicationExpressionNode(Span, p, p);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Tautology_ImplicationFalseAntecedent()
    {
        // (-> false p) → true
        var p = new ReferenceNode(Span, "p");
        var expr = new ImplicationExpressionNode(Span, new BoolLiteralNode(Span, false), p);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Tautology_ImplicationTrueConsequent()
    {
        // (-> p true) → true
        var p = new ReferenceNode(Span, "p");
        var expr = new ImplicationExpressionNode(Span, p, new BoolLiteralNode(Span, true));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    #endregion

    #region Contradiction Detection Tests

    [Fact]
    public void Contradiction_AndNotSelf()
    {
        // (&& x (! x)) → false
        var x = new ReferenceNode(Span, "x");
        var notX = new UnaryOperationNode(Span, UnaryOperator.Not, x);
        var expr = new BinaryOperationNode(Span, BinaryOperator.And, x, notX);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.False(lit.Value);
    }

    [Fact]
    public void Contradiction_AndNotSelf_Reversed()
    {
        // (&& (! x) x) → false
        var x = new ReferenceNode(Span, "x");
        var notX = new UnaryOperationNode(Span, UnaryOperator.Not, x);
        var expr = new BinaryOperationNode(Span, BinaryOperator.And, notX, x);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.False(lit.Value);
    }

    #endregion

    #region Redundant Expression Elimination Tests

    [Fact]
    public void Redundant_AndSelf()
    {
        // (&& x x) → x
        var x = new ReferenceNode(Span, "x");
        var xCopy = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.And, x, xCopy);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Redundant_OrSelf()
    {
        // (|| x x) → x
        var x = new ReferenceNode(Span, "x");
        var xCopy = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Or, x, xCopy);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Redundant_EqualSelf()
    {
        // (== x x) → true
        var x = new ReferenceNode(Span, "x");
        var xCopy = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Equal, x, xCopy);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Redundant_NotEqualSelf()
    {
        // (!= x x) → false
        var x = new ReferenceNode(Span, "x");
        var xCopy = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.NotEqual, x, xCopy);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.False(lit.Value);
    }

    #endregion

    #region Implication Simplification Tests

    [Fact]
    public void Implication_TrueAntecedent()
    {
        // (-> true p) → p
        var p = new ReferenceNode(Span, "p");
        var expr = new ImplicationExpressionNode(Span, new BoolLiteralNode(Span, true), p);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("p", refNode.Name);
    }

    [Fact]
    public void Implication_FalseConsequent()
    {
        // (-> p false) → (! p)
        var p = new ReferenceNode(Span, "p");
        var expr = new ImplicationExpressionNode(Span, p, new BoolLiteralNode(Span, false));

        var result = Simplify(expr);

        var notExpr = Assert.IsType<UnaryOperationNode>(result);
        Assert.Equal(UnaryOperator.Not, notExpr.Operator);
        var refNode = Assert.IsType<ReferenceNode>(notExpr.Operand);
        Assert.Equal("p", refNode.Name);
    }

    #endregion

    #region Quantifier Simplification Tests

    [Fact]
    public void Forall_TrueBody()
    {
        // (forall (...) true) → true
        var boundVar = new QuantifierVariableNode(Span, "i", "i32");
        var expr = new ForallExpressionNode(Span, new[] { boundVar },
            new BoolLiteralNode(Span, true));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Forall_FalseBody()
    {
        // (forall (...) false) → false
        var boundVar = new QuantifierVariableNode(Span, "i", "i32");
        var expr = new ForallExpressionNode(Span, new[] { boundVar },
            new BoolLiteralNode(Span, false));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.False(lit.Value);
    }

    [Fact]
    public void Exists_TrueBody()
    {
        // (exists (...) true) → true
        var boundVar = new QuantifierVariableNode(Span, "i", "i32");
        var expr = new ExistsExpressionNode(Span, new[] { boundVar },
            new BoolLiteralNode(Span, true));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Exists_FalseBody()
    {
        // (exists (...) false) → false
        var boundVar = new QuantifierVariableNode(Span, "i", "i32");
        var expr = new ExistsExpressionNode(Span, new[] { boundVar },
            new BoolLiteralNode(Span, false));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.False(lit.Value);
    }

    #endregion

    #region Fixed-Point Iteration Tests

    [Fact]
    public void FixedPoint_NestedSimplification()
    {
        // (&& true (&& true x)) → x (2 iterations)
        var x = new ReferenceNode(Span, "x");
        var inner = new BinaryOperationNode(Span, BinaryOperator.And,
            new BoolLiteralNode(Span, true), x);
        var outer = new BinaryOperationNode(Span, BinaryOperator.And,
            new BoolLiteralNode(Span, true), inner);

        var result = Simplify(outer);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void FixedPoint_TripleNegation()
    {
        // (! (! (! x))) → (! x)
        var x = new ReferenceNode(Span, "x");
        var notX = new UnaryOperationNode(Span, UnaryOperator.Not, x);
        var notNotX = new UnaryOperationNode(Span, UnaryOperator.Not, notX);
        var notNotNotX = new UnaryOperationNode(Span, UnaryOperator.Not, notNotX);

        var result = Simplify(notNotNotX);

        var unary = Assert.IsType<UnaryOperationNode>(result);
        Assert.Equal(UnaryOperator.Not, unary.Operator);
        var refNode = Assert.IsType<ReferenceNode>(unary.Operand);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void FixedPoint_NestedConstantFolding()
    {
        // (+ (+ 1 2) (+ 3 4)) → 10
        var add12 = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var add34 = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 3), new IntLiteralNode(Span, 4));
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add, add12, add34);

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(10, lit.Value);
    }

    #endregion

    #region Conditional Expression Tests

    [Fact]
    public void Conditional_TrueCondition()
    {
        // (? true t f) → t
        var t = new ReferenceNode(Span, "t");
        var f = new ReferenceNode(Span, "f");
        var expr = new ConditionalExpressionNode(Span,
            new BoolLiteralNode(Span, true), t, f);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("t", refNode.Name);
    }

    [Fact]
    public void Conditional_FalseCondition()
    {
        // (? false t f) → f
        var t = new ReferenceNode(Span, "t");
        var f = new ReferenceNode(Span, "f");
        var expr = new ConditionalExpressionNode(Span,
            new BoolLiteralNode(Span, false), t, f);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("f", refNode.Name);
    }

    [Fact]
    public void Conditional_SameBranches()
    {
        // (? c x x) → x
        var c = new ReferenceNode(Span, "c");
        var x1 = new ReferenceNode(Span, "x");
        var x2 = new ReferenceNode(Span, "x");
        var expr = new ConditionalExpressionNode(Span, c, x1, x2);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    #endregion

    #region Diagnostic Tests

    [Fact]
    public void Diagnostics_TautologyReported()
    {
        var diagnostics = new DiagnosticBag();
        var simplifier = new ExpressionSimplifier(diagnostics);

        // (-> p p) → true (tautology)
        var p = new ReferenceNode(Span, "p");
        var expr = new ImplicationExpressionNode(Span, p, p);

        _ = simplifier.Simplify(expr);

        var infos = diagnostics.Where(d => d.Code == DiagnosticCode.ContractTautology).ToList();
        Assert.Single(infos);
    }

    [Fact]
    public void Diagnostics_ContradictionReported()
    {
        var diagnostics = new DiagnosticBag();
        var simplifier = new ExpressionSimplifier(diagnostics);

        // (&& x (! x)) → false (contradiction)
        var x = new ReferenceNode(Span, "x");
        var notX = new UnaryOperationNode(Span, UnaryOperator.Not, x);
        var expr = new BinaryOperationNode(Span, BinaryOperator.And, x, notX);

        _ = simplifier.Simplify(expr);

        var warnings = diagnostics.Where(d => d.Code == DiagnosticCode.ContractContradiction).ToList();
        Assert.Single(warnings);
    }

    [Fact]
    public void Diagnostics_SimplificationReported()
    {
        var diagnostics = new DiagnosticBag();
        var simplifier = new ExpressionSimplifier(diagnostics);

        // (+ 1 2) → 3 (simplification)
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));

        _ = simplifier.Simplify(expr);

        var infos = diagnostics.Where(d => d.Code == DiagnosticCode.ContractSimplified).ToList();
        Assert.Single(infos);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Integration_ParseAndSimplify()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:x}
  §O{bool}
  §Q (&& true (>= x INT:0))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var simplificationPass = new ContractSimplificationPass(diagnostics);
        var simplified = simplificationPass.Simplify(module);

        var func = simplified.Functions[0];
        Assert.Single(func.Preconditions);

        // The precondition (&& true (>= x 0)) should simplify to (>= x 0)
        var condition = func.Preconditions[0].Condition;
        Assert.IsType<BinaryOperationNode>(condition);
        var binOp = (BinaryOperationNode)condition;
        Assert.Equal(BinaryOperator.GreaterOrEqual, binOp.Operator);
    }

    [Fact]
    public void Integration_SimplifyTautology()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:x}
  §O{bool}
  §Q (-> (>= x INT:0) (>= x INT:0))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var simplificationPass = new ContractSimplificationPass(diagnostics);
        var simplified = simplificationPass.Simplify(module);

        var func = simplified.Functions[0];
        Assert.Single(func.Preconditions);

        // The precondition (-> p p) should simplify to true
        var condition = func.Preconditions[0].Condition;
        var lit = Assert.IsType<BoolLiteralNode>(condition);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Integration_NoSimplificationNeeded()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:x}
  §O{bool}
  §Q (>= x INT:0)
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var simplificationPass = new ContractSimplificationPass(diagnostics);
        var simplified = simplificationPass.Simplify(module);

        // When no simplification is needed, should return the same module instance
        Assert.Same(module, simplified);
    }

    [Fact]
    public void Integration_QuantifierBodySimplification()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((i i32)) true)
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var simplificationPass = new ContractSimplificationPass(diagnostics);
        var simplified = simplificationPass.Simplify(module);

        var func = simplified.Functions[0];
        Assert.Single(func.Preconditions);

        // (forall (...) true) should simplify to true
        var condition = func.Preconditions[0].Condition;
        var lit = Assert.IsType<BoolLiteralNode>(condition);
        Assert.True(lit.Value);
    }

    #endregion

    #region Float Constant Folding Tests

    [Fact]
    public void FloatConstantFolding_Addition()
    {
        // (+ 1.5 2.5) → 4.0
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            new FloatLiteralNode(Span, 1.5),
            new FloatLiteralNode(Span, 2.5));

        var result = Simplify(expr);

        var lit = Assert.IsType<FloatLiteralNode>(result);
        Assert.Equal(4.0, lit.Value, precision: 10);
    }

    [Fact]
    public void FloatConstantFolding_Subtraction()
    {
        // (- 5.5 2.5) → 3.0
        var expr = new BinaryOperationNode(Span, BinaryOperator.Subtract,
            new FloatLiteralNode(Span, 5.5),
            new FloatLiteralNode(Span, 2.5));

        var result = Simplify(expr);

        var lit = Assert.IsType<FloatLiteralNode>(result);
        Assert.Equal(3.0, lit.Value, precision: 10);
    }

    [Fact]
    public void FloatConstantFolding_Multiplication()
    {
        // (* 2.5 4.0) → 10.0
        var expr = new BinaryOperationNode(Span, BinaryOperator.Multiply,
            new FloatLiteralNode(Span, 2.5),
            new FloatLiteralNode(Span, 4.0));

        var result = Simplify(expr);

        var lit = Assert.IsType<FloatLiteralNode>(result);
        Assert.Equal(10.0, lit.Value, precision: 10);
    }

    [Fact]
    public void FloatConstantFolding_Division()
    {
        // (/ 10.0 4.0) → 2.5
        var expr = new BinaryOperationNode(Span, BinaryOperator.Divide,
            new FloatLiteralNode(Span, 10.0),
            new FloatLiteralNode(Span, 4.0));

        var result = Simplify(expr);

        var lit = Assert.IsType<FloatLiteralNode>(result);
        Assert.Equal(2.5, lit.Value, precision: 10);
    }

    [Fact]
    public void FloatConstantFolding_LessThan()
    {
        // (< 1.5 2.5) → true
        var expr = new BinaryOperationNode(Span, BinaryOperator.LessThan,
            new FloatLiteralNode(Span, 1.5),
            new FloatLiteralNode(Span, 2.5));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void FloatConstantFolding_Negation()
    {
        // (- 3.5) → -3.5
        var expr = new UnaryOperationNode(Span, UnaryOperator.Negate,
            new FloatLiteralNode(Span, 3.5));

        var result = Simplify(expr);

        var lit = Assert.IsType<FloatLiteralNode>(result);
        Assert.Equal(-3.5, lit.Value, precision: 10);
    }

    #endregion

    #region Mixed Int/Float Constant Folding Tests

    [Fact]
    public void MixedConstantFolding_IntPlusFloat()
    {
        // (+ 1 2.5) → 3.5
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1),
            new FloatLiteralNode(Span, 2.5));

        var result = Simplify(expr);

        var lit = Assert.IsType<FloatLiteralNode>(result);
        Assert.Equal(3.5, lit.Value, precision: 10);
    }

    [Fact]
    public void MixedConstantFolding_FloatPlusInt()
    {
        // (+ 2.5 1) → 3.5
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            new FloatLiteralNode(Span, 2.5),
            new IntLiteralNode(Span, 1));

        var result = Simplify(expr);

        var lit = Assert.IsType<FloatLiteralNode>(result);
        Assert.Equal(3.5, lit.Value, precision: 10);
    }

    [Fact]
    public void MixedConstantFolding_IntTimesFloat()
    {
        // (* 2 3.5) → 7.0
        var expr = new BinaryOperationNode(Span, BinaryOperator.Multiply,
            new IntLiteralNode(Span, 2),
            new FloatLiteralNode(Span, 3.5));

        var result = Simplify(expr);

        var lit = Assert.IsType<FloatLiteralNode>(result);
        Assert.Equal(7.0, lit.Value, precision: 10);
    }

    #endregion

    #region Algebraic Identity Tests

    [Fact]
    public void AlgebraicIdentity_AddZeroRight()
    {
        // (+ x 0) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            x, new IntLiteralNode(Span, 0));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void AlgebraicIdentity_AddZeroLeft()
    {
        // (+ 0 x) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 0), x);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void AlgebraicIdentity_SubtractZero()
    {
        // (- x 0) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Subtract,
            x, new IntLiteralNode(Span, 0));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void AlgebraicIdentity_SubtractSelf()
    {
        // (- x x) → 0
        var x1 = new ReferenceNode(Span, "x");
        var x2 = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Subtract, x1, x2);

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(0, lit.Value);
    }

    [Fact]
    public void AlgebraicIdentity_MultiplyOneRight()
    {
        // (* x 1) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Multiply,
            x, new IntLiteralNode(Span, 1));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void AlgebraicIdentity_MultiplyOneLeft()
    {
        // (* 1 x) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Multiply,
            new IntLiteralNode(Span, 1), x);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void AlgebraicIdentity_MultiplyZeroRight()
    {
        // (* x 0) → 0
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Multiply,
            x, new IntLiteralNode(Span, 0));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(0, lit.Value);
    }

    [Fact]
    public void AlgebraicIdentity_MultiplyZeroLeft()
    {
        // (* 0 x) → 0
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Multiply,
            new IntLiteralNode(Span, 0), x);

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(0, lit.Value);
    }

    [Fact]
    public void AlgebraicIdentity_DivideByOne()
    {
        // (/ x 1) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Divide,
            x, new IntLiteralNode(Span, 1));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void AlgebraicIdentity_ModuloByOne()
    {
        // (% x 1) → 0 (for integer x)
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Modulo,
            x, new IntLiteralNode(Span, 1));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(0, lit.Value);
    }

    [Fact]
    public void AlgebraicIdentity_DivideSameConstants()
    {
        // (/ 5 5) → 1
        var expr = new BinaryOperationNode(Span, BinaryOperator.Divide,
            new IntLiteralNode(Span, 5),
            new IntLiteralNode(Span, 5));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(1, lit.Value);
    }

    [Fact]
    public void AlgebraicIdentity_AddZeroFloat()
    {
        // (+ x 0.0) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            x, new FloatLiteralNode(Span, 0.0));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void AlgebraicIdentity_MultiplyOneFloat()
    {
        // (* x 1.0) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Multiply,
            x, new FloatLiteralNode(Span, 1.0));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    #endregion

    #region De Morgan's Law Tests

    [Fact]
    public void DeMorgan_NotAnd()
    {
        // (! (&& a b)) → (|| (! a) (! b))
        var a = new ReferenceNode(Span, "a");
        var b = new ReferenceNode(Span, "b");
        var andExpr = new BinaryOperationNode(Span, BinaryOperator.And, a, b);
        var expr = new UnaryOperationNode(Span, UnaryOperator.Not, andExpr);

        var result = Simplify(expr);

        var orExpr = Assert.IsType<BinaryOperationNode>(result);
        Assert.Equal(BinaryOperator.Or, orExpr.Operator);

        var notA = Assert.IsType<UnaryOperationNode>(orExpr.Left);
        Assert.Equal(UnaryOperator.Not, notA.Operator);
        var refA = Assert.IsType<ReferenceNode>(notA.Operand);
        Assert.Equal("a", refA.Name);

        var notB = Assert.IsType<UnaryOperationNode>(orExpr.Right);
        Assert.Equal(UnaryOperator.Not, notB.Operator);
        var refB = Assert.IsType<ReferenceNode>(notB.Operand);
        Assert.Equal("b", refB.Name);
    }

    [Fact]
    public void DeMorgan_NotOr()
    {
        // (! (|| a b)) → (&& (! a) (! b))
        var a = new ReferenceNode(Span, "a");
        var b = new ReferenceNode(Span, "b");
        var orExpr = new BinaryOperationNode(Span, BinaryOperator.Or, a, b);
        var expr = new UnaryOperationNode(Span, UnaryOperator.Not, orExpr);

        var result = Simplify(expr);

        var andExpr = Assert.IsType<BinaryOperationNode>(result);
        Assert.Equal(BinaryOperator.And, andExpr.Operator);

        var notA = Assert.IsType<UnaryOperationNode>(andExpr.Left);
        Assert.Equal(UnaryOperator.Not, notA.Operator);
        var refA = Assert.IsType<ReferenceNode>(notA.Operand);
        Assert.Equal("a", refA.Name);

        var notB = Assert.IsType<UnaryOperationNode>(andExpr.Right);
        Assert.Equal(UnaryOperator.Not, notB.Operator);
        var refB = Assert.IsType<ReferenceNode>(notB.Operand);
        Assert.Equal("b", refB.Name);
    }

    [Fact]
    public void DeMorgan_WithConstant_SimplifiesToTruth()
    {
        // (! (&& true false)) → (|| false true) → true
        var andExpr = new BinaryOperationNode(Span, BinaryOperator.And,
            new BoolLiteralNode(Span, true),
            new BoolLiteralNode(Span, false));
        var expr = new UnaryOperationNode(Span, UnaryOperator.Not, andExpr);

        var result = Simplify(expr);

        // After De Morgan and simplification: (|| (! true) (! false)) → (|| false true) → true
        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    #endregion

    #region Bitwise Operation Tests

    [Fact]
    public void Bitwise_NotConstant()
    {
        // (~ 5) → -6 (in two's complement)
        var expr = new UnaryOperationNode(Span, UnaryOperator.BitwiseNot,
            new IntLiteralNode(Span, 5));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(~5, lit.Value);
    }

    [Fact]
    public void Bitwise_DoubleNot()
    {
        // (~ (~ x)) → x
        var x = new ReferenceNode(Span, "x");
        var inner = new UnaryOperationNode(Span, UnaryOperator.BitwiseNot, x);
        var expr = new UnaryOperationNode(Span, UnaryOperator.BitwiseNot, inner);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Bitwise_AndConstants()
    {
        // (& 0b1010 0b1100) → 0b1000 (8)
        var expr = new BinaryOperationNode(Span, BinaryOperator.BitwiseAnd,
            new IntLiteralNode(Span, 0b1010),
            new IntLiteralNode(Span, 0b1100));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(0b1000, lit.Value);
    }

    [Fact]
    public void Bitwise_OrConstants()
    {
        // (| 0b1010 0b1100) → 0b1110 (14)
        var expr = new BinaryOperationNode(Span, BinaryOperator.BitwiseOr,
            new IntLiteralNode(Span, 0b1010),
            new IntLiteralNode(Span, 0b1100));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(0b1110, lit.Value);
    }

    [Fact]
    public void Bitwise_XorConstants()
    {
        // (^ 0b1010 0b1100) → 0b0110 (6)
        var expr = new BinaryOperationNode(Span, BinaryOperator.BitwiseXor,
            new IntLiteralNode(Span, 0b1010),
            new IntLiteralNode(Span, 0b1100));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(0b0110, lit.Value);
    }

    [Fact]
    public void Bitwise_LeftShift()
    {
        // (<< 1 4) → 16
        var expr = new BinaryOperationNode(Span, BinaryOperator.LeftShift,
            new IntLiteralNode(Span, 1),
            new IntLiteralNode(Span, 4));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(16, lit.Value);
    }

    [Fact]
    public void Bitwise_RightShift()
    {
        // (>> 16 2) → 4
        var expr = new BinaryOperationNode(Span, BinaryOperator.RightShift,
            new IntLiteralNode(Span, 16),
            new IntLiteralNode(Span, 2));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(4, lit.Value);
    }

    [Fact]
    public void ArithmeticDoubleNegation()
    {
        // (- (- x)) → x
        var x = new ReferenceNode(Span, "x");
        var inner = new UnaryOperationNode(Span, UnaryOperator.Negate, x);
        var expr = new UnaryOperationNode(Span, UnaryOperator.Negate, inner);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    #endregion

    #region Commutativity Tests

    [Fact]
    public void Commutativity_AndSameOperandsSwapped()
    {
        // (&& a b) structurally equal to (&& b a) for redundancy detection
        var a1 = new ReferenceNode(Span, "a");
        var b1 = new ReferenceNode(Span, "b");
        var a2 = new ReferenceNode(Span, "a");
        var b2 = new ReferenceNode(Span, "b");

        var expr1 = new BinaryOperationNode(Span, BinaryOperator.And, a1, b1);
        var expr2 = new BinaryOperationNode(Span, BinaryOperator.And, b2, a2);

        // (&& (&& a b) (&& b a)) should simplify to (&& a b) because they're commutatively equal
        var combined = new BinaryOperationNode(Span, BinaryOperator.And, expr1, expr2);

        var result = Simplify(combined);

        // Should simplify to just (&& a b)
        var andResult = Assert.IsType<BinaryOperationNode>(result);
        Assert.Equal(BinaryOperator.And, andResult.Operator);
    }

    [Fact]
    public void Commutativity_AdditionRecognized()
    {
        // (+ a b) and (+ b a) should be recognized as structurally equivalent
        var a1 = new ReferenceNode(Span, "a");
        var b1 = new ReferenceNode(Span, "b");
        var a2 = new ReferenceNode(Span, "a");
        var b2 = new ReferenceNode(Span, "b");

        var expr1 = new BinaryOperationNode(Span, BinaryOperator.Add, a1, b1);
        var expr2 = new BinaryOperationNode(Span, BinaryOperator.Add, b2, a2);

        // (- (+ a b) (+ b a)) should simplify to 0
        var sub = new BinaryOperationNode(Span, BinaryOperator.Subtract, expr1, expr2);

        var result = Simplify(sub);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(0, lit.Value);
    }

    [Fact]
    public void Commutativity_EqualityRecognized()
    {
        // (== a b) and (== b a) should be recognized as structurally equivalent
        var a1 = new ReferenceNode(Span, "a");
        var b1 = new ReferenceNode(Span, "b");
        var a2 = new ReferenceNode(Span, "a");
        var b2 = new ReferenceNode(Span, "b");

        var expr1 = new BinaryOperationNode(Span, BinaryOperator.Equal, a1, b1);
        var expr2 = new BinaryOperationNode(Span, BinaryOperator.Equal, b2, a2);

        // (&& (== a b) (== b a)) should simplify to (== a b)
        var combined = new BinaryOperationNode(Span, BinaryOperator.And, expr1, expr2);

        var result = Simplify(combined);

        var eqResult = Assert.IsType<BinaryOperationNode>(result);
        Assert.Equal(BinaryOperator.Equal, eqResult.Operator);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void EdgeCase_DivisionByZeroNotFolded()
    {
        // (/ 10 0) should NOT be folded (division by zero)
        var expr = new BinaryOperationNode(Span, BinaryOperator.Divide,
            new IntLiteralNode(Span, 10),
            new IntLiteralNode(Span, 0));

        var result = Simplify(expr);

        // Should remain as-is, not folded
        var binOp = Assert.IsType<BinaryOperationNode>(result);
        Assert.Equal(BinaryOperator.Divide, binOp.Operator);
    }

    [Fact]
    public void EdgeCase_ModuloByZeroNotFolded()
    {
        // (% 10 0) should NOT be folded
        var expr = new BinaryOperationNode(Span, BinaryOperator.Modulo,
            new IntLiteralNode(Span, 10),
            new IntLiteralNode(Span, 0));

        var result = Simplify(expr);

        // Should remain as-is, not folded
        var binOp = Assert.IsType<BinaryOperationNode>(result);
        Assert.Equal(BinaryOperator.Modulo, binOp.Operator);
    }

    [Fact]
    public void EdgeCase_FloatDivisionByZeroNotFolded()
    {
        // (/ 10.0 0.0) should NOT be folded
        var expr = new BinaryOperationNode(Span, BinaryOperator.Divide,
            new FloatLiteralNode(Span, 10.0),
            new FloatLiteralNode(Span, 0.0));

        var result = Simplify(expr);

        // Should remain as-is, not folded
        var binOp = Assert.IsType<BinaryOperationNode>(result);
        Assert.Equal(BinaryOperator.Divide, binOp.Operator);
    }

    [Fact]
    public void EdgeCase_NegativeZero()
    {
        // (+ x -0) should still simplify to x (negative zero equals zero)
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            x, new FloatLiteralNode(Span, -0.0));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void EdgeCase_DeeplyNested()
    {
        // (+ (+ (+ 1 2) 3) 4) → 10
        var add12 = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var add123 = new BinaryOperationNode(Span, BinaryOperator.Add,
            add12, new IntLiteralNode(Span, 3));
        var expr = new BinaryOperationNode(Span, BinaryOperator.Add,
            add123, new IntLiteralNode(Span, 4));

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(10, lit.Value);
    }

    [Fact]
    public void EdgeCase_ConditionalWithTrueFalse()
    {
        // (? c true false) → c
        var c = new ReferenceNode(Span, "c");
        var expr = new ConditionalExpressionNode(Span, c,
            new BoolLiteralNode(Span, true),
            new BoolLiteralNode(Span, false));

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("c", refNode.Name);
    }

    [Fact]
    public void EdgeCase_ConditionalWithFalseTrue()
    {
        // (? c false true) → (! c)
        var c = new ReferenceNode(Span, "c");
        var expr = new ConditionalExpressionNode(Span, c,
            new BoolLiteralNode(Span, false),
            new BoolLiteralNode(Span, true));

        var result = Simplify(expr);

        var notExpr = Assert.IsType<UnaryOperationNode>(result);
        Assert.Equal(UnaryOperator.Not, notExpr.Operator);
        var refNode = Assert.IsType<ReferenceNode>(notExpr.Operand);
        Assert.Equal("c", refNode.Name);
    }

    [Fact]
    public void EdgeCase_EqualTrueSimplifies()
    {
        // (== true x) → x
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Equal,
            new BoolLiteralNode(Span, true), x);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void EdgeCase_EqualFalseSimplifiesToNot()
    {
        // (== false x) → (! x)
        var x = new ReferenceNode(Span, "x");
        var expr = new BinaryOperationNode(Span, BinaryOperator.Equal,
            new BoolLiteralNode(Span, false), x);

        var result = Simplify(expr);

        var notExpr = Assert.IsType<UnaryOperationNode>(result);
        Assert.Equal(UnaryOperator.Not, notExpr.Operator);
        var refNode = Assert.IsType<ReferenceNode>(notExpr.Operand);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void EdgeCase_ImplicationNotPToP()
    {
        // (-> (! p) p) → p
        var p = new ReferenceNode(Span, "p");
        var notP = new UnaryOperationNode(Span, UnaryOperator.Not, p);
        var expr = new ImplicationExpressionNode(Span, notP, p);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("p", refNode.Name);
    }

    #endregion

    #region Expression Node Visitor Tests (Passthrough with Child Simplification)

    [Fact]
    public void Visitor_ArrayAccess_SimplifiesChildren()
    {
        // array[(+ 1 2)] → array[3]
        var array = new ReferenceNode(Span, "array");
        var index = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var expr = new ArrayAccessNode(Span, array, index);

        var result = Simplify(expr);

        var access = Assert.IsType<ArrayAccessNode>(result);
        var indexLit = Assert.IsType<IntLiteralNode>(access.Index);
        Assert.Equal(3, indexLit.Value);
    }

    [Fact]
    public void Visitor_ArrayLength_SimplifiesChild()
    {
        // For array length, the array expression itself could be simplified
        // (though typically it's just a reference)
        var array = new ReferenceNode(Span, "arr");
        var expr = new ArrayLengthNode(Span, array);

        var result = Simplify(expr);

        var length = Assert.IsType<ArrayLengthNode>(result);
        Assert.IsType<ReferenceNode>(length.Array);
    }

    [Fact]
    public void Visitor_FieldAccess_SimplifiesTarget()
    {
        // target.field where target could be simplified
        var target = new ReferenceNode(Span, "obj");
        var expr = new FieldAccessNode(Span, target, "field");

        var result = Simplify(expr);

        var access = Assert.IsType<FieldAccessNode>(result);
        Assert.Equal("field", access.FieldName);
    }

    [Fact]
    public void Visitor_SomeExpression_SimplifiesValue()
    {
        // Some((+ 1 2)) → Some(3)
        var value = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var expr = new SomeExpressionNode(Span, value);

        var result = Simplify(expr);

        var some = Assert.IsType<SomeExpressionNode>(result);
        var lit = Assert.IsType<IntLiteralNode>(some.Value);
        Assert.Equal(3, lit.Value);
    }

    [Fact]
    public void Visitor_NoneExpression_Unchanged()
    {
        var expr = new NoneExpressionNode(Span, null);

        var result = Simplify(expr);

        Assert.IsType<NoneExpressionNode>(result);
    }

    [Fact]
    public void Visitor_OkExpression_SimplifiesValue()
    {
        // Ok((+ 1 2)) → Ok(3)
        var value = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var expr = new OkExpressionNode(Span, value);

        var result = Simplify(expr);

        var ok = Assert.IsType<OkExpressionNode>(result);
        var lit = Assert.IsType<IntLiteralNode>(ok.Value);
        Assert.Equal(3, lit.Value);
    }

    [Fact]
    public void Visitor_ErrExpression_SimplifiesError()
    {
        // Err((+ 1 2)) → Err(3)
        var error = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var expr = new ErrExpressionNode(Span, error);

        var result = Simplify(expr);

        var err = Assert.IsType<ErrExpressionNode>(result);
        var lit = Assert.IsType<IntLiteralNode>(err.Error);
        Assert.Equal(3, lit.Value);
    }

    [Fact]
    public void Visitor_CallExpression_SimplifiesArguments()
    {
        // func((+ 1 2), (+ 3 4)) → func(3, 7)
        var args = new List<ExpressionNode>
        {
            new BinaryOperationNode(Span, BinaryOperator.Add,
                new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2)),
            new BinaryOperationNode(Span, BinaryOperator.Add,
                new IntLiteralNode(Span, 3), new IntLiteralNode(Span, 4))
        };
        var expr = new CallExpressionNode(Span, "func", args);

        var result = Simplify(expr);

        var call = Assert.IsType<CallExpressionNode>(result);
        Assert.Equal("func", call.Target);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(call.Arguments[0]).Value);
        Assert.Equal(7, Assert.IsType<IntLiteralNode>(call.Arguments[1]).Value);
    }

    [Fact]
    public void Visitor_NewExpression_SimplifiesArguments()
    {
        // new MyClass((+ 1 2)) → new MyClass(3)
        var args = new List<ExpressionNode>
        {
            new BinaryOperationNode(Span, BinaryOperator.Add,
                new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2))
        };
        var expr = new NewExpressionNode(Span, "MyClass", Array.Empty<string>(), args);

        var result = Simplify(expr);

        var newExpr = Assert.IsType<NewExpressionNode>(result);
        Assert.Equal("MyClass", newExpr.TypeName);
        Assert.Single(newExpr.Arguments);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(newExpr.Arguments[0]).Value);
    }

    [Fact]
    public void Visitor_ThisExpression_Unchanged()
    {
        var expr = new ThisExpressionNode(Span);

        var result = Simplify(expr);

        Assert.IsType<ThisExpressionNode>(result);
    }

    [Fact]
    public void Visitor_BaseExpression_Unchanged()
    {
        var expr = new BaseExpressionNode(Span);

        var result = Simplify(expr);

        Assert.IsType<BaseExpressionNode>(result);
    }

    [Fact]
    public void Visitor_CollectionContains_SimplifiesKeyOrValue()
    {
        // coll.Contains((+ 1 2)) → coll.Contains(3)
        var keyOrValue = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var expr = new CollectionContainsNode(Span, "coll", keyOrValue, ContainsMode.Value);

        var result = Simplify(expr);

        var contains = Assert.IsType<CollectionContainsNode>(result);
        Assert.Equal("coll", contains.CollectionName);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(contains.KeyOrValue).Value);
    }

    [Fact]
    public void Visitor_CollectionCount_SimplifiesCollection()
    {
        var collection = new ReferenceNode(Span, "list");
        var expr = new CollectionCountNode(Span, collection);

        var result = Simplify(expr);

        var count = Assert.IsType<CollectionCountNode>(result);
        Assert.IsType<ReferenceNode>(count.Collection);
    }

    [Fact]
    public void Visitor_NullCoalesce_SimplifiesBothSides()
    {
        // ((+ 1 2) ?? (+ 3 4)) → (3 ?? 7)
        var left = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var right = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 3), new IntLiteralNode(Span, 4));
        var expr = new NullCoalesceNode(Span, left, right);

        var result = Simplify(expr);

        var coalesce = Assert.IsType<NullCoalesceNode>(result);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(coalesce.Left).Value);
        Assert.Equal(7, Assert.IsType<IntLiteralNode>(coalesce.Right).Value);
    }

    [Fact]
    public void Visitor_NullConditional_SimplifiesTarget()
    {
        var target = new ReferenceNode(Span, "obj");
        var expr = new NullConditionalNode(Span, target, "Property");

        var result = Simplify(expr);

        var conditional = Assert.IsType<NullConditionalNode>(result);
        Assert.Equal("Property", conditional.MemberName);
    }

    [Fact]
    public void Visitor_RangeExpression_SimplifiesBounds()
    {
        // ((+ 1 2)...(+ 3 4)) → (3...7)
        var start = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var end = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 3), new IntLiteralNode(Span, 4));
        var expr = new RangeExpressionNode(Span, start, end);

        var result = Simplify(expr);

        var range = Assert.IsType<RangeExpressionNode>(result);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(range.Start).Value);
        Assert.Equal(7, Assert.IsType<IntLiteralNode>(range.End).Value);
    }

    [Fact]
    public void Visitor_IndexFromEnd_SimplifiesOffset()
    {
        // ^(+ 1 2) → ^3
        var offset = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var expr = new IndexFromEndNode(Span, offset);

        var result = Simplify(expr);

        var index = Assert.IsType<IndexFromEndNode>(result);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(index.Offset).Value);
    }

    [Fact]
    public void Visitor_AwaitExpression_SimplifiesAwaited()
    {
        // await (condition ? task1 : task1) where condition simplifies
        var awaited = new ReferenceNode(Span, "task");
        var expr = new AwaitExpressionNode(Span, awaited, null);

        var result = Simplify(expr);

        var awaitExpr = Assert.IsType<AwaitExpressionNode>(result);
        Assert.IsType<ReferenceNode>(awaitExpr.Awaited);
    }

    [Fact]
    public void Visitor_RecordCreation_SimplifiesFieldValues()
    {
        // new Record { Field = (+ 1 2) } → new Record { Field = 3 }
        var fields = new List<FieldAssignmentNode>
        {
            new FieldAssignmentNode(Span, "Field",
                new BinaryOperationNode(Span, BinaryOperator.Add,
                    new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2)))
        };
        var expr = new RecordCreationNode(Span, "MyRecord", fields);

        var result = Simplify(expr);

        var record = Assert.IsType<RecordCreationNode>(result);
        Assert.Single(record.Fields);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(record.Fields[0].Value).Value);
    }

    [Fact]
    public void Visitor_InterpolatedString_SimplifiesExpressions()
    {
        // $"Value: {(+ 1 2)}" → $"Value: {3}"
        var parts = new List<InterpolatedStringPartNode>
        {
            new InterpolatedStringTextNode(Span, "Value: "),
            new InterpolatedStringExpressionNode(Span,
                new BinaryOperationNode(Span, BinaryOperator.Add,
                    new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2)))
        };
        var expr = new InterpolatedStringNode(Span, parts);

        var result = Simplify(expr);

        var interp = Assert.IsType<InterpolatedStringNode>(result);
        Assert.Equal(2, interp.Parts.Count);
        var exprPart = Assert.IsType<InterpolatedStringExpressionNode>(interp.Parts[1]);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(exprPart.Expression).Value);
    }

    [Fact]
    public void Visitor_ArrayCreation_SimplifiesInitializer()
    {
        // new int[] { (+ 1 2), (+ 3 4) } → new int[] { 3, 7 }
        var initializer = new List<ExpressionNode>
        {
            new BinaryOperationNode(Span, BinaryOperator.Add,
                new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2)),
            new BinaryOperationNode(Span, BinaryOperator.Add,
                new IntLiteralNode(Span, 3), new IntLiteralNode(Span, 4))
        };
        var expr = new ArrayCreationNode(Span, "arr1", "arr", "i32", null, initializer, new AttributeCollection());

        var result = Simplify(expr);

        var array = Assert.IsType<ArrayCreationNode>(result);
        Assert.Equal(2, array.Initializer.Count);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(array.Initializer[0]).Value);
        Assert.Equal(7, Assert.IsType<IntLiteralNode>(array.Initializer[1]).Value);
    }

    [Fact]
    public void Visitor_ListCreation_SimplifiesElements()
    {
        // List { (+ 1 2), (+ 3 4) } → List { 3, 7 }
        var elements = new List<ExpressionNode>
        {
            new BinaryOperationNode(Span, BinaryOperator.Add,
                new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2)),
            new BinaryOperationNode(Span, BinaryOperator.Add,
                new IntLiteralNode(Span, 3), new IntLiteralNode(Span, 4))
        };
        var expr = new ListCreationNode(Span, "list1", "list", "i32", elements, new AttributeCollection());

        var result = Simplify(expr);

        var list = Assert.IsType<ListCreationNode>(result);
        Assert.Equal(2, list.Elements.Count);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(list.Elements[0]).Value);
        Assert.Equal(7, Assert.IsType<IntLiteralNode>(list.Elements[1]).Value);
    }

    [Fact]
    public void Visitor_DictionaryCreation_SimplifiesEntries()
    {
        // Dict { (+ 1 2): (+ 3 4) } → Dict { 3: 7 }
        var entries = new List<KeyValuePairNode>
        {
            new KeyValuePairNode(Span,
                new BinaryOperationNode(Span, BinaryOperator.Add,
                    new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2)),
                new BinaryOperationNode(Span, BinaryOperator.Add,
                    new IntLiteralNode(Span, 3), new IntLiteralNode(Span, 4)))
        };
        var expr = new DictionaryCreationNode(Span, "dict1", "dict", "i32", "i32", entries, new AttributeCollection());

        var result = Simplify(expr);

        var dict = Assert.IsType<DictionaryCreationNode>(result);
        Assert.Single(dict.Entries);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(dict.Entries[0].Key).Value);
        Assert.Equal(7, Assert.IsType<IntLiteralNode>(dict.Entries[0].Value).Value);
    }

    [Fact]
    public void Visitor_SetCreation_SimplifiesElements()
    {
        // Set { (+ 1 2), (+ 3 4) } → Set { 3, 7 }
        var elements = new List<ExpressionNode>
        {
            new BinaryOperationNode(Span, BinaryOperator.Add,
                new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2)),
            new BinaryOperationNode(Span, BinaryOperator.Add,
                new IntLiteralNode(Span, 3), new IntLiteralNode(Span, 4))
        };
        var expr = new SetCreationNode(Span, "set1", "set", "i32", elements, new AttributeCollection());

        var result = Simplify(expr);

        var set = Assert.IsType<SetCreationNode>(result);
        Assert.Equal(2, set.Elements.Count);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(set.Elements[0]).Value);
        Assert.Equal(7, Assert.IsType<IntLiteralNode>(set.Elements[1]).Value);
    }

    [Fact]
    public void Visitor_WithExpression_SimplifiesAssignments()
    {
        // record with { Field = (+ 1 2) } → record with { Field = 3 }
        var target = new ReferenceNode(Span, "record");
        var assignments = new List<WithPropertyAssignmentNode>
        {
            new WithPropertyAssignmentNode(Span, "Field",
                new BinaryOperationNode(Span, BinaryOperator.Add,
                    new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2)))
        };
        var expr = new WithExpressionNode(Span, target, assignments);

        var result = Simplify(expr);

        var withExpr = Assert.IsType<WithExpressionNode>(result);
        Assert.Single(withExpr.Assignments);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(withExpr.Assignments[0].Value).Value);
    }

    [Fact]
    public void Visitor_LambdaExpression_SimplifiesBody()
    {
        // (x) => (+ 1 2) → (x) => 3
        var param = new LambdaParameterNode(Span, "x", "i32");
        var body = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));
        var expr = new LambdaExpressionNode(Span, "lam1", new[] { param }, null, false, body, null, new AttributeCollection());

        var result = Simplify(expr);

        var lambda = Assert.IsType<LambdaExpressionNode>(result);
        Assert.NotNull(lambda.ExpressionBody);
        Assert.Equal(3, Assert.IsType<IntLiteralNode>(lambda.ExpressionBody).Value);
    }

    #endregion

    #region Pathological Input Tests (Fuzz-like)

    [Fact]
    public void Pathological_VeryDeepNesting_DoesNotStackOverflow()
    {
        // Create a very deeply nested expression: (+ (+ (+ ... (+ 1 1) ...) 1) 1)
        ExpressionNode expr = new IntLiteralNode(Span, 1);
        const int depth = 100;

        for (int i = 0; i < depth; i++)
        {
            expr = new BinaryOperationNode(Span, BinaryOperator.Add,
                expr, new IntLiteralNode(Span, 1));
        }

        var result = Simplify(expr);

        // Should fold to 101
        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(depth + 1, lit.Value);
    }

    [Fact]
    public void Pathological_ManyIterations_ConvergesToFixedPoint()
    {
        // Create expression that requires many simplification iterations
        // (&& true (&& true (&& true (&& true x))))
        var x = new ReferenceNode(Span, "x");
        ExpressionNode expr = x;

        for (int i = 0; i < 20; i++)
        {
            expr = new BinaryOperationNode(Span, BinaryOperator.And,
                new BoolLiteralNode(Span, true), expr);
        }

        var result = Simplify(expr);

        // Should simplify to just x
        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Pathological_AlternatingNegations()
    {
        // (! (! (! (! (! (! x)))))) with 50 negations → x (even) or (! x) (odd)
        var x = new ReferenceNode(Span, "x");
        ExpressionNode expr = x;
        const int negations = 50;

        for (int i = 0; i < negations; i++)
        {
            expr = new UnaryOperationNode(Span, UnaryOperator.Not, expr);
        }

        var result = Simplify(expr);

        // 50 negations (even) → x
        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Pathological_AlternatingNegations_Odd()
    {
        // 51 negations (odd) → (! x)
        var x = new ReferenceNode(Span, "x");
        ExpressionNode expr = x;
        const int negations = 51;

        for (int i = 0; i < negations; i++)
        {
            expr = new UnaryOperationNode(Span, UnaryOperator.Not, expr);
        }

        var result = Simplify(expr);

        var notExpr = Assert.IsType<UnaryOperationNode>(result);
        Assert.Equal(UnaryOperator.Not, notExpr.Operator);
        var refNode = Assert.IsType<ReferenceNode>(notExpr.Operand);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Pathological_ComplexCombination()
    {
        // (&& (|| true false) (-> (! false) (== (+ 1 2) 3)))
        // Should simplify to true
        var orExpr = new BinaryOperationNode(Span, BinaryOperator.Or,
            new BoolLiteralNode(Span, true),
            new BoolLiteralNode(Span, false));

        var notFalse = new UnaryOperationNode(Span, UnaryOperator.Not,
            new BoolLiteralNode(Span, false));

        var add12 = new BinaryOperationNode(Span, BinaryOperator.Add,
            new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2));

        var eq = new BinaryOperationNode(Span, BinaryOperator.Equal,
            add12, new IntLiteralNode(Span, 3));

        var impl = new ImplicationExpressionNode(Span, notFalse, eq);

        var expr = new BinaryOperationNode(Span, BinaryOperator.And, orExpr, impl);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Pathological_DeeplyNestedConditionals()
    {
        // (? true (? true (? true x y) y) y) → x
        var x = new ReferenceNode(Span, "x");
        var y = new ReferenceNode(Span, "y");

        ExpressionNode expr = x;
        for (int i = 0; i < 10; i++)
        {
            expr = new ConditionalExpressionNode(Span,
                new BoolLiteralNode(Span, true), expr, y);
        }

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Pathological_ChainedImplications()
    {
        // (-> true (-> true (-> true x))) → x
        var x = new ReferenceNode(Span, "x");
        ExpressionNode expr = x;

        for (int i = 0; i < 10; i++)
        {
            expr = new ImplicationExpressionNode(Span,
                new BoolLiteralNode(Span, true), expr);
        }

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Pathological_MixedArithmeticIdentities()
    {
        // (+ (* x 1) (- y 0)) → (+ x y)
        var x = new ReferenceNode(Span, "x");
        var y = new ReferenceNode(Span, "y");

        var xTimes1 = new BinaryOperationNode(Span, BinaryOperator.Multiply,
            x, new IntLiteralNode(Span, 1));
        var yMinus0 = new BinaryOperationNode(Span, BinaryOperator.Subtract,
            y, new IntLiteralNode(Span, 0));

        var expr = new BinaryOperationNode(Span, BinaryOperator.Add, xTimes1, yMinus0);

        var result = Simplify(expr);

        var add = Assert.IsType<BinaryOperationNode>(result);
        Assert.Equal(BinaryOperator.Add, add.Operator);
        Assert.Equal("x", Assert.IsType<ReferenceNode>(add.Left).Name);
        Assert.Equal("y", Assert.IsType<ReferenceNode>(add.Right).Name);
    }

    [Fact]
    public void Pathological_RedundantTautologyChain()
    {
        // (&& (|| a (! a)) (&& (|| b (! b)) (&& (|| c (! c)) x)))
        // Should simplify to x (all tautologies become true)
        var a = new ReferenceNode(Span, "a");
        var b = new ReferenceNode(Span, "b");
        var c = new ReferenceNode(Span, "c");
        var x = new ReferenceNode(Span, "x");

        var tautA = new BinaryOperationNode(Span, BinaryOperator.Or,
            a, new UnaryOperationNode(Span, UnaryOperator.Not, new ReferenceNode(Span, "a")));
        var tautB = new BinaryOperationNode(Span, BinaryOperator.Or,
            b, new UnaryOperationNode(Span, UnaryOperator.Not, new ReferenceNode(Span, "b")));
        var tautC = new BinaryOperationNode(Span, BinaryOperator.Or,
            c, new UnaryOperationNode(Span, UnaryOperator.Not, new ReferenceNode(Span, "c")));

        var inner = new BinaryOperationNode(Span, BinaryOperator.And, tautC, x);
        var middle = new BinaryOperationNode(Span, BinaryOperator.And, tautB, inner);
        var expr = new BinaryOperationNode(Span, BinaryOperator.And, tautA, middle);

        var result = Simplify(expr);

        var refNode = Assert.IsType<ReferenceNode>(result);
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Pathological_LargeConstantFolding()
    {
        // Sum of 1 to 100 via constant folding
        ExpressionNode expr = new IntLiteralNode(Span, 0);
        for (int i = 1; i <= 100; i++)
        {
            expr = new BinaryOperationNode(Span, BinaryOperator.Add,
                expr, new IntLiteralNode(Span, i));
        }

        var result = Simplify(expr);

        var lit = Assert.IsType<IntLiteralNode>(result);
        Assert.Equal(5050, lit.Value); // Sum of 1 to 100
    }

    [Fact]
    public void Pathological_DeMorganChain()
    {
        // (! (&& (! (|| a b)) (! (|| c d))))
        // → (! (&& (&& (! a) (! b)) (&& (! c) (! d))))  [De Morgan on inner]
        // This tests that De Morgan doesn't cause infinite expansion
        var a = new ReferenceNode(Span, "a");
        var b = new ReferenceNode(Span, "b");
        var c = new ReferenceNode(Span, "c");
        var d = new ReferenceNode(Span, "d");

        var orAB = new BinaryOperationNode(Span, BinaryOperator.Or, a, b);
        var orCD = new BinaryOperationNode(Span, BinaryOperator.Or, c, d);

        var notOrAB = new UnaryOperationNode(Span, UnaryOperator.Not, orAB);
        var notOrCD = new UnaryOperationNode(Span, UnaryOperator.Not, orCD);

        var andInner = new BinaryOperationNode(Span, BinaryOperator.And, notOrAB, notOrCD);
        var expr = new UnaryOperationNode(Span, UnaryOperator.Not, andInner);

        // This should complete without stack overflow or timeout
        var result = Simplify(expr);

        // The result structure may vary, but it should terminate and produce a valid expression
        Assert.NotNull(result);
    }

    [Fact]
    public void Pathological_SingleBoundVariableQuantifier()
    {
        // Forall with one bound variable and true body → true
        var boundVar = new QuantifierVariableNode(Span, "i", "i32");
        var expr = new ForallExpressionNode(Span, new[] { boundVar },
            new BoolLiteralNode(Span, true));

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    [Fact]
    public void Pathological_NestedQuantifiers()
    {
        // (forall ((i i32)) (forall ((j i32)) true)) → true
        var innerForall = new ForallExpressionNode(Span,
            new[] { new QuantifierVariableNode(Span, "j", "i32") },
            new BoolLiteralNode(Span, true));
        var expr = new ForallExpressionNode(Span,
            new[] { new QuantifierVariableNode(Span, "i", "i32") },
            innerForall);

        var result = Simplify(expr);

        var lit = Assert.IsType<BoolLiteralNode>(result);
        Assert.True(lit.Value);
    }

    #endregion

    #region Helper Methods

    private static ExpressionNode Simplify(ExpressionNode expr)
    {
        var simplifier = new ExpressionSimplifier();

        // Run until fixed point (up to 10 iterations)
        var current = expr;
        for (int i = 0; i < 10; i++)
        {
            var newSimplifier = new ExpressionSimplifier();
            var result = newSimplifier.Simplify(current);
            if (!newSimplifier.Changed)
                return result;
            current = result;
        }
        return current;
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #endregion
}
