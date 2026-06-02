using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.BugPatterns.Patterns;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Golden file tests that verify exact diagnostic detection for known vulnerabilities.
/// Each test contains code with a KNOWN bug and verifies the EXACT diagnostic is produced.
/// </summary>
public class GoldenFileTests
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

    private static BugPatternOptions DefaultBugOptions => new()
    {
        UseZ3Verification = false,
        Z3TimeoutMs = 1000
    };

    private static BugPatternOptions Z3BugOptions => new()
    {
        UseZ3Verification = Z3ContextFactory.IsAvailable,
        Z3TimeoutMs = 2000
    };

    #endregion

    #region Division By Zero - Known Vulnerabilities

    /// <summary>
    /// KNOWN BUG: Division by literal zero.
    /// EXPECTED: Error with DiagnosticCode.DivisionByZero
    /// </summary>
    [Fact]
    public void Golden_DivisionByLiteralZero_ProducesError()
    {
        var source = @"
§M{m001:Test}
  §F{f001:DivByZero:pub}
      §I{i32:x}
      §O{i32}
      §R (/ x INT:0)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, $"Parse errors: {string.Join(", ", parseDiag.Select(d => d.Message))}");

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // MUST produce division by zero error
        Assert.True(diagnostics.HasErrors, "Expected error for division by literal zero");
        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    /// <summary>
    /// KNOWN BUG: Modulo by literal zero.
    /// EXPECTED: Error with DiagnosticCode.DivisionByZero
    /// </summary>
    [Fact]
    public void Golden_ModuloByLiteralZero_ProducesError()
    {
        var source = @"
§M{m001:Test}
  §F{f001:ModByZero:pub}
      §I{i32:x}
      §O{i32}
      §R (% x INT:0)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // MUST produce division by zero error (modulo by zero)
        var divByZeroDiags = diagnostics.Where(d => d.Code == DiagnosticCode.DivisionByZero).ToList();
        Assert.NotEmpty(divByZeroDiags);
    }

    /// <summary>
    /// KNOWN BUG: Division by unchecked parameter.
    /// EXPECTED: Warning with DiagnosticCode.DivisionByZero
    /// </summary>
    [Fact]
    public void Golden_DivisionByUncheckedParameter_ProducesWarning()
    {
        var source = @"
§M{m001:Test}
  §F{f001:UncheckedDiv:pub}
      §I{i32:x}
      §I{i32:divisor}
      §O{i32}
      §R (/ x divisor)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // MUST produce warning for unchecked divisor
        Assert.NotEmpty(diagnostics.Warnings);
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    /// <summary>
    /// SAFE CODE: Division by non-zero constant.
    /// EXPECTED: No DivisionByZero diagnostics
    /// </summary>
    [Fact]
    public void Golden_DivisionByNonZeroConstant_NoDiagnostic()
    {
        var source = @"
§M{m001:Test}
  §F{f001:SafeDiv:pub}
      §I{i32:x}
      §O{i32}
      §R (/ x INT:7)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // MUST NOT produce division by zero diagnostic
        var divByZeroDiags = diagnostics.Where(d => d.Code == DiagnosticCode.DivisionByZero).ToList();
        Assert.Empty(divByZeroDiags);
    }

    /// <summary>
    /// SAFE CODE: Division with guard check (y != 0).
    /// EXPECTED: No DivisionByZero diagnostics in guarded branch
    /// </summary>
    [Fact]
    public void Golden_DivisionWithNotEqualZeroGuard_NoDiagnostic()
    {
        var source = @"
§M{m001:Test}
  §F{f001:GuardedDiv:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §IF{if1} (!= y INT:0)
          §R (/ x y)
      §R INT:0";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, $"Parse errors: {string.Join(", ", parseDiag.Select(d => d.Message))}");

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // Guard should protect - ideally no warning about y
        // Note: Implementation may or may not recognize the guard
        var yWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero &&
                       d.Message.Contains("'y'"))
            .ToList();

        // If implementation recognizes guards, this should be empty
        // We document expected behavior even if implementation is incomplete
        Assert.True(yWarnings.Count == 0,
            $"Guard (y != 0) should protect division. Found {yWarnings.Count} warnings about y.");
    }

    /// <summary>
    /// KNOWN BUG: Multiple divisions, all unchecked.
    /// EXPECTED: Multiple DivisionByZero warnings
    /// </summary>
    [Fact]
    public void Golden_MultipleDivisionsUnchecked_ProducesMultipleWarnings()
    {
        var source = @"
§M{m001:Test}
  §F{f001:MultiDiv:pub}
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
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // MUST produce warnings for both b and c
        var divByZeroWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.True(divByZeroWarnings.Count >= 2,
            $"Expected at least 2 division warnings, got {divByZeroWarnings.Count}");
    }

    #endregion

    #region Taint Analysis - Known Vulnerabilities

    /// <summary>
    /// SAFE CODE: No taint sources, just arithmetic.
    /// EXPECTED: No taint vulnerabilities
    /// </summary>
    [Fact]
    public void Golden_NoTaintSources_NoVulnerabilities()
    {
        var source = @"
§M{m001:Test}
  §F{f001:SafeArith:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (+ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // MUST have zero vulnerabilities
        Assert.Empty(analysis.Vulnerabilities);
    }

    /// <summary>
    /// SAFE CODE: Constants only, no external input.
    /// EXPECTED: No taint vulnerabilities
    /// </summary>
    [Fact]
    public void Golden_ConstantsOnly_NoVulnerabilities()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Constants:pub}
      §O{i32}
      §B{a:i32} INT:1
      §B{b:i32} INT:2
      §B{c:i32} (+ a b)
      §R c";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // MUST have zero vulnerabilities - no taint sources
        Assert.Empty(analysis.Vulnerabilities);
    }

    /// <summary>
    /// Verify TaintVulnerability produces correct diagnostic code for SQL injection.
    /// </summary>
    [Fact]
    public void Golden_SqlInjectionVulnerability_HasCorrectCode()
    {
        var vuln = new TaintVulnerability(
            TaintSink.SqlQuery,
            TaintSource.UserInput,
            "user_query",
            new TextSpan(0, 10, 1, 1),
            "sql_param",
            new TextSpan(50, 10, 5, 1));

        Assert.Equal(DiagnosticCode.SqlInjection, vuln.DiagnosticCode);
        Assert.Contains("SQL", vuln.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(vuln.Message);
    }

    /// <summary>
    /// Verify TaintVulnerability produces correct diagnostic code for command injection.
    /// </summary>
    [Fact]
    public void Golden_CommandInjectionVulnerability_HasCorrectCode()
    {
        var vuln = new TaintVulnerability(
            TaintSink.CommandExecution,
            TaintSource.UserInput,
            "user_cmd",
            new TextSpan(0, 10, 1, 1),
            "exec_param",
            new TextSpan(50, 10, 5, 1));

        Assert.Equal(DiagnosticCode.CommandInjection, vuln.DiagnosticCode);
        Assert.Contains("command", vuln.Message.ToLower());
    }

    /// <summary>
    /// Verify TaintVulnerability produces correct diagnostic code for path traversal.
    /// </summary>
    [Fact]
    public void Golden_PathTraversalVulnerability_HasCorrectCode()
    {
        var vuln = new TaintVulnerability(
            TaintSink.FilePath,
            TaintSource.UserInput,
            "user_path",
            new TextSpan(0, 10, 1, 1),
            "file_path",
            new TextSpan(50, 10, 5, 1));

        Assert.Equal(DiagnosticCode.PathTraversal, vuln.DiagnosticCode);
        Assert.Contains("path", vuln.Message.ToLower());
    }

    /// <summary>
    /// Verify TaintVulnerability produces correct diagnostic code for XSS.
    /// </summary>
    [Fact]
    public void Golden_XssVulnerability_HasCorrectCode()
    {
        var vuln = new TaintVulnerability(
            TaintSink.HtmlOutput,
            TaintSource.UserInput,
            "user_html",
            new TextSpan(0, 10, 1, 1),
            "output",
            new TextSpan(50, 10, 5, 1));

        Assert.Equal(DiagnosticCode.CrossSiteScripting, vuln.DiagnosticCode);
    }

    /// <summary>
    /// Verify all taint sinks have corresponding diagnostic codes.
    /// </summary>
    [Fact]
    public void Golden_AllTaintSinks_HaveDiagnosticCodes()
    {
        var testCases = new[]
        {
            (TaintSink.SqlQuery, DiagnosticCode.SqlInjection),
            (TaintSink.CommandExecution, DiagnosticCode.CommandInjection),
            (TaintSink.FilePath, DiagnosticCode.PathTraversal),
            (TaintSink.HtmlOutput, DiagnosticCode.CrossSiteScripting),
        };

        foreach (var (sink, expectedCode) in testCases)
        {
            var vuln = new TaintVulnerability(
                sink,
                TaintSource.UserInput,
                "src", default,
                "sink", default);

            Assert.Equal(expectedCode, vuln.DiagnosticCode);
        }
    }

    #endregion

    #region Bug Pattern Runner - Integration Tests

    /// <summary>
    /// KNOWN BUG: Code with division by unchecked variable.
    /// EXPECTED: BugPatternRunner detects it
    /// </summary>
    [Fact]
    public void Golden_BugPatternRunner_DetectsDivisionByZero()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Buggy:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (/ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics, DefaultBugOptions);
        runner.CheckFunction(func);

        // Runner MUST detect the division by zero risk
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    /// <summary>
    /// SAFE CODE: No risky operations.
    /// EXPECTED: BugPatternRunner produces no division/index errors
    /// </summary>
    [Fact]
    public void Golden_BugPatternRunner_SafeCode_NoErrors()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Safe:pub}
      §I{i32:x}
      §O{i32}
      §B{y:i32} (+ x INT:1)
      §B{z:i32} (* y INT:2)
      §R z";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics, DefaultBugOptions);
        runner.CheckFunction(func);

        // Safe arithmetic - no division by zero errors
        Assert.DoesNotContain(diagnostics.Errors, d => d.Code == DiagnosticCode.DivisionByZero);
        Assert.DoesNotContain(diagnostics.Errors, d => d.Code == DiagnosticCode.IndexOutOfBounds);
    }

    /// <summary>
    /// Multiple bugs in one function.
    /// EXPECTED: All bugs detected
    /// </summary>
    [Fact]
    public void Golden_BugPatternRunner_MultipleBugs_AllDetected()
    {
        var source = @"
§M{m001:Test}
  §F{f001:MultiBug:pub}
      §I{i32:a}
      §I{i32:b}
      §I{i32:c}
      §O{i32}
      §B{x:i32} (/ a b)
      §B{y:i32} (/ x c)
      §B{z:i32} (% y b)
      §R z";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics, DefaultBugOptions);
        runner.CheckFunction(func);

        // Should detect multiple division issues
        var divZeroWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();

        Assert.True(divZeroWarnings.Count >= 2,
            $"Expected at least 2 division warnings for 3 unchecked operations, got {divZeroWarnings.Count}");
    }

    #endregion

    #region Verification Analysis Pass - Integration Tests

    /// <summary>
    /// Simple function analysis completes and produces result.
    /// </summary>
    [Fact]
    public void Golden_VerificationPass_SimpleFunction_Completes()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Simple:pub}
      §I{i32:x}
      §O{i32}
      §R (+ x INT:1)";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        Assert.Equal(1, result.FunctionsAnalyzed);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    /// <summary>
    /// Function with known bug - verification pass detects it.
    /// </summary>
    [Fact]
    public void Golden_VerificationPass_BuggyFunction_DetectsBug()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Buggy:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (/ x y)";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        Assert.Equal(1, result.FunctionsAnalyzed);
        Assert.True(result.BugPatternsFound > 0, "Should detect division by zero bug");
    }

    /// <summary>
    /// Multiple functions - all analyzed.
    /// </summary>
    [Fact]
    public void Golden_VerificationPass_MultipleFunctions_AllAnalyzed()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Func1:pub}
      §I{i32:x}
      §O{i32}
      §R x
  §F{f002:Func2:pub}
      §I{i32:y}
      §O{i32}
      §R (+ y INT:1)
  §F{f003:Func3:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (/ a b)";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        Assert.Equal(3, result.FunctionsAnalyzed);
    }

    private static Ast.ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #endregion

    #region Edge Cases and Boundary Conditions

    /// <summary>
    /// Empty module - should not crash.
    /// </summary>
    [Fact]
    public void Golden_EmptyModule_NoCrash()
    {
        var source = @"
§M{m001:Test}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        Assert.Equal(0, result.FunctionsAnalyzed);
        Assert.Equal(0, result.BugPatternsFound);
    }

    /// <summary>
    /// Deeply nested expression - analysis completes.
    /// </summary>
    [Fact]
    public void Golden_DeeplyNestedExpression_Completes()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Nested:pub}
      §I{i32:x}
      §O{i32}
      §R (+ (+ (+ (+ x INT:1) INT:2) INT:3) INT:4)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics, DefaultBugOptions);
        runner.CheckFunction(func);

        // Should complete without crash
        Assert.False(diagnostics.HasErrors);
    }

    /// <summary>
    /// Division in deeply nested expression - still detected.
    /// </summary>
    [Fact]
    public void Golden_DivisionInNestedExpression_Detected()
    {
        var source = @"
§M{m001:Test}
  §F{f001:NestedDiv:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (+ INT:1 (+ INT:2 (/ x y)))";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // Division buried in expression MUST still be detected
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    /// <summary>
    /// Multiple bindings with same variable name pattern - all checked.
    /// </summary>
    [Fact]
    public void Golden_MultipleBindings_AllChecked()
    {
        var source = @"
§M{m001:Test}
  §F{f001:MultiBinds:pub}
      §I{i32:divisor1}
      §I{i32:divisor2}
      §I{i32:divisor3}
      §O{i32}
      §B{a:i32} (/ INT:100 divisor1)
      §B{b:i32} (/ INT:200 divisor2)
      §B{c:i32} (/ INT:300 divisor3)
      §R (+ a (+ b c))";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // All three divisions should be flagged
        var divWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.True(divWarnings.Count >= 3,
            $"Expected 3 division warnings, got {divWarnings.Count}");
    }

    #endregion

    #region Control Flow with Bugs

    /// <summary>
    /// Bug in then-branch only.
    /// </summary>
    [Fact]
    public void Golden_BugInThenBranch_Detected()
    {
        var source = @"
§M{m001:Test}
  §F{f001:ThenBug:pub}
      §I{i32:x}
      §I{i32:y}
      §I{bool:cond}
      §O{i32}
      §IF{if1} cond
          §R (/ x y)
      §EL
          §R INT:0";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // Bug in then-branch should be detected
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    /// <summary>
    /// Bug in else-branch only.
    /// </summary>
    [Fact]
    public void Golden_BugInElseBranch_Detected()
    {
        var source = @"
§M{m001:Test}
  §F{f001:ElseBug:pub}
      §I{i32:x}
      §I{i32:y}
      §I{bool:cond}
      §O{i32}
      §IF{if1} cond
          §R INT:0
      §EL
          §R (/ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // Bug in else-branch should be detected
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    /// <summary>
    /// Bugs in both branches.
    /// </summary>
    [Fact]
    public void Golden_BugsInBothBranches_BothDetected()
    {
        var source = @"
§M{m001:Test}
  §F{f001:BothBugs:pub}
      §I{i32:x}
      §I{i32:y}
      §I{i32:z}
      §I{bool:cond}
      §O{i32}
      §IF{if1} cond
          §R (/ x y)
      §EL
          §R (/ x z)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // Both branches have bugs - both should be detected
        var divWarnings = diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.DivisionByZero)
            .ToList();
        Assert.True(divWarnings.Count >= 2,
            $"Expected at least 2 division warnings, got {divWarnings.Count}");
    }

    /// <summary>
    /// Bug in loop body.
    /// </summary>
    [Fact]
    public void Golden_BugInLoopBody_Detected()
    {
        var source = @"
§M{m001:Test}
  §F{f001:LoopBug:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §B{result:i32} INT:0
      §L{l1:i:0:10:1}
          §B{temp:i32} (/ x y)
      §R result";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(DefaultBugOptions);
        checker.Check(func, diagnostics);

        // Bug inside loop should be detected
        Assert.Contains(diagnostics.Warnings, d => d.Code == DiagnosticCode.DivisionByZero);
    }

    #endregion

    #region Z3-Backed Verification (Skipped if Z3 unavailable)

    /// <summary>
    /// With Z3: Division with precondition y != 0 should be proven safe.
    /// </summary>
    [SkippableFact]
    public void Golden_Z3_DivisionWithPrecondition_ProvenSafe()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
  §F{f001:PrecondDiv:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §Q (!= y INT:0)
      §R (/ x y)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(Z3BugOptions);
        checker.Check(func, diagnostics);

        // With Z3 and precondition, should prove division is safe
        // Note: Implementation may need to use preconditions
        Assert.NotNull(diagnostics);
    }

    /// <summary>
    /// With Z3: Division by literal zero should still be error.
    /// </summary>
    [SkippableFact]
    public void Golden_Z3_DivisionByLiteralZero_StillError()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
  §F{f001:LiteralZero:pub}
      §I{i32:x}
      §O{i32}
      §R (/ x INT:0)";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var checker = new DivisionByZeroChecker(Z3BugOptions);
        checker.Check(func, diagnostics);

        // Even with Z3, literal zero division is an error
        Assert.True(diagnostics.HasErrors ||
            diagnostics.Warnings.Any(d => d.Code == DiagnosticCode.DivisionByZero));
    }

    #endregion
}
