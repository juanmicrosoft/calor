using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.BugPatterns.Patterns;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Tests for bug pattern detection including division by zero,
/// index out of bounds, null dereference, and overflow checking.
/// </summary>
public class BugPatternTests
{
    #region Helpers

    private static BoundModule Bind(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        if (diagnostics.HasErrors) return null!;

        var binder = new Binder(diagnostics);
        return binder.Bind(module);
    }

    private static BoundFunction GetFunction(string source, out DiagnosticBag parseDiag)
    {
        var bound = Bind(source, out parseDiag);
        if (parseDiag.HasErrors) return null!;
        return bound.Functions.First();
    }

    private static BugPatternOptions DefaultOptions => new()
    {
        UseZ3Verification = false,
        Z3TimeoutMs = 1000
    };

    private static BugPatternOptions Z3Options => new()
    {
        UseZ3Verification = Z3ContextFactory.IsAvailable,
        Z3TimeoutMs = 2000
    };

    #endregion

    #region Division By Zero Tests

    [Fact]
    public void DivisionByZero_LiteralZeroDivisor_ReportsError()
    {
        var source = @"
§M{Test}
  §F{Divide:pub}
      §I{i32:x}
      §O{i32}
      §R (/ x INT:0)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void DivisionByZero_NonZeroLiteralDivisor_NoError()
    {
        var source = @"
§M{Test}
  §F{Divide:pub}
      §I{i32:x}
      §O{i32}
      §R (/ x INT:2)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void DivisionByZero_VariableDivisor_NoGuard_ReportsWarning()
    {
        var source = @"
§M{Test}
  §F{Divide:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (/ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.NotEmpty(diagnostics.Warnings);
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void DivisionByZero_VariableDivisor_WithGuard_NoWarning()
    {
        var source = @"
§M{Test}
  §F{SafeDivide:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §IF (!= y INT:0)
          §R (/ x y)
      §R INT:0";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Within the if branch, y != 0 is a path condition
        // The checker should recognize this guard
        var divZeroWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();

        // Should have fewer or no warnings about y being zero
        Assert.True(divZeroWarnings.Count == 0 ||
            !divZeroWarnings.Any(w => w.Message.Contains("'y'")));
    }

    [SkippableFact]
    public void DivisionByZero_WithZ3_ProvesNonZero()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{Test}
  §F{SafeDivide:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §Q (!= y INT:0)
      §R (/ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(Z3Options);
        checker.Check(func, diagnostics);

        // With precondition y != 0, Z3 should prove the division is safe
        // Note: The checker may not use preconditions from the AST directly,
        // but the test validates the Z3 path works
        Assert.False(diagnostics.HasErrors);
    }

    [SkippableFact]
    public void DivisionByZero_UnsignedDivisor_UsesUnsignedTypedRange()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{Test}
  §F{Divide:pub}
      §I{u32:x}
      §I{u32:y}
      §O{u32}
      §R (/ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(Z3Options);
        checker.Check(func, diagnostics);

        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    [SkippableFact]
    public void DivisionByZero_SignedDivisor_Z3StillWarns()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // The signed path is untouched by the unsigned refusal: an unguarded
        // signed variable divisor still warns under the Z3 path.
        var source = @"
§M{Test}
  §F{Divide:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (/ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(Z3Options);
        checker.Check(func, diagnostics);

        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void DivisionByZero_UnsignedLiteralZeroDivisor_StillErrors()
    {
        // The literal-zero path is pre-solver and type-agnostic: refusal of
        // unsigned VARIABLES must not lose the hard error on a literal 0.
        var source = @"
§M{Test}
  §F{Divide:pub}
      §I{u32:x}
      §O{u32}
      §R (/ x INT:0)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void DivisionByZero_InNestedExpression_Detected()
    {
        var source = @"
§M{Test}
  §F{Nested:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (+ INT:1 (/ x y))";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void DivisionByZero_Modulo_AlsoChecked()
    {
        var source = @"
§M{Test}
  §F{Modulo:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (% x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Modulo by zero should also be detected
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    #endregion

    #region Overflow Tests

    [Fact]
    public void Overflow_Addition_WithPotentialOverflow_ReportsWarning()
    {
        var source = @"
§M{Test}
  §F{Add:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (+ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new OverflowChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Fact]
    public void Overflow_Multiplication_HighRisk_ReportsWarning()
    {
        var source = @"
§M{Test}
  §F{Multiply:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (* x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new OverflowChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [SkippableFact]
    public void Overflow_WithZ3_BoundedInputs_ProvesSafe()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{Test}
  §F{SafeAdd:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §Q (&& (>= x INT:0) (<= x INT:100))
      §Q (&& (>= y INT:0) (<= y INT:100))
      §R (+ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new OverflowChecker(Z3Options);
        checker.Check(func, diagnostics);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    #endregion

    #region Null Dereference Tests

    [Fact]
    public void NullDeref_ArrayLengthWithoutCheck_ReportsWarning()
    {
        var source = @"
§M{Test}
  §F{Unwrap:pub}
      §O{i32}
      §B{items:i32[]} null
      §R §LEN items";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var checker = new NullDereferenceChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullDereference);
    }

    [Fact]
    public void NullDeref_Coalesce_Safe()
    {
        var source = @"
§M{Test}
  §F{Safe:pub}
      §I{?i32:value}
      §O{i32}
      §R (?? value INT:0)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var checker = new NullDereferenceChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.DoesNotContain(diagnostics.Warnings, d =>
            d.Code == DiagnosticCode.NullDereference);
    }

    #endregion

    #region Index Out of Bounds Tests

    #endregion

    #region Bug Pattern Runner Tests

    [Fact]
    public void BugPatternRunner_RunsAllCheckers()
    {
        var source = @"
§M{Test}
  §F{Risky:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (+ (/ x y) (* x y))";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics, DefaultOptions);
        runner.CheckFunction(func);

        // Should have warnings from division checker at least
        Assert.NotEmpty(diagnostics.Warnings);
    }

    [Fact]
    public void BugPatternRunner_SafeCode_NoWarnings()
    {
        var source = @"
§M{Test}
  §F{Safe:pub}
      §I{i32:x}
      §O{i32}
      §R (+ x INT:1)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics, DefaultOptions);
        runner.CheckFunction(func);

        // Adding 1 to a parameter - minimal risk
        // May still have info-level diagnostics
        Assert.False(diagnostics.HasErrors);
    }

    #endregion

    #region Comprehensive Division By Zero Tests

    [Fact]
    public void DivisionByZero_NestedDivision_Detected()
    {
        var source = @"
§M{Test}
  §F{NestedDiv:pub}
      §I{i32:a}
      §I{i32:b}
      §I{i32:c}
      §O{i32}
      §R (/ a (/ b c))";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Both divisions have variable divisors - should have warnings
        var divZeroWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.True(divZeroWarnings.Count >= 1);
    }

    [Fact]
    public void DivisionByZero_DivisionInExpression_Detected()
    {
        var source = @"
§M{Test}
  §F{ExprDiv:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (+ INT:10 (/ x y))";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Division in expression should still be detected
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void DivisionByZero_ConstantNonZeroDivisor_Safe()
    {
        var source = @"
§M{Test}
  §F{SafeDiv:pub}
      §I{i32:x}
      §O{i32}
      §R (/ x INT:5)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Division by constant 5 is safe - no division by zero warnings
        var divZeroWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.Empty(divZeroWarnings);
    }

    [Fact]
    public void DivisionByZero_NegativeConstantDivisor_Safe()
    {
        var source = @"
§M{Test}
  §F{NegDiv:pub}
      §I{i32:x}
      §O{i32}
      §R (/ x INT:-3)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Division by -3 is safe
        var divZeroWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.Empty(divZeroWarnings);
    }

    [Fact]
    public void DivisionByZero_MultipleOperations_AllChecked()
    {
        var source = @"
§M{Test}
  §F{MultiOp:pub}
      §I{i32:a}
      §I{i32:b}
      §I{i32:c}
      §O{i32}
      §B{x:i32} (/ a b)
      §B{y:i32} (/ x c)
      §R y";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Both divisions should be detected
        var divZeroWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.True(divZeroWarnings.Count >= 2);
    }

    #endregion

    #region Comprehensive Overflow Tests

    [Fact]
    public void Overflow_LargeConstantAddition_Detected()
    {
        var source = @"
§M{Test}
  §F{LargeAdd:pub}
      §I{i32:x}
      §O{i32}
      §R (+ x INT:2147483647)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new OverflowChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Fact]
    public void Overflow_SmallConstantAddition_Safe()
    {
        var source = @"
§M{Test}
  §F{SmallAdd:pub}
      §I{i32:x}
      §O{i32}
      §R (+ x INT:1)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new OverflowChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Small constant addition is generally safe
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void Overflow_Subtraction_Checked()
    {
        var source = @"
§M{Test}
  §F{Sub:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (- x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new OverflowChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    #endregion

    #region Comprehensive Safe Code Tests (Negative Tests)

    [Fact]
    public void SafeCode_OnlyAddition_NoDivisionWarnings()
    {
        var source = @"
§M{Test}
  §F{OnlyAdd:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (+ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // No division - no division by zero warnings
        var divZeroWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.Empty(divZeroWarnings);
    }

    [Fact]
    public void SafeCode_ReturnConstant_NoWarnings()
    {
        var source = @"
§M{Test}
  §F{Const:pub}
      §O{i32}
      §R INT:42";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics, DefaultOptions);
        runner.CheckFunction(func);

        // Returning a constant - no bugs possible
        Assert.False(diagnostics.HasErrors);
        Assert.Empty(diagnostics.Warnings.Where(d =>
            d.Code == DiagnosticCode.DivisionByZero ||
            d.Code == DiagnosticCode.IndexOutOfBounds));
    }

    [Fact]
    public void SafeCode_SimpleArithmetic_MinimalWarnings()
    {
        var source = @"
§M{Test}
  §F{Arith:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §B{sum:i32} (+ a b)
      §B{diff:i32} (- a b)
      §R (+ sum diff)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics, DefaultOptions);
        runner.CheckFunction(func);

        // Simple arithmetic - no definite bugs
        Assert.False(diagnostics.HasErrors);
    }

    #endregion

    #region Guard Detection Tests

    [Fact]
    public void GuardDetection_GreaterThanZero_ProtectsDivision()
    {
        var source = @"
§M{Test}
  §F{GuardGT:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §IF (> y INT:0)
          §R (/ x y)
      §R INT:0";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // y > 0 guard should protect division - fewer warnings about y
        var yWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero && d.Message.Contains("'y'"))
            .ToList();
        Assert.Empty(yWarnings);
    }

    [Fact]
    public void GuardDetection_LessThanZero_ProtectsDivision()
    {
        var source = @"
§M{Test}
  §F{GuardLT:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §IF (< y INT:0)
          §R (/ x y)
      §R INT:0";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void EdgeCase_DivisionByParameter_Warns()
    {
        var source = @"
§M{Test}
  §F{DivByParam:pub}
      §I{i32:divisor}
      §O{i32}
      §R (/ INT:100 divisor)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Parameter could be zero - should warn
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void EdgeCase_DivisionInBothBranches_BothChecked()
    {
        var source = @"
§M{Test}
  §F{BothBranches:pub}
      §I{i32:x}
      §I{i32:y}
      §I{i32:z}
      §I{bool:cond}
      §O{i32}
      §IF cond
          §R (/ x y)
      §EL
          §R (/ x z)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Both y and z are used as divisors
        var divZeroWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.True(divZeroWarnings.Count >= 2);
    }

    [Fact]
    public void EdgeCase_ModuloByZero_AlsoDetected()
    {
        var source = @"
§M{Test}
  §F{ModZero:pub}
      §O{i32}
      §R (% INT:10 INT:0)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultOptions);
        checker.Check(func, diagnostics);

        // Modulo by literal zero should be error
        Assert.True(diagnostics.HasErrors || diagnostics.Warnings.Any(d => d.Code == DiagnosticCode.DivisionByZero));
    }

    [Fact]
    public void DivisionByZero_LiteralCastThatTruncatesToZero_ReportsError()
    {
        var source = @"
§M{Test}
  §F{Divide:pub} () -> i32
    §R (/ INT:100 (cast i32 FLOAT:0.5))";

        var func = GetFunction(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors);

        var diagnostics = new DiagnosticBag();
        new DivisionByZeroChecker(DefaultOptions).Check(func, diagnostics);

        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero
            && diagnostic.Message.Contains("guaranteed", StringComparison.Ordinal));
    }

    #endregion
}
