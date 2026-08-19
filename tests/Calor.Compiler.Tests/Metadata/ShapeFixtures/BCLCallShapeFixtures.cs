using Calor.Compiler.Binding.Metadata;
using Microsoft.CodeAnalysis;
using Xunit;

// Flat test namespace mirrors MetadataContextTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.14 §F-1 — the 15 BCL call-shape fixtures registered as the spike's
/// discriminating denominator. Fixtures are authored against the S1
/// <see cref="MetadataContext"/> mechanism <b>before</b> S4's
/// <c>MetadataBinder</c> exists, per the F-1 anti-tautology guard.
///
/// Each fixture asserts what <see cref="MetadataContext"/> can currently see
/// for the shape — resolved <see cref="IMethodSymbol"/> name, parameter
/// types, RefKind, return type, nullable annotation. Shapes whose resolution
/// requires a facility the S1 mechanism does not yet expose (properties,
/// indexers, constructors) are pinned with the specific missing capability
/// noted so S4 can add the corresponding <c>MetadataBinder</c> path
/// deliberately, not by accident.
///
/// The scoping doc's discriminating challenge for each shape is quoted in
/// the fixture summary. When a challenge assumes .NET 10 semantics that
/// have shifted (e.g. Shapes 12/13's <c>Oblivious</c> presumption which
/// modern BCL annotates properly), the fixture pins the observed reality
/// and the S4 ledger records the corrected challenge.
/// </summary>
public class BCLCallShapeFixtures
{
    // Shared context — construction is ~50ms so keep one per class.
    private readonly MetadataContext _ctx = MetadataContext.Create();

    /// <summary>Shape 1 — instance property vs method disambiguation.
    /// <c>List&lt;int&gt;.Count</c> is a property; a same-named LINQ
    /// <c>Count()</c> extension method also exists. TryResolveMethodGroup
    /// on the property-only receiver returns empty; the extension is only
    /// reachable via a different resolution path (S4 territory).</summary>
    [Fact]
    public void Shape01_InstanceProperty_MethodGroupIsEmpty()
    {
        var listUnbound = _ctx.TryResolveType("System.Collections.Generic.List`1");
        var intType = _ctx.TryResolveType("System.Int32");
        Assert.NotNull(listUnbound);
        Assert.NotNull(intType);
        var listOfInt = listUnbound!.Construct(intType!);

        var group = _ctx.TryResolveMethodGroup(listOfInt, "Count");
        Assert.Empty(group);

        // The property IS visible via GetMembers directly — S4's binder
        // will route to the appropriate resolution path based on syntax.
        var members = listOfInt.GetMembers("Count").OfType<IPropertySymbol>().ToArray();
        Assert.Single(members);
        Assert.Equal("int", members[0].Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    /// <summary>Shape 2 — instance method, arg-driven overload from the
    /// 18-overload group on Console.WriteLine.</summary>
    [Fact]
    public void Shape02_ConsoleWriteLine_ArgDrivenOverload()
    {
        var console = _ctx.TryResolveType("System.Console");
        var intType = _ctx.TryResolveType("System.Int32");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(console);
        Assert.NotNull(intType);
        Assert.NotNull(stringType);

        var withInt = _ctx.TryResolveOverload(
            console!, "WriteLine",
            new[] { new MetadataArgument(intType!) }, out _);
        var withString = _ctx.TryResolveOverload(
            console!, "WriteLine",
            new[] { new MetadataArgument(stringType!) }, out _);

        Assert.NotNull(withInt);
        Assert.NotNull(withString);
        Assert.Equal("int",
            withInt!.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        Assert.Equal("string?",
            withString!.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    /// <summary>Shape 3 — static generic method, type inferred from arg.
    /// <c>Enumerable.First&lt;T&gt;(IEnumerable&lt;T&gt; source)</c>.
    ///
    /// <b>S4 capability gap:</b> S1's synthetic tree does not carry
    /// <c>using System.Linq;</c>, so extension methods on instance
    /// receivers do not resolve. Directly querying
    /// <c>Enumerable.First</c> as a static method works (asserted below).
    /// S4's MetadataBinder must either (a) add <c>System.Linq</c> to the
    /// synthetic-tree usings when the receiver implements
    /// <c>IEnumerable&lt;T&gt;</c>, or (b) route extension-method
    /// invocation to the reduced static form.</summary>
    [Fact]
    public void Shape03_EnumerableFirst_S1DoesNotResolveExtension_S4Gap()
    {
        var enumerableUnbound = _ctx.TryResolveType("System.Collections.Generic.IEnumerable`1");
        var intType = _ctx.TryResolveType("System.Int32");
        Assert.NotNull(enumerableUnbound);
        Assert.NotNull(intType);
        var enumerableOfInt = enumerableUnbound!.Construct(intType!);

        // Via instance-receiver syntax: fails because System.Linq not in scope.
        var resolved = _ctx.TryResolveOverload(
            enumerableOfInt, "First",
            Array.Empty<MetadataArgument>(), out var reason);
        Assert.Null(resolved);
        Assert.NotNull(reason);

        // Via direct static discovery: succeeds. Pins the capability that
        // S4's binder must plumb through the extension-method path.
        var enumerableStatic = _ctx.TryResolveType("System.Linq.Enumerable");
        Assert.NotNull(enumerableStatic);
        var firstOverloads = enumerableStatic!.GetMembers("First")
            .OfType<IMethodSymbol>().ToArray();
        Assert.NotEmpty(firstOverloads);
        Assert.Contains(firstOverloads, m => m.Parameters.Length == 1);
    }

    /// <summary>Shape 4 — extension method on interface. Same S4 gap as
    /// Shape 3. Assert the static Enumerable.Where overloads exist so S4
    /// has a target to route through.</summary>
    [Fact]
    public void Shape04_EnumerableWhere_S1DoesNotResolveExtension_S4Gap()
    {
        var enumerableUnbound = _ctx.TryResolveType("System.Collections.Generic.IEnumerable`1");
        var intType = _ctx.TryResolveType("System.Int32");
        Assert.NotNull(enumerableUnbound);
        var enumerableOfInt = enumerableUnbound!.Construct(intType!);

        var funcUnbound = _ctx.TryResolveType("System.Func`2");
        var boolType = _ctx.TryResolveType("System.Boolean");
        Assert.NotNull(funcUnbound);
        Assert.NotNull(boolType);
        var predicate = funcUnbound!.Construct(intType!, boolType!);

        // Fails via instance receiver (System.Linq not in synthetic-tree usings).
        var resolved = _ctx.TryResolveOverload(
            enumerableOfInt, "Where",
            new[] { new MetadataArgument(predicate) }, out var reason);
        Assert.Null(resolved);
        Assert.NotNull(reason);

        // Direct static discovery works.
        var enumerableStatic = _ctx.TryResolveType("System.Linq.Enumerable");
        var whereOverloads = enumerableStatic!.GetMembers("Where")
            .OfType<IMethodSymbol>().ToArray();
        Assert.NotEmpty(whereOverloads);
    }

    /// <summary>Shape 5 — <c>params</c> array collapse. Roslyn presents the
    /// overload as expanded (per-arg positions) rather than collapsed to a
    /// single <c>object[]</c> when resolving from N object args.</summary>
    [Fact]
    public void Shape05_StringFormat_ParamsExpansion()
    {
        var stringType = _ctx.TryResolveType("System.String");
        var objectType = _ctx.TryResolveType("System.Object");
        Assert.NotNull(stringType);
        Assert.NotNull(objectType);

        var resolved = _ctx.TryResolveOverload(
            stringType!, "Format",
            new[]
            {
                new MetadataArgument(stringType!),
                new MetadataArgument(objectType!),
                new MetadataArgument(objectType!),
            },
            out var reason);

        Assert.NotNull(resolved);
        Assert.Null(reason);
        Assert.Equal("Format", resolved!.Name);
        // .NET 10 String.Format(string, object?, object?) two-arg-object overload
        // matches exactly on arity (no params expansion needed).
        Assert.True(resolved.Parameters.Length >= 3);
    }

    /// <summary>Shape 6 — nullable-annotated return via
    /// <c>Dictionary&lt;K,V&gt;.TryGetValue(K, out V?)</c>. The out
    /// parameter's type NullableAnnotation is what a null-flow analysis
    /// keys on.</summary>
    [Fact]
    public void Shape06_DictionaryTryGetValue_OutParameter()
    {
        var dictUnbound = _ctx.TryResolveType("System.Collections.Generic.Dictionary`2");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(dictUnbound);
        Assert.NotNull(stringType);
        var dictStringString = dictUnbound!.Construct(stringType!, stringType!);

        var resolved = _ctx.TryResolveOverload(
            dictStringString, "TryGetValue",
            new[]
            {
                new MetadataArgument(stringType!),
                new MetadataArgument(stringType!, RefKind.Out),
            },
            out var reason);

        Assert.NotNull(resolved);
        Assert.Null(reason);
        Assert.Equal("TryGetValue", resolved!.Name);
        Assert.Equal(2, resolved.Parameters.Length);
        Assert.Equal(RefKind.Out, resolved.Parameters[1].RefKind);
    }

    /// <summary>Shape 7 — <c>out int</c> parameter and nullable-annotated
    /// first parameter. <c>int.TryParse(string?, out int)</c>.</summary>
    [Fact]
    public void Shape07_IntTryParse_OutIntAndNullableStringArg()
    {
        var intType = _ctx.TryResolveType("System.Int32");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(intType);
        Assert.NotNull(stringType);

        var resolved = _ctx.TryResolveOverload(
            intType!, "TryParse",
            new[]
            {
                new MetadataArgument(stringType!),
                new MetadataArgument(intType!, RefKind.Out),
            },
            out var reason);

        Assert.NotNull(resolved);
        Assert.Null(reason);
        Assert.True(resolved!.IsStatic);
        // .NET 10 annotates the first param as string? (nullable).
        Assert.Equal(NullableAnnotation.Annotated,
            resolved.Parameters[0].Type.NullableAnnotation);
    }

    /// <summary>Shape 8 — nested generic construction:
    /// <c>Dictionary&lt;string, List&lt;int&gt;&gt;</c>.</summary>
    [Fact]
    public void Shape08_NestedGeneric_ResolvesRecursively()
    {
        var dictUnbound = _ctx.TryResolveType("System.Collections.Generic.Dictionary`2");
        var listUnbound = _ctx.TryResolveType("System.Collections.Generic.List`1");
        var stringType = _ctx.TryResolveType("System.String");
        var intType = _ctx.TryResolveType("System.Int32");
        Assert.NotNull(dictUnbound);
        Assert.NotNull(listUnbound);

        var listOfInt = listUnbound!.Construct(intType!);
        var dict = dictUnbound!.Construct(stringType!, listOfInt);

        Assert.Equal(
            "Dictionary<string, List<int>>",
            dict.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    /// <summary>Shape 9 — overload disambiguation via better conversion.
    /// Same S4 gap as Shapes 3/4/15 — LINQ's SelectMany overloads live on
    /// Enumerable and only resolve via `using System.Linq;`. Direct static
    /// discovery pins two-form (2 params) and three-form (3 params)
    /// coexistence.</summary>
    [Fact]
    public void Shape09_SelectMany_S1DoesNotResolveExtension_S4Gap()
    {
        var enumerableStatic = _ctx.TryResolveType("System.Linq.Enumerable");
        Assert.NotNull(enumerableStatic);

        var selectMany = enumerableStatic!.GetMembers("SelectMany")
            .OfType<IMethodSymbol>().ToArray();
        // Multiple overloads exist; the two-form takes 2 params (source,
        // selector), the three-form takes 3 (source, collectionSelector,
        // resultSelector).
        Assert.Contains(selectMany, m => m.Parameters.Length == 2);
        Assert.Contains(selectMany, m => m.Parameters.Length == 3);
    }

    /// <summary>Shape 10 — constructor overload. <c>MetadataContext</c>
    /// currently only exposes method resolution; constructor resolution is
    /// S4 territory. Pin the gap so S4 adds the path deliberately.</summary>
    [Fact]
    public void Shape10_ConstructorOverload_S4CapabilityGap()
    {
        var listUnbound = _ctx.TryResolveType("System.Collections.Generic.List`1");
        var intType = _ctx.TryResolveType("System.Int32");
        Assert.NotNull(listUnbound);
        var listOfInt = listUnbound!.Construct(intType!);

        // .ctor is discoverable via GetMembers(".ctor") but MetadataContext
        // does not currently route through it — S4's MetadataBinder must
        // add a constructor-specific resolution path.
        var ctors = listOfInt.GetMembers(".ctor").OfType<IMethodSymbol>().ToArray();
        Assert.NotEmpty(ctors);
        // The (int capacity) overload exists.
        var capacityCtor = ctors.FirstOrDefault(c =>
            c.Parameters.Length == 1 &&
            c.Parameters[0].Type.SpecialType == SpecialType.System_Int32);
        Assert.NotNull(capacityCtor);
        Assert.Equal("capacity", capacityCtor!.Parameters[0].Name);
    }

    /// <summary>Shape 11 — indexer. Similar to Shape 10 — indexer
    /// resolution goes through <c>get_Item</c>/<c>set_Item</c> synthesized
    /// accessor methods, which the D4 synthetic-invocation path doesn't
    /// naturally produce. Pin the capability gap.</summary>
    [Fact]
    public void Shape11_Indexer_S4CapabilityGap()
    {
        var dictUnbound = _ctx.TryResolveType("System.Collections.Generic.Dictionary`2");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(dictUnbound);
        var dict = dictUnbound!.Construct(stringType!, stringType!);

        // Indexer synthesis path — S4 must route indexer syntax to get_Item.
        var indexers = dict.GetMembers("this[]").OfType<IPropertySymbol>().ToArray();
        Assert.Single(indexers);
        var indexer = indexers[0];
        Assert.Equal("string", indexer.Parameters[0].Type.ToDisplayString(
            SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    /// <summary>Shape 12 — nullable-oblivious API returning a nullable value.
    /// <b>Scoping-doc assumption drift:</b> the doc's discriminating
    /// challenge presumed .NET 10 might still expose
    /// <c>Environment.GetEnvironmentVariable</c> as Oblivious. In modern .NET
    /// this API IS annotated (return is Annotated). The fixture pins the
    /// observed reality — S4's ledger records the drift.</summary>
    [Fact]
    public void Shape12_GetEnvironmentVariable_ReturnIsAnnotated_NotOblivious()
    {
        var envType = _ctx.TryResolveType("System.Environment");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(envType);
        Assert.NotNull(stringType);

        var resolved = _ctx.TryResolveOverload(
            envType!, "GetEnvironmentVariable",
            new[] { new MetadataArgument(stringType!) },
            out var reason);

        Assert.NotNull(resolved);
        Assert.Null(reason);
        Assert.Equal("GetEnvironmentVariable", resolved!.Name);
        Assert.Equal(NullableAnnotation.Annotated, resolved.ReturnType.NullableAnnotation);
    }

    /// <summary>Shape 13 — mixed static+instance overload group. Regex has
    /// BOTH static <c>Match(input, pattern)</c> and instance
    /// <c>Match(input)</c>. S1's TryResolveOverload picks instance syntax
    /// (mixed group heuristic) which fails to match the 2-arg static
    /// overload. <b>S4 capability gap:</b> when a mixed group has arities
    /// that only match on the static side, syntactic form must switch.
    /// Fixture pins direct static discovery of the annotated (2-arg)
    /// overload.</summary>
    [Fact]
    public void Shape13_RegexMatch_S1MixedGroupInstanceSyntaxFails_S4Gap()
    {
        var regexType = _ctx.TryResolveType("System.Text.RegularExpressions.Regex");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(regexType);
        Assert.NotNull(stringType);

        // S1 path: instance receiver syntax, 2-arg call — no such instance
        // overload, so resolution fails.
        var resolved = _ctx.TryResolveOverload(
            regexType!, "Match",
            new[]
            {
                new MetadataArgument(stringType!),
                new MetadataArgument(stringType!),
            },
            out var reason);
        Assert.Null(resolved);
        Assert.NotNull(reason);

        // Direct-discovery path finds the static 2-arg overload with
        // NotAnnotated string parameters — S4's binder must route here
        // when the arity only matches static.
        var staticMatch = regexType!.GetMembers("Match")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 2);
        Assert.NotNull(staticMatch);
        Assert.Equal(NullableAnnotation.NotAnnotated,
            staticMatch!.Parameters[0].Type.NullableAnnotation);
    }

    /// <summary>Shape 14 — nullable-annotated type argument flowing into a
    /// generic method. <c>list.OfType&lt;string?&gt;()</c> returns
    /// <c>IEnumerable&lt;string?&gt;</c>. The Roslyn API preserves the
    /// annotation through generic type arg inference.</summary>
    [Fact]
    public void Shape14_OfType_NullableTypeArgumentFlows()
    {
        var enumerableType = _ctx.TryResolveType("System.Collections.IEnumerable");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(enumerableType);
        Assert.NotNull(stringType);

        // Construct string? — the annotated variant.
        var annotatedString = stringType!.WithNullableAnnotation(NullableAnnotation.Annotated);

        var group = _ctx.TryResolveMethodGroup(enumerableType!, "OfType");
        // OfType<T>() lives on IEnumerable via extension; the method group
        // on the interface receiver may be empty until S4's binder routes
        // through Enumerable.OfType. Pin the current path.
        Assert.Empty(group);

        // Direct query via Enumerable.OfType.
        var enumerableStatic = _ctx.TryResolveType("System.Linq.Enumerable");
        Assert.NotNull(enumerableStatic);
        var ofType = enumerableStatic!.GetMembers("OfType").OfType<IMethodSymbol>().Single();
        Assert.Equal("OfType", ofType.Name);
        var constructed = ofType.Construct(annotatedString);
        Assert.Equal(NullableAnnotation.Annotated,
            ((INamedTypeSymbol)constructed.ReturnType).TypeArguments[0].NullableAnnotation);
    }

    /// <summary>Shape 15 — interface default method (C# 8+).
    /// <b>Scoping-doc assumption drift + S4 gap:</b>
    /// <c>TryGetNonEnumeratedCount</c> is a LINQ extension in .NET 10, not
    /// a true default-interface-method. The extension-in-scope gap from
    /// Shapes 3/4/9 applies. Fixture pins direct static discovery so S4's
    /// binder has a target; a genuine DIM fixture requires finding a BCL
    /// surface that actually uses the C# 8 feature (uncommon in the base
    /// BCL). Amendment candidate for F-1 once a real DIM surface is
    /// identified.</summary>
    [Fact]
    public void Shape15_TryGetNonEnumeratedCount_IsExtension_S4Gap()
    {
        var enumerableStatic = _ctx.TryResolveType("System.Linq.Enumerable");
        Assert.NotNull(enumerableStatic);

        var tryGetCount = enumerableStatic!.GetMembers("TryGetNonEnumeratedCount")
            .OfType<IMethodSymbol>().ToArray();
        Assert.NotEmpty(tryGetCount);
        var m = tryGetCount[0];
        Assert.True(m.IsExtensionMethod);
        Assert.Equal("System.Linq.Enumerable", m.ContainingType.ToDisplayString());
    }
}
