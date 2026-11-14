using System.Text;

namespace Calor.Compiler.LegacySyntax;

/// <summary>Reads and writes text files without disturbing their BOM or line endings.</summary>
internal sealed class TextFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    public required string Path { get; init; }
    public required string Text { get; init; }
    public required bool HasBom { get; init; }

    public static TextFile Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Utf8NoBom.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        return new TextFile { Path = path, Text = text, HasBom = bom };
    }

    public void Write(string text)
    {
        var body = Utf8NoBom.GetBytes(text);
        using var stream = File.Create(Path);
        if (HasBom)
            stream.Write([0xEF, 0xBB, 0xBF]);
        stream.Write(body);
    }

    /// <summary>"\r\n", "\n", or null when the text mixes both (or has no line break).</summary>
    public static string? UniformNewline(string text)
    {
        var crlf = 0;
        var lf = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;
            if (i > 0 && text[i - 1] == '\r') crlf++;
            else lf++;
        }
        if (crlf > 0 && lf == 0) return "\r\n";
        if (lf > 0 && crlf == 0) return "\n";
        return null;
    }

    /// <summary>
    /// Shapes printer output (LF, one trailing newline) like the text it replaces: same
    /// newline sequence and the same presence/absence of a trailing newline.
    /// </summary>
    public static string Shape(string native, string original, string newline)
    {
        var body = native.Replace("\r\n", "\n").TrimEnd('\n');
        var trailing = original.EndsWith('\n');
        body = body.Replace("\n", newline);
        return trailing ? body + newline : body;
    }
}
