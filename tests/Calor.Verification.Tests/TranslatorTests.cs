using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Xunit;
using System.Runtime.CompilerServices;

namespace Calor.Verification.Tests;

/// <summary>
/// Tests for the ContractTranslator that converts Calor AST to Z3 expressions.
/// All tests skip if Z3 is not available on the system.
/// </summary>
public class TranslatorTests
{
    [SkippableFact]
    public void TranslatesIntegerLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesIntegerLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesIntegerLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new IntLiteralNode(TextSpan.Empty, 42);
        var result = translator.Translate(expr);

        Assert.NotNull(result);
        // Bit-vector representation varies, but should be a BitVecExpr
        Assert.IsType<Microsoft.Z3.BitVecNum>(result);
    }

    [SkippableFact]
    public void TranslatesVariableReference()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesVariableReferenceCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesVariableReferenceCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("x", "i32");
        var expr = new ReferenceNode(TextSpan.Empty, "x");
        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.Equal("x", result.ToString());
    }

    [SkippableFact]
    public void TranslatesArithmeticAdd()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesArithmeticAddCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesArithmeticAddCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Add,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.Contains("a", result.ToString());
        Assert.Contains("b", result.ToString());
    }

    [SkippableFact]
    public void TranslatesArithmeticSub()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesArithmeticSubCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesArithmeticSubCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Subtract,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.Translate(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesArithmeticMul()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesArithmeticMulCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesArithmeticMulCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Multiply,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.Translate(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesArithmeticDiv()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesArithmeticDivCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesArithmeticDivCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Divide,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.Translate(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesArithmeticMod()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesArithmeticModCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesArithmeticModCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Modulo,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.Translate(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesComparisonEq()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesComparisonEqCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesComparisonEqCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Equal,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesComparisonNe()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesComparisonNeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesComparisonNeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.NotEqual,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesComparisonLt()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesComparisonLtCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesComparisonLtCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.LessThan,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesComparisonLe()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesComparisonLeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesComparisonLeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.LessOrEqual,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesComparisonGt()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesComparisonGtCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesComparisonGtCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesComparisonGe()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesComparisonGeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesComparisonGeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i32");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterOrEqual,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesLogicalAnd()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesLogicalAndCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesLogicalAndCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("p", "bool");
        translator.DeclareVariable("q", "bool");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.And,
            new ReferenceNode(TextSpan.Empty, "p"),
            new ReferenceNode(TextSpan.Empty, "q"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesLogicalOr()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesLogicalOrCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesLogicalOrCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("p", "bool");
        translator.DeclareVariable("q", "bool");

        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Or,
            new ReferenceNode(TextSpan.Empty, "p"),
            new ReferenceNode(TextSpan.Empty, "q"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesLogicalNot()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesLogicalNotCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesLogicalNotCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("p", "bool");

        var expr = new UnaryOperationNode(
            TextSpan.Empty,
            UnaryOperator.Not,
            new ReferenceNode(TextSpan.Empty, "p"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesNestedExpr()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesNestedExprCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesNestedExprCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("x", "i32");

        // (x * x) >= 0
        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterOrEqual,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Multiply,
                new ReferenceNode(TextSpan.Empty, "x"),
                new ReferenceNode(TextSpan.Empty, "x")),
            new IntLiteralNode(TextSpan.Empty, 0));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void ReturnsNullForFunctionCall()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForFunctionCallCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForFunctionCallCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new CallExpressionNode(
            TextSpan.Empty,
            "strlen",
            new List<ExpressionNode> { new StringLiteralNode(TextSpan.Empty, "hello") });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void TranslatesStringLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new StringLiteralNode(TextSpan.Empty, "hello");

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void ReturnsNullForUnsupportedType()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForUnsupportedTypeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForUnsupportedTypeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var declared = translator.DeclareVariable("x", "f32");

        Assert.False(declared);
    }

    // ===========================================
    // Quantifier and Implication Tests
    // ===========================================

    [SkippableFact]
    public void TranslatesSimpleForall()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesSimpleForallCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesSimpleForallCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // (forall ((x i32)) (>= x 0))
        var forallExpr = new ForallExpressionNode(
            TextSpan.Empty,
            new List<QuantifierVariableNode>
            {
                new QuantifierVariableNode(TextSpan.Empty, "x", "i32")
            },
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)));

        var result = translator.TranslateBoolExpr(forallExpr);

        Assert.NotNull(result);
        // Z3 should produce a quantified formula
        Assert.Contains("forall", result.ToString().ToLower());
    }

    [SkippableFact]
    public void TranslatesSimpleExists()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesSimpleExistsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesSimpleExistsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // (exists ((x i32)) (== x 0))
        var existsExpr = new ExistsExpressionNode(
            TextSpan.Empty,
            new List<QuantifierVariableNode>
            {
                new QuantifierVariableNode(TextSpan.Empty, "x", "i32")
            },
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)));

        var result = translator.TranslateBoolExpr(existsExpr);

        Assert.NotNull(result);
        // Z3 should produce an existentially quantified formula
        Assert.Contains("exists", result.ToString().ToLower());
    }

    [SkippableFact]
    public void TranslatesImplication()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesImplicationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesImplicationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("p", "bool");
        translator.DeclareVariable("q", "bool");

        // (-> p q)
        var implExpr = new ImplicationExpressionNode(
            TextSpan.Empty,
            new ReferenceNode(TextSpan.Empty, "p"),
            new ReferenceNode(TextSpan.Empty, "q"));

        var result = translator.TranslateBoolExpr(implExpr);

        Assert.NotNull(result);
        // Z3 MkImplies produces an implication using "=>" in SMT-LIB format
        Assert.Contains("=>", result.ToString());
    }

    [SkippableFact]
    public void TranslatesForallWithImplication()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesForallWithImplicationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesForallWithImplicationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("n", "i32");

        // (forall ((i i32)) (-> (>= i 0) (< i n)))
        var forallExpr = new ForallExpressionNode(
            TextSpan.Empty,
            new List<QuantifierVariableNode>
            {
                new QuantifierVariableNode(TextSpan.Empty, "i", "i32")
            },
            new ImplicationExpressionNode(
                TextSpan.Empty,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterOrEqual,
                    new ReferenceNode(TextSpan.Empty, "i"),
                    new IntLiteralNode(TextSpan.Empty, 0)),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.LessThan,
                    new ReferenceNode(TextSpan.Empty, "i"),
                    new ReferenceNode(TextSpan.Empty, "n"))));

        var result = translator.TranslateBoolExpr(forallExpr);

        Assert.NotNull(result);
        Assert.Contains("forall", result.ToString().ToLower());
        Assert.Contains("=>", result.ToString()); // Z3 uses "=>" for implication
    }

    [SkippableFact]
    public void TranslatesForallWithMultipleBoundVariables()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesForallWithMultipleBoundVariablesCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesForallWithMultipleBoundVariablesCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // (forall ((i i32) (j i32)) (>= (+ i j) 0))
        var forallExpr = new ForallExpressionNode(
            TextSpan.Empty,
            new List<QuantifierVariableNode>
            {
                new QuantifierVariableNode(TextSpan.Empty, "i", "i32"),
                new QuantifierVariableNode(TextSpan.Empty, "j", "i32")
            },
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    new ReferenceNode(TextSpan.Empty, "i"),
                    new ReferenceNode(TextSpan.Empty, "j")),
                new IntLiteralNode(TextSpan.Empty, 0)));

        var result = translator.TranslateBoolExpr(forallExpr);

        Assert.NotNull(result);
        // Should contain both bound variables
        var resultStr = result.ToString();
        Assert.Contains("forall", resultStr.ToLower());
    }

    [SkippableFact]
    public void TranslatesNestedQuantifiers()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesNestedQuantifiersCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesNestedQuantifiersCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // (forall ((x i32)) (exists ((y i32)) (== y (* x 2))))
        var forallExpr = new ForallExpressionNode(
            TextSpan.Empty,
            new List<QuantifierVariableNode>
            {
                new QuantifierVariableNode(TextSpan.Empty, "x", "i32")
            },
            new ExistsExpressionNode(
                TextSpan.Empty,
                new List<QuantifierVariableNode>
                {
                    new QuantifierVariableNode(TextSpan.Empty, "y", "i32")
                },
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Equal,
                    new ReferenceNode(TextSpan.Empty, "y"),
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.Multiply,
                        new ReferenceNode(TextSpan.Empty, "x"),
                        new IntLiteralNode(TextSpan.Empty, 2)))));

        var result = translator.TranslateBoolExpr(forallExpr);

        Assert.NotNull(result);
        var resultStr = result.ToString().ToLower();
        Assert.Contains("forall", resultStr);
        Assert.Contains("exists", resultStr);
    }

    [SkippableFact]
    public void QuantifierVariableScopingIsCorrect()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        QuantifierVariableScopingIsCorrectCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void QuantifierVariableScopingIsCorrectCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // Declare outer variable x
        translator.DeclareVariable("x", "i32");

        // (forall ((x i32)) (>= x 0)) - inner x should shadow outer x
        var forallExpr = new ForallExpressionNode(
            TextSpan.Empty,
            new List<QuantifierVariableNode>
            {
                new QuantifierVariableNode(TextSpan.Empty, "x", "i32")
            },
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)));

        var result = translator.TranslateBoolExpr(forallExpr);

        Assert.NotNull(result);

        // After translation, outer x should still be accessible
        var outerX = translator.Variables["x"];
        Assert.NotNull(outerX.Expr);
    }

    [SkippableFact]
    public void TranslatesArrayAccessInQuantifier()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesArrayAccessInQuantifierCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesArrayAccessInQuantifierCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("n", "i32");

        // (forall ((i i32)) (-> (&& (>= i 0) (< i n)) (>= arr{i} 0)))
        var forallExpr = new ForallExpressionNode(
            TextSpan.Empty,
            new List<QuantifierVariableNode>
            {
                new QuantifierVariableNode(TextSpan.Empty, "i", "i32")
            },
            new ImplicationExpressionNode(
                TextSpan.Empty,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.And,
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.GreaterOrEqual,
                        new ReferenceNode(TextSpan.Empty, "i"),
                        new IntLiteralNode(TextSpan.Empty, 0)),
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.LessThan,
                        new ReferenceNode(TextSpan.Empty, "i"),
                        new ReferenceNode(TextSpan.Empty, "n"))),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterOrEqual,
                    new ArrayAccessNode(
                        TextSpan.Empty,
                        new ReferenceNode(TextSpan.Empty, "arr"),
                        new ReferenceNode(TextSpan.Empty, "i")),
                    new IntLiteralNode(TextSpan.Empty, 0))));

        var result = translator.TranslateBoolExpr(forallExpr);

        Assert.NotNull(result);
        var resultStr = result.ToString();
        Assert.Contains("forall", resultStr.ToLower());
        // Array access is translated successfully - Z3 uses its own format for array select
        Assert.Contains("arr", resultStr); // arr should appear in the expression
    }

    [SkippableFact]
    public void TranslatesExistsWithMultipleBoundVariables()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesExistsWithMultipleBoundVariablesCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesExistsWithMultipleBoundVariablesCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // (exists ((i i32) (j i32)) (== (+ i j) 10))
        var existsExpr = new ExistsExpressionNode(
            TextSpan.Empty,
            new List<QuantifierVariableNode>
            {
                new QuantifierVariableNode(TextSpan.Empty, "i", "i32"),
                new QuantifierVariableNode(TextSpan.Empty, "j", "i32")
            },
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    new ReferenceNode(TextSpan.Empty, "i"),
                    new ReferenceNode(TextSpan.Empty, "j")),
                new IntLiteralNode(TextSpan.Empty, 10)));

        var result = translator.TranslateBoolExpr(existsExpr);

        Assert.NotNull(result);
        Assert.Contains("exists", result.ToString().ToLower());
    }

    [SkippableFact]
    public void TranslatesChainedImplication()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesChainedImplicationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesChainedImplicationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("p", "bool");
        translator.DeclareVariable("q", "bool");
        translator.DeclareVariable("r", "bool");

        // (-> p (-> q r)) - chained implication
        var implExpr = new ImplicationExpressionNode(
            TextSpan.Empty,
            new ReferenceNode(TextSpan.Empty, "p"),
            new ImplicationExpressionNode(
                TextSpan.Empty,
                new ReferenceNode(TextSpan.Empty, "q"),
                new ReferenceNode(TextSpan.Empty, "r")));

        var result = translator.TranslateBoolExpr(implExpr);

        Assert.NotNull(result);
        // Should have nested implications - Z3 uses "=>" for implication
        var resultStr = result.ToString();
        // Count occurrences of "=>" for implications (at least 2 for chained implication)
        var count = resultStr.Split("=>").Length - 1;
        Assert.True(count >= 2, $"Expected at least 2 implications, found {count} in: {resultStr}");
    }

    // ===========================================
    // 64-bit and Mixed-Width Tests
    // ===========================================

    [SkippableFact]
    public void Translates64BitVariable()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Translates64BitVariableCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Translates64BitVariableCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var declared = translator.DeclareVariable("x", "i64");
        Assert.True(declared);

        var expr = new ReferenceNode(TextSpan.Empty, "x");
        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
        var bvExpr = (Microsoft.Z3.BitVecExpr)result;
        Assert.Equal(64u, bvExpr.SortSize);
    }

    [SkippableFact]
    public void TranslatesMixed32And64BitAddition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesMixed32And64BitAdditionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesMixed32And64BitAdditionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "i32");
        translator.DeclareVariable("b", "i64");

        // a + b (32-bit + 64-bit should result in 64-bit)
        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Add,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
        var bvExpr = (Microsoft.Z3.BitVecExpr)result;
        // Result should be 64-bit (wider operand wins)
        Assert.Equal(64u, bvExpr.SortSize);
    }

    // ===========================================
    // Unsigned Type Tests
    // ===========================================

    [SkippableFact]
    public void TranslatesUnsigned32BitVariable()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesUnsigned32BitVariableCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesUnsigned32BitVariableCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var declared = translator.DeclareVariable("x", "u32");
        Assert.True(declared);

        var expr = new ReferenceNode(TextSpan.Empty, "x");
        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
        var bvExpr = (Microsoft.Z3.BitVecExpr)result;
        Assert.Equal(32u, bvExpr.SortSize);
    }

    [SkippableFact]
    public void TranslatesUnsigned64BitVariable()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesUnsigned64BitVariableCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesUnsigned64BitVariableCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var declared = translator.DeclareVariable("x", "u64");
        Assert.True(declared);

        var expr = new ReferenceNode(TextSpan.Empty, "x");
        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
        var bvExpr = (Microsoft.Z3.BitVecExpr)result;
        Assert.Equal(64u, bvExpr.SortSize);
    }

    [SkippableFact]
    public void TranslatesAllIntegerTypeSizes()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesAllIntegerTypeSizesCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesAllIntegerTypeSizesCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // Test all signed types
        Assert.True(translator.DeclareVariable("i8_var", "i8"));
        Assert.True(translator.DeclareVariable("i16_var", "i16"));
        Assert.True(translator.DeclareVariable("i32_var", "i32"));
        Assert.True(translator.DeclareVariable("i64_var", "i64"));

        // Test all unsigned types
        Assert.True(translator.DeclareVariable("u8_var", "u8"));
        Assert.True(translator.DeclareVariable("u16_var", "u16"));
        Assert.True(translator.DeclareVariable("u32_var", "u32"));
        Assert.True(translator.DeclareVariable("u64_var", "u64"));

        // Verify bit widths
        var vars = translator.Variables;
        Assert.Equal(8u, ((Microsoft.Z3.BitVecExpr)vars["i8_var"].Expr).SortSize);
        Assert.Equal(16u, ((Microsoft.Z3.BitVecExpr)vars["i16_var"].Expr).SortSize);
        Assert.Equal(32u, ((Microsoft.Z3.BitVecExpr)vars["i32_var"].Expr).SortSize);
        Assert.Equal(64u, ((Microsoft.Z3.BitVecExpr)vars["i64_var"].Expr).SortSize);
        Assert.Equal(8u, ((Microsoft.Z3.BitVecExpr)vars["u8_var"].Expr).SortSize);
        Assert.Equal(16u, ((Microsoft.Z3.BitVecExpr)vars["u16_var"].Expr).SortSize);
        Assert.Equal(32u, ((Microsoft.Z3.BitVecExpr)vars["u32_var"].Expr).SortSize);
        Assert.Equal(64u, ((Microsoft.Z3.BitVecExpr)vars["u64_var"].Expr).SortSize);
    }

    [SkippableFact]
    public void TranslatesUnsignedComparison()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesUnsignedComparisonCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesUnsignedComparisonCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("a", "u32");
        translator.DeclareVariable("b", "u32");

        // a < b (unsigned comparison)
        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.LessThan,
            new ReferenceNode(TextSpan.Empty, "a"),
            new ReferenceNode(TextSpan.Empty, "b"));

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
        // The result should be a BoolExpr representing unsigned comparison
        // Z3 uses bvult for unsigned less-than
        var resultStr = result.ToString().ToLower();
        Assert.True(resultStr.Contains("bvult") || resultStr.Contains("ult"),
            $"Expected unsigned comparison (bvult), got: {result}");
    }

    // ===========================================
    // String Theory Tests
    // ===========================================

    [SkippableFact]
    public void TranslatesStringVariable()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringVariableCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringVariableCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var declared = translator.DeclareVariable("s", "string");
        Assert.True(declared);

        var expr = new ReferenceNode(TextSpan.Empty, "s");
        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void TranslatesStringLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (len s)
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Length,
            new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") });

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        // String length returns a 32-bit unsigned bit-vector
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
        var bvExpr = (Microsoft.Z3.BitVecExpr)result;
        Assert.Equal(32u, bvExpr.SortSize);
    }

    [SkippableFact]
    public void TranslatesStringContains()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringContainsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringContainsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (contains s "hello")
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Contains,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "hello")
            });

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BoolExpr>(result);
    }

    [SkippableFact]
    public void TranslatesStringStartsWith()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringStartsWithCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringStartsWithCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (starts s "prefix")
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.StartsWith,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "prefix")
            });

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BoolExpr>(result);
    }

    [SkippableFact]
    public void TranslatesStringEndsWith()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringEndsWithCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringEndsWithCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (ends s "suffix")
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.EndsWith,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "suffix")
            });

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BoolExpr>(result);
    }

    [SkippableFact]
    public void TranslatesStringEquals()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringEqualsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringEqualsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s1", "string");
        translator.DeclareVariable("s2", "string");

        // (equals s1 s2)
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Equals,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s1"),
                new ReferenceNode(TextSpan.Empty, "s2")
            });

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BoolExpr>(result);
    }

    [SkippableFact]
    public void TranslatesStringIsNullOrEmpty()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringIsNullOrEmptyCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringIsNullOrEmptyCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (isempty s)
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.IsNullOrEmpty,
            new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") });

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BoolExpr>(result);
    }

    [SkippableFact]
    public void TranslatesStringConcat()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringConcatCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringConcatCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s1", "string");
        translator.DeclareVariable("s2", "string");

        // (concat s1 s2)
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Concat,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s1"),
                new ReferenceNode(TextSpan.Empty, "s2")
            });

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void TranslatesStringIndexOf()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringIndexOfCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringIndexOfCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (indexof s "hello")
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.IndexOf,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "hello")
            });

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        // IndexOf returns a signed 32-bit bit-vector (can be -1 if not found)
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
        var bvExpr = (Microsoft.Z3.BitVecExpr)result;
        Assert.Equal(32u, bvExpr.SortSize);
    }

    [SkippableFact]
    public void TranslatesStringIndexOfWithStartOffset()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringIndexOfWithStartOffsetCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringIndexOfWithStartOffsetCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (indexof s "hello" 5) - search starting from index 5
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.IndexOf,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "hello"),
                new IntLiteralNode(TextSpan.Empty, 5)
            });

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
        var bvExpr = (Microsoft.Z3.BitVecExpr)result;
        Assert.Equal(32u, bvExpr.SortSize);
    }

    [SkippableFact]
    public void TranslatesStringSubstring()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringSubstringCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringSubstringCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (substr s 0 5) - substring from index 0, length 5
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Substring,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new IntLiteralNode(TextSpan.Empty, 0),
                new IntLiteralNode(TextSpan.Empty, 5)
            });

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void TranslatesStringSubstringFrom()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringSubstringFromCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringSubstringFromCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (substr s 3) - substring from index 3 to end
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.SubstringFrom,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new IntLiteralNode(TextSpan.Empty, 3)
            });

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void TranslatesStringReplace()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesStringReplaceCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesStringReplaceCore()
    {
        // W1 Slice 1 (T1/D9): Replace is REFUSED — Z3's MkReplace substitutes the
        // first occurrence while .NET substitutes all occurrences; proving through
        // that divergence could elide a runtime guard the program needs.
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (replace s "old" "new")
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Replace,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "old"),
                new StringLiteralNode(TextSpan.Empty, "new")
            });

        var result = translator.Translate(expr);

        Assert.Null(result);
        Assert.Contains("first", translator.LastRefusalReason);
        Assert.DoesNotContain(StringOp.Replace, ModeledForms.StringOperations);
    }

    // ===========================================
    // Array Theory Tests
    // ===========================================

    [SkippableFact]
    public void TranslatesArrayLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesArrayLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesArrayLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // (len arr) - ArrayLengthNode
        var expr = new ArrayLengthNode(
            TextSpan.Empty,
            new ReferenceNode(TextSpan.Empty, "arr"));

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        // Array length returns a 32-bit unsigned bit-vector
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
        var bvExpr = (Microsoft.Z3.BitVecExpr)result;
        Assert.Equal(32u, bvExpr.SortSize);

        // Verify the length variable was created
        Assert.True(translator.Variables.ContainsKey("arr$length"));
    }

    [SkippableFact]
    public void DeclareArrayVariableCreatesLengthVariable()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DeclareArrayVariableCreatesLengthVariableCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DeclareArrayVariableCreatesLengthVariableCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var declared = translator.DeclareArrayVariable("arr", "i32");
        Assert.True(declared);

        // Array variable should exist
        Assert.True(translator.Variables.ContainsKey("arr"));
        Assert.IsType<Microsoft.Z3.ArrayExpr>(translator.Variables["arr"].Expr);

        // Length variable should exist
        Assert.True(translator.Variables.ContainsKey("arr$length"));
        var lengthExpr = translator.Variables["arr$length"].Expr;
        Assert.IsType<Microsoft.Z3.BitVecExpr>(lengthExpr);
        Assert.Equal(32u, ((Microsoft.Z3.BitVecExpr)lengthExpr).SortSize);
    }

    [SkippableFact]
    public void ArrayAccessCreatesLengthVariableOnDemand()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ArrayAccessCreatesLengthVariableOnDemandCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ArrayAccessCreatesLengthVariableOnDemandCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("i", "i32");

        // Access array element without declaring the array first
        // arr{i}
        var expr = new ArrayAccessNode(
            TextSpan.Empty,
            new ReferenceNode(TextSpan.Empty, "arr"),
            new ReferenceNode(TextSpan.Empty, "i"));

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        // Array should have been created on-demand
        Assert.True(translator.Variables.ContainsKey("arr"));
        // Length variable should also have been created
        Assert.True(translator.Variables.ContainsKey("arr$length"));
    }

    // ===========================================
    // Unsupported String Operation Tests
    // ===========================================

    [SkippableFact]
    public void ReturnsNullForStringToUpper()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForStringToUpperCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForStringToUpperCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (upper s) - not supported by Z3
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.ToUpper,
            new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void ReturnsNullForStringToLower()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForStringToLowerCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForStringToLowerCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (lower s) - not supported by Z3
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.ToLower,
            new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void ReturnsNullForStringTrim()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForStringTrimCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForStringTrimCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (trim s) - not supported by Z3
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Trim,
            new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void ReturnsNullForStringRegexTest()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForStringRegexTestCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForStringRegexTestCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (regex-test s "\\d+") - not supported
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.RegexTest,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "\\d+")
            });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void ReturnsNullForStringSplit()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForStringSplitCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForStringSplitCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (split s ",") - not supported (returns array)
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Split,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, ",")
            });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void ReturnsNullForStringIsNullOrWhiteSpace()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForStringIsNullOrWhiteSpaceCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForStringIsNullOrWhiteSpaceCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (isblank s) - not supported (requires whitespace theory)
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.IsNullOrWhiteSpace,
            new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    // ===========================================
    // String Edge Case Tests
    // ===========================================

    [SkippableFact]
    public void TranslatesContainsWithEmptySearchString()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesContainsWithEmptySearchStringCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesContainsWithEmptySearchStringCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (contains s "") - should translate successfully
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Contains,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "")
            });

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesReplaceWithEmptyStrings()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesReplaceWithEmptyStringsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesReplaceWithEmptyStringsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (replace s "" "x") — Replace is refused since W1 Slice 1 (T1/D9),
        // regardless of arguments: the first-vs-all-occurrence divergence is a
        // property of the operation, not of specific inputs.
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Replace,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, ""),
                new StringLiteralNode(TextSpan.Empty, "x")
            });

        var result = translator.Translate(expr);

        Assert.Null(result);
        Assert.Contains("Replace", translator.LastRefusalReason);
    }

    [SkippableFact]
    public void TranslatesIndexOfWithEmptySearchString()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesIndexOfWithEmptySearchStringCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesIndexOfWithEmptySearchStringCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (indexof s "") - should translate successfully
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.IndexOf,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "")
            });

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
    }

    [SkippableFact]
    public void TranslatesSubstringWithZeroLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesSubstringWithZeroLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesSubstringWithZeroLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (substr s 0 0) - zero-length substring
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Substring,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new IntLiteralNode(TextSpan.Empty, 0),
                new IntLiteralNode(TextSpan.Empty, 0)
            });

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void TranslatesArrayWithDifferentElementTypes()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesArrayWithDifferentElementTypesCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesArrayWithDifferentElementTypesCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // Declare arrays with different element types
        Assert.True(translator.DeclareVariable("arr8", "i8[]"));
        Assert.True(translator.DeclareVariable("arr16", "i16[]"));
        Assert.True(translator.DeclareVariable("arr32", "i32[]"));
        Assert.True(translator.DeclareVariable("arr64", "i64[]"));
        Assert.True(translator.DeclareVariable("arru8", "u8[]"));
        Assert.True(translator.DeclareVariable("arru64", "u64[]"));

        // Verify all arrays were created with length variables
        Assert.True(translator.Variables.ContainsKey("arr8$length"));
        Assert.True(translator.Variables.ContainsKey("arr16$length"));
        Assert.True(translator.Variables.ContainsKey("arr32$length"));
        Assert.True(translator.Variables.ContainsKey("arr64$length"));
        Assert.True(translator.Variables.ContainsKey("arru8$length"));
        Assert.True(translator.Variables.ContainsKey("arru64$length"));
    }

    // ===========================================
    // Diagnostic Tests
    // ===========================================

    [SkippableFact]
    public void DiagnosesUnsupportedFloatType()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesUnsupportedFloatTypeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesUnsupportedFloatTypeCore()
    {
        var diagnostic = ContractTranslator.DiagnoseUnsupportedType("float");

        Assert.NotNull(diagnostic);
        Assert.Contains("float", diagnostic);
        Assert.Contains("not supported", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesUnsupportedDoubleType()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesUnsupportedDoubleTypeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesUnsupportedDoubleTypeCore()
    {
        var diagnostic = ContractTranslator.DiagnoseUnsupportedType("double");

        Assert.NotNull(diagnostic);
        Assert.Contains("double", diagnostic);
        Assert.Contains("floating-point", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesFloatingPointLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesFloatingPointLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesFloatingPointLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new FloatLiteralNode(TextSpan.Empty, 3.14);
        var result = translator.Translate(expr);
        Assert.Null(result);

        var diagnostic = translator.DiagnoseTranslationFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("Floating-point", diagnostic);
        Assert.Contains("not supported", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesFunctionCall()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesFunctionCallCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesFunctionCallCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new CallExpressionNode(
            TextSpan.Empty,
            "customFunc",
            new List<ExpressionNode> { new IntLiteralNode(TextSpan.Empty, 42) });

        var result = translator.Translate(expr);
        Assert.Null(result);

        var diagnostic = translator.DiagnoseTranslationFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("customFunc", diagnostic);
        Assert.Contains("not supported", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesUnknownVariable()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesUnknownVariableCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesUnknownVariableCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new ReferenceNode(TextSpan.Empty, "unknownVar");
        var result = translator.Translate(expr);
        Assert.Null(result);

        var diagnostic = translator.DiagnoseTranslationFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("unknownVar", diagnostic);
        Assert.Contains("Unknown variable", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesUnsupportedStringOperation()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesUnsupportedStringOperationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesUnsupportedStringOperationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.ToUpper,
            new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") });

        var result = translator.Translate(expr);
        Assert.Null(result);

        var diagnostic = translator.DiagnoseTranslationFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("ToUpper", diagnostic);
        Assert.Contains("not supported", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesTypeMismatchInArithmeticOp()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesTypeMismatchInArithmeticOpCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesTypeMismatchInArithmeticOpCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("b", "bool");

        // b + 1 (boolean + integer should fail)
        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Add,
            new ReferenceNode(TextSpan.Empty, "b"),
            new IntLiteralNode(TextSpan.Empty, 1));

        var result = translator.Translate(expr);
        Assert.Null(result);

        var diagnostic = translator.DiagnoseTranslationFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("Add", diagnostic);
        Assert.Contains("integer operands", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesTypeMismatchInLogicalOp()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesTypeMismatchInLogicalOpCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesTypeMismatchInLogicalOpCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("x", "i32");

        // x && 1 (integer && integer should fail - needs bool)
        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.And,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 1));

        var result = translator.Translate(expr);
        Assert.Null(result);

        var diagnostic = translator.DiagnoseTranslationFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("And", diagnostic);
        Assert.Contains("boolean operands", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesNestedFailure()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesNestedFailureCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesNestedFailureCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("x", "i32");

        // x + unknownVar (nested unknown variable)
        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Add,
            new ReferenceNode(TextSpan.Empty, "x"),
            new ReferenceNode(TextSpan.Empty, "unknownVar"));

        var result = translator.Translate(expr);
        Assert.Null(result);

        var diagnostic = translator.DiagnoseTranslationFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("unknownVar", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesUnsupportedTypeInQuantifier()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesUnsupportedTypeInQuantifierCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesUnsupportedTypeInQuantifierCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // (forall ((x f32)) (>= x 0)) - f32 is unsupported
        var forallExpr = new ForallExpressionNode(
            TextSpan.Empty,
            new List<QuantifierVariableNode>
            {
                new QuantifierVariableNode(TextSpan.Empty, "x", "f32")
            },
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)));

        var result = translator.TranslateBoolExpr(forallExpr);
        Assert.Null(result);

        var diagnostic = translator.DiagnoseTranslationFailure(forallExpr);

        Assert.NotNull(diagnostic);
        Assert.Contains("f32", diagnostic);
        Assert.Contains("Unsupported type", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesBoolExprFailureForNonBooleanExpression()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesBoolExprFailureForNonBooleanExpressionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesBoolExprFailureForNonBooleanExpressionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("x", "i32");

        // x + 1 is an integer expression, not boolean
        var expr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Add,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 1));

        // Translate succeeds (returns BitVecExpr)
        var result = translator.Translate(expr);
        Assert.NotNull(result);

        // TranslateBoolExpr fails (not a BoolExpr)
        var boolResult = translator.TranslateBoolExpr(expr);
        Assert.Null(boolResult);

        // DiagnoseBoolExprFailure explains why
        var diagnostic = translator.DiagnoseBoolExprFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("must be boolean", diagnostic);
        Assert.Contains("BitVec", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesConditionalTypeMismatch()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesConditionalTypeMismatchCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesConditionalTypeMismatchCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("p", "bool");
        translator.DeclareVariable("x", "i32");
        translator.DeclareVariable("s", "string");

        // (if p x s) - branches have incompatible types (BitVecExpr vs SeqExpr)
        var expr = new ConditionalExpressionNode(
            TextSpan.Empty,
            new ReferenceNode(TextSpan.Empty, "p"),
            new ReferenceNode(TextSpan.Empty, "x"),
            new ReferenceNode(TextSpan.Empty, "s"));

        var diagnostic = translator.DiagnoseTranslationFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("incompatible types", diagnostic);
    }

    [SkippableFact]
    public void DiagnosesComplexArrayExpression()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DiagnosesComplexArrayExpressionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DiagnosesComplexArrayExpressionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // arr{0}{1} - nested array access is not supported
        var expr = new ArrayAccessNode(
            TextSpan.Empty,
            new ArrayAccessNode(
                TextSpan.Empty,
                new ReferenceNode(TextSpan.Empty, "arr"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            new IntLiteralNode(TextSpan.Empty, 1));

        var result = translator.Translate(expr);
        Assert.Null(result);

        var diagnostic = translator.DiagnoseTranslationFailure(expr);

        Assert.NotNull(diagnostic);
        Assert.Contains("simple variable reference", diagnostic);
        Assert.Contains("computed array expressions", diagnostic);
    }

    // ===========================================
    // Edge Case Tests
    // ===========================================

    [SkippableFact]
    public void TranslatesUnicodeStringLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesUnicodeStringLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesUnicodeStringLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // Unicode string with various characters
        var expr = new StringLiteralNode(TextSpan.Empty, "Hello 世界 🌍 αβγ");

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void TranslatesEscapeSequencesInString()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesEscapeSequencesInStringCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesEscapeSequencesInStringCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // String with escape sequences
        var expr = new StringLiteralNode(TextSpan.Empty, "Line1\nLine2\tTabbed\\Backslash\"Quote");

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void TranslatesDeeplyNestedArithmeticExpression()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesDeeplyNestedArithmeticExpressionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesDeeplyNestedArithmeticExpressionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("x", "i32");

        // Build deeply nested expression: ((((x + 1) + 1) + 1) + 1) ... 20 levels
        ExpressionNode expr = new ReferenceNode(TextSpan.Empty, "x");
        for (int i = 0; i < 20; i++)
        {
            expr = new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Add,
                expr,
                new IntLiteralNode(TextSpan.Empty, 1));
        }

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BitVecExpr>(result);
    }

    [SkippableFact]
    public void TranslatesDeeplyNestedLogicalExpression()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesDeeplyNestedLogicalExpressionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesDeeplyNestedLogicalExpressionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("p", "bool");

        // Build deeply nested expression: (((p && p) && p) && p) ... 20 levels
        ExpressionNode expr = new ReferenceNode(TextSpan.Empty, "p");
        for (int i = 0; i < 20; i++)
        {
            expr = new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.And,
                expr,
                new ReferenceNode(TextSpan.Empty, "p"));
        }

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesDeeplyNestedStringConcat()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesDeeplyNestedStringConcatCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesDeeplyNestedStringConcatCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // Build concat with many arguments
        var args = new List<ExpressionNode>();
        for (int i = 0; i < 10; i++)
        {
            args.Add(new ReferenceNode(TextSpan.Empty, "s"));
        }

        var expr = new StringOperationNode(TextSpan.Empty, StringOp.Concat, args);

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void ReturnsNullForStringLengthWithNoArgs()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForStringLengthWithNoArgsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForStringLengthWithNoArgsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // (len) with no arguments
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Length,
            new List<ExpressionNode>());

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void ReturnsNullForContainsWithOneArg()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForContainsWithOneArgCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForContainsWithOneArgCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (contains s) with only one argument
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Contains,
            new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void ReturnsNullForSubstringWithTwoArgs()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForSubstringWithTwoArgsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForSubstringWithTwoArgsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (substr s 0) with only two arguments - Substring requires 3
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Substring,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new IntLiteralNode(TextSpan.Empty, 0)
            });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void ReturnsNullForConcatWithOneArg()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForConcatWithOneArgCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForConcatWithOneArgCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (concat s) with only one argument
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Concat,
            new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") });

        var result = translator.Translate(expr);

        Assert.Null(result);
    }

    [SkippableFact]
    public void TranslatesArrayAccessWithNegativeIndex()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesArrayAccessWithNegativeIndexCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesArrayAccessWithNegativeIndexCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("i", "i32");
        translator.DeclareArrayVariable("arr", "i32");

        // arr{-1} - negative index is valid in Z3 (no bounds checking)
        var expr = new ArrayAccessNode(
            TextSpan.Empty,
            new ReferenceNode(TextSpan.Empty, "arr"),
            new IntLiteralNode(TextSpan.Empty, -1));

        var result = translator.Translate(expr);

        // Should translate (no bounds checking in Z3 array theory)
        Assert.NotNull(result);
    }

    [SkippableFact]
    public void TranslatesEmptyStringLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesEmptyStringLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesEmptyStringLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new StringLiteralNode(TextSpan.Empty, "");

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void TranslatesVeryLongStringLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesVeryLongStringLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesVeryLongStringLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // 1000 character string
        var longString = new string('a', 1000);
        var expr = new StringLiteralNode(TextSpan.Empty, longString);

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.SeqExpr>(result);
    }

    [SkippableFact]
    public void TranslatesMaxIntLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesMaxIntLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesMaxIntLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new IntLiteralNode(TextSpan.Empty, int.MaxValue);

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BitVecNum>(result);
    }

    [SkippableFact]
    public void TranslatesMinIntLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatesMinIntLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatesMinIntLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new IntLiteralNode(TextSpan.Empty, int.MinValue);

        var result = translator.Translate(expr);

        Assert.NotNull(result);
        Assert.IsType<Microsoft.Z3.BitVecNum>(result);
    }

    // ===========================================
    // Warnings Tests
    // ===========================================

    /// <summary>
    /// Divergence D4, closed by refusal. This test previously pinned the OPPOSITE behaviour —
    /// translate as ordinal and emit a warning — and that is exactly the defect: a warning does
    /// not stop <c>Proven</c>, and <c>Proven &amp;&amp; !IsVacuous</c> ELIDES the runtime check.
    /// Reproduced end-to-end before the fix: the same program threw ContractViolationException
    /// without <c>--verify</c> and ran clean with it.
    /// </summary>
    [SkippableFact]
    public void RefusesIgnoreCaseComparisonMode()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        RefusesIgnoreCaseComparisonModeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RefusesIgnoreCaseComparisonModeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (contains s "hello" :ignore-case) - comparison mode will be ignored
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Contains,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "hello")
            },
            StringComparisonMode.IgnoreCase);

        var result = translator.TranslateBoolExpr(expr);

        // Translation is REFUSED — not approximated as ordinal.
        Assert.Null(result);
        Assert.NotNull(translator.LastRefusalReason);
        Assert.Contains("IgnoreCase", translator.LastRefusalReason);

        // And the whitelist agrees with the translator: a mode-bearing form is outside the
        // modeled surface. The two disagreeing about what is modeled IS divergence D4.
        Assert.False(ModeledForms.TryValidate(expr, out var offending));
        Assert.Contains("IgnoreCase", offending);
    }

    [SkippableFact]
    public void NoWarningForOrdinalComparisonMode()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NoWarningForOrdinalComparisonModeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NoWarningForOrdinalComparisonModeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (contains s "hello" :ordinal) - ordinal is the default, no warning needed
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Contains,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "hello")
            },
            StringComparisonMode.Ordinal);

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
        Assert.Empty(translator.Warnings);
    }

    [SkippableFact]
    public void NoWarningForNoComparisonMode()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NoWarningForNoComparisonModeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NoWarningForNoComparisonModeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // (contains s "hello") - no comparison mode specified
        var expr = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Contains,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "hello")
            });

        var result = translator.TranslateBoolExpr(expr);

        Assert.NotNull(result);
        Assert.Empty(translator.Warnings);
    }

    /// <summary>
    /// Replaces <c>ClearWarningsRemovesAccumulatedWarnings</c>, which used the D4 warn-and-
    /// approximate path as its warning source. That path is gone, and this pins WHY: after D4
    /// was closed the translator has <b>no warning producers at all</b>. The mechanism survives
    /// only because <c>Z3Verifier</c> plumbs it into <c>ContractVerificationResult.Warnings</c>.
    ///
    /// <para>The rule this defends: a modeling divergence may not be answered with a warning.
    /// A warning does not stop <c>Proven</c>, and <c>Proven &amp;&amp; !IsVacuous</c> elides the
    /// runtime check — so the warning is emitted into a build whose check has been deleted, which
    /// is precisely how D4 shipped a program whose postcondition was false at runtime. If a future
    /// change adds a producer here, that change must show the warning is NOT soundness-relevant.</para>
    /// </summary>
    [SkippableFact]
    public void TranslatorHasNoWarningProducers_SoundnessDivergencesAreRefusedNotWarned()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatorHasNoWarningProducersCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatorHasNoWarningProducersCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareVariable("s", "string");

        // The form that used to warn. It is now refused, and refusal is NOT a warning.
        var ignoreCase = new StringOperationNode(
            TextSpan.Empty,
            StringOp.Contains,
            new List<ExpressionNode>
            {
                new ReferenceNode(TextSpan.Empty, "s"),
                new StringLiteralNode(TextSpan.Empty, "hello")
            },
            StringComparisonMode.IgnoreCase);

        Assert.Null(translator.TranslateBoolExpr(ignoreCase));
        Assert.Empty(translator.Warnings);

        // Nor does the source-level rule "warn" — no producer exists anywhere in the type.
        var source = File.ReadAllText(ContractTranslatorSourcePath);
        Assert.DoesNotContain("_warnings.Add", source);
    }

    /// <summary>Resolved from the test assembly's location so the check is not CWD-dependent.</summary>
    private static string ContractTranslatorSourcePath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Calor.Compiler")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "src", "Calor.Compiler",
                "Verification", "Z3", "ContractTranslator.cs");
        }
    }
}
