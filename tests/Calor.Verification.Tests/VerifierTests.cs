using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using ProofStatus = Calor.Compiler.Verification.ProofStatus;
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

        // Precondition: x >= 0 AND x <= 46340 (to prevent overflow since 46340^2 < Int32.MaxValue)
        // Postcondition: x * x >= 0 (using x since result is unconstrained)
        // This should be PROVEN because bounded squares of non-negative numbers are non-negative
        // Note: With bit-vector semantics, we need bounds to prevent overflow

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        // x >= 0
        var preconditionLowerBound = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        // x <= 46340 (sqrt(Int32.MaxValue) ≈ 46340)
        var preconditionUpperBound = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 46340)),
            null,
            new AttributeCollection());

        // Postcondition: x * x >= 0 (bounded x so no overflow)
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Multiply,
                    new ReferenceNode(TextSpan.Empty, "x"),
                    new ReferenceNode(TextSpan.Empty, "x")),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { preconditionLowerBound, preconditionUpperBound },
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

    [SkippableFact]
    public void DisprovesUnboundedOverflow()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesUnboundedOverflowCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesUnboundedOverflowCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: x + 1 > x
        // This should be DISPROVEN because x could be Int32.MaxValue (2147483647)
        // and x + 1 would wrap to Int32.MinValue (-2147483648)

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
                new ReferenceNode(TextSpan.Empty, "x")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
    }

    [SkippableFact]
    public void ProvesBoundedOverflow()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesBoundedOverflowCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesBoundedOverflowCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: x < 2147483647 (Int32.MaxValue)
        // Postcondition: x + 1 > x
        // This should be PROVEN because overflow is prevented by precondition

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 2147483647)),
            null,
            new AttributeCollection());

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
                new ReferenceNode(TextSpan.Empty, "x")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // 64-bit Type Tests
    // ===========================================

    [SkippableFact]
    public void Proves64BitAdditionCommutative()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Proves64BitAdditionCommutativeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Proves64BitAdditionCommutativeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: a + b == b + a (with i64 types)
        var parameters = new List<(string Name, string Type)>
        {
            ("a", "i64"),
            ("b", "i64")
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
            "i64",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // Unsigned Type Tests
    // ===========================================

    [SkippableFact]
    public void ProvesUnsignedNonNegative()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesUnsignedNonNegativeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesUnsignedNonNegativeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: x >= 0 for unsigned type
        // This should be PROVEN because unsigned values are always >= 0
        var parameters = new List<(string Name, string Type)> { ("x", "u32") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "u32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void DisprovesSignedAlwaysNonNegative()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesSignedAlwaysNonNegativeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesSignedAlwaysNonNegativeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: x >= 0 for signed type
        // This should be DISPROVEN because signed values can be negative
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    // ===========================================
    // Edge Case Tests
    // ===========================================

    [SkippableFact]
    public void ProvesNegativeNumberComparison()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesNegativeNumberComparisonCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesNegativeNumberComparisonCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: x < 0
        // Postcondition: x < 1
        // This should be PROVEN because all negative numbers are less than 1
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 1)),
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
    public void DetectsMultiplicationOverflow()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DetectsMultiplicationOverflowCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DetectsMultiplicationOverflowCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: x > 0
        // Postcondition: x * 2 > x
        // This should be DISPROVEN because large positive values overflow when multiplied by 2
        // For example: 0x7FFFFFFF * 2 = 0xFFFFFFFE = -2 (signed), which is not > x
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new ReferenceNode(TextSpan.Empty, "x"),
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
                    BinaryOperator.Multiply,
                    new ReferenceNode(TextSpan.Empty, "x"),
                    new IntLiteralNode(TextSpan.Empty, 2)),
                new ReferenceNode(TextSpan.Empty, "x")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void ProvesSubtractionWithBounds()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesSubtractionWithBoundsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesSubtractionWithBoundsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: x > 0
        // Postcondition: x - 1 >= 0
        // This should be PROVEN because positive numbers minus 1 are still >= 0
        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
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
                    BinaryOperator.Subtract,
                    new ReferenceNode(TextSpan.Empty, "x"),
                    new IntLiteralNode(TextSpan.Empty, 1)),
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
    public void DetectsIntMinDivisionOverflow()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DetectsIntMinDivisionOverflowCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DetectsIntMinDivisionOverflowCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // INT_MIN / -1 is a special case that causes overflow in signed division
        // INT_MIN = -2147483648, and -2147483648 / -1 = 2147483648 which overflows to -2147483648
        //
        // Precondition: y != 0 (to avoid division by zero)
        // Postcondition: x / y >= x (false for x = INT_MIN, y = -1)
        // This should be DISPROVEN

        var parameters = new List<(string Name, string Type)>
        {
            ("x", "i32"),
            ("y", "i32")
        };

        // y != 0
        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.NotEqual,
                new ReferenceNode(TextSpan.Empty, "y"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        // x / y >= x (this fails for INT_MIN / -1 because result overflows back to INT_MIN)
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Divide,
                    new ReferenceNode(TextSpan.Empty, "x"),
                    new ReferenceNode(TextSpan.Empty, "y")),
                new ReferenceNode(TextSpan.Empty, "x")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { precondition },
            postcondition);

        // This should be disproven - there exist counterexamples
        // (e.g., x=1, y=2 gives 0 >= 1 which is false)
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
    }

    [SkippableFact]
    public void HandlesIntMinDivisionEdgeCase()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        HandlesIntMinDivisionEdgeCaseCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void HandlesIntMinDivisionEdgeCaseCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Test that INT_MIN / -1 = INT_MIN (overflow wraps)
        // Precondition: x == -2147483648 AND y == -1
        // Postcondition: x / y == x (because overflow wraps back to INT_MIN)
        // This should be PROVEN with bit-vector semantics

        var parameters = new List<(string Name, string Type)>
        {
            ("x", "i32"),
            ("y", "i32")
        };

        // x == INT_MIN (-2147483648)
        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, -2147483648)),
            null,
            new AttributeCollection());

        // y == -1
        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new ReferenceNode(TextSpan.Empty, "y"),
                new IntLiteralNode(TextSpan.Empty, -1)),
            null,
            new AttributeCollection());

        // x / y == x (INT_MIN / -1 overflows to INT_MIN in two's complement)
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Divide,
                    new ReferenceNode(TextSpan.Empty, "x"),
                    new ReferenceNode(TextSpan.Empty, "y")),
                new ReferenceNode(TextSpan.Empty, "x")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { precondition1, precondition2 },
            postcondition);

        // W1 Slice 1 (#833 C4): bit-vector division wraps INT_MIN / -1 to
        // INT_MIN, but the emitted runtime check `x / y == x` THROWS
        // OverflowException at exactly this state (C# division overflow throws
        // in checked and unchecked contexts alike). A plain Proven would elide
        // a throwing check; the proof holds only under the overflow side
        // condition — which these §Q violate — so: Assumed, never elides.
        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
        Assert.Contains(Z3Verifier.ContractExpressionDivisionAssumption,
            result.EffectiveOutcome.Assumptions);
    }

    // ===========================================
    // String Theory Verification Tests
    // ===========================================

    [SkippableFact]
    public void ProvesStringLengthNonNegative()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesStringLengthNonNegativeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesStringLengthNonNegativeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (>= (len s) 0)
        // Should be PROVEN because string length is always >= 0 (unsigned)
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") }),
                new IntLiteralNode(TextSpan.Empty, 0)),
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
    public void ProvesEmptyStringHasZeroLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesEmptyStringHasZeroLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesEmptyStringHasZeroLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: (isempty s)
        // Postcondition: (== (len s) 0)
        // Should be PROVEN because empty string has length 0
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.IsNullOrEmpty,
                new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") }),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") }),
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
    public void ProvesPrefixImpliesContains()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesPrefixImpliesContainsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesPrefixImpliesContainsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: (starts s "hello")
        // Postcondition: (contains s "hello")
        // Should be PROVEN because a prefix is always contained
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.StartsWith,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "hello")
                }),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.Contains,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "hello")
                }),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void ProvesSuffixImpliesContains()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesSuffixImpliesContainsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesSuffixImpliesContainsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: (ends s "world")
        // Postcondition: (contains s "world")
        // Should be PROVEN because a suffix is always contained
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.EndsWith,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "world")
                }),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.Contains,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "world")
                }),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void ProvesStringEqualsReflexive()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesStringEqualsReflexiveCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesStringEqualsReflexiveCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (equals s s)
        // Should be PROVEN because equality is reflexive
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.Equals,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new ReferenceNode(TextSpan.Empty, "s")
                }),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // Array Theory Verification Tests
    // ===========================================

    [SkippableFact]
    public void ProvesArrayLengthNonNegative()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesArrayLengthNonNegativeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesArrayLengthNonNegativeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (>= (len arr) 0)
        // Should be PROVEN because array length is unsigned (always >= 0)
        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ArrayLengthNode(
                    TextSpan.Empty,
                    new ReferenceNode(TextSpan.Empty, "arr")),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // Edge Case Tests
    // ===========================================

    [SkippableFact]
    public void ProvesEmptyStringLiteralHasZeroLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesEmptyStringLiteralHasZeroLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesEmptyStringLiteralHasZeroLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (== (len "") 0)
        // Should be PROVEN because empty string literal has length 0
        var parameters = new List<(string Name, string Type)>();

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode> { new StringLiteralNode(TextSpan.Empty, "") }),
                new IntLiteralNode(TextSpan.Empty, 0)),
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
    public void ProvesStringLiteralLengthMatchesActualLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesStringLiteralLengthMatchesActualLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesStringLiteralLengthMatchesActualLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (== (len "hello") 5)
        // Should be PROVEN because "hello" has 5 characters
        var parameters = new List<(string Name, string Type)>();

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode> { new StringLiteralNode(TextSpan.Empty, "hello") }),
                new IntLiteralNode(TextSpan.Empty, 5)),
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
    public void ProvesStringVariableEqualityWithBinaryOperator()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesStringVariableEqualityWithBinaryOperatorCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesStringVariableEqualityWithBinaryOperatorCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: s == s (using binary operator, not StringOp.Equals)
        // Should be PROVEN because equality is reflexive
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new ReferenceNode(TextSpan.Empty, "s"),
                new ReferenceNode(TextSpan.Empty, "s")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void ProvesConcatLengthIsSum()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesConcatLengthIsSumCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesConcatLengthIsSumCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (== (len (concat "ab" "cd")) 4)
        // Should be PROVEN because concat of "ab" and "cd" has length 4
        var parameters = new List<(string Name, string Type)>();

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode>
                    {
                        new StringOperationNode(
                            TextSpan.Empty,
                            StringOp.Concat,
                            new List<ExpressionNode>
                            {
                                new StringLiteralNode(TextSpan.Empty, "ab"),
                                new StringLiteralNode(TextSpan.Empty, "cd")
                            })
                    }),
                new IntLiteralNode(TextSpan.Empty, 4)),
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
    public void ProvesContainsLiteralInLiteral()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesContainsLiteralInLiteralCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesContainsLiteralInLiteralCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (contains "hello world" "world")
        // Should be PROVEN because "hello world" contains "world"
        var parameters = new List<(string Name, string Type)>();

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.Contains,
                new List<ExpressionNode>
                {
                    new StringLiteralNode(TextSpan.Empty, "hello world"),
                    new StringLiteralNode(TextSpan.Empty, "world")
                }),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void DisprovesContainsNotPresent()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesContainsNotPresentCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesContainsNotPresentCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (contains "hello" "xyz")
        // Should be DISPROVEN because "hello" does not contain "xyz"
        var parameters = new List<(string Name, string Type)>();

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.Contains,
                new List<ExpressionNode>
                {
                    new StringLiteralNode(TextSpan.Empty, "hello"),
                    new StringLiteralNode(TextSpan.Empty, "xyz")
                }),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void ProvesArrayLengthConstraintWithAccess()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesArrayLengthConstraintWithAccessCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesArrayLengthConstraintWithAccessCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: (> (len arr) 0)
        // Postcondition: (>= (len arr) 1)
        // Should be PROVEN
        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new ArrayLengthNode(
                    TextSpan.Empty,
                    new ReferenceNode(TextSpan.Empty, "arr")),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ArrayLengthNode(
                    TextSpan.Empty,
                    new ReferenceNode(TextSpan.Empty, "arr")),
                new IntLiteralNode(TextSpan.Empty, 1)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // String Edge Case Verification Tests
    // ===========================================

    [SkippableFact]
    public void ProvesEveryStringContainsEmptyString()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesEveryStringContainsEmptyStringCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesEveryStringContainsEmptyStringCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (contains s "")
        // Should be PROVEN because every string contains the empty string
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.Contains,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "")
                }),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void ProvesIndexOfEmptyStringReturnsZero()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesIndexOfEmptyStringReturnsZeroCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesIndexOfEmptyStringReturnsZeroCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (== (indexof s "") 0)
        // Should be PROVEN because empty string is found at index 0
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.IndexOf,
                    new List<ExpressionNode>
                    {
                        new ReferenceNode(TextSpan.Empty, "s"),
                        new StringLiteralNode(TextSpan.Empty, "")
                    }),
                new IntLiteralNode(TextSpan.Empty, 0)),
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
    public void ProvesZeroLengthSubstringIsEmpty()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesZeroLengthSubstringIsEmptyCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesZeroLengthSubstringIsEmptyCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (== (len (substr s 0 0)) 0)
        // Should be PROVEN because zero-length substring has length 0
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode>
                    {
                        new StringOperationNode(
                            TextSpan.Empty,
                            StringOp.Substring,
                            new List<ExpressionNode>
                            {
                                new ReferenceNode(TextSpan.Empty, "s"),
                                new IntLiteralNode(TextSpan.Empty, 0),
                                new IntLiteralNode(TextSpan.Empty, 0)
                            })
                    }),
                new IntLiteralNode(TextSpan.Empty, 0)),
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
    public void ProvesReplaceEmptyWithCharInsertsAtStart()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesReplaceEmptyWithCharInsertsAtStartCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesReplaceEmptyWithCharInsertsAtStartCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (starts (replace s "" "X") "X")
        // W1 Slice 1 (T1/D9): Replace is un-whitelisted — this obligation is now
        // UNSUPPORTED (runtime check kept) instead of proven through a model whose
        // first-occurrence semantics diverge from .NET's replace-all.
        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.StartsWith,
                new List<ExpressionNode>
                {
                    new StringOperationNode(
                        TextSpan.Empty,
                        StringOp.Replace,
                        new List<ExpressionNode>
                        {
                            new ReferenceNode(TextSpan.Empty, "s"),
                            new StringLiteralNode(TextSpan.Empty, ""),
                            new StringLiteralNode(TextSpan.Empty, "X")
                        }),
                    new StringLiteralNode(TextSpan.Empty, "X")
                }),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [SkippableFact]
    public void ProvesIndexOfWithOffsetSkipsEarlierMatches()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesIndexOfWithOffsetSkipsEarlierMatchesCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesIndexOfWithOffsetSkipsEarlierMatchesCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Postcondition: (>= (indexof "abcabc" "b" 3) 3)
        // Should be PROVEN because searching from index 3 finds "b" at index 4, which is >= 3
        var parameters = new List<(string Name, string Type)>();

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.IndexOf,
                    new List<ExpressionNode>
                    {
                        new StringLiteralNode(TextSpan.Empty, "abcabc"),
                        new StringLiteralNode(TextSpan.Empty, "b"),
                        new IntLiteralNode(TextSpan.Empty, 3)
                    }),
                new IntLiteralNode(TextSpan.Empty, 3)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // Diagnostic Integration Tests
    // ===========================================

    [SkippableFact]
    public void ReturnsUnsupportedWithDiagnosticForFloatParameter()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsUnsupportedWithDiagnosticForFloatParameterCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsUnsupportedWithDiagnosticForFloatParameterCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Parameter type f32 is not supported
        var parameters = new List<(string Name, string Type)> { ("x", "f32") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "f32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        Assert.Contains("f32", result.CounterexampleDescription);
        Assert.Contains("not supported", result.CounterexampleDescription);
    }

    [SkippableFact]
    public void ReturnsUnsupportedWithDiagnosticForFloatResultType()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsUnsupportedWithDiagnosticForFloatResultTypeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsUnsupportedWithDiagnosticForFloatResultTypeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        // Result type double is not supported
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "result"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "double",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        Assert.Contains("double", result.CounterexampleDescription);
        Assert.Contains("floating-point", result.CounterexampleDescription);
    }

    [SkippableFact]
    public void ReturnsUnsupportedWithDiagnosticForFunctionCallInPostcondition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsUnsupportedWithDiagnosticForFunctionCallInPostconditionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsUnsupportedWithDiagnosticForFunctionCallInPostconditionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        // Function call customFunc is not supported
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new CallExpressionNode(
                    TextSpan.Empty,
                    "customFunc",
                    new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "x") }),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        Assert.Contains("customFunc", result.CounterexampleDescription);
        Assert.Contains("not supported", result.CounterexampleDescription);
    }

    [SkippableFact]
    public void ReturnsUnsupportedWithDiagnosticForUnsupportedStringOpInPrecondition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsUnsupportedWithDiagnosticForUnsupportedStringOpInPreconditionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsUnsupportedWithDiagnosticForUnsupportedStringOpInPreconditionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        // ToUpper is not supported
        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.ToUpper,
                    new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") }),
                new StringLiteralNode(TextSpan.Empty, "HELLO")),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") }),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        Assert.Contains("ToUpper", result.CounterexampleDescription);
        Assert.Contains("not supported", result.CounterexampleDescription);
    }

    [SkippableFact]
    public void ReturnsUnsupportedWithDiagnosticForUnknownVariableInPostcondition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsUnsupportedWithDiagnosticForUnknownVariableInPostconditionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsUnsupportedWithDiagnosticForUnknownVariableInPostconditionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        // References unknownVar which is not declared
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new ReferenceNode(TextSpan.Empty, "x"),
                new ReferenceNode(TextSpan.Empty, "unknownVar")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        Assert.Contains("unknownVar", result.CounterexampleDescription);
        Assert.Contains("Unknown variable", result.CounterexampleDescription);
    }

    [SkippableFact]
    public void ReturnsUnsupportedWithDiagnosticForFloatLiteralInPostcondition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsUnsupportedWithDiagnosticForFloatLiteralInPostconditionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsUnsupportedWithDiagnosticForFloatLiteralInPostconditionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        // Float literal 3.14 is not supported
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new ReferenceNode(TextSpan.Empty, "x"),
                new FloatLiteralNode(TextSpan.Empty, 3.14)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        Assert.Contains("Floating-point", result.CounterexampleDescription);
        Assert.Contains("not supported", result.CounterexampleDescription);
    }

    [SkippableFact]
    public void ReturnsUnsupportedWithDiagnosticForPreconditionWithFunctionCall()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsUnsupportedWithDiagnosticForPreconditionWithFunctionCallCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsUnsupportedWithDiagnosticForPreconditionWithFunctionCallCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        // Precondition with function call
        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new CallExpressionNode(
                    TextSpan.Empty,
                    "validate",
                    new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "x") }),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPrecondition(parameters, precondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        Assert.Contains("validate", result.CounterexampleDescription);
        Assert.Contains("not supported", result.CounterexampleDescription);
    }

    [SkippableFact]
    public void ReturnsUnsupportedWithDiagnosticForTypeMismatch()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsUnsupportedWithDiagnosticForTypeMismatchCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsUnsupportedWithDiagnosticForTypeMismatchCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("b", "bool") };

        // b + 1 is a type mismatch (bool + int)
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    new ReferenceNode(TextSpan.Empty, "b"),
                    new IntLiteralNode(TextSpan.Empty, 1)),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        Assert.Contains("Add", result.CounterexampleDescription);
        Assert.Contains("integer operands", result.CounterexampleDescription);
    }

    // ===========================================
    // Warnings Integration Tests
    // ===========================================

    [SkippableFact]
    public void ReturnsWarningForIgnoreCaseComparisonMode()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsWarningForIgnoreCaseComparisonModeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsWarningForIgnoreCaseComparisonModeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        // Postcondition using :ignore-case which will be ignored
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.Contains,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "hello")
                },
                StringComparisonMode.IgnoreCase),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        // Verification should proceed (not Unsupported)
        Assert.NotEqual(ContractVerificationStatus.Unsupported, result.Status);

        // Should have a warning about ignored comparison mode
        Assert.NotNull(result.Warnings);
        Assert.Single(result.Warnings);
        Assert.Contains("IgnoreCase", result.Warnings[0]);
        Assert.Contains("ignored", result.Warnings[0]);
    }

    [SkippableFact]
    public void NoWarningsForStandardVerification()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NoWarningsForStandardVerificationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NoWarningsForStandardVerificationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new ReferenceNode(TextSpan.Empty, "x"),
                new ReferenceNode(TextSpan.Empty, "x")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
        Assert.Null(result.Warnings);
    }

    [SkippableFact]
    public void PreconditionReturnsWarningForIgnoreCaseComparisonMode()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        PreconditionReturnsWarningForIgnoreCaseComparisonModeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PreconditionReturnsWarningForIgnoreCaseComparisonModeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        // Precondition using :ignore-case which will be ignored
        var precondition = new RequiresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.Contains,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "hello")
                },
                StringComparisonMode.IgnoreCase),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPrecondition(parameters, precondition);

        // Verification should proceed (precondition is satisfiable)
        Assert.Equal(ContractVerificationStatus.Proven, result.Status);

        // Should have a warning about ignored comparison mode
        Assert.NotNull(result.Warnings);
        Assert.Single(result.Warnings);
        Assert.Contains("IgnoreCase", result.Warnings[0]);
    }

    [SkippableFact]
    public void AccumulatesMultipleWarnings()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        AccumulatesMultipleWarningsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AccumulatesMultipleWarningsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        // Precondition with :ignore-case
        var precondition = new RequiresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.StartsWith,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "hello")
                },
                StringComparisonMode.IgnoreCase),
            null,
            new AttributeCollection());

        // Postcondition with :invariant-ignore-case (different mode)
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.EndsWith,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "world")
                },
                StringComparisonMode.InvariantIgnoreCase),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        // Should have warnings for both ignored comparison modes
        Assert.NotNull(result.Warnings);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("IgnoreCase"));
        Assert.Contains(result.Warnings, w => w.Contains("InvariantIgnoreCase"));
    }

    // ===========================================
    // Quantifier with String/Array Tests
    // ===========================================

    [SkippableFact]
    public void ProvesStringPrefixImpliesNotEmpty()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesStringPrefixImpliesNotEmptyCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesStringPrefixImpliesNotEmptyCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("s", "string") };

        // Precondition: (starts s "hello")
        var precondition = new RequiresNode(
            TextSpan.Empty,
            new StringOperationNode(
                TextSpan.Empty,
                StringOp.StartsWith,
                new List<ExpressionNode>
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "hello")
                }),
            null,
            new AttributeCollection());

        // Postcondition: (not (isempty s))
        // If s starts with "hello", then s is not empty
        // Using IsNullOrEmpty (negated) stays in Z3's integer domain, avoiding
        // complex theory interactions with bit-vector conversion
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new UnaryOperationNode(
                TextSpan.Empty,
                UnaryOperator.Not,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.IsNullOrEmpty,
                    new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") })),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void ProvesExistsWithStringOperation()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesExistsWithStringOperationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesExistsWithStringOperationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // No parameters needed for this test
        var parameters = new List<(string Name, string Type)>();

        // Postcondition: There exists a string equal to "test"
        // (exists ((x string)) (equals x "test"))
        // This should be proven - such a string exists
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new ExistsExpressionNode(
                TextSpan.Empty,
                new List<QuantifierVariableNode>
                {
                    new QuantifierVariableNode(TextSpan.Empty, "x", "string")
                },
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Equals,
                    new List<ExpressionNode>
                    {
                        new ReferenceNode(TextSpan.Empty, "x"),
                        new StringLiteralNode(TextSpan.Empty, "test")
                    })),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void ProvesForallWithArrayAndStringLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesForallWithArrayAndStringLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesForallWithArrayAndStringLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("s", "string")
        };

        // Precondition: array length > 0 AND string length > 0
        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new ArrayLengthNode(TextSpan.Empty, new ReferenceNode(TextSpan.Empty, "arr")),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") }),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        // Postcondition: array length >= 1 AND string length >= 1
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.And,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterOrEqual,
                    new ArrayLengthNode(TextSpan.Empty, new ReferenceNode(TextSpan.Empty, "arr")),
                    new IntLiteralNode(TextSpan.Empty, 1)),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterOrEqual,
                    new StringOperationNode(
                        TextSpan.Empty,
                        StringOp.Length,
                        new List<ExpressionNode> { new ReferenceNode(TextSpan.Empty, "s") }),
                    new IntLiteralNode(TextSpan.Empty, 1))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition1, precondition2 },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // Robustness and Exception Handling Tests
    // ===========================================

    [SkippableFact]
    public void ReturnsUnprovenOnSolverTimeout()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ReturnsUnprovenOnSolverTimeoutCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnsUnprovenOnSolverTimeoutCore()
    {
        using var ctx = Z3ContextFactory.Create();
        // Use very short timeout to trigger Unproven status
        using var verifier = new Z3Verifier(ctx, timeoutMs: 1);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        // Complex postcondition that might timeout with 1ms timeout
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Multiply,
                    new ReferenceNode(TextSpan.Empty, "x"),
                    new ReferenceNode(TextSpan.Empty, "x")),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        // Result should be either Proven (if Z3 is fast enough) or Unproven (timeout)
        // Both are valid - the test verifies no exception is thrown
        Assert.True(
            result.Status == ContractVerificationStatus.Proven ||
            result.Status == ContractVerificationStatus.Unproven,
            $"Expected Proven or Unproven but got {result.Status}");
    }

    [SkippableFact]
    public void HandlesEmptyParameterList()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        HandlesEmptyParameterListCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void HandlesEmptyParameterListCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)>();

        // Trivially true postcondition: true
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BoolLiteralNode(TextSpan.Empty, true),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            null,
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void HandlesNullOutputType()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        HandlesNullOutputTypeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void HandlesNullOutputTypeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        // Postcondition not using 'result' variable
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new ReferenceNode(TextSpan.Empty, "x"),
                new ReferenceNode(TextSpan.Empty, "x")),
            null,
            new AttributeCollection());

        // Pass null for outputType
        var result = verifier.VerifyPostcondition(
            parameters,
            null,
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void ResultIncludesDuration()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ResultIncludesDurationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ResultIncludesDurationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new ReferenceNode(TextSpan.Empty, "x"),
                new ReferenceNode(TextSpan.Empty, "x")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            Array.Empty<RequiresNode>(),
            postcondition);

        // Duration should be recorded
        Assert.NotNull(result.Duration);
        Assert.True(result.Duration.Value.TotalMilliseconds >= 0);
    }
}
