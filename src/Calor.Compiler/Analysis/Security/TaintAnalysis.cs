using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis.Security;

/// <summary>
/// Represents a taint source category.
/// </summary>
public enum TaintSource
{
    /// <summary>User input (command line, stdin, web forms).</summary>
    UserInput,
    /// <summary>File read operations.</summary>
    FileRead,
    /// <summary>Network/HTTP input.</summary>
    NetworkInput,
    /// <summary>Environment variables.</summary>
    Environment,
    /// <summary>Database query results.</summary>
    DatabaseResult,
    /// <summary>External API response.</summary>
    ExternalApi
}

/// <summary>
/// Represents a security-sensitive sink category.
/// </summary>
public enum TaintSink
{
    /// <summary>SQL query execution.</summary>
    SqlQuery,
    /// <summary>Command/shell execution.</summary>
    CommandExecution,
    /// <summary>File path operations.</summary>
    FilePath,
    /// <summary>HTML/web output (XSS).</summary>
    HtmlOutput,
    /// <summary>URL redirection.</summary>
    UrlRedirect,
    /// <summary>Code evaluation.</summary>
    CodeEval,
    /// <summary>Deserialization.</summary>
    Deserialization,
    /// <summary>Log injection.</summary>
    LogOutput
}

/// <summary>
/// Represents a taint label attached to a variable.
/// </summary>
public readonly record struct TaintLabel(
    TaintSource Source,
    string SourceVariable,
    TextSpan SourceLocation);

/// <summary>
/// Represents a detected security vulnerability.
/// </summary>
public sealed class TaintVulnerability
{
    public TaintSink Sink { get; }
    public TaintSource Source { get; }
    public string SourceVariable { get; }
    public TextSpan SourceLocation { get; }
    public string SinkVariable { get; }
    public TextSpan SinkLocation { get; }
    public string DiagnosticCode { get; }
    public string Message { get; }
    public DiagnosticSeverity Severity { get; }

    public TaintVulnerability(
        TaintSink sink,
        TaintSource source,
        string sourceVariable,
        TextSpan sourceLocation,
        string sinkVariable,
        TextSpan sinkLocation)
    {
        Sink = sink;
        Source = source;
        SourceVariable = sourceVariable;
        SourceLocation = sourceLocation;
        SinkVariable = sinkVariable;
        SinkLocation = sinkLocation;

        (DiagnosticCode, Message, Severity) = GetDiagnosticInfo(sink, source);
    }

    private static (string Code, string Message, DiagnosticSeverity Severity) GetDiagnosticInfo(
        TaintSink sink, TaintSource source)
    {
        return sink switch
        {
            TaintSink.SqlQuery => (
                Diagnostics.DiagnosticCode.SqlInjection,
                $"Potential SQL injection: tainted data from {source} flows to SQL query",
                DiagnosticSeverity.Warning),
            TaintSink.CommandExecution => (
                Diagnostics.DiagnosticCode.CommandInjection,
                $"Potential command injection: tainted data from {source} flows to command execution",
                DiagnosticSeverity.Warning),
            TaintSink.FilePath => (
                Diagnostics.DiagnosticCode.PathTraversal,
                $"Potential path traversal: tainted data from {source} flows to file path",
                DiagnosticSeverity.Warning),
            TaintSink.HtmlOutput => (
                Diagnostics.DiagnosticCode.CrossSiteScripting,
                $"Potential XSS: tainted data from {source} flows to HTML output",
                DiagnosticSeverity.Warning),
            _ => (
                Diagnostics.DiagnosticCode.TaintedSink,
                $"Tainted data from {source} flows to {sink}",
                DiagnosticSeverity.Warning)
        };
    }
}

/// <summary>
/// Options for taint analysis.
/// </summary>
public sealed class TaintAnalysisOptions
{
    /// <summary>
    /// Track user input as taint source.
    /// </summary>
    public bool TrackUserInput { get; init; } = true;

    /// <summary>
    /// Track file reads as taint source.
    /// </summary>
    public bool TrackFileReads { get; init; } = true;

    /// <summary>
    /// Track network input as taint source.
    /// </summary>
    public bool TrackNetworkInput { get; init; } = true;

    /// <summary>
    /// Track environment variables as taint source.
    /// </summary>
    public bool TrackEnvironment { get; init; } = true;

    /// <summary>
    /// Enable SQL injection detection.
    /// </summary>
    public bool DetectSqlInjection { get; init; } = true;

    /// <summary>
    /// Enable command injection detection.
    /// </summary>
    public bool DetectCommandInjection { get; init; } = true;

    /// <summary>
    /// Enable path traversal detection.
    /// </summary>
    public bool DetectPathTraversal { get; init; } = true;

    /// <summary>
    /// Enable XSS detection.
    /// </summary>
    public bool DetectXss { get; init; } = true;

    public static TaintAnalysisOptions Default => new();
}

/// <summary>
/// Forward dataflow analysis for taint tracking.
/// Detects when user-controlled (tainted) data flows to security-sensitive operations.
/// </summary>
public sealed class TaintAnalysis
{
    private readonly BoundFunction _function;
    private readonly TaintAnalysisOptions _options;
    private readonly List<TaintVulnerability> _vulnerabilities = new();
    private readonly Dictionary<string, HashSet<TaintLabel>> _taintedVariables = new();

    /// <summary>
    /// Effect declarations for the function (populated from AST).
    /// These are used to auto-derive sinks from the effect system.
    /// </summary>
    private readonly IReadOnlyList<string> _declaredEffects;

    public TaintAnalysis(BoundFunction function, TaintAnalysisOptions? options = null)
        : this(function, options, Array.Empty<string>())
    {
    }

    public TaintAnalysis(BoundFunction function, TaintAnalysisOptions? options, IReadOnlyList<string> declaredEffects)
    {
        _function = function ?? throw new ArgumentNullException(nameof(function));
        _options = options ?? TaintAnalysisOptions.Default;
        _declaredEffects = declaredEffects ?? Array.Empty<string>();

        Analyze();
    }

    /// <summary>
    /// Gets all detected vulnerabilities.
    /// </summary>
    public IReadOnlyList<TaintVulnerability> Vulnerabilities => _vulnerabilities;

    /// <summary>
    /// Reports vulnerabilities as diagnostics.
    /// </summary>
    public void ReportDiagnostics(DiagnosticBag diagnostics)
    {
        foreach (var vuln in _vulnerabilities)
        {
            diagnostics.Report(
                vuln.SinkLocation,
                vuln.DiagnosticCode,
                vuln.Message,
                vuln.Severity);
        }
    }

    private void Analyze()
    {
        // Initialize taint from function parameters that are known sources
        InitializeParameterTaint();

        // Walk through all statements
        foreach (var stmt in _function.Body)
        {
            AnalyzeStatement(stmt);
        }
    }

    private void InitializeParameterTaint()
    {
        foreach (var param in _function.Symbol.Parameters)
        {
            // Check if parameter is a known taint source based on naming/type conventions
            var source = InferTaintSource(param.Name, param.TypeName);
            if (source != null)
            {
                var label = new TaintLabel(source.Value, param.Name, _function.Span);
                AddTaint(param.Name, label);
            }
        }
    }

    private TaintSource? InferTaintSource(string name, string typeName)
    {
        var lowerName = name.ToLowerInvariant();
        var lowerType = typeName.ToLowerInvariant();

        // User input patterns
        if (_options.TrackUserInput &&
            (lowerName.Contains("input") ||
             lowerName.Contains("request") ||
             lowerName.Contains("query") ||
             lowerName.Contains("param") ||
             lowerName.Contains("arg") ||
             lowerName.Contains("user") ||
             lowerName.Contains("form")))
        {
            return TaintSource.UserInput;
        }

        // File read patterns
        if (_options.TrackFileReads &&
            (lowerName.Contains("file") ||
             lowerName.Contains("content") ||
             lowerName.Contains("data") && lowerName.Contains("read")))
        {
            return TaintSource.FileRead;
        }

        // Environment patterns
        if (_options.TrackEnvironment &&
            (lowerName.Contains("env") ||
             lowerName.Contains("config")))
        {
            return TaintSource.Environment;
        }

        return null;
    }

    private void AnalyzeStatement(BoundStatement stmt)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                AnalyzeBinding(bind);
                break;

            case BoundCallStatement call:
                AnalyzeCall(call.Target, call.Arguments, call.Span);
                break;

            case BoundReturnStatement ret:
                if (ret.Expression != null)
                {
                    AnalyzeExpression(ret.Expression);
                }
                break;

            case BoundIfStatement ifStmt:
                AnalyzeExpression(ifStmt.Condition);
                foreach (var s in ifStmt.ThenBody)
                    AnalyzeStatement(s);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    AnalyzeExpression(elseIf.Condition);
                    foreach (var s in elseIf.Body)
                        AnalyzeStatement(s);
                }
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        AnalyzeStatement(s);
                break;

            case BoundWhileStatement whileStmt:
                AnalyzeExpression(whileStmt.Condition);
                foreach (var s in whileStmt.Body)
                    AnalyzeStatement(s);
                break;

            case BoundForStatement forStmt:
                AnalyzeExpression(forStmt.From);
                AnalyzeExpression(forStmt.To);
                if (forStmt.Step != null)
                    AnalyzeExpression(forStmt.Step);
                foreach (var s in forStmt.Body)
                    AnalyzeStatement(s);
                break;
        }
    }

    private void AnalyzeBinding(BoundBindStatement bind)
    {
        if (bind.Initializer == null)
            return;

        // Check initializer expressions for sink calls (e.g., file.read with tainted args)
        AnalyzeExpression(bind.Initializer);

        // Check if the initializer is a taint source
        var sourceLabels = GetTaintLabelsFromExpression(bind.Initializer);

        // Check if the initializer is a call that introduces taint
        if (bind.Initializer is BoundCallExpression callExpr)
        {
            var newTaint = CheckForTaintSource(callExpr.Target, callExpr.Span);
            if (newTaint != null)
            {
                sourceLabels = sourceLabels.Append(newTaint.Value);
            }
        }

        // Propagate taint to the variable being defined
        foreach (var label in sourceLabels)
        {
            AddTaint(bind.Variable.Name, label);
        }
    }

    private void AnalyzeCall(string target, IReadOnlyList<BoundExpression> arguments, TextSpan span)
    {
        var sink = IdentifySink(target);
        if (sink != null)
        {
            // Check if any argument is tainted
            foreach (var arg in arguments)
            {
                var labels = GetTaintLabelsFromExpression(arg);
                foreach (var label in labels)
                {
                    var argName = GetExpressionName(arg) ?? "expression";
                    _vulnerabilities.Add(new TaintVulnerability(
                        sink.Value,
                        label.Source,
                        label.SourceVariable,
                        label.SourceLocation,
                        argName,
                        span));
                }
            }
        }

        // Also recursively analyze arguments
        foreach (var arg in arguments)
        {
            AnalyzeExpression(arg);
        }
    }

    private void AnalyzeExpression(BoundExpression expr)
    {
        switch (expr)
        {
            case BoundCallExpression callExpr:
                AnalyzeCall(callExpr.Target, callExpr.Arguments, callExpr.Span);
                break;

            case BoundBinaryExpression binExpr:
                AnalyzeExpression(binExpr.Left);
                AnalyzeExpression(binExpr.Right);
                break;

            case BoundUnaryExpression unaryExpr:
                AnalyzeExpression(unaryExpr.Operand);
                break;
        }
    }

    private TaintLabel? CheckForTaintSource(string target, TextSpan location)
    {
        var lowerTarget = target.ToLowerInvariant();

        // User input sources
        if (_options.TrackUserInput &&
            (lowerTarget.Contains("readline") ||
             lowerTarget.Contains("read_input") ||
             lowerTarget.Contains("getinput") ||
             lowerTarget.Contains("prompt") ||
             lowerTarget.Contains("request.get") ||
             lowerTarget.Contains("request.query") ||
             lowerTarget.Contains("request.param") ||
             lowerTarget.Contains("request.body")))
        {
            return new TaintLabel(TaintSource.UserInput, target, location);
        }

        // File read sources
        if (_options.TrackFileReads &&
            (lowerTarget.Contains("file.read") ||
             lowerTarget.Contains("read_file") ||
             lowerTarget.Contains("fs.read") ||
             lowerTarget.Contains("io.read")))
        {
            return new TaintLabel(TaintSource.FileRead, target, location);
        }

        // Network input sources
        if (_options.TrackNetworkInput &&
            (lowerTarget.Contains("http.get") ||
             lowerTarget.Contains("fetch") ||
             lowerTarget.Contains("socket.read") ||
             lowerTarget.Contains("recv")))
        {
            return new TaintLabel(TaintSource.NetworkInput, target, location);
        }

        // Environment sources
        if (_options.TrackEnvironment &&
            (lowerTarget.Contains("env.get") ||
             lowerTarget.Contains("getenv") ||
             lowerTarget.Contains("environment.get")))
        {
            return new TaintLabel(TaintSource.Environment, target, location);
        }

        return null;
    }

    private TaintSink? IdentifySink(string target)
    {
        // First, try to derive sink from declared effects (more precise)
        var effectSink = IdentifySinkFromEffects(target);
        if (effectSink != null)
            return effectSink;

        // Fall back to pattern matching for external/unknown functions
        var lowerTarget = target.ToLowerInvariant();

        // SQL sinks (but not parameterized/prepared queries, which are safe by design)
        if (_options.DetectSqlInjection &&
            (lowerTarget.Contains("sql.execute") ||
             lowerTarget.Contains("sql.query") ||
             lowerTarget.Contains("db.execute") ||
             lowerTarget.Contains("db.query") ||
             lowerTarget.Contains("db.raw") ||
             lowerTarget.Contains("execute_sql")))
        {
            if (lowerTarget.Contains("_param") ||
                lowerTarget.Contains("parameterized") ||
                lowerTarget.Contains("prepared"))
            {
                return null; // Parameterized queries are safe
            }
            return TaintSink.SqlQuery;
        }

        // Command execution sinks
        if (_options.DetectCommandInjection &&
            (lowerTarget.Contains("exec") ||
             lowerTarget.Contains("system") ||
             lowerTarget.Contains("shell") ||
             lowerTarget.Contains("spawn") ||
             lowerTarget.Contains("popen") ||
             lowerTarget.Contains("run_command")))
        {
            return TaintSink.CommandExecution;
        }

        // File path sinks
        if (_options.DetectPathTraversal &&
            (lowerTarget.Contains("file.open") ||
             lowerTarget.Contains("file.read") ||
             lowerTarget.Contains("file.write") ||
             lowerTarget.Contains("file.delete") ||
             lowerTarget.Contains("fs.read") ||
             lowerTarget.Contains("fs.write") ||
             lowerTarget.Contains("path.join")))
        {
            return TaintSink.FilePath;
        }

        // HTML output sinks (XSS)
        if (_options.DetectXss &&
            (lowerTarget.Contains("html.write") ||
             lowerTarget.Contains("response.write") ||
             lowerTarget.Contains("innerhtml") ||
             lowerTarget.Contains("document.write")))
        {
            return TaintSink.HtmlOutput;
        }

        return null;
    }

    /// <summary>
    /// Identifies sinks from declared effects.
    /// This provides more precise sink detection based on the effect system.
    /// Effects come in "category:value" format (e.g., "io:database_write").
    /// </summary>
    private TaintSink? IdentifySinkFromEffects(string target)
    {
        // If no effects are declared, can't use effect-based detection
        if (_declaredEffects.Count == 0)
            return null;

        // Check if the call target matches any effect-derived sinks
        var lowerTarget = target.ToLowerInvariant();

        foreach (var effect in _declaredEffects)
        {
            // Parse "category:value" format from Binder
            var parts = effect.Split(':');
            if (parts.Length != 2)
                continue;

            var category = parts[0];
            var value = parts[1];

            // Map category to EffectKind
            var kind = category switch
            {
                "io" => EffectKind.IO,
                "process" => EffectKind.Process,
                "memory" => EffectKind.Memory,
                "console" => EffectKind.Console,
                _ => EffectKind.IO // Default
            };

            // Use the expanded value to find the sink
            var sink = EffectSinkMapping.MapEffectToSink(kind, value);
            if (sink == null)
                continue;

            // Check if call target matches the effect's resource domain
            if (MatchesEffectResource(lowerTarget, value, sink.Value))
            {
                // Verify the sink type is enabled in options
                if (IsSinkEnabled(sink.Value))
                    return sink;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a call target matches an effect's resource domain.
    /// Uses multiple matching strategies for robustness.
    /// </summary>
    private static bool MatchesEffectResource(string target, string effectValue, TaintSink sink)
    {
        // Strategy 1: Match by known resource prefixes from the effect
        var resourcePrefixes = GetResourcePrefixes(effectValue);
        foreach (var prefix in resourcePrefixes)
        {
            // Match "db.execute", "db_query", "database.run", etc.
            if (target.StartsWith(prefix + ".") ||
                target.StartsWith(prefix + "_") ||
                target == prefix ||
                target.Contains("." + prefix + ".") ||
                target.Contains("." + prefix + "_"))
            {
                return true;
            }
        }

        // Strategy 2: Match by sink-specific keywords
        var keywords = GetSinkKeywords(sink);
        foreach (var keyword in keywords)
        {
            if (target.Contains(keyword))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets resource prefixes for matching call targets to effects.
    /// </summary>
    private static string[] GetResourcePrefixes(string effectValue)
    {
        return effectValue switch
        {
            "database_read" or "database_write" or "database_readwrite"
                => new[] { "db", "database", "sql", "query", "mysql", "postgres", "sqlite", "mongo" },
            "filesystem_read" or "filesystem_write" or "filesystem_readwrite"
                => new[] { "fs", "file", "path", "directory", "dir", "io" },
            "network_read" or "network_write" or "network_readwrite"
                => new[] { "net", "network", "http", "https", "url", "web", "socket", "fetch", "request" },
            "console_read" or "console_write"
                => new[] { "console", "stdout", "stdin", "print", "readline" },
            "environment_read" or "environment_write"
                => new[] { "env", "environment", "getenv", "setenv" },
            "process"
                => new[] { "process", "exec", "spawn", "shell", "system", "cmd", "command", "run" },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Gets keywords specific to each sink type for fallback matching.
    /// </summary>
    private static string[] GetSinkKeywords(TaintSink sink)
    {
        return sink switch
        {
            TaintSink.SqlQuery => new[] { "execute", "query", "select", "insert", "update", "delete", "exec" },
            TaintSink.CommandExecution => new[] { "exec", "spawn", "shell", "system", "run", "command" },
            TaintSink.FilePath => new[] { "open", "read", "write", "create", "delete", "path" },
            TaintSink.UrlRedirect => new[] { "redirect", "navigate", "location", "url", "href" },
            TaintSink.HtmlOutput => new[] { "write", "html", "render", "innerhtml", "response" },
            TaintSink.CodeEval => new[] { "eval", "compile", "execute", "interpret" },
            TaintSink.Deserialization => new[] { "deserialize", "unmarshal", "parse", "load", "decode" },
            TaintSink.LogOutput => new[] { "log", "trace", "debug", "info", "warn", "error" },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Checks if a sink type is enabled in the options.
    /// </summary>
    private bool IsSinkEnabled(TaintSink sink)
    {
        return sink switch
        {
            TaintSink.SqlQuery => _options.DetectSqlInjection,
            TaintSink.CommandExecution => _options.DetectCommandInjection,
            TaintSink.FilePath => _options.DetectPathTraversal,
            TaintSink.HtmlOutput => _options.DetectXss,
            _ => true // Other sinks are always enabled
        };
    }

    private IEnumerable<TaintLabel> GetTaintLabelsFromExpression(BoundExpression expr)
    {
        switch (expr)
        {
            case BoundVariableExpression varExpr:
                if (_taintedVariables.TryGetValue(varExpr.Variable.Name, out var labels))
                {
                    foreach (var label in labels)
                        yield return label;
                }
                break;

            case BoundBinaryExpression binExpr:
                // Taint propagates through operations (conservative)
                foreach (var label in GetTaintLabelsFromExpression(binExpr.Left))
                    yield return label;
                foreach (var label in GetTaintLabelsFromExpression(binExpr.Right))
                    yield return label;
                break;

            case BoundUnaryExpression unaryExpr:
                foreach (var label in GetTaintLabelsFromExpression(unaryExpr.Operand))
                    yield return label;
                break;

            case BoundCallExpression callExpr:
                // Check if call is a sanitizer
                if (IsSanitizer(callExpr.Target))
                {
                    yield break; // Sanitizer removes taint
                }

                // Otherwise, propagate taint from arguments
                foreach (var arg in callExpr.Arguments)
                {
                    foreach (var label in GetTaintLabelsFromExpression(arg))
                        yield return label;
                }
                break;
        }
    }

    private static bool IsSanitizer(string target)
    {
        var lowerTarget = target.ToLowerInvariant();
        return lowerTarget.Contains("escape") ||
               lowerTarget.Contains("sanitize") ||
               lowerTarget.Contains("encode") ||
               lowerTarget.Contains("html_escape") ||
               lowerTarget.Contains("sql_escape") ||
               lowerTarget.Contains("quote") ||
               lowerTarget.Contains("parameterize");
    }

    private void AddTaint(string variableName, TaintLabel label)
    {
        if (!_taintedVariables.TryGetValue(variableName, out var labels))
        {
            labels = new HashSet<TaintLabel>();
            _taintedVariables[variableName] = labels;
        }
        labels.Add(label);
    }

    private static string? GetExpressionName(BoundExpression expr)
    {
        return expr switch
        {
            BoundVariableExpression varExpr => varExpr.Variable.Name,
            _ => null
        };
    }
}

/// <summary>
/// Runner for taint analysis on a module.
/// </summary>
public sealed class TaintAnalysisRunner
{
    private readonly DiagnosticBag _diagnostics;
    private readonly TaintAnalysisOptions _options;

    public TaintAnalysisRunner(DiagnosticBag diagnostics, TaintAnalysisOptions? options = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _options = options ?? TaintAnalysisOptions.Default;
    }

    /// <summary>
    /// Runs taint analysis on a bound module.
    /// </summary>
    public void Analyze(BoundModule module)
    {
        foreach (var function in module.Functions)
        {
            AnalyzeFunction(function);
        }
    }

    /// <summary>
    /// Runs taint analysis on a single function.
    /// </summary>
    public void AnalyzeFunction(BoundFunction function)
    {
        // Pass the function's declared effects to enable effect-based sink detection
        var analysis = new TaintAnalysis(function, _options, function.DeclaredEffects);
        analysis.ReportDiagnostics(_diagnostics);
    }
}
