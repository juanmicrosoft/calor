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
    public void ReturnsNullForStringLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsNullForStringLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsNullForStringLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        var expr = new StringLiteralNode(TextSpan.Empty, "hello");

        var result = translator.Translate(expr);

        Assert.Null(result);
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
}
