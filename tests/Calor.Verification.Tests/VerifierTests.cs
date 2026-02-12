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

        // This should be proven - INT_MIN / -1 = INT_MIN in bit-vector arithmetic
        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }
}
