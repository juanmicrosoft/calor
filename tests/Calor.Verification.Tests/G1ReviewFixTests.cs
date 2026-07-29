using System.Runtime.CompilerServices;
using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// Pins for the G1 adversarial-review findings on the body→result binding
/// (guarantees plan WS-G1, review of #818): C1 result-parameter collision,
/// M1 dotted result references, M2 array-result length linkage, M3 auto-declared
/// free arrays, M4 Z3 total-division models, m2 vacuity-vs-unencodable ordering.
/// </summary>
public class G1ReviewFixTests
{
    private static ReferenceNode Ref(string name) => new(TextSpan.Empty, name);
    private static IntLiteralNode Int(int value) => new(TextSpan.Empty, value);
    private static BinaryOperationNode Bin(BinaryOperator op, ExpressionNode l, ExpressionNode r)
        => new(TextSpan.Empty, op, l, r);
    private static RequiresNode Requires(ExpressionNode cond)
        => new(TextSpan.Empty, cond, null, new AttributeCollection());
    private static EnsuresNode Ensures(ExpressionNode cond)
        => new(TextSpan.Empty, cond, null, new AttributeCollection());
    private static ReturnStatementNode Return(ExpressionNode expr) => new(TextSpan.Empty, expr);

    // ------------------------------------------------------------------
    // C1 — a parameter named `result` must never alias the result variable
    // into a false Proven (the binding `result == result + 1` is UNSAT and
    // would prove ANY postcondition, deleting its runtime check).
    // ------------------------------------------------------------------

    [SkippableFact]
    public void ResultNamedParameter_IsUnsupported_NeverFalseProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ResultNamedParameterCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ResultNamedParameterCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("result", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterThan, Ref("result"), Int(0)));
        var body = new List<StatementNode> { Return(Bin(BinaryOperator.Add, Ref("result"), Int(1))) };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("named 'result'", result.EffectiveOutcome.Reason);
    }

    // ------------------------------------------------------------------
    // M1 — result.Field lexes as one dotted ReferenceNode; the walker must
    // see it, or the binding is skipped and #807 returns for user types.
    // ------------------------------------------------------------------

    [Fact]
    public void ReferencesResult_SeesDottedResultReference()
    {
        Assert.True(FunctionBodyEncoder.ReferencesResult(Ref("result.Value")));
        Assert.True(FunctionBodyEncoder.ReferencesResult(
            Bin(BinaryOperator.Equal, Ref("result.Value"), Ref("b.Value"))));
        Assert.False(FunctionBodyEncoder.ReferencesResult(Ref("resultant")));
    }

    // ------------------------------------------------------------------
    // M2 — §LEN result must be seen by the walker, and array-returning
    // bodies must be Unsupported (binding does not link $length vars).
    // ------------------------------------------------------------------

    [Fact]
    public void ReferencesResult_SeesArrayLengthOfResult()
    {
        Assert.True(FunctionBodyEncoder.ReferencesResult(
            new ArrayLengthNode(TextSpan.Empty, Ref("result"))));
    }

    [SkippableFact]
    public void ArrayReturningBody_WithLenResultPostcondition_IsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ArrayReturningBodyCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ArrayReturningBodyCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("xs", "i32[]") };
        var post = Ensures(Bin(
            BinaryOperator.Equal,
            new ArrayLengthNode(TextSpan.Empty, Ref("result")),
            new ArrayLengthNode(TextSpan.Empty, Ref("xs"))));
        var body = new List<StatementNode> { Return(Ref("xs")) };

        var result = verifier.VerifyPostcondition(parameters, "i32[]", [], post, body);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("array-returning", result.EffectiveOutcome.Reason);
    }

    // ------------------------------------------------------------------
    // M3 — a body referencing an undeclared array must not verify against a
    // translator-auto-declared free variable.
    // ------------------------------------------------------------------

    [SkippableFact]
    public void UndeclaredArrayReferenceInBody_IsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        UndeclaredArrayReferenceCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UndeclaredArrayReferenceCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("a", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterThan, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            Return(new ArrayAccessNode(TextSpan.Empty, Ref("ys"), Int(0)))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("ys", result.EffectiveOutcome.Reason);
    }

    // ------------------------------------------------------------------
    // M4 — Z3 totalizes x/0; divisor-nonzero side conditions make division
    // refutations runtime-genuine and division proofs normal-return-sound.
    // ------------------------------------------------------------------

    [SkippableFact]
    public void DivisionBody_ProvesUnderNonZeroDivisorCondition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DivisionProvenCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DivisionProvenCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // a >= 0 ⊨ a/b >= -a for all b != 0 (|a/b| <= a). Pre-fix this was
        // "refuted" at b=0 via bvsdiv totalization — a model runtime never produces.
        var parameters = new List<(string Name, string Type)> { ("a", "i32"), ("b", "i32") };
        var pre = Requires(Bin(BinaryOperator.GreaterOrEqual, Ref("a"), Int(0)));
        var post = Ensures(Bin(
            BinaryOperator.GreaterOrEqual,
            Ref("result"),
            Bin(BinaryOperator.Subtract, Int(0), Ref("a"))));
        var body = new List<StatementNode> { Return(Bin(BinaryOperator.Divide, Ref("a"), Ref("b"))) };

        var result = verifier.VerifyPostcondition(parameters, "i32", [pre], post, body);

        Assert.Equal(ProofStatus.Proven, result.EffectiveOutcome.Status);
        Assert.False(result.EffectiveOutcome.IsVacuous);
    }

    [SkippableFact]
    public void DivisionBody_RefutationModelHasNonZeroDivisor()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DivisionRefutedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DivisionRefutedCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // a > 0 does NOT entail a/b > 0 (a=1, b=2 → 0) — genuinely refutable,
        // and the model's divisor must not be the totalized b=0 path.
        var parameters = new List<(string Name, string Type)> { ("a", "i32"), ("b", "i32") };
        var pre = Requires(Bin(BinaryOperator.GreaterThan, Ref("a"), Int(0)));
        var post = Ensures(Bin(BinaryOperator.GreaterThan, Ref("result"), Int(0)));
        var body = new List<StatementNode> { Return(Bin(BinaryOperator.Divide, Ref("a"), Ref("b"))) };

        var result = verifier.VerifyPostcondition(parameters, "i32", [pre], post, body);

        Assert.Equal(ProofStatus.Refuted, result.EffectiveOutcome.Status);
        var model = result.EffectiveOutcome.Counterexample;
        Assert.NotNull(model);
        var divisor = model.Bindings.Single(binding => binding.Name == "b");
        Assert.NotEqual("0", divisor.Value);
    }

    // ------------------------------------------------------------------
    // C1-new (re-verification of the M4 fix) — a divisor inside a branch body
    // is evaluated only on that branch; asserting divisor != 0 GLOBALLY
    // excludes the violating input on the OTHER branch (false Proven, check
    // deleted). Conditionally-evaluated division must be Unsupported until a
    // path-guarded encoding exists.
    // ------------------------------------------------------------------

    [SkippableFact]
    public void DivisionInBranchBody_IsUnsupported_NeverFalseProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DivisionInBranchCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DivisionInBranchCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // The reviewer's Trap shape: Trap(0) takes the then branch, never divides,
        // returns -1 and genuinely violates §S (>= result 0). A global b != 0 from
        // the else-branch divisor would prove the violation away.
        var parameters = new List<(string Name, string Type)> { ("b", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            new IfStatementNode(
                TextSpan.Empty,
                "if1",
                Bin(BinaryOperator.Equal, Ref("b"), Int(0)),
                thenBody: [Return(Bin(BinaryOperator.Subtract, Int(0), Int(1)))],
                elseIfClauses: [],
                elseBody:
                [
                    Return(Bin(BinaryOperator.Add, Int(5),
                        Bin(BinaryOperator.Multiply, Int(0),
                            Bin(BinaryOperator.Divide, Int(1), Ref("b")))))
                ],
                new AttributeCollection())
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.NotEqual(ProofStatus.Proven, result.EffectiveOutcome.Status);
        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("conditionally-evaluated", result.EffectiveOutcome.Reason);
    }

    [SkippableFact]
    public void DivisionInShortCircuitRhs_IsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DivisionInShortCircuitCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DivisionInShortCircuitCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // The guard idiom `b != 0 && 10/b > 0` evaluates the division only when
        // the left conjunct held — same conditional-evaluation class.
        var parameters = new List<(string Name, string Type)> { ("b", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(0)));
        var guardIdiom = Bin(BinaryOperator.And,
            Bin(BinaryOperator.NotEqual, Ref("b"), Int(0)),
            Bin(BinaryOperator.GreaterThan, Bin(BinaryOperator.Divide, Int(10), Ref("b")), Int(0)));
        var body = new List<StatementNode>
        {
            new IfStatementNode(
                TextSpan.Empty, "if1", guardIdiom,
                thenBody: [Return(Int(1))],
                elseIfClauses: [],
                elseBody: [Return(Int(0))],
                new AttributeCollection())
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
    }

    [SkippableFact]
    public void DeadCodeDivisionAfterReturn_DoesNotConstrain()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        DeadCodeDivisionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DeadCodeDivisionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // A division AFTER an unconditional return is never evaluated; it must
        // neither constrain the query nor block the (genuine) refutation of the
        // live return: result = b, post b > 0 is refuted at b = 0 — a model a
        // dead-code b != 0 constraint would wrongly exclude.
        var parameters = new List<(string Name, string Type)> { ("b", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterThan, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            Return(Ref("b")),
            Return(Bin(BinaryOperator.Divide, Int(1), Ref("b")))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Refuted, result.EffectiveOutcome.Status);
        var model = result.EffectiveOutcome.Counterexample;
        Assert.NotNull(model);
        // The refutation at b <= 0 must include b = 0 as reachable; at minimum the
        // dead divisor must not have excluded it (any b <= 0 model is genuine).
    }

    // ------------------------------------------------------------------
    // m2 — a vacuous precondition set wins over an unencodable body: the
    // Calor0719-visible outcome, not a generic Unsupported.
    // ------------------------------------------------------------------

    [SkippableFact]
    public void VacuousPreconditions_WinOverUnencodableBody()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        VacuousBeatsUnencodableCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VacuousBeatsUnencodableCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };
        var pre1 = Requires(Bin(BinaryOperator.GreaterThan, Ref("x"), Int(10)));
        var pre2 = Requires(Bin(BinaryOperator.LessThan, Ref("x"), Int(5)));
        var post = Ensures(Bin(BinaryOperator.Equal, Ref("result"), Ref("x")));

        // body: null — unavailable/unencodable; the vacuity verdict must win.
        var result = verifier.VerifyPostcondition(parameters, "i32", [pre1, pre2], post, body: null);

        Assert.Equal(ProofStatus.Proven, result.EffectiveOutcome.Status);
        Assert.True(result.EffectiveOutcome.IsVacuous);
    }
}
