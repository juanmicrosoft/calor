using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B5: conversion + type/pattern + decimals — the SEMANTIC-REPAIR family. These
/// tests pin the three #762 evidence bullets dead: decimals no longer downcast through
/// double, casts carry their TARGET type instead of the operand's, and `is`/pattern
/// tests are real BOOL type tests instead of literal true.
/// </summary>
public class BinderConversionPatternFamilyTests
{
    private static readonly TextSpan S = new(0, 0, 1, 1);

    private static (BoundExpression Expr, DiagnosticBag Diagnostics) BindReturn(ExpressionNode expr)
    {
        var func = new FunctionNode(S, "f001", "Probe", Visibility.Public,
            Array.Empty<ParameterNode>(), new OutputNode(S, "OBJECT"), null,
            new StatementNode[] { new ReturnStatementNode(S, expr) },
            new AttributeCollection());
        var module = new ModuleNode(S, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(), new[] { func }, new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        var ret = bound.Functions.Single().Body.OfType<BoundReturnStatement>().Single();
        return (ret.Expression!, diagnostics);
    }

    private static void AssertComplete(DiagnosticBag d) =>
        Assert.DoesNotContain(d, x => x.Code == DiagnosticCode.AnalysisIncomplete);

    [Fact]
    public void DecimalLiteral_KeepsFullPrecision_NoDoubleDowncast()
    {
        // 0.1m + a 28-digit tail: provably not double-representable. The old arm bound
        // this as BoundFloatLiteral((double)value) — #762 item 4's defect.
        const decimal precise = 0.1234567890123456789012345678m;
        Assert.NotEqual(precise, (decimal)(double)precise); // the downcast WOULD lose it

        var (expr, diags) = BindReturn(new DecimalLiteralNode(S, precise));
        var bound = Assert.IsType<BoundDecimalLiteral>(expr);
        Assert.Equal(precise, bound.Value);
        Assert.Equal("DECIMAL", bound.Type.DisplayString);
        AssertComplete(diags);
    }

    [Fact]
    public void Cast_CarriesTargetType_RetainsOperand()
    {
        var (expr, diags) = BindReturn(new TypeOperationNode(S, TypeOp.Cast,
            new IntLiteralNode(S, 42), "f64"));
        var conv = Assert.IsType<BoundTypeOperationExpression>(expr);
        Assert.Equal("f64", conv.Type.DisplayString); // the TARGET, not the operand's INT
        Assert.IsType<BoundIntLiteral>(conv.Operand);
        Assert.Equal(TypeOp.Cast, conv.Operation);
        AssertComplete(diags);
    }

    [Fact]
    public void As_CarriesTargetType()
    {
        var (expr, _) = BindReturn(new TypeOperationNode(S, TypeOp.As,
            new ReferenceNode(S, "obj"), "MyClass"));
        Assert.Equal("MyClass", Assert.IsType<BoundTypeOperationExpression>(expr).Type.DisplayString);
    }

    [Fact]
    public void Is_IsARealTypeTest_NotLiteralTrue()
    {
        var (expr, _) = BindReturn(new TypeOperationNode(S, TypeOp.Is,
            new ReferenceNode(S, "x"), "str"));
        var test = Assert.IsType<BoundTypeOperationExpression>(expr);
        Assert.Equal("BOOL", test.Type.DisplayString);
        Assert.Equal("str", test.TargetType);
        // The defect shape: any checker seeing BoundBoolLiteral(true) here could fold.
        Assert.IsNotType<BoundBoolLiteral>(expr);
    }

    [Fact]
    public void IsPattern_RetainsOperandAndVariable()
    {
        var (expr, diags) = BindReturn(new IsPatternNode(S,
            new ReferenceNode(S, "x"), "Circle", "c"));
        var test = Assert.IsType<BoundIsPatternExpression>(expr);
        Assert.Equal("Circle", test.TargetType);
        Assert.Equal("c", test.VariableName);
        Assert.IsType<BoundVariableExpression>(test.Operand);
        AssertComplete(diags);
    }

    [Fact]
    public void TypeOf_BindsWithTypeResult()
    {
        var (expr, diags) = BindReturn(new TypeOfExpressionNode(S, "MyClass"));
        var t = Assert.IsType<BoundStructuralExpression>(expr);
        Assert.Equal("TYPE", t.Type.DisplayString);
        Assert.Equal("MyClass", t.Metadata["OperandType"]);
        AssertComplete(diags);
    }

    [Fact]
    public void BoundChildren_EnumeratesEveryB5NodeChild()
    {
        var i = new BoundIntLiteral(S, 1);
        Assert.Equal([i], BoundChildren.Of(
            new BoundTypeOperationExpression(S, TypeOp.Cast, i, "f64")));
        Assert.Equal([i], BoundChildren.Of(new BoundIsPatternExpression(S, i, "str", null)));
        Assert.Empty(BoundChildren.Of(
            new BoundStructuralExpression(S, nameof(TypeOfExpressionNode), "TYPE")));
        Assert.Empty(BoundChildren.Of(new BoundDecimalLiteral(S, 1m)));
    }

    [Fact]
    public void DecimalZeroDivisor_ProducesHardError_UnderDefaultOptions()
    {
        // Review C1's regression pin: pre-B5 DEC:0 bound as BoundFloatLiteral(0.0) and
        // IsLiteralZero caught it; the B5 BoundDecimalLiteral fell through to false and
        // a hard ERROR silently became nothing. DEFAULT options (ReportOnlyVerified) —
        // the literal-zero case must not need --all-findings.
        const string source = @"
§M{m001:Test}
  §F{f001:Trap:pub} (i32:x) -> dec
    §R (/ x DEC:0)";

        // ReportOnlyVerified=true IS the CLI default (--analyze without --all-findings);
        // BugPatternOptions' own default is false, so it must be set explicitly or this
        // pin silently tests all-findings mode (the first draft's non-discrimination bug).
        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
            VerificationAnalysisOptions = new Compiler.Analysis.VerificationAnalysisOptions
            {
                BugPatternOptions = new Compiler.Analysis.BugPatterns.BugPatternOptions
                {
                    ReportOnlyVerified = true
                }
            }
        });

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.DivisionByZero
                 && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CastDivisor_KeepsVerifiedWarning_UnderDefaultOptions()
    {
        // Review C2's regression pin: the old Cast arm returned the operand bare, so a
        // cast divisor reached Z3 as a variable; the B5 conversion wrapper made it
        // untranslatable and the verified Warning vanished. CheckDivisor now unwraps
        // conversions (numeric casts are zero-preserving).
        const string source = @"
§M{m001:Test}
  §F{f001:Trap:pub} (i32:y) -> i64
    §R (/ 100 (cast i64 y))";

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
            VerificationAnalysisOptions = new Compiler.Analysis.VerificationAnalysisOptions
            {
                BugPatternOptions = new Compiler.Analysis.BugPatterns.BugPatternOptions
                {
                    ReportOnlyVerified = true
                }
            }
        });

        // The VERIFIED warning, not an inconclusive Info — severity is the discriminator.
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.DivisionByZero
                 && d.Severity != DiagnosticSeverity.Info);
    }

    [Fact]
    public void PatternVariable_IsDeclaredInScope_UsableAfterTest()
    {
        // Review M3: `x is str s` DECLARES s. Pre-fix, s was retained into the bound
        // node while every later use was a hard Undefined-variable error.
        var test = new IsPatternNode(S, new ReferenceNode(S, "x"), "str", "s");
        var use = new ReturnStatementNode(S, new ReferenceNode(S, "s"));
        var func = new FunctionNode(S, "f001", "Probe", Visibility.Public,
            new[] { new ParameterNode(S, "x", "OBJECT", new AttributeCollection()) }, new OutputNode(S, "str"), null,
            new StatementNode[] { new ExpressionStatementNode(S, test), use },
            new AttributeCollection());
        var module = new ModuleNode(S, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(), new[] { func }, new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(module);
        Assert.DoesNotContain(diagnostics,
            d => d.Code == DiagnosticCode.UndefinedReference && d.Message.Contains("'s'"));
    }

    [Fact]
    public void NestedDivision_InsideCastOperand_ProducesRealFinding_EndToEnd()
    {
        // The family's span-anchored e2e pin: /0 inside a cast operand must produce
        // the real Calor0920 — the old Cast arm returned the operand so this already
        // worked; the pin guards the new conversion node against regressing it.
        const string source = @"
§M{m001:Test}
  §F{f001:Trap:pub} () -> void
    §E{cw}
    §P (cast f64 (/ 10 0))";

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
            VerificationAnalysisOptions = new Compiler.Analysis.VerificationAnalysisOptions
            {
                BugPatternOptions = new Compiler.Analysis.BugPatterns.BugPatternOptions
                {
                    ReportOnlyVerified = false
                }
            }
        });

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.DivisionByZero && d.Span.Line == 5);
    }
}
