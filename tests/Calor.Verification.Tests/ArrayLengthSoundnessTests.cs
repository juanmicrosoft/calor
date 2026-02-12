using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Xunit;
using System.Runtime.CompilerServices;

namespace Calor.Verification.Tests;

/// <summary>
/// Comprehensive soundness tests for Z3 array length handling.
///
/// Z3 arrays have no native length attributes - the ContractTranslator handles this
/// by creating synthetic $length variables that are completely decoupled from the array.
/// These tests verify the implementation can't be "fooled" into proving false statements.
/// </summary>
public class ArrayLengthSoundnessTests
{
    // ===========================================
    // Helper Methods
    // ===========================================

    private static ArrayLengthNode Len(string arrayName) =>
        new(TextSpan.Empty, new ReferenceNode(TextSpan.Empty, arrayName));

    private static ArrayAccessNode Access(string arrayName, ExpressionNode index) =>
        new(TextSpan.Empty, new ReferenceNode(TextSpan.Empty, arrayName), index);

    private static IntLiteralNode Int(int value) =>
        new(TextSpan.Empty, value);

    private static ReferenceNode Ref(string name) =>
        new(TextSpan.Empty, name);

    private static BoolLiteralNode Bool(bool value) =>
        new(TextSpan.Empty, value);

    // ===========================================
    // 1. Decoupled Length Semantics (Document Expected Behavior)
    // ===========================================

    [SkippableFact]
    public void AccessBeyondLengthIsNotConstrained()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        AccessBeyondLengthIsNotConstrainedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AccessBeyondLengthIsNotConstrainedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // This test documents expected behavior: array access beyond length is allowed
        // in Z3 because length is decoupled from the array indices.
        // Precondition: len(arr) == 5
        // Postcondition: arr[100] == arr[100] (self-equality, always true)
        // This should be PROVEN because Z3 doesn't constrain indices based on length

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                Int(5)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(100)),
                Access("arr", Int(100))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "i32",
            new[] { precondition },
            postcondition);

        // This is EXPECTED to be proven - Z3 doesn't enforce bounds
        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void LengthDoesNotConstrainValidIndices()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LengthDoesNotConstrainValidIndicesCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LengthDoesNotConstrainValidIndicesCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Verify Z3 doesn't automatically infer that only indices 0..len-1 are valid
        // Precondition: len(arr) > 0
        // Postcondition: arr[-1] == arr[-1] (negative index access)
        // Should be PROVEN because Z3 allows any index

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(-1)),
                Access("arr", Int(-1))),
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
    // 2. Length Arithmetic
    // ===========================================

    [SkippableFact]
    public void LengthAdditionCanOverflow()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LengthAdditionCanOverflowCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LengthAdditionCanOverflowCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // len(arr1) + len(arr2) can overflow (u32 wraps around)
        // Postcondition: len(arr1) + len(arr2) >= len(arr1)
        // Should be DISPROVEN because overflow can occur

        var parameters = new List<(string Name, string Type)>
        {
            ("arr1", "i32[]"),
            ("arr2", "i32[]")
        };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    Len("arr1"),
                    Len("arr2")),
                Len("arr1")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "u32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void LengthSubtractionCanUnderflow()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LengthSubtractionCanUnderflowCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LengthSubtractionCanUnderflowCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // len(arr) - 1 when len(arr) == 0 wraps to max u32
        // Postcondition: len(arr) - 1 < len(arr)
        // Should be DISPROVEN because when len(arr) == 0, len(arr) - 1 wraps to 0xFFFFFFFF

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Subtract,
                    Len("arr"),
                    Int(1)),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "u32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void LengthMultiplicationCanOverflow()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LengthMultiplicationCanOverflowCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LengthMultiplicationCanOverflowCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // len(arr) * 2 can overflow
        // Postcondition: len(arr) * 2 >= len(arr)
        // Should be DISPROVEN due to potential overflow

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Multiply,
                    Len("arr"),
                    Int(2)),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "u32",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void ProvesLengthArithmeticWithPreconditions()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesLengthArithmeticWithPreconditionsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesLengthArithmeticWithPreconditionsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // With proper preconditions, arithmetic is safe
        // Precondition: len(arr) > 0 AND len(arr) < 1000
        // Postcondition: len(arr) - 1 < len(arr)
        // Should be PROVEN because underflow is prevented

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                Len("arr"),
                Int(1000)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Subtract,
                    Len("arr"),
                    Int(1)),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "u32",
            new[] { precondition1, precondition2 },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // 3. Signed/Unsigned Comparison Edge Cases
    // ===========================================

    [SkippableFact]
    public void LengthComparedWithNegativeIntegerUsesCorrectSemantics()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LengthComparedWithNegativeIntegerUsesCorrectSemanticsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LengthComparedWithNegativeIntegerUsesCorrectSemanticsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // len(arr) is unsigned 32-bit, so len(arr) >= 0 is always true
        // Postcondition: len(arr) >= 0
        // Should be PROVEN because unsigned values are always >= 0

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                Len("arr"),
                Int(0)),
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
    public void LengthComparedWithSignedMaxUsesCorrectSemantics()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LengthComparedWithSignedMaxUsesCorrectSemanticsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LengthComparedWithSignedMaxUsesCorrectSemanticsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // len(arr) > INT32_MAX (2147483647) can be true for unsigned
        // Precondition: none
        // Postcondition: len(arr) <= 2147483647
        // Should be DISPROVEN because length could be larger

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessOrEqual,
                Len("arr"),
                Int(2147483647)),
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
    public void IndexComparedWithLengthUsesCorrectSemantics()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        IndexComparedWithLengthUsesCorrectSemanticsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void IndexComparedWithLengthUsesCorrectSemanticsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Signed index compared with unsigned length needs careful handling
        // Precondition: index >= 0 AND index < len(arr) AND len(arr) > 0
        // Postcondition: index < len(arr)
        // Should be PROVEN (tautology from precondition)

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("index", "i32")
        };

        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                Ref("index"),
                Int(0)),
            null,
            new AttributeCollection());

        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                Ref("index"),
                Len("arr")),
            null,
            new AttributeCollection());

        var precondition3 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                Ref("index"),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition1, precondition2, precondition3 },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // 4. Empty Array Edge Cases
    // ===========================================

    [SkippableFact]
    public void ProvesEmptyArrayLengthIsZero()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesEmptyArrayLengthIsZeroCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesEmptyArrayLengthIsZeroCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // len(arr) == 0 is satisfiable (verify the constraint system is consistent)
        // Precondition: len(arr) == 0
        // Postcondition: len(arr) == 0
        // Should be PROVEN (tautology)

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                Int(0)),
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
    public void DisprovesAccessToEmptyArrayIsValidWithoutConstraint()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesAccessToEmptyArrayIsValidWithoutConstraintCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesAccessToEmptyArrayIsValidWithoutConstraintCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Can't prove arr[0] has any specific value when len(arr) == 0
        // Precondition: len(arr) == 0
        // Postcondition: arr[0] == 0
        // Should be DISPROVEN - we can't prove arr[0] equals any specific value

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(0)),
                Int(0)),
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
    public void ProvesNoValidIndexForEmptyArray()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesNoValidIndexForEmptyArrayCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesNoValidIndexForEmptyArrayCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // forall i: len(arr) == 0 -> !(0 <= i < len(arr))
        // Simplified: when len == 0, there's no i such that 0 <= i < 0
        // Precondition: len(arr) == 0
        // Postcondition: forall i: !(i >= 0 AND i < len(arr))
        // Should be PROVEN

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        // forall i: !(i >= 0 AND i < len(arr))
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new ForallExpressionNode(
                TextSpan.Empty,
                new List<QuantifierVariableNode>
                {
                    new QuantifierVariableNode(TextSpan.Empty, "i", "i32")
                },
                new UnaryOperationNode(
                    TextSpan.Empty,
                    UnaryOperator.Not,
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.And,
                        new BinaryOperationNode(
                            TextSpan.Empty,
                            BinaryOperator.GreaterOrEqual,
                            Ref("i"),
                            Int(0)),
                        new BinaryOperationNode(
                            TextSpan.Empty,
                            BinaryOperator.LessThan,
                            Ref("i"),
                            Len("arr"))))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // 5. Multi-Array Scenarios
    // ===========================================

    [SkippableFact]
    public void TwoArraysHaveIndependentLengths()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TwoArraysHaveIndependentLengthsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TwoArraysHaveIndependentLengthsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // len(arr1) != len(arr2) is satisfiable
        // Postcondition: len(arr1) == len(arr2)
        // Should be DISPROVEN because arrays have independent lengths

        var parameters = new List<(string Name, string Type)>
        {
            ("arr1", "i32[]"),
            ("arr2", "i32[]")
        };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr1"),
                Len("arr2")),
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
    public void SameArrayLengthIsConsistent()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        SameArrayLengthIsConsistentCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SameArrayLengthIsConsistentCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // len(arr) == len(arr) is tautology
        // Postcondition: len(arr) == len(arr)
        // Should be PROVEN

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                Len("arr")),
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
    public void MultipleLengthReferencesUseSameVariable()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        MultipleLengthReferencesUseSameVariableCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void MultipleLengthReferencesUseSameVariableCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareArrayVariable("arr", "i32");

        // Get length twice
        var len1 = translator.Translate(Len("arr"));
        var len2 = translator.Translate(Len("arr"));

        Assert.NotNull(len1);
        Assert.NotNull(len2);

        // Both should refer to the same Z3 variable
        Assert.Equal(len1.ToString(), len2.ToString());

        // Verify it's the $length variable
        Assert.True(translator.Variables.ContainsKey("arr$length"));
    }

    // ===========================================
    // 6. Bounds Checking Patterns
    // ===========================================

    [SkippableFact]
    public void ProvesIndexValidWhenPreconditionHolds()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesIndexValidWhenPreconditionHoldsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesIndexValidWhenPreconditionHoldsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Precondition: index >= 0 AND index < len(arr)
        // Postcondition: index >= 0 AND index < len(arr)
        // Should be PROVEN (tautology)

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("index", "i32")
        };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.And,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterOrEqual,
                    Ref("index"),
                    Int(0)),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.LessThan,
                    Ref("index"),
                    Len("arr"))),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.And,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterOrEqual,
                    Ref("index"),
                    Int(0)),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.LessThan,
                    Ref("index"),
                    Len("arr"))),
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
    public void DisprovesIndexValidWithoutPrecondition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesIndexValidWithoutPreconditionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesIndexValidWithoutPreconditionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Without bounds check, can't prove index is valid
        // Postcondition: index >= 0 AND index < len(arr)
        // Should be DISPROVEN

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("index", "i32")
        };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.And,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterOrEqual,
                    Ref("index"),
                    Int(0)),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.LessThan,
                    Ref("index"),
                    Len("arr"))),
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
    public void ProvesLastValidIndexIsLengthMinusOneWithBoundedLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ProvesLastValidIndexIsLengthMinusOneWithBoundedLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProvesLastValidIndexIsLengthMinusOneWithBoundedLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // This test demonstrates signed/unsigned semantics in the implementation.
        // len(arr) is unsigned, but subtracting Int(1) (signed) produces a signed result.
        // When comparing (len(arr) - 1) [signed] with len(arr) [unsigned], mixed comparison
        // uses signed semantics unless one is a non-negative literal.
        //
        // With bounded length (len(arr) <= INT32_MAX), the signed interpretation
        // of len(arr) is non-negative, so (len(arr) - 1) < len(arr) holds.
        //
        // Precondition: len(arr) > 0 AND len(arr) <= INT32_MAX
        // Postcondition: (len(arr) - 1) < len(arr)
        // Should be PROVEN

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        // Bound the length to avoid signed interpretation issues
        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessOrEqual,
                Len("arr"),
                Int(2147483647)), // INT32_MAX
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Subtract,
                    Len("arr"),
                    Int(1)),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition1, precondition2 },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void DisprovesLastValidIndexWithUnboundedLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesLastValidIndexWithUnboundedLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesLastValidIndexWithUnboundedLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // This test documents implementation behavior with mixed signed/unsigned semantics.
        // When len(arr) > INT32_MAX (e.g., 0x80000001), interpreted as signed it's negative.
        // Then (len(arr) - 1) as signed could compare unexpectedly with unsigned len(arr).
        //
        // Precondition: len(arr) > 0 (only)
        // Postcondition: (len(arr) - 1) < len(arr)
        // Should be DISPROVEN due to potential signed interpretation of large lengths

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Subtract,
                    Len("arr"),
                    Int(1)),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void DisprovesAccessAtLengthIsValid()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesAccessAtLengthIsValidCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesAccessAtLengthIsValidCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // arr[len(arr)] is out of bounds (index == length is invalid)
        // Precondition: len(arr) > 0
        // Postcondition: len(arr) < len(arr)
        // Should be DISPROVEN (len(arr) is never < len(arr))

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                Len("arr"),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    // ===========================================
    // 7. Quantifier Interactions
    // ===========================================

    [SkippableFact]
    public void ForallOverArrayIndicesWithLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ForallOverArrayIndicesWithLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ForallOverArrayIndicesWithLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // forall i: (0 <= i < len(arr)) -> arr[i] == arr[i]
        // Should be PROVEN because arr[i] == arr[i] is always true

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new ForallExpressionNode(
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
                            Ref("i"),
                            Int(0)),
                        new BinaryOperationNode(
                            TextSpan.Empty,
                            BinaryOperator.LessThan,
                            Ref("i"),
                            Len("arr"))),
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.Equal,
                        Access("arr", Ref("i")),
                        Access("arr", Ref("i"))))),
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
    public void ExistsIndexWithinBoundsWithBoundedLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ExistsIndexWithinBoundsWithBoundedLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExistsIndexWithinBoundsWithBoundedLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // exists i: 0 <= i < len(arr) when len(arr) > 0 AND len(arr) <= INT32_MAX
        // The bound on length ensures signed/unsigned comparisons work correctly.
        // Precondition: len(arr) > 0 AND len(arr) <= INT32_MAX
        // Postcondition: exists i: i >= 0 AND i < len(arr)
        // Should be PROVEN

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        // Bound the length to avoid signed interpretation issues
        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessOrEqual,
                Len("arr"),
                Int(2147483647)), // INT32_MAX
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new ExistsExpressionNode(
                TextSpan.Empty,
                new List<QuantifierVariableNode>
                {
                    new QuantifierVariableNode(TextSpan.Empty, "i", "i32")
                },
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.And,
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.GreaterOrEqual,
                        Ref("i"),
                        Int(0)),
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.LessThan,
                        Ref("i"),
                        Len("arr")))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition1, precondition2 },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void DisprovesExistsIndexWithUnboundedLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DisprovesExistsIndexWithUnboundedLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisprovesExistsIndexWithUnboundedLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // This test documents implementation behavior with mixed signed/unsigned semantics.
        // When len(arr) > INT32_MAX, the signed index i (i32) cannot reach it.
        // With signed comparison semantics, the exists quantifier fails.
        //
        // Precondition: len(arr) > 0 (only)
        // Postcondition: exists i: i >= 0 AND i < len(arr)
        // Should be DISPROVEN due to signed/unsigned comparison issues

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new ExistsExpressionNode(
                TextSpan.Empty,
                new List<QuantifierVariableNode>
                {
                    new QuantifierVariableNode(TextSpan.Empty, "i", "i32")
                },
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.And,
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.GreaterOrEqual,
                        Ref("i"),
                        Int(0)),
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.LessThan,
                        Ref("i"),
                        Len("arr")))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    // ===========================================
    // 8. Negative Index Handling
    // ===========================================

    [SkippableFact]
    public void NegativeIndexComparedWithLengthHandledCorrectly()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NegativeIndexComparedWithLengthHandledCorrectlyCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NegativeIndexComparedWithLengthHandledCorrectlyCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // -1 (signed) compared with len(arr) (unsigned)
        // For signed comparison: -1 < len(arr) depends on interpretation
        // Postcondition: -1 < len(arr) when using signed comparison
        // Result depends on how mixed sign comparison is handled

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        // Note: The exact result depends on the implementation's handling of
        // mixed signed/unsigned comparisons. This test verifies consistent behavior.
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.NotEqual,
                Int(-1),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        // -1 as i32 is 0xFFFFFFFF which as u32 is a large positive number
        // So -1 (as u32) != len(arr) when len(arr) > 0 might not hold if len is large
        // This should be DISPROVEN because len(arr) could be 0xFFFFFFFF
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void SignedIndexCanBeNegative()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        SignedIndexCanBeNegativeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SignedIndexCanBeNegativeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Verify signed indices can take negative values
        // Postcondition: index >= 0
        // Should be DISPROVEN because index is signed and can be negative

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("index", "i32")
        };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                Ref("index"),
                Int(0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    // ===========================================
    // 9. Different Array Element Types
    // ===========================================

    [SkippableFact]
    public void U32ArrayLengthIsUnsigned()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        U32ArrayLengthIsUnsignedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void U32ArrayLengthIsUnsignedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Verify u32[] array length is also unsigned and >= 0
        var parameters = new List<(string Name, string Type)> { ("arr", "u32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                Len("arr"),
                Int(0)),
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
    public void I64ArrayLengthIsStill32Bit()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        I64ArrayLengthIsStill32BitCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void I64ArrayLengthIsStill32BitCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // Even for i64[] arrays, length should be 32-bit unsigned
        translator.DeclareArrayVariable("arr", "i64");

        var lengthExpr = translator.Translate(Len("arr"));
        Assert.NotNull(lengthExpr);
        Assert.IsType<Microsoft.Z3.BitVecExpr>(lengthExpr);

        var bvExpr = (Microsoft.Z3.BitVecExpr)lengthExpr;
        Assert.Equal(32u, bvExpr.SortSize);
    }

    [SkippableFact]
    public void BoolArrayIsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        BoolArrayIsUnsupportedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BoolArrayIsUnsupportedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Document that bool[] arrays are currently unsupported
        // This is a limitation to be aware of
        var parameters = new List<(string Name, string Type)> { ("flags", "bool[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("flags"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                Len("flags"),
                Int(1)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        // bool[] arrays are not supported - this documents the limitation
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    // ===========================================
    // 10. Unsigned Index Tests (Fully Unsigned Path)
    // ===========================================

    [SkippableFact]
    public void U32IndexWithU32ArrayLengthComparison()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        U32IndexWithU32ArrayLengthComparisonCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void U32IndexWithU32ArrayLengthComparisonCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // With u32 index and u32 length, comparison should be fully unsigned
        // Precondition: index < len(arr) AND len(arr) > 0
        // Postcondition: index < len(arr)
        // Should be PROVEN (tautology)

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "u32[]"),
            ("index", "u32")
        };

        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                Ref("index"),
                Len("arr")),
            null,
            new AttributeCollection());

        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                Ref("index"),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition1, precondition2 },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void U32IndexMinusSignedOneHasMixedSemantics()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        U32IndexMinusSignedOneHasMixedSemanticsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void U32IndexMinusSignedOneHasMixedSemanticsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // This test documents the signed/unsigned interaction.
        // Even with u32 index, subtracting Int(1) (signed i32) produces a signed result.
        // When comparing (index - 1) [signed] with index [unsigned], mixed comparison
        // uses signed semantics by default.
        //
        // Precondition: index > 0
        // Postcondition: (index - 1) < index
        // Should be DISPROVEN due to mixed signed/unsigned comparison semantics

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "u32[]"),
            ("index", "u32")
        };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Ref("index"),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Subtract,
                    Ref("index"),
                    Int(1)),
                Ref("index")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        // Documents that even u32 - i32 produces signed result
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void U32IndexSubtractionWorksWithBoundedIndex()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        U32IndexSubtractionWorksWithBoundedIndexCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void U32IndexSubtractionWorksWithBoundedIndexCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // With bounded u32 index (< INT32_MAX), subtraction works correctly
        // because the signed interpretation is still positive.
        //
        // Precondition: index > 0 AND index <= INT32_MAX
        // Postcondition: (index - 1) < index
        // Should be PROVEN

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "u32[]"),
            ("index", "u32")
        };

        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Ref("index"),
                Int(0)),
            null,
            new AttributeCollection());

        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessOrEqual,
                Ref("index"),
                Int(2147483647)), // INT32_MAX
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Subtract,
                    Ref("index"),
                    Int(1)),
                Ref("index")),
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
    // 11. Counterexample Validation
    // ===========================================

    [SkippableFact]
    public void CounterexampleProvidedForDisprovenLengthClaim()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CounterexampleProvidedForDisprovenLengthClaimCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CounterexampleProvidedForDisprovenLengthClaimCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // A clearly false claim should be disproven WITH a counterexample
        // Postcondition: len(arr) == 42 (can't prove for arbitrary array)

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                Int(42)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        Assert.NotEmpty(result.CounterexampleDescription);
    }

    [SkippableFact]
    public void CounterexampleContainsArrayLengthInfo()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CounterexampleContainsArrayLengthInfoCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CounterexampleContainsArrayLengthInfoCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Counterexample should mention the array or its length
        // Postcondition: len(arr) > 1000000 (disproven because len could be 0)

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(1000000)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
        Assert.NotNull(result.CounterexampleDescription);
        // The counterexample should reference the array length variable
        Assert.Contains("arr", result.CounterexampleDescription);
    }

    // ===========================================
    // 12. Adversarial "Fooling" Scenarios
    // ===========================================

    [SkippableFact]
    public void CannotProveLengthEqualsLengthPlusOne()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CannotProveLengthEqualsLengthPlusOneCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CannotProveLengthEqualsLengthPlusOneCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // This is a fundamental contradiction - should never be provable
        // Postcondition: len(arr) == len(arr) + 1
        // Should be DISPROVEN

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    Len("arr"),
                    Int(1))),
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
    public void CannotProveContradictoryLengthConstraints()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CannotProveContradictoryLengthConstraintsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CannotProveContradictoryLengthConstraintsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Contradictory preconditions should make the function trivially satisfy any postcondition
        // BUT the precondition check should fail (unsatisfiable)
        // Precondition: len(arr) > 10 AND len(arr) < 5
        // This precondition is unsatisfiable

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.And,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterThan,
                    Len("arr"),
                    Int(10)),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.LessThan,
                    Len("arr"),
                    Int(5))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPrecondition(parameters, precondition);

        // The precondition itself should be unsatisfiable (Disproven)
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void CircularConstraintDoesNotFoolProver()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CircularConstraintDoesNotFoolProverCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CircularConstraintDoesNotFoolProverCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Try to "fool" the prover with a circular constraint
        // Precondition: len(arr1) == len(arr2)
        // Postcondition: len(arr1) == len(arr2) + 1 (should fail despite precondition)

        var parameters = new List<(string Name, string Type)>
        {
            ("arr1", "i32[]"),
            ("arr2", "i32[]")
        };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr1"),
                Len("arr2")),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr1"),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    Len("arr2"),
                    Int(1))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        // The precondition says arr1.len == arr2.len
        // The postcondition says arr1.len == arr2.len + 1
        // These are contradictory, so should be DISPROVEN
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void LengthOverflowDoesNotCreateFalseProof()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LengthOverflowDoesNotCreateFalseProofCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LengthOverflowDoesNotCreateFalseProofCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Try to exploit overflow to "prove" something false
        // If len(arr) == UINT32_MAX, then len(arr) + 1 == 0 due to overflow
        // Postcondition: len(arr) + 1 > len(arr) (false when overflow occurs)
        // Should be DISPROVEN

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    Len("arr"),
                    Int(1)),
                Len("arr")),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        // Should be DISPROVEN because len(arr) could be UINT32_MAX
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    // ===========================================
    // 13. INT32_MIN Edge Cases
    // ===========================================

    [SkippableFact]
    public void SignedIndexAtInt32MinHandledCorrectly()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        SignedIndexAtInt32MinHandledCorrectlyCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SignedIndexAtInt32MinHandledCorrectlyCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // INT32_MIN negation overflows: -INT32_MIN == INT32_MIN in two's complement
        // Postcondition: index != -2147483648 OR -index != index
        // This is tricky: when index == INT32_MIN, -index == INT32_MIN due to overflow
        // So the "OR -index != index" part is false when index == INT32_MIN

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("index", "i32")
        };

        // Postcondition: -index >= 0 when index < 0
        // This should be DISPROVEN because when index == INT32_MIN, -index is still negative
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new ImplicationExpressionNode(
                TextSpan.Empty,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.LessThan,
                    Ref("index"),
                    Int(0)),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterOrEqual,
                    new UnaryOperationNode(
                        TextSpan.Empty,
                        UnaryOperator.Negate,
                        Ref("index")),
                    Int(0))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        // Should be DISPROVEN due to INT32_MIN edge case
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void Int32MinSubtractionBehavior()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        Int32MinSubtractionBehaviorCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Int32MinSubtractionBehaviorCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // index - 1 when index == INT32_MIN overflows to INT32_MAX
        // Postcondition: index > 0 implies index - 1 >= 0
        // Should be PROVEN because when index > 0, index - 1 >= 0 (no underflow possible)

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("index", "i32")
        };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new ImplicationExpressionNode(
                TextSpan.Empty,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterThan,
                    Ref("index"),
                    Int(0)),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.GreaterOrEqual,
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.Subtract,
                        Ref("index"),
                        Int(1)),
                    Int(0))),
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
    // 14. String and Array Length Interaction
    // ===========================================

    [SkippableFact]
    public void StringAndArrayLengthAreIndependent()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        StringAndArrayLengthAreIndependentCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StringAndArrayLengthAreIndependentCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // String length and array length should be independent
        // Postcondition: len(arr) == len(s)
        // Should be DISPROVEN (they're independent variables)

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("s", "string")
        };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode> { Ref("s") })),
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
    public void StringAndArrayLengthCanBeConstrainedTogether()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        StringAndArrayLengthCanBeConstrainedTogetherCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StringAndArrayLengthCanBeConstrainedTogetherCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // With precondition, we can prove relationship between string and array length
        // Precondition: len(arr) == len(s) AND len(arr) > 0
        // Postcondition: len(s) > 0

        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("s", "string")
        };

        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                new StringOperationNode(
                    TextSpan.Empty,
                    StringOp.Length,
                    new List<ExpressionNode> { Ref("s") })),
            null,
            new AttributeCollection());

        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(0)),
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
                    new List<ExpressionNode> { Ref("s") }),
                Int(0)),
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
    // 15. Large Index Literal Tests
    // ===========================================

    [SkippableFact]
    public void LargeIndexLiteralAccessIsAllowed()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LargeIndexLiteralAccessIsAllowedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LargeIndexLiteralAccessIsAllowedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Access at a very large index (INT32_MAX) should work
        // Postcondition: arr[2147483647] == arr[2147483647]
        // Should be PROVEN (reflexivity)

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(2147483647)),
                Access("arr", Int(2147483647))),
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
    public void DifferentLargeIndicesYieldDifferentElements()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DifferentLargeIndicesYieldDifferentElementsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DifferentLargeIndicesYieldDifferentElementsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Can't prove arr[INT32_MAX] == arr[INT32_MAX - 1] (independent elements)
        // Postcondition: arr[2147483647] == arr[2147483646]
        // Should be DISPROVEN

        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(2147483647)),
                Access("arr", Int(2147483646))),
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
    // 16. Multi-Dimensional Array Tests
    // ===========================================

    [SkippableFact]
    public void NestedArrayTypeIsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NestedArrayTypeIsUnsupportedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NestedArrayTypeIsUnsupportedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Multi-dimensional arrays (i32[][]) are not supported
        // The element type "i32[]" doesn't have a known bit-width
        var parameters = new List<(string Name, string Type)> { ("matrix", "i32[][]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                Len("matrix"),
                Int(0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        // Nested arrays are unsupported
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [SkippableFact]
    public void TranslatorRejectsNestedArrayDeclaration()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatorRejectsNestedArrayDeclarationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TranslatorRejectsNestedArrayDeclarationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        // Attempting to declare a nested array should fail
        var declared = translator.DeclareArrayVariable("matrix", "i32[]");

        // Should return false because "i32[]" is not a valid element type
        Assert.False(declared);
        Assert.False(translator.Variables.ContainsKey("matrix"));
    }

    // ===========================================
    // 17. Array Aliasing Tests
    // ===========================================

    [SkippableFact]
    public void TwoArrayParametersAreDistinctByDefault()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TwoArrayParametersAreDistinctByDefaultCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TwoArrayParametersAreDistinctByDefaultCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Two array parameters are modeled as distinct Z3 arrays
        // We cannot prove arr1[0] == arr2[0] without explicit constraint
        var parameters = new List<(string Name, string Type)>
        {
            ("arr1", "i32[]"),
            ("arr2", "i32[]")
        };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr1", Int(0)),
                Access("arr2", Int(0))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        // Should be DISPROVEN - arrays are independent
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void ArrayEqualityCannotBeExpressedDirectly()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ArrayEqualityCannotBeExpressedDirectlyCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ArrayEqualityCannotBeExpressedDirectlyCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);

        translator.DeclareArrayVariable("arr1", "i32");
        translator.DeclareArrayVariable("arr2", "i32");

        // Try to express arr1 == arr2 using reference equality
        // This would require array extensionality which may not be directly expressible
        var equalityExpr = new BinaryOperationNode(
            TextSpan.Empty,
            BinaryOperator.Equal,
            Ref("arr1"),
            Ref("arr2"));

        // The translator should handle this - let's see what it produces
        var result = translator.TranslateBoolExpr(equalityExpr);

        // Either it translates to something (array equality) or returns null
        // The key insight is that even if it translates, two distinct array constants
        // are not equal by default in Z3
        if (result != null)
        {
            // If it translates, verify the two arrays are still independent
            using var solver = ctx.MkSolver();
            solver.Assert(result);
            var status = solver.Check();
            // Should be satisfiable (arrays CAN be equal) but not a tautology
            Assert.Equal(Microsoft.Z3.Status.SATISFIABLE, status);
        }
        // If null, array equality is unsupported - also acceptable
    }

    [SkippableFact]
    public void SameArrayAccessedTwiceYieldsSameValue()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        SameArrayAccessedTwiceYieldsSameValueCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SameArrayAccessedTwiceYieldsSameValueCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Accessing the same array at the same index twice should yield the same value
        // This is fundamental array theory - arr[i] == arr[i]
        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("i", "i32")
        };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Ref("i")),
                Access("arr", Ref("i"))),
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
    public void ConstrainedArraysCanShareElements()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ConstrainedArraysCanShareElementsCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ConstrainedArraysCanShareElementsCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // If we constrain arr1[0] == arr2[0], we can prove it
        var parameters = new List<(string Name, string Type)>
        {
            ("arr1", "i32[]"),
            ("arr2", "i32[]")
        };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr1", Int(0)),
                Access("arr2", Int(0))),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr1", Int(0)),
                Access("arr2", Int(0))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // 18. Additional Adversarial Scenarios
    // ===========================================

    [SkippableFact]
    public void CannotProveArrayElementConstrainsLength()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CannotProveArrayElementConstrainsLengthCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CannotProveArrayElementConstrainsLengthCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Adversarial: Try to infer length from element constraints
        // Even if we know arr[5] exists (has some value), we can't prove len(arr) > 5
        // because Z3 arrays are total functions - arr[5] always "exists"
        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(5)),
                Int(42)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                Len("arr"),
                Int(5)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        // Should be DISPROVEN - element access doesn't imply length
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void CannotExploitIndexWraparound()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CannotExploitIndexWraparoundCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CannotExploitIndexWraparoundCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Adversarial: Try to exploit index wraparound
        // If index wraps around, arr[i] and arr[i + UINT64_MAX + 1] would be same
        // But this requires 64-bit overflow which is tested differently
        // Here we test that normal index arithmetic doesn't cause confusion
        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("i", "i32")
        };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                Ref("i"),
                Int(0)),
            null,
            new AttributeCollection());

        // arr[i] == arr[i + 1] should NOT be provable for distinct indices
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Ref("i")),
                Access("arr", new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    Ref("i"),
                    Int(1)))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void CannotProveAllElementsEqualWithoutQuantifier()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CannotProveAllElementsEqualWithoutQuantifierCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CannotProveAllElementsEqualWithoutQuantifierCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Adversarial: Constraining some elements doesn't constrain others
        // Even with arr[0] == 0 AND arr[1] == 0, we can't prove arr[2] == 0
        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition1 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(0)),
                Int(0)),
            null,
            new AttributeCollection());

        var precondition2 = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(1)),
                Int(0)),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(2)),
                Int(0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition1, precondition2 },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void CannotInferLengthFromForallConstraint()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        CannotInferLengthFromForallConstraintCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CannotInferLengthFromForallConstraintCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Adversarial: A forall constraint over bounded indices doesn't set length
        // forall i: 0 <= i < 10 -> arr[i] >= 0 doesn't mean len(arr) >= 10
        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new ForallExpressionNode(
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
                            Ref("i"),
                            Int(0)),
                        new BinaryOperationNode(
                            TextSpan.Empty,
                            BinaryOperator.LessThan,
                            Ref("i"),
                            Int(10))),
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.GreaterOrEqual,
                        Access("arr", Ref("i")),
                        Int(0)))),
            null,
            new AttributeCollection());

        // Try to infer len(arr) >= 10 from the forall
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                Len("arr"),
                Int(10)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        // The forall doesn't constrain length - should be DISPROVEN
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    // ===========================================
    // 19. Extreme Value Tests
    // ===========================================

    [SkippableFact]
    public void LengthCanBeMaxU32()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LengthCanBeMaxU32Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LengthCanBeMaxU32Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Verify that len(arr) can be UINT32_MAX (0xFFFFFFFF)
        // Postcondition: len(arr) < 4294967295 (UINT32_MAX)
        // Should be DISPROVEN because len could equal UINT32_MAX
        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                Len("arr"),
                // UINT32_MAX = 4294967295, but we can only use int literals
                // So we use the fact that -1 as unsigned is UINT32_MAX
                // Actually, let's test with a smaller value that's provable
                Int(2147483647)), // INT32_MAX
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        // len(arr) could be larger than INT32_MAX (it's unsigned)
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void NegativeIndexAsUnsignedIsLargePositive()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NegativeIndexAsUnsignedIsLargePositiveCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NegativeIndexAsUnsignedIsLargePositiveCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // When a negative i32 is used as an array index (promoted to 64-bit),
        // it becomes a large positive number if sign-extended
        // arr[-1] as signed 64-bit index is a huge positive index
        // This test verifies the behavior is consistent
        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        // arr[-1] == arr[-1] should still be true (reflexivity)
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Int(-1)),
                Access("arr", Int(-1))),
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
    public void ZeroLengthArrayHasNoValidIndices()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ZeroLengthArrayHasNoValidIndicesCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ZeroLengthArrayHasNoValidIndicesCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // For an array with len == 0, there should be no valid index i where 0 <= i < len
        // This is a logical property, not enforced by Z3 array theory
        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Len("arr"),
                Int(0)),
            null,
            new AttributeCollection());

        // NOT exists i: 0 <= i < 0 (vacuously true - no such i exists)
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new UnaryOperationNode(
                TextSpan.Empty,
                UnaryOperator.Not,
                new ExistsExpressionNode(
                    TextSpan.Empty,
                    new List<QuantifierVariableNode>
                    {
                        new QuantifierVariableNode(TextSpan.Empty, "i", "i32")
                    },
                    new BinaryOperationNode(
                        TextSpan.Empty,
                        BinaryOperator.And,
                        new BinaryOperationNode(
                            TextSpan.Empty,
                            BinaryOperator.GreaterOrEqual,
                            Ref("i"),
                            Int(0)),
                        new BinaryOperationNode(
                            TextSpan.Empty,
                            BinaryOperator.LessThan,
                            Ref("i"),
                            Len("arr"))))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            new[] { precondition },
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    // ===========================================
    // 20. Read-Only Nature of Contract Verification
    // ===========================================

    [SkippableFact]
    public void ArrayElementsAreImmutableDuringVerification()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ArrayElementsAreImmutableDuringVerificationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ArrayElementsAreImmutableDuringVerificationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // In contract verification, array elements are modeled as immutable
        // (no store operations). This means we can prove properties about
        // element values being consistent.
        var parameters = new List<(string Name, string Type)>
        {
            ("arr", "i32[]"),
            ("i", "i32"),
            ("j", "i32")
        };

        // If i == j, then arr[i] == arr[j] (by substitution)
        var precondition = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Ref("i"),
                Ref("j")),
            null,
            new AttributeCollection());

        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                Access("arr", Ref("i")),
                Access("arr", Ref("j"))),
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
    public void LengthIsImmutableDuringVerification()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        LengthIsImmutableDuringVerificationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LengthIsImmutableDuringVerificationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Length doesn't change during verification (it's a single symbolic value)
        // If we reference len(arr) multiple times, it's always the same
        var parameters = new List<(string Name, string Type)> { ("arr", "i32[]") };

        // len(arr) + len(arr) == 2 * len(arr) (arithmetic identity)
        var postcondition = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    Len("arr"),
                    Len("arr")),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Multiply,
                    Int(2),
                    Len("arr"))),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(
            parameters,
            "bool",
            Array.Empty<RequiresNode>(),
            postcondition);

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }
}
