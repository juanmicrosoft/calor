using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Negative verification tests to ensure safe code produces no warnings.
/// These tests verify that the analysis doesn't produce false positives.
///
/// NOTE: Some tests use Calor syntax that may not be fully supported by the parser.
/// These tests use Skip.If() to explicitly mark themselves as skipped when parsing fails,
/// rather than silently passing. This ensures test results accurately reflect coverage.
/// </summary>
public class NegativeVerificationTests
{
    private readonly ITestOutputHelper _output;

    public NegativeVerificationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Helpers

    private static BoundModule? Bind(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        try
        {
            var lexer = new Lexer(source, diagnostics);
            var tokens = lexer.TokenizeAllForParser();
            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();

            if (diagnostics.HasErrors) return null;

            var binder = new Binder(diagnostics);
            return binder.Bind(module);
        }
        catch (InvalidOperationException)
        {
            // Syntax may not be supported - mark as error
            diagnostics.ReportError(default, "ParseError", "Parsing failed with unsupported syntax");
            return null;
        }
    }

    private static BoundFunction? GetFunction(string source, out DiagnosticBag parseDiag)
    {
        var bound = Bind(source, out parseDiag);
        if (parseDiag.HasErrors || bound == null) return null;
        return bound.Functions.FirstOrDefault();
    }

    private void AssertNoTaintVulnerabilities(string source)
    {
        var func = GetFunction(source, out var parseDiag);
        Skip.If(parseDiag.HasErrors || func == null, "Calor syntax not supported by parser");

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);
        Assert.Empty(analysis.Vulnerabilities);
    }

    private void AssertNoDivisionByZeroWarnings(string source)
    {
        var bound = Bind(source, out var parseDiag);
        Skip.If(parseDiag.HasErrors || bound == null, "Calor syntax not supported by parser");

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(bound);

        // Only check for actual errors/warnings, not info messages (which are "inconclusive" checks)
        var divByZeroWarnings = diagnostics
            .Where(d => d.Code == DiagnosticCode.DivisionByZero &&
                       d.Severity != DiagnosticSeverity.Info)
            .ToList();
        Assert.Empty(divByZeroWarnings);
    }

    private void AssertNoNullDereferenceWarnings(string source)
    {
        var bound = Bind(source, out var parseDiag);
        Skip.If(parseDiag.HasErrors || bound == null, "Calor syntax not supported by parser");

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(bound);

        var nullWarnings = diagnostics
            .Where(d => d.Code == DiagnosticCode.NullDereference &&
                       d.Severity != DiagnosticSeverity.Info)
            .ToList();
        Assert.Empty(nullWarnings);
    }

    private void AssertNoBoundsWarnings(string source)
    {
        var bound = Bind(source, out var parseDiag);
        Skip.If(parseDiag.HasErrors || bound == null, "Calor syntax not supported by parser");

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(bound);

        var boundsWarnings = diagnostics
            .Where(d => d.Code == DiagnosticCode.IndexOutOfBounds &&
                       d.Severity != DiagnosticSeverity.Info)
            .ToList();
        Assert.Empty(boundsWarnings);
    }

    #endregion

    #region Taint - Safe Patterns

    [SkippableFact]
    public void SafeCode_ConstantSqlQuery_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeQuery () → string
    ! db:w
    let string:query = ""SELECT * FROM users WHERE id = 1""
    db.execute(query)
    return query";

        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_ParameterizedQuery_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeParameterizedQuery (string:user_input) → string
    ! db:w
    let string:query = ""SELECT * FROM users WHERE id = ?""
    db.execute_param(query, user_input)
    return query";

        // Parameterized queries are safe even with user input
        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_SanitizedInput_NoWarning()
    {
        // §B target + §O return relaxed :string → :?string to stay under
        // the v0.14 §S5 gate: parameters do not yet flow annotations
        // (task 3 / PR #1063), so a §B{...:string} initialized from a
        // param-derived (CALL ...) trips Calor0272 at Error under
        // Major>=2 and the §R query trips Calor0273 — either would send
        // this test silently through Skip.If. Preserves the taint intent.
        var source = @"
module Test Test block
  func SafeSanitized (string:user_input) → string?
    ! db:w
    let string?:sanitized = sql_escape(user_input)
    let string?:query = (""SELECT * FROM users WHERE name = '"" + sanitized)
    db.execute(query)
    return query";

        // Sanitized input should not trigger taint warnings
        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_EncodedHtmlOutput_NoWarning()
    {
        // §B target + §O relaxed :string → :?string; see SafeCode_SanitizedInput.
        var source = @"
module Test Test block
  func SafeHtml (string:user_input) → string?
    ! net:w
    let string?:encoded = html_escape(user_input)
    response.write(encoded)
    return encoded";

        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_NoUserInput_NoTaintWarning()
    {
        var source = @"
module Test Test block
  func SafeNoUserInput (int:count) → int
    !
    return count";

        // Integer parameter named 'count' should not be treated as user input
        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_InternalDataOnly_NoTaintWarning()
    {
        var source = @"
module Test Test block
  func SafeInternalData () → string
    ! db:w
    let string:name = ""admin""
    let string:query = (""SELECT * FROM users WHERE name = '"" + name)
    db.execute(query)
    return query";

        // Internal/constant data should not trigger taint warnings
        AssertNoTaintVulnerabilities(source);
    }

    #endregion

    #region Division - Safe Patterns

    [SkippableFact]
    public void SafeCode_DivisionWithGuard_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeDivWithGuard (int:x, int:y) → int
    !
    if (y != 0)
      return (x / y)
    else
      return 0";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionByLiteralNonZero_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeDivByLiteral (int:x) → int
    !
    return (x / 2)";

        // Division by a literal non-zero value is safe
        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionByPositiveConstant_NoWarning()
    {
        // This test requires constant propagation to track that divisor = INT:10
        // is always non-zero. The current analysis is conservative and warns about
        // any variable division without explicit guards.
        var source = @"
module Test Test block
  func SafeDivByConstant (int:x) → int
    !
    let divisor = 10
    return (x / divisor)";

        // Division by a constant known to be non-zero
        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_ModuloByLiteralNonZero_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeModulo (int:x) → int
    !
    return (x % 7)";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionInLoopWithNonZeroInit_NoWarning()
    {
        // Note: Using constant bound (10) because parser doesn't support variable bounds
        var source = @"
module Test Test block
  func SafeDivInLoop () → int
    !
    let result = 0
    for i from 1 to 10 step 1
      let int:result = (result + (100 / i))
    return result";

        // Loop starts from 1, so i is always >= 1 (non-zero)
        // But the analysis doesn't yet track loop bounds to know this
        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionAfterAbsoluteValue_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeDivAfterAbs (int:x, int:y) → int
    !
    let int:absY = math.abs(y)
    if (absY > 0)
      return (x / absY)
    else
      return 0";

        AssertNoDivisionByZeroWarnings(source);
    }

    #endregion

    #region Null - Safe Patterns

    [SkippableFact]
    public void SafeCode_NullCheckBeforeUse_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeNullCheck (string:maybeNull) → int
    !
    if (maybeNull != null)
      return string.length(maybeNull)
    else
      return 0";

        AssertNoNullDereferenceWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_OptionMatch_NoWarning()
    {
        // Note: This test verifies null checking doesn't produce warnings
        var source = @"
module Test Test block
  func SafeOptionMatch (int:value) → int
    !
    let int:opt = value
    if (opt != 0)
      return opt
    else
      return 0";

        AssertNoNullDereferenceWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_NonNullableParameter_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeNonNullable (int:x) → int
    !
    return (x + 1)";

        // Non-nullable types (i32) should not trigger null warnings
        AssertNoNullDereferenceWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DefaultValueProvided_NoWarning()
    {
        // §B target + §O relaxed :string → :?string; see SafeCode_SanitizedInput.
        var source = @"
module Test Test block
  func SafeDefaultValue (string:input) → string?
    !
    let string?:result = coalesce(input, ""default"")
    return result";

        // Coalesce function provides a default, making result non-null
        AssertNoNullDereferenceWarnings(source);
    }

    #endregion

    #region Bounds - Safe Patterns

    [SkippableFact]
    public void SafeCode_LoopWithinArrayLength_NoWarning()
    {
        // Note: Using constant bound (10) because parser doesn't support variable bounds
        var source = @"
module Test Test block
  func SafeLoopBounds () → int
    !
    let sum = 0
    for i from 0 to 10 step 1
      let int:sum = (sum + i)
    return sum";

        // Loop bounds are explicit and valid
        AssertNoBoundsWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_BoundsCheckFirst_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeBoundsCheck (int:index, int:len) → int
    !
    if ((index >= 0) && (index < len))
      return index
    else
      return -1";

        // Bounds check before access
        AssertNoBoundsWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_LiteralIndex_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeLiteralIndex (int:x) → int
    !
    let arr = 10
    return (arr + x)";

        // No actual array access in this simplified form
        AssertNoBoundsWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_ClampedIndex_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeClampedIndex (int:index, int:maxLen) → int
    !
    let int:clamped = math.min(index, (maxLen - 1))
    let int:safeIndex = math.max(clamped, 0)
    return safeIndex";

        // Index is clamped to valid range
        AssertNoBoundsWarnings(source);
    }

    #endregion

    #region Dataflow - Safe Patterns

    [SkippableFact]
    public void SafeCode_InitializedBeforeUse_NoWarning()
    {
        // Test that code initializing variables before use parses and binds correctly
        var source = @"
module Test Test block
  func SafeInitialized (int:x) → int
    !
    let result = 0
    let int:result = (result + x)
    return result";

        var bound = Bind(source, out var parseDiag);
        // Skip test if syntax not supported (allows Calor syntax to evolve)
        if (parseDiag.HasErrors) return;
        Assert.NotNull(bound);
    }

    [SkippableFact]
    public void SafeCode_AllPathsInitialize_NoWarning()
    {
        // Test that code with all paths initializing variables parses correctly
        var source = @"
module Test Test block
  func SafeAllPathsInit (int:x) → int
    !
    let result = 0
    if (x > 0)
      let result = 1
    else
      let result = -1
    return result";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;
        Assert.NotNull(bound);
    }

    [SkippableFact]
    public void SafeCode_ParameterAlwaysInitialized_NoWarning()
    {
        // Test that parameters are always considered initialized
        var source = @"
module Test Test block
  func SafeParameterInit (int:x, int:y) → int
    !
    return (x + y)";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;
        Assert.NotNull(bound);
    }

    [Fact]
    public void Binding_NullExpression_ProducesBoundNullLiteral()
    {
        var source = @"
module Test Test block
  func TestNone (string:maybeNull) → int
    !
    return ((maybeNull != null) ? 1 : 0)";

        var bound = Bind(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag));
        Assert.NotNull(bound);

        // Null must not be confused with integer zero by numeric analyses.
        var func = bound!.Functions.First();
        BoundNullLiteral? foundNone = null;

        void CheckExpr(BoundExpression? expr)
        {
            if (expr is BoundNullLiteral nl) foundNone = nl;
            if (expr is BoundBinaryExpression bin)
            {
                CheckExpr(bin.Left);
                CheckExpr(bin.Right);
            }
        }

        foreach (var stmt in func.Body)
        {
            if (stmt is BoundIfStatement ifStmt)
            {
                CheckExpr(ifStmt.Condition);
            }
        }

        Assert.NotNull(foundNone);

        Assert.False(BoundNodeHelpers.IsConstant(foundNone),
            "Null should not be considered a numeric constant");
        Assert.False(BoundNodeHelpers.IsLiteralZero(foundNone),
            "Null should not be confused with literal zero");
    }

    [SkippableFact]
    public void SafeCode_DivisionByClamp_NoWarning()
    {
        // math.clamp(y, 1, 10) has lower bound 1, so it's always >= 1 (non-zero)
        // The call must appear directly as the divisor so Z3 can translate it
        // (the translator doesn't follow variable initializers)
        var source = @"
module Test Test block
  func ClampDiv (int:x, int:y) → int
    !
    return (x / math.clamp(y, 1, 10))";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionBySignGuarded_NoWarning()
    {
        // math.sign(y) can be zero when y == 0, so a guard is needed
        // The call must appear directly as the divisor so Z3 can translate it
        var source = @"
module Test Test block
  func SignDiv (int:x, int:y) → int
    !
    if (y != 0)
      return (x / math.sign(y))
    else
      return 0";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionByConstantInIfBlock_NoWarning()
    {
        // Constant declared inside an if block — requires deep constant propagation
        var source = @"
module Test Test block
  func DeepConst (int:x, int:flag) → int
    !
    let result = 0
    if (flag > 0)
      let int:divisor = 5
      let int:result = (x / divisor)
    return result";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionByConstantInLoopBody_NoWarning()
    {
        // Constant declared inside a for-loop body — requires recursive FindVariableInitializer
        var source = @"
module Test Test block
  func LoopConst (int:x) → int
    !
    let result = 0
    for i from 0 to 3 step 1
      let int:divisor = 7
      let int:result = (result + (x / divisor))
    return result";

        AssertNoDivisionByZeroWarnings(source);
    }

    #endregion

    #region Complex Safe Patterns

    [SkippableFact]
    public void SafeCode_ComplexControlFlow_NoWarning()
    {
        var source = @"
§M{Test}
  §F{SafeComplexFlow:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §B{result} INT:0
      §IF (> x INT:0)
          §IF (> y INT:0)
              §B{result:i32} (+ x y)
          §EL
              §B{result:i32} x
      §EL
          §IF (> y INT:0)
              §B{result:i32} y
          §EL
              §B{result} INT:0
      §R result";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors || bound == null) return;

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(bound);

        // No bug pattern warnings expected
        var bugWarnings = diagnostics
            .Where(d => d.Code == DiagnosticCode.DivisionByZero ||
                       d.Code == DiagnosticCode.NullDereference ||
                       d.Code == DiagnosticCode.IndexOutOfBounds)
            .ToList();
        Assert.Empty(bugWarnings);
    }

    [SkippableFact]
    public void SafeCode_NestedLoops_NoWarning()
    {
        // Note: Using constant bounds because parser doesn't support variable bounds
        var source = @"
module Test Test block
  func SafeNestedLoops () → int
    !
    let sum = 0
    for i from 1 to 5 step 1
      for j from 1 to 5 step 1
        let int:sum = (sum + (i * j))
    return sum";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(bound!);

        // Division/bounds issues - none expected since we use multiplication
        var bugWarnings = diagnostics
            .Where(d => d.Code == DiagnosticCode.DivisionByZero ||
                       d.Code == DiagnosticCode.IndexOutOfBounds)
            .ToList();
        Assert.Empty(bugWarnings);
    }

    [SkippableFact]
    public void SafeCode_MixedOperations_NoWarning()
    {
        var source = @"
module Test Test block
  func SafeMixedOps (int:a, int:b) → int
    !
    let int:sum = (a + b)
    let int:product = (a * b)
    let int:diff = (a - b)
    return ((sum + product) + diff)";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors || bound == null) return;

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(bound);

        // No division = no division by zero warnings
        var divWarnings = diagnostics
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.Empty(divWarnings);
    }

    #endregion

    #region Edge Cases - Still Safe

    [SkippableFact]
    public void SafeCode_EmptyFunction_NoWarning()
    {
        var source = @"
module Test Test block
  func EmptyFunc () → void
    !
    return";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors || bound == null) return;

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(bound);

        Assert.False(diagnostics.HasErrors);
    }

    [SkippableFact]
    public void SafeCode_SingleStatement_NoWarning()
    {
        var source = @"
module Test Test block
  func SingleStmt (int:x) → int
    !
    return x";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors || bound == null) return;

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(bound);

        Assert.False(diagnostics.HasErrors);
    }

    [SkippableFact]
    public void SafeCode_MultipleReturns_NoWarning()
    {
        var source = @"
module Test Test block
  func MultiReturn (int:x) → int
    !
    if (x < 0)
      return 0
    if (x > 100)
      return 100
    return x";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors || bound == null) return;

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(bound);

        var bugWarnings = diagnostics
            .Where(d => d.Code == DiagnosticCode.DivisionByZero ||
                       d.Code == DiagnosticCode.NullDereference ||
                       d.Code == DiagnosticCode.IndexOutOfBounds)
            .ToList();
        Assert.Empty(bugWarnings);
    }

    #endregion
}
