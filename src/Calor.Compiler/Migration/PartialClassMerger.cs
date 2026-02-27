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

        foreach (var (_, group) in partialGroups)
        {
            if (group.Count <= 1)
                continue;

            var merged = MergePartialClasses(group.Select(g => g.Class).ToList());

            // Track which classes are being merged
            foreach (var (_, cls) in group)
            {
                mergedClasses.Add(cls);
            }

            // The merged class goes into the first module that contained a partial
            var targetModule = group[0].Module;
            if (!mergedByModule.TryGetValue(targetModule, out var mergedList))
            {
                mergedList = new List<ClassDefinitionNode>();
                mergedByModule[targetModule] = mergedList;
            }
            mergedList.Add(merged);
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
                !remainingClasses.SequenceEqual(module.Classes))
            {
                result.Add(RebuildModule(module, remainingClasses));
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
        // Group by module name (= C# namespace) + class name + arity.
        // In project migration, the converter derives module name from the C# namespace,
        // so partial classes in the same namespace get the same module name and merge.
        // Classes with the same name in different namespaces stay separate.
        var typeParamSuffix = cls.TypeParameters.Count > 0
            ? $"`{cls.TypeParameters.Count}"
            : "";
        return $"{module.Name}.{cls.Name}{typeParamSuffix}";
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

        // Merge attributes: union all, deduplicating by name
        var seenAttrNames = new HashSet<string>();
        var csharpAttributes = new List<CalorAttributeNode>();
        foreach (var attr in partials.SelectMany(p => p.CSharpAttributes))
        {
            if (seenAttrNames.Add(attr.Name))
            {
                csharpAttributes.Add(attr);
            }
        }

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

        var merged = new ClassDefinitionNode(
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
            nestedEnums: nestedEnums.Count > 0 ? nestedEnums : null);

        // Tag with source files
        if (sourceFiles.Count > 0)
        {
            merged.SourceFile = string.Join(", ", sourceFiles);
        }

        return merged;
    }

    private static int VisibilityRank(Visibility v) => v switch
    {
        Visibility.Public => 4,
        Visibility.ProtectedInternal => 3,
        Visibility.Internal => 2,
        Visibility.Protected => 1,
        Visibility.Private => 0,
        _ => 0
    };

    private static ModuleNode RebuildModule(ModuleNode original, List<ClassDefinitionNode> newClasses)
    {
        return new ModuleNode(
            original.Span,
            original.Id,
            original.Name,
            original.Usings,
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
            typePreprocessorBlocks: original.TypePreprocessorBlocks.Count > 0 ? original.TypePreprocessorBlocks : null);
    }
}
