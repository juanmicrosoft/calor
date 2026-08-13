using System.Runtime.CompilerServices;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis.Security;

/// <summary>Represents a taint source category.</summary>
public enum TaintSource
{
    UserInput,
    FileRead,
    NetworkInput,
    Environment,
    DatabaseResult,
    ExternalApi,
}

/// <summary>Represents a security-sensitive sink category.</summary>
public enum TaintSink
{
    SqlQuery,
    CommandExecution,
    FilePath,
    HtmlOutput,
    UrlRedirect,
    CodeEval,
    Deserialization,
    LogOutput,
}

/// <summary>One reproducible step in a source-to-sink taint flow.</summary>
public sealed record TaintFlowStep(string Description, TextSpan Location);

/// <summary>Represents a taint label attached to a value.</summary>
public readonly record struct TaintLabel(
    TaintSource Source,
    string SourceVariable,
    TextSpan SourceLocation,
    int Hops = 1);

/// <summary>Describes an exact external or resolved call identity.</summary>
public sealed record TaintCallIdentity(
    string? Target = null,
    string? TypeName = null,
    string? MethodName = null,
    IReadOnlyList<string>? ParameterTypes = null,
    SymbolId? FunctionSymbolId = null)
{
    internal bool Matches(TaintAnalysis.TaintCallDescriptor call)
    {
        if (FunctionSymbolId is { } symbolId)
        {
            return call.ResolvedSymbols.Any(symbol =>
                !symbol.Id.IsNone && symbol.Id == symbolId);
        }

        if (TypeName != null || MethodName != null || ParameterTypes != null)
        {
            if (!string.Equals(TypeName, call.ResolvedTypeName, StringComparison.Ordinal)
                || !string.Equals(MethodName, call.ResolvedMethodName, StringComparison.Ordinal))
            {
                return false;
            }

            return ParameterTypes == null
                || call.ResolvedParameterTypes != null
                    && ParameterTypes.SequenceEqual(
                        call.ResolvedParameterTypes,
                        StringComparer.Ordinal);
        }

        return !call.HasResolvedIdentity
            && Target != null
            && string.Equals(Target, call.Target, StringComparison.Ordinal);
    }
}

/// <summary>Declares an exact call-return source.</summary>
public sealed record TaintSourceRule(TaintCallIdentity Identity, TaintSource Source);

/// <summary>Declares an exact call sink and, when specified, its sink argument positions.</summary>
public sealed class TaintSinkRule
{
    public TaintCallIdentity Identity { get; }
    public TaintSink Sink { get; }
    public IReadOnlyList<int>? ArgumentIndices { get; }

    public TaintSinkRule(
        TaintCallIdentity identity,
        TaintSink sink,
        params int[] argumentIndices)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Sink = sink;
        ArgumentIndices = argumentIndices.Length == 0
            ? null
            : argumentIndices.Distinct().Order().ToArray();
    }

    internal IEnumerable<int> GetArgumentIndices(int argumentCount) =>
        ArgumentIndices ?? Enumerable.Range(0, argumentCount);
}

/// <summary>Declares the sink kinds removed from a sanitizer's return value.</summary>
public sealed class TaintSanitizerRule
{
    public TaintCallIdentity Identity { get; }
    public IReadOnlyList<TaintSink> Sanitizes { get; }

    public TaintSanitizerRule(TaintCallIdentity identity, params TaintSink[] sanitizes)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Sanitizes = sanitizes?.Distinct().ToArray()
            ?? throw new ArgumentNullException(nameof(sanitizes));
    }
}

/// <summary>Represents a detected security vulnerability.</summary>
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
    /// <summary>Ordered source-to-sink evidence retained by the CFG analysis.</summary>
    public IReadOnlyList<TaintFlowStep> ProvenancePath { get; }
    public int HopCount => Math.Max(0, ProvenancePath.Count - 1);

    public TaintVulnerability(
        TaintSink sink,
        TaintSource source,
        string sourceVariable,
        TextSpan sourceLocation,
        string sinkVariable,
        TextSpan sinkLocation,
        IReadOnlyList<TaintFlowStep>? provenancePath = null)
    {
        Sink = sink;
        Source = source;
        SourceVariable = sourceVariable;
        SourceLocation = sourceLocation;
        SinkVariable = sinkVariable;
        SinkLocation = sinkLocation;
        ProvenancePath = provenancePath
            ?? [
                new TaintFlowStep($"source {sourceVariable}", sourceLocation),
                new TaintFlowStep($"sink {sinkVariable}", sinkLocation),
            ];

        (DiagnosticCode, var message, Severity) = GetDiagnosticInfo(sink, source);
        Message = ProvenancePath.Count > 0
            ? $"{message}; path: {string.Join(" -> ", ProvenancePath.Select(step => step.Description))}"
            : message;
    }

    private static (string Code, string Message, DiagnosticSeverity Severity) GetDiagnosticInfo(
        TaintSink sink, TaintSource source) =>
        sink switch
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
                DiagnosticSeverity.Warning),
        };
}

/// <summary>Options for symbol/access-path based taint analysis.</summary>
public sealed class TaintAnalysisOptions
{
    public bool TrackUserInput { get; init; } = true;
    public bool TrackFileReads { get; init; } = true;
    public bool TrackNetworkInput { get; init; } = true;
    public bool TrackEnvironment { get; init; } = true;
    public bool DetectSqlInjection { get; init; } = true;
    public bool DetectCommandInjection { get; init; } = true;
    public bool DetectPathTraversal { get; init; } = true;
    public bool DetectXss { get; init; } = true;

    /// <summary>
    /// Retained for API compatibility. Hop count ranks evidence only; it never suppresses
    /// a direct source-to-sink vulnerability.
    /// </summary>
    public int MinTaintHops { get; init; } = 1;

    /// <summary>
    /// Treat an unresolved external call as potentially returning externally controlled
    /// data. Its arguments still propagate in either mode.
    /// </summary>
    public bool StrictExternalCalls { get; init; }

    /// <summary>
    /// Exact parameter names intentionally treated as API-boundary user input. This is
    /// an explicit compatibility manifest, not substring matching.
    /// </summary>
    public IReadOnlyCollection<string> UserInputParameterNames { get; init; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "input", "user_input", "user_data", "user_path", "user",
            "request", "request_data", "request_body", "request_query",
            "query_string", "form", "form_data",
        };

    public IReadOnlyList<TaintSourceRule> AdditionalSources { get; init; } = [];
    public IReadOnlyList<TaintSinkRule> AdditionalSinks { get; init; } = [];
    public IReadOnlyList<TaintSanitizerRule> AdditionalSanitizers { get; init; } = [];

    public static TaintAnalysisOptions Default => new();
}

/// <summary>
/// A forward CFG may-taint analysis. Facts are keyed by stable symbol roots plus
/// field/index access paths; joins union facts and assignments perform strong updates.
/// </summary>
public sealed class TaintAnalysis
{
    private static readonly IReadOnlyList<TaintSourceRule> BuiltInSources =
    [
        new(new("Console.ReadLine"), TaintSource.UserInput),
        new(new("System.Console.ReadLine"), TaintSource.UserInput),
        new(new(TypeName: "System.Console", MethodName: "ReadLine"), TaintSource.UserInput),
        new(new("file.read"), TaintSource.FileRead),
        new(new("File.ReadAllText"), TaintSource.FileRead),
        new(new(TypeName: "System.IO.File", MethodName: "ReadAllText"), TaintSource.FileRead),
        new(new("http.get"), TaintSource.NetworkInput),
        new(new("fetch"), TaintSource.NetworkInput),
        new(new("Environment.GetEnvironmentVariable"), TaintSource.Environment),
        new(new(TypeName: "System.Environment", MethodName: "GetEnvironmentVariable"), TaintSource.Environment),
    ];

    private static readonly IReadOnlyList<TaintSinkRule> BuiltInSinks =
    [
        new(new("db.execute"), TaintSink.SqlQuery),
        new(new("db.query"), TaintSink.SqlQuery),
        new(new("db.raw"), TaintSink.SqlQuery),
        new(new("db.execute_with_param_logging"), TaintSink.SqlQuery),
        new(new("sql.execute"), TaintSink.SqlQuery),
        new(new("sql.query"), TaintSink.SqlQuery),
        new(new("ExecuteSql"), TaintSink.SqlQuery),
        new(new("shell"), TaintSink.CommandExecution),
        new(new("exec"), TaintSink.CommandExecution),
        new(new("system"), TaintSink.CommandExecution),
        new(new("Process.Start"), TaintSink.CommandExecution),
        new(new(
            TypeName: "System.Diagnostics.Process",
            MethodName: "Start",
            ParameterTypes: ["STRING"]), TaintSink.CommandExecution),
        new(new(
            TypeName: "System.Diagnostics.Process",
            MethodName: "Start",
            ParameterTypes: ["STRING", "STRING"]), TaintSink.CommandExecution),
        new(new(
            TypeName: "System.Diagnostics.Process",
            MethodName: "Start",
            ParameterTypes: ["System.String"]), TaintSink.CommandExecution),
        new(new(
            TypeName: "System.Diagnostics.Process",
            MethodName: "Start",
            ParameterTypes: ["System.String", "System.String"]), TaintSink.CommandExecution),
        new(new("file.open"), TaintSink.FilePath, 0),
        new(new("file.read"), TaintSink.FilePath, 0),
        new(new("file.write"), TaintSink.FilePath, 0),
        new(new("file.delete"), TaintSink.FilePath, 0),
        new(new("file.move"), TaintSink.FilePath, 0, 1),
        new(new(TypeName: "db", MethodName: "execute"), TaintSink.SqlQuery),
        new(new(TypeName: "db", MethodName: "query"), TaintSink.SqlQuery),
        new(new(TypeName: "db", MethodName: "raw"), TaintSink.SqlQuery),
        new(new(TypeName: "db", MethodName: "execute_with_param_logging"), TaintSink.SqlQuery),
        new(new(TypeName: "sql", MethodName: "execute"), TaintSink.SqlQuery),
        new(new(TypeName: "sql", MethodName: "query"), TaintSink.SqlQuery),
        new(new(TypeName: "file", MethodName: "open"), TaintSink.FilePath, 0),
        new(new(TypeName: "file", MethodName: "read"), TaintSink.FilePath, 0),
        new(new(TypeName: "file", MethodName: "write"), TaintSink.FilePath, 0),
        new(new(TypeName: "file", MethodName: "delete"), TaintSink.FilePath, 0),
        new(new(TypeName: "file", MethodName: "move"), TaintSink.FilePath, 0, 1),
        new(new(TypeName: "html", MethodName: "write"), TaintSink.HtmlOutput),
        new(new(TypeName: "response", MethodName: "write"), TaintSink.HtmlOutput),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Open",
            ParameterTypes: ["STRING", "System.IO.FileMode"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Open",
            ParameterTypes: ["System.String", "System.IO.FileMode"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Open",
            ParameterTypes: ["System.String", "System.IO.FileMode", "System.IO.FileAccess"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Open",
            ParameterTypes:
                ["System.String", "System.IO.FileMode", "System.IO.FileAccess", "System.IO.FileShare"]),
            TaintSink.FilePath,
            0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "ReadAllText",
            ParameterTypes: ["STRING"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "ReadAllText",
            ParameterTypes: ["System.String"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "ReadAllText",
            ParameterTypes: ["STRING", "System.Text.Encoding"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "ReadAllText",
            ParameterTypes: ["System.String", "System.Text.Encoding"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Delete",
            ParameterTypes: ["STRING"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Delete",
            ParameterTypes: ["System.String"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Move",
            ParameterTypes: ["STRING", "STRING"]), TaintSink.FilePath, 0, 1),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Move",
            ParameterTypes: ["System.String", "System.String"]), TaintSink.FilePath, 0, 1),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Move",
            ParameterTypes: ["STRING", "STRING", "BOOL"]), TaintSink.FilePath, 0, 1),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "Move",
            ParameterTypes: ["System.String", "System.String", "System.Boolean"]),
            TaintSink.FilePath,
            0,
            1),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "WriteAllText",
            ParameterTypes: ["STRING", "STRING"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "WriteAllText",
            ParameterTypes: ["System.String", "System.String"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "WriteAllText",
            ParameterTypes: ["System.String", "System.String", "System.Text.Encoding"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "WriteAllBytes",
            ParameterTypes: ["STRING", "BYTE[]"]), TaintSink.FilePath, 0),
        new(new(
            TypeName: "System.IO.File",
            MethodName: "WriteAllBytes",
            ParameterTypes: ["System.String", "System.Byte[]"]), TaintSink.FilePath, 0),
        new(new(TypeName: "System.IO.File", MethodName: "open"), TaintSink.FilePath, 0),
        new(new(TypeName: "System.IO.File", MethodName: "read"), TaintSink.FilePath, 0),
        new(new(TypeName: "System.IO.File", MethodName: "write"), TaintSink.FilePath, 0),
        new(new(TypeName: "System.IO.File", MethodName: "delete"), TaintSink.FilePath, 0),
        new(new("html.write"), TaintSink.HtmlOutput),
        new(new("response.write"), TaintSink.HtmlOutput),
        new(new("document.write"), TaintSink.HtmlOutput),
    ];

    private static readonly IReadOnlyList<TaintSanitizerRule> BuiltInSanitizers =
    [
        new(new("sql_escape"), TaintSink.SqlQuery),
        new(new("SqlEscape"), TaintSink.SqlQuery),
        new(new("sql.parameterize"), TaintSink.SqlQuery),
        new(new("html_escape"), TaintSink.HtmlOutput),
        new(new("HtmlEncode"), TaintSink.HtmlOutput),
        new(new(TypeName: "System.Net.WebUtility", MethodName: "HtmlEncode"), TaintSink.HtmlOutput),
        new(new("sanitize"), TaintSink.SqlQuery, TaintSink.HtmlOutput),
    ];

    private readonly BoundFunction _function;
    private readonly TaintAnalysisOptions _options;
    private readonly IReadOnlyList<string> _declaredEffects;
    private readonly IReadOnlyDictionary<SymbolId, TaintFunctionSummary> _summaries;
    private readonly bool _symbolicParameters;
    private readonly List<TaintVulnerability> _vulnerabilities = new();
    private readonly HashSet<TaintFindingKey> _reportedFindings = new();

    public IReadOnlyList<BoundNode> IncompleteNodes { get; }
    public bool IsComplete => IncompleteNodes.Count == 0;
    public IReadOnlyList<TaintVulnerability> Vulnerabilities => _vulnerabilities;
    public ControlFlowGraph Cfg { get; }
    internal DataflowAnalysisResult<TaintState> DataflowResult { get; }

    public TaintAnalysis(BoundFunction function, TaintAnalysisOptions? options = null)
        : this(function, options, Array.Empty<string>(), null, symbolicParameters: false)
    {
    }

    public TaintAnalysis(BoundFunction function, TaintAnalysisOptions? options, IReadOnlyList<string> declaredEffects)
        : this(function, options, declaredEffects, null, symbolicParameters: false)
    {
    }

    internal TaintAnalysis(
        BoundFunction function,
        TaintAnalysisOptions? options,
        IReadOnlyList<string> declaredEffects,
        IReadOnlyDictionary<SymbolId, TaintFunctionSummary>? summaries,
        bool symbolicParameters)
    {
        _function = function ?? throw new ArgumentNullException(nameof(function));
        _options = options ?? TaintAnalysisOptions.Default;
        _declaredEffects = declaredEffects ?? Array.Empty<string>();
        _summaries = summaries ?? new Dictionary<SymbolId, TaintFunctionSummary>();
        _symbolicParameters = symbolicParameters;
        IncompleteNodes = BoundNodeHelpers.GetAnalysisIncompleteNodes(function).ToArray();
        Cfg = ControlFlowGraph.Build(function);

        var initial = SeedParameterFacts();
        var analysis = new DataflowAnalysis<TaintState>(
            new TaintStateLattice(),
            new TaintTransfer(this),
            DataflowDirection.Forward,
            initial,
            TaintState.Empty,
            TaintState.Empty);
        DataflowResult = analysis.AnalyzeWithMetadata(Cfg);
        if (!symbolicParameters)
            CollectFindings();
    }

    /// <summary>Reports all source-to-sink findings as diagnostics.</summary>
    public void ReportDiagnostics(DiagnosticBag diagnostics)
    {
        foreach (var vulnerability in _vulnerabilities)
        {
            diagnostics.Report(
                vulnerability.SinkLocation,
                vulnerability.DiagnosticCode,
                vulnerability.Message,
                vulnerability.Severity);
        }
    }

    internal TaintFunctionSummary CreateSummary()
    {
        var returns = new Dictionary<TaintFlowIdentity, TaintFlow>();
        var sinks = new Dictionary<TaintSummarySinkKey, TaintSummarySink>();
        var returnExpressions = BoundNodeHelpers.DescendantsAndSelf(_function)
            .OfType<BoundReturnStatement>()
            .Where(statement => statement.Expression != null)
            .Select(statement => statement.Expression!)
            .ToHashSet();

        foreach (var block in Cfg.ReachableBlocks)
        {
            var state = DataflowResult.Blocks[block].In;
            for (var statementIndex = 0; statementIndex < block.Statements.Count; statementIndex++)
            {
                var statement = block.Statements[statementIndex];
                RecordSummarySinks(statement, state, sinks);
                state = TransferStatement(
                    block,
                    statementIndex,
                    statement,
                    state,
                    applyDeferredDefinition: !block.IsDefinitionDeferred(statementIndex));

                if (statement is BoundReturnStatement { Expression: { } expression })
                {
                    foreach (var flow in EvaluateExpression(expression, state))
                        AddShortest(returns, flow);
                }
            }

            foreach (var operation in block.SyntheticOperations)
            {
                // CFG construction moves call-containing return expressions into a
                // synthetic evaluation block so exceptional edges are explicit.
                // Retain their return summary rather than losing the value flow.
                if (operation.Expression != null && returnExpressions.Contains(operation.Expression))
                {
                    foreach (var flow in EvaluateExpression(operation.Expression, state))
                        AddShortest(returns, flow);
                }
                state = TransferSynthetic(operation, state);
            }
        }

        return new TaintFunctionSummary(
            returns.Values.OrderBy(flow => flow.Origin.Name, StringComparer.Ordinal).ToArray(),
            sinks.Values.OrderBy(sink => sink.Location.Start).ToArray());
    }

    private TaintState SeedParameterFacts()
    {
        var state = TaintState.Empty;
        for (var index = 0; index < _function.Symbol.Parameters.Count; index++)
        {
            var parameter = _function.Symbol.Parameters[index];
            var path = TaintAccessPath.For(parameter);
            if (IsReferenceLike(parameter.TypeName))
            {
                state = state.InitializeReference(path.Root, parameter.DeclarationSpan);
                path = path with { Root = state.GetAliasRoots(path.Root).Single() };
            }
            TaintFlow? flow = _symbolicParameters
                ? TaintFlow.ForParameter(index, parameter.Name, parameter.DeclarationSpan)
                : InferParameterSource(parameter);
            if (flow != null)
                state = state.StrongUpdate(path, [flow]);
        }

        return state;
    }

    private TaintFlow? InferParameterSource(VariableSymbol parameter)
    {
        if (_options.TrackUserInput
            && _options.UserInputParameterNames.Contains(parameter.Name, StringComparer.Ordinal))
        {
            return TaintFlow.ForSource(TaintSource.UserInput, parameter.Name, parameter.DeclarationSpan);
        }

        return null;
    }

    private TaintState TransferStatement(
        BasicBlock? block,
        int statementIndex,
        BoundStatement statement,
        TaintState input,
        bool applyDeferredDefinition = true)
    {
        switch (statement)
        {
            case BoundBindStatement bind when bind.Initializer == null:
                return IsReferenceLike(bind.Variable.TypeName)
                    ? input.InitializeReference(TaintAccessPath.For(bind.Variable).Root, bind.Span)
                    : input;

            case BoundBindStatement bind when bind.Initializer != null:
                return applyDeferredDefinition
                    ? Assign(bind.Variable, bind.Initializer, input, bind.Span)
                    : input;

            case BoundAssignmentStatement
                {
                    Target: BoundVariableExpression target,
                } assignment:
                return applyDeferredDefinition
                    ? Assign(target.Variable, assignment.Value, input, assignment.Span)
                    : input;

            case BoundAssignmentStatement assignment:
                return applyDeferredDefinition
                    ? Assign(assignment.Target, assignment.Value, input, assignment.Span)
                    : input;

            case BoundCompoundAssignment compound:
                if (!applyDeferredDefinition)
                    return input;
                var compoundFlows = EvaluateExpression(compound.Target, input)
                    .Concat(EvaluateExpression(compound.Value, input));
                return WriteTarget(compound.Target, compoundFlows, input, compound.Span);

            default:
                return input;
        }
    }

    private TaintState TransferSynthetic(SyntheticOperation operation, TaintState input)
    {
        return operation.Kind switch
        {
            SyntheticOperationKind.ForInitialization or SyntheticOperationKind.ForStep
                when operation.DefinedVariable != null && operation.Expression != null =>
                Assign(operation.DefinedVariable, operation.Expression, input, operation.Span),

            SyntheticOperationKind.ForeachCollectionEvaluation when operation.Expression != null =>
                input.SetForeachFlows(operation.Span, EvaluateExpression(operation.Expression, input)),

            SyntheticOperationKind.ForeachIteration when operation.DefinedVariable != null =>
                input.StrongUpdate(
                    TaintAccessPath.For(operation.DefinedVariable),
                    input.GetForeachFlows(operation.Span)
                        .Select(flow => flow.WithStep("foreach element", operation.Span))),

            SyntheticOperationKind.UsingResourceInitialization
                when operation.DefinedVariable != null && operation.Expression != null =>
                Assign(operation.DefinedVariable, operation.Expression, input, operation.Span),

            SyntheticOperationKind.StatementDefinition when operation.SourceStatement != null =>
                TransferStatement(null, -1, operation.SourceStatement, input),

            _ => input,
        };
    }

    private TaintState Assign(
        VariableSymbol target,
        BoundExpression value,
        TaintState input,
        TextSpan location)
    {
        var flows = EvaluateExpression(value, input);
        var targetPath = TaintAccessPath.For(target);
        var propagated = AddPropagation(flows, $"assign {target.Name}", location);

        if (!IsReferenceLike(target.TypeName))
        {
            return input.WithoutAlias(targetPath.Root)
                .StrongUpdate(targetPath, propagated);
        }

        // A reference assignment snapshots the source's current abstract object
        // pointees. Rebinding the source variable later therefore cannot retarget
        // aliases that still refer to its old object.
        if (value is BoundVariableExpression source && IsReferenceLike(source.Variable.TypeName))
        {
            return input.WithAliases(
                    targetPath.Root,
                    input.GetAliasRoots(TaintAccessPath.For(source.Variable).Root))
                .StrongUpdate(targetPath, Array.Empty<TaintFlow>());
        }

        if (HasStableObjectIdentity(target.TypeName)
            && HasStableObjectIdentity(value.TypeName)
            && TryGetAccessPath(value, input, out var sourcePath))
        {
            var (referenceState, targets) = input.GetOrCreateReferenceTargets(sourcePath);
            return referenceState.WithAliases(targetPath.Root, targets)
                .StrongUpdate(targetPath, Array.Empty<TaintFlow>());
        }

        var state = input.NewReference(targetPath.Root, location)
            .StrongUpdate(targetPath, Array.Empty<TaintFlow>());
        var objectPath = targetPath with { Root = state.GetAliasRoots(targetPath.Root).Single() };
        return state.StrongUpdate(objectPath, propagated);
    }

    private TaintState Assign(
        BoundExpression target,
        BoundExpression value,
        TaintState input,
        TextSpan location)
    {
        if (HasStableObjectIdentity(target.TypeName)
            && HasStableObjectIdentity(value.TypeName)
            && TryGetAccessPath(value, input, out var sourcePath)
            && TryGetAccessPath(target, input, out var targetPath))
        {
            var (state, targets) = input.GetOrCreateReferenceTargets(sourcePath);
            var storagePaths = state.ResolveStoragePaths(targetPath).Distinct().ToArray();
            return state.WithReferenceValues(
                storagePaths,
                targets,
                storagePaths.Length == 1 && !HasWildcardIndex(storagePaths[0]));
        }

        return WriteTarget(target, EvaluateExpression(value, input), input, location);
    }

    private TaintState WriteTarget(
        BoundExpression target,
        IEnumerable<TaintFlow> flows,
        TaintState input,
        TextSpan location)
    {
        if (!TryGetAccessPath(target, input, out var rawPath))
            return input;

        var propagated = AddPropagation(flows, $"assign {rawPath.DisplayName}", location);
        var state = input;
        if (target is BoundVariableExpression)
            state = state.WithoutAlias(rawPath.Root);

        var targets = state.ResolveStoragePaths(rawPath).Distinct().ToArray();
        return targets.Length == 1 && !HasWildcardIndex(targets[0])
            ? state.StrongUpdate(targets[0], propagated)
            : state.WeakUpdate(targets, propagated);
    }

    private IEnumerable<TaintFlow> EvaluateExpression(BoundExpression expression, TaintState state)
    {
        if (TryGetAccessPath(expression, state, out var path))
            return state.Get(path);

        switch (expression)
        {
            case BoundCallExpression call:
                return EvaluateCall(TaintCallDescriptor.From(call), call.Arguments, call.Span, state);

            case BoundExpressionCall expressionCall:
                return expressionCall.Arguments.SelectMany(argument => EvaluateExpression(argument, state));

            default:
                return expression.Children.SelectMany(child => EvaluateExpression(child, state));
        }
    }

    private IEnumerable<TaintFlow> EvaluateCall(
        TaintCallDescriptor call,
        IReadOnlyList<BoundExpression> arguments,
        TextSpan location,
        TaintState state)
    {
        var argumentFlows = arguments
            .SelectMany(argument => EvaluateExpression(argument, state))
            .ToArray();
        var source = IdentifySource(call);
        if (source != null)
            return [TaintFlow.ForSource(source.Value, call.DisplayName, location)];

        var sanitizer = IdentifySanitizer(call);
        if (sanitizer != null)
        {
            var sanitized = sanitizer.Sanitizes.Aggregate(
                TaintSinkMask.None,
                (mask, sink) => mask | TaintSinkMaskExtensions.For(sink));
            return argumentFlows.Select(flow =>
                flow.WithSanitizedSinks(sanitized).WithStep($"sanitize {call.DisplayName}", location));
        }

        var summaries = GetSummaries(call).ToArray();
        if (summaries.Length > 0)
        {
            return summaries.SelectMany(summary =>
                summary.ReturnFlows.SelectMany(flow =>
                    flow.Origin.ParameterIndex is { } index && index < arguments.Count
                        ? EvaluateExpression(arguments[index], state)
                            .Select(argument => argument.WithSummaryFlow(flow, call.DisplayName, location))
                        : flow.Origin.Source is { }
                            ? [flow.WithSummaryReturn(call.DisplayName, location)]
                            : Array.Empty<TaintFlow>())
                    .Where(flow => flow.Origin.ParameterIndex == null
                        || flow.Origin.ParameterIndex < arguments.Count));
        }

        if (_options.StrictExternalCalls && call.ResolvedSymbols.Count == 0)
        {
            return [
                .. argumentFlows,
                TaintFlow.ForSource(TaintSource.ExternalApi, call.DisplayName, location),
            ];
        }

        return argumentFlows;
    }

    private IEnumerable<TaintFunctionSummary> GetSummaries(TaintCallDescriptor call) =>
        call.ResolvedSymbols
            .Where(symbol => !symbol.Id.IsNone)
            .Select(symbol => _summaries.TryGetValue(symbol.Id, out var summary) ? summary : null)
            .Where(summary => summary != null)
            .Cast<TaintFunctionSummary>()
            .Distinct();

    private TaintSource? IdentifySource(TaintCallDescriptor call)
    {
        foreach (var rule in BuiltInSources.Concat(_options.AdditionalSources))
        {
            if (!rule.Identity.Matches(call))
                continue;

            return IsSourceEnabled(rule.Source) ? rule.Source : null;
        }

        return null;
    }

    private TaintSinkRule? IdentifySink(TaintCallDescriptor call)
    {
        foreach (var rule in BuiltInSinks.Concat(_options.AdditionalSinks))
        {
            if (rule.Identity.Matches(call) && IsSinkEnabled(rule.Sink))
                return rule;
        }

        return null;
    }

    private TaintSanitizerRule? IdentifySanitizer(TaintCallDescriptor call) =>
        BuiltInSanitizers.Concat(_options.AdditionalSanitizers)
            .FirstOrDefault(rule => rule.Identity.Matches(call));

    private bool IsSourceEnabled(TaintSource source) => source switch
    {
        TaintSource.UserInput => _options.TrackUserInput,
        TaintSource.FileRead => _options.TrackFileReads,
        TaintSource.NetworkInput => _options.TrackNetworkInput,
        TaintSource.Environment => _options.TrackEnvironment,
        _ => true,
    };

    private bool IsSinkEnabled(TaintSink sink) => sink switch
    {
        TaintSink.SqlQuery => _options.DetectSqlInjection,
        TaintSink.CommandExecution => _options.DetectCommandInjection,
        TaintSink.FilePath => _options.DetectPathTraversal,
        TaintSink.HtmlOutput => _options.DetectXss,
        _ => true,
    };

    private void CollectFindings()
    {
        foreach (var block in Cfg.ReachableBlocks)
        {
            var state = DataflowResult.Blocks[block].In;
            for (var statementIndex = 0; statementIndex < block.Statements.Count; statementIndex++)
            {
                var statement = block.Statements[statementIndex];
                InspectStatement(statement, state);
                state = TransferStatement(
                    block,
                    statementIndex,
                    statement,
                    state,
                    applyDeferredDefinition: !block.IsDefinitionDeferred(statementIndex));
            }

            foreach (var operation in block.SyntheticOperations)
            {
                if (operation.Expression != null)
                    InspectExpression(operation.Expression, state);
                state = TransferSynthetic(operation, state);
            }

            if (block.Terminator.Condition != null)
                InspectExpression(block.Terminator.Condition, state);
        }
    }

    private void InspectStatement(BoundStatement statement, TaintState state)
    {
        switch (statement)
        {
            case BoundCallStatement call:
                InspectCall(TaintCallDescriptor.From(call), call.Arguments, call.Span, state);
                break;
            default:
                foreach (var expression in BoundNodeHelpers.GetImmediateExpressions(statement))
                    InspectExpression(expression, state);
                break;
        }
    }

    private void InspectExpression(BoundExpression expression, TaintState state)
    {
        if (expression is BoundCallExpression call)
            InspectCall(TaintCallDescriptor.From(call), call.Arguments, call.Span, state);

        foreach (var child in expression.Children)
            InspectExpression(child, state);
    }

    private void InspectCall(
        TaintCallDescriptor call,
        IReadOnlyList<BoundExpression> arguments,
        TextSpan location,
        TaintState state)
    {
        var sink = IdentifySink(call);
        if (sink != null)
        {
            RecordSinkFlows(
                sink.Sink,
                SelectSinkArguments(sink, arguments),
                call.DisplayName,
                location,
                state);
        }

        foreach (var summary in GetSummaries(call))
        {
            foreach (var summarySink in summary.ParameterSinks)
            {
                if (summarySink.ParameterIndex >= arguments.Count)
                    continue;
                RecordSinkFlows(
                    summarySink.Sink,
                    [arguments[summarySink.ParameterIndex]],
                    summarySink.Target,
                    summarySink.Location,
                    state,
                    summarySink.ProvenancePath,
                    call.DisplayName);
            }
        }
    }

    private void RecordSummarySinks(
        BoundStatement statement,
        TaintState state,
        Dictionary<TaintSummarySinkKey, TaintSummarySink> sinks)
    {
        IEnumerable<(TaintCallDescriptor Call, IReadOnlyList<BoundExpression> Arguments, TextSpan Location)> calls =
            statement switch
            {
                BoundCallStatement call =>
                [
                    (TaintCallDescriptor.From(call), call.Arguments, call.Span),
                ],
                _ => BoundNodeHelpers.GetImmediateExpressions(statement)
                    .SelectMany(FindCalls)
                    .Select(call => (TaintCallDescriptor.From(call), call.Arguments, call.Span)),
            };

        foreach (var (call, arguments, location) in calls)
        {
            var sink = IdentifySink(call);
            if (sink == null)
                continue;
            foreach (var index in sink.GetArgumentIndices(arguments.Count))
            {
                if (index < 0 || index >= arguments.Count)
                    continue;
                foreach (var flow in EvaluateExpression(arguments[index], state))
                {
                    if (flow.Origin.ParameterIndex is not { } parameterIndex
                        || flow.IsSanitizedFor(sink.Sink))
                    {
                        continue;
                    }

                    var summarySink = new TaintSummarySink(
                        sink.Sink,
                        parameterIndex,
                        call.DisplayName,
                        location,
                        flow.Path);
                    sinks.TryAdd(
                        new TaintSummarySinkKey(sink.Sink, parameterIndex, location),
                        summarySink);
                }
            }
        }
    }

    private static IReadOnlyList<BoundExpression> SelectSinkArguments(
        TaintSinkRule sink,
        IReadOnlyList<BoundExpression> arguments) =>
        sink.GetArgumentIndices(arguments.Count)
            .Where(index => index >= 0 && index < arguments.Count)
            .Select(index => arguments[index])
            .ToArray();

    private static IEnumerable<BoundCallExpression> FindCalls(BoundExpression expression)
    {
        if (expression is BoundCallExpression call)
            yield return call;
        foreach (var child in expression.Children)
        {
            foreach (var nested in FindCalls(child))
                yield return nested;
        }
    }

    private void RecordSinkFlows(
        TaintSink sink,
        IReadOnlyList<BoundExpression> arguments,
        string sinkName,
        TextSpan sinkLocation,
        TaintState state,
        IReadOnlyList<TaintFlowStep>? calleeProvenance = null,
        string? calleeName = null)
    {
        foreach (var argument in arguments)
        {
            var sinkVariable = GetExpressionName(argument) ?? sinkName;
            foreach (var flow in EvaluateExpression(argument, state))
            {
                if (flow.Origin.Source is not { } source || flow.IsSanitizedFor(sink))
                    continue;

                var sourceName = flow.Origin.Name;
                var key = new TaintFindingKey(
                    sink,
                    source,
                    sourceName,
                    flow.Origin.Location,
                    sinkVariable,
                    sinkLocation);
                if (!_reportedFindings.Add(key))
                    continue;

                var provenance = BuildFindingProvenance(
                    flow,
                    sinkName,
                    sinkVariable,
                    sinkLocation,
                    calleeProvenance,
                    calleeName);
                _vulnerabilities.Add(new TaintVulnerability(
                    sink,
                    source,
                    sourceName,
                    flow.Origin.Location,
                    sinkVariable,
                    sinkLocation,
                    provenance));
            }
        }
    }

    private static IEnumerable<TaintFlow> AddPropagation(
        IEnumerable<TaintFlow> flows,
        string description,
        TextSpan location) =>
        flows.Select(flow => flow.WithStep(description, location));

    private static IReadOnlyList<TaintFlowStep> BuildFindingProvenance(
        TaintFlow flow,
        string sinkName,
        string sinkVariable,
        TextSpan sinkLocation,
        IReadOnlyList<TaintFlowStep>? calleeProvenance,
        string? calleeName)
    {
        var provenance = flow.Path.ToList();
        if (calleeProvenance != null)
        {
            provenance.Add(new TaintFlowStep($"call {calleeName}", sinkLocation));
            provenance.AddRange(calleeProvenance.Skip(1));
        }
        provenance.Add(new TaintFlowStep($"sink {sinkName}({sinkVariable})", sinkLocation));
        return provenance;
    }

    private static string? GetExpressionName(BoundExpression expression) => expression switch
    {
        BoundVariableExpression variable => variable.Variable.Name,
        BoundFieldAccessExpression field => field.FieldName,
        BoundArrayAccess or BoundArrayAccessExpression or BoundMultiDimArrayAccess => "element",
        _ => null,
    };

    private static bool IsReferenceLike(string typeName) =>
        typeName.EndsWith("[]", StringComparison.Ordinal)
        || typeName.Contains('<', StringComparison.Ordinal)
        || typeName is "OBJECT" or "STRING"
        || (!IsPrimitiveValueType(typeName)
            && typeName is not "BOOL" and not "CHAR" and not "VOID");

    private static bool HasStableObjectIdentity(string typeName) =>
        IsReferenceLike(typeName) && typeName != "STRING";

    private static bool IsPrimitiveValueType(string typeName) =>
        typeName.ToUpperInvariant() is
            "BYTE" or "SBYTE" or "SHORT" or "USHORT" or "INT" or "UINT"
            or "LONG" or "ULONG" or "FLOAT" or "DOUBLE" or "DECIMAL";

    private static bool TryGetAccessPath(
        BoundExpression expression,
        TaintState state,
        out TaintAccessPath path)
    {
        switch (expression)
        {
            case BoundVariableExpression variable:
                path = TaintAccessPath.For(variable.Variable);
                return true;

            case BoundFieldAccessExpression field when TryGetAccessPath(field.Target, state, out var target):
                var fieldIdentity = field.ResolvedField is { } resolvedField
                    && !resolvedField.Id.IsNone
                        ? resolvedField.Id.Value
                        : field.FieldName;
                path = target.Append($"field:{fieldIdentity}");
                return true;

            case BoundFieldAccessExpression { ResolvedField: { } field }:
                path = TaintAccessPath.For(field);
                return true;

            case BoundArrayAccess array when TryGetAccessPath(array.Array, state, out var arrayTarget):
                path = arrayTarget.Append(IndexSegment(array.Index));
                return true;

            case BoundArrayAccessExpression array when TryGetAccessPath(array.Array, state, out var arrayTarget):
                path = array.Indices.Aggregate(arrayTarget, (current, index) => current.Append(IndexSegment(index)));
                return true;

            case BoundMultiDimArrayAccess array when TryGetAccessPath(array.Array, state, out var arrayTarget):
                path = array.Indices.Aggregate(arrayTarget, (current, index) => current.Append(IndexSegment(index)));
                return true;

            default:
                path = default;
                return false;
        }
    }

    private static string IndexSegment(BoundExpression index) =>
        index is BoundIntLiteral literal
            ? $"index:{literal.Value}"
            : "index:*";

    private static bool HasWildcardIndex(TaintAccessPath path) =>
        path.Segments.Split('/').Contains("index:*", StringComparer.Ordinal);

    private sealed class TaintTransfer : ITransferFunction<TaintState>
    {
        private readonly TaintAnalysis _analysis;

        public TaintTransfer(TaintAnalysis analysis) => _analysis = analysis;

        public TaintState Transfer(BoundStatement statement, TaintState input) =>
            _analysis.TransferStatement(null, -1, statement, input);

        public TaintState Transfer(
            BasicBlock block,
            int statementIndex,
            BoundStatement statement,
            TaintState input) =>
            _analysis.TransferStatement(
                block,
                statementIndex,
                statement,
                input,
                applyDeferredDefinition: !block.IsDefinitionDeferred(statementIndex));

        public TaintState TransferExpression(BoundExpression? expression, TaintState input) => input;

        public TaintState TransferSynthetic(SyntheticOperation operation, TaintState input) =>
            _analysis.TransferSynthetic(operation, input);
    }

    internal readonly record struct TaintCallDescriptor(
        string Target,
        string? ResolvedTypeName,
        string? ResolvedMethodName,
        IReadOnlyList<string>? ResolvedParameterTypes,
        FunctionSymbol? ResolvedSymbol,
        IReadOnlyList<FunctionSymbol> ResolvedSymbols)
    {
        public bool HasResolvedIdentity =>
            ResolvedSymbol != null
            || ResolvedTypeName != null && ResolvedMethodName != null;

        public string DisplayName => ResolvedTypeName != null && ResolvedMethodName != null
            ? $"{ResolvedTypeName}.{ResolvedMethodName}"
            : Target;

        public static TaintCallDescriptor From(BoundCallExpression call) => new(
            call.Target,
            call.ResolvedTypeName,
            call.ResolvedMethodName,
            call.ResolvedParameterTypes,
            call.ResolvedSymbol,
            call.ResolvedSymbols);

        public static TaintCallDescriptor From(BoundCallStatement call) => new(
            call.Target,
            call.ResolvedTypeName,
            call.ResolvedMethodName,
            call.ResolvedParameterTypes,
            call.ResolvedSymbol,
            call.ResolvedSymbols);
    }

    [Flags]
    internal enum TaintSinkMask
    {
        None = 0,
        SqlQuery = 1 << 0,
        CommandExecution = 1 << 1,
        FilePath = 1 << 2,
        HtmlOutput = 1 << 3,
        UrlRedirect = 1 << 4,
        CodeEval = 1 << 5,
        Deserialization = 1 << 6,
        LogOutput = 1 << 7,
    }

    internal static class TaintSinkMaskExtensions
    {
        public static TaintSinkMask For(TaintSink sink) => (TaintSinkMask)(1 << (int)sink);
    }

    internal readonly record struct TaintOrigin(
        TaintSource? Source,
        int? ParameterIndex,
        string Name,
        TextSpan Location);

    internal readonly record struct TaintFlowIdentity(TaintOrigin Origin, TaintSinkMask SanitizedSinks);

    internal sealed class TaintFlow
    {
        private const int MaximumEvidenceSteps = 32;

        public TaintOrigin Origin { get; }
        public TaintSinkMask SanitizedSinks { get; }
        public IReadOnlyList<TaintFlowStep> Path { get; }
        public TaintFlowIdentity Identity => new(Origin, SanitizedSinks);

        private TaintFlow(
            TaintOrigin origin,
            TaintSinkMask sanitizedSinks,
            IReadOnlyList<TaintFlowStep> path)
        {
            Origin = origin;
            SanitizedSinks = sanitizedSinks;
            Path = path;
        }

        public static TaintFlow ForSource(TaintSource source, string name, TextSpan location) =>
            new(
                new TaintOrigin(source, null, name, location),
                TaintSinkMask.None,
                [new TaintFlowStep($"source {name}", location)]);

        public static TaintFlow ForParameter(int index, string name, TextSpan location) =>
            new(
                new TaintOrigin(null, index, name, location),
                TaintSinkMask.None,
                [new TaintFlowStep($"parameter {name}", location)]);

        public TaintFlow WithStep(string description, TextSpan location)
        {
            if (Path.Count >= MaximumEvidenceSteps)
                return this;
            return new TaintFlow(Origin, SanitizedSinks, [.. Path, new TaintFlowStep(description, location)]);
        }

        public TaintFlow WithSanitizedSinks(TaintSinkMask sinks) =>
            new(Origin, SanitizedSinks | sinks, Path);

        public TaintFlow WithSummaryFlow(TaintFlow summaryFlow, string call, TextSpan location) =>
            new(
                Origin,
                SanitizedSinks | summaryFlow.SanitizedSinks,
                CombineSummaryPath(Path, summaryFlow.Path, $"call {call}", location));

        public TaintFlow WithSummaryReturn(string call, TextSpan location) =>
            new(Origin, SanitizedSinks, Append(Path, $"return from {call}", location));

        public bool IsSanitizedFor(TaintSink sink) =>
            (SanitizedSinks & TaintSinkMaskExtensions.For(sink)) != 0;

        private static IReadOnlyList<TaintFlowStep> CombineSummaryPath(
            IReadOnlyList<TaintFlowStep> callerPath,
            IReadOnlyList<TaintFlowStep> calleePath,
            string call,
            TextSpan location)
        {
            var combined = callerPath.ToList();
            if (combined.Count < MaximumEvidenceSteps)
                combined.Add(new TaintFlowStep(call, location));
            foreach (var step in calleePath.Skip(1))
            {
                if (combined.Count >= MaximumEvidenceSteps)
                    break;
                combined.Add(step);
            }
            return combined;
        }

        private static IReadOnlyList<TaintFlowStep> Append(
            IReadOnlyList<TaintFlowStep> path,
            string description,
            TextSpan location) =>
            path.Count >= MaximumEvidenceSteps
                ? path
                : [.. path, new TaintFlowStep(description, location)];
    }

    internal readonly record struct TaintRoot(
        SymbolId Id,
        VariableSymbol? Symbol,
        string? ObjectIdentity = null)
    {
        public bool Equals(TaintRoot other) =>
            ObjectIdentity != null || other.ObjectIdentity != null
                ? string.Equals(ObjectIdentity, other.ObjectIdentity, StringComparison.Ordinal)
                : !Id.IsNone && !other.Id.IsNone
                ? Id == other.Id
                : ReferenceEquals(Symbol, other.Symbol);

        public override int GetHashCode() =>
            ObjectIdentity != null
                ? StringComparer.Ordinal.GetHashCode(ObjectIdentity)
                : Id.IsNone
                ? Symbol == null ? 0 : RuntimeHelpers.GetHashCode(Symbol)
                : Id.GetHashCode();

        public TaintRoot CreateObject(TextSpan location) =>
            new(
                Id,
                Symbol,
                $"object:{(Id.IsNone ? RuntimeHelpers.GetHashCode(Symbol!).ToString() : Id.Value)}:{location.Start}:{location.End}");

        public TaintRoot CreateNestedObject(TaintAccessPath storagePath) =>
            new(
                Id,
                Symbol,
                $"object-slot:{storagePath.Root.StableIdentity}:{storagePath.Segments}");

        private string StableIdentity =>
            ObjectIdentity
            ?? (Id.IsNone
                ? RuntimeHelpers.GetHashCode(Symbol!).ToString()
                : Id.Value);
    }

    internal readonly record struct TaintAccessPath(TaintRoot Root, string Segments)
    {
        public string DisplayName => string.IsNullOrEmpty(Segments)
            ? Root.Symbol?.Name ?? Root.Id.Value
            : $"{Root.Symbol?.Name ?? Root.Id.Value}.{Segments}";

        public static TaintAccessPath For(VariableSymbol symbol) =>
            new(new TaintRoot(symbol.Id, symbol), string.Empty);

        public TaintAccessPath Append(string segment) =>
            new(Root, string.IsNullOrEmpty(Segments) ? segment : $"{Segments}/{segment}");

        public bool IsSameOrDescendantOf(TaintAccessPath other) =>
            Root.Equals(other.Root)
            && (Segments == other.Segments
                || string.IsNullOrEmpty(other.Segments)
                || Segments.StartsWith(other.Segments + "/", StringComparison.Ordinal));
    }

    internal sealed class TaintState : IEquatable<TaintState>
    {
        private readonly Dictionary<TaintAccessPath, Dictionary<TaintFlowIdentity, TaintFlow>> _facts;
        private readonly Dictionary<TaintRoot, HashSet<TaintRoot>> _aliases;
        private readonly Dictionary<TaintAccessPath, HashSet<TaintRoot>> _referenceValues;
        private readonly Dictionary<TextSpan, Dictionary<TaintFlowIdentity, TaintFlow>> _foreachFlows;

        public static TaintState Empty { get; } = new(
            new Dictionary<TaintAccessPath, Dictionary<TaintFlowIdentity, TaintFlow>>(),
            new Dictionary<TaintRoot, HashSet<TaintRoot>>(),
            new Dictionary<TaintAccessPath, HashSet<TaintRoot>>(),
            new Dictionary<TextSpan, Dictionary<TaintFlowIdentity, TaintFlow>>());

        private TaintState(
            Dictionary<TaintAccessPath, Dictionary<TaintFlowIdentity, TaintFlow>> facts,
            Dictionary<TaintRoot, HashSet<TaintRoot>> aliases,
            Dictionary<TaintAccessPath, HashSet<TaintRoot>> referenceValues,
            Dictionary<TextSpan, Dictionary<TaintFlowIdentity, TaintFlow>> foreachFlows)
        {
            _facts = facts;
            _aliases = aliases;
            _referenceValues = referenceValues;
            _foreachFlows = foreachFlows;
        }

        public IReadOnlyCollection<TaintRoot> GetAliasRoots(TaintRoot root)
        {
            if (root.ObjectIdentity != null)
                return [root];
            return _aliases.TryGetValue(root, out var targets) && targets.Count > 0
                ? targets
                : [root];
        }

        public IEnumerable<TaintAccessPath> ResolveStoragePaths(TaintAccessPath path)
        {
            var current = GetAliasRoots(path.Root)
                .Select(root => new TaintAccessPath(root, string.Empty))
                .ToArray();
            if (string.IsNullOrEmpty(path.Segments))
                return current;

            var segments = path.Segments.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                var appended = current.Select(candidate => candidate.Append(segments[index])).ToArray();
                if (index == segments.Length - 1)
                    return appended;

                current = appended
                    .SelectMany(candidate =>
                    {
                        var references = GetReferenceValues(candidate).ToArray();
                        return references.Length == 0
                            ? [candidate]
                            : references.Select(root => new TaintAccessPath(root, string.Empty));
                    })
                    .ToArray();
            }

            return current;
        }

        public IEnumerable<TaintFlow> Get(TaintAccessPath path)
        {
            var result = new Dictionary<TaintFlowIdentity, TaintFlow>();
            foreach (var target in ResolveValuePaths(path))
                AddFacts(target, includeContainer: true, result);
            return result.Values;
        }

        public (TaintState State, IReadOnlyCollection<TaintRoot> Targets)
            GetOrCreateReferenceTargets(TaintAccessPath path)
        {
            if (string.IsNullOrEmpty(path.Segments))
                return (this, GetAliasRoots(path.Root));

            var referenceValues = CloneReferenceValues();
            var targets = new HashSet<TaintRoot>();
            var changed = false;
            foreach (var storagePath in ResolveStoragePaths(path))
            {
                var existing = GetReferenceValues(storagePath, referenceValues).ToArray();
                if (existing.Length > 0)
                {
                    targets.UnionWith(existing);
                    continue;
                }

                var nestedObject = storagePath.Root.CreateNestedObject(storagePath);
                referenceValues[storagePath] = [nestedObject];
                targets.Add(nestedObject);
                changed = true;
            }

            return (
                changed
                    ? new TaintState(
                        CloneFacts(),
                        CloneAliases(),
                        referenceValues,
                        CloneForeachFlows())
                    : this,
                targets);
        }

        public TaintState WithReferenceValues(
            IEnumerable<TaintAccessPath> storagePaths,
            IEnumerable<TaintRoot> targets,
            bool strongUpdate)
        {
            var referenceValues = CloneReferenceValues();
            var targetSet = targets.ToHashSet();
            foreach (var storagePath in storagePaths.Distinct())
            {
                if (strongUpdate)
                {
                    foreach (var existing in referenceValues.Keys
                        .Where(path => path.IsSameOrDescendantOf(storagePath))
                        .ToArray())
                    {
                        referenceValues.Remove(existing);
                    }

                    if (targetSet.Count > 0)
                        referenceValues[storagePath] = new HashSet<TaintRoot>(targetSet);
                    continue;
                }

                referenceValues.TryGetValue(storagePath, out var existingTargets);
                referenceValues[storagePath] = (existingTargets ?? [])
                    .Concat(targetSet)
                    .ToHashSet();
            }

            return new TaintState(
                CloneFacts(),
                CloneAliases(),
                referenceValues,
                CloneForeachFlows());
        }

        public TaintState StrongUpdate(TaintAccessPath path, IEnumerable<TaintFlow> flows)
        {
            var facts = CloneFacts();
            foreach (var existing in facts.Keys.Where(key => key.IsSameOrDescendantOf(path)).ToArray())
                facts.Remove(existing);

            var merged = Merge(flows);
            if (merged.Count > 0)
                facts[path] = merged;
            return new TaintState(facts, CloneAliases(), CloneReferenceValues(), CloneForeachFlows());
        }

        public TaintState WeakUpdate(
            IEnumerable<TaintAccessPath> paths,
            IEnumerable<TaintFlow> flows)
        {
            var facts = CloneFacts();
            var generated = Merge(flows);
            foreach (var path in paths.Distinct())
            {
                facts.TryGetValue(path, out var existing);
                var merged = Merge(
                    (existing != null ? existing.Values : Enumerable.Empty<TaintFlow>())
                    .Concat(generated.Values));
                if (merged.Count > 0)
                    facts[path] = merged;
            }
            return new TaintState(facts, CloneAliases(), CloneReferenceValues(), CloneForeachFlows());
        }

        public TaintState WithoutAlias(TaintRoot root)
        {
            var aliases = CloneAliases();
            aliases.Remove(root);
            return new TaintState(CloneFacts(), aliases, CloneReferenceValues(), CloneForeachFlows());
        }

        public TaintState InitializeReference(TaintRoot root, TextSpan location)
        {
            if (_aliases.ContainsKey(root))
                return this;
            return NewReference(root, location);
        }

        public TaintState NewReference(TaintRoot root, TextSpan location)
        {
            var aliases = CloneAliases();
            aliases[root] = [root.CreateObject(location)];
            return new TaintState(CloneFacts(), aliases, CloneReferenceValues(), CloneForeachFlows());
        }

        public TaintState WithAliases(TaintRoot source, IEnumerable<TaintRoot> targets)
        {
            var aliases = CloneAliases();
            var targetSet = targets.ToHashSet();
            if (source.ObjectIdentity != null)
                throw new InvalidOperationException("Abstract object identities cannot be rebound");
            if (targetSet.Count == 0)
            {
                aliases.Remove(source);
            }
            else
            {
                aliases[source] = targetSet;
            }

            return new TaintState(CloneFacts(), aliases, CloneReferenceValues(), CloneForeachFlows());
        }

        public TaintState SetForeachFlows(TextSpan span, IEnumerable<TaintFlow> flows)
        {
            var foreachFlows = CloneForeachFlows();
            foreachFlows[span] = Merge(flows);
            return new TaintState(CloneFacts(), CloneAliases(), CloneReferenceValues(), foreachFlows);
        }

        public IEnumerable<TaintFlow> GetForeachFlows(TextSpan span) =>
            _foreachFlows.TryGetValue(span, out var flows)
                ? flows.Values
                : Array.Empty<TaintFlow>();

        public static TaintState Join(TaintState left, TaintState right)
        {
            var facts = left.CloneFacts();
            foreach (var (path, rightFlows) in right._facts)
            {
                facts.TryGetValue(path, out var leftFlows);
                facts[path] = Merge(
                    (leftFlows != null
                        ? leftFlows.Values
                        : Enumerable.Empty<TaintFlow>())
                    .Concat(rightFlows.Values));
            }

            var aliases = new Dictionary<TaintRoot, HashSet<TaintRoot>>();
            foreach (var root in left._aliases.Keys.Union(right._aliases.Keys))
            {
                var targets = left.GetAliasRoots(root)
                    .Concat(right.GetAliasRoots(root))
                    .ToHashSet();
                if (targets.Count != 1 || !targets.Contains(root))
                    aliases[root] = targets;
            }

            var referenceValues = left.CloneReferenceValues();
            foreach (var (path, rightTargets) in right._referenceValues)
            {
                referenceValues.TryGetValue(path, out var leftTargets);
                referenceValues[path] = (leftTargets ?? [])
                    .Concat(rightTargets)
                    .ToHashSet();
            }

            var foreachFlows = left.CloneForeachFlows();
            foreach (var (span, rightFlows) in right._foreachFlows)
            {
                foreachFlows.TryGetValue(span, out var leftFlows);
                foreachFlows[span] = Merge(
                    (leftFlows != null
                        ? leftFlows.Values
                        : Enumerable.Empty<TaintFlow>())
                    .Concat(rightFlows.Values));
            }
            return new TaintState(facts, aliases, referenceValues, foreachFlows);
        }

        public bool Equals(TaintState? other)
        {
            if (other == null
                || _facts.Count != other._facts.Count
                || _aliases.Count != other._aliases.Count
                || _referenceValues.Count != other._referenceValues.Count
                || _foreachFlows.Count != other._foreachFlows.Count)
            {
                return false;
            }

            foreach (var (path, flows) in _facts)
            {
                if (!other._facts.TryGetValue(path, out var otherFlows)
                    || !flows.Keys.ToHashSet().SetEquals(otherFlows.Keys))
                {
                    return false;
                }
            }

            foreach (var (root, targets) in _aliases)
            {
                if (!other._aliases.TryGetValue(root, out var otherTargets)
                    || !targets.SetEquals(otherTargets))
                {
                    return false;
                }
            }

            foreach (var (path, targets) in _referenceValues)
            {
                if (!other._referenceValues.TryGetValue(path, out var otherTargets)
                    || !targets.SetEquals(otherTargets))
                {
                    return false;
                }
            }

            foreach (var (span, flows) in _foreachFlows)
            {
                if (!other._foreachFlows.TryGetValue(span, out var otherFlows)
                    || !flows.Keys.ToHashSet().SetEquals(otherFlows.Keys))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is TaintState state && Equals(state);

        public override int GetHashCode()
        {
            var hash = _facts.Count ^ _aliases.Count ^ _referenceValues.Count ^ _foreachFlows.Count;
            foreach (var (path, flows) in _facts)
            {
                hash ^= path.GetHashCode();
                foreach (var flow in flows.Keys)
                    hash ^= flow.GetHashCode();
            }
            foreach (var (root, targets) in _aliases)
            {
                hash ^= root.GetHashCode();
                foreach (var target in targets)
                    hash ^= target.GetHashCode();
            }
            foreach (var (path, targets) in _referenceValues)
            {
                hash ^= path.GetHashCode();
                foreach (var target in targets)
                    hash ^= target.GetHashCode();
            }
            return hash;
        }

        private IEnumerable<TaintAccessPath> ResolveValuePaths(TaintAccessPath path)
        {
            foreach (var storagePath in ResolveStoragePaths(path))
            {
                var references = string.IsNullOrEmpty(path.Segments)
                    ? Array.Empty<TaintRoot>()
                    : GetReferenceValues(storagePath).ToArray();
                if (references.Length == 0)
                {
                    yield return storagePath;
                    continue;
                }

                foreach (var root in references)
                    yield return new TaintAccessPath(root, string.Empty);
            }
        }

        private IEnumerable<TaintRoot> GetReferenceValues(TaintAccessPath storagePath) =>
            GetReferenceValues(storagePath, _referenceValues);

        private static IEnumerable<TaintRoot> GetReferenceValues(
            TaintAccessPath storagePath,
            IReadOnlyDictionary<TaintAccessPath, HashSet<TaintRoot>> referenceValues)
        {
            foreach (var (candidate, targets) in referenceValues)
            {
                var matchesExact = candidate == storagePath;
                var matchesWildcard = candidate.Root.Equals(storagePath.Root)
                    && candidate.Segments.EndsWith("index:*", StringComparison.Ordinal)
                    && storagePath.Segments.StartsWith(
                        candidate.Segments[..^1],
                        StringComparison.Ordinal);
                if (!matchesExact && !matchesWildcard)
                    continue;
                foreach (var target in targets)
                    yield return target;
            }
        }

        private void AddFacts(
            TaintAccessPath path,
            bool includeContainer,
            Dictionary<TaintFlowIdentity, TaintFlow> result)
        {
            foreach (var (candidate, flows) in _facts)
            {
                var matchesExact = candidate == path;
                var matchesWildcard = candidate.Root.Equals(path.Root)
                    && candidate.Segments.EndsWith("index:*", StringComparison.Ordinal)
                    && path.Segments.StartsWith(
                        candidate.Segments[..^1],
                        StringComparison.Ordinal);
                var matchesContainer = includeContainer
                    && candidate.Root.Equals(path.Root)
                    && string.IsNullOrEmpty(candidate.Segments);
                if (!matchesExact && !matchesWildcard && !matchesContainer)
                    continue;
                foreach (var flow in flows.Values)
                    AddShortest(result, flow);
            }
        }

        private Dictionary<TaintAccessPath, Dictionary<TaintFlowIdentity, TaintFlow>> CloneFacts() =>
            _facts.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<TaintFlowIdentity, TaintFlow>(pair.Value));

        private Dictionary<TaintRoot, HashSet<TaintRoot>> CloneAliases() =>
            _aliases.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<TaintRoot>(pair.Value));

        private Dictionary<TaintAccessPath, HashSet<TaintRoot>> CloneReferenceValues() =>
            _referenceValues.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<TaintRoot>(pair.Value));

        private Dictionary<TextSpan, Dictionary<TaintFlowIdentity, TaintFlow>> CloneForeachFlows() =>
            _foreachFlows.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<TaintFlowIdentity, TaintFlow>(pair.Value));

        private static Dictionary<TaintFlowIdentity, TaintFlow> Merge(IEnumerable<TaintFlow> flows)
        {
            var result = new Dictionary<TaintFlowIdentity, TaintFlow>();
            foreach (var flow in flows)
                AddShortest(result, flow);
            return result;
        }
    }

    private sealed class TaintStateLattice : IDataflowLattice<TaintState>
    {
        public TaintState Bottom => TaintState.Empty;
        public TaintState Top => TaintState.Empty;
        public TaintState Join(TaintState a, TaintState b) => TaintState.Join(a, b);
        public bool LessOrEqual(TaintState a, TaintState b) => TaintState.Join(a, b).Equals(b);
    }

    private static void AddShortest(
        IDictionary<TaintFlowIdentity, TaintFlow> flows,
        TaintFlow flow)
    {
        if (!flows.TryGetValue(flow.Identity, out var existing)
            || flow.Path.Count < existing.Path.Count)
        {
            flows[flow.Identity] = flow;
        }
    }

    private readonly record struct TaintFindingKey(
        TaintSink Sink,
        TaintSource Source,
        string SourceName,
        TextSpan SourceLocation,
        string SinkName,
        TextSpan SinkLocation);

    private readonly record struct TaintSummarySinkKey(
        TaintSink Sink,
        int ParameterIndex,
        TextSpan Location);
}

internal sealed class TaintFunctionSummary : IEquatable<TaintFunctionSummary>
{
    public IReadOnlyList<TaintAnalysis.TaintFlow> ReturnFlows { get; }
    public IReadOnlyList<TaintSummarySink> ParameterSinks { get; }

    public TaintFunctionSummary(
        IReadOnlyList<TaintAnalysis.TaintFlow> returnFlows,
        IReadOnlyList<TaintSummarySink> parameterSinks)
    {
        ReturnFlows = returnFlows;
        ParameterSinks = parameterSinks;
    }

    public static TaintFunctionSummary Join(
        TaintFunctionSummary current,
        TaintFunctionSummary discovered)
    {
        var returns = new Dictionary<TaintAnalysis.TaintFlowIdentity, TaintAnalysis.TaintFlow>();
        foreach (var flow in current.ReturnFlows.Concat(discovered.ReturnFlows))
        {
            if (!returns.TryGetValue(flow.Identity, out var existing)
                || flow.Path.Count < existing.Path.Count)
            {
                returns[flow.Identity] = flow;
            }
        }

        var sinks = new Dictionary<TaintSummarySinkIdentity, TaintSummarySink>();
        foreach (var sink in current.ParameterSinks.Concat(discovered.ParameterSinks))
        {
            if (!sinks.TryGetValue(sink.Identity, out var existing)
                || sink.ProvenancePath.Count < existing.ProvenancePath.Count)
            {
                sinks[sink.Identity] = sink;
            }
        }

        return new TaintFunctionSummary(
            returns.Values.ToArray(),
            sinks.Values.ToArray());
    }

    public bool Equals(TaintFunctionSummary? other) =>
        other != null
        && ReturnFlows.Select(flow => flow.Identity).ToHashSet()
            .SetEquals(other.ReturnFlows.Select(flow => flow.Identity))
        && ParameterSinks.Select(sink => sink.Identity).ToHashSet()
            .SetEquals(other.ParameterSinks.Select(sink => sink.Identity));

    public override bool Equals(object? obj) => obj is TaintFunctionSummary summary && Equals(summary);
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var flow in ReturnFlows)
            hash ^= flow.Identity.GetHashCode();
        foreach (var sink in ParameterSinks)
            hash ^= sink.Identity.GetHashCode();
        return hash;
    }
}

internal sealed record TaintSummarySink(
    TaintSink Sink,
    int ParameterIndex,
    string Target,
    TextSpan Location,
    IReadOnlyList<TaintFlowStep> ProvenancePath)
{
    public TaintSummarySinkIdentity Identity => new(Sink, ParameterIndex, Target, Location);
}

internal readonly record struct TaintSummarySinkIdentity(
    TaintSink Sink,
    int ParameterIndex,
    string Target,
    TextSpan Location);

/// <summary>Runner for taint analysis on a bound module.</summary>
public sealed class TaintAnalysisRunner
{
    private readonly DiagnosticBag _diagnostics;
    private readonly TaintAnalysisOptions _options;
    public int VulnerabilityCount { get; private set; }

    public TaintAnalysisRunner(DiagnosticBag diagnostics, TaintAnalysisOptions? options = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _options = options ?? TaintAnalysisOptions.Default;
    }

    public void Analyze(BoundModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        VulnerabilityCount = 0;
        var summaries = BuildSummaries(module.Functions);
        foreach (var function in module.Functions)
        {
            var analysis = new TaintAnalysis(
                function,
                _options,
                function.DeclaredEffects,
                summaries,
                symbolicParameters: false);
            analysis.ReportDiagnostics(_diagnostics);
            VulnerabilityCount += analysis.Vulnerabilities.Count;
        }
    }

    public void AnalyzeFunction(BoundFunction function)
    {
        VulnerabilityCount = 0;
        var analysis = new TaintAnalysis(function, _options, function.DeclaredEffects);
        analysis.ReportDiagnostics(_diagnostics);
        VulnerabilityCount += analysis.Vulnerabilities.Count;
    }

    private IReadOnlyDictionary<SymbolId, TaintFunctionSummary> BuildSummaries(
        IReadOnlyList<BoundFunction> functions)
    {
        var functionsById = functions
            .Where(function => !function.SymbolId.IsNone)
            .ToDictionary(function => function.SymbolId);
        var summaries = functionsById.Keys.ToDictionary(
            id => id,
            _ => new TaintFunctionSummary([], []));

        var callersByCallee = functionsById.Keys.ToDictionary(
            id => id,
            _ => new HashSet<SymbolId>());
        foreach (var (callerId, function) in functionsById)
        {
            foreach (var calleeId in GetResolvedCallees(function))
            {
                if (callersByCallee.TryGetValue(calleeId, out var callers))
                    callers.Add(callerId);
            }
        }

        // Summary identities form a finite powerset lattice: origins and sink sites
        // come from the finite bound tree, while sanitizer masks have finitely many
        // combinations. Joining discoveries monotonically and revisiting only callers
        // therefore reaches the least fixed point, including recursive SCCs.
        var worklist = new Queue<SymbolId>(functionsById.Keys);
        var queued = functionsById.Keys.ToHashSet();
        while (worklist.Count > 0)
        {
            var id = worklist.Dequeue();
            queued.Remove(id);
            var function = functionsById[id];
            var analysis = new TaintAnalysis(
                function,
                _options,
                function.DeclaredEffects,
                summaries,
                symbolicParameters: true);
            var joined = TaintFunctionSummary.Join(summaries[id], analysis.CreateSummary());
            if (joined.Equals(summaries[id]))
                continue;

            summaries[id] = joined;
            foreach (var callerId in callersByCallee[id])
            {
                if (queued.Add(callerId))
                    worklist.Enqueue(callerId);
            }
        }

        return summaries;
    }

    private static IEnumerable<SymbolId> GetResolvedCallees(BoundFunction function) =>
        BoundNodeHelpers.DescendantsAndSelf(function)
            .SelectMany(node => node switch
            {
                BoundCallStatement call => call.ResolvedSymbols,
                BoundCallExpression call => call.ResolvedSymbols,
                _ => Array.Empty<FunctionSymbol>(),
            })
            .Select(symbol => symbol.Id)
            .Where(id => !id.IsNone)
            .Distinct();
}
