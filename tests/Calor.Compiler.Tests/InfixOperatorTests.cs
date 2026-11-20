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
    [InlineData("(cast Func<string, bool> null)", "Func<string, bool>", "(Func<string, bool>)null")]
    [InlineData("(cast RuleComponent<Person, string> _rule.Current)", "RuleComponent<Person, string>", "(RuleComponent<Person, string>)_rule.Current")]
    [InlineData("(cast RuleBuilder<T, TProperty?> ruleBuilder)", "RuleBuilder<T, TProperty?>", "(RuleBuilder<T, TProperty?>)ruleBuilder")]
    [InlineData("(cast System.Func<string, List<bool>> null)", "System.Func<string, List<bool>>", "(System.Func<string, List<bool>>)null")]
    [InlineData("(cast global::System.Func<string, bool> null)", "global::System.Func<string, bool>", "(global::System.Func<string, bool>)null")]
    public void LegacyGenericCasts_DoNotBecomeTuples(string expression, string type, string nativeExpression)
    {
        AssertRejectedExpression(expression, "Expected CloseParen");
        var cast = Assert.IsType<TypeOperationNode>(ParseExpression(nativeExpression));
        Assert.Equal(TypeOp.Cast, cast.Operation);
        // Native cast type identity omits trivia; its source span must not.
        Assert.Equal(type.Replace(" ", ""), cast.TargetType);
        Assert.Equal(type, Source(nativeExpression).Substring(cast.TargetTypeSpan.Start, cast.TargetTypeSpan.Length));
    }

    [Theory]
    [InlineData("(cast ?i32 value)", "(int?)value", "int?", "int?", "Expected Colon")]
    [InlineData("(cast ?List<int> value)", "(List<int>?)value", "List<int>?", "List<int>?", "Expected Colon")]
    [InlineData("(cast i32? value)", "(int?)value", "int?", "int?", "Expected CloseParen")]
    [InlineData("(cast List<int>? value)", "(List<int>?)value", "List<int>?", "List<int>?", "Expected CloseParen")]
    [InlineData("(cast ?global::System.Int32 value)", "(global::System.Int32?)value", "global::System.Int32?", "global::System.Int32?", "Expected operand after ':'")]
    [InlineData("(cast ?i32 global::Namespace.Value)", "(int?)global::Namespace.Value", "int?", "int?", "Expected Colon", false)]
    [InlineData("(cast ?Dictionary<string, List<int?>> value)",
        "(Dictionary<string, List<int?>>?)value", "Dictionary<string,List<int?>>?", "Dictionary<string, List<int?>>?", "Expected Colon")]
    [InlineData("(cast ?(i32, string) value)", "((int, string)?)value", "(int,string)?", "(int, string)?", "Expected Colon")]
    public void NullableCastTypes_PreservePrefixPostfixAndExactTypeSpans(
        string expression, string nativeExpression, string type, string typeText, string rejection,
        bool nativeAdmitted = true)
    {
        AssertRejectedExpression(expression, rejection);
        if (nativeAdmitted)
        {
            var cast = Assert.IsType<TypeOperationNode>(ParseExpression(nativeExpression));
            Assert.Equal(TypeOp.Cast, cast.Operation);
            Assert.Equal(type, cast.TargetType);
            Assert.Equal(typeText, Source(nativeExpression).Substring(cast.TargetTypeSpan.Start, cast.TargetTypeSpan.Length));
        }
        else
        {
            // Alias-qualified values are not admitted; this is not positive cast/span coverage.
            AssertRejectedExpression(nativeExpression, "Expected statement but found Colon");
        }
    }

    [Theory]
    [InlineData("(cast ?left : right)")]
    [InlineData("(cast ?left:right)")]
    [InlineData("(cast?left:right)")]
    [InlineData("(cast ? left : right)")]
    [InlineData("(cast ?left + 1:right)")]
    [InlineData("(cast ?flag ? a : b : c)")]
    [InlineData("(cast ?List<int?> : fallback)")]
    [InlineData("(cast ?(nameof global::System.String) : fallback)", "(cast ?(nameof(global::System.String)) : fallback)")]
    [InlineData("(cast ?(a, b) : other)")]
    [InlineData("(cast ?(x ? a : b) : c)")]
    public void GroupedConditionals_KeepCastAsTheirCondition(string expression, string? nativeExpression = null)
    {
        if (nativeExpression != null)
        {
            AssertRejectedExpression(expression, "Expected OpenParen");
            expression = nativeExpression;
        }
        var conditional = Assert.IsType<ConditionalExpressionNode>(ParseExpression(expression));
        Assert.Equal("cast", Assert.IsType<ReferenceNode>(conditional.Condition).Name);
        Assert.Equal(expression.Length, conditional.Span.Length);
    }

    [Fact]
    public void NullableCastOperand_CanContainItsOwnConditional()
    {
        AssertRejectedExpression("(cast ?i32 (flag ? left : right))", "Expected Colon");
        var cast = Assert.IsType<TypeOperationNode>(ParseExpression("(int?)(flag ? left : right)"));
        Assert.Equal("int?", cast.TargetType);
        Assert.IsType<ConditionalExpressionNode>(cast.Operand);
        AssertRejectedExpression("(cast ?Func<i32, i32> §LAM{x:i32} x > 0 ? x : 0 §/LAM)",
            "Legacy section marker", DiagnosticCode.UnknownSectionMarker);
        var lambdaCast = Assert.IsType<TypeOperationNode>(
            ParseExpression("(Func<int, int>?)((int:x) => x > 0 ? x : 0)"));
        Assert.IsType<ConditionalExpressionNode>(
            Assert.IsType<LambdaExpressionNode>(lambdaCast.Operand).ExpressionBody);
    }

    [Theory]
    [InlineData("(cast ?)", "value as", "Use a C# type spelling")]
    [InlineData("(cast ?i32)", "(int?)", "Expected operand after '?'")]
    [InlineData("(cast ?List<int>)", "(List<int>?)", "Expected operand after '?'")]
    public void NullableCasts_KeepMissingTypeAndOperandDiagnostics(
        string expression, string nativeExpression, string rejection)
    {
        // Prefix casts now enter conditional parsing, not the retired Lisp diagnostic path.
        AssertRejectedExpression(expression, "Expected", DiagnosticCode.UnexpectedToken);
        AssertRejectedExpression(nativeExpression, rejection);
    }

    [Fact]
    public void NullableCastAndCastNamedCondition_RunWithDistinctSemantics()
    {
        var type = Compile("""
            module Infix Infix
              func Probe (bool:cast) → int
                !
                return (cast?4:9)
              func Nullable (object:value) → int?
                !
                return (int?)value
            """);
        Assert.Equal(4, Invoke(type, "Probe", true));
        Assert.Equal(9, Invoke(type, "Probe", false));
        Assert.Equal(23, Invoke(type, "Nullable", 23));
        Assert.Null(Invoke(type, "Nullable", (object?)null));
    }

    [Theory]
    [InlineData("(is o Func<i32, i32> Add)", "Func<int, int>", "Add", "o is Func<int, int> Add")]
    [InlineData("(is o Func<string, bool>)", "Func<string, bool>", null, "o is Func<string, bool>")]
    [InlineData("(is o Dictionary<string, List<bool>> match)", "Dictionary<string, List<bool>>", "match", "o is Dictionary<string, List<bool>> @match")]
    [InlineData("(is (as o Func<string, bool>) Func<string, bool> Add)", "Func<string, bool>", "Add", "(o as Func<string, bool>) is Func<string, bool> Add")]
    public void LegacyGenericTypeTests_PreserveTypesPatternVariablesAndSpans(
        string expression, string type, string? variable, string nativeExpression)
    {
        AssertRejectedExpression(expression, "Expected CloseParen");
        var pattern = Assert.IsType<IsPatternNode>(ParseExpression(nativeExpression));
        Assert.Equal(type.Replace(" ", ""), pattern.TargetType);
        Assert.Equal(variable, pattern.VariableName);
        Assert.Equal(type, Source(nativeExpression).Substring(pattern.TargetTypeSpan.Start, pattern.TargetTypeSpan.Length));
        if (pattern.Operand is TypeOperationNode operation)
            Assert.Equal(TypeOp.As, operation.Operation);
    }

    [Theory]
    [InlineData("(as o Func<string, bool>)", "Func<string, bool>", "o as Func<string, bool>")]
    [InlineData("(as (cast object o) Dictionary<string, List<bool>>)", "Dictionary<string, List<bool>>", "((object)o) as Dictionary<string, List<bool>>")]
    public void LegacyGenericAsExpressions_PreserveTypesAndSpans(string expression, string type, string nativeExpression)
    {
        AssertRejectedExpression(expression, "Expected CloseParen");
        var operation = Assert.IsType<TypeOperationNode>(ParseExpression(nativeExpression));
        Assert.Equal(TypeOp.As, operation.Operation);
        Assert.Equal(type.Replace(" ", ""), operation.TargetType);
        Assert.Equal(type, Source(nativeExpression).Substring(operation.TargetTypeSpan.Start, operation.TargetTypeSpan.Length));
    }

    [Fact]
    public void ParenthesizedTypeOperations_PreserveGroupingSpansAndRuntimeSemantics()
    {
        foreach (var op in new[] { "as", "is" })
        {
            var expression = $"(value {op} string) is object item";
            var outer = Assert.IsType<IsPatternNode>(ParseExpression(expression));
            Assert.Equal("object", outer.TargetType);
            Assert.Equal("item", outer.VariableName);
            Assert.Equal(expression.Length, outer.Span.Length);
            Assert.Equal("object", Source(expression).Substring(outer.TargetTypeSpan.Start, outer.TargetTypeSpan.Length));
            var inner = outer.Operand;
            var typeSpan = op == "as"
                ? Assert.IsType<TypeOperationNode>(inner).TargetTypeSpan
                : Assert.IsType<IsPatternNode>(inner).TargetTypeSpan;
            Assert.Equal($"(value {op} string)".Length, inner.Span.Length);
            Assert.Equal("string", Source(expression).Substring(typeSpan.Start, typeSpan.Length));
            Assert.Equal("string", op == "as"
                ? Assert.IsType<TypeOperationNode>(inner).TargetType
                : Assert.IsType<IsPatternNode>(inner).TargetType);
            if (op == "as")
                Assert.Equal(TypeOp.As, Assert.IsType<TypeOperationNode>(inner).Operation);
            var operand = op == "as"
                ? Assert.IsType<TypeOperationNode>(inner).Operand
                : Assert.IsType<IsPatternNode>(inner).Operand;
            Assert.Equal("value", Assert.IsType<ReferenceNode>(operand).Name);
        }
        foreach (var type in new[] { "@as", "Namespace.@is" })
            Assert.Equal(type, Assert.IsType<TypeOperationNode>(ParseExpression($"({type})value")).TargetType);
        foreach (var (malformed, rejection) in new[]
        {
            ("(value as) is object", "Use a C# type spelling"),
            ("(value is) == true", "Use a C# type spelling"),
            ("(value as string extra) is object", "Expected CloseParen"),
            ("(value is string item extra) == true", "Expected CloseParen")
        })
            AssertRejectedExpression(malformed, rejection);

        var typeWithOperations = Compile("""
            module Infix Infix
              func As (object:value) → bool
                !
                return (value as string) is string text && text.Length > 0
              func Is (object:value) → bool
                !
                return (value is string) is bool flag && flag
            """);
        foreach (var value in new object?[] { "text", "", 7, null })
        {
            Assert.Equal(value is string text && text.Length > 0, Invoke(typeWithOperations, "As", value));
            Assert.Equal(value is string, Invoke(typeWithOperations, "Is", value));
        }
    }

    [Theory]
    [InlineData("is")]
    [InlineData("as")]
    [InlineData("cast")]
    public void TypeOperationNames_WithoutOperandsRemainGroupedReferences(string name)
    {
        Assert.Equal(name, Assert.IsType<ReferenceNode>(ParseExpression($"({name})")).Name);
    }

    [Theory]
    [InlineData("((cast Func<string, bool> null), 1)", "((Func<string, bool>)null, 1)")]
    [InlineData("((a > b), c)")]
    [InlineData("((a < b), c > d)")]
    [InlineData("(cast, other)")]
    [InlineData("(cast < b, c > d)")]
    [InlineData("(Func<string, bool>, other)")]
    [InlineData("(Dictionary<string, List<int>>, other)")]
    [InlineData("(Container<Dictionary<string, List<int>>, bool>, other)")]
    [InlineData("((is o Func<string, bool> match), a < b)", "((o is Func<string, bool> @match), a < b)")]
    [InlineData("((as o Func<string, bool>), a > b)", "((o as Func<string, bool>), a > b)")]
    [InlineData("(is, other)")]
    [InlineData("(as, other)")]
    [InlineData("(is < b, c > d)")]
    [InlineData("(as < b, c > d)")]
    [InlineData("(cast?left:right, other)")]
    [InlineData("((cast?left:right), other)")]
    public void TupleLookahead_DistinguishesNestedGenericsComparisonsAndTypeOperationNames(
        string expression, string? nativeExpression = null)
    {
        if (nativeExpression != null)
        {
            AssertRejectedExpression(expression, "Expected CloseParen");
            expression = nativeExpression;
        }
        Assert.Equal(2, Assert.IsType<TupleLiteralNode>(ParseExpression(expression)).Elements.Count);
    }

    [Theory]
    [InlineData("§C{_expr004.Clear}", "_expr004.Clear()")]
    [InlineData("§B{next} 1", "let next = 1")]
    [InlineData("§R", "return")]
    public void GenericReference_EndsBeforeTheNextStatement(string nextStatement, string nativeStatement)
    {
        AssertRejectedExpression(nextStatement, "Legacy section marker", DiagnosticCode.UnknownSectionMarker);
        var module = Parse($$"""
            module Infix Infix
              func Probe () → void
                !
                let _expr004 = AccessorCache<Person>
                {{nativeStatement}}
            """);
        var body = Assert.Single(module.Functions).Body;
        Assert.Equal(2, body.Count);
        var binding = Assert.IsType<BindStatementNode>(body[0]);
        Assert.Equal("AccessorCache<Person>", Assert.IsType<ReferenceNode>(binding.Initializer).Name);
    }

    [Theory]
    [InlineData("return (Func<string, bool>)null;", "(Func<string, bool>)")]
    [InlineData("return (RuleComponent<Person, string>)_rule.Current;", "(RuleComponent<Person, string>)")]
    [InlineData("return (RuleBuilder<T, TProperty?>)ruleBuilder;", "(RuleBuilder<T, TProperty?>)")]
    [InlineData("AccessorCache<Person>.Clear(); return null;", "AccessorCache<Person>")]
    [InlineData("return ruleBuilder is Func<string, bool> Add;", "is Func<string, bool> Add")]
    [InlineData("return ruleBuilder as Func<string, bool>;", "as Func<string, bool>")]
    [InlineData("return (ruleBuilder is Func<string, bool> Add, ruleBuilder as Func<string, bool>);", "is Func<string, bool> Add")]
    public void ConvertedGenericTypeOperationsAndStaticCalls_ReparseWithoutLosingTheirShape(
        string body, string expectedForm)
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
        Assert.True(source.Contains(expectedForm, StringComparison.Ordinal),
            source + Environment.NewLine
                + string.Join(Environment.NewLine, conversion.Issues.Select(issue => issue.Message)));
        Parse(new CalorEmitter().Emit(module));
    }

    [Fact]
    public void StagedPrefixNegativeOperands_RemainDistinctFromUnspacedUnaryGrouping()
    {
        // The old spaced-prefix exception is gone: this is now -x minus a positive magnitude.
        var regrouped = Assert.IsType<BinaryOperationNode>(ParseExpression("(- x -2147483648)"));
        Assert.Equal(BinaryOperator.Subtract, regrouped.Operator);
        Assert.IsType<UnaryOperationNode>(regrouped.Left);
        Assert.Equal(IntegerLiteralSign.Positive, Assert.IsType<IntLiteralNode>(regrouped.Right).Sign);
        var prefix = Assert.IsType<BinaryOperationNode>(ParseExpression("x - -2147483648"));
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
            module Infix Infix
              func Probe (int:x | (# >= 0 && # < 10) = 1 + 2 * 3) → int
                !
                => result > x
                let int:n = x + 1
                Sink(n * 2, x - 1)
                return n
              func Next () → int
                !
                return 4
            """;
        var module = Parse(source);
        Assert.Equal(2, module.Functions.Count);
        var function = module.Functions[0];
        Assert.Single(function.Preconditions);
        Assert.Single(function.Postconditions);
        Assert.Equal(3, function.Body.Count);
        var call = Assert.IsType<CallExpressionNode>(
            Assert.IsType<ExpressionStatementNode>(function.Body[1]).Expression);
        Assert.Equal(2, call.Arguments.Count);
        Assert.All(call.Arguments, argument => Assert.IsType<BinaryOperationNode>(argument));
    }

    [Fact]
    public void MissingSameLineOperand_LeavesTheNextStatementForRecovery()
    {
        var diagnostics = new DiagnosticBag();
        var module = Parse("""
            module Infix Infix
              func Probe (int:a, int:b) → int
                return a +
                return b
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
    [InlineData("§ASSIGN x -5", "set x -5")]
    [InlineData("§SETIDX{items} 0 -5", "set items[0] -5")]
    [InlineData("§INS{items} 0 -5", "items.Insert(0, -5)")]
    [InlineData("§PUT{map} \"a\" -5", "set map[\"a\"] -5")]
    public void PositionalStatementOperands_DoNotConsumeANegativeValue(string statement, string nativeStatement)
    {
        var diagnostics = new DiagnosticBag();
        Parse($"module Infix\n  func Probe () → void\n    !\n    {statement}", diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownSectionMarker
            && diagnostic.Message.Contains("Legacy section marker", StringComparison.Ordinal));
        var module = Parse($$"""
            module Infix Infix
              func Probe () → void
                !
                {{nativeStatement}}
            """);
        var parsed = Assert.Single(Assert.Single(module.Functions).Body);
        var value = parsed switch
        {
            AssignmentStatementNode assignment => assignment.Value,
            ExpressionStatementNode expression => Assert.IsType<ExpressionCallNode>(expression.Expression).Arguments[1],
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(-5, Assert.IsType<IntLiteralNode>(value).Value);
    }

    [Fact]
    public void PositionalExpressionOperands_DoNotConsumeNegativeIndicesOrFallbacks()
    {
        AssertRejectedExpression("§IDX a -5", "Legacy section marker", DiagnosticCode.UnknownSectionMarker);
        var access = Assert.IsType<ArrayAccessNode>(ParseExpression("a[-5]"));
        Assert.Equal("a", Assert.IsType<ReferenceNode>(access.Array).Name);
        Assert.Equal(-5, Assert.IsType<IntLiteralNode>(access.Index).Value);
        AssertRejectedExpression("§?? x -5", "Legacy section marker", DiagnosticCode.UnknownSectionMarker);
        var coalesce = Assert.IsType<NullCoalesceNode>(ParseExpression("x ?? -5"));
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
    [InlineData("(cast Func<string, bool>)", "(Func<string, bool>)", null, true)]
    [InlineData("(cast Func<string, bool> null extra)", "(Func<string, bool>)null extra", "A native expression statement must be an invocation")]
    [InlineData("(is o)", "o is", "Use a C# type spelling")]
    [InlineData("(as o)", "o as", "Use a C# type spelling")]
    [InlineData("(is o Func<string, bool> match extra)", "o is Func<string, bool> @match extra", "A native expression statement must be an invocation")]
    [InlineData("(as o Func<string, bool> extra)", "o as Func<string, bool> extra", "A native expression statement must be an invocation")]
    [InlineData("(cast ?i32 value extra)", "(int?)value extra", "A native expression statement must be an invocation")]
    [InlineData("(cast ? : value)", "(?)value", "Expected expression but found Question")]
    [InlineData("(cast?left:)")]
    [InlineData("(cast ?left + 1)")]
    [InlineData("a +\n    return b")]
    public void MalformedOrUnsupportedForms_ProduceErrors(
        string expression, string? nativeExpression = null, string? nativeRejection = null,
        bool nativeRequiresBinding = false)
    {
        var diagnostics = new DiagnosticBag();
        Parse(Source(expression), diagnostics);
        Assert.True(diagnostics.HasErrors, expression);
        if (nativeExpression != null)
        {
            if (nativeRequiresBinding)
            {
                // Without an operand a generic name is grouping, not a cast.
                Assert.IsType<ReferenceNode>(ParseExpression(nativeExpression));
                var result = Program.Compile(Source(nativeExpression), "missing-cast-operand.calr",
                    new CompilationOptions { EnableTypeChecking = true, EnforceEffects = false, StatusWriter = TextWriter.Null });
                Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.UndefinedReference);
            }
            else
                AssertRejectedExpression(nativeExpression, Assert.IsType<string>(nativeRejection));
        }
    }

    [Theory]
    [InlineData("1+2*3", "int", 7)]
    [InlineData("(1+2)*3", "int", 9)]
    [InlineData("20-5-3", "int", 12)]
    [InlineData("24/4/2", "int", 3)]
    [InlineData("20%6*3", "int", 6)]
    [InlineData("1<<2+1", "int", 8)]
    [InlineData("32>>1+2", "int", 4)]
    [InlineData("1|2^3&6", "int", 1)]
    [InlineData("!false && false || true", "bool", true)]
    [InlineData("false & true || true", "bool", true)]
    [InlineData("true ^ false & false", "bool", true)]
    [InlineData("true | false && false", "bool", false)]
    [InlineData("1+2<4 == true", "bool", true)]
    [InlineData("1<2 && 2<=2 && 3>2 && 3>=3 && 1!=2", "bool", true)]
    [InlineData("-2*-3+~0", "int", 5)]
    [InlineData("-(2+3)*4", "int", -20)]
    [InlineData("false ? 1 : true ? 2 : 3", "int", 2)]
    [InlineData("true ? false ? 1 : 2 : 3", "int", 2)]
    public void Runtime_UsesSelectedCSharpPrecedence(string expression, string type, object expected)
    {
        Assert.Equal(expected, Invoke(Compile(Source(expression, type)), "Probe"));
    }

    [Theory]
    [InlineData("-2147483648", "int", int.MinValue)]
    [InlineData("-9223372036854775808", "long", long.MinValue)]
    [InlineData("-9223372036854775808L", "long", long.MinValue)]
    [InlineData("-0x8000000000000000L", "long", long.MinValue)]
    [InlineData("4294967295U", "uint", uint.MaxValue)]
    [InlineData("18446744073709551615UL", "ulong", ulong.MaxValue)]
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
            module Infix Infix
              func Double () → double
                !
                return -0.0
              func Single () → float
                !
                return -0.0f
              func Decimal () → decimal
                !
                return -{{decimalLiteral}}
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
        Parse(Source(expression, "long"), diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.SignedIntegerLiteralOverflow);
    }

    [Fact]
    public void ShortCircuitAndEvaluateOnce_PreserveBranchingAndLeftToRightOrder()
    {
        var type = Compile("""
            module Infix Infix
              func Number private (List<int>:trace, int:n) → int
                trace.Add(n)
                return n
              func Probe (List<int>:trace, int?:x, bool:condition) → int
                ! mut
                return condition ? x ?? Number(trace, 1) + Number(trace, 2) * Number(trace, 3) : Number(trace, 4)
              func And (bool:left, int:zero) → bool
                !
                return left && 1 / zero > 0
              func Or (bool:left, int:zero) → bool
                !
                return left || 1 / zero > 0
              func Eager (bool:left, int:zero) → bool
                !
                return left & (1 / zero > 0)
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
            module Infix Infix overflow checked
              func Probe (int:x) → int
                !
                return x + 1 * 2
              func Negate (int:x) → int
                !
                return -x
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
            module Infix Infix
              func Probe (int:x) → string
                !
                return "v=${x + 2 * 3}"
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

    private static string Source(string expression, string type = "int") => $$"""
        module Infix Infix
          func Probe () → {{type}}
            !
            return {{expression}}
        """;

    private static ExpressionNode ParseExpression(string expression) =>
        Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(Parse(Source(expression)).Functions).Body)).Expression!;

    private static void AssertRejectedExpression(
        string expression, string message, string code = DiagnosticCode.UnexpectedToken)
    {
        var diagnostics = new DiagnosticBag();
        Parse(Source(expression), diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == code
            && diagnostic.Message.Contains(message, StringComparison.Ordinal));
    }

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
