using System.Collections.Immutable;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Calor.LanguageServer.State;

public sealed class ImmutableDiagnostics : IReadOnlyList<Diagnostic>
{
    private readonly ImmutableArray<Diagnostic> _items;

    public ImmutableDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        _items = diagnostics.ToImmutableArray();
    }

    public int Count => _items.Length;
    public Diagnostic this[int index] => _items[index];
    public bool HasErrors => _items.Any(diagnostic => diagnostic.IsError);
    public IReadOnlyList<Diagnostic> Errors =>
        _items.Where(diagnostic => diagnostic.IsError).ToImmutableArray();
    public IReadOnlyList<Diagnostic> Warnings =>
        _items.Where(diagnostic => diagnostic.IsWarning).ToImmutableArray();

    public ImmutableArray<Diagnostic>.Enumerator GetEnumerator() =>
        _items.GetEnumerator();

    IEnumerator<Diagnostic> IEnumerable<Diagnostic>.GetEnumerator() =>
        ((IEnumerable<Diagnostic>)_items).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        ((System.Collections.IEnumerable)_items).GetEnumerator();
}

public sealed record DocumentSnapshot(
    int Version,
    string Source,
    ImmutableArray<Token> Tokens,
    ModuleNode? Ast,
    BoundModule? BoundModule,
    ImmutableDiagnostics Diagnostics,
    ImmutableArray<DiagnosticWithFix> DiagnosticsWithFixes)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.IsError);

    public Token? GetTokenAtPosition(int line, int column)
    {
        foreach (var token in Tokens)
        {
            if (token.Span.Line == line
                && column >= token.Span.Column
                && column < token.Span.Column + token.Span.Length)
            {
                return token;
            }
        }

        return null;
    }

    public Token? GetTokenAtOffset(int offset)
    {
        foreach (var token in Tokens)
        {
            if (token.Span.Contains(offset))
                return token;
        }

        return null;
    }
}

/// <summary>
/// Owns the immutable, versioned analysis snapshot for one document.
/// </summary>
public sealed class DocumentState : IDisposable
{
    private readonly record struct AnalysisOutcome(
        DocumentSnapshot Snapshot,
        bool Published);

    private readonly string _sourceIdentity;
    private readonly ILogger _logger;
    private readonly Func<DocumentAnalysisPhase, Exception?>? _failureInjector;
    private readonly Func<Task>? _reanalysisRegistrationBarrier;
    private readonly object _updateGate = new();
    private CancellationTokenSource? _analysisCancellation;
    private DocumentSnapshot _snapshot;
    private int _desiredVersion;
    private string _desiredSource;
    private long _contentGeneration;
    private bool _hasCompletedAnalysis;
    private bool _disposed;

    internal enum DocumentAnalysisPhase
    {
        Lexing,
        Parsing,
        Binding,
        BindValidation,
        ReturnValidation,
    }

    public Uri Uri { get; }
    public int Version => Snapshot.Version;
    public string Source => Snapshot.Source;
    public ImmutableArray<Token> Tokens => Snapshot.Tokens;
    public ModuleNode? Ast => Snapshot.Ast;
    public BoundModule? BoundModule => Snapshot.BoundModule;
    public ImmutableDiagnostics Diagnostics => Snapshot.Diagnostics;
    public ImmutableArray<DiagnosticWithFix> DiagnosticsWithFixes =>
        Snapshot.DiagnosticsWithFixes;
    public DocumentSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public DocumentState(
        Uri uri,
        string source,
        int version = 0,
        string? sourceIdentity = null)
        : this(
            uri,
            source,
            version,
            sourceIdentity,
            NullLogger.Instance,
            failureInjector: null,
            reanalysisRegistrationBarrier: null)
    {
    }

    internal DocumentState(
        Uri uri,
        string source,
        int version,
        string? sourceIdentity,
        ILogger logger,
        Func<DocumentAnalysisPhase, Exception?>? failureInjector,
        Func<Task>? reanalysisRegistrationBarrier = null)
    {
        Uri = uri;
        _sourceIdentity = SymbolSourceIdentity.Canonicalize(sourceIdentity ?? uri.ToString());
        _logger = logger;
        _failureInjector = failureInjector;
        _reanalysisRegistrationBarrier = reanalysisRegistrationBarrier;
        _desiredVersion = version;
        _desiredSource = source;
        _snapshot = new DocumentSnapshot(
            version,
            source,
            ImmutableArray<Token>.Empty,
            null,
            null,
            new ImmutableDiagnostics([]),
            ImmutableArray<DiagnosticWithFix>.Empty);
    }

    private async Task<(bool Accepted, Task<AnalysisOutcome> Analysis)>
        StartAnalysisAsync(
        string source,
        int version,
        bool requireNewerVersion,
        long? expectedContentGeneration,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? previousCancellation;
        CancellationTokenSource analysisCancellation;
        CancellationToken analysisToken;
        lock (_updateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (requireNewerVersion && version < _desiredVersion)
            {
                return (false, Task.FromResult(
                    new AnalysisOutcome(Snapshot, Published: false)));
            }
            if (requireNewerVersion
                && version == _desiredVersion
                && (_analysisCancellation != null
                    || (_hasCompletedAnalysis
                        && Snapshot.Version == version)
                    || !string.Equals(
                        source,
                        _desiredSource,
                        StringComparison.Ordinal)))
            {
                return (false, Task.FromResult(
                    new AnalysisOutcome(Snapshot, Published: false)));
            }
            if (expectedContentGeneration is { } expected
                && expected != _contentGeneration)
            {
                return (false, Task.FromResult(
                    new AnalysisOutcome(Snapshot, Published: false)));
            }

            if (requireNewerVersion && version > _desiredVersion)
            {
                _desiredVersion = version;
                _desiredSource = source;
                _contentGeneration++;
            }
            previousCancellation = _analysisCancellation;
            analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            analysisToken = analysisCancellation.Token;
            _analysisCancellation = analysisCancellation;
        }

        var analysis = Task.Run(
            () => AnalyzeAndPublish(
                source,
                version,
                analysisToken,
                analysisCancellation),
            CancellationToken.None);
        await CancelAnalysisAsync(previousCancellation).ConfigureAwait(false);
        return (true, analysis);
    }

    public async Task<DocumentSnapshot> ReanalyzeAsync(
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source;
            int version;
            long contentGeneration;
            lock (_updateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                source = _desiredSource;
                version = _desiredVersion;
                contentGeneration = _contentGeneration;
            }

            if (_reanalysisRegistrationBarrier != null)
            {
                await _reanalysisRegistrationBarrier()
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            var request = await StartAnalysisAsync(
                source,
                version,
                requireNewerVersion: false,
                expectedContentGeneration: contentGeneration,
                cancellationToken).ConfigureAwait(false);
            if (!request.Accepted)
                continue;
#pragma warning disable VSTHRD003 // The task is explicitly started with Task.Run in StartAnalysisAsync.
            var outcome = await request.Analysis.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            cancellationToken.ThrowIfCancellationRequested();
            if (outcome.Published)
                return outcome.Snapshot;
            lock (_updateGate)
            {
                if (_disposed)
                    return outcome.Snapshot;
            }
        }
    }

    public void Update(string newSource, int newVersion)
    {
#pragma warning disable VSTHRD002 // Compatibility wrapper; analysis itself runs on TaskScheduler.Default.
        var request = StartAnalysisAsync(
                newSource,
                newVersion,
                requireNewerVersion: true,
                expectedContentGeneration: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (request.Accepted)
        {
            request.Analysis.GetAwaiter().GetResult();
        }
#pragma warning restore VSTHRD002
    }

    public async Task<(DocumentSnapshot Snapshot, bool Accepted)> UpdateAsync(
        string newSource,
        int newVersion,
        CancellationToken cancellationToken = default)
    {
        var request = await StartAnalysisAsync(
                newSource,
                newVersion,
                requireNewerVersion: true,
                expectedContentGeneration: null,
                cancellationToken).ConfigureAwait(false);
        if (!request.Accepted)
        {
            return (Snapshot, false);
        }

#pragma warning disable VSTHRD003 // The task is explicitly started with Task.Run in StartAnalysisAsync.
        var outcome = await request.Analysis.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        return (outcome.Snapshot, outcome.Published
            && IsCurrent(outcome.Snapshot)
            && outcome.Snapshot.Version == newVersion
            && string.Equals(
                outcome.Snapshot.Source,
                newSource,
                StringComparison.Ordinal));
    }

    public void Reanalyze() =>
#pragma warning disable VSTHRD002 // Compatibility wrapper; analysis itself runs on TaskScheduler.Default.
        ReanalyzeAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

    private AnalysisOutcome AnalyzeAndPublish(
        string source,
        int version,
        CancellationToken analysisToken,
        CancellationTokenSource analysisCancellation)
    {
        try
        {
            var next = Analyze(source, version, analysisToken);
            lock (_updateGate)
            {
                if (!_disposed
                    && version == _desiredVersion
                    && string.Equals(
                        source,
                        _desiredSource,
                        StringComparison.Ordinal)
                    && ReferenceEquals(_analysisCancellation, analysisCancellation)
                    && !analysisCancellation.IsCancellationRequested)
                {
                    Volatile.Write(ref _snapshot, next);
                    _hasCompletedAnalysis = true;
                    return new AnalysisOutcome(next, Published: true);
                }
            }

            return new AnalysisOutcome(Snapshot, Published: false);
        }
        catch (OperationCanceledException) when (analysisToken.IsCancellationRequested)
        {
            return new AnalysisOutcome(Snapshot, Published: false);
        }
        finally
        {
            lock (_updateGate)
            {
                if (ReferenceEquals(_analysisCancellation, analysisCancellation))
                    _analysisCancellation = null;
            }
            analysisCancellation.Dispose();
        }
    }

    private DocumentSnapshot Analyze(
        string source,
        int version,
        CancellationToken cancellationToken)
    {
        var diagnostics = new DiagnosticBag();
        List<Token> tokens = [];
        ModuleNode? ast = null;
        BoundModule? boundModule = null;
        var filePath = Uri.IsFile ? Uri.LocalPath : Uri.ToString();
        var currentPhase = DocumentAnalysisPhase.Lexing;
        diagnostics.SetFilePath(filePath);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentPhase = DocumentAnalysisPhase.Lexing;
            ThrowInjectedFailure(currentPhase);
            cancellationToken.ThrowIfCancellationRequested();
            tokens = new Lexer(source, diagnostics).TokenizeAll();
            cancellationToken.ThrowIfCancellationRequested();

            cancellationToken.ThrowIfCancellationRequested();
            currentPhase = DocumentAnalysisPhase.Parsing;
            ThrowInjectedFailure(currentPhase);
            cancellationToken.ThrowIfCancellationRequested();
            var parserTokens = new Lexer(source, new DiagnosticBag()).TokenizeAllForParser();
            cancellationToken.ThrowIfCancellationRequested();
            ast = new Parser(parserTokens, diagnostics).Parse();
            cancellationToken.ThrowIfCancellationRequested();

            if (ast != null && !diagnostics.HasErrors)
            {
                RunAnalysisPhase(
                    DocumentAnalysisPhase.Binding,
                    ast.Span,
                    diagnostics,
                    () =>
                    {
                        boundModule = new Binder(diagnostics, _sourceIdentity).Bind(ast);
                    });
            }

            if (ast != null)
            {
                RunAnalysisPhase(
                    DocumentAnalysisPhase.BindValidation,
                    ast.Span,
                    diagnostics,
                    () => new BindValidationPass(
                        diagnostics,
                        source,
                        strictInference: true).Check(ast));
                RunAnalysisPhase(
                    DocumentAnalysisPhase.ReturnValidation,
                    ast.Span,
                    diagnostics,
                    () => new ReturnValidationPass(diagnostics).Check(ast));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ReportInternalFailure(
                currentPhase,
                ex,
                TextSpan.Empty,
                diagnostics,
                version);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new DocumentSnapshot(
            version,
            source,
            tokens.ToImmutableArray(),
            ast,
            boundModule,
            new ImmutableDiagnostics(diagnostics),
            diagnostics.DiagnosticsWithFixes.ToImmutableArray());

        void RunAnalysisPhase(
            DocumentAnalysisPhase phase,
            TextSpan span,
            DiagnosticBag bag,
            Action action)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ThrowInjectedFailure(phase);
                cancellationToken.ThrowIfCancellationRequested();
                action();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportInternalFailure(phase, ex, span, bag, version);
            }
        }
    }

    private void ThrowInjectedFailure(DocumentAnalysisPhase phase)
    {
        var failure = _failureInjector?.Invoke(phase);
        if (failure != null)
            throw failure;
    }

    private void ReportInternalFailure(
        DocumentAnalysisPhase phase,
        Exception exception,
        TextSpan span,
        DiagnosticBag diagnostics,
        int version)
    {
        _logger.LogError(
            exception,
            "LSP document analysis failed in {AnalysisPhase} for {DocumentUri} at version {DocumentVersion}.",
            phase,
            Uri,
            version);
        diagnostics.ReportError(
            span,
            "Calor9999",
            $"Internal {GetPhaseName(phase)} analysis error ({exception.GetType().Name}).");
    }

    private static string GetPhaseName(DocumentAnalysisPhase phase) => phase switch
    {
        DocumentAnalysisPhase.BindValidation => "bind validation",
        DocumentAnalysisPhase.ReturnValidation => "return validation",
        _ => phase.ToString().ToLowerInvariant(),
    };

    public Token? GetTokenAtPosition(int line, int column) =>
        Snapshot.GetTokenAtPosition(line, column);

    public Token? GetTokenAtOffset(int offset) =>
        Snapshot.GetTokenAtOffset(offset);

    public bool IsCurrent(DocumentSnapshot snapshot)
    {
        lock (_updateGate)
        {
            return !_disposed
                && ReferenceEquals(_snapshot, snapshot)
                && _hasCompletedAnalysis
                && snapshot.Version == _desiredVersion;
        }
    }

    internal bool TryUseCurrentSnapshot(
        DocumentSnapshot snapshot,
        Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_updateGate)
        {
            if (_disposed
                || !ReferenceEquals(_snapshot, snapshot)
                || !_hasCompletedAnalysis
                || snapshot.Version != _desiredVersion)
            {
                return false;
            }

            action();
            return true;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_updateGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            cancellation = _analysisCancellation;
            _analysisCancellation = null;
        }
        _ = CancelAnalysisAsync(cancellation);
    }

    private async Task CancelAnalysisAsync(
        CancellationTokenSource? cancellation)
    {
        if (cancellation == null)
            return;

        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "LSP document analysis cancellation failed for {DocumentUri}.",
                Uri);
        }
    }
}
