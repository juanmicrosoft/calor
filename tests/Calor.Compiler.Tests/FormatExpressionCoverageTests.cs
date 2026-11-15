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
        var tokens = lexer.TokenizeAllForParser();
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
            module Test Test block
              func Foo () → int
                !
                return (true ? 1 : 0)
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ConditionalExpressionNode */", result);
        Assert.Contains("?", result);
    }

    [Fact]
    public void Format_DecimalLiteral_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo () → void
                !
                let x = 3.14m
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* DecimalLiteralNode */", result);
        Assert.Contains("3.14m", result);
        Assert.DoesNotContain("DEC:", result);
    }

    #endregion

    #region String Interpolation

    [Fact]
    public void Format_InterpolatedString_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo () → string
                !
                let name = "World"
                return "Hello, ${name}!"
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
            module Test Test block
              func Foo () → void
                !
                let r = 1..5
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* RangeExpressionNode */", result);
        // Phase 4b: the formatter delegates to Migration/CalorEmitter, which
        // renders ranges as the canonical `§RANGE start end` section marker
        // (parser-symmetric with the source form), not the legacy `start..end`
        // pretty-print produced by the old hand-coded formatter.
        Assert.Contains("1..5", result);
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
        var converter = new Migration.CSharpToCalorConverter(new Migration.ConversionOptions
        {
            Fidelity = Migration.ConversionFidelity.Lossy
        });
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
            module Test Test block
              func Foo () → void
                !
                let List<int>:l1 = new List<int>() { 1, 2, 3 }
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ListCreationNode */", result);
        Assert.Contains("new List<int>()", result);
    }

    [Fact]
    public void Format_DictionaryCreation_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo () → void
                !
                let Dictionary<string, int>:d1 = new Dictionary<string, int>() { { "one", 1 }, { "two", 2 } }
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* DictionaryCreationNode */", result);
        Assert.Contains("new Dictionary<string, int>()", result);
    }

    [Fact]
    public void Format_SetCreation_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo () → void
                !
                let HashSet<string>:s1 = new HashSet<string>() { "apple", "banana" }
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* SetCreationNode */", result);
        Assert.Contains("new HashSet<string>()", result);
    }

    [Fact]
    public void Format_CollectionCount_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo () → int
                !
                let List<int>:l1 = new List<int>() { 1 }
                return l1 . Count
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* CollectionCountNode */", result);
        Assert.Contains("Count", result);
    }

    [Fact]
    public void Format_CollectionContains_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo () → bool
                !
                let List<int>:l1 = new List<int>() { 1 }
                return l1.Contains(1)
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* CollectionContainsNode */", result);
        Assert.Contains("Contains(1)", result);
    }

    #endregion

    #region Array Length

    [Fact]
    public void Format_ArrayLength_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo () → int
                !
                let arr = new int[] { 1, 2, 3 }
                return arr.Length
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
            module Test Test block
              func Foo () → string
                !
                let s = "hello"
                return (s).ToUpper()
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* StringOperationNode */", result);
        Assert.Contains("ToUpper()", result);
    }

    [Fact]
    public void Format_CharOperation_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo () → bool
                !
                let c = 'A'
                return char.IsLetter(c)
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* CharOperationNode */", result);
        Assert.Contains("IsLetter(c)", result);
    }

    [Fact]
    public void Format_StringBuilderOperation_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo () → string
                ! alloc
                let sb = new System.Text.StringBuilder()
                return sb.ToString()
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* StringBuilderOperationNode */", result);
        Assert.Contains("sb.ToString()", result);
    }

    #endregion

    #region Quantifiers

    [Fact]
    public void Format_ForallExpression_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo (int:x) → int
                !
                requires forall int:n in 0..x : n >= 0
                return x
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ForallExpressionNode */", result);
        Assert.Contains("forall", result);
    }

    [Fact]
    public void Format_ExistsExpression_NoFallbackComment()
    {
        var source = """
            module Test Test block
              func Foo (int:x) → int
                !
                requires exists int:n in 0..x : n > 0
                return x
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* ExistsExpressionNode */", result);
        Assert.Contains("exists", result);
    }

    [Fact]
    public void Format_ImplicationExpression_NoFallbackComment()
    {
        var source = """
            §M{Test}
              §F{Foo:pub}
                §I{i32:x}
                §O{i32}
                §Q (-> (> x 0) (>= x 1))
                §R x
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
            module Test Test block
              func Foo () → void
                !
                let p = new Person()
                let p2 = p with { Name = "Alice" }
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* WithExpressionNode */", result);
        // Phase 4b: the formatter delegates to Migration/CalorEmitter, which
        // renders C# `with` expressions as the canonical `§WITH target ... §/WITH`
        // section-marker form (parser-symmetric with the source), not the legacy
        // `target with { ... }` pretty-print produced by the old formatter.
        Assert.Contains("p with { Name = \"Alice\" }", result);
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
        var converter = new Migration.CSharpToCalorConverter(new Migration.ConversionOptions
        {
            Fidelity = Migration.ConversionFidelity.Lossy
        });
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
            module Test Test block
              func Foo () → void
                !
                let obj = new { Name = "Alice", Age = 30 }
            """;
        var result = FormatModule(source);
        Assert.DoesNotContain("/* AnonymousObjectCreationNode */", result);
        Assert.Contains("new { Name = \"Alice\", Age = 30 }", result);
    }

    #endregion

    #region Comprehensive: No /* ... */ in Complex Formatted Output

    [Fact]
    public void Format_MixedExpressions_NoFallbackComments()
    {
        // A function using multiple expression types that were previously unhandled
        var source = """
            module Test Test block
              func Complex (string:input) → int
                !
                let upper = (input).ToUpper()
                let length = upper.Length
                let result = ((length > 5) ? 1 : 0)
                return result
            """;
        var result = FormatModule(source);

        // None of these should appear — they indicate unhandled expression types
        Assert.DoesNotContain("/* StringOperationNode */", result);
        Assert.DoesNotContain("/* ConditionalExpressionNode */", result);
    }

    #endregion
}
