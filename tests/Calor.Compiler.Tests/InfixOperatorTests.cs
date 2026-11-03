using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class InfixOperatorTests
{
    [Theory]
    [InlineData("a + b * c", BinaryOperator.Add, BinaryOperator.Multiply)]
    [InlineData("a - b / c", BinaryOperator.Subtract, BinaryOperator.Divide)]
    [InlineData("a + b % c", BinaryOperator.Add, BinaryOperator.Modulo)]
    [InlineData("a << b + c", BinaryOperator.LeftShift, BinaryOperator.Add)]
    [InlineData("a >> b - c", BinaryOperator.RightShift, BinaryOperator.Subtract)]
    [InlineData("a < b << c", BinaryOperator.LessThan, BinaryOperator.LeftShift)]
    [InlineData("a <= b >> c", BinaryOperator.LessOrEqual, BinaryOperator.RightShift)]
    [InlineData("a == b > c", BinaryOperator.Equal, BinaryOperator.GreaterThan)]
    [InlineData("a != b >= c", BinaryOperator.NotEqual, BinaryOperator.GreaterOrEqual)]
    [InlineData("a & b == c", BinaryOperator.BitwiseAnd, BinaryOperator.Equal)]
    [InlineData("a ^ b & c", BinaryOperator.BitwiseXor, BinaryOperator.BitwiseAnd)]
    [InlineData("a | b ^ c", BinaryOperator.BitwiseOr, BinaryOperator.BitwiseXor)]
    [InlineData("a && b | c", BinaryOperator.And, BinaryOperator.BitwiseOr)]
    [InlineData("a || b && c", BinaryOperator.Or, BinaryOperator.And)]
    public void BinaryPrecedence_BindsHigherTierOnEitherSide(
        string source, BinaryOperator lower, BinaryOperator higher)
    {
        var root = Assert.IsType<BinaryOperationNode>(ParseExpression(source));
        Assert.Equal(lower, root.Operator);
        Assert.Equal(higher, Assert.IsType<BinaryOperationNode>(root.Right).Operator);
        var reversed = source.Split(' ');
        var other = $"{reversed[2]} {reversed[3]} {reversed[4]} {reversed[1]} {reversed[0]}";
        root = Assert.IsType<BinaryOperationNode>(ParseExpression(other));
        Assert.Equal(lower, root.Operator);
        Assert.Equal(higher, Assert.IsType<BinaryOperationNode>(root.Left).Operator);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("/")]
    [InlineData("%")]
    [InlineData("<<")]
    [InlineData(">>")]
    [InlineData("<")]
    [InlineData("==")]
    [InlineData("&")]
    [InlineData("^")]
    [InlineData("|")]
    [InlineData("&&")]
    [InlineData("||")]
    public void BinaryOperators_AreLeftAssociative(string op)
    {
        var root = Assert.IsType<BinaryOperationNode>(ParseExpression($"a {op} b {op} c"));
        Assert.IsType<BinaryOperationNode>(root.Left);
        Assert.IsType<ReferenceNode>(root.Right);
    }

    [Fact]
    public void CoalescingAndConditional_AreRightAssociativeAndBelowBooleanOr()
    {
        var coalesce = Assert.IsType<NullCoalesceNode>(ParseExpression("a || b ?? c ?? d"));
        Assert.Equal(BinaryOperator.Or, Assert.IsType<BinaryOperationNode>(coalesce.Left).Operator);
        Assert.IsType<NullCoalesceNode>(coalesce.Right);
        var conditional = Assert.IsType<ConditionalExpressionNode>(
            ParseExpression("a ?? b ? c ? d : e : f ? g : h"));
        Assert.IsType<NullCoalesceNode>(conditional.Condition);
        Assert.IsType<ConditionalExpressionNode>(conditional.WhenTrue);
        Assert.IsType<ConditionalExpressionNode>(conditional.WhenFalse);
    }

    [Fact]
    public void GroupingAndUnary_PreserveCompleteSourceSpans()
    {
        const string expression = "((a + b) * -c)";
        var root = Assert.IsType<BinaryOperationNode>(ParseExpression(expression));
        Assert.Equal(expression.Length, root.Span.Length);
        Assert.Equal(BinaryOperator.Multiply, root.Operator);
        Assert.Equal("(a + b)".Length, root.Left.Span.Length);
        var unary = Assert.IsType<UnaryOperationNode>(root.Right);
        Assert.Equal(UnaryOperator.Negate, unary.Operator);
        Assert.Equal(2, unary.Span.Length);
        Assert.Equal("((a))".Length, ParseExpression("((a))").Span.Length);
        var not = Assert.IsType<UnaryOperationNode>(ParseExpression("!(a == b)"));
        Assert.IsType<BinaryOperationNode>(not.Operand);
        Assert.Equal("(a.Name)".Length, ParseExpression("(a.Name)").Span.Length);
    }

    [Fact]
    public void Comparisons_DoNotBecomeGenericReferencesOrHideTupleCommas()
    {
        var comparison = Assert.IsType<BinaryOperationNode>(ParseExpression("a < b > c"));
        Assert.Equal(BinaryOperator.GreaterThan, comparison.Operator);
        Assert.Equal(BinaryOperator.LessThan, Assert.IsType<BinaryOperationNode>(comparison.Left).Operator);
        var tuple = Assert.IsType<TupleLiteralNode>(ParseExpression("(a < b, c > d)"));
        Assert.All(tuple.Elements, element => Assert.IsType<BinaryOperationNode>(element));
    }

    [Theory]
    [InlineData("(cast Func<string, bool> null)", "Func<string, bool>")]
    [InlineData("(cast RuleComponent<Person, string> _rule.Current)", "RuleComponent<Person, string>")]
    [InlineData("(cast RuleBuilder<T, TProperty?> ruleBuilder)", "RuleBuilder<T, TProperty?>")]
    [InlineData("(cast System.Func<string, List<bool>> null)", "System.Func<string, List<bool>>")]
    [InlineData("(cast global::System.Func<string, bool> null)", "global::System.Func<string, bool>")]
    public void LegacyGenericCasts_DoNotBecomeTuples(string expression, string type)
    {
        var cast = Assert.IsType<TypeOperationNode>(ParseExpression(expression));
        Assert.Equal(TypeOp.Cast, cast.Operation);
        Assert.Equal(type, cast.TargetType);
        Assert.Equal(type, Source(expression).Substring(cast.TargetTypeSpan.Start, cast.TargetTypeSpan.Length));
    }

    [Theory]
    [InlineData("((cast Func<string, bool> null), 1)")]
    [InlineData("((a > b), c)")]
    [InlineData("((a < b), c > d)")]
    [InlineData("(cast, other)")]
    [InlineData("(cast < b, c > d)")]
    [InlineData("(Func<string, bool>, other)")]
    [InlineData("(Dictionary<string, List<int>>, other)")]
    [InlineData("(Container<Dictionary<string, List<int>>, bool>, other)")]
    public void TupleLookahead_DistinguishesNestedGenericsComparisonsAndCastReferences(string expression)
    {
        Assert.Equal(2, Assert.IsType<TupleLiteralNode>(ParseExpression(expression)).Elements.Count);
    }

    [Theory]
    [InlineData("§C{_expr004.Clear}")]
    [InlineData("§B{next} 1")]
    [InlineData("§R")]
    public void GenericReference_EndsBeforeTheNextStatement(string nextStatement)
    {
        var module = Parse($$"""
            §M{Infix}
              §F{Probe:pub} () -> void
                §B{_expr004} AccessorCache<Person>
                {{nextStatement}}
            """);
        var body = Assert.Single(module.Functions).Body;
        Assert.Equal(2, body.Count);
        var binding = Assert.IsType<BindStatementNode>(body[0]);
        Assert.Equal("AccessorCache<Person>", Assert.IsType<ReferenceNode>(binding.Initializer).Name);
    }

    [Theory]
    [InlineData("return (Func<string, bool>)null;")]
    [InlineData("return (RuleComponent<Person, string>)_rule.Current;")]
    [InlineData("return (RuleBuilder<T, TProperty?>)ruleBuilder;")]
    [InlineData("AccessorCache<Person>.Clear(); return null;")]
    public void ConvertedGenericCastsAndStaticCalls_ReparseWithoutLosingTheirShape(string body)
    {
        var conversion = new CSharpToCalorConverter().Convert($$"""
            using System;
            public class GenericProbe<T, TProperty>
            {
                private RuleComponent<Person, string> _rule;
                public object Probe(object ruleBuilder)
                {
                    {{body}}
                }
            }
            public class Person { }
            public class RuleComponent<T, TProperty> { public object Current; }
            public class RuleBuilder<T, TProperty> { }
            public class AccessorCache<T> { public static void Clear() { } }
            """);
        Assert.True(conversion.Success,
            string.Join(Environment.NewLine, conversion.Issues.Select(issue => issue.Message)));
        var source = Assert.IsType<string>(conversion.CalorSource);
        var module = Parse(source);
        if (body.StartsWith("return"))
            Assert.Contains("(cast ", source);
        else
            Assert.Contains("AccessorCache<Person>", source);
        Parse(new CalorEmitter().Emit(module));
    }

    [Fact]
    public void StagedPrefixNegativeOperands_RemainDistinctFromUnspacedUnaryGrouping()
    {
        var prefix = Assert.IsType<BinaryOperationNode>(ParseExpression("(- x -2147483648)"));
        Assert.Equal(BinaryOperator.Subtract, prefix.Operator);
        Assert.IsType<ReferenceNode>(prefix.Left);
        Assert.Equal(IntegerLiteralSign.Negative, Assert.IsType<IntLiteralNode>(prefix.Right).Sign);
        var infix = Assert.IsType<BinaryOperationNode>(ParseExpression("(-x - 1)"));
        Assert.IsType<UnaryOperationNode>(infix.Left);
        Assert.Equal(IntegerLiteralSign.Positive, Assert.IsType<IntLiteralNode>(infix.Right).Sign);
    }

    [Fact]
    public void Expressions_StopAtNewlinesCallArgumentsAndContractClauses()
    {
        const string source = """
            §M{Infix}
              §F{Probe:pub} (i32:x = 1 + 2 * 3) -> i32
                §Q x >= 0 && x < 10
                §S result > x
                §B{n:i32} x + 1
                §C{Sink} §A n * 2 §A x - 1 §/C
                §R n
              §F{Next:pub} () -> i32
                §R 4
            """;
        var module = Parse(source);
        Assert.Equal(2, module.Functions.Count);
        var function = module.Functions[0];
        Assert.Single(function.Preconditions);
        Assert.Single(function.Postconditions);
        Assert.Equal(3, function.Body.Count);
        var call = Assert.IsType<CallStatementNode>(function.Body[1]);
        Assert.Equal(2, call.Arguments.Count);
        Assert.All(call.Arguments, argument => Assert.IsType<BinaryOperationNode>(argument));
    }

    [Fact]
    public void MissingSameLineOperand_LeavesTheNextStatementForRecovery()
    {
        var diagnostics = new DiagnosticBag();
        var module = Parse("""
            §M{Infix}
              §F{Probe:pub} (i32:a, i32:b) -> i32
                §R a +
                §R b
            """, diagnostics);
        Assert.True(diagnostics.HasErrors);
        var body = Assert.Single(module.Functions).Body;
        Assert.Equal(2, body.Count);
        Assert.Equal("b", Assert.IsType<ReferenceNode>(Assert.IsType<ReturnStatementNode>(body[1]).Expression).Name);
    }

    [Fact]
    public void MultilineLiteral_EndLineStillAllowsAnOperator()
    {
        var expression = Assert.IsType<BinaryOperationNode>(ParseExpression("\"a\nb\" + \"c\""));
        Assert.Equal(BinaryOperator.Add, expression.Operator);
        Assert.Equal("a b", Assert.IsType<StringLiteralNode>(expression.Left).Value);
    }

    [Theory]
    [InlineData("§ASSIGN x -5")]
    [InlineData("§SETIDX{items} 0 -5")]
    [InlineData("§INS{items} 0 -5")]
    [InlineData("§PUT{map} \"a\" -5")]
    public void PositionalStatementOperands_DoNotConsumeANegativeValue(string statement)
    {
        var module = Parse($$"""
            §M{Infix}
              §F{Probe:pub} () -> void
                {{statement}}
            """);
        var parsed = Assert.Single(Assert.Single(module.Functions).Body);
        var value = parsed switch
        {
            AssignmentStatementNode assignment => assignment.Value,
            CollectionSetIndexNode set => set.Value,
            CollectionInsertNode insert => insert.Value,
            DictionaryPutNode put => put.Value,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(-5, Assert.IsType<IntLiteralNode>(value).Value);
    }

    [Fact]
    public void PositionalExpressionOperands_DoNotConsumeNegativeIndicesOrFallbacks()
    {
        var access = Assert.IsType<ArrayAccessNode>(ParseExpression("§IDX a -5"));
        Assert.Equal("a", Assert.IsType<ReferenceNode>(access.Array).Name);
        Assert.Equal(-5, Assert.IsType<IntLiteralNode>(access.Index).Value);
        var coalesce = Assert.IsType<NullCoalesceNode>(ParseExpression("§?? x -5"));
        Assert.Equal("x", Assert.IsType<ReferenceNode>(coalesce.Left).Name);
        Assert.Equal(-5, Assert.IsType<IntLiteralNode>(coalesce.Right).Value);
    }

    [Theory]
    [InlineData("a +")]
    [InlineData("a ? b")]
    [InlineData("a ? : b")]
    [InlineData("a ??")]
    [InlineData("a ** b")]
    [InlineData("a >>> b")]
    [InlineData("++a")]
    [InlineData("+a")]
    [InlineData("--a")]
    [InlineData("a--b")]
    [InlineData("(a + b")]
    [InlineData("(cast Func<string, bool>)")]
    [InlineData("(cast Func<string, bool> null extra)")]
    [InlineData("a +\n    §R b")]
    public void MalformedOrUnsupportedForms_ProduceErrors(string expression)
    {
        var diagnostics = new DiagnosticBag();
        Parse(Source(expression), diagnostics);
        Assert.True(diagnostics.HasErrors, expression);
    }

    [Theory]
    [InlineData("1+2*3", "i32", 7)]
    [InlineData("(1+2)*3", "i32", 9)]
    [InlineData("20-5-3", "i32", 12)]
    [InlineData("24/4/2", "i32", 3)]
    [InlineData("20%6*3", "i32", 6)]
    [InlineData("1<<2+1", "i32", 8)]
    [InlineData("32>>1+2", "i32", 4)]
    [InlineData("1|2^3&6", "i32", 1)]
    [InlineData("!false && false || true", "bool", true)]
    [InlineData("false & true || true", "bool", true)]
    [InlineData("true ^ false & false", "bool", true)]
    [InlineData("true | false && false", "bool", false)]
    [InlineData("1+2<4 == true", "bool", true)]
    [InlineData("1<2 && 2<=2 && 3>2 && 3>=3 && 1!=2", "bool", true)]
    [InlineData("-2*-3+~0", "i32", 5)]
    [InlineData("-(2+3)*4", "i32", -20)]
    [InlineData("false ? 1 : true ? 2 : 3", "i32", 2)]
    [InlineData("true ? false ? 1 : 2 : 3", "i32", 2)]
    public void Runtime_UsesSelectedCSharpPrecedence(string expression, string type, object expected)
    {
        Assert.Equal(expected, Invoke(Compile(Source(expression, type)), "Probe"));
    }

    [Theory]
    [InlineData("-2147483648", "i32", int.MinValue)]
    [InlineData("-9223372036854775808", "i64", long.MinValue)]
    [InlineData("-9223372036854775808L", "i64", long.MinValue)]
    [InlineData("-0x8000000000000000L", "i64", long.MinValue)]
    [InlineData("4294967295U", "u32", uint.MaxValue)]
    [InlineData("18446744073709551615UL", "u64", ulong.MaxValue)]
    public void IntegerBoundaries_PreserveWidthAndSign(string expression, string type, object expected)
    {
        Assert.Equal(expected, Invoke(Compile(Source(expression, type)), "Probe"));
    }

    [Theory]
    [InlineData("0.0m", 1)]
    [InlineData("0.0000m", 4)]
    public void NegativeZero_PreservesFloatSingleAndDecimalSign(string decimalLiteral, int scale)
    {
        var type = Compile($$"""
            §M{Infix}
              §F{Double:pub} () -> f64
                §R -0.0
              §F{Single:pub} () -> f32
                §R -0.0f
              §F{Decimal:pub} () -> decimal
                §R -{{decimalLiteral}}
            """);
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(Assert.IsType<double>(Invoke(type, "Double"))));
        Assert.Equal(int.MinValue, BitConverter.SingleToInt32Bits(Assert.IsType<float>(Invoke(type, "Single"))));
        Assert.Equal(new[] { 0, 0, 0, int.MinValue | scale << 16 },
            decimal.GetBits(Assert.IsType<decimal>(Invoke(type, "Decimal"))));
    }

    [Theory]
    [InlineData("true & 1")]
    [InlineData("1 | false")]
    [InlineData("true ^ 1")]
    public void EagerBitwiseOperators_DoNotAcceptMixedBooleanAndNumericOperands(string expression)
    {
        var result = Program.Compile(Source(expression), "invalid-bitwise.calr",
            new CompilationOptions { EnableTypeChecking = true, EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData("9223372036854775808L")]
    [InlineData("0x8000000000000000L")]
    [InlineData("-(9223372036854775808L)")]
    [InlineData("-9223372036854775809")]
    [InlineData("-0xFFFFFFFFFFFFFFFF")]
    public void InvalidSignedMagnitude_ProducesOverflowDiagnostic(string expression)
    {
        var diagnostics = new DiagnosticBag();
        Parse(Source(expression, "i64"), diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.SignedIntegerLiteralOverflow);
    }

    [Fact]
    public void ShortCircuitAndEvaluateOnce_PreserveBranchingAndLeftToRightOrder()
    {
        var type = Compile("""
            §M{Infix}
              §F{Number:priv} (List<i32>:trace, i32:n) -> i32
                §E{mut}
                §C{trace.Add} §A n §/C
                §R n
              §F{Probe:pub} (List<i32>:trace, ?i32:x, bool:condition) -> i32
                §E{mut}
                §R condition ? x ?? §C{Number} §A trace §A 1 §/C + §C{Number} §A trace §A 2 §/C * §C{Number} §A trace §A 3 §/C : §C{Number} §A trace §A 4 §/C
              §F{And:pub} (bool:left, i32:zero) -> bool
                §R left && 1 / zero > 0
              §F{Or:pub} (bool:left, i32:zero) -> bool
                §R left || 1 / zero > 0
              §F{Eager:pub} (bool:left, i32:zero) -> bool
                §R left & (1 / zero > 0)
            """);
        var trace = new List<int>();
        Assert.Equal(7, Invoke(type, "Probe", trace, null, true));
        Assert.Equal([1, 2, 3], trace);
        trace.Clear();
        Assert.Equal(9, Invoke(type, "Probe", trace, 9, true));
        Assert.Empty(trace);
        Assert.Equal(4, Invoke(type, "Probe", trace, null, false));
        Assert.Equal([4], trace);
        Assert.Equal(false, Invoke(type, "And", false, 0));
        Assert.Equal(true, Invoke(type, "Or", true, 0));
        Assert.IsType<DivideByZeroException>(
            Assert.Throws<TargetInvocationException>(() => Invoke(type, "And", true, 0)).InnerException);
        Assert.IsType<DivideByZeroException>(
            Assert.Throws<TargetInvocationException>(() => Invoke(type, "Or", false, 0)).InnerException);
        Assert.IsType<DivideByZeroException>(
            Assert.Throws<TargetInvocationException>(() => Invoke(type, "Eager", false, 0)).InnerException);
    }

    [Fact]
    public void ArithmeticAndUnary_KeepCheckedOverflowBehavior()
    {
        var type = Compile("""
            §M{Infix:overflow=checked}
              §F{Probe:pub} (i32:x) -> i32
                §R x + 1 * 2
              §F{Negate:pub} (i32:x) -> i32
                §R -x
            """);
        Assert.IsType<OverflowException>(
            Assert.Throws<TargetInvocationException>(() => Invoke(type, "Probe", int.MaxValue)).InnerException);
        Assert.IsType<OverflowException>(
            Assert.Throws<TargetInvocationException>(() => Invoke(type, "Negate", int.MinValue)).InnerException);
    }

    [Fact]
    public void InterpolationHoles_ParseInfixWithRebasedSpans()
    {
        const string source = """
            §M{Infix}
              §F{Probe:pub} (i32:x) -> str
                §R "v=${x + 2 * 3}"
            """;
        var function = Assert.Single(Parse(source).Functions);
        var interpolation = Assert.IsType<InterpolatedStringNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body)).Expression);
        var expression = Assert.Single(interpolation.Parts.Where(part => part is InterpolatedStringExpressionNode));
        var hole = Assert.IsType<InterpolatedStringExpressionNode>(expression);
        Assert.Equal("x + 2 * 3", source.Substring(hole.Expression.Span.Start, hole.Expression.Span.Length));
        Assert.Equal("v=11", Invoke(Compile(source), "Probe", 5));
    }

    [Theory]
    [InlineData("!")]
    [InlineData("(")]
    [InlineData("a ?? ")]
    public void DeepNesting_ProducesDiagnosticRatherThanStackExhaustion(string fragment)
    {
        var diagnostics = new DiagnosticBag();
        var source = string.Concat(Enumerable.Repeat(fragment, 150)) + "a"
            + (fragment == "(" ? new string(')', 150) : "");
        Parse(Source(source), diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ExpressionNestingTooDeep);
    }

    private static string Source(string expression, string type = "i32") => $$"""
        §M{Infix}
          §F{Probe:pub} () -> {{type}}
            §R {{expression}}
        """;

    private static ExpressionNode ParseExpression(string expression) =>
        Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(Parse(Source(expression)).Functions).Body)).Expression!;

    private static ModuleNode Parse(string source, DiagnosticBag? diagnostics = null)
    {
        var requireSuccess = diagnostics == null;
        diagnostics ??= new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        if (requireSuccess)
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return module;
    }

    private static object? Invoke(Type type, string name, params object?[] arguments) =>
        type.GetMethod(name)!.Invoke(null, arguments);

    private static Type Compile(string source)
    {
        var result = Program.Compile(source, "infix.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("Infix_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("Infix.InfixModule", throwOnError: true)!;
    }
}
