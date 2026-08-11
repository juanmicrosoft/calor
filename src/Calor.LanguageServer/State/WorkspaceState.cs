using System.Collections.Concurrent;
using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Calor.LanguageServer.State;

public sealed record WorkspaceDocumentSnapshot(
    DocumentState Document,
    DocumentAnalysisSnapshot Analysis);

public sealed record ProjectSymbolLocation(
    DocumentState? Doc,
    DocumentAnalysisSnapshot? Snapshot,
    Symbol? Symbol);

public sealed record ProjectFunctionLocation(
    DocumentState? Doc,
    DocumentAnalysisSnapshot? Snapshot,
    FunctionSymbol? Symbol);

public sealed record ProjectReferenceLocation(
    DocumentState Doc,
    DocumentAnalysisSnapshot Snapshot,
    TextSpan Span);

public enum SymbolOccurrenceKind
{
    Definition,
    Reference,
}

public sealed record ProjectSymbolOccurrence(
    DocumentState Doc,
    DocumentAnalysisSnapshot Snapshot,
    SymbolId SymbolId,
    TextSpan Span,
    SymbolOccurrenceKind Kind,
    bool IsOpen);

/// <summary>
/// Manages document state for the entire workspace.
/// </summary>
public sealed class WorkspaceState
{
    private sealed record WorkspaceRoot(string Path, string Identity);
    private sealed record DocumentSymbolIndex(
        IReadOnlyDictionary<SymbolId, ProjectSymbolOccurrence[]> BySymbol,
        ProjectSymbolOccurrence[] Occurrences);
    private sealed record WorkspaceSymbolIndex(
        long Generation,
        IReadOnlyDictionary<DocumentUri, DocumentSymbolIndex> ByDocument,
        IReadOnlyDictionary<SymbolId, ProjectSymbolOccurrence[]> BySymbol);

    private readonly ConcurrentDictionary<DocumentUri, DocumentState> _documents = new();
    private readonly ConcurrentDictionary<DocumentUri, DocumentState> _closedDocuments = new();
    private readonly object _workspaceRootsGate = new();
    private readonly object _indexGate = new();
    private WorkspaceRoot[] _workspaceRoots = [];
    private long _workspaceGeneration;
    private WorkspaceSymbolIndex? _symbolIndex;

    public WorkspaceState(string? workspaceRootPath = null)
    {
        ConfigureWorkspaceRoot(workspaceRootPath);
    }

    public void ConfigureWorkspaceRoot(string? workspaceRootPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
            return;

        var normalized = NormalizeWorkspaceRoot(workspaceRootPath);
        lock (_workspaceRootsGate)
        {
            var current = Volatile.Read(ref _workspaceRoots);
            if (current.Any(root =>
                    string.Equals(root.Path, normalized, StringComparison.Ordinal)))
            {
                return;
            }

            Volatile.Write(
                ref _workspaceRoots,
                current
                    .Append(new WorkspaceRoot(normalized, $"root{current.Length}"))
                    .ToArray());
        }

        RefreshWorkspaceIndex();
    }

    public void ConfigureWorkspaceRoot(Uri? workspaceRoot)
    {
        if (workspaceRoot?.IsFile == true)
            ConfigureWorkspaceRoot(workspaceRoot.LocalPath);
    }

    public void ConfigureWorkspaceRoots(IEnumerable<Uri> workspaceRoots)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoots);
        var normalized = workspaceRoots
            .Where(root => root.IsFile)
            .Select(root => NormalizeWorkspaceRoot(root.LocalPath))
            .Distinct(StringComparer.Ordinal)
            .Select((path, index) => new WorkspaceRoot(path, $"root{index}"))
            .ToArray();
        if (normalized.Length == 0)
            return;

        lock (_workspaceRootsGate)
            Volatile.Write(ref _workspaceRoots, normalized);

        RefreshWorkspaceIndex();
    }

    /// <summary>
    /// Get or create a document state for the given URI.
    /// </summary>
    public DocumentState GetOrCreate(DocumentUri uri, string source, int version = 0)
    {
        _closedDocuments.TryRemove(uri, out _);
        if (_documents.TryGetValue(uri, out var existing))
        {
            var before = existing.Snapshot;
            existing.Update(source, version);
            if (!ReferenceEquals(before, existing.Snapshot))
                InvalidateSymbolIndex();
            return existing;
        }

        var state = _documents.GetOrAdd(uri, _ => CreateAndAnalyze(uri, source, version));
        InvalidateSymbolIndex();
        return state;
    }

    /// <summary>
    /// Get an existing document state, or null if not found.
    /// </summary>
    public DocumentState? Get(DocumentUri uri)
    {
        return _documents.TryGetValue(uri, out var state) ? state : null;
    }

    /// <summary>
    /// Update a document's content.
    /// </summary>
    public DocumentState Update(DocumentUri uri, string source, int version)
    {
        var state = _documents.GetOrAdd(uri, _ => CreateAndAnalyze(uri, source, version));
        var before = state.Snapshot;
        state.Update(source, version);
        if (!ReferenceEquals(before, state.Snapshot))
            InvalidateSymbolIndex();
        return state;
    }

    /// <summary>
    /// Remove a document from the workspace.
    /// </summary>
    public bool Remove(DocumentUri uri)
    {
        var removed = _documents.TryRemove(uri, out _);
        if (removed)
        {
            InvalidateSymbolIndex();
            RefreshWorkspaceIndex();
        }
        return removed;
    }

    public DocumentState? Reanalyze(DocumentUri uri)
    {
        if (!_documents.TryGetValue(uri, out var state))
            return null;

        state.Reanalyze();
        InvalidateSymbolIndex();
        return state;
    }

    /// <summary>
    /// Get all open documents.
    /// </summary>
    public IEnumerable<DocumentState> GetAllDocuments()
    {
        return _documents.Values;
    }

    /// <summary>
    /// Check if a document is open.
    /// </summary>
    public bool Contains(DocumentUri uri)
    {
        return _documents.ContainsKey(uri);
    }

    public ProjectSymbolOccurrence? ResolveOccurrence(DocumentUri uri, int offset)
    {
        var index = GetSymbolIndex();
        if (!index.ByDocument.TryGetValue(uri, out var documentIndex))
            return null;

        var matches = documentIndex.Occurrences
            .Where(occurrence => occurrence.Span.Contains(offset))
            .GroupBy(occurrence => occurrence.SymbolId)
            .Select(group => group
                .OrderBy(occurrence => occurrence.Span.Length)
                .ThenBy(occurrence => occurrence.Kind)
                .First())
            .OrderBy(occurrence => occurrence.Span.Length)
            .ToArray();
        if (matches.Length == 0)
            return null;

        var shortestLength = matches[0].Span.Length;
        var shortest = matches
            .Where(occurrence => occurrence.Span.Length == shortestLength)
            .ToArray();
        return shortest.Length == 1 ? shortest[0] : null;
    }

    public ProjectSymbolOccurrence? FindSymbolDefinition(SymbolId symbolId)
    {
        if (symbolId.IsNone)
            return null;

        return GetSymbolIndex().BySymbol.TryGetValue(symbolId, out var occurrences)
            ? occurrences.FirstOrDefault(occurrence =>
                occurrence.Kind == SymbolOccurrenceKind.Definition)
            : null;
    }

    public IReadOnlyList<ProjectSymbolOccurrence> FindSymbolOccurrences(
        SymbolId symbolId,
        bool includeDeclaration)
    {
        if (symbolId.IsNone
            || !GetSymbolIndex().BySymbol.TryGetValue(symbolId, out var occurrences))
        {
            return Array.Empty<ProjectSymbolOccurrence>();
        }

        return occurrences
            .Where(occurrence =>
                includeDeclaration || occurrence.Kind != SymbolOccurrenceKind.Definition)
            .OrderBy(occurrence => occurrence.Doc.Uri.ToString(), StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.Span.Start)
            .ToArray();
    }

    public bool AreOccurrenceSnapshotsCurrent(
        IEnumerable<ProjectSymbolOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        RefreshWorkspaceIndex();

        foreach (var occurrence in occurrences)
        {
            var uri = DocumentUri.From(occurrence.Doc.Uri);
            if (occurrence.IsOpen)
            {
                if (!_documents.TryGetValue(uri, out var open)
                    || !ReferenceEquals(open.Snapshot, occurrence.Snapshot))
                {
                    return false;
                }
                continue;
            }

            if (_documents.ContainsKey(uri)
                || !_closedDocuments.TryGetValue(uri, out var closed)
                || !ReferenceEquals(closed.Snapshot, occurrence.Snapshot)
                || !occurrence.Doc.Uri.IsFile)
            {
                return false;
            }

            try
            {
                if (!string.Equals(
                        File.ReadAllText(occurrence.Doc.Uri.LocalPath),
                        occurrence.Snapshot.Source,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        return true;
    }

    public ProjectSymbolLocation FindBoundSymbol(SymbolId symbolId)
    {
        if (symbolId.IsNone)
            return new ProjectSymbolLocation(null, null, null);

        foreach (var document in CaptureDocuments())
        {
            if (document.Analysis.BoundModule?.SymbolsById.TryGetValue(symbolId, out var symbol) == true)
            {
                return new ProjectSymbolLocation(
                    document.Document,
                    document.Analysis,
                    symbol);
            }
        }

        return new ProjectSymbolLocation(null, null, null);
    }

    public ProjectFunctionLocation ResolveProjectCall(
        DocumentState caller,
        DocumentAnalysisSnapshot callerSnapshot,
        BoundNode? call)
    {
        return ResolveProjectCall(CaptureDocuments(), caller, callerSnapshot, call);
    }

    public ProjectFunctionLocation ResolveProjectCall(BoundNode? call)
    {
        return ResolveProjectCall(CaptureDocuments(), null, null, call);
    }

    private static ProjectFunctionLocation ResolveProjectCall(
        IReadOnlyList<WorkspaceDocumentSnapshot> documents,
        DocumentState? caller,
        DocumentAnalysisSnapshot? callerSnapshot,
        BoundNode? call)
    {
        if (call == null)
            return new ProjectFunctionLocation(null, null, null);

        if (GetResolvedFunction(call) is { } resolved)
        {
            var owner = FindFunctionOwner(documents, resolved.Id);
            var resolvedCallerType = FindCallerContainingType(callerSnapshot, call);
            if (owner.Doc == null
                || owner.Symbol == null
                || !IsVisibleToCaller(
                    (new WorkspaceDocumentSnapshot(owner.Doc, owner.Snapshot!), owner.Symbol),
                    caller,
                    resolvedCallerType,
                    documents))
            {
                return new ProjectFunctionLocation(null, null, null);
            }

            return owner;
        }

        if (!TryGetCallShape(
                call,
                out var target,
                out var arguments,
                out var argumentNames,
                out var argumentModifiers,
                out var typeArguments,
                out var receiver))
        {
            return new ProjectFunctionLocation(null, null, null);
        }

        var callerContainingType = FindCallerContainingType(callerSnapshot, call);
        var lookupTarget = GetProjectLookupTarget(target, receiver);
        if (callerContainingType != null
            && !lookupTarget.Contains('.', StringComparison.Ordinal))
        {
            lookupTarget = $"{callerContainingType}.{lookupTarget}";
        }
        if (callerContainingType != null
            && target.StartsWith("base.", StringComparison.Ordinal))
        {
            var callerClass = FindClass(documents, callerContainingType);
            if (callerClass?.BaseClass is { Length: > 0 } baseClass)
            {
                lookupTarget =
                    $"{GetNominalTypeName(baseClass)}.{target["base.".Length..]}";
            }
        }
        var candidates = documents
            .Where(document => document.Analysis.BoundModule != null)
            .SelectMany(document => document.Analysis.BoundModule!.Functions
                .Select(function => (Owner: document, Symbol: function.Symbol)))
            .Where(candidate => CallableNameMatches(candidate.Symbol.Name, lookupTarget))
            .Where(candidate => IsVisibleToCaller(
                candidate,
                caller,
                callerContainingType,
                documents))
            .Where(candidate => IsInCallerScope(
                candidate.Symbol,
                lookupTarget,
                callerContainingType))
            .ToArray();
        if (candidates.Length == 0)
            return new ProjectFunctionLocation(null, null, null);

        var scope = new Scope();
        var duplicateSignature = false;
        foreach (var candidate in candidates)
        {
            if (!scope.TryDeclareOverload(lookupTarget, candidate.Symbol, out _))
                duplicateSignature = true;
        }

        if (duplicateSignature)
            return new ProjectFunctionLocation(null, null, null);

        var resolution = scope.ResolveOverload(
            lookupTarget,
            arguments.Select(argument =>
                    argument is BoundVariableExpression { Variable.Id.IsNone: true }
                        ? "<unresolved>"
                        : argument.TypeName)
                .ToArray(),
            argumentNames,
            argumentModifiers,
            typeArguments);
        return resolution.Function == null
            ? new ProjectFunctionLocation(null, null, null)
            : FindFunctionOwner(documents, resolution.Function.Id);
    }

    public ProjectSymbolLocation ResolveProjectType(
        DocumentState caller,
        DocumentAnalysisSnapshot callerSnapshot,
        BoundNewExpression creation)
    {
        var documents = CaptureDocuments();
        if (creation.ResolvedType is { } resolved)
        {
            foreach (var document in documents)
            {
                if (document.Analysis.BoundModule?.SymbolsById.TryGetValue(
                        resolved.Id,
                        out var symbol) == true)
                {
                    return new ProjectSymbolLocation(
                        document.Document,
                        document.Analysis,
                        symbol);
                }
            }
        }

        var typeName = GetNominalTypeName(creation.TypeName);
        var matches = documents
            .SelectMany(document =>
                document.Analysis.BoundModule?.SymbolsById.Values
                    .OfType<TypeSymbol>()
                    .Select(symbol => (Owner: document, Symbol: symbol))
                ?? Enumerable.Empty<(WorkspaceDocumentSnapshot Owner, TypeSymbol Symbol)>())
            .Where(candidate =>
                string.Equals(candidate.Symbol.Name, typeName, StringComparison.Ordinal)
                || string.Equals(candidate.Symbol.QualifiedName, typeName, StringComparison.Ordinal))
            .Where(candidate =>
                candidate.Symbol.Visibility != Calor.Compiler.Ast.Visibility.Private
                || candidate.Owner.Document.Uri == caller.Uri)
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? new ProjectSymbolLocation(
                matches[0].Owner.Document,
                matches[0].Owner.Analysis,
                matches[0].Symbol)
            : new ProjectSymbolLocation(null, null, null);
    }

    public IReadOnlyList<ProjectReferenceLocation> FindProjectFunctionReferences(
        FunctionSymbol target,
        bool includeDeclaration)
    {
        ArgumentNullException.ThrowIfNull(target);
        return FindSymbolOccurrences(target.Id, includeDeclaration)
            .Select(occurrence => new ProjectReferenceLocation(
                occurrence.Doc,
                occurrence.Snapshot,
                occurrence.Span))
            .ToArray();
    }

    private static ProjectFunctionLocation FindFunctionOwner(
        IReadOnlyList<WorkspaceDocumentSnapshot> documents,
        SymbolId functionId)
    {
        foreach (var document in documents)
        {
            var symbol = document.Analysis.BoundModule?.Functions
                .Select(bound => bound.Symbol)
                .FirstOrDefault(candidate => candidate.Id == functionId);
            if (symbol != null)
            {
                return new ProjectFunctionLocation(
                    document.Document,
                    document.Analysis,
                    symbol);
            }
        }

        return new ProjectFunctionLocation(null, null, null);
    }

    private static FunctionSymbol? GetResolvedFunction(BoundNode call) =>
        call switch
        {
            BoundCallStatement statement => statement.ResolvedSymbol,
            BoundCallExpression expression => expression.ResolvedSymbol,
            BoundNewExpression creation => creation.ResolvedConstructor,
            _ => null,
        };

    private static TextSpan GetCallReferenceSpan(BoundNode call) =>
        call switch
        {
            BoundCallStatement statement => statement.CalleeSpan,
            BoundCallExpression expression => expression.CalleeSpan,
            BoundNewExpression creation => creation.TypeNameSpan,
            _ => call.Span,
        };

    private static bool TryGetCallShape(
        BoundNode call,
        out string target,
        out IReadOnlyList<BoundExpression> arguments,
        out IReadOnlyList<string?>? argumentNames,
        out IReadOnlyList<string?>? argumentModifiers,
        out IReadOnlyList<string>? typeArguments,
        out VariableSymbol? receiver)
    {
        switch (call)
        {
            case BoundCallStatement statement:
                target = statement.Target;
                arguments = statement.Arguments;
                argumentNames = statement.ArgumentNames;
                argumentModifiers = statement.ArgumentModifiers;
                typeArguments = statement.TypeArguments;
                receiver = statement.ReceiverSymbol;
                return true;
            case BoundCallExpression expression:
                target = expression.Target;
                arguments = expression.Arguments;
                argumentNames = expression.ArgumentNames;
                argumentModifiers = expression.ArgumentModifiers;
                typeArguments = expression.TypeArguments;
                receiver = expression.ReceiverSymbol;
                return true;
            case BoundNewExpression creation:
                target = $"{creation.TypeName}..ctor";
                arguments = creation.Arguments;
                argumentNames = null;
                argumentModifiers = null;
                typeArguments = null;
                receiver = null;
                return true;
            default:
                target = string.Empty;
                arguments = Array.Empty<BoundExpression>();
                argumentNames = null;
                argumentModifiers = null;
                typeArguments = null;
                receiver = null;
                return false;
        }
    }

    private static string GetProjectLookupTarget(
        string target,
        VariableSymbol? receiver)
    {
        if (receiver == null)
            return target;

        var firstDot = target.IndexOf('.');
        if (firstDot <= 0)
            return target;

        var receiverType = receiver.TypeName.Trim().TrimStart('?');
        var generic = receiverType.IndexOf('<');
        if (generic > 0)
            receiverType = receiverType[..generic];
        var array = receiverType.IndexOf('[');
        if (array > 0)
            receiverType = receiverType[..array];
        receiverType = receiverType.TrimEnd('?', '*');
        return $"{receiverType}.{target[(firstDot + 1)..]}";
    }

    private static string GetNominalTypeName(string typeName)
    {
        var type = typeName.Trim().TrimStart('?');
        var generic = type.IndexOf('<');
        if (generic > 0)
            type = type[..generic];
        var array = type.IndexOf('[');
        if (array > 0)
            type = type[..array];
        return type.TrimEnd('?', '*');
    }

    private static bool CallableNameMatches(string declaredName, string lookupName)
    {
        if (string.Equals(declaredName, lookupName, StringComparison.Ordinal))
            return true;

        var generic = declaredName.LastIndexOf('<');
        return generic > 0
            && declaredName.EndsWith('>')
            && string.Equals(declaredName[..generic], lookupName, StringComparison.Ordinal);
    }

    private static bool IsVisibleToCaller(
        (WorkspaceDocumentSnapshot Owner, FunctionSymbol Symbol) candidate,
        DocumentState? caller,
        string? callerContainingType,
        IReadOnlyList<WorkspaceDocumentSnapshot> documents)
    {
        var sameDocument = caller != null && candidate.Owner.Document.Uri == caller.Uri;
        return candidate.Symbol.Visibility switch
        {
            Calor.Compiler.Ast.Visibility.Private
                when candidate.Symbol.ContainingTypeName == null => sameDocument,
            Calor.Compiler.Ast.Visibility.Private =>
                sameDocument
                && string.Equals(
                    candidate.Symbol.ContainingTypeName,
                    callerContainingType,
                    StringComparison.Ordinal),
            Calor.Compiler.Ast.Visibility.Protected =>
                callerContainingType != null
                && candidate.Symbol.ContainingTypeName != null
                && IsSameOrDerivedType(
                    callerContainingType,
                    candidate.Symbol.ContainingTypeName,
                    documents),
            _ => true,
        };
    }

    private static bool IsInCallerScope(
        FunctionSymbol function,
        string lookupTarget,
        string? callerContainingType)
    {
        return lookupTarget.Contains('.', StringComparison.Ordinal)
            || function.ContainingTypeName == null
            || string.Equals(
                function.ContainingTypeName,
                callerContainingType,
                StringComparison.Ordinal);
    }

    private static string? FindCallerContainingType(
        DocumentAnalysisSnapshot? callerSnapshot,
        BoundNode call)
    {
        return callerSnapshot?.BoundModule?.Functions
            .Where(function => function.Span.Start <= call.Span.Start
                && function.Span.End >= call.Span.End)
            .OrderBy(function => function.Span.Length)
            .Select(function => function.ContainingTypeName)
            .FirstOrDefault();
    }

    private static bool IsSameOrDerivedType(
        string typeName,
        string expectedBaseType,
        IReadOnlyList<WorkspaceDocumentSnapshot> documents)
    {
        var current = typeName;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current))
        {
            if (string.Equals(current, expectedBaseType, StringComparison.Ordinal))
                return true;

            var declaration = FindClass(documents, current);
            if (declaration?.BaseClass is not { Length: > 0 } baseClass)
                return false;
            current = GetNominalTypeName(baseClass);
        }

        return false;
    }

    private static Calor.Compiler.Ast.ClassDefinitionNode? FindClass(
        IReadOnlyList<WorkspaceDocumentSnapshot> documents,
        string qualifiedName)
    {
        foreach (var document in documents)
        {
            if (document.Analysis.Ast == null)
                continue;

            foreach (var cls in EnumerateClasses(document.Analysis.Ast.Classes, containingType: null))
            {
                if (string.Equals(cls.QualifiedName, qualifiedName, StringComparison.Ordinal)
                    || string.Equals(cls.Node.Name, qualifiedName, StringComparison.Ordinal))
                {
                    return cls.Node;
                }
            }
        }

        return null;
    }

    private static IEnumerable<(
        string QualifiedName,
        Calor.Compiler.Ast.ClassDefinitionNode Node)> EnumerateClasses(
        IEnumerable<Calor.Compiler.Ast.ClassDefinitionNode> classes,
        string? containingType)
    {
        foreach (var cls in classes)
        {
            var qualifiedName = containingType == null
                ? cls.Name
                : $"{containingType}.{cls.Name}";
            yield return (qualifiedName, cls);
            foreach (var nested in EnumerateClasses(cls.NestedClasses, qualifiedName))
                yield return nested;
        }
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

    private WorkspaceSymbolIndex GetSymbolIndex()
    {
        lock (_indexGate)
        {
            RefreshWorkspaceIndexCore();
            if (_symbolIndex?.Generation == _workspaceGeneration)
                return _symbolIndex;

            _symbolIndex = BuildSymbolIndex(
                CaptureDocumentsCore(),
                _workspaceGeneration);
            return _symbolIndex;
        }
    }

    private WorkspaceSymbolIndex BuildSymbolIndex(
        IReadOnlyList<WorkspaceDocumentSnapshot> documents,
        long generation)
    {
        var byDocument = documents.ToDictionary(
            document => DocumentUri.From(document.Document.Uri),
            _ => new List<ProjectSymbolOccurrence>());
        var bySymbol = new Dictionary<SymbolId, List<ProjectSymbolOccurrence>>();
        var seen = new HashSet<(DocumentUri Uri, SymbolId Id, TextSpan Span, SymbolOccurrenceKind Kind)>();
        var typeSymbols = documents
            .SelectMany(document =>
                document.Analysis.BoundModule?.SymbolsById.Values
                    .OfType<TypeSymbol>()
                    .Select(symbol => (Owner: document, Symbol: symbol))
                ?? Enumerable.Empty<(WorkspaceDocumentSnapshot Owner, TypeSymbol Symbol)>())
            .ToArray();

        foreach (var document in documents)
        {
            var boundModule = document.Analysis.BoundModule;
            if (boundModule == null)
                continue;

            var moduleId = SymbolId.Create(
                "source",
                GetCanonicalSourceIdentity(document.Document.Uri),
                "module",
                document.Analysis.Ast!.Id);
            AddOccurrence(
                document,
                moduleId,
                document.Analysis.Ast.IdentifierSpan,
                SymbolOccurrenceKind.Definition);

            foreach (var symbol in boundModule.SymbolsById.Values)
            {
                if (!symbol.Id.IsNone
                    && IsExactSymbolDeclaration(
                        document.Analysis.Source,
                        symbol,
                        symbol.DeclarationSpan))
                {
                    AddOccurrence(
                        document,
                        symbol.Id,
                        symbol.DeclarationSpan,
                        SymbolOccurrenceKind.Definition);
                }
            }

            foreach (var node in Descendants(boundModule))
            {
                switch (node)
                {
                    case BoundVariableExpression variable:
                        foreach (var symbol in variable.ResolvedSymbols.Where(
                                     symbol => !symbol.Id.IsNone))
                        {
                            AddOccurrence(
                                document,
                                symbol.Id,
                                variable.Span,
                                SymbolOccurrenceKind.Reference);
                        }
                        break;

                    case BoundFieldAccessExpression field:
                        foreach (var symbol in ResolveProjectFields(
                                     documents,
                                     document,
                                     field))
                        {
                            AddOccurrence(
                                document,
                                symbol.Id,
                                field.FieldNameSpan,
                                SymbolOccurrenceKind.Reference);
                        }
                        break;

                    case BoundCallExpression call:
                        AddCallOccurrences(document, call);
                        break;

                    case BoundCallStatement call:
                        AddCallOccurrences(document, call);
                        break;
                }
            }

            var visibleTypeSymbols = typeSymbols
                .Where(candidate =>
                    candidate.Owner.Document.Uri == document.Document.Uri
                    || candidate.Symbol.Visibility
                        != Calor.Compiler.Ast.Visibility.Private)
                .Select(candidate => candidate.Symbol)
                .ToArray();
            foreach (var reference in TypeReferenceIndex.Build(
                         document.Analysis.Ast!,
                         boundModule,
                         document.Analysis.Source,
                         visibleTypeSymbols))
            {
                AddOccurrence(
                    document,
                    reference.SymbolId,
                    reference.Span,
                    SymbolOccurrenceKind.Reference);
            }
        }

        return new WorkspaceSymbolIndex(
            generation,
            byDocument.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var occurrences = pair.Value
                        .OrderBy(occurrence => occurrence.Span.Start)
                        .ThenBy(occurrence => occurrence.Span.Length)
                        .ThenBy(
                            occurrence => occurrence.SymbolId.Value,
                            StringComparer.Ordinal)
                        .ToArray();
                    return new DocumentSymbolIndex(
                        occurrences
                            .GroupBy(occurrence => occurrence.SymbolId)
                            .ToDictionary(
                                group => group.Key,
                                group => group.ToArray()),
                        occurrences);
                }),
            bySymbol.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .OrderBy(occurrence => occurrence.Doc.Uri.ToString(), StringComparer.Ordinal)
                    .ThenBy(occurrence => occurrence.Span.Start)
                    .ThenBy(occurrence => occurrence.Kind)
                    .ToArray()));

        void AddCallOccurrences(
            WorkspaceDocumentSnapshot document,
            BoundNode call)
        {
            switch (call)
            {
                case BoundCallExpression expression
                    when expression.ReceiverSymbol is { Id.IsNone: false } receiver:
                    AddOccurrence(
                        document,
                        receiver.Id,
                        expression.ReceiverSpan ?? expression.Span,
                        SymbolOccurrenceKind.Reference);
                    break;
                case BoundCallStatement statement
                    when statement.ReceiverSymbol is { Id.IsNone: false } receiver:
                    AddOccurrence(
                        document,
                        receiver.Id,
                        statement.ReceiverSpan ?? statement.Span,
                        SymbolOccurrenceKind.Reference);
                    break;
            }

            var resolved = ResolveProjectCall(
                documents,
                document.Document,
                document.Analysis,
                call);
            if (resolved.Symbol is { Id.IsNone: false } function)
            {
                AddOccurrence(
                    document,
                    function.Id,
                    GetCallReferenceSpan(call),
                    SymbolOccurrenceKind.Reference);
            }
        }

        void AddOccurrence(
            WorkspaceDocumentSnapshot document,
            SymbolId symbolId,
            TextSpan span,
            SymbolOccurrenceKind kind)
        {
            if (symbolId.IsNone
                || !IsExactIdentifierSpan(document.Analysis.Source, span))
            {
                return;
            }

            var uri = DocumentUri.From(document.Document.Uri);
            if (!seen.Add((uri, symbolId, span, kind)))
                return;

            var occurrence = new ProjectSymbolOccurrence(
                document.Document,
                document.Analysis,
                symbolId,
                span,
                kind,
                _documents.ContainsKey(uri));
            byDocument[uri].Add(occurrence);
            if (!bySymbol.TryGetValue(symbolId, out var symbolOccurrences))
            {
                symbolOccurrences = [];
                bySymbol.Add(symbolId, symbolOccurrences);
            }
            symbolOccurrences.Add(occurrence);
        }
    }

    private static IReadOnlyList<VariableSymbol> ResolveProjectFields(
        IReadOnlyList<WorkspaceDocumentSnapshot> documents,
        WorkspaceDocumentSnapshot caller,
        BoundFieldAccessExpression field)
    {
        var resolved = field.ResolvedFields
            .Where(symbol => !symbol.Id.IsNone)
            .DistinctBy(symbol => symbol.Id)
            .ToArray();
        if (resolved.Length > 0)
            return resolved;

        var callerContainingType = FindCallerContainingType(caller.Analysis, field);
        var currentType = GetNominalTypeName(field.Target.TypeName);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (currentType.Length > 0 && visited.Add(currentType))
        {
            var candidates = documents
                .SelectMany(document =>
                    document.Analysis.BoundModule?.SymbolsById.Values
                        .OfType<VariableSymbol>()
                        .Select(symbol => (Owner: document, Symbol: symbol))
                    ?? Enumerable.Empty<(WorkspaceDocumentSnapshot Owner, VariableSymbol Symbol)>())
                .Where(candidate =>
                    (candidate.Symbol.IsField || candidate.Symbol.IsProperty)
                    && string.Equals(
                        candidate.Symbol.DeclaringTypeName,
                        currentType,
                        StringComparison.Ordinal)
                    && string.Equals(
                        candidate.Symbol.Name,
                        field.FieldName,
                        StringComparison.Ordinal))
                .Where(candidate => IsVisibleToCaller(
                    candidate,
                    caller,
                    callerContainingType,
                    documents))
                .Select(candidate => candidate.Symbol)
                .DistinctBy(symbol => symbol.Id)
                .Take(2)
                .ToArray();
            if (candidates.Length > 0)
                return candidates.Length == 1 ? candidates : Array.Empty<VariableSymbol>();

            var declaration = FindClass(documents, currentType);
            currentType = declaration?.BaseClass is { Length: > 0 } baseClass
                ? GetNominalTypeName(baseClass)
                : string.Empty;
        }

        return Array.Empty<VariableSymbol>();
    }

    private static bool IsVisibleToCaller(
        (WorkspaceDocumentSnapshot Owner, VariableSymbol Symbol) candidate,
        WorkspaceDocumentSnapshot caller,
        string? callerContainingType,
        IReadOnlyList<WorkspaceDocumentSnapshot> documents)
    {
        var sameDocument = candidate.Owner.Document.Uri == caller.Document.Uri;
        return candidate.Symbol.Visibility switch
        {
            Calor.Compiler.Ast.Visibility.Private =>
                sameDocument
                && string.Equals(
                    candidate.Symbol.DeclaringTypeName,
                    callerContainingType,
                    StringComparison.Ordinal),
            Calor.Compiler.Ast.Visibility.Protected =>
                callerContainingType != null
                && candidate.Symbol.DeclaringTypeName != null
                && IsSameOrDerivedType(
                    callerContainingType,
                    candidate.Symbol.DeclaringTypeName,
                    documents),
            _ => true,
        };
    }

    private static bool IsExactSymbolDeclaration(
        string source,
        Symbol symbol,
        TextSpan span)
    {
        if (!IsExactIdentifierSpan(source, span))
            return false;

        var sourceName = source.Substring(span.Start, span.Length);
        var symbolName = symbol.Name;
        if (symbol is FunctionSymbol)
        {
            var lastDot = symbolName.LastIndexOf('.');
            if (lastDot >= 0)
                symbolName = symbolName[(lastDot + 1)..];
            var generic = symbolName.IndexOf('<');
            if (generic > 0)
                symbolName = symbolName[..generic];
        }

        return string.Equals(sourceName, symbolName, StringComparison.Ordinal);
    }

    private static bool IsExactIdentifierSpan(string source, TextSpan span)
    {
        if (span.Length <= 0
            || span.Start < 0
            || span.End > source.Length
            || (!char.IsLetter(source[span.Start]) && source[span.Start] != '_'))
        {
            return false;
        }

        for (var offset = span.Start + 1; offset < span.End; offset++)
        {
            if (!char.IsLetterOrDigit(source[offset]) && source[offset] != '_')
                return false;
        }

        return true;
    }

    private WorkspaceDocumentSnapshot[] CaptureDocuments()
    {
        lock (_indexGate)
        {
            RefreshWorkspaceIndexCore();
            return CaptureDocumentsCore();
        }
    }

    private WorkspaceDocumentSnapshot[] CaptureDocumentsCore()
    {
        return _documents.Values
            .Concat(_closedDocuments
                .Where(pair => !_documents.ContainsKey(pair.Key))
                .Select(pair => pair.Value))
            .Select(document => new WorkspaceDocumentSnapshot(document, document.Snapshot))
            .ToArray();
    }

    private void RefreshWorkspaceIndex()
    {
        lock (_indexGate)
            RefreshWorkspaceIndexCore();
    }

    private void RefreshWorkspaceIndexCore()
    {
        var roots = Volatile.Read(ref _workspaceRoots);
        if (roots.Length == 0)
            return;

        var seen = new HashSet<DocumentUri>();
        var changed = false;
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Path))
                continue;

            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(
                    root.Path,
                    "*.calr",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                    })
                    .Where(ShouldIndexPath)
                    .ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                var uri = DocumentUri.FromFileSystemPath(fullPath);
                seen.Add(uri);
                if (_documents.ContainsKey(uri))
                {
                    changed |= _closedDocuments.TryRemove(uri, out _);
                    continue;
                }

                string source;
                try
                {
                    source = File.ReadAllText(fullPath);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (_closedDocuments.TryGetValue(uri, out var existing)
                    && string.Equals(existing.Source, source, StringComparison.Ordinal))
                {
                    continue;
                }

                _closedDocuments[uri] = CreateAndAnalyze(uri, source, version: 0);
                changed = true;
            }
        }

        foreach (var uri in _closedDocuments.Keys)
        {
            if (!seen.Contains(uri) && _closedDocuments.TryRemove(uri, out _))
                changed = true;
        }

        if (changed)
        {
            _workspaceGeneration++;
            _symbolIndex = null;
        }
    }

    private static bool ShouldIndexPath(string path)
    {
        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return !segments.Any(segment =>
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    private void InvalidateSymbolIndex()
    {
        lock (_indexGate)
        {
            _workspaceGeneration++;
            _symbolIndex = null;
        }
    }

    private DocumentState CreateAndAnalyze(DocumentUri uri, string source, int version)
    {
        var state = new DocumentState(
            uri.ToUri(),
            source,
            version,
            GetCanonicalSourceIdentity(uri.ToUri()));
        state.Reanalyze();
        return state;
    }

    private string GetCanonicalSourceIdentity(Uri uri)
    {
        var roots = Volatile.Read(ref _workspaceRoots);
        if (uri.IsFile && roots.Length > 0)
        {
            var fullPath = Path.GetFullPath(uri.LocalPath);
            foreach (var root in roots.OrderByDescending(root => root.Path.Length))
            {
                var relative = Path.GetRelativePath(root.Path, fullPath).Replace('\\', '/');
                if (relative != ".."
                    && !relative.StartsWith("../", StringComparison.Ordinal))
                {
                    return $"workspace:{root.Identity}:{relative}";
                }
            }
        }

        return SymbolSourceIdentity.Canonicalize(uri.ToString());
    }

    private static string NormalizeWorkspaceRoot(string workspaceRootPath) =>
        Path.GetFullPath(workspaceRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Find a symbol definition across all open documents.
    /// </summary>
    public (DocumentState? Doc, Calor.Compiler.Ast.AstNode? Node) FindDefinitionAcrossFiles(string name)
    {
        foreach (var doc in _documents.Values)
        {
            if (doc.Ast == null) continue;

            // Check functions
            var func = doc.Ast.Functions.FirstOrDefault(f => f.Name == name);
            if (func != null) return (doc, func);

            // Check classes
            var cls = doc.Ast.Classes.FirstOrDefault(c => c.Name == name);
            if (cls != null) return (doc, cls);

            // Check interfaces
            var iface = doc.Ast.Interfaces.FirstOrDefault(i => i.Name == name);
            if (iface != null) return (doc, iface);

            // Check enums
            var enumDef = doc.Ast.Enums.FirstOrDefault(e => e.Name == name);
            if (enumDef != null) return (doc, enumDef);

            // Check delegates
            var del = doc.Ast.Delegates.FirstOrDefault(d => d.Name == name);
            if (del != null) return (doc, del);
        }

        return (null, null);
    }

    /// <summary>
    /// Find a member (field, property, method) on a type across all open documents.
    /// </summary>
    public (DocumentState? Doc, Calor.Compiler.Ast.AstNode? Node) FindMemberAcrossFiles(string typeName, string memberName)
    {
        foreach (var doc in _documents.Values)
        {
            if (doc.Ast == null) continue;

            // Check classes
            var cls = doc.Ast.Classes.FirstOrDefault(c => c.Name == typeName);
            if (cls != null)
            {
                // Check fields
                var field = cls.Fields.FirstOrDefault(f => f.Name == memberName);
                if (field != null) return (doc, field);

                // Check properties
                var prop = cls.Properties.FirstOrDefault(p => p.Name == memberName);
                if (prop != null) return (doc, prop);

                // Check methods
                var method = cls.Methods.FirstOrDefault(m => m.Name == memberName);
                if (method != null) return (doc, method);

                // Check base class (recursively)
                if (!string.IsNullOrEmpty(cls.BaseClass))
                {
                    var baseResult = FindMemberAcrossFiles(cls.BaseClass, memberName);
                    if (baseResult.Node != null) return baseResult;
                }
            }

            // Check interfaces
            var iface = doc.Ast.Interfaces.FirstOrDefault(i => i.Name == typeName);
            if (iface != null)
            {
                var method = iface.Methods.FirstOrDefault(m => m.Name == memberName);
                if (method != null) return (doc, method);
            }

            // Check enums for enum members
            var enumDef = doc.Ast.Enums.FirstOrDefault(e => e.Name == typeName);
            if (enumDef != null)
            {
                var member = enumDef.Members.FirstOrDefault(m => m.Name == memberName);
                if (member != null) return (doc, member);
            }

            // Check enum extensions
            var enumExt = doc.Ast.EnumExtensions.FirstOrDefault(e => e.EnumName == typeName);
            if (enumExt != null)
            {
                var method = enumExt.Methods.FirstOrDefault(m => m.Name == memberName);
                if (method != null) return (doc, method);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Get all public symbols from all open documents.
    /// </summary>
    public IEnumerable<(DocumentState Doc, string Name, string Kind, string? Type)> GetAllPublicSymbols()
    {
        foreach (var doc in _documents.Values)
        {
            if (doc.Ast == null) continue;

            // Functions (public by default unless marked private)
            foreach (var func in doc.Ast.Functions)
            {
                if (func.Visibility != Calor.Compiler.Ast.Visibility.Private)
                {
                    yield return (doc, func.Name, "function", func.Output?.TypeName ?? "void");
                }
            }

            // Classes
            foreach (var cls in doc.Ast.Classes)
            {
                yield return (doc, cls.Name, "class", null);
            }

            // Interfaces
            foreach (var iface in doc.Ast.Interfaces)
            {
                yield return (doc, iface.Name, "interface", null);
            }

            // Enums
            foreach (var enumDef in doc.Ast.Enums)
            {
                yield return (doc, enumDef.Name, "enum", enumDef.UnderlyingType);
            }

            // Delegates
            foreach (var del in doc.Ast.Delegates)
            {
                yield return (doc, del.Name, "delegate", del.Output?.TypeName ?? "void");
            }
        }
    }
}
