using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.LanguageServer.State;

/// <summary>
/// Holds the analysis state for a single document.
/// </summary>
public sealed class DocumentState
{
    /// <summary>
    /// The document URI.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>
    /// The document version (incremented on each change).
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// The source text content.
    /// </summary>
    public string Source { get; private set; }

    /// <summary>
    /// The parsed tokens.
    /// </summary>
    public List<Token>? Tokens { get; private set; }

    /// <summary>
    /// The parsed AST.
    /// </summary>
    public ModuleNode? Ast { get; private set; }

    /// <summary>
    /// The bound module (with resolved symbols).
    /// </summary>
    public BoundModule? BoundModule { get; private set; }

    /// <summary>
    /// All diagnostics from lexing, parsing, and binding.
    /// </summary>
    public DiagnosticBag Diagnostics { get; private set; }

    /// <summary>
    /// Diagnostics with suggested fixes.
    /// </summary>
    public List<DiagnosticWithFix> DiagnosticsWithFixes { get; private set; }

    public DocumentState(Uri uri, string source, int version = 0)
    {
        Uri = uri;
        Source = source;
        Version = version;
        Diagnostics = new DiagnosticBag();
        DiagnosticsWithFixes = new List<DiagnosticWithFix>();
    }

    /// <summary>
    /// Update the document content and reanalyze.
    /// </summary>
    public void Update(string newSource, int newVersion)
    {
        Source = newSource;
        Version = newVersion;
        Reanalyze();
    }

    /// <summary>
    /// Full reparse and rebind of the document.
    /// </summary>
    public void Reanalyze()
    {
        Diagnostics = new DiagnosticBag();
        DiagnosticsWithFixes = new List<DiagnosticWithFix>();
        Tokens = null;
        Ast = null;
        BoundModule = null;

        // Set file path for diagnostics
        var filePath = Uri.IsFile ? Uri.LocalPath : Uri.ToString();
        Diagnostics.SetFilePath(filePath);

        try
        {
            // Phase 1: Lexing
            var lexer = new Lexer(Source, Diagnostics);
            Tokens = lexer.TokenizeAll();

            // Phase 2: Parsing (uses indent-aware token stream)
            var parserLexer = new Lexer(Source, new DiagnosticBag());
            var parserTokens = parserLexer.TokenizeAllForParser();
            var parser = new Parser(parserTokens, Diagnostics);
            Ast = parser.Parse();

            // Phase 3: Binding (only if parsing succeeded without critical errors)
            if (Ast != null && !Diagnostics.HasErrors)
            {
                try
                {
                    var binder = new Binder(Diagnostics);
                    BoundModule = binder.Bind(Ast);
                }
                catch (Exception)
                {
                    // Binding can fail on malformed AST, continue without bound module
                }
            }

            // Phase 4: Bind validation (Calor0250-0253). Always runs when an
            // AST is available so quick-fixes for strict bind-inference
            // diagnostics surface in the IDE. Strict checks default-on per
            // v0.6.3 (RFC v0.6 bind-inference-formalization §6 Phase 4).
            if (Ast != null)
            {
                try
                {
                    var bindValidator = new BindValidationPass(Diagnostics, Source, strictInference: true);
                    bindValidator.Check(Ast);
                }
                catch (Exception)
                {
                    // Validation should never throw on a parsed AST, but be defensive.
                }
            }

            // Phase 5: Return validation (Calor0205). Always runs when an AST is
            // available so the IDE surfaces value-returned-from-void-owner errors.
            if (Ast != null)
            {
                try
                {
                    var returnValidator = new ReturnValidationPass(Diagnostics);
                    returnValidator.Check(Ast);
                }
                catch (Exception)
                {
                    // Validation should never throw on a parsed AST, but be defensive.
                }
            }

            // Populate DiagnosticsWithFixes from DiagnosticBag
            DiagnosticsWithFixes.AddRange(Diagnostics.DiagnosticsWithFixes);
        }
        catch (Exception ex)
        {
            // Log unexpected errors as diagnostics
            Diagnostics.ReportError(
                TextSpan.Empty,
                "Calor9999",
                $"Internal error: {ex.Message}");
        }
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
