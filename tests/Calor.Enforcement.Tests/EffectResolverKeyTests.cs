using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// v0.15 E1 slice 2c, review round 1 (MINOR 9) — the equality contract of
/// <see cref="EffectResolverKey"/>, pinned directly rather than inferred from
/// resolution behaviour.
///
/// <para>The key is a dictionary key twice over: it is the manifest cache's key
/// and the resolution cache's key. Every one of its equality decisions is
/// therefore a correctness decision, and round 1 found two of them unpinned —
/// collapsing null and empty <c>ParameterTypes</c> left the entire suite green,
/// even though the six-step order depends on the distinction (step 2a probes the
/// signature form, step 2b the name-only form). A test that only exercises
/// equality through resolution cannot see that, because manifests happen not to
/// use both forms for one member.</para>
/// </summary>
public class EffectResolverKeyTests
{
    /// <summary>
    /// <c>Kind</c> is in equality. This is what replaced the resolver's old
    /// <c>"m:"</c>/<c>"g:"</c>/<c>"s:"</c>/<c>"c:"</c> cache-key prefixes: without
    /// it, a method named <c>set_X</c> and the setter of <c>X</c> collide, and
    /// whichever resolves first poisons the other's cached answer.
    /// </summary>
    [Theory]
    [InlineData(EffectMemberKind.Method, EffectMemberKind.Getter)]
    [InlineData(EffectMemberKind.Method, EffectMemberKind.Setter)]
    [InlineData(EffectMemberKind.Method, EffectMemberKind.Constructor)]
    [InlineData(EffectMemberKind.Method, EffectMemberKind.Extension)]
    [InlineData(EffectMemberKind.Getter, EffectMemberKind.Setter)]
    public void KindIsPartOfIdentity(EffectMemberKind left, EffectMemberKind right)
    {
        var a = EffectResolverKey.FromStrings("Vendor.Widget", "Value", kind: left);
        var b = EffectResolverKey.FromStrings("Vendor.Widget", "Value", kind: right);

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// <b>No parameter list</b> and <b>an empty parameter list</b> are different
    /// keys. A manifest's <c>"ReadAllText"</c> entry answers for any overload;
    /// its <c>"ReadLine()"</c> entry answers only for the zero-argument one. The
    /// pre-slice string dictionary drew the same distinction between its
    /// <c>"Name"</c> and <c>"Name()"</c> keys; collapsing it here would make
    /// step 2b's fallback indistinguishable from step 2a's probe.
    /// </summary>
    [Fact]
    public void NullParameterList_IsNotTheSameKeyAsAnEmptyOne()
    {
        var explicitlyZeroArguments = EffectResolverKey.FromStrings("System.Console", "ReadLine");
        var noParameterListNamed = explicitlyZeroArguments.WithoutParameterList();

        Assert.NotNull(explicitlyZeroArguments.ParameterTypes);
        Assert.Empty(explicitlyZeroArguments.ParameterTypes!);
        Assert.Null(noParameterListNamed.ParameterTypes);

        Assert.NotEqual(explicitlyZeroArguments, noParameterListNamed);
        Assert.NotEqual(explicitlyZeroArguments.GetHashCode(), noParameterListNamed.GetHashCode());
    }

    /// <summary>
    /// Parameter types are part of identity, and are normalized first, so the
    /// Calor and CLR spellings of one signature are one key rather than two.
    /// </summary>
    [Theory]
    [InlineData("i32", "System.Int32", true)]
    [InlineData("str", "System.String", true)]
    [InlineData("i32", "System.String", false)]
    public void ParameterTypesAreNormalizedThenCompared(string left, string right, bool expectEqual)
    {
        var a = EffectResolverKey.FromStrings("Vendor.Widget", "Transform", [left]);
        var b = EffectResolverKey.FromStrings("Vendor.Widget", "Transform", [right]);

        Assert.Equal(expectEqual, a.Equals(b));
    }

    /// <summary>
    /// Declaring type and member name are part of identity — the obvious half,
    /// pinned so the theory below reads as a partition rather than a list.
    /// </summary>
    [Theory]
    [InlineData("Vendor.Widget", "Save", "Vendor.Other", "Save")]
    [InlineData("Vendor.Widget", "Save", "Vendor.Widget", "Load")]
    public void DeclaringTypeAndMemberNameArePartOfIdentity(
        string leftType, string leftMember, string rightType, string rightMember)
    {
        Assert.NotEqual(
            EffectResolverKey.FromStrings(leftType, leftMember),
            EffectResolverKey.FromStrings(rightType, rightMember));
    }

    /// <summary>
    /// PROVENANCE IS OUTSIDE EQUALITY, and this is the pin that says so on
    /// purpose rather than by omission.
    ///
    /// <para>A key built from a bound receiver and a key built from text must be
    /// the SAME cache entry when they name the same member. If provenance split
    /// them, one member would resolve twice and — worse — the two answers could
    /// diverge as the manifest set changed underneath them. The counting that
    /// feeds the key ledger happens per call site in
    /// <c>EffectResolver.Resolve</c>, before the cache, precisely so that
    /// keeping provenance out of equality costs no measurement.</para>
    /// </summary>
    [Fact]
    public void ProvenanceAndReceiverInterfacesAreOutsideEquality()
    {
        var fromText = EffectResolverKey.FromStrings("STRING[]", "Select");
        var fromBinder = EffectResolverKey.FromBoundReceiver(
            new Calor.Compiler.Binding.BoundTypes.ArrayBoundType(
                new Calor.Compiler.Binding.BoundTypes.NominalBoundType("STRING")),
            "Select");

        // Same member, different provenance, and the interface set differs too.
        Assert.Equal(fromText.DeclaringType, fromBinder.DeclaringType);
        Assert.True(fromText.FromStringFallback);
        Assert.False(fromBinder.FromStringFallback);
        Assert.Empty(fromText.ReceiverInterfaces);
        Assert.NotEmpty(fromBinder.ReceiverInterfaces);

        Assert.Equal(fromText, fromBinder);
        Assert.Equal(fromText.GetHashCode(), fromBinder.GetHashCode());
    }

    /// <summary>
    /// <c>HasKnownParameterTypes</c> gates step 2a. The <c>"?"</c> sentinel and
    /// blanks are what "unknown" means here — the same test the pre-slice path
    /// applied via <c>IsKnownParameterType</c>.
    /// </summary>
    [Theory]
    [InlineData(new[] { "i32" }, true)]
    [InlineData(new[] { "i32", "str" }, true)]
    [InlineData(new[] { "?" }, false)]
    [InlineData(new[] { "i32", "?" }, false)]
    [InlineData(new[] { " " }, false)]
    public void HasKnownParameterTypes_RejectsTheUnknownSentinel(string[] parameters, bool expected)
    {
        var key = EffectResolverKey.FromStrings("Vendor.Widget", "Transform", parameters);
        Assert.Equal(expected, key.HasKnownParameterTypes);
    }

    /// <summary>
    /// A key with no parameter list has nothing to gate step 2a on, so the
    /// property is false rather than vacuously true — otherwise the name-only
    /// probe would be eligible for the signature lookup.
    /// </summary>
    [Fact]
    public void KeyWithoutAParameterList_HasNoKnownParameterTypes()
    {
        var key = EffectResolverKey.FromStrings("Vendor.Widget", "Transform", ["i32"])
            .WithoutParameterList();

        Assert.False(key.HasKnownParameterTypes);
    }
}
