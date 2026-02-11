using Calor.Compiler.Parsing;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Calor.LanguageServer.Utilities;

/// <summary>
/// Converts between LSP positions (0-based) and Calor positions (1-based).
/// </summary>
public static class PositionConverter
{
    /// <summary>
    /// Convert a Calor TextSpan to an LSP Range.
    /// LSP is 0-based, Calor TextSpan is 1-based.
    /// </summary>
    public static LspRange ToLspRange(TextSpan span, string source)
    {
        // TextSpan has Line and Column as 1-based, LSP uses 0-based
        var startLine = span.Line - 1;
        var startChar = span.Column - 1;

        // Calculate end position by finding the position in source
        var endLine = startLine;
        var endChar = startChar;

        // Walk through the span to find the end position
        var pos = span.Start;
        var endPos = span.End;
        var line = startLine;
        var col = startChar;

        for (var i = span.Start; i < endPos && i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                col = 0;
            }
            else
            {
                col++;
            }
        }

        endLine = line;
        endChar = col;

        return new LspRange(
            new Position(startLine, startChar),
            new Position(endLine, endChar)
        );
    }

    /// <summary>
    /// Convert a Calor TextSpan to an LSP Range using only line/column info.
    /// Use when source is not available and span length is known to be single line.
    /// </summary>
    public static LspRange ToLspRangeSingleLine(TextSpan span)
    {
        var startLine = span.Line - 1;
        var startChar = span.Column - 1;
        var endChar = startChar + span.Length;

        return new LspRange(
            new Position(startLine, startChar),
            new Position(startLine, endChar)
        );
    }

    /// <summary>
    /// Convert an LSP Position to a Calor position (line, column).
    /// LSP is 0-based, Calor is 1-based.
    /// </summary>
    public static (int Line, int Column) ToCalorPosition(Position position)
    {
        return (position.Line + 1, position.Character + 1);
    }

    /// <summary>
    /// Convert an LSP Position to an absolute offset in the source text.
    /// </summary>
    public static int ToOffset(Position position, string source)
    {
        var targetLine = position.Line;
        var targetChar = position.Character;

        var currentLine = 0;
        var offset = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (currentLine == targetLine)
            {
                return offset + targetChar;
            }

            if (source[i] == '\n')
            {
                currentLine++;
                offset = i + 1;
            }
        }

        // If we're at the target line, add the character offset
        if (currentLine == targetLine)
        {
            return offset + targetChar;
        }

        return source.Length;
    }

    /// <summary>
    /// Find the TextSpan at a given LSP position in the source.
    /// Returns a zero-length span at the position.
    /// </summary>
    public static TextSpan FromLspPosition(Position position, string source)
    {
        var offset = ToOffset(position, source);
        var (line, column) = ToCalorPosition(position);
        return new TextSpan(offset, 0, line, column);
    }
}
