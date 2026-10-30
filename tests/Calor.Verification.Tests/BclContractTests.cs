using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Verification.Tests;

public class BclContractTests
{
    private static ReferenceNode Ref(string name) => new(TextSpan.Empty, name);
    private static StringLiteralNode Str(string value) => new(TextSpan.Empty, value);
    private static IntLiteralNode Int(int value) => new(TextSpan.Empty, value);
    private static CallExpressionNode Call(string target, params ExpressionNode[] args) => new(TextSpan.Empty, target, args);
    private static BinaryOperationNode Equal(ExpressionNode left, ExpressionNode right) =>
        new(TextSpan.Empty, BinaryOperator.Equal, left, right);
    private static RequiresNode Requires(ExpressionNode condition) => new(TextSpan.Empty, condition, null, new());
    private static EnsuresNode Ensures(ExpressionNode condition) => new(TextSpan.Empty, condition, null, new());

    public static IEnumerable<object[]> StringModels()
    {
        yield return [StringOp.Length, Ref("s.Length"), new ExpressionNode[] { Ref("s") }, Str("abc"), Int(3), null!];
        yield return [StringOp.Contains, Call("s.Contains", Str("b")), new ExpressionNode[] { Ref("s"), Str("b") }, Str("abc"), new BoolLiteralNode(default, true), null!];
        yield return [StringOp.StartsWith, Call("s.StartsWith", Str("a"), Ref("StringComparison.Ordinal")), new ExpressionNode[] { Ref("s"), Str("a") }, Str("abc"), new BoolLiteralNode(default, true), StringComparisonMode.Ordinal];
        yield return [StringOp.EndsWith, Call("s.EndsWith", Str("c"), Ref("System.StringComparison.Ordinal")), new ExpressionNode[] { Ref("s"), Str("c") }, Str("abc"), new BoolLiteralNode(default, true), StringComparisonMode.Ordinal];
        yield return [StringOp.IndexOf, Call("s.IndexOf", Str("b"), Ref("StringComparison.Ordinal")), new ExpressionNode[] { Ref("s"), Str("b") }, Str("abc"), Int(1), StringComparisonMode.Ordinal];
        yield return [StringOp.Equals, Call("s.Equals", Str("abc")), new ExpressionNode[] { Ref("s"), Str("abc") }, Str("abc"), new BoolLiteralNode(default, true), null!];
        yield return [StringOp.IsNullOrEmpty, Call("string.IsNullOrEmpty", Ref("s")), new ExpressionNode[] { Ref("s") }, Str(""), new BoolLiteralNode(default, true), null!];
        yield return [StringOp.Concat, Call("string.Concat", Ref("s"), Str("d")), new ExpressionNode[] { Ref("s"), Str("d") }, Str("abc"), Str("abcd"), null!];
        yield return [StringOp.Substring, Call("s.Substring", Int(1), Int(2)), new ExpressionNode[] { Ref("s"), Int(1), Int(2) }, Str("abc"), Str("bc"), null!];
        yield return [StringOp.SubstringFrom, Call("s.Substring", Int(1)), new ExpressionNode[] { Ref("s"), Int(1) }, Str("abc"), Str("bc"), null!];
    }

    [SkippableTheory]
    [MemberData(nameof(StringModels))]
    public void BclSpelling_PreservesLegacyProofAndRefutation(
        StringOp operation, ExpressionNode bcl, ExpressionNode[] args,
        StringLiteralNode input, ExpressionNode expected, StringComparisonMode? mode)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var legacy = new StringOperationNode(default, operation, args, mode);
        foreach (var negate in new[] { false, true })
        {
            ExpressionNode Condition(ExpressionNode value) => negate
                ? new UnaryOperationNode(default, UnaryOperator.Not, Equal(value, expected))
                : Equal(value, expected);
            var preconditions = new[] { Requires(Equal(Ref("s"), input)) };
            var oldResult = verifier.VerifyPostcondition([("s", "str")], null, preconditions, Ensures(Condition(legacy)));
            var newResult = verifier.VerifyPostcondition([("s", "str")], null, preconditions, Ensures(Condition(bcl)));
            Assert.NotEqual(ProofStatus.Unknown, oldResult.EffectiveOutcome.Status);
            Assert.Equal(negate ? ProofStatus.Refuted : ProofStatus.Assumed, oldResult.EffectiveOutcome.Status);
            Assert.True(oldResult.Status == newResult.Status,
                $"Legacy: {oldResult.Status}; BCL: {newResult.Status}: {newResult.CounterexampleDescription}");
            Assert.Equal(oldResult.EffectiveOutcome.Status, newResult.EffectiveOutcome.Status);
            Assert.Equal(oldResult.EffectiveOutcome.Assumptions, newResult.EffectiveOutcome.Assumptions);
        }
    }

    [SkippableTheory]
    [InlineData("StartsWith")]
    [InlineData("EndsWith")]
    [InlineData("IndexOf")]
    public void CultureSensitiveOverloads_RemainUnsupported(string method)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        foreach (var comparison in new[] { "", "StringComparison.OrdinalIgnoreCase", "StringComparison.CurrentCulture" })
        {
            var call = comparison == "" ? Call($"s.{method}", Str("a"))
                : Call($"s.{method}", Str("a"), Ref(comparison));
            var result = verifier.VerifyPrecondition([("s", "str")],
                Requires(Equal(call, method == "IndexOf" ? Int(0) : new BoolLiteralNode(default, true))));
            Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        }
    }

    [SkippableTheory]
    [InlineData("i32[]", "Length")]
    [InlineData("List<i32>", "Count")]
    [InlineData("System.Collections.Generic.List<int>", "Count")]
    [InlineData("List<str>", "Count")]
    public void BclSequenceSize_WorksInQuantifiers(string type, string member)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var size = Ref($"items.{member}");
        var condition = new ForallExpressionNode(default, [new QuantifierVariableNode(default, "i", "i32")],
            new BinaryOperationNode(default, BinaryOperator.Or,
                new BinaryOperationNode(default, BinaryOperator.LessThan, Ref("i"), size),
                new BinaryOperationNode(default, BinaryOperator.GreaterOrEqual, Ref("i"), size)));
        var result = verifier.VerifyPostcondition([("items", type)], null, [], Ensures(condition));
        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
        Assert.Contains(Z3Verifier.NullableReferenceModelAssumption, result.EffectiveOutcome.Assumptions);
    }

    [SkippableTheory]
    [InlineData("str")]
    [InlineData("i32[]")]
    [InlineData("List<i32>")]
    public void BclSize_IsSignedInt32AndNonnegative(string type)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var member = type.StartsWith("List", StringComparison.Ordinal) ? "Count" : "Length";
        var result = verifier.VerifyPrecondition([("items", type)], Requires(
            new BinaryOperationNode(default, BinaryOperator.LessThan, Ref($"items.{member}"), Int(0))));
        Assert.Equal(ContractVerificationStatus.Disproven, result.Status);
    }

    [SkippableFact]
    public void SameMethodName_OnUserReceiver_IsNotModeled()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var result = verifier.VerifyPrecondition([("s", "Widget")], Requires(Call("s.Contains", Str("a"))));
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [SkippableFact]
    public void SubstringBody_PreservesDefinednessAndResultBinding()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var preconditions = new[] { Requires(Equal(Ref("s"), Str("abc"))) };
        var postcondition = Ensures(Equal(Ref("result"), Str("bc")));
        var legacy = new StringOperationNode(default, StringOp.Substring, [Ref("s"), Int(1), Int(2)]);
        var oldResult = verifier.VerifyPostcondition([("s", "str")], "str", preconditions, postcondition,
            [new ReturnStatementNode(default, legacy)]);
        var newResult = verifier.VerifyPostcondition([("s", "str")], "str", preconditions, postcondition,
            [new ReturnStatementNode(default, Call("s.Substring", Int(1), Int(2)))]);
        Assert.Equal(ProofStatus.Assumed, newResult.EffectiveOutcome.Status);
        Assert.Equal(oldResult.EffectiveOutcome.Status, newResult.EffectiveOutcome.Status);
        Assert.Equal(oldResult.EffectiveOutcome.Assumptions, newResult.EffectiveOutcome.Assumptions);
    }

    [SkippableFact]
    public void UnsupportedOverloads_DoNotDropOperandsOrGuessConversions()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        ExpressionNode[] calls =
        [
            Call("s.Substring", new IntLiteralNode(default, 1) { IsLong = true }),
            Call("s.Substring", Int(0), Int(1), Int(2)),
            Call("s.IndexOf", Str("a"), Int(0), Ref("StringComparison.Ordinal")),
            Call("string.Concat", Ref("s"), Int(1)),
            Call("s.Replace", Str("a"), Str("b")),
            new CallExpressionNode(default, "s.Contains", [Str("a")], ["value"]),
            new CallExpressionNode(default, "s.Contains", [Str("a")], null, ["ref"])
        ];
        foreach (var call in calls)
        {
            var result = verifier.VerifyPrecondition([("s", "str")], Requires(Equal(call, call)));
            Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        }
    }

    [SkippableTheory]
    [InlineData("String")]
    [InlineData("List")]
    public void InModuleTypeNames_AreNotTreatedAsBclTypes(string type)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx, userTypeRegistry:
            new Dictionary<string, IReadOnlyDictionary<string, string>> { [type] = new Dictionary<string, string>() });
        var result = type == "String"
            ? verifier.VerifyPrecondition([], Requires(Call("String.IsNullOrEmpty", Str(""))))
            : verifier.VerifyPrecondition([("items", "List<i32>")], Requires(Equal(Ref("items.Count"), Int(0))));
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [SkippableFact]
    public void ShadowedOrdinalName_IsNotAnOrdinalConstant()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var result = verifier.VerifyPrecondition([("s", "str"), ("StringComparison", "Widget")],
            Requires(Call("s.StartsWith", Str("a"), Ref("StringComparison.Ordinal"))));
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [SkippableFact]
    public void NestedBclCalls_AreModeledThroughSelectedStringOverloads()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var condition = Equal(Call("string.Concat", Call("s.Substring", Int(1)), Str("d")), Str("bcd"));
        var result = verifier.VerifyPostcondition([("s", "str")], null,
            [Requires(Equal(Ref("s"), Str("abc")))], Ensures(condition));
        Assert.Equal(ProofStatus.Assumed, result.EffectiveOutcome.Status);
    }

    [SkippableFact]
    public void ConditionalSubstring_RetainsExistingDefinednessRefusal()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var condition = new BinaryOperationNode(default, BinaryOperator.Or, Ref("skip"),
            Equal(Call("s.Substring", Int(1)), Str("")));
        var result = verifier.VerifyPostcondition([("s", "str"), ("skip", "bool")], null, [], Ensures(condition));
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        Assert.Contains("conditionally-evaluated", result.CounterexampleDescription);
    }

    [SkippableFact]
    public void ExplicitlyDeclaredArray_SupportsBothPropertyAstShapes()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);
        Assert.True(translator.DeclareArrayVariable("a", "i32"));
        var dotted = translator.Translate(Ref("a.Length"));
        var field = translator.Translate(new FieldAccessNode(default, Ref("a"), "Length"));
        Assert.NotNull(dotted);
        Assert.NotNull(field);
        Assert.Equal(dotted.ToString(), field.ToString());
    }
}
