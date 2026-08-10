using System.Collections.Concurrent;
using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;
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

/// <summary>
/// Manages document state for the entire workspace.
/// </summary>
public sealed class WorkspaceState
{
    private readonly ConcurrentDictionary<DocumentUri, DocumentState> _documents = new();
    private string? _workspaceRootPath;

    public WorkspaceState(string? workspaceRootPath = null)
    {
        ConfigureWorkspaceRoot(workspaceRootPath);
    }

    public void ConfigureWorkspaceRoot(string? workspaceRootPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
            return;

        Volatile.Write(
            ref _workspaceRootPath,
            Path.GetFullPath(workspaceRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public void ConfigureWorkspaceRoot(Uri? workspaceRoot)
    {
        if (workspaceRoot?.IsFile == true)
            ConfigureWorkspaceRoot(workspaceRoot.LocalPath);
    }

    /// <summary>
    /// Get or create a document state for the given URI.
    /// </summary>
    public DocumentState GetOrCreate(DocumentUri uri, string source, int version = 0)
    {
        return _documents.GetOrAdd(uri, _ => CreateAndAnalyze(uri, source, version));
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
        state.Update(source, version);
        return state;
    }

    /// <summary>
    /// Remove a document from the workspace.
    /// </summary>
    public bool Remove(DocumentUri uri)
    {
        return _documents.TryRemove(uri, out _);
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
            .Where(candidate => IsInCallerScope(candidate.Symbol, lookupTarget))
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
        var documents = CaptureDocuments();
        var references = new List<ProjectReferenceLocation>();
        var owner = FindFunctionOwner(documents, target.Id);
        if (includeDeclaration
            && owner.Doc != null
            && owner.Snapshot != null
            && owner.Symbol?.DeclarationSpan.Length > 0)
        {
            references.Add(new ProjectReferenceLocation(
                owner.Doc,
                owner.Snapshot,
                owner.Symbol.DeclarationSpan));
        }

        foreach (var document in documents)
        {
            if (document.Analysis.BoundModule == null)
                continue;

            foreach (var node in Descendants(document.Analysis.BoundModule))
            {
                if (node is not (BoundCallStatement
                    or BoundCallExpression
                    or BoundNewExpression
                    or BoundExpressionCallExpression))
                {
                    continue;
                }

                var resolved = ResolveProjectCall(
                    documents,
                    document.Document,
                    document.Analysis,
                    node);
                if (resolved.Symbol?.Id == target.Id)
                {
                    references.Add(new ProjectReferenceLocation(
                        document.Document,
                        document.Analysis,
                        node.Span));
                }
            }
        }

        return references;
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
                typeArguments = null;
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

    private static bool IsInCallerScope(FunctionSymbol function, string lookupTarget)
    {
        return lookupTarget.Contains('.', StringComparison.Ordinal)
            || function.ContainingTypeName == null;
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

    private WorkspaceDocumentSnapshot[] CaptureDocuments()
    {
        return _documents.Values
            .Select(document => new WorkspaceDocumentSnapshot(document, document.Snapshot))
            .ToArray();
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
        var root = Volatile.Read(ref _workspaceRootPath);
        if (uri.IsFile && root != null)
        {
            var fullPath = Path.GetFullPath(uri.LocalPath);
            var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            if (relative != ".."
                && !relative.StartsWith("../", StringComparison.Ordinal))
            {
                return $"workspace:{relative}";
            }
        }

        return SymbolSourceIdentity.Canonicalize(uri.ToString());
    }

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
