using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using System.Runtime.CompilerServices;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// W1 Slice 1 soundness-batch pins (wedge-w1-prereqs.md §1.1 T1 + D6–D10):
/// the whitelisted divergences that could mint a false Proven-that-elides are
/// closed — by refusal (Replace, narrow-int arithmetic, 64-bit mixed
/// signedness, unregistered fields), by correct modeling (sub-64-bit mixed
/// signedness via C# promotion; unsigned-with-literal typing), or by side
/// conditions (contract-expression division → Assumed unless entailed).
/// </summary>
public class W1Slice1SoundnessTests
{
    private static ReferenceNode Ref(string name) => new(TextSpan.Empty, name);
    private static IntLiteralNode Int(long v) => new(TextSpan.Empty, v);
    private static BinaryOperationNode BinOp(BinaryOperator op, ExpressionNode l, ExpressionNode r)
        => new(TextSpan.Empty, op, l, r);
    private static RequiresNode Requires(ExpressionNode cond)
        => new(TextSpan.Empty, cond, null, new AttributeCollection());
    private static EnsuresNode Ensures(ExpressionNode cond)
        => new(TextSpan.Empty, cond, null, new AttributeCollection());

    private static Calor.Compiler.Verification.Z3.ContractVerificationResult Verify(
        Z3Verifier verifier,
        (string, string)[] parameters,
        RequiresNode[] preconditions,
        EnsuresNode postcondition,
        string outputType = "bool")
        => verifier.VerifyPostcondition(
            parameters.Select(p => (p.Item1, p.Item2)).ToList(),
            outputType,
            preconditions,
            postcondition);

    // ---- T1: narrow-int arithmetic (D1 class) ----

    [SkippableFact]
    public void NarrowIntArithmetic_IsRefused()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NarrowIntArithmetic_IsRefusedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NarrowIntArithmetic_IsRefusedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // The kickoff's false-Proven shape: §S (< (+ x y) 128) over i8 —
        // an 8-bit solver add is always in [-128,127] (always "proven") while
        // C# promotes to int and 100+100=200 violates the contract at runtime.
        var result = Verify(verifier,
            [("x", "i8"), ("y", "i8")],
            [],
            Ensures(BinOp(BinaryOperator.LessThan,
                BinOp(BinaryOperator.Add, Ref("x"), Ref("y")),
                Int(128))));

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [SkippableFact]
    public void NarrowIntComparisonWithLiteral_StaysModeled()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NarrowIntComparisonWithLiteral_StaysModeledCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NarrowIntComparisonWithLiteral_StaysModeledCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Comparison (no arithmetic) on a narrow operand is safe: sign-extension
        // to the literal's 32-bit width IS C#'s promotion.
        var result = Verify(verifier,
            [("x", "i8")],
            [],
            Ensures(BinOp(BinaryOperator.LessOrEqual, Ref("x"), Int(127))));

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void NarrowIntNegation_IsRefused()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NarrowIntNegation_IsRefusedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NarrowIntNegation_IsRefusedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // -(sbyte)(-128) is 128 at runtime (int promotion); an 8-bit negation
        // wraps back to -128 — D1's unary form.
        var result = Verify(verifier,
            [("x", "i8")],
            [],
            Ensures(BinOp(BinaryOperator.NotEqual,
                new UnaryOperationNode(TextSpan.Empty, UnaryOperator.Negate, Ref("x")),
                Int(-128))));

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    // ---- D10: mixed signedness ----

    [SkippableFact]
    public void MixedSignedness_MinusOneNeverEqualsUnsigned()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        MixedSignedness_MinusOneNeverEqualsUnsignedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void MixedSignedness_MinusOneNeverEqualsUnsignedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // The canonical D10 repro: `-1 == 4294967295u` held under raw bit-pattern
        // equality. Under C# promotion semantics (both sides → 64-bit signed),
        // -1 equals no u32 value — so §S (!= -1 x) is PROVEN, as at runtime.
        var result = Verify(verifier,
            [("x", "u32")],
            [],
            Ensures(BinOp(BinaryOperator.NotEqual, Int(-1), Ref("x"))));

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
    }

    [SkippableFact]
    public void MixedSignedness64Bit_IsRefused()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        MixedSignedness64Bit_IsRefusedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void MixedSignedness64Bit_IsRefusedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // long vs ulong has no common C# type (the comparison does not compile);
        // there is no runtime semantics to model, so the form is refused.
        var result = Verify(verifier,
            [("x", "i64"), ("y", "u64")],
            [],
            Ensures(BinOp(BinaryOperator.NotEqual, Ref("x"), Ref("y"))));

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    // ---- D7: unregistered fields ----

    [SkippableFact]
    public void UnregisteredField_IsRefused()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        UnregisteredField_IsRefusedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UnregisteredField_IsRefusedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // No user-type registry: the field's width/signedness would be a guess
        // (the old i32 default), and a wrong guess reasons at the wrong wrap
        // boundary — refuse instead.
        var result = Verify(verifier,
            [("o", "Order")],
            [],
            Ensures(BinOp(BinaryOperator.GreaterThan,
                new FieldAccessNode(TextSpan.Empty, Ref("o"), "Total"),
                Int(0))));

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    // ---- D8: contract-expression division ----

    [SkippableFact]
    public void ContractDivision_UnguardedDivisor_DemotesToAssumed()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ContractDivision_UnguardedDivisor_DemotesToAssumedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ContractDivision_UnguardedDivisor_DemotesToAssumedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // §S (== (* (/ x y) 0) 0) is provable regardless of the quotient — but
        // the runtime check evaluates x/y and THROWS at y=0, so the proof is
        // conditional on the contract expression's own evaluability: Assumed,
        // carrying the canonical contract-division assumption; never elides.
        var result = Verify(verifier,
            [("x", "i32"), ("y", "i32")],
            [],
            Ensures(BinOp(BinaryOperator.Equal,
                BinOp(BinaryOperator.Multiply,
                    BinOp(BinaryOperator.Divide, Ref("x"), Ref("y")),
                    Int(0)),
                Int(0))));

        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
        Assert.Contains(Z3Verifier.ContractExpressionDivisionAssumption,
            result.EffectiveOutcome.Assumptions);
    }

    [SkippableFact]
    public void ContractDivision_GuardEntailedByPrecondition_StaysProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ContractDivision_GuardEntailedByPrecondition_StaysProvenCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ContractDivision_GuardEntailedByPrecondition_StaysProvenCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // The guard idiom: §Q (!= y 0) entails the divisor side condition, so
        // the runtime check cannot throw on any valid call — plain Proven, no
        // assumption (the entailment refinement).
        var result = Verify(verifier,
            [("x", "i32"), ("y", "i32")],
            [Requires(BinOp(BinaryOperator.NotEqual, Ref("y"), Int(0)))],
            Ensures(BinOp(BinaryOperator.Equal,
                BinOp(BinaryOperator.Multiply,
                    BinOp(BinaryOperator.Divide, Ref("x"), Ref("y")),
                    Int(0)),
                Int(0))));

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
        Assert.Equal(ProofStatus.Proven, result.EffectiveOutcome.Status);
    }

    [SkippableFact]
    public void ContractDivision_ConstantDivisor_StaysProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ContractDivision_ConstantDivisor_StaysProvenCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ContractDivision_ConstantDivisor_StaysProvenCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // A non-zero literal divisor can never throw: no side condition, no demotion.
        var result = Verify(verifier,
            [("x", "i32")],
            [],
            Ensures(BinOp(BinaryOperator.Equal,
                BinOp(BinaryOperator.Multiply,
                    BinOp(BinaryOperator.Divide, Ref("x"), Int(2)),
                    Int(0)),
                Int(0))));

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
        Assert.Equal(ProofStatus.Proven, result.EffectiveOutcome.Status);
    }

    [SkippableFact]
    public void ContractDivision_ConditionalPosition_IsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ContractDivision_ConditionalPosition_IsUnsupportedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ContractDivision_ConditionalPosition_IsUnsupportedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Division guarded by short-circuit || is evaluated only on some paths;
        // asserting its divisor globally would exclude the y=0 disjunct — the
        // same position rule as the body collector: Unsupported.
        var result = Verify(verifier,
            [("x", "i32"), ("y", "i32")],
            [],
            Ensures(BinOp(BinaryOperator.Or,
                BinOp(BinaryOperator.Equal, Ref("y"), Int(0)),
                BinOp(BinaryOperator.Equal,
                    BinOp(BinaryOperator.Multiply,
                        BinOp(BinaryOperator.Divide, Ref("x"), Ref("y")),
                        Int(0)),
                    Int(0)))));

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }
}
