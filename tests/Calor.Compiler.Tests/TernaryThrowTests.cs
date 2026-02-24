using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for ternary throw expression hoisting and round-trip.
///
/// Fixes: ConvertThrowExpression produced ErrExpressionNode for ternary throws
/// (flag ? x : throw ...) which degraded to Result.Err in generated C#.
/// Now hoists to guard statements like null-coalescing throws.
///
/// Also adds dedicated round-trip tests for all throw expression patterns.
/// </summary>
public class TernaryThrowTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Ternary Throw — False Branch (flag ? value : throw ...)

    [Fact]
    public void Convert_TernaryThrowInFalseBranch_HoistsNegatedGuard()
    {
        var csharp = """
            public class Service
            {
                public int Validate(bool ok)
                {
                    return ok ? 42 : throw new InvalidOperationException("failed");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);

        // Should have 2 statements: guard + return
        Assert.Equal(2, method.Body.Count);

        // Guard: if (!ok) throw new InvalidOperationException("failed")
        var guard = Assert.IsType<IfStatementNode>(method.Body[0]);
        var negation = Assert.IsType<UnaryOperationNode>(guard.Condition);
        Assert.Equal(UnaryOperator.Not, negation.Operator);
        var throwStmt = Assert.IsType<ThrowStatementNode>(guard.ThenBody[0]);
        Assert.NotNull(throwStmt.Exception);

        // Return: 42 (directly, no conditional wrapper)
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[1]);
        Assert.IsType<IntLiteralNode>(ret.Expression);
    }

    [Fact]
    public void Convert_TernaryThrowInFalseBranch_PreservesExceptionType()
    {
        var csharp = """
            public class Svc
            {
                public string Get(string? input)
                {
                    return input != null ? input : throw new ArgumentNullException(nameof(input));
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);

        var guard = Assert.IsType<IfStatementNode>(method.Body[0]);
        var throwStmt = Assert.IsType<ThrowStatementNode>(guard.ThenBody[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(throwStmt.Exception);
        Assert.Equal("ArgumentNullException", newExpr.TypeName);
    }

    [Fact]
    public void Convert_TernaryThrowInFalseBranch_Assignment()
    {
        var csharp = """
            public class Svc
            {
                public void Process(bool valid)
                {
                    int value = valid ? 100 : throw new Exception("bad");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);

        // Guard should be hoisted before the assignment
        Assert.True(method.Body.Count >= 2);
        Assert.IsType<IfStatementNode>(method.Body[0]);
    }

    #endregion

    #region Ternary Throw — True Branch (flag ? throw ... : value)

    [Fact]
    public void Convert_TernaryThrowInTrueBranch_HoistsDirectGuard()
    {
        var csharp = """
            public class Service
            {
                public int Check(bool isError)
                {
                    return isError ? throw new Exception("err") : 0;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);

        Assert.Equal(2, method.Body.Count);

        // Guard: if (isError) throw new Exception("err") — NO negation
        var guard = Assert.IsType<IfStatementNode>(method.Body[0]);
        // Condition should NOT be negated (throw is in true branch)
        Assert.IsNotType<UnaryOperationNode>(guard.Condition);
        var throwStmt = Assert.IsType<ThrowStatementNode>(guard.ThenBody[0]);
        Assert.NotNull(throwStmt.Exception);

        // Return: 0
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[1]);
        Assert.IsType<IntLiteralNode>(ret.Expression);
    }

    #endregion

    #region Regular Ternary (no throw) — Unchanged

    [Fact]
    public void Convert_RegularTernary_StillProducesConditionalExpression()
    {
        var csharp = """
            public class Svc
            {
                public int Max(int a, int b)
                {
                    return a > b ? a : b;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);

        // Regular ternary should NOT be hoisted — still a ConditionalExpression
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        Assert.IsType<ConditionalExpressionNode>(ret.Expression);
    }

    #endregion

    #region C# Round-Trip: C# → Calor → C#

    [Fact]
    public void RoundTrip_TernaryThrowFalse_ProducesThrowStatement()
    {
        var csharp = """
            public class Svc
            {
                public int Check(bool flag)
                {
                    return flag ? 42 : throw new InvalidOperationException("nope");
                }
            }
            """;

        // C# → Calor AST
        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        // Calor AST → C#
        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(convertResult.Ast!);

        // Should contain a proper throw statement, NOT Result.Err
        Assert.Contains("throw", regenerated);
        Assert.DoesNotContain("Result.Err", regenerated);
        Assert.Contains("InvalidOperationException", regenerated);
    }

    [Fact]
    public void RoundTrip_TernaryThrowTrue_ProducesThrowStatement()
    {
        var csharp = """
            public class Svc
            {
                public int Check(bool bad)
                {
                    return bad ? throw new Exception("bad") : 0;
                }
            }
            """;

        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(convertResult.Ast!);

        Assert.Contains("throw", regenerated);
        Assert.DoesNotContain("Result.Err", regenerated);
    }

    [Fact]
    public void RoundTrip_NullCoalesceThrow_ProducesThrowStatement()
    {
        var csharp = """
            public class Svc
            {
                public string GetName(string? input)
                {
                    return input ?? throw new ArgumentNullException(nameof(input));
                }
            }
            """;

        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(convertResult.Ast!);

        Assert.Contains("throw", regenerated);
        Assert.DoesNotContain("Result.Err", regenerated);
        Assert.Contains("ArgumentNullException", regenerated);
    }

    #endregion

    #region Calor Text Round-Trip: C# → Calor text → parse → C#

    [Fact]
    public void CalorRoundTrip_TernaryThrow_ParsesAndCompiles()
    {
        var csharp = """
            public class Svc
            {
                public int Check(bool flag)
                {
                    return flag ? 42 : throw new InvalidOperationException("nope");
                }
            }
            """;

        // C# → Calor AST
        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        // Calor AST → Calor text
        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(convertResult.Ast!);

        // Calor text should contain §TH (throw) not §ERR
        Assert.Contains("§TH", calrText);
        Assert.DoesNotContain("§ERR", calrText);

        // Calor text → parse → compile to C#
        var compileResult = Program.Compile(calrText, "round-trip.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
        Assert.Contains("throw", compileResult.GeneratedCode);
        Assert.DoesNotContain("Result.Err", compileResult.GeneratedCode);
    }

    [Fact]
    public void CalorRoundTrip_NullCoalesceThrow_ParsesAndCompiles()
    {
        var csharp = """
            public class Svc
            {
                public string Require(string? input)
                {
                    return input ?? throw new ArgumentNullException("input");
                }
            }
            """;

        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(convertResult.Ast!);

        Assert.Contains("§TH", calrText);

        var compileResult = Program.Compile(calrText, "round-trip.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
        Assert.Contains("throw", compileResult.GeneratedCode);
    }

    #endregion

    #region Migration Scoring

    [Fact]
    public void MigrationAnalyzer_TernaryThrow_NotPenalized()
    {
        var csharp = """
            using System;
            public class Svc
            {
                public int Check(bool flag)
                {
                    return flag ? 42 : throw new InvalidOperationException("nope");
                }
            }
            """;

        var analyzer = new MigrationAnalyzer();
        var score = analyzer.AnalyzeSource(csharp, "test.cs", "test.cs");

        // Ternary throws should NOT reduce the score — no "unsupported" penalty
        Assert.DoesNotContain(score.UnsupportedConstructs,
            c => c.Name.Contains("throw", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MigrationAnalyzer_BothCoalesceAndTernaryThrow_NotPenalized()
    {
        var csharp = """
            using System;
            public class Svc
            {
                public string Get(string? input, bool flag)
                {
                    var name = input ?? throw new ArgumentNullException(nameof(input));
                    return flag ? name : throw new InvalidOperationException("bad");
                }
            }
            """;

        var analyzer = new MigrationAnalyzer();
        var score = analyzer.AnalyzeSource(csharp, "test.cs", "test.cs");

        Assert.DoesNotContain(score.UnsupportedConstructs,
            c => c.Name.Contains("throw", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Helpers

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
            return string.Join("\n", result.Issues.Select(i => i.Message));
        return "Conversion failed with no specific error message";
    }

    private static string FormatDiagnostics(CompilationResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"[{d.Code}] {d.Message}");
        return string.Join("\n", errors);
    }

    #endregion
}
