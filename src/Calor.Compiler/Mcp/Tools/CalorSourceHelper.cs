using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Helper for parsing Calor source code in MCP tools.
/// </summary>
internal static class CalorSourceHelper
{
    /// <summary>
    /// Parses Calor source code and returns the AST if successful.
    /// </summary>
    public static ParseResult Parse(string source, string? filePath = null)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath ?? "mcp-input.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        if (diagnostics.HasErrors)
        {
            return ParseResult.Failed(diagnostics.Errors.Select(e => e.Message).ToList(), diagnostics);
        }

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (diagnostics.HasErrors)
        {
            return ParseResult.Failed(diagnostics.Errors.Select(e => e.Message).ToList(), diagnostics);
        }

        return ParseResult.Success(ast, source);
    }

    /// <summary>
    /// Fault-tolerant parse (loop plan WS2 D2.5): always returns the parser's
    /// best-effort AST alongside the diagnostics instead of discarding it on
    /// the first error. The recursive-descent parser has localized recovery
    /// (synchronization points, dummy-value insertion), so a near-miss file
    /// typically yields a tree with the broken region elided and every other
    /// declaration intact — enough to attribute errors to their enclosing
    /// declaration and to keep project-wide checks seeing the file's surface.
    /// A partial result is never a compile pass: <see cref="ParseResult.IsSuccess"/>
    /// stays false, and verdict logic must not treat a partial AST as valid.
    /// </summary>
    public static ParseResult ParseTolerant(string source, string? filePath = null)
    {
        var diagnostics = new DiagnosticBag();
        var effectivePath = filePath ?? "mcp-input.calr";
        diagnostics.SetFilePath(effectivePath);

        try
        {
            var lexer = new Lexer(source, diagnostics);
            var tokens = lexer.TokenizeAllForParser();

            // Unlike the strict path, lexer errors do not stop the parse:
            // the token stream that was produced is usually still walkable.
            var parser = new Parser(tokens, diagnostics);
            var ast = parser.Parse();

            if (!diagnostics.HasErrors)
                return ParseResult.Success(ast, source);

            return ParseResult.Partial(ast, source,
                diagnostics.Errors.Select(e => e.Message).ToList(), diagnostics, effectivePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Recovery gave up somewhere unrecoverable — tolerant parsing
            // must degrade to a plain failure, never throw.
            if (!diagnostics.HasErrors)
            {
                return ParseResult.Failed(new List<string> { $"Parse failed: {ex.Message}" });
            }

            return ParseResult.Failed(diagnostics.Errors.Select(e => e.Message).ToList(), diagnostics);
        }
    }

    /// <summary>
    /// Reads a file and parses it.
    /// </summary>
    public static async Task<ParseResult> ParseFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return ParseResult.Failed(new List<string> { $"File not found: {filePath}" });
        }

        var source = await File.ReadAllTextAsync(filePath);
        return Parse(source, filePath);
    }

    /// <summary>
    /// Converts 1-based line/column to character offset.
    /// </summary>
    public static int GetOffset(string source, int line, int column)
    {
        var currentLine = 1;
        var offset = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (currentLine == line)
            {
                return offset + column - 1;
            }

            if (source[i] == '\n')
            {
                currentLine++;
                offset = i + 1;
            }
        }

        if (currentLine == line)
        {
            return offset + column - 1;
        }

        return source.Length;
    }

    /// <summary>
    /// Converts character offset to 1-based line/column.
    /// </summary>
    public static (int Line, int Column) GetPosition(string source, int offset)
    {
        var line = 1;
        var column = 1;

        for (var i = 0; i < Math.Min(offset, source.Length); i++)
        {
            if (source[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    /// <summary>
    /// Gets a preview of source code around a position.
    /// </summary>
    public static string GetPreview(string source, int line, int maxLength = 80)
    {
        var lines = source.Split('\n');
        if (line < 1 || line > lines.Length)
            return "";

        var lineContent = lines[line - 1].TrimEnd('\r');
        if (lineContent.Length > maxLength)
            return lineContent.Substring(0, maxLength) + "...";

        return lineContent;
    }
}

/// <summary>
/// Result of parsing Calor source code.
/// </summary>
internal sealed class ParseResult
{
    public bool IsSuccess { get; }
    public ModuleNode? Ast { get; }
    public string? Source { get; }
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// The diagnostic bag from the lex/parse, when one exists. Lets tools emit
    /// envelope schema v1.1 entries instead of flattened message strings.
    /// </summary>
    public DiagnosticBag? Diagnostics { get; }

    /// <summary>File path the source was parsed as, when known (used for declaration-id attribution).</summary>
    public string? FilePath { get; }

    /// <summary>
    /// True when the parse failed but a best-effort AST is available
    /// (<see cref="CalorSourceHelper.ParseTolerant"/>). A partial AST is for
    /// diagnostics enrichment and surface-level scanning only — never treat
    /// it as a compile pass.
    /// </summary>
    public bool IsPartial => !IsSuccess && Ast != null;

    private ParseResult(bool success, ModuleNode? ast, string? source, IReadOnlyList<string> errors,
        DiagnosticBag? diagnostics, string? filePath = null)
    {
        IsSuccess = success;
        Ast = ast;
        Source = source;
        Errors = errors;
        Diagnostics = diagnostics;
        FilePath = filePath;
    }

    public static ParseResult Success(ModuleNode ast, string source)
        => new(true, ast, source, Array.Empty<string>(), null);

    public static ParseResult Failed(IReadOnlyList<string> errors, DiagnosticBag? diagnostics = null)
        => new(false, null, null, errors, diagnostics);

    public static ParseResult Partial(ModuleNode ast, string source, IReadOnlyList<string> errors,
        DiagnosticBag diagnostics, string filePath)
        => new(false, ast, source, errors, diagnostics, filePath);

    /// <summary>
    /// Parse errors as envelope schema v1.1 entries (shared EnvelopeDiagnostic
    /// shape, loop plan D1.3). Parsing failed, so there is no AST and
    /// declarationId is null. Falls back to message-only entries for failures
    /// that produced no diagnostic bag (e.g. file-not-found).
    /// </summary>
    public List<EnvelopeDiagnostic> ToEnvelopeDiagnostics()
    {
        if (IsSuccess)
            return new List<EnvelopeDiagnostic>();

        if (Diagnostics != null)
        {
            // A partial AST lets parse errors carry declarationId — the
            // enclosing declaration resolved by span containment (D2.5): the
            // agent learns WHICH function is broken, not just where.
            Ids.DeclarationIdResolver? resolver = null;
            if (Ast != null && Source != null)
            {
                resolver = new Ids.DeclarationIdResolver();
                resolver.AddFile(FilePath ?? "mcp-input.calr", Source, Ast);
            }

            return DiagnosticEnvelope.Build(Diagnostics, resolver)
                .Where(e => e.Severity == "error")
                .ToList();
        }

        return Errors.Select(message => new EnvelopeDiagnostic
        {
            // Message-only failures carry no code; classify the common
            // missing-input case as CliInputNotFound so it matches the code
            // other surfaces emit for the same condition (review of #757 item 3),
            // and everything else as CliInternalError.
            Code = message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticCode.CliInputNotFound
                : DiagnosticCode.CliInternalError,
            Message = message,
            Severity = "error",
            Location = new EnvelopeLocation { File = null, Line = 1, Column = 1, Length = 0 }
        }).ToList();
    }
}
