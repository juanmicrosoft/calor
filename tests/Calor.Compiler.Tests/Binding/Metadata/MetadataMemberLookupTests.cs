using System.Linq;
using Calor.Compiler.Binding.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.17 R3 — inherited-member lookup in the metadata binder.
///
/// <para>Before this change both metadata resolution paths built their method
/// group with <c>receiverType.GetMembers(name)</c>, which returns only the
/// members <em>declared</em> on that one type. An empty group short-circuited
/// to "Receiver 'X' has no member named 'Y'." without Roslyn ever being asked,
/// so every inherited member was unresolvable. Each fact below was one of the
/// residual clusters in the gate-6 corpus measurement
/// (<c>bench/phase0-agent-native/metadata-binding-corpus-ledger.json</c>).</para>
/// </summary>
public class MetadataMemberLookupTests
{
    private readonly MetadataContext _ctx = MetadataContext.Create();
    private readonly MetadataBinder _binder;

    public MetadataMemberLookupTests()
    {
        _binder = new MetadataBinder(_ctx);
    }

    private Compilation Host => _ctx.HostCompilationForBinder;

    private INamedTypeSymbol Type(string metadataName) =>
        Host.GetTypeByMetadataName(metadataName)
        ?? throw new System.InvalidOperationException($"'{metadataName}' is not in the host reference set.");

    private static MetadataArgument[] NoArgs => [];

    // ================================================================
    // System.Object's members are reachable from every receiver
    // ================================================================

    [Fact]
    public void InterfaceReceiver_ResolvesObjectMember()
    {
        // An interface has no BaseType, so `GetMembers("GetType")` on it is
        // empty. Corpus cluster: IValidator/IRequest/INotification.GetType().
        var result = _binder.ResolveCall(Type("System.IDisposable"), "GetType", NoArgs);

        Assert.True(result.IsResolved, result.UnresolvedReason);
        Assert.Equal("GetType", result.Symbol!.Name);
        Assert.Equal(SpecialType.System_Object, result.Symbol.ContainingType.SpecialType);
    }

    [Fact]
    public void EnumReceiver_ResolvesToStringThroughSystemEnum()
    {
        // Corpus cluster: FluentValidation.Severity / Serilog LogEventLevel.
        var result = _binder.ResolveCall(Type("System.DayOfWeek"), "ToString", NoArgs);

        Assert.True(result.IsResolved, result.UnresolvedReason);
        Assert.Equal("ToString", result.Symbol!.Name);
    }

    [Fact]
    public void ArrayReceiver_ResolvesObjectMember()
    {
        // Corpus cluster: `object[]`.GetType().
        var array = Host.CreateArrayTypeSymbol(Host.GetSpecialType(SpecialType.System_Int32));
        var result = _binder.ResolveCall(array, "GetType", NoArgs);

        Assert.True(result.IsResolved, result.UnresolvedReason);
        Assert.Equal("GetType", result.Symbol!.Name);
    }

    [Fact]
    public void ArrayReceiver_ResolvesSystemArrayMember()
    {
        var array = Host.CreateArrayTypeSymbol(Host.GetSpecialType(SpecialType.System_Int32));
        var result = _binder.ResolveCall(array, "GetLength",
            [new MetadataArgument(Host.GetSpecialType(SpecialType.System_Int32))]);

        Assert.True(result.IsResolved, result.UnresolvedReason);
        Assert.Equal("GetLength", result.Symbol!.Name);
    }

    // ================================================================
    // Base classes and base interfaces
    // ================================================================

    [Fact]
    public void DerivedClassReceiver_ResolvesBaseClassMember()
    {
        // `GetParameters` is declared on MethodBase, not MethodInfo.
        // Corpus cluster: MethodInfo.Invoke / MethodInfo.GetParameters.
        var result = _binder.ResolveCall(Type("System.Reflection.MethodInfo"), "GetParameters", NoArgs);

        Assert.True(result.IsResolved, result.UnresolvedReason);
        Assert.Equal("MethodBase", result.Symbol!.ContainingType.Name);
    }

    [Fact]
    public void InterfaceReceiver_ResolvesBaseInterfaceMember()
    {
        // `Add` is declared on ICollection<T>, not IList<T>.
        // Corpus cluster: IList<Order>.Add(order) — 8 candidates.
        var int32 = Host.GetSpecialType(SpecialType.System_Int32);
        var listOfInt = Type("System.Collections.Generic.IList`1").Construct(int32);

        var result = _binder.ResolveCall(listOfInt, "Add", [new MetadataArgument(int32)]);

        Assert.True(result.IsResolved, result.UnresolvedReason);
        Assert.Equal("ICollection", result.Symbol!.ContainingType.Name);
    }

    // ================================================================
    // Type parameters — members come from the constraints, plus object's
    // ================================================================

    [Fact]
    public void TypeParameterReceiver_ResolvesConstraintMember()
    {
        // Corpus cluster: TProperty.CompareTo(TProperty) — 5 candidates.
        var t = TypeParameterFrom("interface I { } class C<T> where T : System.IComparable { }");

        var result = _binder.ResolveCall(t, "CompareTo",
            [new MetadataArgument(Host.GetSpecialType(SpecialType.System_Object))]);

        Assert.True(result.IsResolved, result.UnresolvedReason);
        Assert.Equal("CompareTo", result.Symbol!.Name);
    }

    [Fact]
    public void TypeParameterReceiver_ResolvesObjectMember()
    {
        // Corpus cluster: T.Equals / T.ToString / TRequest.GetType.
        var t = TypeParameterFrom("class C<T> { }");

        var result = _binder.ResolveCall(t, "GetType", NoArgs);

        Assert.True(result.IsResolved, result.UnresolvedReason);
        Assert.Equal(SpecialType.System_Object, result.Symbol!.ContainingType.SpecialType);
    }

    // ================================================================
    // The widened surface must not resolve what does not exist
    // ================================================================

    [Fact]
    public void UnknownMember_StillUnresolved()
    {
        var result = _binder.ResolveCall(Type("System.IDisposable"), "NoSuchMemberAnywhere", NoArgs);

        Assert.False(result.IsResolved);
        Assert.NotNull(result.UnresolvedReason);
    }

    [Fact]
    public void InheritedMember_WithWrongArguments_StillUnresolved()
    {
        // `Add` exists on ICollection<int> but takes one int, not two strings.
        var int32 = Host.GetSpecialType(SpecialType.System_Int32);
        var str = Host.GetSpecialType(SpecialType.System_String);
        var listOfInt = Type("System.Collections.Generic.IList`1").Construct(int32);

        var result = _binder.ResolveCall(listOfInt, "Add",
            [new MetadataArgument(str), new MetadataArgument(str)]);

        Assert.False(result.IsResolved);
    }

    // ================================================================
    // helpers
    // ================================================================

    /// <summary>
    /// Builds a type-parameter symbol by parsing <paramref name="source"/> into
    /// the host compilation and returning class <c>C</c>'s single parameter.
    /// </summary>
    private ITypeParameterSymbol TypeParameterFrom(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var extended = Host.AddSyntaxTrees(tree);
        var c = (INamedTypeSymbol)extended.GetSymbolsWithName("C", SymbolFilter.Type).Single();
        return c.TypeParameters.Single();
    }
}
