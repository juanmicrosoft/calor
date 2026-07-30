using System.Runtime.CompilerServices;
using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// D-G3.1 (guarantees plan G4): immutable §B bindings encode via SSA-style
/// substitution. Pins the soundness edges: mutable bindings refuse, use before
/// declaration refuses, branch-local bindings don't leak into fall-through,
/// rebinding shadows lexically, an unused dividing initializer still produces
/// the exceptional-path assumption, and the caps hold.
/// </summary>
public class BindingEncodingTests
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
    private static BindStatementNode Bind(string name, ExpressionNode init, bool mutable = false)
        => new(TextSpan.Empty, name, "i32", mutable, init, new AttributeCollection());
    private static IfStatementNode If(ExpressionNode cond, List<StatementNode> then, List<StatementNode>? els = null)
        => new(TextSpan.Empty, "if1", cond, then, [], els, new AttributeCollection());

    [SkippableFact]
    public void GuardClauseBindingChain_Proves()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        GuardClauseCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GuardClauseCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // The W5B shape: total = a + b; if (total > cap) return cap; return total
        // ⊨ result <= cap.
        var parameters = new List<(string Name, string Type)> { ("a", "i32"), ("b", "i32"), ("cap", "i32") };
        var post = Ensures(Bin(BinaryOperator.LessOrEqual, Ref("result"), Ref("cap")));
        var body = new List<StatementNode>
        {
            Bind("total", Bin(BinaryOperator.Add, Ref("a"), Ref("b"))),
            If(Bin(BinaryOperator.GreaterThan, Ref("total"), Ref("cap")), [Return(Ref("cap"))]),
            Return(Ref("total"))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Proven, result.EffectiveOutcome.Status);
    }

    [SkippableFact]
    public void MutableBinding_IsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        MutableCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MutableCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("a", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            Bind("t", Ref("a"), mutable: true),
            Return(Ref("t"))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("mutable", result.EffectiveOutcome.Reason);
    }

    [SkippableFact]
    public void UseBeforeDeclaration_IsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        UseBeforeDeclCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UseBeforeDeclCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // return t; §B{t} — flow order matters; the walker must refuse.
        var parameters = new List<(string Name, string Type)> { ("a", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            Return(Ref("t")),
            Bind("t", Ref("a"))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("'t'", result.EffectiveOutcome.Reason);
    }

    [SkippableFact]
    public void BranchLocalBinding_DoesNotLeakIntoFallThrough()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        BranchLocalCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BranchLocalCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // if (a > 0) { §B{t} = 1; return t; }  return t;   ← trailing t is UNBOUND
        var parameters = new List<(string Name, string Type)> { ("a", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            If(Bin(BinaryOperator.GreaterThan, Ref("a"), Int(0)),
               [Bind("t", Int(1)), Return(Ref("t"))]),
            Return(Ref("t"))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
    }

    [SkippableFact]
    public void RebindingInBranch_ShadowsLexically()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ShadowCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShadowCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // §B{t}=1; if (a>0) { §B{t}=2; return t; } return t;
        // then-branch returns 2, fall-through returns 1 ⊨ result >= 1 (proven),
        // and result >= 2 is refutable (fall-through path) — both directions pin
        // that the right binding is seen on each path.
        var parameters = new List<(string Name, string Type)> { ("a", "i32") };
        var body = new List<StatementNode>
        {
            Bind("t", Int(1)),
            If(Bin(BinaryOperator.GreaterThan, Ref("a"), Int(0)),
               [Bind("t", Int(2)), Return(Ref("t"))]),
            Return(Ref("t"))
        };

        var proven = verifier.VerifyPostcondition(parameters, "i32", [],
            Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(1))), body);
        Assert.Equal(ProofStatus.Proven, proven.EffectiveOutcome.Status);

        var refuted = verifier.VerifyPostcondition(parameters, "i32", [],
            Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(2))), body);
        Assert.Equal(ProofStatus.Refuted, refuted.EffectiveOutcome.Status);
    }

    [SkippableFact]
    public void UnusedDividingInitializer_StillYieldsAssumed()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        UnusedDivisionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnusedDivisionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // §B{probe} = 100 / b; return a;  — probe unused, but the division still
        // evaluates (and can throw) at runtime; the proof must carry the
        // exceptional-path assumption, not plain Proven.
        var parameters = new List<(string Name, string Type)> { ("a", "i32"), ("b", "i32") };
        var pre = Requires(Bin(BinaryOperator.GreaterOrEqual, Ref("a"), Int(0)));
        var post = Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            Bind("probe", Bin(BinaryOperator.Divide, Int(100), Ref("b"))),
            Return(Ref("a"))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [pre], post, body);

        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
        Assert.Contains(result.EffectiveOutcome.Assumptions,
            assumption => assumption.StartsWith("exceptional-paths:division", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void DivisionInBranchLocalBinding_IsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        BranchDivisionCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BranchDivisionCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // A dividing initializer INSIDE a branch is conditionally evaluated —
        // the same rule as branch-body division (no global side condition).
        var parameters = new List<(string Name, string Type)> { ("a", "i32"), ("b", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            If(Bin(BinaryOperator.GreaterThan, Ref("a"), Int(0)),
               [Bind("q", Bin(BinaryOperator.Divide, Int(100), Ref("b"))), Return(Ref("q"))]),
            Return(Int(0))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("conditionally-evaluated", result.EffectiveOutcome.Reason);
    }

    [SkippableFact]
    public void WideningBindingType_IsUnsupported_NeverFalseProven()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        WideningBindingCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WideningBindingCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // #824 review C1: §B{t:i64} INT:MaxValue — the annotation makes t+1
        // 64-bit at runtime while substitution encodes 32-bit-wrapped, minting a
        // false Proven that deleted the runtime check. Width-changing binding
        // annotations must refuse.
        var parameters = new List<(string Name, string Type)> { ("a", "i32") };
        var post = Ensures(Bin(BinaryOperator.Equal, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            new BindStatementNode(TextSpan.Empty, "t", "i64", false, Int(int.MaxValue), new AttributeCollection()),
            If(Bin(BinaryOperator.GreaterThan, Bin(BinaryOperator.Add, Ref("t"), Int(1)), Int(0)),
               [Return(Int(1))]),
            Return(Int(0))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.NotEqual(ProofStatus.Proven, result.EffectiveOutcome.Status);
        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("width", result.EffectiveOutcome.Reason);
    }

    [SkippableFact]
    public void BindingShadowingParameter_IsUnsupported()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        ShadowParamCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShadowParamCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        // #824 review M1: a binding named like a parameter desynchronizes the
        // divisor collector (unsubstituted trees) from the encoder — refuse
        // (Calor0255 forbids the shape in legal source; this covers raw-AST callers).
        var parameters = new List<(string Name, string Type)> { ("p", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(0)));
        var body = new List<StatementNode>
        {
            Bind("p", Bin(BinaryOperator.Subtract, Ref("p"), Int(1))),
            Return(Bin(BinaryOperator.Divide, Int(100), Ref("p")))
        };

        var result = verifier.VerifyPostcondition(parameters, "i32", [], post, body);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("shadows a parameter", result.EffectiveOutcome.Reason);
    }

    [Fact]
    public void HashPostcondition_DistinguishesStringOperationContent()
    {
        // #824 review C2: (len s) vs (len u) must never share a key — the
        // content-free marker let a stale false Proven serve from cache.
        var hasher = new Calor.Compiler.Verification.Z3.Cache.ContractHasher();
        var parameters = new List<(string Name, string TypeName)> { ("s", "str"), ("u", "str") };
        var post = Ensures(Bin(BinaryOperator.Equal, Ref("result"),
            new StringOperationNode(TextSpan.Empty, StringOp.Length, [Ref("s")])));

        string HashForBody(string arg) =>
            hasher.HashPostcondition(parameters, "i32", [], post,
                [Return(new StringOperationNode(TextSpan.Empty, StringOp.Length, [Ref(arg)]))]);

        Assert.NotEqual(HashForBody("s"), HashForBody("u"));
    }

    [Fact]
    public void HashPostcondition_DistinguishesInitializers_AndMutability()
    {
        // Two bodies differing only in a §B initializer must never share a key;
        // the mutable spelling must hash distinctly from the immutable one.
        var hasher = new Calor.Compiler.Verification.Z3.Cache.ContractHasher();
        var parameters = new List<(string Name, string TypeName)> { ("a", "i32") };
        var post = Ensures(Bin(BinaryOperator.GreaterOrEqual, Ref("result"), Int(0)));

        string HashFor(ExpressionNode init, bool mutable = false) =>
            hasher.HashPostcondition(parameters, "i32", [], post,
                [Bind("t", init, mutable), Return(Ref("t"))]);

        Assert.NotEqual(HashFor(Int(1)), HashFor(Int(2)));
        Assert.NotEqual(HashFor(Int(1)), HashFor(Int(1), mutable: true));
    }
}
