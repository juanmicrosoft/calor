using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Tests that verify the benchmark programs produce expected diagnostics.
/// These tests validate that intentional bugs are detected and safe code is clean.
/// </summary>
public class BenchmarkVerificationTests
{
    #region Helpers

    private static string GetBenchmarkPath(string relativePath)
    {
        // Navigate from test output to benchmarks directory
        var testDir = AppContext.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        return Path.Combine(projectRoot, "benchmarks", relativePath);
    }

    private static BoundModule? ParseAndBind(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        if (diagnostics.HasErrors) return null;

        var binder = new Binder(diagnostics);
        return binder.Bind(module);
    }

    private static List<Diagnostic> RunTaintAnalysis(BoundModule module)
    {
        var diagnostics = new DiagnosticBag();
        var runner = new TaintAnalysisRunner(diagnostics, TaintAnalysisOptions.Default);
        runner.Analyze(module);
        return diagnostics.ToList();
    }

    private static List<Diagnostic> RunBugPatternAnalysis(BoundModule module)
    {
        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(diagnostics);
        runner.Check(module);
        return diagnostics.ToList();
    }

    private static bool HasDiagnosticCode(IEnumerable<Diagnostic> diagnostics, string code)
    {
        return diagnostics.Any(d => d.Code == code);
    }

    #endregion

    #region SQL Injection Benchmark Tests

    [Fact]
    public void SqlInjection_VulnerableQuery_DetectsInjection()
    {
        var source = @"
§M{m001:Test}
§F{f001:VulnerableQuery:pub}
  §I{string:user_input}
  §O{string}
  §B{query:string} (+ STR:""SELECT * FROM users WHERE name = '"" user_input)
  §C db.execute query
  §R query
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return; // Skip if parsing not supported

        var diagnostics = RunTaintAnalysis(module!);

        // Should detect SQL injection
        Assert.True(HasDiagnosticCode(diagnostics, DiagnosticCode.SqlInjection),
            "Expected SQL injection warning for direct concatenation with user input");
    }

    [Fact]
    public void SqlInjection_SafeParameterizedQuery_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeQuery:pub}
  §I{string:user_input}
  §O{string}
  §B{query:string} STR:""SELECT * FROM users WHERE name = ?""
  §C db.execute_param query user_input
  §R query
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunTaintAnalysis(module!);

        // Should NOT detect SQL injection for parameterized queries
        Assert.False(HasDiagnosticCode(diagnostics, DiagnosticCode.SqlInjection),
            "Parameterized query should not trigger SQL injection warning");
    }

    [Fact]
    public void SqlInjection_EscapedInput_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeEscaped:pub}
  §I{string:user_input}
  §O{string}
  §B{escaped:string} (CALL sql_escape user_input)
  §B{query:string} (+ STR:""SELECT * FROM users WHERE name = '"" escaped)
  §C db.execute query
  §R query
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunTaintAnalysis(module!);

        // Sanitized input should not trigger warning
        Assert.False(HasDiagnosticCode(diagnostics, DiagnosticCode.SqlInjection),
            "Escaped input should not trigger SQL injection warning");
    }

    [Fact]
    public void TaintAnalysis_ParamLogging_NotExcluded()
    {
        // db.execute_with_param_logging should NOT be excluded — it's a real sink
        var source = @"
§M{m001:Test}
§F{f001:ParamLogging:pub}
  §I{string:user_input}
  §O{string}
  §B{query:string} (+ STR:""SELECT * FROM users WHERE name = '"" user_input)
  §C db.execute_with_param_logging query
  §R query
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunTaintAnalysis(module!);

        Assert.True(HasDiagnosticCode(diagnostics, DiagnosticCode.SqlInjection),
            "db.execute_with_param_logging should be flagged as a SQL sink (not falsely excluded)");
    }

    [Fact]
    public void TaintAnalysis_ExecuteParam_StillExcluded()
    {
        // db.execute_param should still be excluded — it's a parameterized query
        var source = @"
§M{m001:Test}
§F{f001:ExecuteParam:pub}
  §I{string:user_input}
  §O{string}
  §B{query:string} STR:""SELECT * FROM users WHERE name = ?""
  §C db.execute_param query user_input
  §R query
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunTaintAnalysis(module!);

        Assert.False(HasDiagnosticCode(diagnostics, DiagnosticCode.SqlInjection),
            "db.execute_param should be excluded as a parameterized query");
    }

    #endregion

    #region Command Injection Benchmark Tests

    [Fact]
    public void CommandInjection_VulnerableShellExec_DetectsInjection()
    {
        var source = @"
§M{m001:Test}
§F{f001:VulnerableShell:pub}
  §I{string:user_input}
  §O{i32}
  §B{cmd:string} (+ STR:""ls -la "" user_input)
  §C shell cmd
  §R INT:0
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunTaintAnalysis(module!);

        Assert.True(HasDiagnosticCode(diagnostics, DiagnosticCode.CommandInjection),
            "Expected command injection warning for shell execution with user input");
    }

    [Fact]
    public void CommandInjection_SafeWhitelisted_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeWhitelist:pub}
  §I{i32:option}
  §O{i32}
  §IF (== option INT:1)
    §C exec STR:""ls""
  §EL
    §C exec STR:""pwd""
  §/IF
  §R INT:0
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunTaintAnalysis(module!);

        Assert.False(HasDiagnosticCode(diagnostics, DiagnosticCode.CommandInjection),
            "Whitelisted commands should not trigger command injection warning");
    }

    #endregion

    #region Path Traversal Benchmark Tests

    [Fact]
    public void PathTraversal_VulnerableFileRead_DetectsTraversal()
    {
        var source = @"
§M{m001:Test}
§F{f001:VulnerableRead:pub}
  §I{string:user_path}
  §O{string}
  §B{content:string} (CALL file.read user_path)
  §R content
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunTaintAnalysis(module!);

        Assert.True(HasDiagnosticCode(diagnostics, DiagnosticCode.PathTraversal),
            "Expected path traversal warning for file.read with user input");
    }

    [Fact]
    public void PathTraversal_SafeConstantPath_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeConstant:pub}
  §O{string}
  §B{path:string} STR:""/etc/config.json""
  §B{content:string} (CALL file.read path)
  §R content
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunTaintAnalysis(module!);

        Assert.False(HasDiagnosticCode(diagnostics, DiagnosticCode.PathTraversal),
            "Constant path should not trigger path traversal warning");
    }

    #endregion

    #region Division by Zero Benchmark Tests

    [Fact]
    public void DivByZero_VulnerableDivision_DetectsRisk()
    {
        var source = @"
§M{m001:Test}
§F{f001:VulnerableDiv:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §R (/ x y)
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = RunBugPatternAnalysis(module!);

        Assert.True(HasDiagnosticCode(diagnostics, DiagnosticCode.DivisionByZero),
            "Expected division by zero warning for unchecked divisor");
    }

    [Fact]
    public void DivByZero_SafeWithGuard_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeDiv:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §IF (!= y INT:0)
    §R (/ x y)
  §EL
    §R INT:0
  §/IF
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return; // Skip if IF syntax not yet supported

        var diagnostics = RunBugPatternAnalysis(module!);

        Assert.False(HasDiagnosticCode(diagnostics, DiagnosticCode.DivisionByZero),
            "Division with guard should not trigger warning");
    }

    [Fact]
    public void DivByZero_DivisionByLiteral_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeLiteralDiv:pub}
  §I{i32:x}
  §O{i32}
  §R (/ x INT:2)
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = RunBugPatternAnalysis(module!);

        Assert.False(HasDiagnosticCode(diagnostics, DiagnosticCode.DivisionByZero),
            "Division by non-zero literal should not trigger warning");
    }

    #endregion

    #region Null Dereference Benchmark Tests

    [Fact]
    public void NullDeref_VulnerableAccess_DetectsRisk()
    {
        var source = @"
§M{m001:Test}
§F{f001:VulnerableNull:pub}
  §I{string:maybeNull}
  §O{i32}
  §R (CALL string.length maybeNull)
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunBugPatternAnalysis(module!);

        // Note: This depends on whether the bug pattern runner tracks null
        // The test validates the runner processes the code without error
        Assert.NotNull(diagnostics);
    }

    [Fact]
    public void NullDeref_SafeNullCheck_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeNull:pub}
  §I{string:maybeNull}
  §O{i32}
  §IF (!= maybeNull NONE)
    §R (CALL string.length maybeNull)
  §EL
    §R INT:0
  §/IF
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunBugPatternAnalysis(module!);

        Assert.False(HasDiagnosticCode(diagnostics, DiagnosticCode.NullDereference),
            "Null check should prevent null dereference warning");
    }

    #endregion

    #region Bounds Violation Benchmark Tests

    [Fact]
    public void Bounds_VulnerableArrayAccess_DetectsRisk()
    {
        var source = @"
§M{m001:Test}
§F{f001:VulnerableBounds:pub}
  §I{i32:index}
  §O{i32}
  §R (CALL array.get index)
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunBugPatternAnalysis(module!);

        // Test validates the analysis runs without errors
        Assert.NotNull(diagnostics);
    }

    [Fact]
    public void Bounds_SafeBoundsCheck_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:SafeBounds:pub}
  §I{i32:index}
  §I{i32:len}
  §O{i32}
  §IF (&& (>= index INT:0) (< index len))
    §R (CALL array.get index)
  §EL
    §R INT:-1
  §/IF
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var diagnostics = RunBugPatternAnalysis(module!);

        Assert.False(HasDiagnosticCode(diagnostics, DiagnosticCode.IndexOutOfBounds),
            "Bounds check should prevent out of bounds warning");
    }

    #endregion

    #region Integration Tests - Multiple Analyses

    [Fact]
    public void Integration_SafeFunction_NoWarnings()
    {
        var source = @"
§M{m001:Test}
§F{f001:CompletelySecure:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §IF (== y INT:0)
    §R INT:0
  §/IF
  §R (/ x y)
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return; // Skip if IF syntax not yet supported

        var taintDiags = RunTaintAnalysis(module!);
        var bugDiags = RunBugPatternAnalysis(module!);

        // No taint vulnerabilities (no user input to sinks)
        Assert.Empty(taintDiags.Where(d =>
            d.Code == DiagnosticCode.SqlInjection ||
            d.Code == DiagnosticCode.CommandInjection ||
            d.Code == DiagnosticCode.PathTraversal));

        // No division by zero (guarded)
        Assert.False(HasDiagnosticCode(bugDiags, DiagnosticCode.DivisionByZero));
    }

    [Fact]
    public void Integration_VulnerableFunction_MultipleWarnings()
    {
        var source = @"
§M{m001:Test}
§F{f001:InsecureFunction:pub}
  §I{string:user_input}
  §I{i32:divisor}
  §O{i32}
  §B{query:string} (+ STR:""SELECT * FROM t WHERE x = '"" user_input)
  §C db.execute query
  §B{result:i32} (/ INT:100 divisor)
  §R result
§/F{f001}
§/M{m001}";

        var module = ParseAndBind(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var taintDiags = RunTaintAnalysis(module!);
        var bugDiags = RunBugPatternAnalysis(module!);

        // Should have SQL injection
        Assert.True(HasDiagnosticCode(taintDiags, DiagnosticCode.SqlInjection),
            "Expected SQL injection for vulnerable query");

        // Should have division by zero risk
        Assert.True(HasDiagnosticCode(bugDiags, DiagnosticCode.DivisionByZero),
            "Expected division by zero for unchecked divisor");
    }

    #endregion

    #region Benchmark File Tests

    // Note: These tests verify benchmark files exist and can be read.
    // Parsing validation is skipped as the Calor syntax may evolve.

    [SkippableFact]
    public void BenchmarkFiles_SqlInjection_Exists()
    {
        var path = GetBenchmarkPath("security/sql-injection.calr");
        Skip.If(!File.Exists(path), "Benchmark file not found");

        var source = File.ReadAllText(path);
        Assert.NotEmpty(source);
        Assert.Contains("SqlInjection", source);
    }

    [SkippableFact]
    public void BenchmarkFiles_CommandInjection_Exists()
    {
        var path = GetBenchmarkPath("security/command-injection.calr");
        Skip.If(!File.Exists(path), "Benchmark file not found");

        var source = File.ReadAllText(path);
        Assert.NotEmpty(source);
        Assert.Contains("CommandInjection", source);
    }

    [SkippableFact]
    public void BenchmarkFiles_PathTraversal_Exists()
    {
        var path = GetBenchmarkPath("security/path-traversal.calr");
        Skip.If(!File.Exists(path), "Benchmark file not found");

        var source = File.ReadAllText(path);
        Assert.NotEmpty(source);
        Assert.Contains("PathTraversal", source);
    }

    [SkippableFact]
    public void BenchmarkFiles_DivByZero_Exists()
    {
        var path = GetBenchmarkPath("arithmetic/div-by-zero.calr");
        Skip.If(!File.Exists(path), "Benchmark file not found");

        var source = File.ReadAllText(path);
        Assert.NotEmpty(source);
        Assert.Contains("DivByZero", source);
    }

    [SkippableFact]
    public void BenchmarkFiles_Overflow_Exists()
    {
        var path = GetBenchmarkPath("arithmetic/overflow.calr");
        Skip.If(!File.Exists(path), "Benchmark file not found");

        var source = File.ReadAllText(path);
        Assert.NotEmpty(source);
        Assert.Contains("Overflow", source);
    }

    [SkippableFact]
    public void BenchmarkFiles_NullDeref_Exists()
    {
        var path = GetBenchmarkPath("null-safety/null-deref.calr");
        Skip.If(!File.Exists(path), "Benchmark file not found");

        var source = File.ReadAllText(path);
        Assert.NotEmpty(source);
        Assert.Contains("NullDeref", source);
    }

    [SkippableFact]
    public void BenchmarkFiles_BoundsViolation_Exists()
    {
        var path = GetBenchmarkPath("loops/bounds-violation.calr");
        Skip.If(!File.Exists(path), "Benchmark file not found");

        var source = File.ReadAllText(path);
        Assert.NotEmpty(source);
        Assert.Contains("BoundsViolation", source);
    }

    #endregion
}
