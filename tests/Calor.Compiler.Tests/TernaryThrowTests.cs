using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests that throw expressions retain their conditional branch through migration.
/// </summary>
public class TernaryThrowTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Ternary Throw — False Branch (flag ? value : throw ...)

    [Fact]
    public void Convert_TernaryThrowInFalseBranch_PreservesConditionalThrow()
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

        var ret = Assert.IsType<ReturnStatementNode>(Assert.Single(method.Body));
        var conditional = Assert.IsType<ConditionalExpressionNode>(ret.Expression);
        Assert.Equal("ok", Assert.IsType<ReferenceNode>(conditional.Condition).Name);
        Assert.Equal(42, Assert.IsType<IntLiteralNode>(conditional.WhenTrue).Value);
        Assert.IsType<ThrowExpressionNode>(conditional.WhenFalse);
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

        var ret = Assert.IsType<ReturnStatementNode>(Assert.Single(method.Body));
        var conditional = Assert.IsType<ConditionalExpressionNode>(ret.Expression);
        var throwExpr = Assert.IsType<ThrowExpressionNode>(conditional.WhenFalse);
        var newExpr = Assert.IsType<NewExpressionNode>(throwExpr.Exception);
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

        var binding = Assert.IsType<BindStatementNode>(Assert.Single(method.Body));
        var conditional = Assert.IsType<ConditionalExpressionNode>(binding.Initializer);
        Assert.Equal(100, Assert.IsType<IntLiteralNode>(conditional.WhenTrue).Value);
        Assert.IsType<ThrowExpressionNode>(conditional.WhenFalse);
    }

    #endregion

    #region Ternary Throw — True Branch (flag ? throw ... : value)

    [Fact]
    public void Convert_TernaryThrowInTrueBranch_PreservesConditionalThrow()
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

        var ret = Assert.IsType<ReturnStatementNode>(Assert.Single(method.Body));
        var conditional = Assert.IsType<ConditionalExpressionNode>(ret.Expression);
        Assert.Equal("isError", Assert.IsType<ReferenceNode>(conditional.Condition).Name);
        Assert.IsType<ThrowExpressionNode>(conditional.WhenTrue);
        Assert.Equal(0, Assert.IsType<IntLiteralNode>(conditional.WhenFalse).Value);
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
