using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Refactoring;

/// <summary>
/// Where an identifier for a symbol appears in a source file.
/// </summary>
public enum SymbolOccurrenceKind
{
    Definition,
    Reference,
}

/// <summary>
/// One identifier token belonging to one symbol. <see cref="Span"/> always
/// covers exactly the identifier, never the surrounding tag or attribute block,
/// so an edit built from it rewrites the name and nothing else.
/// </summary>
public sealed record SymbolOccurrence(
    string FilePath,
    SymbolId SymbolId,
    TextSpan Span,
    SymbolOccurrenceKind Kind,
    bool IsSplitDeclaration,
    bool IsTypeDeclaration = false);

/// <summary>
/// One parsed and bound file in the project.
/// </summary>
public sealed record IndexedDocument(
    string FilePath,
    string Source,
    ModuleNode Ast,
    BoundModule BoundModule);

/// <summary>
/// A call resolved to a declaration, attributed to the function containing it.
/// </summary>
public sealed record ProjectCallEdge(
    SymbolId CallerSymbolId,
    SymbolId CalleeSymbolId,
    string FilePath,
    TextSpan Span);

/// <summary>
/// What resolution could not account for. Kept beside the edges rather than
/// discarded: a caller list computed from partial resolution is only honest if
/// the part it could not resolve travels with it.
/// </summary>
public sealed record ProjectResolutionResidual(
    IReadOnlyList<string> UnresolvedCalls,
    IReadOnlyList<string> AmbiguousCallees);

/// <summary>
/// A project-wide map from <see cref="SymbolId"/> to the identifier tokens that
/// denote it, built from bound trees rather than from text.
///
/// This is the identity substrate the rename harness addresses (roadmap §2.5
/// gate 4). It lives in the compiler, not the language server, because the
/// dependency runs language-server → compiler: the LSP is a *consumer* of these
/// identities, not their owner.
/// </summary>
public sealed class ProjectSymbolIndex
{
    private readonly Dictionary<SymbolId, List<SymbolOccurrence>> _bySymbol = [];
    private readonly Dictionary<string, List<SymbolOccurrence>> _byFile;
    private readonly List<ProjectCallEdge> _callEdges = [];
    private readonly List<string> _unresolvedCalls = [];
    private readonly SortedSet<string> _ambiguousCallees = new(StringComparer.Ordinal);

    public IReadOnlyList<IndexedDocument> Documents { get; }

    /// <summary>Calls resolved to a declaration, attributed to their containing function.</summary>
    public IReadOnlyList<ProjectCallEdge> CallEdges => _callEdges;

    /// <summary>What resolution could not account for; travels with the edges.</summary>
    public ProjectResolutionResidual Residual =>
        new(_unresolvedCalls, [.. _ambiguousCallees]);

    private ProjectSymbolIndex(IReadOnlyList<IndexedDocument> documents)
    {
        Documents = documents;
        _byFile = documents.ToDictionary(
            document => document.FilePath,
            _ => new List<SymbolOccurrence>(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Parses and binds every file, then indexes the identifier tokens.
    /// Files that fail to parse or bind are skipped and reported in
    /// <paramref name="skipped"/> — the index never guesses at a broken file,
    /// because a rename derived from a partial tree is a rename that edits the
    /// wrong tokens.
    /// </summary>
    public static ProjectSymbolIndex Build(
        IEnumerable<string> filePaths,
        out IReadOnlyList<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var documents = new List<IndexedDocument>();
        var failed = new List<string>();

        foreach (var path in filePaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            var source = File.ReadAllText(path);
            var parseDiagnostics = new DiagnosticBag();
            var parser = new Parser(new Lexer(source, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics);
            ModuleNode? ast;
            try
            {
                ast = parser.Parse();
            }
            catch (Exception)
            {
                failed.Add(path);
                continue;
            }

            if (ast == null || parseDiagnostics.HasErrors)
            {
                failed.Add(path);
                continue;
            }

            var bindDiagnostics = new DiagnosticBag();
            BoundModule bound;
            try
            {
                bound = new Binder(bindDiagnostics, path).Bind(ast);
            }
            catch (Exception)
            {
                failed.Add(path);
                continue;
            }

            documents.Add(new IndexedDocument(path, source, ast, bound));
        }

        skipped = failed;
        var index = new ProjectSymbolIndex(documents);
        index.Populate();
        return index;
    }

    /// <summary>Every indexed occurrence in one file, in document order.</summary>
    public IReadOnlyList<SymbolOccurrence> OccurrencesIn(string filePath) =>
        _byFile.TryGetValue(filePath, out var occurrences)
            ? occurrences
            : Array.Empty<SymbolOccurrence>();

    public IReadOnlyList<SymbolOccurrence> Occurrences(SymbolId symbolId) =>
        _bySymbol.TryGetValue(symbolId, out var occurrences)
            ? occurrences
            : Array.Empty<SymbolOccurrence>();

    /// <summary>
    /// The symbol denoted by the identifier at <paramref name="offset"/>, or
    /// null when the position is not on an indexed identifier or is ambiguous
    /// between symbols. Ambiguity resolves to null rather than to a guess.
    /// </summary>
    public SymbolOccurrence? Resolve(string filePath, int offset)
    {
        if (!_byFile.TryGetValue(filePath, out var occurrences))
            return null;

        var candidates = occurrences
            .Where(occurrence => occurrence.Span.Start <= offset && offset < occurrence.Span.End)
            .GroupBy(occurrence => occurrence.SymbolId)
            .Select(group => group.OrderBy(occurrence => occurrence.Span.Length).First())
            .OrderBy(occurrence => occurrence.Span.Length)
            .ToArray();
        if (candidates.Length == 0)
            return null;

        var shortest = candidates
            .Where(occurrence => occurrence.Span.Length == candidates[0].Span.Length)
            .ToArray();
        return shortest.Length == 1 ? shortest[0] : null;
    }

    private void Populate()
    {
        // A module, and a type declared across several files, is one declaration
        // in the language but one symbol per file here. Renaming from such a
        // declaration would edit a single part and split it, with no Calor
        // diagnostic to show for it (the break surfaces only in generated C#).
        // They are marked so the engine can refuse. See #922.
        var moduleDocumentCounts = Documents
            .GroupBy(document => document.Ast.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var importedNamespaces = Documents
            .SelectMany(document => document.Ast.Usings)
            .Select(directive => directive.Namespace)
            .ToHashSet(StringComparer.Ordinal);
        var typeDocumentCounts = Documents
            .SelectMany(document => document.BoundModule.SymbolsById.Values
                .OfType<TypeSymbol>()
                .Select(symbol => (Module: document.Ast.Name, Type: symbol.Name)))
            .GroupBy(pair => pair)
            .ToDictionary(group => group.Key, group => group.Count());

        var seen = new HashSet<(string File, SymbolId Id, int Start, SymbolOccurrenceKind Kind)>();

        foreach (var document in Documents)
        {
            foreach (var symbol in document.BoundModule.SymbolsById.Values)
            {
                if (symbol.Id.IsNone || !IsExactDeclaration(document.Source, symbol))
                    continue;

                var split = symbol is TypeSymbol
                    && typeDocumentCounts.TryGetValue(
                        (document.Ast.Name, symbol.Name),
                        out var declaringDocuments)
                    && declaringDocuments > 1;
                Add(document, symbol.Id, symbol.DeclarationSpan,
                    SymbolOccurrenceKind.Definition, split,
                    isTypeDeclaration: symbol is TypeSymbol);
            }

            foreach (var node in Descendants(document.BoundModule))
            {
                switch (node)
                {
                    case BoundVariableExpression variable:
                        foreach (var symbol in variable.ResolvedSymbols)
                        {
                            if (!symbol.Id.IsNone)
                            {
                                Add(document, symbol.Id, variable.Span,
                                    SymbolOccurrenceKind.Reference, false);
                            }
                        }
                        break;

                    case BoundFieldAccessExpression field:
                        foreach (var fieldSymbol in field.ResolvedFields)
                        {
                            if (!fieldSymbol.Id.IsNone)
                            {
                                Add(document, fieldSymbol.Id, field.FieldNameSpan,
                                    SymbolOccurrenceKind.Reference, false);
                            }
                        }
                        break;

                    case BoundCallExpression call:
                        if (call.ResolvedSymbols.Count > 0)
                        {
                            foreach (var symbol in call.ResolvedSymbols)
                            {
                                if (!symbol.Id.IsNone)
                                {
                                    Add(document, symbol.Id, call.CalleeSpan,
                                        SymbolOccurrenceKind.Reference, false);
                                }
                            }
                        }
                        else if (ResolveAcrossDocuments(call) is { } crossModule)
                        {
                            Add(document, crossModule.Id, call.CalleeSpan,
                                SymbolOccurrenceKind.Reference, false);
                        }
                        break;

                    default:
                        // No occurrence for this node kind. The traversal is
                        // universal (every node is reached through ChildNodes),
                        // so this arm is only about which nodes *carry* a
                        // renameable identifier. A newly added identifier-bearing
                        // bound node would land here and its references would go
                        // unindexed — silently, since a rename that misses
                        // references still produces a compiling program. That is
                        // why rename correctness is established by the
                        // apply-recompile-and-run oracle over the rename corpus,
                        // not by this switch being complete.
                        break;
                }
            }

            // Call edges are collected per function rather than from a flat walk,
            // because an edge needs the function that CONTAINS the call, and a
            // flat walk of the module has no notion of "containing".
            foreach (var function in document.BoundModule.Functions)
            {
                foreach (var node in Descendants(function))
                {
                    if (node is not BoundCallExpression call)
                        continue;

                    var callee = call.ResolvedSymbols.Count > 0
                        ? call.ResolvedSymbols[0]
                        : ResolveAcrossDocuments(call);
                    if (callee is { Id.IsNone: false })
                    {
                        _callEdges.Add(new ProjectCallEdge(
                            function.Symbol.Id, callee.Id, document.FilePath, call.CalleeSpan));
                    }
                    else
                    {
                        // Named, not counted: "3 unresolved" tells a reader
                        // nothing they can act on.
                        _unresolvedCalls.Add(
                            $"{document.FilePath}: {call.Target}");
                        if (IsAmbiguousAcrossDocuments(call))
                            _ambiguousCallees.Add(call.Target);
                    }
                }
            }

            // Module names are indexed so a rename can be refused explicitly
            // rather than silently doing nothing.
            var moduleId = SymbolId.Create(
                "source",
                SymbolSourceIdentity.Canonicalize(document.FilePath),
                "module",
                document.Ast.Id);
            var moduleSplit =
                (moduleDocumentCounts.TryGetValue(document.Ast.Name, out var declaringModules)
                    && declaringModules > 1)
                || importedNamespaces.Contains(document.Ast.Name);
            Add(document, moduleId, document.Ast.IdentifierSpan,
                SymbolOccurrenceKind.Definition, moduleSplit);
        }

        // Each file is bound on its own, so a call into another module resolves
        // to nothing locally. Without this, a cross-file call site is invisible
        // and renaming the callee leaves the caller pointing at a name that no
        // longer exists. Matching is by bare callee name across the project and
        // requires exactly one candidate: ambiguity yields no occurrence, which
        // makes the rename refuse rather than guess.
        FunctionSymbol? ResolveAcrossDocuments(BoundCallExpression call)
        {
            var target = call.Target;
            if (string.IsNullOrEmpty(target) || target.Contains('.', StringComparison.Ordinal))
                return null;

            var candidates = Documents
                .SelectMany(candidate => candidate.BoundModule.SymbolsById.Values
                    .OfType<FunctionSymbol>())
                .Where(symbol => !symbol.Id.IsNone
                    && string.Equals(
                        BareFunctionName(symbol.Name),
                        target,
                        StringComparison.Ordinal))
                .DistinctBy(symbol => symbol.Id)
                .Take(2)
                .ToArray();

            return candidates.Length == 1 ? candidates[0] : null;
        }

        // Distinguishes "no such name" from "several declarations share it".
        // Both drop the edge, but only the second is a resolution *limit* worth
        // reporting separately — it is what 0.14's typed signatures fix.
        bool IsAmbiguousAcrossDocuments(BoundCallExpression call)
        {
            var target = call.Target;
            if (string.IsNullOrEmpty(target) || target.Contains('.', StringComparison.Ordinal))
                return false;

            return Documents
                .SelectMany(candidate => candidate.BoundModule.SymbolsById.Values
                    .OfType<FunctionSymbol>())
                .Where(symbol => !symbol.Id.IsNone
                    && string.Equals(
                        BareFunctionName(symbol.Name), target, StringComparison.Ordinal))
                .DistinctBy(symbol => symbol.Id)
                .Take(2)
                .Count() > 1;
        }

        void Add(
            IndexedDocument document,
            SymbolId symbolId,
            TextSpan span,
            SymbolOccurrenceKind kind,
            bool isSplitDeclaration,
            bool isTypeDeclaration = false)
        {
            if (!IsExactIdentifier(document.Source, span))
                return;
            if (!seen.Add((document.FilePath, symbolId, span.Start, kind)))
                return;

            var occurrence = new SymbolOccurrence(
                document.FilePath, symbolId, span, kind, isSplitDeclaration,
                isTypeDeclaration);
            _byFile[document.FilePath].Add(occurrence);
            if (!_bySymbol.TryGetValue(symbolId, out var list))
            {
                list = [];
                _bySymbol[symbolId] = list;
            }
            list.Add(occurrence);
        }
    }

    internal static string BareFunctionName(string name)
    {
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
            name = name[(lastDot + 1)..];
        var generic = name.IndexOf('<');
        return generic > 0 ? name[..generic] : name;
    }

    private static bool IsExactDeclaration(string source, Symbol symbol)
    {
        if (!IsExactIdentifier(source, symbol.DeclarationSpan))
            return false;

        var text = source.Substring(symbol.DeclarationSpan.Start, symbol.DeclarationSpan.Length);
        var name = symbol.Name;
        if (symbol is FunctionSymbol)
        {
            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
                name = name[(lastDot + 1)..];
            var generic = name.IndexOf('<');
            if (generic > 0)
                name = name[..generic];
        }

        return string.Equals(text, name, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the span covers exactly one identifier token. This is the
    /// guard that keeps an edit from rewriting a tag, an attribute block, or a
    /// qualified name: anything that is not a bare identifier is not renameable.
    /// </summary>
    private static bool IsExactIdentifier(string source, TextSpan span)
    {
        if (span.Length <= 0 || span.Start < 0 || span.End > source.Length)
            return false;
        if (!char.IsLetter(source[span.Start]) && source[span.Start] != '_')
            return false;

        for (var offset = span.Start + 1; offset < span.End; offset++)
        {
            if (!char.IsLetterOrDigit(source[offset]) && source[offset] != '_')
                return false;
        }

        return true;
    }

    private static IEnumerable<BoundNode> Descendants(BoundNode node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
