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
    public void MixedSignedness_U32WithI64_ComparisonModeled()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        MixedSignedness_U32WithI64_ComparisonModeledCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void MixedSignedness_U32WithI64_ComparisonModeledCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Verification round N2: uint-vs-long COMPILES in C# (uint → long
        // implicit), so it must be modeled (promotion to 64-bit signed), not
        // refused — only a 64-bit UNSIGNED side has no common type.
        var result = Verify(verifier,
            [("x", "u32"), ("y", "i64")],
            [
                Requires(BinOp(BinaryOperator.Equal, Ref("y"), Int(10))),
                Requires(BinOp(BinaryOperator.LessOrEqual, Ref("x"), Int(5)))
            ],
            Ensures(BinOp(BinaryOperator.LessThan, Ref("x"), Ref("y"))));

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

        // The guard idiom for SIGNED division needs to exclude both throw
        // states: §Q (> y 0) entails y ≠ 0 AND ¬(x = MinValue ∧ y = −1)
        // (review #833 C4 added the overflow condition), so the runtime check
        // cannot throw on any valid call — plain Proven, no assumption.
        var result = Verify(verifier,
            [("x", "i32"), ("y", "i32")],
            [Requires(BinOp(BinaryOperator.GreaterThan, Ref("y"), Int(0)))],
            Ensures(BinOp(BinaryOperator.Equal,
                BinOp(BinaryOperator.Multiply,
                    BinOp(BinaryOperator.Divide, Ref("x"), Ref("y")),
                    Int(0)),
                Int(0))));

        Assert.Equal(ContractVerificationStatus.Proven, result.Status);
        Assert.Equal(ProofStatus.Proven, result.EffectiveOutcome.Status);
    }

    // ---- Review #833 repro pins ----

    [SkippableFact]
    public void NarrowUnsignedWithInt32Arithmetic_WrapsAt32_NotAt64()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NarrowUnsignedWithInt32Arithmetic_WrapsAt32_NotAt64Core();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NarrowUnsignedWithInt32Arithmetic_WrapsAt32_NotAt64Core()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Review #833 C1 repro: `b + x` over (u8, i32) is INT arithmetic in C#
        // and wraps at 32 bits — b=1, x=int.MaxValue gives int.MinValue < 0.
        // The 64-bit promotion never wrapped and falsely PROVED this (eliding
        // the check the runtime violates).
        var result = Verify(verifier,
            [("b", "u8"), ("x", "i32")],
            [Requires(BinOp(BinaryOperator.Equal, Ref("x"), Int(int.MaxValue)))],
            Ensures(BinOp(BinaryOperator.GreaterThan,
                BinOp(BinaryOperator.Add, Ref("b"), Ref("x")),
                Int(0))));

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void NarrowUnsignedMinusLiteral_IsSignedInt_CanBeNegative()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NarrowUnsignedMinusLiteral_IsSignedInt_CanBeNegativeCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NarrowUnsignedMinusLiteral_IsSignedInt_CanBeNegativeCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Review #833 C2 repro: C# has no byte arithmetic — `x - 5` over u8 is
        // int and is negative for x < 5. The literal-conversion rescue wrongly
        // typed it unsigned (BVUGE always true → false Proven + elide).
        var result = Verify(verifier,
            [("x", "u8")],
            [],
            Ensures(BinOp(BinaryOperator.GreaterOrEqual,
                BinOp(BinaryOperator.Subtract, Ref("x"), Int(5)),
                Int(0))));

        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void ShiftCount_IsMasked_LikeCSharp()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ShiftCount_IsMasked_LikeCSharpCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ShiftCount_IsMasked_LikeCSharpCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Review #833 C3 repro: C# masks the shift count by width−1, so
        // `x << 32` IS `x` at runtime — an unmasked solver shift yields 0 and
        // falsely proved `(x << 32) == 0`. With the mask modeled, the
        // runtime-true identity proves and the runtime-false one refutes.
        var identity = Verify(verifier,
            [("x", "i32")],
            [],
            Ensures(BinOp(BinaryOperator.Equal,
                BinOp(BinaryOperator.LeftShift, Ref("x"), Int(32)),
                Ref("x"))));
        Assert.Equal(ContractVerificationStatus.Proven, identity.Status);

        var zeroClaim = Verify(verifier,
            [("x", "i32")],
            [],
            Ensures(BinOp(BinaryOperator.Equal,
                BinOp(BinaryOperator.LeftShift, Ref("x"), Int(32)),
                Int(0))));
        Assert.Equal(ContractVerificationStatus.Disproven, zeroClaim.Status);
    }

    [SkippableFact]
    public void ContractDivision_NonZeroGuardAlone_StillAssumed_OverflowResidual()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ContractDivision_NonZeroGuardAlone_StillAssumed_OverflowResidualCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ContractDivision_NonZeroGuardAlone_StillAssumed_OverflowResidualCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Review #833 C4: §Q (!= y 0) does NOT entail the MinValue÷−1 overflow
        // condition — x=MinValue, y=−1 passes the guard and the runtime check
        // throws OverflowException. The proof stays Assumed.
        var result = Verify(verifier,
            [("x", "i32"), ("y", "i32")],
            [Requires(BinOp(BinaryOperator.NotEqual, Ref("y"), Int(0)))],
            Ensures(BinOp(BinaryOperator.Equal,
                BinOp(BinaryOperator.Multiply,
                    BinOp(BinaryOperator.Divide, Ref("x"), Ref("y")),
                    Int(0)),
                Int(0))));

        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
    }

    [SkippableFact]
    public void ContractDivision_InsideImplicationConsequent_IsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ContractDivision_InsideImplicationConsequent_IsUnsupportedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ContractDivision_InsideImplicationConsequent_IsUnsupportedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // Review #833 C5 repro: the collector silently skipped implications —
        // §S (-> (== y 0) (!= (/ x y) 5)) was Proven+elided while the emitted
        // short-circuit check `!(y==0) || x/y != 5` throws at y=0. The
        // consequent is conditionally evaluated: Unsupported, check kept.
        var result = Verify(verifier,
            [("x", "i32"), ("y", "i32")],
            [],
            Ensures(new ImplicationExpressionNode(TextSpan.Empty,
                BinOp(BinaryOperator.Equal, Ref("y"), Int(0)),
                BinOp(BinaryOperator.NotEqual,
                    BinOp(BinaryOperator.Divide, Ref("x"), Ref("y")),
                    Int(5)))));

        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
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
    // ---- D3/D12: the string model is null-blind and byte-counted (v0.12) ----

    /// <summary>
    /// D3, the vector this demotion exists for. Z3 makes <c>len(s)=0 ⟺ s=""</c> a tautology, but
    /// in C# <c>null</c> satisfies <c>IsNullOrEmpty</c> while <c>null == ""</c> is <b>false</b> —
    /// so this postcondition was <c>Proven</c> and ELIDED while being false at runtime. Reproduced
    /// end-to-end before the fix: `calor run` threw, `calor run --verify` printed.
    /// </summary>
    [SkippableFact]
    public void StringObligation_IsAssumedNotProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        StringObligation_IsAssumedNotProvenCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StringObligation_IsAssumedNotProvenCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // (|| (! (isempty s)) (== s ""))  — a Z3 tautology, false at runtime when s is null.
        // Stated over the PARAMETER rather than `result`: an unbound `result` is Unsupported by
        // D-G1.1 (it must be tied to the body), which would mask the property under test.
        var post = Ensures(BinOp(BinaryOperator.Or,
            new UnaryOperationNode(TextSpan.Empty, UnaryOperator.Not,
                new StringOperationNode(TextSpan.Empty, StringOp.IsNullOrEmpty,
                    new List<ExpressionNode> { Ref("s") })),
            BinOp(BinaryOperator.Equal, Ref("s"), new StringLiteralNode(TextSpan.Empty, ""))));

        var result = Verify(verifier, [("s", "str")], [], post);

        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
        Assert.Contains(Z3Verifier.StringModelAssumption, result.EffectiveOutcome.Assumptions);
    }

    /// <summary>
    /// The demotion must not be silently narrowed later: a purely NUMERIC obligation on a function
    /// that happens to take a string is demoted too. That is deliberately coarse — the body can
    /// route string theory into <c>result</c> (<c>§R (len s)</c>) with no string anywhere in the
    /// contract — and this pins the coarseness so a future "optimization" has to argue with a test.
    /// </summary>
    [SkippableFact]
    public void StringTypedParameter_DemotesEvenNumericObligation()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        StringTypedParameter_DemotesEvenNumericObligationCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StringTypedParameter_DemotesEvenNumericObligationCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var result = Verify(verifier, [("s", "str"), ("x", "i32")], [],
            Ensures(BinOp(BinaryOperator.Equal, Ref("x"), Ref("x"))), outputType: "i32");

        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
        Assert.Contains(Z3Verifier.StringModelAssumption, result.EffectiveOutcome.Assumptions);
    }

    /// <summary>
    /// The other direction, so the demotion is known not to be a blanket one: with no string in
    /// sight, a numeric obligation is still genuinely <c>Proven</c> and still elides. Without this
    /// the tests above would pass just as well if the verifier had stopped proving anything.
    /// </summary>
    [SkippableFact]
    public void NonStringObligation_StaysProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        NonStringObligation_StaysProvenCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NonStringObligation_StaysProvenCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var result = Verify(verifier, [("x", "i32")], [],
            Ensures(BinOp(BinaryOperator.Equal, Ref("x"), Ref("x"))), outputType: "i32");

        Assert.Equal(ProofStatus.Proven, result.EffectiveOutcome.Status);
    }

}
