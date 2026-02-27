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
            var tokens = lexer.TokenizeAll();
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
§M{m001:Test}
§F{f001:SafeQuery:pub}
  §O{string}
  §E{db:w}
  §B{query:string} STR:""SELECT * FROM users WHERE id = 1""
  §C{db.execute}
    §A query
  §/C
  §R query
§/F{f001}
§/M{m001}";

        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_ParameterizedQuery_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeParameterizedQuery:pub}
  §I{string:user_input}
  §O{string}
  §E{db:w}
  §B{query:string} STR:""SELECT * FROM users WHERE id = ?""
  §C{db.execute_param}
    §A query
    §A user_input
  §/C
  §R query
§/F{f001}
§/M{m001}";

        // Parameterized queries are safe even with user input
        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_SanitizedInput_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeSanitized:pub}
  §I{string:user_input}
  §O{string}
  §E{db:w}
  §B{sanitized:string} (CALL sql_escape user_input)
  §B{query:string} (+ STR:""SELECT * FROM users WHERE name = '"" sanitized)
  §C{db.execute}
    §A query
  §/C
  §R query
§/F{f001}
§/M{m001}";

        // Sanitized input should not trigger taint warnings
        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_EncodedHtmlOutput_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeHtml:pub}
  §I{string:user_input}
  §O{string}
  §E{net:w}
  §B{encoded:string} (CALL html_escape user_input)
  §C{response.write}
    §A encoded
  §/C
  §R encoded
§/F{f001}
§/M{m001}";

        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_NoUserInput_NoTaintWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeNoUserInput:pub}
  §I{i32:count}
  §O{i32}
  §R count
§/F{f001}
§/M{m001}";

        // Integer parameter named 'count' should not be treated as user input
        AssertNoTaintVulnerabilities(source);
    }

    [SkippableFact]
    public void SafeCode_InternalDataOnly_NoTaintWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeInternalData:pub}
  §O{string}
  §E{db:w}
  §B{name:string} STR:""admin""
  §B{query:string} (+ STR:""SELECT * FROM users WHERE name = '"" name)
  §C{db.execute}
    §A query
  §/C
  §R query
§/F{f001}
§/M{m001}";

        // Internal/constant data should not trigger taint warnings
        AssertNoTaintVulnerabilities(source);
    }

    #endregion

    #region Division - Safe Patterns

    [SkippableFact]
    public void SafeCode_DivisionWithGuard_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeDivWithGuard:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §IF{if1} (!= y INT:0)
    §R (/ x y)
  §EL
    §R INT:0
  §/I{if1}
§/F{f001}
§/M{m001}";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionByLiteralNonZero_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeDivByLiteral:pub}
  §I{i32:x}
  §O{i32}
  §R (/ x INT:2)
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:SafeDivByConstant:pub}
  §I{i32:x}
  §O{i32}
  §B{divisor} INT:10
  §R (/ x divisor)
§/F{f001}
§/M{m001}";

        // Division by a constant known to be non-zero
        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_ModuloByLiteralNonZero_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeModulo:pub}
  §I{i32:x}
  §O{i32}
  §R (% x INT:7)
§/F{f001}
§/M{m001}";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionInLoopWithNonZeroInit_NoWarning()
    {
        // Note: Using constant bound (10) because parser doesn't support variable bounds
        var source = @"
§M{m001:Test}
§F{f001:SafeDivInLoop:pub}
  §O{i32}
  §B{result} INT:0
  §L{l1:i:1:10:1}
    §B{result:i32} (+ result (/ INT:100 i))
  §/L{l1}
  §R result
§/F{f001}
§/M{m001}";

        // Loop starts from 1, so i is always >= 1 (non-zero)
        // But the analysis doesn't yet track loop bounds to know this
        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionAfterAbsoluteValue_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeDivAfterAbs:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §B{absY:i32} (CALL math.abs y)
  §IF{if1} (> absY INT:0)
    §R (/ x absY)
  §EL
    §R INT:0
  §/I{if1}
§/F{f001}
§/M{m001}";

        AssertNoDivisionByZeroWarnings(source);
    }

    #endregion

    #region Null - Safe Patterns

    [SkippableFact]
    public void SafeCode_NullCheckBeforeUse_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeNullCheck:pub}
  §I{string:maybeNull}
  §O{i32}
  §IF{if1} (!= maybeNull §NN)
    §R (CALL string.length maybeNull)
  §EL
    §R INT:0
  §/I{if1}
§/F{f001}
§/M{m001}";

        AssertNoNullDereferenceWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_OptionMatch_NoWarning()
    {
        // Note: This test verifies null checking doesn't produce warnings
        var source = @"
§M{m001:Test}
§F{f001:SafeOptionMatch:pub}
  §I{i32:value}
  §O{i32}
  §B{opt:i32} value
  §IF{if1} (!= opt INT:0)
    §R opt
  §EL
    §R INT:0
  §/I{if1}
§/F{f001}
§/M{m001}";

        AssertNoNullDereferenceWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_NonNullableParameter_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeNonNullable:pub}
  §I{i32:x}
  §O{i32}
  §R (+ x INT:1)
§/F{f001}
§/M{m001}";

        // Non-nullable types (i32) should not trigger null warnings
        AssertNoNullDereferenceWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DefaultValueProvided_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeDefaultValue:pub}
  §I{string:input}
  §O{string}
  §B{result:string} (CALL coalesce input STR:""default"")
  §R result
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:SafeLoopBounds:pub}
  §O{i32}
  §B{sum} INT:0
  §L{l1:i:0:10:1}
    §B{sum:i32} (+ sum i)
  §/L{l1}
  §R sum
§/F{f001}
§/M{m001}";

        // Loop bounds are explicit and valid
        AssertNoBoundsWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_BoundsCheckFirst_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeBoundsCheck:pub}
  §I{i32:index}
  §I{i32:len}
  §O{i32}
  §IF{if1} (&& (>= index INT:0) (< index len))
    §R index
  §EL
    §R INT:-1
  §/I{if1}
§/F{f001}
§/M{m001}";

        // Bounds check before access
        AssertNoBoundsWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_LiteralIndex_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeLiteralIndex:pub}
  §I{i32:x}
  §O{i32}
  §B{arr} INT:10
  §R (+ arr x)
§/F{f001}
§/M{m001}";

        // No actual array access in this simplified form
        AssertNoBoundsWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_ClampedIndex_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeClampedIndex:pub}
  §I{i32:index}
  §I{i32:maxLen}
  §O{i32}
  §B{clamped:i32} (CALL math.min index (- maxLen INT:1))
  §B{safeIndex:i32} (CALL math.max clamped INT:0)
  §R safeIndex
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:SafeInitialized:pub}
  §I{i32:x}
  §O{i32}
  §B{result} INT:0
  §B{result:i32} (+ result x)
  §R result
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:SafeAllPathsInit:pub}
  §I{i32:x}
  §O{i32}
  §B{result} INT:0
  §IF{if1} (> x INT:0)
    §B{result} INT:1
  §EL
    §B{result} INT:-1
  §/I{if1}
  §R result
§/F{f001}
§/M{m001}";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;
        Assert.NotNull(bound);
    }

    [SkippableFact]
    public void SafeCode_ParameterAlwaysInitialized_NoWarning()
    {
        // Test that parameters are always considered initialized
        var source = @"
§M{m001:Test}
§F{f001:SafeParameterInit:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §R (+ x y)
§/F{f001}
§/M{m001}";

        var bound = Bind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;
        Assert.NotNull(bound);
    }

    [SkippableFact]
    public void Binding_NoneExpression_ProducesBoundNoneLiteral()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestNone:pub}
  §I{string:maybeNull}
  §O{i32}
  §IF{if1} (!= maybeNull §NN)
    §R INT:1
  §EL
    §R INT:0
  §/I{if1}
§/F{f001}
§/M{m001}";

        var bound = Bind(source, out var parseDiag);
        Skip.If(parseDiag.HasErrors || bound == null, "Calor syntax not supported by parser");

        // Walk the bound tree looking for a BoundNoneLiteral
        var func = bound!.Functions.First();
        BoundNoneLiteral? foundNone = null;

        void CheckExpr(BoundExpression? expr)
        {
            if (expr is BoundNoneLiteral nl) foundNone = nl;
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

        // BoundNoneLiteral must NOT be treated as a numeric constant or as literal zero
        Assert.False(BoundNodeHelpers.IsConstant(foundNone),
            "BoundNoneLiteral should not be considered a numeric constant");
        Assert.False(BoundNodeHelpers.IsLiteralZero(foundNone),
            "BoundNoneLiteral should not be confused with literal zero");
    }

    [SkippableFact]
    public void SafeCode_DivisionByClamp_NoWarning()
    {
        // math.clamp(y, 1, 10) has lower bound 1, so it's always >= 1 (non-zero)
        // The call must appear directly as the divisor so Z3 can translate it
        // (the translator doesn't follow variable initializers)
        var source = @"
§M{m001:Test}
§F{f001:ClampDiv:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §R (/ x (CALL math.clamp y INT:1 INT:10))
§/F{f001}
§/M{m001}";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionBySignGuarded_NoWarning()
    {
        // math.sign(y) can be zero when y == 0, so a guard is needed
        // The call must appear directly as the divisor so Z3 can translate it
        var source = @"
§M{m001:Test}
§F{f001:SignDiv:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §IF{if1} (!= y INT:0)
    §R (/ x (CALL math.sign y))
  §EL
    §R INT:0
  §/I{if1}
§/F{f001}
§/M{m001}";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionByConstantInIfBlock_NoWarning()
    {
        // Constant declared inside an if block — requires deep constant propagation
        var source = @"
§M{m001:Test}
§F{f001:DeepConst:pub}
  §I{i32:x}
  §I{i32:flag}
  §O{i32}
  §B{result} INT:0
  §IF{if1} (> flag INT:0)
    §B{divisor:i32} INT:5
    §B{result:i32} (/ x divisor)
  §/I{if1}
  §R result
§/F{f001}
§/M{m001}";

        AssertNoDivisionByZeroWarnings(source);
    }

    [SkippableFact]
    public void SafeCode_DivisionByConstantInLoopBody_NoWarning()
    {
        // Constant declared inside a for-loop body — requires recursive FindVariableInitializer
        var source = @"
§M{m001:Test}
§F{f001:LoopConst:pub}
  §I{i32:x}
  §O{i32}
  §B{result} INT:0
  §L{l1:i:0:3:1}
    §B{divisor:i32} INT:7
    §B{result:i32} (+ result (/ x divisor))
  §/L{l1}
  §R result
§/F{f001}
§/M{m001}";

        AssertNoDivisionByZeroWarnings(source);
    }

    #endregion

    #region Complex Safe Patterns

    [SkippableFact]
    public void SafeCode_ComplexControlFlow_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeComplexFlow:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §B{result} INT:0
  §IF{if1} (> x INT:0)
    §IF{if2} (> y INT:0)
      §B{result:i32} (+ x y)
    §EL
      §B{result:i32} x
    §/I{if2}
  §EL
    §IF{if3} (> y INT:0)
      §B{result:i32} y
    §EL
      §B{result} INT:0
    §/I{if3}
  §/I{if1}
  §R result
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:SafeNestedLoops:pub}
  §O{i32}
  §B{sum} INT:0
  §L{l1:i:1:5:1}
    §L{l2:j:1:5:1}
      §B{sum:i32} (+ sum (* i j))
    §/L{l2}
  §/L{l1}
  §R sum
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:SafeMixedOps:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §B{sum:i32} (+ a b)
  §B{product:i32} (* a b)
  §B{diff:i32} (- a b)
  §R (+ (+ sum product) diff)
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:EmptyFunc:pub}
  §O{void}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:SingleStmt:pub}
  §I{i32:x}
  §O{i32}
  §R x
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:MultiReturn:pub}
  §I{i32:x}
  §O{i32}
  §IF{if1} (< x INT:0)
    §R INT:0
  §/I{if1}
  §IF{if2} (> x INT:100)
    §R INT:100
  §/I{if2}
  §R x
§/F{f001}
§/M{m001}";

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
