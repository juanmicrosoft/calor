using System.Collections.Concurrent;
using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Calor.LanguageServer.State;

/// <summary>
/// Manages document state for the entire workspace.
/// </summary>
public sealed class WorkspaceState
{
    private readonly ConcurrentDictionary<DocumentUri, DocumentState> _documents = new();

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

    public (DocumentState? Doc, Symbol? Symbol) FindBoundSymbol(SymbolId symbolId)
    {
        if (symbolId.IsNone)
            return (null, null);

        foreach (var document in _documents.Values)
        {
            if (document.BoundModule?.SymbolsById.TryGetValue(symbolId, out var symbol) == true)
                return (document, symbol);
        }

        return (null, null);
    }

    public (DocumentState? Doc, FunctionSymbol? Symbol) ResolveProjectCall(BoundNode? call)
    {
        if (call == null)
            return (null, null);

        if (GetResolvedFunction(call) is { } resolved)
            return FindFunctionOwner(resolved);

        if (!TryGetCallShape(
                call,
                out var target,
                out var arguments,
                out var argumentNames,
                out var argumentModifiers,
                out var typeArguments,
                out var receiver))
        {
            return (null, null);
        }

        var lookupTarget = GetProjectLookupTarget(target, receiver);
        var applicable = new List<FunctionSymbol>();
        foreach (var function in _documents.Values
                     .Where(document => document.BoundModule != null)
                     .SelectMany(document => document.BoundModule!.Functions)
                     .Select(function => function.Symbol)
                     .Where(function => CallableNameMatches(function.Name, lookupTarget)))
        {
            var scope = new Scope();
            if (!scope.TryDeclareOverload(lookupTarget, function, out _))
                continue;

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
            if (resolution.Kind == OverloadResolutionKind.Resolved)
                applicable.Add(function);
        }

        return applicable.Count == 1
            ? FindFunctionOwner(applicable[0])
            : (null, null);
    }

    public IEnumerable<(DocumentState Doc, TextSpan Span)> FindProjectFunctionReferences(
        FunctionSymbol target,
        bool includeDeclaration)
    {
        var owner = FindFunctionOwner(target);
        if (includeDeclaration && owner.Doc != null)
            yield return (owner.Doc, target.DeclarationSpan);

        foreach (var document in _documents.Values)
        {
            if (document.BoundModule == null)
                continue;

            foreach (var node in Descendants(document.BoundModule))
            {
                if (node is not (BoundCallStatement
                    or BoundCallExpression
                    or BoundNewExpression
                    or BoundExpressionCallExpression))
                {
                    continue;
                }

                var resolved = ResolveProjectCall(node);
                if (ReferenceEquals(resolved.Symbol, target))
                    yield return (document, node.Span);
            }
        }
    }

    private (DocumentState? Doc, FunctionSymbol? Symbol) FindFunctionOwner(
        FunctionSymbol function)
    {
        foreach (var document in _documents.Values)
        {
            if (document.BoundModule?.Functions.Any(bound =>
                    ReferenceEquals(bound.Symbol, function)) == true)
            {
                return (document, function);
            }
        }

        return (null, null);
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

    private static bool CallableNameMatches(string declaredName, string lookupName)
    {
        if (string.Equals(declaredName, lookupName, StringComparison.Ordinal))
            return true;

        var generic = declaredName.LastIndexOf('<');
        return generic > 0
            && declaredName.EndsWith('>')
            && string.Equals(declaredName[..generic], lookupName, StringComparison.Ordinal);
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

    private static DocumentState CreateAndAnalyze(DocumentUri uri, string source, int version)
    {
        var state = new DocumentState(uri.ToUri(), source, version);
        state.Reanalyze();
        return state;
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
