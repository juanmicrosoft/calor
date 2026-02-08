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
        Assert.Equal("42", result.ToString());
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
}
