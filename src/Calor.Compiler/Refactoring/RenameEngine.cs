using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Refactoring;

/// <summary>One identifier rewrite in one file.</summary>
public sealed record RenameEdit(string FilePath, TextSpan Span, string NewText);

/// <summary>
/// Why a rename was refused. Every refusal is explicit: the engine never
/// silently renames less than it should, because a partial rename produces a
/// program that still compiles and no longer means the same thing.
/// </summary>
public enum RenameRefusal
{
    None,
    SymbolNotFound,
    NotAnIdentifier,
    NameUnchanged,
    /// <summary>
    /// The declaration spans several files under one name (a module, or a type
    /// declared across files) but carries one identity per file, so the edit set
    /// would cover only one part and split the declaration. See #922.
    /// </summary>
    SplitDeclaration,
    /// <summary>An occurrence's span does not hold the symbol's current name.</summary>
    InexactOccurrence,
    /// <summary>The new name already denotes something where the symbol is used.</summary>
    NameCollision,
    /// <summary>
    /// A type declaration. Type *references* (§B bindings, §NEW, parameter and
    /// return types) are not indexed yet, so renaming the declaration alone
    /// would leave every use pointing at a name that no longer exists. Refusing
    /// keeps the engine from producing a broken program; indexing them is the
    /// follow-up.
    /// </summary>
    TypeReferencesNotIndexed,
}

public sealed record RenameResult(
    RenameRefusal Refusal,
    string? OldName,
    IReadOnlyList<RenameEdit> Edits)
{
    public bool Succeeded => Refusal == RenameRefusal.None && Edits.Count > 0;
}

/// <summary>
/// SymbolId-addressed rename over a <see cref="ProjectSymbolIndex"/>.
///
/// The instrument behind roadmap §2.5 gate 4. Renames are addressed by identity,
/// never by text, and every edit targets an exact identifier token.
/// </summary>
public static class RenameEngine
{
    public static RenameResult Rename(
        ProjectSymbolIndex index,
        SymbolId symbolId,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (!IsValidIdentifier(newName))
            return Refuse(RenameRefusal.NotAnIdentifier);

        var occurrences = index.Occurrences(symbolId);
        if (occurrences.Count == 0)
            return Refuse(RenameRefusal.SymbolNotFound);

        if (occurrences.Any(occurrence => occurrence.IsSplitDeclaration))
            return Refuse(RenameRefusal.SplitDeclaration);

        if (occurrences.Any(occurrence => occurrence.IsTypeDeclaration))
            return Refuse(RenameRefusal.TypeReferencesNotIndexed);

        var sources = index.Documents.ToDictionary(
            document => document.FilePath,
            document => document.Source,
            StringComparer.Ordinal);

        var definition = occurrences.FirstOrDefault(
            occurrence => occurrence.Kind == SymbolOccurrenceKind.Definition)
            ?? occurrences[0];
        var oldName = sources[definition.FilePath]
            .Substring(definition.Span.Start, definition.Span.Length);

        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return Refuse(RenameRefusal.NameUnchanged, oldName);

        // Every occurrence must currently read as the symbol's own name. A span
        // that holds anything else means the index and the text disagree, and
        // the safe response is to touch nothing.
        foreach (var occurrence in occurrences)
        {
            var source = sources[occurrence.FilePath];
            if (occurrence.Span.End > source.Length
                || !source.AsSpan(occurrence.Span.Start, occurrence.Span.Length)
                    .SequenceEqual(oldName.AsSpan()))
            {
                return Refuse(RenameRefusal.InexactOccurrence, oldName);
            }
        }

        // Capture check. The behaviour oracle exists because a capturing rename
        // still compiles; refusing the ones we can see keeps the corpus honest
        // about which cases are prevented rather than merely detected.
        if (WouldCollide(index, symbolId, occurrences, newName))
            return Refuse(RenameRefusal.NameCollision, oldName);

        var edits = occurrences
            .Select(occurrence => new RenameEdit(occurrence.FilePath, occurrence.Span, newName))
            .OrderBy(edit => edit.FilePath, StringComparer.Ordinal)
            .ThenByDescending(edit => edit.Span.Start)
            .ToArray();

        return new RenameResult(RenameRefusal.None, oldName, edits);
    }

    /// <summary>
    /// Applies edits to in-memory sources. Edits are applied back-to-front per
    /// file so earlier spans stay valid.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Apply(
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyList<RenameEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(edits);

        var updated = new Dictionary<string, string>(sources, StringComparer.Ordinal);
        foreach (var group in edits.GroupBy(edit => edit.FilePath, StringComparer.Ordinal))
        {
            var text = updated[group.Key];
            foreach (var edit in group.OrderByDescending(edit => edit.Span.Start))
                text = text[..edit.Span.Start] + edit.NewText + text[edit.Span.End..];
            updated[group.Key] = text;
        }

        return updated;
    }

    /// <summary>
    /// True when some other symbol already declares <paramref name="newName"/>
    /// in a file the renamed symbol appears in. Deliberately conservative and
    /// file-scoped: it is a guard, not a scope analysis, and the apply-and-run
    /// oracle is what actually establishes that behaviour is preserved.
    /// </summary>
    private static bool WouldCollide(
        ProjectSymbolIndex index,
        SymbolId symbolId,
        IReadOnlyList<SymbolOccurrence> occurrences,
        string newName)
    {
        var touchedFiles = occurrences
            .Select(occurrence => occurrence.FilePath)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var document in index.Documents)
        {
            if (!touchedFiles.Contains(document.FilePath))
                continue;

            foreach (var symbol in document.BoundModule.SymbolsById.Values)
            {
                if (symbol.Id == symbolId || symbol.Id.IsNone)
                    continue;
                if (string.Equals(symbol.Name, newName, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static RenameResult Refuse(RenameRefusal refusal, string? oldName = null) =>
        new(refusal, oldName, Array.Empty<RenameEdit>());

    private static bool IsValidIdentifier(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        return name.Skip(1).All(character =>
            char.IsLetterOrDigit(character) || character == '_');
    }
}
