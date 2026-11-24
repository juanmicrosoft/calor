using System.Reflection;
using System.Runtime.CompilerServices;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1738: a project pass reuses values computed from ASTs (a module's binding, its snapshot
/// identity, a repeated source-call answer) only while <see cref="AstMutation.Version"/> is
/// unchanged. That is sound only if every way to change a node after construction moves the
/// count. These tests pin it structurally, over every node type, so a new setter or mutable
/// field cannot silently bypass it.
/// <remarks>
/// The count is process-wide, so the one test that asserts it does NOT move runs in a
/// collection with parallelization disabled: no other test parses or rewrites meanwhile.
/// </remarks>
/// </summary>
[Collection(AstMutationCountCollection.Name)]
public sealed class AstMutationStampTests
{
    private static readonly Type[] NodeTypes = typeof(AstNode).Assembly.GetTypes()
        .Where(type => typeof(AstNode).IsAssignableFrom(type) || type == typeof(AttributeCollection))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    // Every other type a tree is made of: the records and classes of the Ast namespace that
    // nodes hold as values (spans of metadata, refinements, initializer entries, ...).
    private static readonly Type[] ValueTypes = typeof(AstNode).Assembly.GetTypes()
        .Where(type => type.Namespace == typeof(AstNode).Namespace && type.IsClass && !type.IsAbstract
            && !typeof(AstNode).IsAssignableFrom(type) && type != typeof(AttributeCollection)
            // A builder that produces a new module; no node holds one.
            && type != typeof(ModuleNode.ModuleUpdate)
            && !type.IsDefined(typeof(CompilerGeneratedAttribute), false)
            && !(type.IsAbstract && type.IsSealed) && !typeof(Delegate).IsAssignableFrom(type)
            && type.GetInterfaces().All(contract => contract.Namespace != typeof(AstNode).Namespace
                || !contract.Name.StartsWith("IAstVisitor", StringComparison.Ordinal)))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    // Mutable fields that are not property backing fields, each with the members that write it.
    private static readonly Dictionary<(Type Type, string Field), string> CountedFields = new()
    {
        [(typeof(AstNode), "_span")] = "IncludeSpan",
        [(typeof(MethodNode), "_effectParameters")] = "SetSynthesizedEffectParameters (and init)",
        [(typeof(FunctionNode), "_effectParameters")] = "SetSynthesizedEffectParameters (and init)",
        [(typeof(AttributeCollection), "_attributes")] = "Add (contents)",
        [(typeof(AttributeCollection), "_spans")] = "Add (contents)",
    };

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

    private static IEnumerable<PropertyInfo> MutableProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(property => property.SetMethod is { } setter && !IsInitOnly(setter)
                && property.GetIndexParameters().Length == 0);

    [Fact]
    public void EveryNonInitSetterOfEveryNodeType_MovesTheMutationCount()
    {
        var checkedSetters = 0;
        foreach (var type in NodeTypes.Where(type => !type.IsAbstract && type != typeof(AttributeCollection)))
        {
            var node = RuntimeHelpers.GetUninitializedObject(type);
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (var property in MutableProperties(current))
                {
                    var before = AstMutation.Version;
                    property.SetValue(node, property.GetValue(node));
                    Assert.True(AstMutation.Version > before,
                        $"{current.Name}.{property.Name} has a setter that does not move AstMutation.Version");
                    checkedSetters++;
                }
            }
        }
        // ToolingId, ToolingIdAllocationKind, the namespace/doc metadata and the rows: if this
        // drops, a setter was removed or became init-only (fine) or reflection missed it.
        Assert.True(checkedSetters >= 16, $"only {checkedSetters} setters were checked");
    }

    [Fact]
    public void EveryMutableFieldOfEveryNodeType_IsAPropertyBackingFieldOrACountedWriter()
    {
        var uncounted = new List<string>();
        foreach (var type in NodeTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.DeclaredOnly))
            {
                var mutableContents = field.FieldType.IsArray
                    || field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() is var definition
                    && (definition == typeof(List<>) || definition == typeof(Dictionary<,>) || definition == typeof(HashSet<>));
                if (field.IsInitOnly && !mutableContents)
                    continue;
                if (CountedFields.ContainsKey((type, field.Name)))
                    continue;
                // The backing field of a property whose setter is checked above (or of an
                // init-only property, written only while the node is being initialized).
                var backs = MutableProperties(type).Concat(type.GetProperties(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(property => property.SetMethod is { } setter && IsInitOnly(setter)))
                    .Any(property => field.Name == $"<{property.Name}>k__BackingField"
                        || string.Equals(field.Name, "_" + property.Name, StringComparison.OrdinalIgnoreCase));
                if (backs && !mutableContents)
                    continue;
                uncounted.Add($"{type.Name}.{field.Name} ({field.FieldType.Name})");
            }
        }
        Assert.True(uncounted.Count == 0,
            "AST state that can change without moving AstMutation.Version:\n" + string.Join("\n", uncounted));
    }

    [Fact]
    public void TheValuesNodesHold_HaveNoMutableState()
    {
        var mutable = new List<string>();
        foreach (var type in ValueTypes)
        {
            foreach (var property in MutableProperties(type))
                mutable.Add($"{type.Name}.{property.Name} (setter)");
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.DeclaredOnly))
            {
                if (!field.IsInitOnly && !field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal))
                    mutable.Add($"{type.Name}.{field.Name} (field)");
            }
        }
        Assert.True(ValueTypes.Length > 5, $"only {ValueTypes.Length} value types were found");
        Assert.True(mutable.Count == 0,
            "Values held by AST nodes that can change after construction:\n" + string.Join("\n", mutable));
    }

    [Fact]
    public void TheCountedWriters_MoveTheCount_AndConstructionDoesNot()
    {
        var before = AstMutation.Version;
        var attributes = new AttributeCollection();
        var parameter = new ParameterNode(new TextSpan(0, 1, 1, 1), "x", "i32", attributes);
        var output = new OutputNode(new TextSpan(0, 1, 1, 1), "i32");
        var using_ = new UsingDirectiveNode(new TextSpan(0, 1, 1, 1), "System", null, false, false, "System", "scope");
        Assert.Equal(before, AstMutation.Version);
        Assert.Equal("System", using_.NamespaceIdentity);
        Assert.Equal("scope", using_.NamespaceScopeId);

        attributes.Add("k", "v");
        Assert.True(AstMutation.Version > before);
        before = AstMutation.Version;
        attributes.Add("k2", "v", new TextSpan(0, 1, 1, 1));
        Assert.True(AstMutation.Version > before);
        before = AstMutation.Version;
        parameter.IncludeSpan(new TextSpan(0, 3, 1, 1));
        Assert.True(AstMutation.Version > before);
        before = AstMutation.Version;
        parameter.Row = null;
        Assert.True(AstMutation.Version > before);
        before = AstMutation.Version;
        output.Row = null;
        Assert.True(AstMutation.Version > before);
    }

    [Fact]
    public void PriorBinding_AnswersOnlyTheSameBindWhileNoTreeChanged()
    // In this collection no other test parses or rewrites a tree meanwhile, so the count
    // moves only where this test moves it.
    {
        var module = IndexBuildEquivalenceTests.ParseFixtureModule("l1m02.calr");
        var noInheritance = new Verification.ModuleInheritanceResult([],
            new Dictionary<Verification.MethodContractKey, Verification.InheritedContractInfo>());
        var map = new Dictionary<string, CodeGen.CrossModuleFunctionTarget>(StringComparer.Ordinal)
        {
            ["F"] = new("M", "N", "MModule"),
        };
        var bag = new DiagnosticBag();
        var version = AstMutation.Version;
        var binder = new Calor.Compiler.Binding.Binder(bag, "l1m02.calr") { CrossModuleFunctionModules = map };
        var bound = binder.Bind(module);
        var prior = new CallGraphAnalysis.PriorModuleBinding(module, "l1m02.calr", null, map,
            ContractMode.Debug, version, binder, bound, bag);

        // An equal map (same comparer, same entries, same order) is the same input.
        var copy = new Dictionary<string, CodeGen.CrossModuleFunctionTarget>(map, StringComparer.Ordinal);
        Assert.True(prior.Answers(module, "l1m02.calr", null, copy, ContractMode.Debug, noInheritance));
        Assert.False(prior.Answers(IndexBuildEquivalenceTests.ParseFixtureModule("l1m02.calr"), "l1m02.calr", null, map, ContractMode.Debug, noInheritance));
        Assert.False(prior.Answers(module, "other.calr", null, map, ContractMode.Debug, noInheritance));
        Assert.False(prior.Answers(module, "l1m02.calr", null, null, ContractMode.Debug, noInheritance));
        Assert.False(prior.Answers(module, "l1m02.calr", null,
            new Dictionary<string, CodeGen.CrossModuleFunctionTarget>(map, StringComparer.OrdinalIgnoreCase),
            ContractMode.Debug, noInheritance));
        Assert.False(prior.Answers(module, "l1m02.calr", null, map, ContractMode.Off, noInheritance));
        using (CallGraphAnalysis.RunAsReferencePass())
            Assert.False(prior.Answers(module, "l1m02.calr", null, map, ContractMode.Debug, noInheritance));

        // Any AST mutation anywhere ends the reuse.
        var other = IndexBuildEquivalenceTests.ParseFixtureModule("l1m03.calr");
        other.Classes[0].DocComment = "changed";
        Assert.False(prior.Answers(module, "l1m02.calr", null, map, ContractMode.Debug, noInheritance));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AstMutationCountCollection
{
    public const string Name = "AST mutation count";
}
