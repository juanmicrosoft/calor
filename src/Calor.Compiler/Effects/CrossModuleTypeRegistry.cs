using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Incremental;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Effects;

internal sealed class CrossModuleTypeRegistry : ISourceBindingContext
{
    private readonly Dictionary<string, List<TypeDefinitionNode>> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<AstNode, ModuleNode> _modules = new();
    private readonly List<UsingDirectiveNode> _globalUsings;
    private readonly IReadOnlyDictionary<string, string>? _sourceHashes;
    private readonly IReadOnlyDictionary<ModuleNode, string> _sourcePaths;
    private readonly Dictionary<ContractMode, IReadOnlyList<SourceProjection>> _projections = [];
    private readonly Dictionary<ModuleNode, (Binder Binder, DiagnosticBag Diagnostics)> _declarationBinders = [];

    internal string Fingerprint { get; }
    internal bool HasTypes => _types.Count != 0;

    private CrossModuleTypeRegistry(
        IReadOnlyList<ModuleNode> modules,
        IReadOnlyDictionary<string, string>? sourceHashes,
        IReadOnlyDictionary<ModuleNode, string>? sourcePaths = null)
    {
        _sourceHashes = sourceHashes;
        _sourcePaths = sourcePaths ?? new Dictionary<ModuleNode, string>();
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
            Encoding.UTF8.GetBytes(string.Join("\n", declarations)
                + "\n" + string.Join("\n", sourceHashes?.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + pair.Value) ?? []))));
    }

    internal static CrossModuleTypeRegistry Build(
        IReadOnlyList<ModuleNode> modules,
        IReadOnlyDictionary<string, string>? sourceHashes = null) => new(modules, sourceHashes);

    internal static CrossModuleTypeRegistry Build(
        IReadOnlyList<(ModuleNode Module, string Path)> sources,
        IReadOnlyDictionary<string, string> sourceHashes) => new(
            sources.Select(source => source.Module).ToArray(), sourceHashes,
            sources.ToDictionary(source => source.Module, source => Path.GetFullPath(source.Path)));

    public IReadOnlyList<SourceProjection> CompanionSources(string? sourcePath, ContractMode mode)
    {
        if (sourcePath is null || !_sourcePaths.Values.Contains(Path.GetFullPath(sourcePath),
                BuildStateCache.GetPathComparer()))
            return [];
        if (!_projections.TryGetValue(mode, out var projections))
        {
            var emitted = new List<SourceProjection>();
            foreach (var (module, path) in _sourcePaths)
            {
                var callables = new List<AstNode>();
                var marker = "calor-source-callable-" + Guid.NewGuid().ToString("N") + ":";
                var diagnostics = new DiagnosticBag();
                var emitter = new CSharpEmitter(mode, null, null, diagnostics: diagnostics)
                {
                    AnnotateSource = (_, text) => text,
                    CallableOrigin = declaration =>
                    {
                        var index = callables.Count;
                        callables.Add(declaration);
                        return $"/*{marker}{index}*/";
                    }
                };
                var text = emitter.Emit(module);
                if (diagnostics.HasErrors)
                    throw new InvalidOperationException(
                        $"Cannot project source '{path}': "
                        + string.Join("; ", diagnostics.Errors.Select(error => error.Message)));
                emitted.Add(new(module, path, text, marker, callables));
            }
            _projections[mode] = projections = emitted;
        }
        return projections.Where(source =>
            !BuildStateCache.GetPathComparer().Equals(source.Path, Path.GetFullPath(sourcePath))).ToArray();
    }

    public SourceCallable? FindCallable(AstNode declaration)
    {
        if (declaration is not MethodNode method)
            return null;
        var owner = _sourcePaths.Keys.SelectMany(CallGraphAnalysis.EnumerateClasses)
            .FirstOrDefault(candidate => CallGraphAnalysis.EnumerateMethods(candidate)
                .Any(member => ReferenceEquals(member, method)));
        if (owner is null)
            return null;
        var module = _sourcePaths.Keys.First(candidate =>
            CallGraphAnalysis.EnumerateClasses(candidate).Contains(owner));
        if (!_declarationBinders.TryGetValue(module, out var binding))
        {
            var diagnostics = new DiagnosticBag();
            var binder = new Binder(diagnostics, _sourcePaths[module])
            {
                ResolveSourceCallConversions = false
            };
            binder.Bind(module, null, declarationsOnly: true);
            _declarationBinders[module] = binding = (binder, diagnostics);
        }
        return binding.Diagnostics.HasErrors ? null
            : new(method, owner, binding.Binder.FunctionSymbols[method], binding.Binder.ClassSymbols[owner]);
    }

    internal bool CanReuseIndependentOutput(string sourcePath, string output,
        IReadOnlyDictionary<string, string> previousOutputs, ContractMode mode)
    {
        if (_sourceHashes is null || _sourceHashes.Count != _sourcePaths.Count
            || _sourcePaths.Count != previousOutputs.Count
            || !_sourcePaths.Values.Contains(Path.GetFullPath(sourcePath), BuildStateCache.GetPathComparer())
            || !_sourcePaths.Values.All(previousOutputs.ContainsKey))
            return false;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in CompanionSources(sourcePath, mode))
        {
            if (!CollectContainerNames(source.Source, names)
                || !CollectContainerNames(previousOutputs[source.Path], names))
                return false;
        }
        var root = CSharpSyntaxTree.ParseText(output).GetRoot();
        // Inactive branches are disabled trivia, not syntax names.
        if (root.ContainsDiagnostics || HasUnprovenTrivia(root))
            return false;
        // Type declarations count too: a new companion can complete or collide
        // with a cached partial owner even without an explicit member access.
        return !root.DescendantNodes().OfType<SimpleNameSyntax>()
                   .Any(name => names.Contains(name.Identifier.ValueText))
            && !root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                   .Any(type => names.Contains(type.Identifier.ValueText))
            && !root.DescendantNodes().OfType<DelegateDeclarationSyntax>()
                   .Any(type => names.Contains(type.Identifier.ValueText));

        static bool HasUnprovenTrivia(SyntaxNode root) =>
            root.DescendantTrivia(descendIntoTrivia: true).Any(trivia =>
                trivia.IsKind(SyntaxKind.DisabledTextTrivia)
                || trivia.IsDirective && trivia.GetStructure() is not
                    (NullableDirectiveTriviaSyntax or LineDirectiveTriviaSyntax));

        static bool CollectContainerNames(string source, HashSet<string> names)
        {
            var root = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Preview)).GetCompilationUnitRoot();
            if (root.ContainsDiagnostics || root.AttributeLists.Count != 0
                || root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                    .Any(directive => directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
                || HasUnprovenTrivia(root))
                return false;
            return root.Members.All(Collect);

            bool Collect(MemberDeclarationSyntax member)
            {
                switch (member)
                {
                    case BaseNamespaceDeclarationSyntax ns:
                        foreach (var name in ns.Name.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
                            names.Add(name.Identifier.ValueText);
                        return ns.Members.All(Collect);
                    case ClassDeclarationSyntax type when type.Modifiers.Any(SyntaxKind.StaticKeyword)
                        && !type.Modifiers.Any(SyntaxKind.PartialKeyword)
                        && type.TypeParameterList is null && type.BaseList is null
                        && type.AttributeLists.Count == 0:
                        names.Add(type.Identifier.ValueText);
                        return type.Members.All(Collect);
                    case MethodDeclarationSyntax method when method.Modifiers.Any(SyntaxKind.StaticKeyword)
                        && method.AttributeLists.Count == 0
                        && !method.ParameterList.Parameters.Any(parameter =>
                            parameter.Modifiers.Any(SyntaxKind.ThisKeyword)):
                        names.Add(method.Identifier.ValueText);
                        return true;
                    default:
                        // Instance/partial types, initialization, constants,
                        // operators and implicit extension lookup retain the
                        // complete source fingerprint's conservative invalidation.
                        return false;
                }
            }
        }
    }

    internal bool MatchesSourceHash(string path, string hash) =>
        _sourceHashes == null
        || (_sourceHashes.TryGetValue(Path.GetFullPath(path), out var expected) && expected == hash);

    internal void EnsureSourceSnapshot(string path, byte[] bytes)
    {
        if (!MatchesSourceHash(path, BuildStateCache.ComputeContentHash(bytes)))
            throw new IOException(
                $"Source '{path}' changed after cross-module declaration collection. Retry compilation.");
    }

    internal void EnsureSourceSnapshots()
    {
        if (_sourceHashes is null)
            return;
        foreach (var path in _sourceHashes.Keys)
            EnsureSourceSnapshot(path, File.ReadAllBytes(path));
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
