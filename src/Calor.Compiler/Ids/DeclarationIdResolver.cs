using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Ids;

/// <summary>
/// Maps a diagnostic's position to the ID of its nearest enclosing declaration
/// (envelope schema v1 <c>declarationId</c>, loop plan D1.1). Built once per
/// compilation from parsed modules; resolution is byte-offset containment against
/// the full-extent spans that <see cref="IdScanner"/> records, picking the
/// innermost containing declaration that carries a non-empty ID.
/// </summary>
public sealed class DeclarationIdResolver
{
    private sealed record FileIndex(string Source, List<IdEntry> Entries);

    private readonly Dictionary<string, FileIndex> _files = new(StringComparer.Ordinal);

    /// <summary>Registers a parsed file. Entries with empty IDs are dropped (IDs are optional per language policy).</summary>
    public void AddFile(string filePath, string source, ModuleNode module)
    {
        var entries = new IdScanner()
            .Scan(module, filePath)
            .Where(e => !string.IsNullOrEmpty(e.Id))
            .ToList();
        _files[Normalize(filePath)] = new FileIndex(source, entries);
    }

    /// <summary>
    /// Resolves the enclosing declaration ID for a diagnostic, or null when the
    /// file is unknown, the position falls outside every ID-bearing declaration,
    /// or no ancestor declaration carries an ID.
    /// </summary>
    public string? Resolve(Diagnostic diagnostic)
        => Resolve(diagnostic.FilePath, diagnostic.Span.Start, diagnostic.Span.Length,
            diagnostic.Span.Line, diagnostic.Span.Column);

    /// <summary>Position-based overload for producers that don't hold a <see cref="Diagnostic"/>.</summary>
    public string? Resolve(string? filePath, int spanStart, int spanLength, int line, int column)
    {
        var index = Lookup(filePath);
        if (index == null)
            return null;

        // Diagnostics built from the (code, severity, message, file, line, column)
        // constructor carry a zero span — reconstruct the offset from line/column.
        var offset = spanStart == 0 && spanLength == 0 && (line > 1 || column > 1)
            ? OffsetOf(index.Source, line, column)
            : spanStart;
        if (offset < 0 || offset > index.Source.Length)
            return null;

        // Innermost containing declaration = largest span start among containers
        // (sibling declaration spans don't overlap; ancestors strictly contain).
        IdEntry? best = null;
        foreach (var entry in index.Entries)
        {
            if (entry.Span.Start <= offset && offset <= entry.Span.End)
            {
                if (best == null || entry.Span.Start > best.Span.Start)
                    best = entry;
            }
        }
        return best?.Id;
    }

    private FileIndex? Lookup(string? filePath)
    {
        if (filePath != null && _files.TryGetValue(Normalize(filePath), out var exact))
            return exact;

        // Single-file compilations frequently report diagnostics without a file
        // path; the only registered file is unambiguous.
        if (filePath == null && _files.Count == 1)
            return _files.Values.First();

        // Fall back to basename match (some passes report relative paths).
        if (filePath != null)
        {
            var name = Path.GetFileName(filePath);
            var matches = _files.Where(kv => Path.GetFileName(kv.Key) == name).ToList();
            if (matches.Count == 1)
                return matches[0].Value;
        }

        return null;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>Converts a 1-based line/column to a byte offset, or -1 when out of range.</summary>
    private static int OffsetOf(string source, int line, int column)
    {
        if (line < 1 || column < 1)
            return -1;

        var offset = 0;
        for (var currentLine = 1; currentLine < line; currentLine++)
        {
            var nl = source.IndexOf('\n', offset);
            if (nl < 0)
                return -1;
            offset = nl + 1;
        }

        return offset + (column - 1);
    }
}
