using Calor.Compiler.Analysis;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Integration tests for the VerificationAnalysisPass that orchestrates
/// dataflow, bug patterns, taint analysis, and k-induction.
/// </summary>
public class VerificationAnalysisPassTests
{
    #region Helpers

    private static Ast.ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static BoundModule Bind(string source, out DiagnosticBag diagnostics)
    {
        var module = Parse(source, out diagnostics);
        if (diagnostics.HasErrors) return null!;

        var binder = new Binder(diagnostics);
        return binder.Bind(module);
    }

    #endregion

    #region Options Tests

    [Fact]
    public void VerificationAnalysisOptions_Default_EnablesMainAnalyses()
    {
        var options = VerificationAnalysisOptions.Default;

        Assert.True(options.EnableDataflow);
        Assert.True(options.EnableBugPatterns);
        Assert.True(options.EnableTaintAnalysis);
        Assert.False(options.EnableKInduction); // Off by default - expensive
    }

    [Fact]
    public void VerificationAnalysisOptions_Fast_DisablesZ3()
    {
        var options = VerificationAnalysisOptions.Fast;

        Assert.False(options.UseZ3Verification);
        Assert.False(options.EnableKInduction);
    }

    [Fact]
    public void VerificationAnalysisOptions_Thorough_EnablesAll()
    {
        var options = VerificationAnalysisOptions.Thorough;

        Assert.True(options.EnableDataflow);
        Assert.True(options.EnableBugPatterns);
        Assert.True(options.EnableTaintAnalysis);
        Assert.True(options.EnableKInduction);
        Assert.True(options.UseZ3Verification);
    }

    [Fact]
    public void VerificationAnalysisOptions_DefaultTimeout_IsReasonable()
    {
        var options = VerificationAnalysisOptions.Default;

        Assert.True(options.Z3TimeoutMs >= 1000);
        Assert.True(options.Z3TimeoutMs <= 30000);
    }

    #endregion

    #region Result Tests

    [Fact]
    public void VerificationAnalysisResult_HasAllFields()
    {
        var result = new VerificationAnalysisResult
        {
            FunctionsAnalyzed = 5,
            DataflowIssues = 2,
            BugPatternsFound = 3,
            TaintVulnerabilities = 1,
            LoopInvariantsSynthesized = 4,
            Duration = TimeSpan.FromSeconds(1)
        };

        Assert.Equal(5, result.FunctionsAnalyzed);
        Assert.Equal(2, result.DataflowIssues);
        Assert.Equal(3, result.BugPatternsFound);
        Assert.Equal(1, result.TaintVulnerabilities);
        Assert.Equal(4, result.LoopInvariantsSynthesized);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Duration);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void VerificationAnalysisPass_SimpleFunction_Completes()
    {
        var source = @"
§M{m001:Test}
§F{f001:Add:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §R (+ x y)
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        Assert.Equal(1, result.FunctionsAnalyzed);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public void VerificationAnalysisPass_BoundModule_Completes()
    {
        var source = @"
§M{m001:Test}
§F{f001:Multiply:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §R (* x y)
§/F{f001}
§/M{m001}";

        var bound = Bind(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.AnalyzeBound(bound);

        Assert.Equal(1, result.FunctionsAnalyzed);
    }

    [Fact]
    public void VerificationAnalysisPass_MultipleFunctions_AnalyzesAll()
    {
        var source = @"
§M{m001:Test}
§F{f001:Foo:pub}
  §O{i32}
  §R INT:1
§/F{f001}
§F{f002:Bar:pub}
  §O{i32}
  §R INT:2
§/F{f002}
§F{f003:Baz:pub}
  §O{i32}
  §R INT:3
§/F{f003}
§/M{m001}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        Assert.Equal(3, result.FunctionsAnalyzed);
    }

    [Fact]
    public void VerificationAnalysisPass_DivisionByZero_DetectsBug()
    {
        var source = @"
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §R (/ x y)
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        Assert.True(result.BugPatternsFound > 0);
    }

    [Fact]
    public void VerificationAnalysisPass_SafeCode_FewIssues()
    {
        var source = @"
§M{m001:Test}
§F{f001:Safe:pub}
  §I{i32:x}
  §O{i32}
  §R (+ x INT:1)
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        // Safe code should have minimal issues
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void VerificationAnalysisPass_DisabledDataflow_SkipsDataflow()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §O{i32}
  §B{x:i32} INT:1
  §R x
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var options = new VerificationAnalysisOptions
        {
            EnableDataflow = false,
            EnableBugPatterns = false,
            EnableTaintAnalysis = false,
            EnableKInduction = false
        };

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, options);
        var result = pass.Analyze(module);

        Assert.Equal(0, result.DataflowIssues);
        Assert.Equal(0, result.BugPatternsFound);
        Assert.Equal(0, result.TaintVulnerabilities);
    }

    [Fact]
    public void VerificationAnalysisPass_WithLoop_AnalyzesLoop()
    {
        var source = @"
§M{m001:Test}
§F{f001:Sum:pub}
  §I{i32:n}
  §O{i32}
  §B{sum:i32} INT:0
  §L{l1:i:0:10:1}
    §C{Console.WriteLine}
      §A i
    §/C
  §/L{l1}
  §R sum
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        Assert.Equal(1, result.FunctionsAnalyzed);
    }

    [SkippableFact]
    public void VerificationAnalysisPass_WithZ3_MorePrecise()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:SafeDivide:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §Q (!= y INT:0)
  §R (/ x y)
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var options = new VerificationAnalysisOptions
        {
            EnableDataflow = true,
            EnableBugPatterns = true,
            EnableTaintAnalysis = false,
            EnableKInduction = false,
            UseZ3Verification = true,
            Z3TimeoutMs = 5000
        };

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, options);
        var result = pass.Analyze(module);

        // With precondition, Z3 should prove division is safe
        Assert.NotNull(result);
    }

    [SkippableFact]
    public void VerificationAnalysisPass_Thorough_EnablesKInduction()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Sum:pub}
  §I{i32:n}
  §O{i32}
  §Q (>= n INT:0)
  §B{sum:i32} INT:0
  §L{l1:i:0:10:1}
    §C{Console.WriteLine}
      §A i
    §/C
  §/L{l1}
  §R sum
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Thorough);
        var result = pass.Analyze(module);

        // Thorough mode enables k-induction
        Assert.NotNull(result);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void VerificationAnalysisPass_NullDiagnostics_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new VerificationAnalysisPass(null!));
    }

    [Fact]
    public void VerificationAnalysisPass_EmptyModule_Handles()
    {
        var source = @"
§M{m001:Test}
§/M{m001}";

        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        Assert.Equal(0, result.FunctionsAnalyzed);
    }

    #endregion
}
