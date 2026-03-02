using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Regression tests for converter bugs found during Humanizer migration gap analysis.
/// Bug 1: Lambda bodies not preserved (Aggregate, LINQ chains, lambda returns)
/// Bug 2: Ternary operator not round-tripping
/// Bug 4: Boolean literals not parsing
/// Bug 6: string.Empty not handled
/// All four bugs are now fixed — these tests prevent recurrence.
///
/// Each test has two layers:
///   1. AST-level: verifies the converter produces the correct node types (not interop fallback)
///   2. Round-trip: verifies C# → Calor text → Parse → C# compiles and preserves semantics
/// </summary>
public class HumanizerRegressionTests
{
    private readonly CSharpToCalorConverter _converter = new();

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"[{i.Severity}] {i.Message}"));
    }

    private ConversionResult Convert(string csharpSource)
    {
        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        return result;
    }

    /// <summary>
    /// Converts C# → Calor AST → Calor text → Parse → C# (full round-trip).
    /// </summary>
    private string RoundTrip(string csharpSource)
    {
        var result = Convert(csharpSource);

        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(result.Ast!);

        var compileResult = Program.Compile(calrText, "humanizer-regression.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors,
            $"Round-trip compilation failed:\n" +
            string.Join("\n", compileResult.Diagnostics.Select(d => d.Message)));

        return compileResult.GeneratedCode;
    }

    /// <summary>
    /// Asserts the method body was NOT silently wrapped in a CSharpInteropBlockNode.
    /// If the converter falls back to raw passthrough, the class will have interop blocks
    /// instead of proper AST nodes.
    /// </summary>
    private static void AssertNoInteropFallback(ModuleNode ast)
    {
        foreach (var cls in ast.Classes)
        {
            Assert.True(cls.InteropBlocks.Count == 0,
                $"Class '{cls.Name}' has {cls.InteropBlocks.Count} interop block(s) — " +
                $"converter fell back to raw C# passthrough: " +
                string.Join("; ", cls.InteropBlocks.Select(b => b.Reason ?? b.FeatureName ?? "unknown")));
        }
    }

    /// <summary>
    /// Recursively searches an expression tree for a node of type T.
    /// </summary>
    private static bool ContainsNodeType<T>(IReadOnlyList<StatementNode> body) where T : AstNode
    {
        foreach (var stmt in body)
        {
            if (stmt is T) return true;
            if (stmt is ReturnStatementNode ret && ret.Expression is T) return true;
            if (stmt is BindStatementNode bind && bind.Initializer is T) return true;
            if (stmt is IfStatementNode ifStmt)
            {
                if (ifStmt.Condition is T) return true;
                if (ContainsNodeType<T>(ifStmt.ThenBody)) return true;
                if (ifStmt.ElseBody != null && ContainsNodeType<T>(ifStmt.ElseBody)) return true;
            }
            if (stmt is ExpressionStatementNode exprStmt && exprStmt.Expression is T) return true;
        }
        return false;
    }

    #region Bug 1: Lambda bodies preserved

    [Fact]
    public void Bug1_Aggregate_LambdaWithMethodCallBody()
    {
        var csharp = """
            using System.Linq;
            public class StringHelper
            {
                public string Transform(string[] items)
                {
                    return items.Aggregate("", (current, s) => current + s);
                }
            }
            """;

        // AST-level: converter should produce proper nodes, not interop fallback
        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);

        // Round-trip: compiles back to valid C#
        var output = RoundTrip(csharp);
        Assert.Contains("Aggregate", output);
    }

    [Fact]
    public void Bug1_LinqChain_WhereSelectToList()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Linq;
            public class Filter
            {
                public List<string> Clean(List<string> items)
                {
                    return items.Where(s => !string.IsNullOrEmpty(s)).Select(s => s.Trim()).ToList();
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);

        var output = RoundTrip(csharp);
        Assert.Contains("Where", output);
        Assert.Contains("Select", output);
        Assert.Contains("ToList", output);
    }

    [Fact]
    public void Bug1_LambdaAsReturnValue()
    {
        var csharp = """
            using System;
            public class Factory
            {
                public Func<int, int> Multiplier(int factor)
                {
                    return x => x * factor;
                }
            }
            """;

        // AST-level: must contain a LambdaExpressionNode, not interop fallback
        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        Assert.True(ContainsNodeType<LambdaExpressionNode>(method.Body),
            "Expected LambdaExpressionNode in method body — lambda was not preserved as proper AST");

        var output = RoundTrip(csharp);
        Assert.Contains("factor", output);
    }

    [Fact]
    public void Bug1_SimpleLambdaInMethodCall()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Linq;
            public class Sorter
            {
                public List<string> SortByLength(List<string> items)
                {
                    return items.OrderBy(s => s.Length).ToList();
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);

        var output = RoundTrip(csharp);
        Assert.Contains("OrderBy", output);
        Assert.Contains("Length", output);
    }

    #endregion

    #region Bug 2: Ternary operator round-trips

    [Fact]
    public void Bug2_SimpleTernary()
    {
        var csharp = """
            public class Formatter
            {
                public string Sign(int x)
                {
                    return x > 0 ? "pos" : "neg";
                }
            }
            """;

        // AST-level: must produce ConditionalExpressionNode, not interop fallback
        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        Assert.True(ContainsNodeType<ConditionalExpressionNode>(method.Body),
            "Expected ConditionalExpressionNode in method body — ternary was not preserved as proper AST");

        var output = RoundTrip(csharp);
        Assert.Contains("pos", output);
        Assert.Contains("neg", output);
    }

    [Fact]
    public void Bug2_TernaryWithMethodCalls()
    {
        var csharp = """
            public class CaseConverter
            {
                public string Convert(string val, bool toUpper)
                {
                    return toUpper ? val.ToUpper() : val.ToLower();
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        Assert.True(ContainsNodeType<ConditionalExpressionNode>(method.Body),
            "Expected ConditionalExpressionNode — ternary with method calls not preserved");

        var output = RoundTrip(csharp);
        Assert.Contains("ToUpper", output);
        Assert.Contains("ToLower", output);
    }

    [Fact]
    public void Bug2_NullCoalescing()
    {
        var csharp = """
            public class SafeAccess
            {
                public int GetLength(string? s)
                {
                    return s?.Length ?? 0;
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);

        var output = RoundTrip(csharp);
        Assert.Contains("Length", output);
    }

    [Fact]
    public void Bug2_TernaryInAssignment()
    {
        var csharp = """
            public class Logic
            {
                public string Describe(int count)
                {
                    var label = count == 1 ? "item" : "items";
                    return label;
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        Assert.True(ContainsNodeType<ConditionalExpressionNode>(method.Body),
            "Expected ConditionalExpressionNode in bind initializer — ternary assignment not preserved");

        var output = RoundTrip(csharp);
        Assert.Contains("item", output);
    }

    #endregion

    #region Bug 4: Boolean literals parse

    [Fact]
    public void Bug4_ReturnTrue()
    {
        var csharp = """
            public class Validator
            {
                public bool IsValid()
                {
                    return true;
                }
            }
            """;

        // AST-level: must produce BoolLiteralNode(true)
        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var boolLit = Assert.IsType<BoolLiteralNode>(ret.Expression);
        Assert.True(boolLit.Value);

        var output = RoundTrip(csharp);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Bug4_ReturnFalse()
    {
        var csharp = """
            public class Validator
            {
                public bool IsInvalid()
                {
                    return false;
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var boolLit = Assert.IsType<BoolLiteralNode>(ret.Expression);
        Assert.False(boolLit.Value);

        var output = RoundTrip(csharp);
        Assert.Contains("false", output);
    }

    [Fact]
    public void Bug4_BooleanInComparison()
    {
        var csharp = """
            public class Checker
            {
                public string Check(bool flag)
                {
                    if (flag == true)
                    {
                        return "yes";
                    }
                    return "no";
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);

        var output = RoundTrip(csharp);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Bug4_ComplexBooleanExpression()
    {
        var csharp = """
            public class BoolLogic
            {
                public bool Compute(bool a)
                {
                    return a && true || !false;
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);

        var output = RoundTrip(csharp);
        Assert.Contains("true", output);
        Assert.Contains("false", output);
    }

    #endregion

    #region Bug 6: string.Empty handled

    [Fact]
    public void Bug6_ReturnStringEmpty()
    {
        var csharp = """
            public class Defaults
            {
                public string GetDefault()
                {
                    return string.Empty;
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);

        // The converter normalizes string.Empty → "" at the AST level (semantically equivalent).
        // The bug was about conversion *failing*, not about preserving the exact form.
        // Verify the Calor text has either string.Empty or the normalized "" literal.
        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(result.Ast!);
        Assert.True(calrText.Contains("string.Empty") || calrText.Contains("\"\""),
            $"Calor text should contain string.Empty or \"\", got:\n{calrText}");

        var output = RoundTrip(csharp);
        Assert.Contains("return", output);
    }

    [Fact]
    public void Bug6_AssignStringEmpty()
    {
        var csharp = """
            public class Resetter
            {
                private string _field;
                public Resetter()
                {
                    _field = string.Empty;
                }
                public string GetField()
                {
                    return _field;
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);

        // The converter normalizes string.Empty → "" at the AST level.
        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(result.Ast!);
        Assert.True(calrText.Contains("string.Empty") || calrText.Contains("\"\""),
            $"Calor text should contain string.Empty or \"\", got:\n{calrText}");

        var output = RoundTrip(csharp);
        Assert.Contains("_field", output);
    }

    [Fact]
    public void Bug6_StringIsNullOrEmpty()
    {
        var csharp = """
            public class Guard
            {
                public bool IsBlank(string s)
                {
                    return string.IsNullOrEmpty(s);
                }
            }
            """;

        var result = Convert(csharp);
        AssertNoInteropFallback(result.Ast!);

        var output = RoundTrip(csharp);
        Assert.Contains("IsNullOrEmpty", output);
    }

    #endregion
}
