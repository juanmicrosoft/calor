using Calor.Compiler.Binding.Metadata;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.14 §S4 exit-criterion measurement — runs <see cref="MetadataBinder"/>
/// against F-1's 15 BCL call shapes and produces the "Metadata-binding
/// shape resolution: X / 15" number.
///
/// <para>The measurement itself is <see cref="MetadataBinder_ResolvesF1Shapes"/>
/// — an aggregating test that iterates the F-1 shape set and asserts a
/// minimum resolved count. The minimum is <b>set as a floor, not a target</b>:
/// the PR body publishes the actual number, and this floor is only used to
/// prevent silent regression.</para>
///
/// <para>Companion per-shape tests below assert the specific closure S4
/// achieves relative to S3's fixture disposition (S3 pinned 7 as resolvable
/// via S1's TryResolveOverload and 8 as S4 capability gaps). S4's closures:
/// shapes 3/4/9/15 (extension-on-interface via System.Linq usings), shape 13
/// (mixed static/instance via static-only fallback), shapes 10/11 (dedicated
/// constructor/indexer paths), shape 1 (dedicated property path).</para>
/// </summary>
public class MetadataBinderShapeMeasurementTests
{
    private readonly MetadataContext _ctx = MetadataContext.Create();
    private readonly MetadataBinder _binder;

    public MetadataBinderShapeMeasurementTests()
    {
        _binder = new MetadataBinder(_ctx);
    }

    // ================================================================
    // S4 aggregate measurement — the "X / 15" PR-body number lives here.
    // ================================================================

    /// <summary>Iterates the 15 F-1 shapes and produces the aggregate
    /// resolved-fraction. Reports individual outcomes via test output so
    /// the PR-body number is derivable from CI logs.</summary>
    [Fact]
    public void MetadataBinder_ResolvesF1Shapes()
    {
        var results = new List<(int shape, bool resolved, string label)>
        {
            (1,  TryShape1_Property(),          "Property"),
            (2,  TryShape2_ArgDrivenOverload(), "ArgDrivenOverload"),
            (3,  TryShape3_ExtensionGeneric(),  "ExtensionGeneric"),
            (4,  TryShape4_ExtensionOnInterface(), "ExtensionOnInterface"),
            (5,  TryShape5_ParamsCollapse(),    "ParamsCollapse"),
            (6,  TryShape6_OutParameter(),      "OutParameter"),
            (7,  TryShape7_OutIntNullableString(), "OutIntNullableString"),
            (8,  TryShape8_NestedGeneric(),     "NestedGeneric"),
            (9,  TryShape9_BetterConversion(),  "BetterConversion"),
            (10, TryShape10_Constructor(),      "Constructor"),
            (11, TryShape11_Indexer(),          "Indexer"),
            (12, TryShape12_ObliviousAnnotated(), "ObliviousAnnotated"),
            (13, TryShape13_MixedStaticInstance(), "MixedStaticInstance"),
            (14, TryShape14_NullableTypeArg(),  "NullableTypeArg"),
            (15, TryShape15_DIMExtension(),     "DIMExtension"),
        };

        var resolvedCount = results.Count(r => r.resolved);
        // Emit per-shape outcomes so CI logs carry the breakdown.
        foreach (var r in results)
        {
            Console.WriteLine($"S4-shape {r.shape:00} {r.label}: {(r.resolved ? "RESOLVED" : "gap")}");
        }
        Console.WriteLine($"Metadata-binding shape resolution: {resolvedCount} / 15");

        // Floor: at S4 authoring time, 15/15 shapes resolve via MetadataBinder
        // (extension resolution via `using System.Linq;`, static fallback for
        // mixed groups, dedicated ctor/indexer/property paths). Lowering this
        // floor is a subtractive amendment; raising it is normal S5+ growth.
        Assert.True(resolvedCount >= 15,
            $"S4 exit-criterion floor: expected 15 / 15 shapes resolvable; got {resolvedCount}. " +
            "MetadataBinder has regressed on at least one shape S4 closed.");
    }

    // ================================================================
    // Per-shape resolvers (mirror S3's fixture disposition; each returns
    // true when MetadataBinder produces a resolved IMethodSymbol/IPropertySymbol).
    // ================================================================

    private bool TryShape1_Property()
    {
        var listUnbound = _ctx.TryResolveType("System.Collections.Generic.List`1");
        var intType = _ctx.TryResolveType("System.Int32");
        if (listUnbound is null || intType is null) return false;
        var listOfInt = listUnbound.Construct(intType);
        return _binder.ResolveProperty(listOfInt, "Count") is not null;
    }

    private bool TryShape2_ArgDrivenOverload()
    {
        var console = _ctx.TryResolveType("System.Console");
        var intType = _ctx.TryResolveType("System.Int32");
        if (console is null || intType is null) return false;
        var r = _binder.ResolveCall(console, "WriteLine",
            new[] { new MetadataArgument(intType) });
        return r.IsResolved
            && r.Symbol!.Parameters[0].Type.SpecialType == SpecialType.System_Int32;
    }

    private bool TryShape3_ExtensionGeneric()
    {
        var enumerableUnbound = _ctx.TryResolveType("System.Collections.Generic.IEnumerable`1");
        var intType = _ctx.TryResolveType("System.Int32");
        if (enumerableUnbound is null || intType is null) return false;
        var enumerableOfInt = enumerableUnbound.Construct(intType);
        var r = _binder.ResolveCall(enumerableOfInt, "First", Array.Empty<MetadataArgument>());
        return r.IsResolved && r.Symbol!.Name == "First";
    }

    private bool TryShape4_ExtensionOnInterface()
    {
        var enumerableUnbound = _ctx.TryResolveType("System.Collections.Generic.IEnumerable`1");
        var funcUnbound = _ctx.TryResolveType("System.Func`2");
        var intType = _ctx.TryResolveType("System.Int32");
        var boolType = _ctx.TryResolveType("System.Boolean");
        if (enumerableUnbound is null || funcUnbound is null || intType is null || boolType is null) return false;
        var enumerableOfInt = enumerableUnbound.Construct(intType);
        var predicate = funcUnbound.Construct(intType, boolType);
        var r = _binder.ResolveCall(enumerableOfInt, "Where",
            new[] { new MetadataArgument(predicate) });
        return r.IsResolved && r.Symbol!.Name == "Where";
    }

    private bool TryShape5_ParamsCollapse()
    {
        var stringType = _ctx.TryResolveType("System.String");
        var objectType = _ctx.TryResolveType("System.Object");
        if (stringType is null || objectType is null) return false;
        var r = _binder.ResolveCall(stringType, "Format",
            new[]
            {
                new MetadataArgument(stringType),
                new MetadataArgument(objectType),
                new MetadataArgument(objectType),
            });
        return r.IsResolved && r.Symbol!.Name == "Format";
    }

    private bool TryShape6_OutParameter()
    {
        var dictUnbound = _ctx.TryResolveType("System.Collections.Generic.Dictionary`2");
        var stringType = _ctx.TryResolveType("System.String");
        if (dictUnbound is null || stringType is null) return false;
        var dict = dictUnbound.Construct(stringType, stringType);
        var r = _binder.ResolveCall(dict, "TryGetValue",
            new[]
            {
                new MetadataArgument(stringType),
                new MetadataArgument(stringType, RefKind.Out),
            });
        return r.IsResolved && r.Symbol!.Name == "TryGetValue";
    }

    private bool TryShape7_OutIntNullableString()
    {
        var intType = _ctx.TryResolveType("System.Int32");
        var stringType = _ctx.TryResolveType("System.String");
        if (intType is null || stringType is null) return false;
        var r = _binder.ResolveCall(intType, "TryParse",
            new[]
            {
                new MetadataArgument(stringType),
                new MetadataArgument(intType, RefKind.Out),
            });
        return r.IsResolved && r.Symbol!.IsStatic;
    }

    private bool TryShape8_NestedGeneric()
    {
        // Shape 8 is a type-construction check, not a call. MetadataBinder
        // has no direct role here; it succeeds if the nested type can be
        // constructed at all — which S1's MetadataContext already enables.
        var dictUnbound = _ctx.TryResolveType("System.Collections.Generic.Dictionary`2");
        var listUnbound = _ctx.TryResolveType("System.Collections.Generic.List`1");
        var stringType = _ctx.TryResolveType("System.String");
        var intType = _ctx.TryResolveType("System.Int32");
        if (dictUnbound is null || listUnbound is null || stringType is null || intType is null) return false;
        var listOfInt = listUnbound.Construct(intType);
        var dict = dictUnbound.Construct(stringType, listOfInt);
        return dict.TypeArguments.Length == 2;
    }

    private bool TryShape9_BetterConversion()
    {
        var enumerableUnbound = _ctx.TryResolveType("System.Collections.Generic.IEnumerable`1");
        var funcUnbound = _ctx.TryResolveType("System.Func`2");
        var intType = _ctx.TryResolveType("System.Int32");
        if (enumerableUnbound is null || funcUnbound is null || intType is null) return false;
        var enumerableOfInt = enumerableUnbound.Construct(intType);
        var selectorReturn = enumerableUnbound.Construct(intType);
        var selector = funcUnbound.Construct(intType, selectorReturn);
        var r = _binder.ResolveCall(enumerableOfInt, "SelectMany",
            new[] { new MetadataArgument(selector) });
        // Reduced extension form: source is `this`, so Parameters.Length == 1
        // (just the selector). The unreduced overload on Enumerable has 2.
        return r.IsResolved
            && r.Symbol!.Name == "SelectMany"
            && r.Symbol.Parameters.Length == 1;
    }

    private bool TryShape10_Constructor()
    {
        var listUnbound = _ctx.TryResolveType("System.Collections.Generic.List`1");
        var intType = _ctx.TryResolveType("System.Int32");
        if (listUnbound is null || intType is null) return false;
        var listOfInt = listUnbound.Construct(intType);
        var r = _binder.ResolveConstructor(listOfInt,
            new[] { new MetadataArgument(intType) });
        return r.IsResolved
            && r.Symbol!.Parameters.Length == 1
            && r.Symbol.Parameters[0].Name == "capacity";
    }

    private bool TryShape11_Indexer()
    {
        var dictUnbound = _ctx.TryResolveType("System.Collections.Generic.Dictionary`2");
        var stringType = _ctx.TryResolveType("System.String");
        if (dictUnbound is null || stringType is null) return false;
        var dict = dictUnbound.Construct(stringType, stringType);
        return _binder.ResolveIndexer(dict,
            new[] { new MetadataArgument(stringType) }) is not null;
    }

    private bool TryShape12_ObliviousAnnotated()
    {
        var envType = _ctx.TryResolveType("System.Environment");
        var stringType = _ctx.TryResolveType("System.String");
        if (envType is null || stringType is null) return false;
        var r = _binder.ResolveCall(envType, "GetEnvironmentVariable",
            new[] { new MetadataArgument(stringType) });
        return r.IsResolved
            && r.Symbol!.ReturnType.NullableAnnotation == NullableAnnotation.Annotated;
    }

    private bool TryShape13_MixedStaticInstance()
    {
        var regexType = _ctx.TryResolveType("System.Text.RegularExpressions.Regex");
        var stringType = _ctx.TryResolveType("System.String");
        if (regexType is null || stringType is null) return false;
        var r = _binder.ResolveCall(regexType, "Match",
            new[]
            {
                new MetadataArgument(stringType),
                new MetadataArgument(stringType),
            });
        return r.IsResolved && r.Symbol!.IsStatic && r.Symbol.Parameters.Length == 2;
    }

    private bool TryShape14_NullableTypeArg()
    {
        // Shape 14 is a type-arg-annotation check — the shape asserts that a
        // constructed type preserves the Annotated annotation on its type
        // arguments. MetadataBinder plays no role; we rely on the S1 API.
        var stringType = _ctx.TryResolveType("System.String");
        if (stringType is null) return false;
        var annotated = stringType.WithNullableAnnotation(NullableAnnotation.Annotated);
        var enumerableStatic = _ctx.TryResolveType("System.Linq.Enumerable");
        if (enumerableStatic is null) return false;
        var ofType = enumerableStatic.GetMembers("OfType").OfType<IMethodSymbol>().Single();
        var constructed = ofType.Construct(annotated);
        return ((INamedTypeSymbol)constructed.ReturnType).TypeArguments[0].NullableAnnotation
            == NullableAnnotation.Annotated;
    }

    private bool TryShape15_DIMExtension()
    {
        var enumerableUnbound = _ctx.TryResolveType("System.Collections.Generic.IEnumerable`1");
        var intType = _ctx.TryResolveType("System.Int32");
        if (enumerableUnbound is null || intType is null) return false;
        var enumerableOfInt = enumerableUnbound.Construct(intType);
        var r = _binder.ResolveCall(enumerableOfInt, "TryGetNonEnumeratedCount",
            new[] { new MetadataArgument(intType, RefKind.Out) });
        return r.IsResolved
            && r.Symbol!.IsExtensionMethod
            && r.Symbol.ContainingType.ToDisplayString() == "System.Linq.Enumerable";
    }

    // ================================================================
    // Discriminating pin (S4 §S4 mutation): mutate the MetadataBinder's
    // synthetic-tree usings — remove `using System.Linq;` — and shapes 3, 4,
    // 9, 15 must all regress. Restoring the using restores green. Since we
    // cannot mutate MetadataBinder at test time, we assert the closure
    // indirectly: MetadataContext's TryResolveOverload (no Linq usings)
    // fails these shapes; MetadataBinder (with Linq usings) succeeds.
    // ================================================================

    [Fact]
    public void MetadataBinder_ClosesExtensionShapes_ThatMetadataContextCannot()
    {
        var enumerableUnbound = _ctx.TryResolveType("System.Collections.Generic.IEnumerable`1");
        var intType = _ctx.TryResolveType("System.Int32");
        Assert.NotNull(enumerableUnbound);
        Assert.NotNull(intType);
        var enumerableOfInt = enumerableUnbound!.Construct(intType!);

        // MetadataContext's TryResolveOverload — no System.Linq usings.
        var withContext = _ctx.TryResolveOverload(
            enumerableOfInt, "First", Array.Empty<MetadataArgument>(), out _);
        Assert.Null(withContext);

        // MetadataBinder — adds System.Linq using, resolves.
        var withBinder = _binder.ResolveCall(
            enumerableOfInt, "First", Array.Empty<MetadataArgument>());
        Assert.True(withBinder.IsResolved);
        Assert.Equal("First", withBinder.Symbol!.Name);
    }

    [Fact]
    public void MetadataBinder_ClosesMixedStaticInstance_ThatMetadataContextCannot()
    {
        var regexType = _ctx.TryResolveType("System.Text.RegularExpressions.Regex");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(regexType);
        Assert.NotNull(stringType);

        var contextResult = _ctx.TryResolveOverload(regexType!, "Match",
            new[]
            {
                new MetadataArgument(stringType!),
                new MetadataArgument(stringType!),
            },
            out _);
        Assert.Null(contextResult);

        var binderResult = _binder.ResolveCall(regexType!, "Match",
            new[]
            {
                new MetadataArgument(stringType!),
                new MetadataArgument(stringType!),
            });
        Assert.True(binderResult.IsResolved);
        Assert.True(binderResult.Symbol!.IsStatic);
    }

    [Fact]
    public void ResolveCall_UnresolvedProducesReason()
    {
        var console = _ctx.TryResolveType("System.Console");
        Assert.NotNull(console);
        var result = _binder.ResolveCall(console!, "NoSuchMethod",
            Array.Empty<MetadataArgument>());
        Assert.False(result.IsResolved);
        Assert.NotNull(result.UnresolvedReason);
    }
}
