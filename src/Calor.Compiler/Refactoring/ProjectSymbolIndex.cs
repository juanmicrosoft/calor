using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;
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
/// <param name="BindHadErrors">v0.15 E5 — whether the binder reported an error
/// for this file. The CLI skips the effect pass on such a file, so the index's
/// effects facet skips it too (and says so in its residual) rather than
/// recording rows the CLI would never have computed.</param>
public sealed record IndexedDocument(
    string FilePath,
    string Source,
    ModuleNode Ast,
    BoundModule BoundModule,
    bool BindHadErrors = false)
{
    /// <summary>
    /// #1738: the binding that produced <see cref="BoundModule"/>, offered to the effect
    /// pass, which reuses it only when it is the bind it would make itself. Null when the
    /// AST changed while it was bound.
    /// </summary>
    internal Analysis.CallGraphAnalysis.PriorModuleBinding? Binding { get; init; }
}

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
    IReadOnlyList<ProjectUnresolvedCall> UnresolvedCalls,
    IReadOnlyList<string> AmbiguousCallees);

/// <summary>
/// A call that resolved to nothing, recorded WITH the function it sits in.
///
/// The containing function is not decoration: "what does X call?" is partial
/// exactly when a call inside X failed to resolve, and without the caller the
/// residual cannot answer that. The gate-3 golden corpus caught this — the first
/// version recorded only the target name.
/// </summary>
public sealed record ProjectUnresolvedCall(
    SymbolId CallerSymbolId,
    string Target,
    string FilePath);

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
    private readonly List<ProjectUnresolvedCall> _unresolvedCalls = [];
    private readonly SortedSet<string> _ambiguousCallees = new(StringComparer.Ordinal);

    public IReadOnlyList<IndexedDocument> Documents { get; }
    internal CrossModuleTypeRegistry SourceTypes { get; }

    /// <summary>The cross-module function map every document was bound over (null for one file).</summary>
    internal IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? CrossModuleFunctions { get; private init; }

    /// <summary>Calls resolved to a declaration, attributed to their containing function.</summary>
    public IReadOnlyList<ProjectCallEdge> CallEdges => _callEdges;

    /// <summary>What resolution could not account for; travels with the edges.</summary>
    public ProjectResolutionResidual Residual =>
        new(_unresolvedCalls, [.. _ambiguousCallees]);

    private ProjectSymbolIndex(IReadOnlyList<IndexedDocument> documents, CrossModuleTypeRegistry sourceTypes)
    {
        Documents = documents;
        SourceTypes = sourceTypes;
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
        out IReadOnlyList<string> skipped,
        bool synchronizeIdentities = true,
        string? initializeIdentityRoot = null)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var documents = new List<IndexedDocument>();
        var failed = new List<string>();
        var parsed = new List<(string Path, string Source, ModuleNode Ast)>();
        var sourceHashes = new Dictionary<string, string>(BuildStateCache.GetPathComparer());

        // Reading, lexing and parsing a file read that file alone, so the files are parsed
        // concurrently (#1738) and taken in path order: the same hashes, the same skipped
        // files, and a read or lex failure surfaces as the first one in that order.
        var ordered = filePaths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var reads = Analysis.CallGraphAnalysis.ForEachModule(ordered, path =>
        {
            var bytes = File.ReadAllBytes(path);
            var hash = BuildStateCache.ComputeContentHash(bytes);
            var source = CompilationDriver.DecodeSource(bytes);
            var parseDiagnostics = new DiagnosticBag();
            var parser = new Parser(new Lexer(source, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics);
            ModuleNode? ast;
            try
            {
                ast = parser.Parse();
            }
            catch (Exception)
            {
                ast = null;
            }
            return (Hash: hash, Source: source, Ast: ast == null || parseDiagnostics.HasErrors ? null : ast);
        });
        for (var position = 0; position < ordered.Length; position++)
        {
            var path = ordered[position];
            var (hash, source, ast) = reads[position].Value;
            sourceHashes[Path.GetFullPath(path)] = hash;
            if (ast == null)
            {
                failed.Add(path);
                continue;
            }

            parsed.Add((path, source, ast));
        }
        // Syncing and attaching each reconcile the whole store over the same sources; the
        // declarations of an unchanged source are parsed once for both (#1738).
        using (Ids.DeclarationIdentityStore.ShareDeclarationParses())
        {
            if (initializeIdentityRoot != null)
                Ids.DeclarationIdentityStore.SyncActiveInputs(parsed.Select(document => document.Path), initializeIdentityRoot);
            // Explicit project enrollment above already reconciled every selected input.
            // Each sync reconciles its whole store, so syncing before attaching changes nothing
            // an attachment sees; attaching as one batch reconciles each store once, not per file.
            if (synchronizeIdentities && initializeIdentityRoot == null)
            {
                foreach (var (path, _, _) in parsed)
                    Ids.DeclarationIdentityStore.SyncIfRegistered(Path.GetFullPath(path));
            }
            Ids.DeclarationIdentityStore.AttachAllIfRegistered(
                parsed.Select(document => (Path.GetFullPath(document.Path), document.Source, document.Ast)));
        }
        var sourceTypes = CrossModuleTypeRegistry.Build(
            parsed.Select(document => (document.Ast, document.Path)).ToArray(),
            sourceHashes);
        // The driver binds every file of a multi-file build over its cross-module
        // function map; without it a native qualified call into another module
        // (`OrderService.SaveOrder()`) is an undefined name, and the whole file
        // would be skipped as a bind error that `calor build` never reports.
        var crossModuleFunctions = parsed.Count > 1
            ? CompilationDriver.BuildCrossModuleFunctionMap(parsed.Select(document => document.Ast).ToList())
            : null;
        // The prewarm runs on the thread pool while this thread binds: a binder that asks
        // for a module the prewarm has answered takes the answer, and one that asks first
        // resolves it itself (the same answer either way, see PrewarmSourceCallBindings).
        var prewarm = PrewarmSourceCallBindings(parsed.Select(document => (document.Path, document.Ast)).ToArray(),
            sourceTypes, crossModuleFunctions);
        foreach (var (path, source, ast) in parsed)
        {
            var bindDiagnostics = new DiagnosticBag();
            BoundModule bound;
            // The AST mutation count before the bind: the binding is offered to the effect
            // pass only while it is unchanged (see CallGraphAnalysis.PriorModuleBinding).
            var version = AstMutation.Version;
            Binder binder;
            try
            {
                binder = new Binder(bindDiagnostics, path)
                {
                    CrossModuleTypes = sourceTypes,
                    CrossModuleFunctionModules = crossModuleFunctions
                };
                bound = binder.Bind(ast);
            }
            catch (Exception)
            {
                failed.Add(path);
                continue;
            }

            documents.Add(new IndexedDocument(path, source, ast, bound, bindDiagnostics.HasErrors)
            {
                Binding = AstMutation.Version == version
                    ? new Analysis.CallGraphAnalysis.PriorModuleBinding(ast, path, sourceTypes, crossModuleFunctions,
                        ContractMode.Debug, version, binder, bound, bindDiagnostics)
                    : null,
            });
        }

        prewarm?.Wait();
        skipped = failed;
        sourceTypes.EnsureSourceSnapshots();
        var index = new ProjectSymbolIndex(documents, sourceTypes) { CrossModuleFunctions = crossModuleFunctions };
        index.Populate();
        return index;
    }

    /// <summary>
    /// Resolves every module's source-call bindings (the Roslyn binding of its C# image)
    /// on the thread pool while the caller binds, leaving the answers in the pass's
    /// <see cref="Analysis.CallGraphAnalysis.OperatorBindingsMemo"/> (#1738). The first
    /// module is resolved before this returns: it builds the pass's shared image and
    /// compilation, which every later request waits for anyway. The returned task (null
    /// when there is nothing to prewarm) finishes the rest.
    ///
    /// <para>Two requests per module, each exactly as a later consumer makes it: the
    /// binder's (<c>Binder.SourceCallBindings</c>: the same module object, the default
    /// projection contract mode, the same registry and source path, a single-module
    /// context, no cross-module map; the effect pass's binder makes the same one) and the
    /// effect pass's call graph's (the same, with the cross-module map and a multi-module
    /// context). The memo answers a request only when it is equal to a recorded one and its
    /// projection text is the recorded text, or when it repeats a recorded request while no
    /// AST has changed; otherwise the request is resolved again, as before. A consumer
    /// that asks before the prewarm reaches its module resolves it itself, and both
    /// resolutions give the same answer. So the prewarm changes when an answer is
    /// computed, never what it is. A request is a function of the module, the shared
    /// compilation and the request: the shared image, its compilations, their semantic
    /// models and diagnostics are built once under locks or lazies and read concurrently,
    /// and each request writes only its own bindings and the memo (under its lock). It
    /// reports nothing but the projection's emission diagnostics, and the consumer's own
    /// request reports those to its own bag. A request that throws here is left
    /// unanswered, so the consumer's request throws as before. The snapshot identity of
    /// each module is computed here too (see <c>Binder.SnapshotIdentity</c>).</para>
    /// </summary>
    private static Task? PrewarmSourceCallBindings(
        IReadOnlyList<(string Path, ModuleNode Ast)> modules, CrossModuleTypeRegistry sourceTypes,
        IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget>? crossModuleFunctions)
    {
        if (Analysis.CallGraphAnalysis.IsReferencePass || modules.Count < 2)
            return null;
        void Resolve((string Path, ModuleNode Ast) module)
        {
            try
            {
                // The module's snapshot identity too: every bind of the module reads it, and
                // it is kept while no AST changes (Binder.SnapshotIdentity).
                Binder.PrewarmSnapshotIdentity(module.Ast);
                Analysis.CallGraphAnalysis.ResolveOperatorCalls(module.Ast, diagnostics: null,
                    ContractMode.Debug, crossModuleTypes: sourceTypes, sourcePath: module.Path);
                // And the effect pass's call-graph request over the same map, which the effect
                // rows ask for later (ProjectIndexBuilder.PrewarmCallGraphBindings).
                if (crossModuleFunctions is not null)
                    Analysis.CallGraphAnalysis.ResolveOperatorCalls(module.Ast, diagnostics: null,
                        ContractMode.Debug, singleModuleContext: false, crossModuleTypes: sourceTypes,
                        sourcePath: module.Path, crossModuleFunctions: crossModuleFunctions);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }
        // The first request builds the pass's shared image and compilation (each of
        // which spreads its own per-module work over the cores); the rest then bind
        // against it concurrently instead of queueing behind its locks.
        Resolve(modules[0]);
        return Task.Run(() => System.Threading.Tasks.Parallel.For(1, modules.Count, index => Resolve(modules[index])));
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
        // Type parts share CLR identity, not compilation-unit labels. A per-file
        // occurrence set cannot safely rename one part of that declaration.
        var typeDocumentCounts = Documents
            .SelectMany(document => document.BoundModule.SymbolsById.Values
                .OfType<TypeSymbol>()
                .Select(symbol => (symbol.QualifiedName, document.FilePath)))
            .GroupBy(part => part.QualifiedName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Select(part => part.FilePath).Distinct(StringComparer.Ordinal).Count(),
                StringComparer.Ordinal);

        var seen = new HashSet<(string File, SymbolId Id, int Start, SymbolOccurrenceKind Kind)>();

        foreach (var document in Documents)
        {
            foreach (var symbol in document.BoundModule.SymbolsById.Values)
            {
                if (symbol.Id.IsNone || !IsExactDeclaration(document.Source, symbol))
                    continue;

                var split = symbol is TypeSymbol typeSymbol
                    && typeDocumentCounts.TryGetValue(
                        typeSymbol.QualifiedName,
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
                        else if (ResolveAcrossDocuments(call.Target) is { } crossModule)
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
                    // v0.15 E5 — a call STATEMENT (`§C{Log} §A x §/C` on its own
                    // line, the common shape of an effectful call) is an edge
                    // too. Before E5 only call expressions were, so `callers`
                    // and `impact` were blind to exactly the calls the effects
                    // blast radius (gate 7) has to see. Occurrences are not
                    // touched: rename's corpus oracle, not this switch, decides
                    // what rename edits.
                    string target;
                    IReadOnlyList<FunctionSymbol> resolved;
                    TextSpan calleeSpan;
                    switch (node)
                    {
                        case BoundCallExpression call:
                            (target, resolved, calleeSpan) = (call.Target, call.ResolvedSymbols, call.CalleeSpan);
                            break;
                        case BoundCallStatement statement:
                            (target, resolved, calleeSpan) =
                                (statement.Target, statement.ResolvedSymbols, statement.CalleeSpan);
                            break;
                        case BoundExpressionCall valueCall:
                            // 0.30: invoking a computed value (`pick()(x)`, an event) calls
                            // whatever it holds, so the site is unknown, never absent.
                            (target, resolved, calleeSpan) =
                                ("<expression-call>", Array.Empty<FunctionSymbol>(), valueCall.Span);
                            break;
                        default:
                            continue;
                    }

                    var callee = resolved.Count > 0
                        ? resolved[0]
                        : node is BoundExpressionCall ? null : ResolveAcrossDocuments(target);
                    if (callee is { Id.IsNone: false })
                    {
                        _callEdges.Add(new ProjectCallEdge(
                            function.Symbol.Id, callee.Id, document.FilePath, calleeSpan));
                    }
                    else if (node is BoundCallExpression metadataCall
                        && Calor.Compiler.Analysis.CallGraphAnalysis.IsProvablyMetadataCall(metadataCall))
                    {
                        // 0.30: `Console.WriteLine(s)` binds to a metadata member. It is
                        // attributed — to the BCL, not to a declaration of this project —
                        // so it neither adds an edge nor makes the caller's answers partial.
                        continue;
                    }
                    else
                    {
                        // Named, not counted: "3 unresolved" tells a reader
                        // nothing they can act on. The containing function is
                        // recorded too, so "what does X call?" can tell whether
                        // its own answer is partial.
                        _unresolvedCalls.Add(new ProjectUnresolvedCall(
                            function.Symbol.Id, target, document.FilePath));
                        if (IsAmbiguousAcrossDocuments(target))
                            _ambiguousCallees.Add(target);
                    }
                }
            }

            // Root module names are independent per-file labels, not namespaces.
            var moduleId = document.Ast.ToolingId != null
                ? SymbolId.Create("declaration", document.Ast.ToolingId)
                : SymbolId.Create(
                "source",
                SymbolSourceIdentity.Canonicalize(document.FilePath),
                "module",
                document.Ast.Name);
            Add(document, moduleId, document.Ast.IdentifierSpan,
                SymbolOccurrenceKind.Definition, isSplitDeclaration: false);
        }

        // Each file is bound on its own, so a call into another module resolves
        // to nothing locally. Without this, a cross-file call site is invisible
        // and renaming the callee leaves the caller pointing at a name that no
        // longer exists. Matching is by bare callee name across the project and
        // requires exactly one candidate: ambiguity yields no occurrence, which
        // makes the rename refuse rather than guess.
        FunctionSymbol? ResolveAcrossDocuments(string target)
        {
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
        bool IsAmbiguousAcrossDocuments(string target)
        {
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
