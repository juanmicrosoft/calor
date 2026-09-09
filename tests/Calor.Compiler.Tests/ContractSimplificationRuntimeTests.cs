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
            §M{m1:TypedContracts}
              §F{f1:Check:pub} (bool:guard, i32:x) -> i32
                §E{}
                §Q {{predicate}}
                §R INT:7
            """;
        var all = Compile(Guarded(
            "(forall ((i i32)) (-> (&& guard (&& (>= i (/ INT:1 x)) (< i INT:2))) true))"), verify);
        var any = Compile(Guarded(
            "(exists ((i i32)) (&& guard (&& (>= i (/ INT:1 x)) (< i INT:2))))"), verify);
        Assert.Equal(7, InvokeArguments(all, false, 0));
        AssertContractViolation(() => InvokeArguments(any, false, 0));
        Assert.Equal(7, InvokeArguments(all, true, 1));
        Assert.Equal(7, InvokeArguments(any, true, 1));
        foreach (var assembly in new[] { all, any })
        {
            var error = Assert.Throws<TargetInvocationException>(() => InvokeArguments(assembly, true, 0));
            Assert.IsType<DivideByZeroException>(error.InnerException);
        }

        var emptyPrefix = Compile(Function("i32",
            "(forall ((i i32)) (-> (&& (> i INT:2147483647) (< i (/ INT:1 x))) false))"), verify);
        Assert.Equal(7, Invoke(emptyPrefix, 0));
        var earlierThrow = Compile(Function("i32",
            "(forall ((i i32)) (-> (&& (< i (/ INT:1 x)) (> i INT:2147483647)) false))"), verify);
        Assert.IsType<DivideByZeroException>(
            Assert.Throws<TargetInvocationException>(() => Invoke(earlierThrow, 0)).InnerException);

        var betweenBounds = Compile(Guarded(
            "(forall ((i i32)) (-> (&& (>= i INT:0) (&& guard (< i (/ INT:1 x)))) true))"), verify);
        Assert.Equal(7, InvokeArguments(betweenBounds, false, 0));
        Assert.IsType<DivideByZeroException>(
            Assert.Throws<TargetInvocationException>(() => InvokeArguments(betweenBounds, true, 0)).InnerException);
        var nested = Compile(Function("i32",
            "(forall ((i i32) (j i32)) (-> (&& (>= i INT:0) (&& (< i x) (&& (>= j (/ INT:1 x)) (< j (+ i INT:2))))) true))"), verify);
        Assert.Equal(7, Invoke(nested, 0));
        Assert.Equal(7, Invoke(nested, 2));
        var nullableArray = Compile("""
            §M{m1:TypedContracts}
              §CL{c1:NullableDomain:pub}
                §MT{mt1:Check:pub} (i32[]?:x) -> i32
                  §E{}
                  §Q (forall ((i i32)) (-> (&& (!= x null) (&& (>= i INT:0) (< i x.Length))) true))
                  §R INT:7
            """, verify, rejectNullableWarnings: true);
        var domainType = nullableArray.GetType("TypedContracts.NullableDomain")!;
        var domain = Activator.CreateInstance(domainType);
        Assert.Equal(7, domainType.GetMethod("Check")!.Invoke(domain, [null]));
        Assert.Equal(7, domainType.GetMethod("Check")!.Invoke(domain, [new[] { 1, 2 }]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncertifiedQuantifierPrefixes_AreRejectedExplicitly(bool verify)
    {
        foreach (var predicate in new[]
        {
            "(forall ((i i32)) (-> (&& (> Tick INT:0) (&& (>= i INT:0) (< i INT:3))) true))",
            "(exists ((i i32)) (&& (>= i Tick) (< i INT:3)))"
        })
        {
            var source = $$"""
                §M{m1:ContractEffects}
                  §CL{c1:Counter:pub}
                    §FLD{i32:Value:pub}
                    §PROP{p1:Tick:i32:pub}
                      §GET
                        §ASSIGN Value (+ Value INT:1)
                        §R Value
                    §MT{mt1:Check:pub} () -> i32
                      §E{}
                      §Q {{predicate}}
                      §R INT:7
                """;
            var result = Program.Compile(source, "uncertified-quantifier.calr", Options(verify));
            Assert.Contains(result.Diagnostics.Errors,
                error => error.Code == Calor.Compiler.Diagnostics.DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
        }
        var forwardDependent = Program.Compile(Function("i32",
            "(forall ((i i32) (j i32)) (-> (&& (>= i INT:0) (&& (< i j) (&& (>= j INT:0) (< j x)))) true))"),
            "forward-bound.calr", Options(verify));
        Assert.Contains(forwardDependent.Diagnostics.Errors,
            error => error.Code == Calor.Compiler.Diagnostics.DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
        foreach (var predicate in new[]
        {
            "(forall ((i i32)) (-> (&& false x) false))",
            "(forall ((i i8)) (-> (&& (&& (>= i INT:0) (< i INT:1)) x) (== i INT:0)))"
        })
        {
            var overloaded = $$"""
                §M{m1:TypedContracts}
                  §CL{c1:Probe:pub}
                    §OP{op1:implicit:pub}
                      §I{bool:value}
                      §O{Probe}
                      §R §NEW{Probe}
                    §OP{op2:implicit:pub}
                      §I{Probe:value}
                      §O{bool}
                      §R true
                    §OP{op3:true:pub}
                      §I{Probe:value}
                      §O{bool}
                      §R true
                    §OP{op4:false:pub}
                      §I{Probe:value}
                      §O{bool}
                      §R false
                    §OP{op5:&:pub}
                      §I{Probe:a}
                      §I{Probe:b}
                      §O{Probe}
                      §R a
                  §F{f1:Check:pub} (Probe:x) -> i32
                    §E{}
                    §Q {{predicate}}
                    §R INT:7
                """;
            var result = Program.Compile(overloaded, "overloaded-logic.calr", Options(verify));
            Assert.Contains(result.Diagnostics.Errors,
                error => error.Code == Calor.Compiler.Diagnostics.DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
            var scalar = predicate.Contains("i8", StringComparison.Ordinal)
                ? "(-> (&& (&& (>= INT:-1 INT:0) (< INT:-1 INT:1)) x) (== INT:-1 INT:0))"
                : "(-> (&& false x) false)";
            var control = Compile(overloaded.Replace(predicate, scalar), verify);
            AssertContractViolation(() => Invoke(control, null));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quantifiers_KeepEveryConjunctAndGroupedBounds(bool verify)
    {
        var falseExists = Compile(Function("i32",
            "(exists ((i i32)) (&& (>= i INT:0) (&& (< i INT:1) (&& (== i i) false))))"), verify);
        AssertContractViolation(() => Invoke(falseExists, 0));

        var throwingExists = Compile(Function("i32",
            "(exists ((i i32)) (&& (>= i INT:0) (&& (< i INT:1) (&& (== i i) (> (/ INT:1 x) INT:0)))))"), verify);
        var exception = Assert.Throws<TargetInvocationException>(() => Invoke(throwingExists, 0));
        Assert.IsType<DivideByZeroException>(exception.InnerException);

        var forall = Compile(Function("i32",
            "(forall ((i i32)) (-> (&& (>= i (- x x)) (< i INT:2)) (< i INT:1)))"), verify);
        var exists = Compile(Function("i32",
            "(exists ((i i32)) (&& (>= i (- x x)) (&& (< i INT:2) (== i INT:1))))"), verify);
        var multi = Compile(Function("i32",
            "(exists ((i i32) (j i32)) (&& (>= i (- x x)) (&& (< i INT:2) (&& (>= j (- x x)) (&& (< j INT:2) (&& (== i j) (== j INT:1)))))))"), verify);
        foreach (var value in new[] { -2, 0, 1, 2 })
        {
            AssertContractViolation(() => Invoke(forall, value));
            Assert.Equal(7, Invoke(exists, value));
            Assert.Equal(7, Invoke(multi, value));
        }

        var emptyAll = Compile(Function("i32",
            "(forall ((i i32)) (-> (&& (>= i INT:1) (< i INT:0)) true))"), verify);
        var emptyAny = Compile(Function("i32",
            "(exists ((i i32)) (&& (>= i INT:1) (&& (< i INT:0) true)))"), verify);
        Assert.Equal(7, Invoke(emptyAll, 0));
        AssertContractViolation(() => Invoke(emptyAny, 0));

        var shortAll = Compile(Function("i32",
            "(forall ((i i32)) (-> (&& false (&& (>= i (/ INT:1 x)) (< i INT:2))) true))"), verify);
        var shortAny = Compile(Function("i32",
            "(exists ((i i32)) (&& false (&& (>= i (/ INT:1 x)) (< i INT:2))))"), verify);
        Assert.Equal(7, Invoke(shortAll, 0));
        AssertContractViolation(() => Invoke(shortAny, 0));
        Assert.Equal(new[] { int.MinValue, int.MinValue + 1, int.MinValue + 2 },
            Calor.Runtime.ContractQuantifier.Range(int.MinValue, int.MaxValue).Take(3));
        Assert.Equal(new[] { int.MaxValue - 1 },
            Calor.Runtime.ContractQuantifier.Range(int.MaxValue - 1, int.MaxValue));
        Assert.Equal(new[] { int.MaxValue },
            Calor.Runtime.ContractQuantifier.Range(int.MaxValue, (long)int.MaxValue + 1));
        Assert.Empty(Calor.Runtime.ContractQuantifier.Range((long)int.MaxValue + 1, long.MaxValue));

        var inclusive = Compile(Function("i32",
            "(forall ((i i32)) (-> (&& (>= i x) (<= i x)) (< i INT:0)))"), verify);
        AssertContractViolation(() => Invoke(inclusive, int.MaxValue));
        AssertContractViolation(() => Invoke(inclusive, int.MaxValue - 1));
        Assert.Equal(7, Invoke(inclusive, int.MinValue));
        var strict = Compile(Function("i32",
            "(exists ((i i32)) (&& (> i x) (<= i INT:2147483647)))"), verify);
        AssertContractViolation(() => Invoke(strict, int.MaxValue));
        Assert.Equal(7, Invoke(strict, int.MaxValue - 1));
        var wideEndpoint = Compile(Function("i64",
            "(forall ((i i32)) (-> (&& (>= i INT:0) (<= i x)) (< i INT:0)))"), verify);
        AssertContractViolation(() => Invoke(wideEndpoint, long.MaxValue));
        Assert.Equal(7, Invoke(wideEndpoint, long.MinValue));
        (string Type, object Min, object Max)[] domains =
        [
            ("i8", sbyte.MinValue, sbyte.MaxValue), ("u8", byte.MinValue, byte.MaxValue),
            ("i16", short.MinValue, short.MaxValue), ("u16", ushort.MinValue, ushort.MaxValue),
            ("i32", int.MinValue, int.MaxValue), ("u32", uint.MinValue, uint.MaxValue),
            ("i64", long.MinValue, long.MaxValue), ("u64", ulong.MinValue, ulong.MaxValue)
        ];
        foreach (var (type, min, max) in domains)
        {
            var singleton = Compile(Function(type,
                $"(forall ((i {type})) (-> (&& (>= i x) (<= i x)) false))"), verify);
            AssertContractViolation(() => Invoke(singleton, min));
            AssertContractViolation(() => Invoke(singleton, max));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatternOperand_ContainsCompleteEquality(bool verify)
    {
        var assembly = Compile(Function("bool", "(== (is (== x x) bool) false)"), verify);
        AssertContractViolation(() => Invoke(assembly, false));
        AssertContractViolation(() => Invoke(assembly, true));
        var compound = Compile(Function("object", "(&& (== (|| (is x i32) false) true) false)"), verify);
        AssertContractViolation(() => Invoke(compound, 5));
        AssertContractViolation(() => Invoke(compound, "text"));

        var overloaded = Compile("""
            §M{m1:TypedContracts}
              §CL{c1:Probe:pub}
                §OP{op1:==:pub}
                  §I{Probe:a}
                  §I{Probe:b}
                  §O{Probe}
                  §R a
                §OP{op2:!=:pub}
                  §I{Probe:a}
                  §I{Probe:b}
                  §O{Probe}
                  §R a
                §OP{op3:==:pub}
                  §I{Probe:a}
                  §I{bool:b}
                  §O{bool}
                  §R false
                §OP{op4:!=:pub}
                  §I{Probe:a}
                  §I{bool:b}
                  §O{bool}
                  §R true
                §OP{op5:!:pub}
                  §I{Probe:a}
                  §O{bool}
                  §R false
              §F{f1:Check:pub} (Probe:x) -> i32
                §E{}
                §Q (== (== x (? (is x Probe candidate) x x)) true)
                §R INT:7
            """, verify);
        var probe = Activator.CreateInstance(Assert.Single(overloaded.GetTypes(), type => type.Name == "Probe"));
        AssertContractViolation(() => Invoke(overloaded, probe));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResultPayloads_UseExpressionTypesNotLiteralShapes(bool verify)
    {
        string[] predicates =
        [
            "(is (cast object §OK (== x x)) Result<bool,str>)",
            "(is (cast object §OK (- x x)) Result<i32,str>)",
            "(is (cast object §ERR (== x x)) Result<object,bool>)",
            "(is (cast object §OK INT:2147483648) Result<i64,str>)"
        ];
        foreach (var predicate in predicates)
        {
            var assembly = Compile(Function("i32", $"(== {predicate} true)"), verify);
            Assert.Equal(7, Invoke(assembly, 3));
        }

        foreach (var body in new[]
        {
            "§R §OK x",
            """§R (? (> x INT:0) §OK x §ERR "negative")"""
        })
        {
            var source = $$"""
                §M{m1:TypedContracts}
                  §F{f1:Check:pub} (i32:x) -> Result<object,str>
                    §E{}
                    §S true
                    {{body}}
                """;
            var assembly = Compile(source, verify);
            Assert.Equal(3, Assert.IsType<Calor.Runtime.Result<object, string>>(Invoke(assembly, 3)).Unwrap());
        }

        var nested = Compile("""
            §M{m1:TypedContracts}
              §F{f1:Check:pub} (i32:x) -> Result<Result<object,str>,str>
                §E{}
                §R §OK §OK x
            """, verify);
        Assert.Equal(3, Assert.IsType<Calor.Runtime.Result<Calor.Runtime.Result<object, string>, string>>(
            Invoke(nested, 3)).Unwrap().Unwrap());
        var nullable = Compile("""
            §M{m1:TypedContracts}
              §F{f1:Check:pub} (i32:x) -> Result<object,str>?
                §E{}
                §R §OK x
            """, verify);
        Assert.Equal(3, Assert.IsType<Calor.Runtime.Result<object, string>>(Invoke(nullable, 3)).Unwrap());
        var lambda = Compile("""
            §M{m1:TypedContracts}
              §F{f1:Check:pub} (i32:x) -> Func<Result<object,str>>
                §E{}
                §R §LAM{l1} §R §OK x §/LAM{l1}
            """, verify);
        var factory = Assert.IsType<Func<Calor.Runtime.Result<object, string>>>(Invoke(lambda, 3));
        Assert.Equal(3, factory().Unwrap());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatternBinding_SurvivesBooleanComparisonWrappers(bool verify)
    {
        string[] wrappers =
        [
            "(== (is result i32 result) true)",
            "(!= false (is result i32 result))",
            "(! (== (is result i32 result) false))",
            "(== (== (is result i32 result) true) true)",
            "(== (&& (is result i32 result) (> result INT:0)) true)",
            "(!= false (&& (is result i32 result) (== result INT:5)))"
        ];
        foreach (var wrapper in wrappers)
        {
            var source = $$"""
                §M{m1:TypedContracts}
                  §F{f1:Check:pub} (object:x) -> object
                    §E{}
                    §S (&& {{wrapper}} (> result INT:0))
                    §R x
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
        var receiver = Compile(Function("i32?", "(== (+ x INT:0).Value INT:3)"), verify);
        Assert.Equal(7, Invoke(receiver, 3));
        AssertContractViolation(() => Invoke(receiver, 2));
        var exception = Assert.Throws<TargetInvocationException>(() => Invoke(receiver, null));
        Assert.IsType<InvalidOperationException>(exception.InnerException);

        var index = Compile(Function("i32", """(== §IDX "abcd" §^ (+ x INT:0) (cast char INT:98))"""), verify);
        Assert.Equal(7, Invoke(index, 3));
        AssertContractViolation(() => Invoke(index, 2));
        var range = Compile(Function("i32", """(== §IDX "abcd" §RANGE (+ x INT:0) INT:4 "d")"""), verify);
        Assert.Equal(7, Invoke(range, 3));
        AssertContractViolation(() => Invoke(range, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryReceivers_PreserveBaseAndConditionalChains(bool verify)
    {
        const string source = """
            §M{m1:ReceiverChains}
              §CL{c1:Parent:pub}
                §FLD{i32:Value:pub}
              §CL{c2:Derived:pub}
                §EXT{Parent}
                §MT{mt1:Read:pub} () -> i32
                  §R §BASE.Value
              §CL{c3:Node:pub}
                §FLD{Node:Child:pub}
                §FLD{i32:Value:pub}
              §F{f1:Check:pub} (Node:x) -> i32?
                §R x?.Child .Value
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
        var equal = Compile(Function("f64", "(== (cast i32 (- x x)) INT:0)"), verify);
        var unequal = Compile(Function("f64", "(!= (cast i32 (- x x)) INT:0)"), verify);
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
        var right = Compile(Function("bool?", "(&& true (?? x false))"), verify);
        Assert.Equal(7, Invoke(right, true));
        AssertContractViolation(() => Invoke(right, false));
        AssertContractViolation(() => Invoke(right, null));

        var left = Compile(Function("bool?", "(&& (?? x false) false)"), verify);
        foreach (object? value in new object?[] { true, false, null })
            AssertContractViolation(() => Invoke(left, value));

        var arithmetic = Compile(Function("i32?", "(== (+ (?? x INT:1) INT:2) INT:7)"), verify);
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
            var assembly = Compile(Function("bool?", $"(! (! (?? x {fallback.ToString().ToLowerInvariant()})))"), verify);
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
        var assembly = Compile(Function("i32", "(== (- (- x)) INT:5)"), verify);
        Assert.Equal(7, Invoke(assembly, 5));
        AssertContractViolation(() => Invoke(assembly, 6));

        var longLiteral = Compile(Function("i32", "(== (- INT:-2147483649) INT:2147483649)"), verify);
        Assert.Equal(7, Invoke(longLiteral, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatingPredicates_AgreeWithIeeeEvaluation(bool verify)
    {
        (string Predicate, Func<double, bool> Expected)[] predicates =
        [
            ("(== x x)", x => x == Identity(x)),
            ("(!= x x)", x => x != Identity(x)),
            ("(== (- x x) FLOAT:0.0)", x => x - Identity(x) == 0),
            ("(== (* x FLOAT:0.0) FLOAT:0.0)", x => x * 0 == 0),
            ("(== (% x INT:1) FLOAT:0.0)", x => x % 1 == 0),
        ];
        double[] values = [double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            0, -0.0, 1, -1, 1.5, double.Epsilon];
        foreach (var (predicate, expected) in predicates)
        {
            var assembly = Compile(Function("f64", predicate), verify);
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
            "(== (/ INT:1 x) (/ INT:1 x))",
            "(== (* (/ INT:1 x) INT:0) INT:0)",
            "(|| (> (/ INT:1 x) INT:0) true)",
            "(&& (> (/ INT:1 x) INT:0) false)",
            "(-> (> (/ INT:1 x) INT:0) true)",
            "(== (? (> (/ INT:1 x) INT:0) INT:1 INT:1) INT:1)"
        ];
        foreach (var predicate in predicates)
        {
            var assembly = Compile(Function("i32", predicate), verify);
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
            §M{m1:ContractEffects}
              §CL{c1:Counter:pub}
                §FLD{i32:Value:pub}
                §PROP{p1:Tick:i32:pub}
                  §GET
                    §ASSIGN Value (+ Value INT:1)
                    §R Value
                §MT{mt1:Check:pub} () -> i32
                  §E{}
                  §Q (== Tick Tick)
                  §R INT:7
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
        var quantified = Compile(source.Replace("§Q (== Tick Tick)",
            "§Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i INT:3)) (> Tick INT:0)))"),
            verify, harness.Replace("return -1;", "return counter.Value + 100;"));
        Assert.Equal(103, quantified.GetType("Caller")!.GetMethod("Run")!.Invoke(null, null));
        var quantifiedFailure = Compile(source.Replace("§Q (== Tick Tick)",
            "§Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i INT:3)) (< Tick INT:2)))"),
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
        §M{m1:TypedContracts}
          §F{f1:Check:pub} ({{type}}:x) -> i32
            §E{}
            §Q {{predicate}}
            §R INT:7
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
