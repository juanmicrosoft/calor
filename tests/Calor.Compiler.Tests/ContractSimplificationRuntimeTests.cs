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

    private static Assembly Compile(string source, bool verify, string? harness = null)
    {
        var result = Program.Compile(source, "typed-contract.calr", new CompilationOptions
        {
            VerifyContracts = verify,
            ContractMode = ContractMode.Debug,
            ElideProvenGuards = true,
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create(
            "TypedContracts_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode + harness)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Assembly assembly, object value) =>
        Assert.Single(assembly.GetTypes(), t => t.Name == "TypedContractsModule")
            .GetMethod("Check")!.Invoke(null, [value]);

    private static void AssertContractViolation(Action action)
    {
        var exception = Assert.Throws<TargetInvocationException>(action);
        Assert.Equal("ContractViolationException", exception.InnerException!.GetType().Name);
    }
}
