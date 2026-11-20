using Calor.Compiler.Ast;
using Calor.Compiler;
using Binder = Calor.Compiler.Binding.Binder;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using Xunit;
using Z3ContractVerificationResult = Calor.Compiler.Verification.Z3.ContractVerificationResult;

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

    [SkippableTheory]
    [InlineData("string.IsNullOrEmpty(s)", "")]
    [InlineData("string.Concat(s, \"d\") == \"abcd\"", "abc")]
    [InlineData("s.Contains(\"b\")", "abc")]
    [InlineData("s.StartsWith(\"a\", System.StringComparison.Ordinal)", "abc")]
    [InlineData("s.EndsWith(\"c\", System.StringComparison.Ordinal)", "abc")]
    [InlineData("s.IndexOf(\"b\", System.StringComparison.Ordinal) == 1", "abc")]
    [InlineData("s.Equals(\"abc\")", "abc")]
    [InlineData("s.Substring(1, 2) == \"bc\"", "abc")]
    [InlineData("s.Substring(1) == \"bc\"", "abc")]
    public void DirectNativeStringCalls_TranslateSelectedModels(string predicate, string input)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var condition = ParseNativePredicate(predicate);
        Assert.Contains(Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(condition).Prepend(condition),
            node => node is ExpressionCallNode);
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);
        Assert.True(translator.DeclareVariable("s", "string"));
        Assert.NotNull(translator.Translate(condition));
        var translated = translator.TranslateBoolExpr(condition);
        Assert.NotNull(translated);
        using var solver = ctx.MkSolver();
        solver.Assert(ctx.MkEq(translator.Variables["s"].Expr, ctx.MkString(input)), ctx.MkNot(translated));
        Assert.Equal(Microsoft.Z3.Status.UNSATISFIABLE, solver.Check());
    }

    [SkippableTheory]
    [InlineData("string.IsNullOrEmpty(s) && s != \"\"", "x > 0")]
    [InlineData("s.Substring(-1, 1) == \"\"", "false")]
    [InlineData("s.Substring(0, -1) == \"\"", "false")]
    [InlineData("s.Substring(-1) == \"\"", "false")]
    public void DirectNativeImplication_KeepsStringAssumptionsAndRanges(string assumed, string required)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var antecedent = ParseNativePredicate(assumed);
        var consequent = ParseNativePredicate(required);
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        (string Name, string Type)[] parameters = [("s", "string"), ("x", "int"),
            ("unusedFloat", "float"), ("unusedArray", "int[]")];
        foreach (var result in new[]
        {
            prover.ProveImplication(parameters, antecedent, consequent),
            prover.CheckPreconditionWeakening(parameters, antecedent, consequent),
            prover.CheckPostconditionStrengthening(parameters, "string", consequent, antecedent)
        })
        {
            Assert.Equal(ImplicationStatus.Unknown, result.Status);
            Assert.Equal(ProofStatus.Assumed, result.Outcome!.Status);
            Assert.Equal(new[] { Z3Verifier.StringModelAssumption }, result.Outcome.Assumptions);
            Assert.Null(result.Outcome.Counterexample);
        }
    }

    [SkippableFact]
    public void DirectNativePostconditions_KeepResultAndPreconditionScopes()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        var throwingResult = prover.CheckPostconditionStrengthening([], "string",
            ParseNativePredicate("false"), ParseNativePredicate("result.Substring(-1, 1) == \"\""));
        var nullOnlyPrecondition = prover.CheckPostconditionStrengthening(
            [("s", "string"), ("x", "int"), ("unused", "int[]")], "int",
            ParseNativePredicate("x > 0"), ParseNativePredicate("true"),
            ParseNativePredicate("string.IsNullOrEmpty(s) && s != \"\""));
        foreach (var result in new[] { throwingResult, nullOnlyPrecondition })
        {
            Assert.Equal(ImplicationStatus.Unknown, result.Status);
            Assert.Equal(ProofStatus.Assumed, result.Outcome!.Status);
            Assert.Equal(new[] { Z3Verifier.StringModelAssumption }, result.Outcome.Assumptions);
            Assert.Null(result.Outcome.Counterexample);
        }
    }

    [SkippableFact]
    public void DirectNativeConditionalSubstring_KeepsDefinednessRefusal()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        var antecedent = ParseNativePredicate("x > 0");
        var consequent = ParseNativePredicate("x > 0 || s.Substring(-1, 1) == \"\"");
        foreach (var result in new[]
        {
            prover.ProveImplication([("s", "string"), ("x", "int")], antecedent, consequent),
            prover.CheckPostconditionStrengthening([("s", "string"), ("x", "int")], "int",
                consequent, antecedent)
        })
        {
            Assert.Equal(ImplicationStatus.Unknown, result.Status);
            Assert.Equal(ProofStatus.Assumed, result.Outcome!.Status);
            Assert.Equal(new[] { Z3Verifier.ContractExpressionDivisionAssumption, Z3Verifier.StringModelAssumption },
                result.Outcome.Assumptions);
            Assert.Contains("conditionally-evaluated", result.Outcome.Reason);
            Assert.Null(result.Outcome.Counterexample);
        }
    }

    [SkippableFact]
    public void DirectNativeSubstring_KeepsCheckedOverflowInArguments()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var antecedent = ParseNativePredicate("s.Substring((2147483647 + 1) * 0, 1) == \"a\"");
        var consequent = ParseNativePredicate("false");
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        var checkedResult = prover.ProveImplication([("s", "string")], antecedent, consequent);
        Assert.Equal(ImplicationStatus.Unknown, checkedResult.Status);
        Assert.Equal(ProofStatus.Assumed, checkedResult.Outcome!.Status);
        Assert.Equal(new[] { Z3Verifier.StringModelAssumption }, checkedResult.Outcome.Assumptions);
        prover.CheckIntegerOverflow = false;
        var uncheckedResult = prover.ProveImplication([("s", "string")], antecedent, consequent);
        Assert.Equal(ImplicationStatus.Unsupported, uncheckedResult.Status);
        Assert.Null(uncheckedResult.Outcome!.Counterexample);
    }

    [SkippableTheory]
    [InlineData("String.IsNullOrEmpty(s)")]
    [InlineData("System.String.IsNullOrEmpty(s)")]
    [InlineData("@string.IsNullOrEmpty(s)")]
    [InlineData("s.Contains(value: \"a\")")]
    [InlineData("s.Contains(ref s)")]
    [InlineData("string.Concat<char>(s) == \"\"")]
    [InlineData("string.Concat(s, 1) == \"\"")]
    [InlineData("s.Substring(1, 2, 3) == \"\"")]
    [InlineData("s.StartsWith(\"a\")")]
    [InlineData("s.StartsWith(\"a\", StringComparison.Ordinal)")]
    [InlineData("s.Contains('a')")]
    [InlineData("s.Replace(\"a\", \"b\") == s")]
    [InlineData("s.Substring(1).Contains(\"a\")")]
    [InlineData("Echo(s).Contains(\"a\")")]
    public void DirectNativeCalls_KeepOwnershipAndOverloadRefusals(string predicate)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var condition = ParseNativePredicate(predicate);
        (string Name, string Type)[] parameters = [("s", "string"), ("String", "Widget"),
            ("System", "Widget"), ("StringComparison", "Widget")];
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);
        foreach (var (name, type) in parameters)
            Assert.True(translator.DeclareVariable(name, type));
        Assert.Null(translator.TranslateBoolExpr(condition));
        using var prover = new Z3ImplicationProver(ctx);
        foreach (var result in new[]
        {
            prover.ProveImplication(parameters, condition, condition),
            prover.CheckPostconditionStrengthening(parameters, "bool", condition, condition)
        })
        {
            Assert.Equal(ImplicationStatus.Unsupported, result.Status);
            Assert.Equal(ProofStatus.Unsupported, result.Outcome!.Status);
            Assert.Null(result.Outcome.Counterexample);
        }
    }

    private static ExpressionNode ParseNativePredicate(string predicate)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer($$"""
            module DirectNativeBcl
              func Probe (string:s, int:x) → int
                !
                requires {{predicate}}
                return 0
            """, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return Assert.Single(Assert.Single(module.Functions).Preconditions).Condition;
    }

    [SkippableTheory]
    [InlineData("string.IsNullOrEmpty(s)", "bool", "", "true")]
    [InlineData("string.Concat(s, \"d\")", "string", "abc", "\"abcd\"")]
    [InlineData("string.Concat(s.Substring(1), \"d\")", "string", "abc", "\"bcd\"")]
    [InlineData("s.Substring(1).Contains(\"b\")", "bool", "abc", "true")]
    [InlineData("s.StartsWith(\"a\", System.StringComparison.Ordinal)", "bool", "abc", "true")]
    public void NativeSelectedStringCalls_PreserveOutcomesRuntimeGuardsAndReparse(
        string expression, string output, string input, string expected)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        foreach (var refute in new[] { false, true })
        {
            var source = $$"""
                module NativeBclProof
                  func Probe public (string:s | (# == "{{input}}")) → {{output}}
                    ! alloc
                    => result {{(refute ? "!=" : "==")}} {{expected}}
                    return {{expression}}
                """;
            foreach (var reparse in new[] { false, true })
            {
                var (compiled, verification) = CompileNative(source);
                var binder = new Binder(new DiagnosticBag());
                binder.Bind(compiled.Ast!);
                Assert.NotEmpty(binder.BoundVerificationCalls);
                Assert.All(binder.BoundVerificationCalls.Values, call =>
                {
                    Assert.NotNull(call.ResolvedMetadataMethod);
                    Assert.Equal(SpecialType.System_String, call.ResolvedMetadataMethod.ContainingType.SpecialType);
                    Assert.False(call.ResolvedMetadataMethod.IsGenericMethod);
                    Assert.DoesNotContain(call.ResolvedMetadataMethod.Parameters, parameter => parameter.IsParams);
                });
                Assert.Equal(refute ? ProofStatus.Refuted : ProofStatus.Assumed, verification.EffectiveOutcome.Status);
                if (!refute)
                    Assert.Equal(expression.Contains("Substring", StringComparison.Ordinal)
                        ? new[] { Z3Verifier.ExceptionalPathDivisionAssumption, Z3Verifier.StringModelAssumption }
                        : new[] { Z3Verifier.StringModelAssumption }, verification.EffectiveOutcome.Assumptions);
                Assert.Contains("ContractKind.Ensures", compiled.GeneratedCode);
                Assert.DoesNotContain("// PROVEN: Postcondition", compiled.GeneratedCode);
                var unverified = Program.Compile(source, "native-bcl-proof.calr", new Calor.Compiler.CompilationOptions
                {
                    StrictEffects = true,
                    ContractMode = ContractMode.Debug,
                    StatusWriter = TextWriter.Null
                });
                Assert.False(unverified.HasErrors, string.Join("\n", unverified.Diagnostics));
                foreach (var code in new[] { compiled.GeneratedCode, unverified.GeneratedCode })
                {
                    var exception = InvokeNative(code, input);
                    if (refute)
                        Assert.IsType<Calor.Runtime.ContractViolationException>(exception);
                    else
                        Assert.Null(exception);
                }
                if (!reparse)
                    source = new CalorEmitter().Emit(compiled.Ast!);
            }
        }
    }

    [SkippableTheory]
    [InlineData("string.Concat(part, \"d\")", "let part = s.Substring(1)", "\"bcd\"", true)]
    [InlineData("part.Substring(1)", "let part = s", "\"bc\"", false)]
    public void NativeSelectedCalls_KeepImmutableSubstitutionAndDefinedness(
        string expression, string binding, string expected, bool modeled)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = $$"""
            module NativeBclProof
              func Probe public (string:s | (# == "abc")) → string
                ! alloc
                => result == {{expected}}
                {{binding}}
                return {{expression}}
            """;
        var (compiled, verification) = CompileNative(source);
        Assert.Equal(modeled ? ProofStatus.Assumed : ProofStatus.Unsupported, verification.EffectiveOutcome.Status);
        if (!modeled)
            Assert.Contains("index range could not be modeled", verification.CounterexampleDescription);
        Assert.Null(InvokeNative(compiled.GeneratedCode, "abc"));

        var (_, undefined) = CompileNative(source.Replace("Substring(1)", "Substring(5)", StringComparison.Ordinal));
        Assert.Equal(ContractVerificationStatus.Unsupported, undefined.Status);
    }

    [SkippableFact]
    public void NativeContractCalls_UseSelectedOverloadsWithoutPromotingNullableAssumptions()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var (compiled, verification) = CompileNative("""
            module NativeBclProof
              func Probe public (string:s | (# == "abc"), int[]:items) → string
                ! alloc
                => string.Concat(s, "d") == result && items.Length == items.Length
                return string.Concat(s, "d")
            """);
        Assert.Equal(ProofStatus.Assumed, verification.EffectiveOutcome.Status);
        Assert.Equal(new[] { Z3Verifier.NullableReferenceModelAssumption, Z3Verifier.StringModelAssumption },
            verification.EffectiveOutcome.Assumptions);
        Assert.Contains("ContractKind.Ensures", compiled.GeneratedCode);
    }

    [SkippableTheory]
    [InlineData("@string")]
    [InlineData("String")]
    [InlineData("@String")]
    [InlineData("System")]
    [InlineData("@System")]
    public void NativeReceiverShadows_DoNotBorrowStaticStringIdentity(string name)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var (compiled, verification) = CompileNative($$"""
            module NativeBclProof
              class Widget public
                func IsNullOrEmpty public (string:s) → bool
                  !
                  return false
              func Probe public (Widget:{{name}}, string:s | (# == "")) → bool
                !
                => result == false
                return {{name}}.IsNullOrEmpty(s)
            """);
        Assert.Equal(ContractVerificationStatus.Unsupported, verification.Status);
        Assert.Contains("ContractKind.Ensures", compiled.GeneratedCode);
    }

    [SkippableFact]
    public void StandaloneNativeCalls_RequireSuppliedReceiverOwnership()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        foreach (var receiver in new[] { "String", "System.String", "@string" })
        {
            var diagnostics = new DiagnosticBag();
            var module = new Parser(new Lexer($$"""
                module NativeBclProof
                  func Probe (string:s) → bool
                    !
                    return {{receiver}}.IsNullOrEmpty(s)
                """, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors);
            var call = Assert.IsType<ExpressionCallNode>(Assert.IsType<ReturnStatementNode>(
                Assert.Single(module.Functions).Body[0]).Expression);
            var result = verifier.VerifyPostcondition([("s", "str")], "bool",
                [Requires(Equal(Ref("s"), Str("")))], Ensures(Equal(Ref("result"), new BoolLiteralNode(default, true))),
                [new ReturnStatementNode(default, call)]);
            Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
        }
        var escapedLegacy = verifier.VerifyPrecondition([], Requires(Call("@string.IsNullOrEmpty", Str(""))));
        Assert.Equal(ContractVerificationStatus.Unsupported, escapedLegacy.Status);
    }

    [SkippableTheory]
    [InlineData("string.Concat<char>(s)")]
    [InlineData("string.Concat(ref s, \"d\")")]
    [InlineData("string.Concat(str0: s, str1: \"d\")")]
    public void ParsedNativeCallModifiers_AreOutsideTheBoundedModel(string expression)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer($$"""
            module NativeBclProof
              func Probe (string:s) → string
                !
                return {{expression}}
            """, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        var call = Assert.IsType<ExpressionCallNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(module.Functions).Body[0]).Expression);
        Assert.True(call.TypeArguments?.Count > 0
            || call.ArgumentNames?.Any(name => name != null) == true
            || call.ArgumentModifiers?.Any(modifier => modifier != null) == true);
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var result = verifier.VerifyPostcondition([("s", "str")], "str",
            [Requires(Equal(Ref("s"), Str("abc")))], Ensures(Equal(Ref("result"), Str("abcd"))),
            [new ReturnStatementNode(default, call)]);
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [SkippableTheory]
    [InlineData("@string")]
    [InlineData("String")]
    [InlineData("@String")]
    [InlineData("System")]
    [InlineData("@System")]
    public void NativeSourceShadows_DoNotBorrowStringModels(string name)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = $$"""
            module NativeBclProof NativeBclProof
              class {{name}} public
                func IsNullOrEmpty public static (string:s) → bool
                  !
                  return false
              func Probe public (string:s | (# == "")) → bool
                !
                => result == false
                return {{name}}.IsNullOrEmpty(s)
            """;
        var (compiled, verification) = CompileNative(source);
        Assert.Equal(ContractVerificationStatus.Unsupported, verification.Status);
        Assert.Contains("ContractKind.Ensures", compiled.GeneratedCode);
        Assert.Null(InvokeNative(compiled.GeneratedCode, ""));
    }

    [SkippableTheory]
    [InlineData("s.StartsWith(\"a\")")]
    [InlineData("s.EndsWith(\"c\", System.StringComparison.OrdinalIgnoreCase)")]
    [InlineData("s.Contains('b')")]
    [InlineData("s.Contains(value: \"b\")")]
    [InlineData("string.Concat(s, 1).Contains(\"b\")")]
    [InlineData("Echo(s).Contains(\"b\")")]
    [InlineData("string.IsNullOrEmpty(Echo(s))")]
    public void NativeUnmodeledCalls_KeepRuntimeGuards(string expression)
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        var source = $$"""
            module NativeBclProof
              func Echo (string:s) → string
                !
                return s
              func Probe public (string:s | (# == "abc")) → bool
                ! alloc
                => result == result
                return {{expression}}
            """;
        var (compiled, verification) = CompileNative(source);
        Assert.Equal(ContractVerificationStatus.Unsupported, verification.Status);
        Assert.Contains("ContractKind.Ensures", compiled.GeneratedCode);
        Assert.DoesNotContain("// PROVEN: Postcondition", compiled.GeneratedCode);
    }

    private static (CompilationResult Compilation, Z3ContractVerificationResult Verification) CompileNative(string source)
    {
        var options = new Calor.Compiler.CompilationOptions
        {
            VerifyContracts = true,
            StrictEffects = true,
            ContractMode = ContractMode.Debug,
            ElideProvenGuards = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
            StatusWriter = TextWriter.Null
        };
        var compiled = Program.Compile(source, "native-bcl-proof.calr", options);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        return (compiled, Assert.Single(Assert.Single(options.VerificationResults!.Functions).PostconditionResults));
    }

    private static Exception? InvokeNative(string code, string input)
    {
        var compilation = CSharpCompilation.Create("NativeBcl_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var probe = Assert.Single(assembly.GetTypes().Select(type => type.GetMethod("Probe")).OfType<MethodInfo>());
        try
        {
            probe.Invoke(null, [input]);
            return null;
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException != null)
        {
            return invocation.InnerException;
        }
    }
}
