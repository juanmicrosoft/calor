using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Xunit;
using System.Runtime.CompilerServices;

namespace Calor.Verification.Tests;

/// <summary>
/// Tests for the Z3ImplicationProver that proves contract implications for LSP enforcement.
/// All tests skip if Z3 is not available on the system.
/// </summary>
public class ImplicationProverTests
{
    #region Arithmetic Implication Tests

    [SkippableFact]
    public void Proves_ArithmeticImplication_GreaterOrEqual_Implies_GreaterThan()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Proves_ArithmeticImplication_GreaterOrEqual_Implies_GreaterThan_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Proves_ArithmeticImplication_GreaterOrEqual_Implies_GreaterThan_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // (>= x 1) → (> x 0) should be PROVEN
        // Because if x >= 1, then x is at least 1, which is > 0
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterOrEqual,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 1));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void Proves_ConjunctionImpliesConjunct()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Proves_ConjunctionImpliesConjunct_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Proves_ConjunctionImpliesConjunct_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // (&& (> x 0) (< x 100)) → (> x 0) should be PROVEN
        // Because if (x > 0 AND x < 100), then x > 0 is true
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.And,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 100)));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void Proves_EqualityImplication()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Proves_EqualityImplication_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Proves_EqualityImplication_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // (== x 5) → (> x 0) should be PROVEN
        // Because if x == 5, then x > 0 is true
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Equal,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 5));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Proven, result.Status);
    }

    #endregion

    #region Disproven Implication Tests

    [SkippableFact]
    public void Disproves_WeakerDoesNotImplyStronger()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Disproves_WeakerDoesNotImplyStronger_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Disproves_WeakerDoesNotImplyStronger_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // (> x 0) → (> x 10) should be DISPROVEN
        // Because x=5 satisfies x > 0 but not x > 10
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 10));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Disproven, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
    }

    [SkippableFact]
    public void Disproves_StrongerPrecondition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Disproves_StrongerPrecondition_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Disproves_StrongerPrecondition_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // (>= x 0) → (> x 5) should be DISPROVEN
        // Because x=0 satisfies x >= 0 but not x > 5
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterOrEqual,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 5));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Disproven, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
    }

    [SkippableFact]
    public void Disproves_DisjunctDoesNotImplyConjunction()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Disproves_DisjunctDoesNotImplyConjunction_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Disproves_DisjunctDoesNotImplyConjunction_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // (> x 0) → (&& (> x 0) (< x 100)) should be DISPROVEN
        // Because x=1000 satisfies x > 0 but not (x > 0 AND x < 100)
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.And,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 100)));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Disproven, result.Status);
    }

    #endregion

    #region LSP Checking Tests

    [SkippableFact]
    public void CheckPreconditionWeakening_ValidWeakening()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CheckPreconditionWeakening_ValidWeakening_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckPreconditionWeakening_ValidWeakening_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // Interface: (>= x 0)
        // Implementer: (>= x -10) - weaker (accepts more) - VALID
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var interfacePrecondition = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterOrEqual,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var implementerPrecondition = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterOrEqual,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, -10));

        var result = prover.CheckPreconditionWeakening(parameters, interfacePrecondition, implementerPrecondition);

        Assert.Equal(ImplicationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void CheckPreconditionWeakening_InvalidStrengthening()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CheckPreconditionWeakening_InvalidStrengthening_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckPreconditionWeakening_InvalidStrengthening_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // Interface: (>= x 0)
        // Implementer: (>= x 10) - stronger (rejects valid inputs) - INVALID
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var interfacePrecondition = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterOrEqual,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var implementerPrecondition = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterOrEqual,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 10));

        var result = prover.CheckPreconditionWeakening(parameters, interfacePrecondition, implementerPrecondition);

        Assert.Equal(ImplicationStatus.Disproven, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
    }

    [SkippableFact]
    public void CheckPostconditionStrengthening_ValidStrengthening()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CheckPostconditionStrengthening_ValidStrengthening_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckPostconditionStrengthening_ValidStrengthening_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // Interface: (> result 0)
        // Implementer: (>= result 10) - stronger (guarantees more) - VALID
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var interfacePostcondition = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "result"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var implementerPostcondition = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterOrEqual,
            new ReferenceNode(TextSpan.Empty, "result"),
            new IntLiteralNode(TextSpan.Empty, 10));

        var result = prover.CheckPostconditionStrengthening(
            parameters,
            "i32",
            interfacePostcondition,
            implementerPostcondition);

        Assert.Equal(ImplicationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void CheckPostconditionStrengthening_InvalidWeakening()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CheckPostconditionStrengthening_InvalidWeakening_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckPostconditionStrengthening_InvalidWeakening_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // Interface: (> result 10)
        // Implementer: (> result 0) - weaker (guarantees less) - INVALID
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var interfacePostcondition = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "result"),
            new IntLiteralNode(TextSpan.Empty, 10));

        var implementerPostcondition = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "result"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var result = prover.CheckPostconditionStrengthening(
            parameters,
            "i32",
            interfacePostcondition,
            implementerPostcondition);

        Assert.Equal(ImplicationStatus.Disproven, result.Status);
    }

    #endregion

    #region Unsupported Construct Tests

    [SkippableFact]
    public void Returns_Unsupported_ForStrings()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Returns_Unsupported_ForStrings_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Returns_Unsupported_ForStrings_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // String contracts are unsupported
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.NotEqual,
            new ReferenceNode(TextSpan.Empty, "s"),
            new StringLiteralNode(TextSpan.Empty, ""));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.NotEqual,
            new ReferenceNode(TextSpan.Empty, "s"),
            new StringLiteralNode(TextSpan.Empty, ""));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Unsupported, result.Status);
    }

    [SkippableFact]
    public void Returns_Unsupported_ForFloats()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Returns_Unsupported_ForFloats_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Returns_Unsupported_ForFloats_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // Float contracts are unsupported
        var parameters = new List<(string Name, string Type)> { ("f", "f32") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "f"),
            new FloatLiteralNode(TextSpan.Empty, 0.0));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "f"),
            new FloatLiteralNode(TextSpan.Empty, 0.0));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Unsupported, result.Status);
    }

    [SkippableFact]
    public void Returns_Unsupported_ForFunctionCalls()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Returns_Unsupported_ForFunctionCalls_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Returns_Unsupported_ForFunctionCalls_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // Function calls in contracts are unsupported
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new CallExpressionNode(
                TextSpan.Empty,
                "abs",
                new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "x") }),
            new IntLiteralNode(TextSpan.Empty, 0));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new ReferenceNode(TextSpan.Empty, "x"),
            new IntLiteralNode(TextSpan.Empty, 0));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Unsupported, result.Status);
    }

    #endregion

    #region Multi-Parameter Tests

    [SkippableFact]
    public void Proves_MultiParameter_Implication()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Proves_MultiParameter_Implication_Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Proves_MultiParameter_Implication_Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);

        // (&& (> x 0) (> y 0)) → (> (+ x y) 0) should be PROVEN
        var parameters = new List<(string Name, string Type)> { ("x", "i32"), ("y", "i32") };

        var antecedent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.And,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new ReferenceNode(TextSpan.Empty, "y"),
                new IntLiteralNode(TextSpan.Empty, 0)));

        var consequent = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.GreaterThan,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Add,
                new ReferenceNode(TextSpan.Empty, "x"),
                new ReferenceNode(TextSpan.Empty, "y")),
            new IntLiteralNode(TextSpan.Empty, 0));

        var result = prover.ProveImplication(parameters, antecedent, consequent);

        Assert.Equal(ImplicationStatus.Proven, result.Status);
    }

    #endregion
}
