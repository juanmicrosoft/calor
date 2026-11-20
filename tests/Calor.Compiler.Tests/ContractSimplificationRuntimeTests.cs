using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class ContractSimplificationRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuantifierDiscovery_PreservesShortCircuitAndEndpointOrder(bool verify)
    {
        string Guarded(string predicate) => $$"""
            module TypedContracts TypedContracts block
              func Check (bool:guard, int:x) → int
                !
                requires {{predicate}}
                return 7
            """;
        var all = Compile(Guarded(
            "(forall int:i where (guard && ((i >= (1 / x)) && (i < 2))) : true)"), verify);
        var any = Compile(Guarded(
            "(exists int:i where guard : ((i >= (1 / x)) && (i < 2)))"), verify);
        Assert.Equal(7, InvokeArguments(all, false, 0));
        AssertContractViolation(() => InvokeArguments(any, false, 0));
        Assert.Equal(7, InvokeArguments(all, true, 1));
        Assert.Equal(7, InvokeArguments(any, true, 1));
        foreach (var assembly in new[] { all, any })
        {
            var error = Assert.Throws<TargetInvocationException>(() => InvokeArguments(assembly, true, 0));
            Assert.IsType<DivideByZeroException>(error.InnerException);
        }

        var emptyPrefix = Compile(Function("int",
            "(forall int:i where ((i > 2147483647) && (i < (1 / x))) : false)"), verify);
        Assert.Equal(7, Invoke(emptyPrefix, 0));
        var earlierThrow = Compile(Function("int",
            "(forall int:i where ((i < (1 / x)) && (i > 2147483647)) : false)"), verify);
        Assert.IsType<DivideByZeroException>(
            Assert.Throws<TargetInvocationException>(() => Invoke(earlierThrow, 0)).InnerException);

        var betweenBounds = Compile(Guarded(
            "(forall int:i where ((i >= 0) && (guard && (i < (1 / x)))) : true)"), verify);
        Assert.Equal(7, InvokeArguments(betweenBounds, false, 0));
        Assert.IsType<DivideByZeroException>(
            Assert.Throws<TargetInvocationException>(() => InvokeArguments(betweenBounds, true, 0)).InnerException);
        var nested = Compile(Function("int",
            "(forall int:i, int:j where ((i >= 0) && ((i < x) && ((j >= (1 / x)) && (j < (i + 2))))) : true)"), verify);
        Assert.Equal(7, Invoke(nested, 0));
        Assert.Equal(7, Invoke(nested, 2));
        var nullableArray = Compile("""
            module TypedContracts TypedContracts block
              class NullableDomain
                func Check (int[]?:x) → int
                  !
                  requires (forall int:i where ((x != null) && ((i >= 0) && (i < x.Length))) : true)
                  return 7
            """, verify, rejectNullableWarnings: true);
        var domainType = nullableArray.GetType("TypedContracts.NullableDomain")!;
        var domain = Activator.CreateInstance(domainType);
        Assert.Equal(7, domainType.GetMethod("Check")!.Invoke(domain, [null]));
        Assert.Equal(7, domainType.GetMethod("Check")!.Invoke(domain, [new[] { 1, 2 }]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quantifiers_KeepEveryConjunctAndGroupedBounds(bool verify)
    {
        var falseExists = Compile(Function("int",
            "(exists int:i where (i >= 0) : ((i < 1) && ((i == i) && false)))"), verify);
        AssertContractViolation(() => Invoke(falseExists, 0));

        var throwingExists = Compile(Function("int",
            "(exists int:i where (i >= 0) : ((i < 1) && ((i == i) && ((1 / x) > 0))))"), verify);
        var exception = Assert.Throws<TargetInvocationException>(() => Invoke(throwingExists, 0));
        Assert.IsType<DivideByZeroException>(exception.InnerException);

        var forall = Compile(Function("int",
            "(forall int:i in (x - x)..2 : (i < 1))"), verify);
        var exists = Compile(Function("int",
            "(exists int:i where (i >= (x - x)) : ((i < 2) && (i == 1)))"), verify);
        var multi = Compile(Function("int",
            "(exists int:i, int:j where (i >= (x - x)) : ((i < 2) && ((j >= (x - x)) && ((j < 2) && ((i == j) && (j == 1))))))"), verify);
        foreach (var value in new[] { -2, 0, 1, 2 })
        {
            AssertContractViolation(() => Invoke(forall, value));
            Assert.Equal(7, Invoke(exists, value));
            Assert.Equal(7, Invoke(multi, value));
        }

        var emptyAll = Compile(Function("int",
            "(forall int:i in 1..0 : true)"), verify);
        var emptyAny = Compile(Function("int",
            "(exists int:i where (i >= 1) : ((i < 0) && true))"), verify);
        Assert.Equal(7, Invoke(emptyAll, 0));
        AssertContractViolation(() => Invoke(emptyAny, 0));

        var shortAll = Compile(Function("int",
            "(forall int:i where (false && ((i >= (1 / x)) && (i < 2))) : true)"), verify);
        var shortAny = Compile(Function("int",
            "(exists int:i where false : ((i >= (1 / x)) && (i < 2)))"), verify);
        Assert.Equal(7, Invoke(shortAll, 0));
        AssertContractViolation(() => Invoke(shortAny, 0));
        Assert.Equal(new[] { int.MinValue, int.MinValue + 1, int.MinValue + 2 },
            Calor.Runtime.ContractQuantifier.Range(int.MinValue, int.MaxValue).Take(3));
        Assert.Equal(new[] { int.MaxValue - 1 },
            Calor.Runtime.ContractQuantifier.Range(int.MaxValue - 1, int.MaxValue));
        Assert.Equal(new[] { int.MaxValue },
            Calor.Runtime.ContractQuantifier.Range(int.MaxValue, (long)int.MaxValue + 1));
        Assert.Empty(Calor.Runtime.ContractQuantifier.Range((long)int.MaxValue + 1, long.MaxValue));

        var inclusive = Compile(Function("int",
            "(forall int:i where ((i >= x) && (i <= x)) : (i < 0))"), verify);
        AssertContractViolation(() => Invoke(inclusive, int.MaxValue));
        AssertContractViolation(() => Invoke(inclusive, int.MaxValue - 1));
        Assert.Equal(7, Invoke(inclusive, int.MinValue));
        var strict = Compile(Function("int",
            "(exists int:i where (i > x) : (i <= 2147483647))"), verify);
        AssertContractViolation(() => Invoke(strict, int.MaxValue));
        Assert.Equal(7, Invoke(strict, int.MaxValue - 1));
        var wideEndpoint = Compile(Function("long",
            "(forall int:i where ((i >= 0) && (i <= x)) : (i < 0))"), verify);
        AssertContractViolation(() => Invoke(wideEndpoint, long.MaxValue));
        Assert.Equal(7, Invoke(wideEndpoint, long.MinValue));
        (string Type, object Min, object Max)[] domains =
        [
            ("sbyte", sbyte.MinValue, sbyte.MaxValue), ("byte", byte.MinValue, byte.MaxValue),
            ("short", short.MinValue, short.MaxValue), ("ushort", ushort.MinValue, ushort.MaxValue),
            ("int", int.MinValue, int.MaxValue), ("uint", uint.MinValue, uint.MaxValue),
            ("long", long.MinValue, long.MaxValue), ("ulong", ulong.MinValue, ulong.MaxValue)
        ];
        foreach (var (type, min, max) in domains)
        {
            var singleton = Compile(Function(type,
                $"(forall {type}:i where ((i >= x) && (i <= x)) : false)"), verify);
            AssertContractViolation(() => Invoke(singleton, min));
            AssertContractViolation(() => Invoke(singleton, max));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatternOperand_ContainsCompleteEquality(bool verify)
    {
        var assembly = Compile(Function("bool", "(((x == x) is bool) == false)"), verify);
        AssertContractViolation(() => Invoke(assembly, false));
        AssertContractViolation(() => Invoke(assembly, true));
        var compound = Compile(Function("object", "((((x is int) || false) == true) && false)"), verify);
        AssertContractViolation(() => Invoke(compound, 5));
        AssertContractViolation(() => Invoke(compound, "text"));

        var overloaded = Compile("""
            module TypedContracts TypedContracts block
              class Probe
                op == (Probe:a, Probe:b) → Probe
                  !
                  return a
                op != (Probe:a, Probe:b) → Probe
                  !
                  return a
                op == (Probe:a, bool:b) → bool
                  !
                  return false
                op != (Probe:a, bool:b) → bool
                  !
                  return true
                op ! (Probe:a) → bool
                  !
                  return false
              func Check (Probe:x) → int
                !
                requires ((x == ((x is Probe candidate) ? x : x)) == true)
                return 7
            """, verify);
        var probe = Activator.CreateInstance(Assert.Single(overloaded.GetTypes(), type => type.Name == "Probe"));
        AssertContractViolation(() => Invoke(overloaded, probe));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullablePayloads_UseExpressionTypesNotLiteralShapes(bool verify)
    {
        string[] predicates =
        [
            "(((object)(x == x)) is bool)",
            "(((object)(x - x)) is int)",
            "(((object)2147483648L) is long)"
        ];
        foreach (var predicate in predicates)
        {
            var assembly = Compile(Function("int", $"({predicate} == true)"), verify);
            Assert.Equal(7, Invoke(assembly, 3));
        }

        foreach (var body in new[]
        {
            "return x",
            "return ((x > 0) ? x : null)"
        })
        {
            var source = $$"""
                module TypedContracts TypedContracts block
                  func Check (int:x) → long?
                    !
                    => true
                    {{body}}
                """;
            var assembly = Compile(source, verify);
            Assert.Equal(3L, Assert.IsType<long>(Invoke(assembly, 3)));
            if (body != "return x")
                Assert.Null(Invoke(assembly, -1));
        }

        var nullable = Compile("""
            module TypedContracts TypedContracts block
              func Check (int:x) → long?
                !
                return x
            """, verify);
        Assert.Equal(3L, Assert.IsType<long>(Invoke(nullable, 3)));
        var lambda = Compile("""
            module TypedContracts TypedContracts block
              func Check (int:x) → Func<long?> !
                !
                return () =>
                  return x
            """, verify);
        var factory = Assert.IsType<Func<long?>>(Invoke(lambda, 3));
        Assert.Equal(3L, factory());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatternBinding_SurvivesBooleanComparisonWrappers(bool verify)
    {
        string[] wrappers =
        [
            "((result is int result) == true)",
            "(false != (result is int result))",
            "(!((result is int result) == false))",
            "(((result is int result) == true) == true)",
            "(((result is int result) && (result > 0)) == true)",
            "(false != ((result is int result) && (result == 5)))"
        ];
        foreach (var wrapper in wrappers)
        {
            var source = $$"""
                module TypedContracts TypedContracts block
                  func Check (object:x) → object
                    !
                    => ({{wrapper}} && (result > 0))
                    return x
                """;
            var assembly = Compile(source, verify);
            Assert.Equal(5, Invoke(assembly, 5));
            AssertContractViolation(() => Invoke(assembly, 0));
            AssertContractViolation(() => Invoke(assembly, "not an integer"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReceiversAndIndices_GroupRetainedArithmetic(bool verify)
    {
        var receiver = Compile(Function("int?", "((x + 0) . Value == 3)"), verify);
        Assert.Equal(7, Invoke(receiver, 3));
        AssertContractViolation(() => Invoke(receiver, 2));
        var exception = Assert.Throws<TargetInvocationException>(() => Invoke(receiver, null));
        Assert.IsType<InvalidOperationException>(exception.InnerException);

        var index = Compile(Function("int", "((\"abcd\")[(^(x + 0))] == ((char)98))"), verify);
        Assert.Equal(7, Invoke(index, 3));
        AssertContractViolation(() => Invoke(index, 2));
        var range = Compile(Function("int", "((\"abcd\")[((x + 0)..4)] == \"d\")"), verify);
        Assert.Equal(7, Invoke(range, 3));
        AssertContractViolation(() => Invoke(range, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryReceivers_PreserveBaseAndConditionalChains(bool verify)
    {
        const string source = """
            module ReceiverChains ReceiverChains block
              class Parent
                field int:Value

              class Derived
                extends Parent

                func Read () → int
                  !
                  return base . Value

              class Node
                field Node:Child
                field int:Value

              func Check (Node:x) → int?
                !
                return x?.Child . Value
            """;
        const string harness = """
            public static class Caller
            {
                public static object?[] Run()
                {
                    var child = new ReceiverChains.Derived { Value = 9 };
                    var node = new ReceiverChains.Node { Child = new ReceiverChains.Node { Value = 3 } };
                    return [child.Read(), ReceiverChains.ReceiverChainsModule.Check(null),
                        ReceiverChains.ReceiverChainsModule.Check(node)];
                }
            }
            """;
        var assembly = Compile(source, verify, harness);
        var values = Assert.IsType<object[]>(assembly.GetType("Caller")!.GetMethod("Run")!.Invoke(null, null));
        Assert.Equal(new object?[] { 9, null, 3 }, values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservedArithmetic_StaysInsideCast(bool verify)
    {
        var equal = Compile(Function("double", "(((int)(x - x)) == 0)"), verify);
        var unequal = Compile(Function("double", "(((int)(x - x)) != 0)"), verify);
        foreach (var value in new[] { 1.5, -1.5, 0.0, 10.0 })
        {
            Assert.Equal(7, Invoke(equal, value));
            AssertContractViolation(() => Invoke(unequal, value));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservedBinaryOperators_GroupNullableOperands(bool verify)
    {
        var right = Compile(Function("bool?", "(true && (x ?? false))"), verify);
        Assert.Equal(7, Invoke(right, true));
        AssertContractViolation(() => Invoke(right, false));
        AssertContractViolation(() => Invoke(right, null));

        var left = Compile(Function("bool?", "((x ?? false) && false)"), verify);
        foreach (object? value in new object?[] { true, false, null })
            AssertContractViolation(() => Invoke(left, value));

        var arithmetic = Compile(Function("int?", "(((x ?? 1) + 2) == 7)"), verify);
        Assert.Equal(7, Invoke(arithmetic, 5));
        AssertContractViolation(() => Invoke(arithmetic, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservedLogicalNegation_GroupsNullableFallback(bool verify)
    {
        foreach (var fallback in new[] { false, true })
        {
            var assembly = Compile(Function("bool?", $"(! (! (x ?? {fallback.ToString().ToLowerInvariant()})))"), verify);
            Assert.Equal(7, Invoke(assembly, true));
            AssertContractViolation(() => Invoke(assembly, false));
            if (fallback)
                Assert.Equal(7, Invoke(assembly, null));
            else
                AssertContractViolation(() => Invoke(assembly, null));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservedUnaryOperators_DoNotBecomeMutation(bool verify)
    {
        var assembly = Compile(Function("int", "((-(-x)) == 5)"), verify);
        Assert.Equal(7, Invoke(assembly, 5));
        AssertContractViolation(() => Invoke(assembly, 6));

        var longLiteral = Compile(Function("int", "((-(-2147483649)) == 2147483649)"), verify);
        Assert.Equal(7, Invoke(longLiteral, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatingPredicates_AgreeWithIeeeEvaluation(bool verify)
    {
        (string Predicate, Func<double, bool> Expected)[] predicates =
        [
            ("(x == x)", x => x == Identity(x)),
            ("(x != x)", x => x != Identity(x)),
            ("((x - x) == 0.0)", x => x - Identity(x) == 0),
            ("((x * 0.0) == 0.0)", x => x * 0 == 0),
            ("((x % 1) == 0.0)", x => x % 1 == 0),
        ];
        double[] values = [double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            0, -0.0, 1, -1, 1.5, double.Epsilon];
        foreach (var (predicate, expected) in predicates)
        {
            var assembly = Compile(Function("double", predicate), verify);
            foreach (var value in values)
            {
                if (expected(value))
                    Assert.Equal(7, Invoke(assembly, value));
                else
                    AssertContractViolation(() => Invoke(assembly, value));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedOperands_MustStillThrow(bool verify)
    {
        string[] predicates =
        [
            "((1 / x) == (1 / x))",
            "(((1 / x) * 0) == 0)",
            "(((1 / x) > 0) || true)",
            "(((1 / x) > 0) && false)",
            "(!((1 / x) > 0) || true)",
            "((((1 / x) > 0) ? 1 : 1) == 1)"
        ];
        foreach (var predicate in predicates)
        {
            var assembly = Compile(Function("int", predicate), verify);
            var exception = Assert.Throws<TargetInvocationException>(() => Invoke(assembly, 0));
            Assert.IsType<DivideByZeroException>(exception.InnerException);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedGetterOperands_ExecuteTwice(bool verify)
    {
        const string source = """
            module ContractEffects ContractEffects block
              class Counter
                field int:Value
                property int:Tick
                  get
                    set Value (Value + 1)
                    return Value
                func Check () → int
                  !
                  requires (Tick == Tick)
                  return 7
            """;
        const string harness = """
            public static class Caller
            {
                public static int Run()
                {
                    var counter = new ContractEffects.Counter();
                    try { counter.Check(); }
                    catch (System.Exception e) when (e.GetType().Name == "ContractViolationException")
                    { return counter.Value; }
                    return -1;
                }
            }
            """;
        var assembly = Compile(source, verify, harness);
        Assert.Equal(2, assembly.GetType("Caller")!.GetMethod("Run")!.Invoke(null, null));
        var quantified = Compile(source.Replace("requires (Tick == Tick)",
            "requires (forall int:i in 0..3 : (Tick > 0))"),
            verify, harness.Replace("return -1;", "return counter.Value + 100;"));
        Assert.Equal(103, quantified.GetType("Caller")!.GetMethod("Run")!.Invoke(null, null));
        var quantifiedFailure = Compile(source.Replace("requires (Tick == Tick)",
            "requires (forall int:i in 0..3 : (Tick < 2))"),
            verify, harness.Replace("return -1;", "return counter.Value + 100;"));
        Assert.Equal(2, quantifiedFailure.GetType("Caller")!.GetMethod("Run")!.Invoke(null, null));
    }

    [Fact]
    public void LiteralFolding_PreservesWidthsFlagsAndExceptionalOperations()
    {
        var span = TextSpan.Empty;
        ExpressionNode[] expressions =
        [
            new BinaryOperationNode(span, BinaryOperator.Add, new IntLiteralNode(span, 1) { IsLong = true }, new IntLiteralNode(span, 2)),
            new BinaryOperationNode(span, BinaryOperator.Add, new IntLiteralNode(span, 1, false, true, 1), new IntLiteralNode(span, 2)),
            new BinaryOperationNode(span, BinaryOperator.Add, new FloatLiteralNode(span, 1) { IsSingle = true }, new FloatLiteralNode(span, 2) { IsSingle = true }),
            new UnaryOperationNode(span, UnaryOperator.Negate, new FloatLiteralNode(span, 1, isDecimal: true)),
            new BinaryOperationNode(span, BinaryOperator.Add, new IntLiteralNode(span, int.MaxValue), new IntLiteralNode(span, 1)),
            new BinaryOperationNode(span, BinaryOperator.Divide, new IntLiteralNode(span, int.MinValue), new IntLiteralNode(span, -1)),
            new UnaryOperationNode(span, UnaryOperator.Negate, new IntLiteralNode(span, int.MinValue)),
            new ConditionalExpressionNode(span, new BoolLiteralNode(span, true), new IntLiteralNode(span, 1), new FloatLiteralNode(span, 2))
        ];
        foreach (var expression in expressions)
            Assert.Same(expression, new ExpressionSimplifier().Simplify(expression));

        var shift = new BinaryOperationNode(span, BinaryOperator.LeftShift,
            new IntLiteralNode(span, 1), new IntLiteralNode(span, 32));
        Assert.Equal(1, Assert.IsType<IntLiteralNode>(new ExpressionSimplifier().Simplify(shift)).Value);
    }

    [Fact]
    public void LiteralEquality_UsesIeeeNotEpsilonComparison()
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0 })
        {
            var equal = new BinaryOperationNode(TextSpan.Empty, BinaryOperator.Equal,
                new FloatLiteralNode(TextSpan.Empty, value), new FloatLiteralNode(TextSpan.Empty, value));
            var unequal = new BinaryOperationNode(TextSpan.Empty, BinaryOperator.NotEqual, equal.Left, equal.Right);
            Assert.Equal(!double.IsNaN(value), Assert.IsType<BoolLiteralNode>(new ExpressionSimplifier().Simplify(equal)).Value);
            Assert.Equal(double.IsNaN(value), Assert.IsType<BoolLiteralNode>(new ExpressionSimplifier().Simplify(unequal)).Value);
        }
    }

    private static double Identity(double value) => value;

    private static string Function(string type, string predicate) => $$"""
        module TypedContracts TypedContracts block
          func Check ({{type}}:x) → int
            !
            requires {{predicate}}
            return 7
        """;

    private static Assembly Compile(string source, bool verify, string? harness = null, bool rejectNullableWarnings = false)
    {
        var result = Program.Compile(source, "typed-contract.calr", Options(verify));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create(
            "TypedContracts_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode + harness)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        if (rejectNullableWarnings)
            Assert.DoesNotContain(emit.Diagnostics, diagnostic => diagnostic.Id == "CS8602");
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Assembly assembly, object? value) =>
        InvokeArguments(assembly, value);

    private static object? InvokeArguments(Assembly assembly, params object?[] values) =>
        Assert.Single(assembly.GetTypes(), t => t.Name == "TypedContractsModule")
            .GetMethod("Check")!.Invoke(null, values);

    private static CompilationOptions Options(bool verify) => new()
    {
        VerifyContracts = verify,
        ContractMode = ContractMode.Debug,
        ElideProvenGuards = true,
        EnableTypeChecking = true,
        EnforceEffects = true,
        StatusWriter = TextWriter.Null,
        VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
    };

    private static void AssertContractViolation(Action action)
    {
        var exception = Assert.Throws<TargetInvocationException>(action);
        Assert.Equal("ContractViolationException", exception.InnerException!.GetType().Name);
    }
}
