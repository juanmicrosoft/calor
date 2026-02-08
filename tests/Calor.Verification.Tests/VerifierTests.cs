using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Xunit;
using System.Runtime.CompilerServices;

namespace Calor.Verification.Tests;

/// <summary>
/// Tests for the Z3Verifier that proves or disproves contracts.
/// All tests skip if Z3 is not available on the system.
/// </summary>
public class VerifierTests
{
    [SkippableFact]
    public void ProvesSquareNonNegative()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesSquareNonNegativeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesSquareNonNegativeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: x >= 0
        // Postcondition: x * x >= 0
        // This should be PROVEN because squares of non-negative numbers are non-negative

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Multiply,
                    new ReferenceNode(TextSpan.Empty, "result"),
                    new ReferenceNode(TextSpan.Empty, "result")),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void ProvesAdditionCommutative()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesAdditionCommutativeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesAdditionCommutativeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: a + b == b + a
        // This should be PROVEN

        var parameters = new List<(string Name, string Type)>
        {
            ("a", "i32"),
            ("b", "i32")
        };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    new ReferenceNode(TextSpan.Empty, "a"),
                    new ReferenceNode(TextSpan.Empty, "b")),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    new ReferenceNode(TextSpan.Empty, "b"),
                    new ReferenceNode(TextSpan.Empty, "a"))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void ProvesDivisorCheck()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesDivisorCheckCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesDivisorCheckCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: b != 0
        // Postcondition: b != 0
        // This is a tautology when precondition is assumed

        var parameters = new List<(string Name, string Type)>
        {
            ("a", "i32"),
            ("b", "i32")
        };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.NotEqual,
                new ReferenceNode(TextSpan.Empty, "b"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.NotEqual,
                new ReferenceNode(TextSpan.Empty, "b"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void DisprovesInvalidDiv()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesInvalidDivCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesInvalidDivCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: b != 0
        // Postcondition: a / b > a
        // This is FALSE for counterexample: a=0, b=1 (0/1=0, not > 0)

        var parameters = new List<(string Name, string Type)>
        {
            ("a", "i32"),
            ("b", "i32")
        };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.NotEqual,
                new ReferenceNode(TextSpan.Empty, "b"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Divide,
                    new ReferenceNode(TextSpan.Empty, "a"),
                    new ReferenceNode(TextSpan.Empty, "b")),
                new ReferenceNode(TextSpan.Empty, "a")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
    }

    [SkippableFact]
    public void DisprovesOverflow()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesOverflowCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesOverflowCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: x + 1 > x + 2
        // This is always FALSE

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    new ReferenceNode(TextSpan.Empty, "x"),
                    new IntLiteralNode(TextSpan.Empty, 1)),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    new ReferenceNode(TextSpan.Empty, "x"),
                    new IntLiteralNode(TextSpan.Empty, 2))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void UnsupportedFunctionCall()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        UnsupportedFunctionCallCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UnsupportedFunctionCallCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: strlen(s) > 0
        // This should be UNSUPPORTED because we can't translate function calls

        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new CallExpressionNode(
                    TextSpan.Empty,
                    "strlen",
                    new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") }),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [SkippableFact]
    public void PreconditionSatisfiabilityCheck()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        PreconditionSatisfiabilityCheckCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PreconditionSatisfiabilityCheckCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: x >= 0
        // This should be satisfiable (x=0, x=1, etc.)

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPrecondition(parameters, precondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void UnsatisfiablePrecondition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        UnsatisfiablePreconditionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UnsatisfiablePreconditionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: x > 0 AND x < 0
        // This should be DISPROVEN (unsatisfiable)

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
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
                    new IntLiteralNode(TextSpan.Empty, 0))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPrecondition(parameters, precondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }
}
