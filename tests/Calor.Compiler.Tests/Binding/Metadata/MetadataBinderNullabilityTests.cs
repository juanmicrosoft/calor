using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Binding.Metadata;
using Xunit;
using BoundNullableAnnotation = Calor.Compiler.Binding.BoundTypes.NullableAnnotation;
using RoslynNullableAnnotation = Microsoft.CodeAnalysis.NullableAnnotation;

namespace Calor.Compiler.Tests;

/// <summary>
/// S2 nullability wiring tests for v0.14 nullability enforcement workstream
/// (issue #875). Verifies that Roslyn's NullableAnnotation propagates through
/// MetadataBinderResult.GetReturnBoundType() and GetParameterBoundTypes()
/// into the NominalBoundType.NullableAnnotation field.
///
/// See docs/plans/v0.14-nullability-enforcement-scoping.md §3 S2.
///
/// No diagnostics fire from these tests — that's S3's job.
/// </summary>
public class MetadataBinderNullabilityTests
{
    private readonly MetadataContext _ctx = MetadataContext.Create();
    private readonly MetadataBinder _binder;

    public MetadataBinderNullabilityTests()
    {
        _binder = new MetadataBinder(_ctx);
    }

    // ================================================================
    // MapAnnotation — the pure enum translation
    // ================================================================

    [Fact]
    public void MapAnnotation_Roslyn_Annotated_Becomes_Annotated()
    {
        Assert.Equal(
            BoundNullableAnnotation.Annotated,
            MetadataBinderResult.MapAnnotation(RoslynNullableAnnotation.Annotated));
    }

    [Fact]
    public void MapAnnotation_Roslyn_NotAnnotated_Becomes_NotAnnotated()
    {
        Assert.Equal(
            BoundNullableAnnotation.NotAnnotated,
            MetadataBinderResult.MapAnnotation(RoslynNullableAnnotation.NotAnnotated));
    }

    [Fact]
    public void MapAnnotation_Roslyn_None_Becomes_Oblivious_PerD3()
    {
        // Per docs/plans/v0.14-nullability-enforcement-scoping.md D3:
        // Roslyn's None (unannotated third-party surface) is treated as
        // Oblivious → conservative "possibly-null" at check time.
        Assert.Equal(
            BoundNullableAnnotation.Oblivious,
            MetadataBinderResult.MapAnnotation(RoslynNullableAnnotation.None));
    }

    // ================================================================
    // GetReturnBoundType — reads Roslyn annotation on return type
    // ================================================================

    /// <summary>
    /// #875's canonical D3 repro: <c>Environment.GetEnvironmentVariable(string)</c>
    /// returns <c>string?</c>. The BoundType's NullableAnnotation must be
    /// Annotated — this is the exact call that the option-A fix at bind time
    /// (S3+) will reject when the target Calor binding declares <c>:string</c>.
    /// </summary>
    [Fact]
    public void GetReturnBoundType_EnvironmentGetVariable_IsAnnotated()
    {
        var envType = _ctx.TryResolveType("System.Environment");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(envType);
        Assert.NotNull(stringType);

        var result = _binder.ResolveCall(envType!, "GetEnvironmentVariable",
            new[] { new MetadataArgument(stringType!) });
        Assert.True(result.IsResolved);

        var returnType = result.GetReturnBoundType();
        Assert.NotNull(returnType);
        Assert.Equal(BoundNullableAnnotation.Annotated, returnType!.NullableAnnotation);
        Assert.Equal("string?", returnType.QualifiedName);
    }

    /// <summary>
    /// A non-nullable BCL return: <c>int.Parse(string)</c> returns <c>int</c>,
    /// which as a value type does not carry a NullableAnnotation. The BoundType
    /// still produces a well-defined annotation (NotAnnotated is Roslyn's
    /// default for value types).
    /// </summary>
    [Fact]
    public void GetReturnBoundType_IntParse_IsNotAnnotated()
    {
        var intType = _ctx.TryResolveType("System.Int32");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(intType);
        Assert.NotNull(stringType);

        var result = _binder.ResolveCall(intType!, "Parse",
            new[] { new MetadataArgument(stringType!) });
        Assert.True(result.IsResolved);

        var returnType = result.GetReturnBoundType();
        Assert.NotNull(returnType);
        Assert.Equal(BoundNullableAnnotation.NotAnnotated, returnType!.NullableAnnotation);
    }

    /// <summary>
    /// <c>string.Concat(string, string)</c> returns non-nullable <c>string</c>.
    /// This is the "safe interop" case: an annotated BCL method whose contract
    /// says never-null, which S3's check should accept when assigned to <c>:string</c>.
    /// </summary>
    [Fact]
    public void GetReturnBoundType_StringConcat_IsNotAnnotated()
    {
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(stringType);

        var result = _binder.ResolveCall(stringType!, "Concat",
            new[] { new MetadataArgument(stringType!), new MetadataArgument(stringType!) });
        Assert.True(result.IsResolved);

        var returnType = result.GetReturnBoundType();
        Assert.NotNull(returnType);
        Assert.Equal(BoundNullableAnnotation.NotAnnotated, returnType!.NullableAnnotation);
        Assert.Equal("string", returnType.QualifiedName);
    }

    // ================================================================
    // GetParameterBoundTypes — reads Roslyn annotation on parameter types
    // ================================================================

    /// <summary>
    /// <c>string.Concat(string?, string?)</c> takes annotated-nullable
    /// parameters in the .NET 10 BCL — Concat handles null by treating it
    /// as empty. This test verifies parameter annotations propagate through
    /// GetParameterBoundTypes(), even when the annotation is Annotated
    /// (the "input may be null" case).
    /// </summary>
    [Fact]
    public void GetParameterBoundTypes_StringConcat_ParametersAreAnnotated()
    {
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(stringType);

        var result = _binder.ResolveCall(stringType!, "Concat",
            new[] { new MetadataArgument(stringType!), new MetadataArgument(stringType!) });
        Assert.True(result.IsResolved);

        var paramTypes = result.GetParameterBoundTypes();
        Assert.Equal(2, paramTypes.Count);
        // Both string parameters are Annotated in the .NET 10 BCL Concat surface.
        Assert.Equal(BoundNullableAnnotation.Annotated, paramTypes[0].NullableAnnotation);
        Assert.Equal(BoundNullableAnnotation.Annotated, paramTypes[1].NullableAnnotation);
    }

    /// <summary>
    /// <c>int.Parse(string)</c> takes a non-nullable string parameter — a
    /// case where the parameter annotation should be NotAnnotated. Confirms
    /// parameter-side annotation propagation for the strict-input case.
    /// </summary>
    [Fact]
    public void GetParameterBoundTypes_IntParse_ParameterIsNotAnnotated()
    {
        var intType = _ctx.TryResolveType("System.Int32");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(intType);
        Assert.NotNull(stringType);

        var result = _binder.ResolveCall(intType!, "Parse",
            new[] { new MetadataArgument(stringType!) });
        Assert.True(result.IsResolved);

        var paramTypes = result.GetParameterBoundTypes();
        Assert.Single(paramTypes);
        Assert.Equal(BoundNullableAnnotation.NotAnnotated, paramTypes[0].NullableAnnotation);
    }

    // ================================================================
    // Non-resolved results return null / empty
    // ================================================================

    [Fact]
    public void GetReturnBoundType_UnresolvedResult_ReturnsNull()
    {
        var unresolved = MetadataBinderResult.CreateUnresolved("no such method");
        Assert.Null(unresolved.GetReturnBoundType());
    }

    [Fact]
    public void GetParameterBoundTypes_UnresolvedResult_ReturnsEmpty()
    {
        var unresolved = MetadataBinderResult.CreateUnresolved("no such method");
        Assert.Empty(unresolved.GetParameterBoundTypes());
    }

    // ================================================================
    // RoslynSymbol back-reference preserved
    // ================================================================

    [Fact]
    public void GetReturnBoundType_PreservesRoslynSymbolBackreference()
    {
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(stringType);

        var result = _binder.ResolveCall(stringType!, "Concat",
            new[] { new MetadataArgument(stringType!), new MetadataArgument(stringType!) });
        var returnType = result.GetReturnBoundType();

        Assert.NotNull(returnType);
        Assert.NotNull(returnType!.RoslynSymbol);
        // The Roslyn back-ref should be the concrete return type symbol.
        Assert.Equal("String", returnType.RoslynSymbol!.Name);
    }

    // ================================================================
    // v0.14 §S6 (task #7 Phase-C): array-return BoundType plumbing —
    // GetReturnBoundTypeEx surfaces IArrayTypeSymbol returns as
    // ArrayBoundType (element-annotation preserved).
    // ================================================================

    /// <summary>
    /// A BCL call whose return type is <c>string[]</c>
    /// (e.g. <c>Environment.GetCommandLineArgs()</c>) must surface as an
    /// <see cref="ArrayBoundType"/> whose element is a STRING
    /// NominalBoundType carrying the Roslyn-declared element annotation.
    /// Before §S6, GetReturnBoundType flattened arrays into a nominal
    /// <c>"string[]"</c> and downstream checks lost the element shape.
    /// </summary>
    [Fact]
    public void GetReturnBoundTypeEx_StringArrayReturn_Surfaces_As_ArrayBoundType()
    {
        var envType = _ctx.TryResolveType("System.Environment");
        Assert.NotNull(envType);

        var result = _binder.ResolveCall(envType!, "GetCommandLineArgs",
            Array.Empty<MetadataArgument>());
        Assert.True(result.IsResolved);

        var returnType = result.GetReturnBoundTypeEx();
        var array = Assert.IsType<ArrayBoundType>(returnType);
        var element = Assert.IsType<NominalBoundType>(array.ElementType);
        // Roslyn reports "string" (not "System.String") through
        // ToDisplayString by default. Match the observable spelling.
        Assert.Equal("string", element.QualifiedName);
    }

    /// <summary>
    /// A non-array return still flows through
    /// <see cref="MetadataBinderResult.GetReturnBoundTypeEx"/> as a
    /// <see cref="NominalBoundType"/> — S6 widens the return shape
    /// discretely on arrays only, leaving the scalar path unchanged.
    /// </summary>
    [Fact]
    public void GetReturnBoundTypeEx_ScalarReturn_Surfaces_As_NominalBoundType()
    {
        var envType = _ctx.TryResolveType("System.Environment");
        var stringType = _ctx.TryResolveType("System.String");
        Assert.NotNull(envType);
        Assert.NotNull(stringType);

        var result = _binder.ResolveCall(envType!, "GetEnvironmentVariable",
            new[] { new MetadataArgument(stringType!) });
        Assert.True(result.IsResolved);

        var returnType = result.GetReturnBoundTypeEx();
        Assert.IsType<NominalBoundType>(returnType);
    }

    /// <summary>
    /// Unresolved results yield null from the extended API too — matches
    /// <see cref="MetadataBinderResult.GetReturnBoundType"/>'s contract.
    /// </summary>
    [Fact]
    public void GetReturnBoundTypeEx_UnresolvedResult_ReturnsNull()
    {
        var unresolved = MetadataBinderResult.CreateUnresolved("no such method");
        Assert.Null(unresolved.GetReturnBoundTypeEx());
    }
}
