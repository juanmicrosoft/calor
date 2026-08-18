using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Migration;

/// <summary>
/// Merges partial class definitions from multiple files into single unified definitions.
/// Used during project-level migration where partial classes span multiple .cs files.
/// </summary>
public sealed class PartialClassMerger
{
    /// <summary>
    /// Merges partial class definitions across multiple modules.
    /// Returns a list of merged modules where partial classes have been consolidated.
    /// </summary>
    public List<ModuleNode> Merge(IReadOnlyList<ModuleNode> modules)
    {
        // Collect all partial classes with their source modules
        var partialGroups = new Dictionary<string, List<(ModuleNode Module, ClassDefinitionNode Class)>>();

        foreach (var module in modules)
        {
            foreach (var cls in module.Classes)
            {
                if (!cls.IsPartial)
                    continue;

                // Group by qualified name (namespace + class name)
                var qualifiedName = GetQualifiedName(module, cls);
                if (!partialGroups.TryGetValue(qualifiedName, out var group))
                {
                    group = new List<(ModuleNode, ClassDefinitionNode)>();
                    partialGroups[qualifiedName] = group;
                }
                group.Add((module, cls));
            }
        }

        // If no partial classes need merging, return modules as-is
        if (partialGroups.Values.All(g => g.Count <= 1))
        {
            return modules.ToList();
        }

        // Track which classes have been merged (to remove them from their original modules)
        var mergedClasses = new HashSet<ClassDefinitionNode>();
        // Maps target module -> list of merged classes to add
        var mergedByModule = new Dictionary<ModuleNode, List<ClassDefinitionNode>>();
        var additionalUsingsByModule =
            new Dictionary<ModuleNode, List<UsingDirectiveNode>>();

        foreach (var (_, group) in partialGroups)
        {
            if (group.Count <= 1)
                continue;

            var targetModule = group[0].Module;
            var targetClass = group[0].Class;
            if (!TryCollectMergedUsings(
                    group,
                    targetModule,
                    targetClass,
                    out var additionalUsings))
            {
                continue;
            }

            var merged = MergePartialClasses(group.Select(g => g.Class).ToList());

            // Track which classes are being merged
            foreach (var (_, cls) in group)
            {
                mergedClasses.Add(cls);
            }

            // The merged class goes into the first module that contained a partial
            if (!mergedByModule.TryGetValue(targetModule, out var mergedList))
            {
                mergedList = new List<ClassDefinitionNode>();
                mergedByModule[targetModule] = mergedList;
            }
            mergedList.Add(merged);
            if (additionalUsings.Count > 0)
            {
                if (!additionalUsingsByModule.TryGetValue(
                        targetModule,
                        out var usingList))
                {
                    usingList = new List<UsingDirectiveNode>();
                    additionalUsingsByModule[targetModule] = usingList;
                }
                usingList.AddRange(additionalUsings);
            }
        }

        // Rebuild modules, removing merged partials and adding merged results
        var result = new List<ModuleNode>();

        foreach (var module in modules)
        {
            var remainingClasses = module.Classes.Where(c => !mergedClasses.Contains(c)).ToList();

            // Add merged classes that belong to this module
            if (mergedByModule.TryGetValue(module, out var toAdd))
            {
                remainingClasses.AddRange(toAdd);
            }

            // Rebuild module only if its classes changed
            if (remainingClasses.Count != module.Classes.Count ||
                !remainingClasses.SequenceEqual(module.Classes) ||
                additionalUsingsByModule.ContainsKey(module))
            {
                var usings = module.Usings.ToList();
                if (additionalUsingsByModule.TryGetValue(module, out var additions))
                {
                    var existing = usings
                        .Select(GetUsingKey)
                        .ToHashSet();
                    foreach (var addition in additions)
                    {
                        if (existing.Add(GetUsingKey(addition)))
                            usings.Add(addition);
                    }
                }
                result.Add(RebuildModule(module, remainingClasses, usings));
            }
            else
            {
                result.Add(module);
            }
        }

        return result;
    }

    private static string GetQualifiedName(ModuleNode module, ClassDefinitionNode cls)
    {
        if (!string.IsNullOrEmpty(cls.FullyQualifiedSymbolIdentity))
            return cls.FullyQualifiedSymbolIdentity;

        var namespaceIdentity = cls.NamespaceIdentity ?? module.Name;
        var typeParamSuffix = cls.TypeParameters.Count > 0
            ? $"`{cls.TypeParameters.Count}"
            : "";
        return $"global::{(string.IsNullOrEmpty(namespaceIdentity) ? "" : namespaceIdentity + ".")}" +
               cls.Name +
               typeParamSuffix;
    }

    /// <summary>
    /// Merges multiple partial class definitions into a single class definition.
    /// </summary>
    private static ClassDefinitionNode MergePartialClasses(List<ClassDefinitionNode> partials)
    {
        var primary = partials[0];

        // Merge base class: take the first non-null base class
        var baseClass = partials.Select(p => p.BaseClass).FirstOrDefault(b => b != null);

        // Merge interfaces: union all, preserving order, deduplicating
        var interfaces = partials
            .SelectMany(p => p.ImplementedInterfaces)
            .Distinct()
            .ToList();

        // Merge type parameters: use the first non-empty set
        var typeParameters = partials
            .Select(p => p.TypeParameters)
            .FirstOrDefault(tp => tp.Count > 0)
            ?? Array.Empty<TypeParameterNode>();

        // Merge members from all partials
        var fields = partials.SelectMany(p => p.Fields).ToList();
        var properties = partials.SelectMany(p => p.Properties).ToList();
        var constructors = partials.SelectMany(p => p.Constructors).ToList();
        var methods = partials.SelectMany(p => p.Methods).ToList();
        var events = partials.SelectMany(p => p.Events).ToList();
        var operatorOverloads = partials.SelectMany(p => p.OperatorOverloads).ToList();
        var interopBlocks = partials.SelectMany(p => p.InteropBlocks).ToList();
        var preprocessorBlocks = partials.SelectMany(p => p.PreprocessorBlocks).ToList();
        var nestedClasses = partials.SelectMany(p => p.NestedClasses).ToList();
        var nestedInterfaces = partials.SelectMany(p => p.NestedInterfaces).ToList();
        var nestedEnums = partials.SelectMany(p => p.NestedEnums).ToList();
        var nestedDelegates = partials.SelectMany(p => p.NestedDelegates).ToList();
        var indexers = partials.SelectMany(p => p.Indexers).ToList();
        var items = partials.SelectMany(CanonicalClassItems).ToList();

        // Attribute multiplicity and arguments are semantically significant.
        var csharpAttributes = partials.SelectMany(p => p.CSharpAttributes).ToList();

        // Use the most permissive visibility
        var visibility = partials.Select(p => p.Visibility).OrderByDescending(VisibilityRank).First();

        // Merge modifier flags
        var isAbstract = partials.Any(p => p.IsAbstract);
        var isSealed = partials.Any(p => p.IsSealed);
        var isStatic = partials.Any(p => p.IsStatic);
        var isStruct = partials.Any(p => p.IsStruct);
        var isReadOnly = partials.Any(p => p.IsReadOnly);

        // Build source file list for tracking
        var sourceFiles = partials
            .Select(p => p.SourceFile)
            .Where(f => f != null)
            .Distinct()
            .ToList();

        var merged = primary.CopyMetadataTo(new ClassDefinitionNode(
            primary.Span,
            primary.Id,
            primary.Name,
            isAbstract,
            isSealed,
            isPartial: true,
            isStatic,
            baseClass,
            interfaces,
            typeParameters,
            fields,
            properties,
            constructors,
            methods,
            events,
            operatorOverloads,
            new AttributeCollection(),
            csharpAttributes,
            isStruct: isStruct,
            isReadOnly: isReadOnly,
            visibility: visibility,
            interopBlocks: interopBlocks.Count > 0 ? interopBlocks : null,
            preprocessorBlocks: preprocessorBlocks.Count > 0 ? preprocessorBlocks : null,
            nestedClasses: nestedClasses.Count > 0 ? nestedClasses : null,
            nestedInterfaces: nestedInterfaces.Count > 0 ? nestedInterfaces : null,
            nestedEnums: nestedEnums.Count > 0 ? nestedEnums : null,
            indexers: indexers.Count > 0 ? indexers : null,
            nestedDelegates: nestedDelegates.Count > 0 ? nestedDelegates : null,
            identifierSpan: primary.IdentifierSpan,
            items: items.Count > 0 ? items : null));

        // Tag with source files
        if (sourceFiles.Count > 0)
        {
            merged.SourceFile = string.Join(", ", sourceFiles);
        }
        return merged;
    }

    private static int VisibilityRank(Visibility v) => v switch
    {
        Visibility.Public => 5,
        Visibility.ProtectedInternal => 4,
        Visibility.Internal => 3,
        Visibility.Protected => 2,
        Visibility.PrivateProtected => 1,
        Visibility.Private => 0,
        _ => 0
    };

    private static bool TryCollectMergedUsings(
        IReadOnlyList<(ModuleNode Module, ClassDefinitionNode Class)> group,
        ModuleNode targetModule,
        ClassDefinitionNode targetClass,
        out List<UsingDirectiveNode> additions)
    {
        additions = new List<UsingDirectiveNode>();
        var applicable = group
            .SelectMany(item => GetApplicableUsings(item.Module, item.Class))
            .ToList();
        var aliasConflicts = applicable
            .Where(item => item.Alias != null)
            .GroupBy(item => item.Alias!, StringComparer.Ordinal)
            .Any(aliasGroup => aliasGroup
                .Select(item => (item.Namespace, item.IsStatic))
                .Distinct()
                .Count() > 1);
        if (aliasConflicts)
            return false;

        var targetApplicable = GetApplicableUsings(targetModule, targetClass)
            .Select(GetUsingKey)
            .ToHashSet();
        foreach (var usingDirective in applicable.Where(item => !item.IsGlobal))
        {
            var clone = new UsingDirectiveNode(
                usingDirective.Span,
                usingDirective.Namespace,
                usingDirective.Alias,
                usingDirective.IsStatic,
                isGlobal: false,
                namespaceIdentity: targetClass.NamespaceIdentity,
                namespaceScopeId: string.IsNullOrEmpty(targetClass.NamespaceScopeId)
                    ? null
                    : targetClass.NamespaceScopeId);
            if (targetApplicable.Add(GetUsingKey(clone)))
                additions.Add(clone);
        }
        return true;
    }

    private static IEnumerable<UsingDirectiveNode> GetApplicableUsings(
        ModuleNode module,
        ClassDefinitionNode cls)
        => module.Usings.Where(usingDirective =>
            usingDirective.IsGlobal
            || usingDirective.NamespaceScopeId == null
            || usingDirective.NamespaceScopeId == cls.NamespaceScopeId);

    private static (
        string Namespace,
        string? Alias,
        bool IsStatic,
        bool IsGlobal,
        string? ScopeId) GetUsingKey(UsingDirectiveNode usingDirective)
        => (
            usingDirective.Namespace,
            usingDirective.Alias,
            usingDirective.IsStatic,
            usingDirective.IsGlobal,
            usingDirective.NamespaceScopeId);

    private static ModuleNode RebuildModule(
        ModuleNode original,
        List<ClassDefinitionNode> newClasses,
        IReadOnlyList<UsingDirectiveNode> usings)
    {
        var replacements = newClasses
            .Where(cls => !original.Classes.Contains(cls))
            .GroupBy(ClassIdentity)
            .ToDictionary(
                group => group.Key,
                group => new Queue<ClassDefinitionNode>(group));
        var items = new List<AstNode>();
        foreach (var item in CanonicalModuleItems(original, usings))
        {
            if (item is not ClassDefinitionNode cls)
            {
                items.Add(item);
                continue;
            }
            if (newClasses.Contains(cls))
            {
                items.Add(cls);
            }
            else if (replacements.TryGetValue(
                         ClassIdentity(cls),
                         out var matching)
                     && matching.Count > 0)
            {
                items.Add(matching.Dequeue());
            }
        }
        items.AddRange(replacements.Values.SelectMany(queue => queue));

        return original.CopyMetadataTo(new ModuleNode(
            original.Span,
            original.Id,
            original.Name,
            usings,
            original.Interfaces,
            newClasses,
            original.Enums,
            original.EnumExtensions,
            original.Delegates,
            original.Functions,
            original.Attributes,
            original.Issues,
            original.Assumptions,
            original.Invariants,
            original.Decisions,
            original.Context,
            interopBlocks: original.InteropBlocks.Count > 0 ? original.InteropBlocks : null,
            refinementTypes: original.RefinementTypes.Count > 0 ? original.RefinementTypes : null,
            indexedTypes: original.IndexedTypes.Count > 0 ? original.IndexedTypes : null,
            typePreprocessorBlocks: original.TypePreprocessorBlocks.Count > 0 ? original.TypePreprocessorBlocks : null,
            identifierSpan: original.IdentifierSpan,
            items: items.Count > 0 ? items : null,
            namespaceScopes: original.NamespaceScopes));
    }

    private static IReadOnlyList<AstNode> CanonicalClassItems(
        ClassDefinitionNode cls)
        => Canonicalize(
            cls.Items,
            cls.Fields.Cast<AstNode>()
                .Concat(cls.Properties)
                .Concat(cls.Indexers)
                .Concat(cls.Constructors)
                .Concat(cls.Methods)
                .Concat(cls.Events)
                .Concat(cls.OperatorOverloads)
                .Concat(cls.InteropBlocks)
                .Concat(cls.PreprocessorBlocks)
                .Concat(cls.NestedClasses)
                .Concat(cls.NestedInterfaces)
                .Concat(cls.NestedEnums)
                .Concat(cls.NestedDelegates));

    private static IReadOnlyList<AstNode> CanonicalModuleItems(
        ModuleNode module,
        IReadOnlyList<UsingDirectiveNode> usings)
        => Canonicalize(
            module.Items,
            usings.Cast<AstNode>()
                .Concat(module.Interfaces)
                .Concat(module.Classes)
                .Concat(module.Enums)
                .Concat(module.EnumExtensions)
                .Concat(module.Delegates)
                .Concat(module.Functions)
                .Concat(module.InteropBlocks)
                .Concat(module.RefinementTypes)
                .Concat(module.IndexedTypes)
                .Concat(module.TypePreprocessorBlocks));

    private static IReadOnlyList<AstNode> Canonicalize(
        IReadOnlyList<AstNode> explicitItems,
        IEnumerable<AstNode> legacyItems)
    {
        var seen = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        return explicitItems
            .Concat(legacyItems)
            .Where(seen.Add)
            .Select((item, index) => (item, index))
            .OrderBy(entry => entry.item.Span.Start)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.item)
            .ToList();
    }

    private static string ClassIdentity(ClassDefinitionNode cls)
        => $"{cls.Name}`{cls.TypeParameters.Count}";
}
