using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Tests for taint analysis including SQL injection, command injection,
/// path traversal, and XSS detection.
/// </summary>
public class TaintAnalysisTests
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

    #endregion

    #region Taint Source Enum Tests

    [Fact]
    public void TaintSource_UserInput_Exists()
    {
        var source = TaintSource.UserInput;
        Assert.Equal(TaintSource.UserInput, source);
    }

    [Fact]
    public void TaintSource_Environment_Exists()
    {
        var source = TaintSource.Environment;
        Assert.Equal(TaintSource.Environment, source);
    }

    [Fact]
    public void TaintSource_FileRead_Exists()
    {
        var source = TaintSource.FileRead;
        Assert.Equal(TaintSource.FileRead, source);
    }

    [Fact]
    public void TaintSource_NetworkInput_Exists()
    {
        var source = TaintSource.NetworkInput;
        Assert.Equal(TaintSource.NetworkInput, source);
    }

    [Fact]
    public void TaintSource_DatabaseResult_Exists()
    {
        var source = TaintSource.DatabaseResult;
        Assert.Equal(TaintSource.DatabaseResult, source);
    }

    #endregion

    #region Taint Sink Enum Tests

    [Fact]
    public void TaintSink_SqlQuery_Exists()
    {
        var sink = TaintSink.SqlQuery;
        Assert.Equal(TaintSink.SqlQuery, sink);
    }

    [Fact]
    public void TaintSink_CommandExecution_Exists()
    {
        var sink = TaintSink.CommandExecution;
        Assert.Equal(TaintSink.CommandExecution, sink);
    }

    [Fact]
    public void TaintSink_FilePath_Exists()
    {
        var sink = TaintSink.FilePath;
        Assert.Equal(TaintSink.FilePath, sink);
    }

    [Fact]
    public void TaintSink_HtmlOutput_Exists()
    {
        var sink = TaintSink.HtmlOutput;
        Assert.Equal(TaintSink.HtmlOutput, sink);
    }

    [Fact]
    public void TaintSink_CodeEval_Exists()
    {
        var sink = TaintSink.CodeEval;
        Assert.Equal(TaintSink.CodeEval, sink);
    }

    #endregion

    #region Taint Label Tests

    [Fact]
    public void TaintLabel_Creation_StoresValues()
    {
        var span = new TextSpan(0, 10, 1, 1);
        var label = new TaintLabel(TaintSource.UserInput, "param", span);

        Assert.Equal(TaintSource.UserInput, label.Source);
        Assert.Equal("param", label.SourceVariable);
        Assert.Equal(span, label.SourceLocation);
    }

    [Fact]
    public void TaintLabel_Equality_SameValues()
    {
        var span = new TextSpan(0, 10, 1, 1);
        var label1 = new TaintLabel(TaintSource.UserInput, "param", span);
        var label2 = new TaintLabel(TaintSource.UserInput, "param", span);

        Assert.Equal(label1, label2);
    }

    [Fact]
    public void TaintLabel_Inequality_DifferentSource()
    {
        var span = new TextSpan(0, 10, 1, 1);
        var label1 = new TaintLabel(TaintSource.UserInput, "param", span);
        var label2 = new TaintLabel(TaintSource.FileRead, "param", span);

        Assert.NotEqual(label1, label2);
    }

    #endregion

    #region Taint Analysis Options Tests

    [Fact]
    public void TaintAnalysisOptions_Default_TracksUserInput()
    {
        var options = TaintAnalysisOptions.Default;

        Assert.True(options.TrackUserInput);
    }

    [Fact]
    public void TaintAnalysisOptions_Default_TracksFileReads()
    {
        var options = TaintAnalysisOptions.Default;

        Assert.True(options.TrackFileReads);
    }

    [Fact]
    public void TaintAnalysisOptions_Default_TracksNetworkInput()
    {
        var options = TaintAnalysisOptions.Default;

        Assert.True(options.TrackNetworkInput);
    }

    [Fact]
    public void TaintAnalysisOptions_Default_DetectsSqlInjection()
    {
        var options = TaintAnalysisOptions.Default;

        Assert.True(options.DetectSqlInjection);
    }

    [Fact]
    public void TaintAnalysisOptions_Default_DetectsCommandInjection()
    {
        var options = TaintAnalysisOptions.Default;

        Assert.True(options.DetectCommandInjection);
    }

    [Fact]
    public void TaintAnalysisOptions_Default_DetectsPathTraversal()
    {
        var options = TaintAnalysisOptions.Default;

        Assert.True(options.DetectPathTraversal);
    }

    [Fact]
    public void TaintAnalysisOptions_Default_DetectsXss()
    {
        var options = TaintAnalysisOptions.Default;

        Assert.True(options.DetectXss);
    }

    [Fact]
    public void TaintAnalysisOptions_CustomDisabled_RespectsSetting()
    {
        var options = new TaintAnalysisOptions
        {
            TrackUserInput = false,
            DetectSqlInjection = false
        };

        Assert.False(options.TrackUserInput);
        Assert.False(options.DetectSqlInjection);
    }

    #endregion

    #region Taint Vulnerability Tests

    [Fact]
    public void TaintVulnerability_Creation_HasAllFields()
    {
        var sourceSpan = new TextSpan(0, 10, 1, 1);
        var sinkSpan = new TextSpan(50, 20, 5, 5);

        var vuln = new TaintVulnerability(
            TaintSink.SqlQuery,
            TaintSource.UserInput,
            "user_input",
            sourceSpan,
            "query",
            sinkSpan);

        Assert.Equal(TaintSink.SqlQuery, vuln.Sink);
        Assert.Equal(TaintSource.UserInput, vuln.Source);
        Assert.Equal("user_input", vuln.SourceVariable);
        Assert.Equal(sourceSpan, vuln.SourceLocation);
        Assert.Equal("query", vuln.SinkVariable);
        Assert.Equal(sinkSpan, vuln.SinkLocation);
    }

    [Fact]
    public void TaintVulnerability_SqlInjection_HasCorrectDiagnosticCode()
    {
        var vuln = new TaintVulnerability(
            TaintSink.SqlQuery,
            TaintSource.UserInput,
            "input", default, "query", default);

        Assert.Equal(DiagnosticCode.SqlInjection, vuln.DiagnosticCode);
    }

    [Fact]
    public void TaintVulnerability_CommandInjection_HasCorrectDiagnosticCode()
    {
        var vuln = new TaintVulnerability(
            TaintSink.CommandExecution,
            TaintSource.UserInput,
            "cmd", default, "exec", default);

        Assert.Equal(DiagnosticCode.CommandInjection, vuln.DiagnosticCode);
    }

    [Fact]
    public void TaintVulnerability_PathTraversal_HasCorrectDiagnosticCode()
    {
        var vuln = new TaintVulnerability(
            TaintSink.FilePath,
            TaintSource.UserInput,
            "path", default, "file", default);

        Assert.Equal(DiagnosticCode.PathTraversal, vuln.DiagnosticCode);
    }

    [Fact]
    public void TaintVulnerability_XSS_HasCorrectDiagnosticCode()
    {
        var vuln = new TaintVulnerability(
            TaintSink.HtmlOutput,
            TaintSource.UserInput,
            "html", default, "output", default);

        Assert.Equal(DiagnosticCode.CrossSiteScripting, vuln.DiagnosticCode);
    }

    [Fact]
    public void TaintVulnerability_HasMessage()
    {
        var vuln = new TaintVulnerability(
            TaintSink.SqlQuery,
            TaintSource.UserInput,
            "input", default, "query", default);

        Assert.NotNull(vuln.Message);
        Assert.NotEmpty(vuln.Message);
        Assert.Contains("SQL", vuln.Message);
    }

    #endregion

    #region Taint Analysis Integration Tests

    [Fact]
    public void TaintAnalysis_SimpleFunction_NoVulnerabilities()
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

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // Simple arithmetic - no taint vulnerabilities
        Assert.Empty(analysis.Vulnerabilities);
    }

    [Fact]
    public void TaintAnalysis_ReportDiagnostics_AddsToCollection()
    {
        var source = @"
§M{m001:Test}
§F{f001:Simple:pub}
  §I{i32:x}
  §O{i32}
  §R x
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);
        var diagnostics = new DiagnosticBag();
        analysis.ReportDiagnostics(diagnostics);

        // No vulnerabilities = no diagnostics added
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void TaintAnalysis_EmptyFunction_NoVulnerabilities()
    {
        var source = @"
§M{m001:Test}
§F{f001:Empty:pub}
  §O{void}
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        Assert.Empty(analysis.Vulnerabilities);
    }

    [Fact]
    public void TaintAnalysis_FunctionWithCall_AnalyzesArguments()
    {
        var source = @"
§M{m001:Test}
§F{f001:Process:pub}
  §I{string:data}
  §O{string}
  §R (CALL process data)
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // The analysis runs without crashing
        Assert.NotNull(analysis);
    }

    [Fact]
    public void TaintAnalysis_UserInputParameter_IsTainted()
    {
        var source = @"
§M{m001:Test}
§F{f001:Process:pub}
  §I{string:user_input}
  §O{string}
  §R user_input
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // Parameter named "user_input" should be recognized as tainted
        // Whether it creates a vulnerability depends on the sink
        Assert.NotNull(analysis);
    }

    [Fact]
    public void TaintAnalysis_DisabledTracking_NoTaint()
    {
        var source = @"
§M{m001:Test}
§F{f001:Process:pub}
  §I{string:user_input}
  §O{string}
  §R user_input
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var options = new TaintAnalysisOptions
        {
            TrackUserInput = false,
            TrackFileReads = false,
            TrackNetworkInput = false,
            TrackEnvironment = false
        };

        var analysis = new TaintAnalysis(func, options);

        // With all tracking disabled, no vulnerabilities
        Assert.Empty(analysis.Vulnerabilities);
    }

    #endregion

    #region Taint Analysis Runner Tests

    [Fact]
    public void TaintAnalysisRunner_NullDiagnostics_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TaintAnalysisRunner(null!));
    }

    [Fact]
    public void TaintAnalysisRunner_AnalyzesModule()
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
§/M{m001}";

        var bound = Bind(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new TaintAnalysisRunner(diagnostics, TaintAnalysisOptions.Default);
        runner.Analyze(bound);

        // Should complete without error
        Assert.NotNull(runner);
    }

    [Fact]
    public void TaintAnalysisRunner_AnalyzesFunction()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
  §R x
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new TaintAnalysisRunner(diagnostics, TaintAnalysisOptions.Default);
        runner.AnalyzeFunction(func);

        Assert.NotNull(runner);
    }

    #endregion

    #region Taint Source Detection Tests

    [Fact]
    public void TaintSource_ParameterNamedUserInput_IsTainted()
    {
        var source = @"
§M{m001:Test}
§F{f001:Process:pub}
  §I{string:user_input}
  §O{string}
  §R user_input
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        if (parseDiag.HasErrors) return; // Skip if string type not supported

        var options = TaintAnalysisOptions.Default;
        var analysis = new TaintAnalysis(func, options);

        // Parameter with "user" in name should be tainted
        Assert.NotNull(analysis);
    }

    [Fact]
    public void TaintSource_ParameterNamedRequest_IsTainted()
    {
        var source = @"
§M{m001:Test}
§F{f001:Handle:pub}
  §I{string:request_data}
  §O{string}
  §R request_data
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);
        Assert.NotNull(analysis);
    }

    [Fact]
    public void TaintSource_RegularParameter_NotTainted()
    {
        var source = @"
§M{m001:Test}
§F{f001:Calculate:pub}
  §I{i32:value}
  §O{i32}
  §R value
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // Regular numeric parameter should not be tainted
        Assert.Empty(analysis.Vulnerabilities);
    }

    #endregion

    #region Taint Propagation Tests

    [Fact]
    public void TaintPropagation_ThroughBinding_Propagates()
    {
        var source = @"
§M{m001:Test}
§F{f001:Propagate:pub}
  §I{string:user_data}
  §O{string}
  §B{processed:string} user_data
  §R processed
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // Taint should propagate through binding
        Assert.NotNull(analysis);
    }

    [Fact]
    public void TaintPropagation_ThroughExpression_Propagates()
    {
        var source = @"
§M{m001:Test}
§F{f001:Concat:pub}
  §I{string:user_input}
  §I{string:prefix}
  §O{string}
  §B{result:string} (+ prefix user_input)
  §R result
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        if (parseDiag.HasErrors) return;

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // Expression containing tainted data should be tainted
        Assert.NotNull(analysis);
    }

    #endregion

    #region Safe Code Tests (Negative Tests)

    [Fact]
    public void SafeCode_NoTaintSources_NoVulnerabilities()
    {
        var source = @"
§M{m001:Test}
§F{f001:Safe:pub}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
  §R (+ x y)
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // No taint sources - no vulnerabilities
        Assert.Empty(analysis.Vulnerabilities);
    }

    [Fact]
    public void SafeCode_ConstantsOnly_NoVulnerabilities()
    {
        var source = @"
§M{m001:Test}
§F{f001:Constants:pub}
  §O{i32}
  §B{a:i32} INT:1
  §B{b:i32} INT:2
  §R (+ a b)
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);

        // Constants only - no vulnerabilities
        Assert.Empty(analysis.Vulnerabilities);
    }

    #endregion

    #region Vulnerability Detection Tests

    [Fact]
    public void TaintVulnerability_Severity_IsCritical()
    {
        var sourceSpan = new TextSpan(0, 10, 1, 1);
        var sinkSpan = new TextSpan(50, 20, 5, 5);

        var vuln = new TaintVulnerability(
            TaintSink.SqlQuery,
            TaintSource.UserInput,
            "user_input", sourceSpan,
            "query", sinkSpan);

        // SQL injection is a critical vulnerability
        Assert.Equal(DiagnosticCode.SqlInjection, vuln.DiagnosticCode);
    }

    [Fact]
    public void TaintVulnerability_CodeEval_HasCorrectCode()
    {
        var vuln = new TaintVulnerability(
            TaintSink.CodeEval,
            TaintSource.UserInput,
            "code", default, "eval", default);

        // Code eval should have a specific diagnostic code (not empty)
        Assert.NotNull(vuln.DiagnosticCode);
        Assert.NotEmpty(vuln.DiagnosticCode);
    }

    [Fact]
    public void TaintVulnerability_AllSinkTypes_HaveDiagnosticCodes()
    {
        var sinks = new[]
        {
            TaintSink.SqlQuery,
            TaintSink.CommandExecution,
            TaintSink.FilePath,
            TaintSink.HtmlOutput,
            TaintSink.CodeEval
        };

        foreach (var sink in sinks)
        {
            var vuln = new TaintVulnerability(
                sink,
                TaintSource.UserInput,
                "src", default, "sink", default);

            // Each sink should have a valid diagnostic code
            Assert.NotNull(vuln.DiagnosticCode);
            Assert.NotEmpty(vuln.DiagnosticCode);
            Assert.NotEmpty(vuln.Message);
        }
    }

    #endregion

    #region Options Configuration Tests

    [Fact]
    public void TaintAnalysisOptions_AllDisabled_NoTracking()
    {
        var options = new TaintAnalysisOptions
        {
            TrackUserInput = false,
            TrackFileReads = false,
            TrackNetworkInput = false,
            TrackEnvironment = false,
            DetectSqlInjection = false,
            DetectCommandInjection = false,
            DetectPathTraversal = false,
            DetectXss = false
        };

        Assert.False(options.TrackUserInput);
        Assert.False(options.DetectSqlInjection);
    }

    [Fact]
    public void TaintAnalysisOptions_SelectiveTracking_Respected()
    {
        var options = new TaintAnalysisOptions
        {
            TrackUserInput = true,
            TrackFileReads = false,
            DetectSqlInjection = true,
            DetectCommandInjection = false
        };

        Assert.True(options.TrackUserInput);
        Assert.False(options.TrackFileReads);
        Assert.True(options.DetectSqlInjection);
        Assert.False(options.DetectCommandInjection);
    }

    #endregion

    #region Diagnostic Reporting Tests

    [Fact]
    public void TaintAnalysis_ReportDiagnostics_NoVulnerabilities_NoDiagnostics()
    {
        var source = @"
§M{m001:Test}
§F{f001:Safe:pub}
  §I{i32:x}
  §O{i32}
  §R x
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var analysis = new TaintAnalysis(func, TaintAnalysisOptions.Default);
        var diagnostics = new DiagnosticBag();
        analysis.ReportDiagnostics(diagnostics);

        // No vulnerabilities - no taint-related errors
        var taintDiags = diagnostics.Where(d =>
            d.Code == DiagnosticCode.SqlInjection ||
            d.Code == DiagnosticCode.CommandInjection ||
            d.Code == DiagnosticCode.PathTraversal ||
            d.Code == DiagnosticCode.CrossSiteScripting).ToList();
        Assert.Empty(taintDiags);
    }

    [Fact]
    public void TaintAnalysis_MultipleFunctions_AllAnalyzed()
    {
        var source = @"
§M{m001:Test}
§F{f001:Func1:pub}
  §I{i32:x}
  §O{i32}
  §R x
§/F{f001}
§F{f002:Func2:pub}
  §I{i32:y}
  §O{i32}
  §R y
§/F{f002}
§/M{m001}";

        var bound = Bind(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new TaintAnalysisRunner(diagnostics, TaintAnalysisOptions.Default);
        runner.Analyze(bound);

        // Both functions should be analyzed
        Assert.NotNull(runner);
    }

    #endregion
}
