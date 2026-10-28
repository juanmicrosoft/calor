using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Incremental;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Effects;

internal sealed class CrossModuleTypeRegistry
{
    private readonly Dictionary<string, List<TypeDefinitionNode>> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<AstNode, ModuleNode> _modules = new();
    private readonly List<UsingDirectiveNode> _globalUsings;
    private readonly IReadOnlyDictionary<string, string>? _sourceHashes;

    internal string Fingerprint { get; }
    internal bool HasTypes => _types.Count != 0;

    private CrossModuleTypeRegistry(
        IReadOnlyList<ModuleNode> modules,
        IReadOnlyDictionary<string, string>? sourceHashes)
    {
        _sourceHashes = sourceHashes;
        _globalUsings = modules.SelectMany(module => module.Usings)
            .Where(directive => directive.IsGlobal).ToList();
        foreach (var module in new PartialClassMerger().Merge(modules))
            Collect(module, module);

        var declarations = _types.Values.SelectMany(types => types)
            .Select(type => JsonSerializer.Serialize(new
            {
                Identity = Identity(type, _modules[type]),
                type.NamespaceScopeId,
                Parameters = Parameters(type).Select(parameter => parameter.Name),
                Bases = type is ClassDefinitionNode cls
                    ? new[] { cls.BaseClass }.Concat(cls.ImplementedInterfaces)
                    : ((InterfaceDefinitionNode)type).BaseInterfaces,
                Methods = type is ClassDefinitionNode owner
                    ? CallGraphAnalysis.EnumerateMethods(owner).Select(method =>
                        MethodContract(method.Name, method.Parameters, method.TypeParameters, method.Effects))
                    : ((InterfaceDefinitionNode)type).Methods.Select(method =>
                        MethodContract(method.Name, method.Parameters, method.TypeParameters, method.Effects)),
                Usings = _modules[type].Usings.Concat(_globalUsings).Select(directive => new
                {
                    directive.Namespace,
                    directive.Alias,
                    directive.IsStatic,
                    directive.IsGlobal,
                    directive.NamespaceScopeId
                }),
                Scopes = _modules[type].NamespaceScopes.Select(scope => new
                {
                    scope.Id,
                    scope.FullName,
                    scope.ParentScopeId
                })
            }))
            .OrderBy(declaration => declaration, StringComparer.Ordinal);
        Fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join("\n", declarations))));
    }

    internal static CrossModuleTypeRegistry Build(
        IReadOnlyList<ModuleNode> modules,
        IReadOnlyDictionary<string, string>? sourceHashes = null) => new(modules, sourceHashes);

    internal bool MatchesSourceHash(string path, string hash) =>
        _sourceHashes == null
        || (_sourceHashes.TryGetValue(Path.GetFullPath(path), out var expected) && expected == hash);

    internal void EnsureSourceSnapshot(string path, byte[] bytes)
    {
        if (!MatchesSourceHash(path, BuildStateCache.ComputeContentHash(bytes)))
            throw new IOException(
                $"Source '{path}' changed after cross-module declaration collection. Retry compilation.");
    }

    private static object MethodContract(
        string name,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<TypeParameterNode> typeParameters,
        EffectsNode? effects) => new
        {
            Name = name,
            Parameters = parameters.Select(parameter => new { parameter.TypeName, parameter.Modifier }),
            TypeParameters = typeParameters.Select(parameter => parameter.Name),
            Effects = effects?.Effects.OrderBy(effect => effect.Key, StringComparer.Ordinal),
            Variables = effects?.EffectVariables,
            Ordinals = effects?.EffectVariableOrdinals
        };

    private void Collect(AstNode node, ModuleNode module)
    {
        if (node is ClassDefinitionNode or InterfaceDefinitionNode)
        {
            var type = (TypeDefinitionNode)node;
            _modules[type] = module;
            var identity = Identity(type, module);
            if (!_types.TryGetValue(identity, out var declarations))
                _types[identity] = declarations = [];
            declarations.Add(type);
        }
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
        {
            if (child is ClassDefinitionNode or InterfaceDefinitionNode
                or TypePreprocessorBlockNode or MemberPreprocessorBlockNode)
                Collect(child, module);
        }
    }

    internal static IReadOnlyList<TypeParameterNode> Parameters(TypeDefinitionNode type) => type switch
    {
        ClassDefinitionNode cls => cls.TypeParameters,
        InterfaceDefinitionNode iface => iface.TypeParameters,
        _ => throw new ArgumentException("Expected a class or interface", nameof(type))
    };

    internal string Identity(TypeDefinitionNode type, ModuleNode module)
    {
        module = _modules.GetValueOrDefault(type) ?? module;
        if (!string.IsNullOrEmpty(type.FullyQualifiedSymbolIdentity))
            return TypeIdentity.ToLookupName(type.FullyQualifiedSymbolIdentity).Replace('+', '.');
        var ns = type.NamespaceIdentity ?? (module.Name == "_global" ? "" : module.Name);
        return (ns.Length == 0 ? "" : ns + ".")
            + TypeIdentity.ToLookupName(type.Name, Parameters(type).Count);
    }

    internal ClassDefinitionNode CanonicalClass(ClassDefinitionNode cls, ModuleNode module) =>
        Unique(Identity(cls, module)) as ClassDefinitionNode ?? cls;

    internal sealed record ConstructedType(
        TypeDefinitionNode Declaration,
        IReadOnlyDictionary<string, string> Substitutions);

    internal ConstructedType Open(ClassDefinitionNode cls, ModuleNode module) => new(
        CanonicalClass(cls, module),
        cls.TypeParameters.Select((parameter, index) => (parameter.Name, Value: $"!class{index}"))
            .ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal));

    internal ConstructedType? Resolve(
        ConstructedType owner, string reference, ModuleNode module)
    {
        var bound = TypeIdentity.Substitute(reference, owner.Substitutions);
        var declaration = Resolve(owner.Declaration, bound, module, out var expanded, out _);
        bound = expanded;
        if (declaration == null)
            return null;

        IReadOnlyList<string> arguments = [];
        if (TypeIdentity.TrySplitGeneric(bound, out var genericName, out var parsedArguments))
        {
            // Nested constructed types need bindings for every containing type.
            if (genericName.Contains('<'))
                return null;
            arguments = parsedArguments;
        }
        else if (bound.Contains('<'))
        {
            return null;
        }

        var parameters = Parameters(declaration);
        if (parameters.Count != arguments.Count)
            return null;
        return new ConstructedType(declaration,
            parameters.Select((parameter, index) => (parameter.Name,
                    Value: BindParameterType(arguments[index], [], owner, module)))
                .ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal));
    }

    internal string BindParameterType(
        string typeName,
        IReadOnlyList<TypeParameterNode> methodParameters,
        ConstructedType owner,
        ModuleNode module)
    {
        var type = TypeIdentity.Substitute(
            TypeIdentity.CanonicalizeSignature(typeName,
                methodParameters.Select(parameter => parameter.Name).ToArray()),
            owner.Substitutions);
        return Qualify(type);

        string Qualify(string value)
        {
            if (value.StartsWith('!') || TypeIdentity.IsBuiltinCanonicalName(value))
                return value;
            if (value.StartsWith('[') && value.EndsWith(']'))
                return Qualify(value[1..^1]) + "[]";
            if (value.EndsWith('?'))
                return Qualify(value[..^1]) + "?";
            if (value.EndsWith('*'))
                return Qualify(value[..^1]) + "*";
            if (value.EndsWith(']') && value.LastIndexOf('[') is var bracket && bracket > 0
                && value[(bracket + 1)..^1].All(character => character == ','))
                return Qualify(value[..bracket]) + value[bracket..];
            if (TypeIdentity.TrySplitGeneric(value, out var name, out var arguments))
            {
                var target = Resolve(owner.Declaration, value, module);
                var qualified = target == null ? name : Identity(target, module);
                var arity = qualified.LastIndexOf('`');
                if (arity >= 0)
                    qualified = qualified[..arity];
                return qualified + "<" + string.Join(",", arguments.Select(Qualify)) + ">";
            }
            var declaration = Resolve(owner.Declaration, value, module);
            return declaration == null ? value : Identity(declaration, module);
        }
    }

    private TypeDefinitionNode? Unique(string identity) => Unique(identity, out _);

    private TypeDefinitionNode? Unique(string identity, out bool ambiguous)
    {
        _types.TryGetValue(identity, out var matches);
        ambiguous = matches is { Count: > 1 };
        return matches is { Count: 1 } ? matches[0] : null;
    }

    internal bool HasKnownInterfaceReference(
        ConstructedType owner, string reference, ModuleNode module)
    {
        var declaration = Resolve(owner.Declaration,
            TypeIdentity.Substitute(reference, owner.Substitutions),
            module, out _, out var ambiguous);
        return declaration is InterfaceDefinitionNode || ambiguous;
    }

    internal TypeDefinitionNode? Resolve(TypeDefinitionNode owner, string reference, ModuleNode fallbackModule)
        => Resolve(owner, reference, fallbackModule, out _, out _);

    private TypeDefinitionNode? Resolve(
        TypeDefinitionNode owner,
        string reference,
        ModuleNode fallbackModule,
        out string expandedReference,
        out bool ambiguous)
    {
        expandedReference = reference;
        ambiguous = false;
        var module = _modules.GetValueOrDefault(owner) ?? fallbackModule;
        var lookup = TypeIdentity.ToLookupName(reference).Replace('+', '.');
        if (reference.StartsWith("global::", StringComparison.Ordinal))
            return Unique(lookup, out ambiguous);

        var ns = owner.NamespaceIdentity ?? (module.Name == "_global" ? "" : module.Name);
        var identity = Identity(owner, module);
        var nestedSeparator = identity.LastIndexOf('.');
        for (var prefix = nestedSeparator < 0 ? "" : identity[..nestedSeparator];
             prefix.Length > ns.Length;
             prefix = prefix.Contains('.') ? prefix[..prefix.LastIndexOf('.')] : "")
        {
            var nested = prefix + "." + lookup;
            if (_types.ContainsKey(nested))
                return Unique(nested, out ambiguous);
        }

        var scopes = module.NamespaceScopes.ToDictionary(scope => scope.Id, StringComparer.Ordinal);
        var scopeId = owner.NamespaceScopeId;
        if (scopeId == null)
        {
            for (var prefix = ns; prefix.Length > 0;
                 prefix = prefix.Contains('.') ? prefix[..prefix.LastIndexOf('.')] : "")
            {
                var qualified = prefix + "." + lookup;
                if (_types.ContainsKey(qualified))
                    return Unique(qualified, out ambiguous);
            }
        }
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var directives = module.Usings.Where(directive =>
                !directive.IsGlobal && directive.NamespaceScopeId == scopeId).ToList();
            if (scopeId == null)
                directives.AddRange(_globalUsings);
            var firstSeparator = lookup.IndexOf('.');
            var first = firstSeparator < 0 ? lookup : lookup[..firstSeparator];
            var aliases = directives.Where(directive => directive.Alias == first).ToArray();
            if (aliases.Length > 0)
            {
                var targets = aliases.Select(directive =>
                    directive.Namespace + reference[first.Length..])
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (targets.Length != 1)
                {
                    ambiguous = true;
                    return null;
                }
                expandedReference = targets[0];
                return Unique(TypeIdentity.ToLookupName(expandedReference).Replace('+', '.'), out ambiguous);
            }

            var qualified = ns.Length == 0 ? lookup : ns + "." + lookup;
            if (_types.ContainsKey(qualified))
                return Unique(qualified, out ambiguous);

            var imports = directives.Where(directive => directive.Alias == null)
                .Select(directive => TypeIdentity.ToLookupName(directive.Namespace) + "." + lookup)
                .Where(_types.ContainsKey).Distinct(StringComparer.Ordinal).ToArray();
            if (imports.Length > 0)
            {
                if (imports.Length == 1)
                    return Unique(imports[0], out ambiguous);
                ambiguous = true;
                return null;
            }

            if (scopeId != null && visited.Add(scopeId) && scopes.TryGetValue(scopeId, out var scope))
            {
                scopeId = scope.ParentScopeId;
                var parentNamespace = scopeId != null && scopes.TryGetValue(scopeId, out var parent)
                    ? parent.FullName : "";
                for (var prefix = ns.Contains('.') ? ns[..ns.LastIndexOf('.')] : "";
                     prefix.Length > parentNamespace.Length;
                     prefix = prefix.Contains('.') ? prefix[..prefix.LastIndexOf('.')] : "")
                {
                    qualified = prefix + "." + lookup;
                    if (_types.ContainsKey(qualified))
                        return Unique(qualified, out ambiguous);
                }
                ns = parentNamespace;
                continue;
            }

            for (var prefix = ns.Contains('.') ? ns[..ns.LastIndexOf('.')] : "";
                 prefix.Length > 0;
                 prefix = prefix.Contains('.') ? prefix[..prefix.LastIndexOf('.')] : "")
            {
                qualified = prefix + "." + lookup;
                if (_types.ContainsKey(qualified))
                    return Unique(qualified, out ambiguous);
            }
            return Unique(lookup, out ambiguous);
        }
    }
}
