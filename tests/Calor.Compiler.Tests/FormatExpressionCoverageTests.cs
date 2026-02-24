using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests that CalorFormatter.FormatExpression handles all expression node types
/// without falling back to /* TypeName */ comments.
/// </summary>
public class FormatExpressionCoverageTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string FormatModule(string calorSource)
    {
        var module = Parse(calorSource, out var diag);
        Assert.False(diag.HasErrors, "Parse failed:\n" + string.Join("\n", diag.Select(d => d.Message)));
        var formatter = new CalorFormatter();
        return formatter.Format(module);
    }

    #region Literal and Basic Expression Coverage

    [Fact]
    public void Format_ConditionalExpression_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{i32}
            §R (? true 1 0)
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ConditionalExpressionNode */", result);
        Assert.Contains("?", result);
    }

    [Fact]
    public void Format_DecimalLiteral_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{void}
            §B{x} DEC:3.14
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* DecimalLiteralNode */", result);
        Assert.Contains("DEC:", result);
    }

    #endregion

    #region String Interpolation

    [Fact]
    public void Format_InterpolatedString_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{str}
            §B{name} "World"
            §R §INTERP "Hello, " §EXP name "!" §/INTERP
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* InterpolatedStringNode */", result);
    }

    #endregion

    #region Range and Index

    [Fact]
    public void Format_RangeExpression_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{void}
            §B{r} §RANGE 1 5
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* RangeExpressionNode */", result);
        Assert.Contains("..", result);
    }

    [Fact]
    public void Format_IndexFromEnd_NoFallbackComment()
    {
        // IndexFromEnd uses ^offset syntax which is constructed by the C# converter
        var csharp = """
            using System;
            public class Foo
            {
                public int Bar(int[] arr)
                {
                    return arr[^1];
                }
            }
            """;
        var converter = new Migration.CSharpToCalorConverter();
        var convResult = converter.Convert(csharp);
        Assert.True(convResult.Success, string.Join("\n", convResult.Issues.Select(i => i.ToString())));

        var formatter = new CalorFormatter();
        var result = formatter.Format(convResult.Ast!);
        Assert.DoesNotContain("/* IndexFromEndNode */", result);
    }

    #endregion

    #region Collections

    [Fact]
    public void Format_ListCreation_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{void}
            §LIST{l1:i32}
              1
              2
              3
            §/LIST{l1}
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ListCreationNode */", result);
        Assert.Contains("§LIST", result);
    }

    [Fact]
    public void Format_DictionaryCreation_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{void}
            §DICT{d1:str:i32}
              §KV "one" 1
              §KV "two" 2
            §/DICT{d1}
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* DictionaryCreationNode */", result);
        Assert.Contains("§DICT", result);
    }

    [Fact]
    public void Format_SetCreation_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{void}
            §HSET{s1:str}
              "apple"
              "banana"
            §/HSET{s1}
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* SetCreationNode */", result);
        Assert.Contains("§HSET", result);
    }

    [Fact]
    public void Format_CollectionCount_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{i32}
            §LIST{l1:i32}
              1
            §/LIST{l1}
            §R §CNT l1
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* CollectionCountNode */", result);
        Assert.Contains("§CNT", result);
    }

    [Fact]
    public void Format_CollectionContains_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{bool}
            §LIST{l1:i32}
              1
            §/LIST{l1}
            §R §HAS{l1} 1
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* CollectionContainsNode */", result);
        Assert.Contains("§HAS", result);
    }

    #endregion

    #region Array Length

    [Fact]
    public void Format_ArrayLength_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{i32}
            §B{arr} §ARR{arr:i32} 1 2 3 §/ARR{arr}
            §R §LEN arr
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ArrayLengthNode */", result);
    }

    #endregion

    #region String Operations

    [Fact]
    public void Format_StringOperation_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{str}
            §B{s} "hello"
            §R (upper s)
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* StringOperationNode */", result);
        Assert.Contains("upper", result);
    }

    [Fact]
    public void Format_CharOperation_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{bool}
            §B{c} (char-lit "A")
            §R (is-letter c)
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* CharOperationNode */", result);
        Assert.Contains("is-letter", result);
    }

    [Fact]
    public void Format_StringBuilderOperation_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{str}
            §B{sb} (sb-new)
            §R (sb-tostring sb)
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* StringBuilderOperationNode */", result);
        Assert.Contains("sb-tostring", result);
    }

    #endregion

    #region Quantifiers

    [Fact]
    public void Format_ForallExpression_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §I{i32:x}
            §O{i32}
            §Q (forall ((n i32)) (>= n 0))
            §R x
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ForallExpressionNode */", result);
        Assert.Contains("forall", result);
    }

    [Fact]
    public void Format_ExistsExpression_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §I{i32:x}
            §O{i32}
            §Q (exists ((n i32)) (> n 0))
            §R x
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ExistsExpressionNode */", result);
        Assert.Contains("exists", result);
    }

    [Fact]
    public void Format_ImplicationExpression_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §I{i32:x}
            §O{i32}
            §Q (-> (> x 0) (>= x 1))
            §R x
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ImplicationExpressionNode */", result);
        Assert.Contains("->", result);
    }

    #endregion

    #region With Expression

    [Fact]
    public void Format_WithExpression_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{void}
            §B{p} §NEW{Person} §/NEW
            §B{p2} §WITH p
              §SET{Name} "Alice"
            §/WITH
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* WithExpressionNode */", result);
        Assert.Contains("with", result);
    }

    #endregion

    #region Generic Type

    [Fact]
    public void Format_GenericType_NoFallbackComment()
    {
        // GenericTypeNode is a legacy node; verify the formatter handles it
        // by constructing it inline as part of a simple expression
        var span = new Parsing.TextSpan(0, 0, 0, 0);
        var genNode = new GenericTypeNode(span, "List", new[] { "i32" });
        var formatter = new CalorFormatter();
        // FormatExpression is private, so test it indirectly via CalorEmitter
        // which uses the same format. Instead, use C# converter for a type that
        // produces GenericTypeNode via typeof() expression.
        var csharp = """
            using System.Collections.Generic;
            public class Foo
            {
                public void Bar()
                {
                    var t = typeof(List<int>);
                }
            }
            """;
        var converter = new Migration.CSharpToCalorConverter();
        var convResult = converter.Convert(csharp);
        Assert.True(convResult.Success, string.Join("\n", convResult.Issues.Select(i => i.ToString())));

        var result = formatter.Format(convResult.Ast!);
        Assert.DoesNotContain("/* GenericTypeNode */", result);
    }

    #endregion

    #region Anonymous Object

    [Fact]
    public void Format_AnonymousObject_NoFallbackComment()
    {
        var source = """
            §M{m1:Test}
            §F{f1:Foo:pub}
            §O{void}
            §B{obj} §ANON
              Name = "Alice"
              Age = 30
            §/ANON
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* AnonymousObjectCreationNode */", result);
        Assert.Contains("§ANON", result);
    }

    #endregion

    #region Comprehensive: No /* ... */ in Complex Formatted Output

    [Fact]
    public void Format_MixedExpressions_NoFallbackComments()
    {
        // A function using multiple expression types that were previously unhandled
        var source = """
            §M{m1:Test}
            §F{f1:Complex:pub}
            §I{str:input}
            §O{i32}
            §B{upper} (upper input)
            §B{length} (len upper)
            §B{result} (? (> length 5) 1 0)
            §R result
            §/F{f1}
            §/M{m1}
            """;
        var result = FormatModule(source);

        // None of these should appear — they indicate unhandled expression types
        Assert.DoesNotContain("/* StringOperationNode */", result);
        Assert.DoesNotContain("/* ConditionalExpressionNode */", result);
    }

    #endregion
}
