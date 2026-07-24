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

    private ParseResult(bool success, ModuleNode? ast, string? source, IReadOnlyList<string> errors, DiagnosticBag? diagnostics)
    {
        IsSuccess = success;
        Ast = ast;
        Source = source;
        Errors = errors;
        Diagnostics = diagnostics;
    }

    public static ParseResult Success(ModuleNode ast, string source)
        => new(true, ast, source, Array.Empty<string>(), null);

    public static ParseResult Failed(IReadOnlyList<string> errors, DiagnosticBag? diagnostics = null)
        => new(false, null, null, errors, diagnostics);

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
            return DiagnosticEnvelope.Build(Diagnostics)
                .Where(e => e.Severity == "error")
                .ToList();
        }

        return Errors.Select(message => new EnvelopeDiagnostic
        {
            Code = DiagnosticCode.CliInternalError,
            Message = message,
            Severity = "error",
            Location = new EnvelopeLocation { File = null, Line = 1, Column = 1, Length = 0 }
        }).ToList();
    }
}
