using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.LanguageServer.State;

public sealed record DocumentAnalysisSnapshot(
    int Version,
    string Source,
    List<Token>? Tokens,
    ModuleNode? Ast,
    BoundModule? BoundModule,
    DiagnosticBag Diagnostics,
    List<DiagnosticWithFix> DiagnosticsWithFixes);

/// <summary>
/// Holds the analysis state for a single document.
/// </summary>
public sealed class DocumentState
{
    private readonly string _sourceIdentity;
    private DocumentAnalysisSnapshot _snapshot;

    /// <summary>
    /// The document URI.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>
    /// The document version (incremented on each change).
    /// </summary>
    public int Version => Snapshot.Version;

    /// <summary>
    /// The source text content.
    /// </summary>
    public string Source => Snapshot.Source;

    /// <summary>
    /// The parsed tokens.
    /// </summary>
    public List<Token>? Tokens => Snapshot.Tokens;

    /// <summary>
    /// The parsed AST.
    /// </summary>
    public ModuleNode? Ast => Snapshot.Ast;

    /// <summary>
    /// The bound module (with resolved symbols).
    /// </summary>
    public BoundModule? BoundModule => Snapshot.BoundModule;

    /// <summary>
    /// All diagnostics from lexing, parsing, and binding.
    /// </summary>
    public DiagnosticBag Diagnostics => Snapshot.Diagnostics;

    /// <summary>
    /// Diagnostics with suggested fixes.
    /// </summary>
    public List<DiagnosticWithFix> DiagnosticsWithFixes => Snapshot.DiagnosticsWithFixes;

    public DocumentAnalysisSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public DocumentState(
        Uri uri,
        string source,
        int version = 0,
        string? sourceIdentity = null)
    {
        Uri = uri;
        _sourceIdentity = SymbolSourceIdentity.Canonicalize(sourceIdentity ?? uri.ToString());
        _snapshot = new DocumentAnalysisSnapshot(
            version,
            source,
            null,
            null,
            null,
            new DiagnosticBag(),
            []);
    }

    /// <summary>
    /// Update the document content and reanalyze.
    /// </summary>
    public void Update(string newSource, int newVersion)
    {
        var next = Analyze(newSource, newVersion);
        while (true)
        {
            var current = Snapshot;
            if (newVersion < current.Version)
                return;
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _snapshot, next, current),
                    current))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Full reparse and rebind of the document.
    /// </summary>
    public void Reanalyze()
    {
        while (true)
        {
            var current = Snapshot;
            var next = Analyze(current.Source, current.Version);
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _snapshot, next, current),
                    current))
            {
                return;
            }
        }
    }

    private DocumentAnalysisSnapshot Analyze(string source, int version)
    {
        var diagnostics = new DiagnosticBag();
        var diagnosticsWithFixes = new List<DiagnosticWithFix>();
        List<Token>? tokens = null;
        ModuleNode? ast = null;
        BoundModule? boundModule = null;

        // Set file path for diagnostics
        var filePath = Uri.IsFile ? Uri.LocalPath : Uri.ToString();
        diagnostics.SetFilePath(filePath);

        try
        {
            // Phase 1: Lexing
            var lexer = new Lexer(source, diagnostics);
            tokens = lexer.TokenizeAll();

            // Phase 2: Parsing (uses indent-aware token stream)
            var parserLexer = new Lexer(source, new DiagnosticBag());
            var parserTokens = parserLexer.TokenizeAllForParser();
            var parser = new Parser(parserTokens, diagnostics);
            ast = parser.Parse();

            // Phase 3: Binding (only if parsing succeeded without critical errors)
            if (ast != null && !diagnostics.HasErrors)
            {
                try
                {
                    var binder = new Binder(diagnostics, _sourceIdentity);
                    boundModule = binder.Bind(ast);
                }
                catch (Exception ex)
                {
                    diagnostics.ReportInfo(
                        ast.Span,
                        DiagnosticCode.AnalysisSkipped,
                        $"Bound symbol analysis did not complete: {ex.GetType().Name}");
                }
            }

            // Phase 4: Bind validation (Calor0250-0253). Always runs when an
            // AST is available so quick-fixes for strict bind-inference
            // diagnostics surface in the IDE. Strict checks default-on per
            // v0.6.3 (RFC v0.6 bind-inference-formalization §6 Phase 4).
            if (ast != null)
            {
                try
                {
                    var bindValidator = new BindValidationPass(diagnostics, source, strictInference: true);
                    bindValidator.Check(ast);
                }
                catch (Exception)
                {
                    // Validation should never throw on a parsed AST, but be defensive.
                }
            }

            // Phase 5: Return validation (Calor0205). Always runs when an AST is
            // available so the IDE surfaces value-returned-from-void-owner errors.
            if (ast != null)
            {
                try
                {
                    var returnValidator = new ReturnValidationPass(diagnostics);
                    returnValidator.Check(ast);
                }
                catch (Exception)
                {
                    // Validation should never throw on a parsed AST, but be defensive.
                }
            }

            // Populate DiagnosticsWithFixes from DiagnosticBag
            diagnosticsWithFixes.AddRange(diagnostics.DiagnosticsWithFixes);
        }
        catch (Exception ex)
        {
            // Log unexpected errors as diagnostics
            diagnostics.ReportError(
                TextSpan.Empty,
                "Calor9999",
                $"Internal error: {ex.Message}");
        }

        return new DocumentAnalysisSnapshot(
            version,
            source,
            tokens,
            ast,
            boundModule,
            diagnostics,
            diagnosticsWithFixes);
    }

    /// <summary>
    /// Find the token at a given position.
    /// </summary>
    public Token? GetTokenAtPosition(int line, int column)
    {
        if (Tokens == null)
            return null;

        foreach (var token in Tokens)
        {
            if (token.Span.Line == line &&
                column >= token.Span.Column &&
                column < token.Span.Column + token.Span.Length)
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>
    /// Find the token at a given offset.
    /// </summary>
    public Token? GetTokenAtOffset(int offset)
    {
        if (Tokens == null)
            return null;

        foreach (var token in Tokens)
        {
            if (token.Span.Contains(offset))
            {
                return token;
            }
        }

        return null;
    }
}
