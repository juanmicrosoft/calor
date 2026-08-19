using System.Collections.Immutable;
using Calor.Compiler.Binding.BoundTypes;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.14 §S2 — unit tests for the six BoundType kinds.
/// Covers constructor invariants, equality, and DisplayString formatting.
/// </summary>
public class BoundTypeTests
{
    // --- PrimitiveBoundType ---

    [Fact]
    public void PrimitiveBoundType_Bare_ProducesSimpleDisplayString()
    {
        var t = new PrimitiveBoundType("INT");
        Assert.Equal("INT", t.DisplayString);
    }

    [Fact]
    public void PrimitiveBoundType_WithBits_IncludesBitsAnnotation()
    {
        var t = new PrimitiveBoundType("INT", bits: 16);
        Assert.Equal("INT[bits=16]", t.DisplayString);
    }

    [Fact]
    public void PrimitiveBoundType_WithBitsAndSigned_IncludesBothAnnotations()
    {
        var t = new PrimitiveBoundType("INT", bits: 16, signed: true);
        Assert.Equal("INT[bits=16][signed=true]", t.DisplayString);
    }

    [Fact]
    public void PrimitiveBoundType_Equality_IsStructural()
    {
        var a = new PrimitiveBoundType("INT", bits: 32, signed: true);
        var b = new PrimitiveBoundType("INT", bits: 32, signed: true);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void PrimitiveBoundType_DifferentBits_NotEqual()
    {
        Assert.NotEqual<BoundType>(
            new PrimitiveBoundType("INT", bits: 16),
            new PrimitiveBoundType("INT", bits: 32));
    }

    // --- NominalBoundType ---

    [Fact]
    public void NominalBoundType_DisplayString_MatchesQualifiedName()
    {
        var t = new NominalBoundType("System.Console");
        Assert.Equal("System.Console", t.DisplayString);
        Assert.Equal("System.Console", t.FullyQualifiedName);
    }

    [Fact]
    public void NominalBoundType_Annotated_DoesNotAppendQuestionMark_InS2()
    {
        // S2 discipline: DisplayString stays byte-identical to the existing
        // TypeName format so verifier caches don't invalidate. Annotation
        // visibility lands in S3+ once downstream consumers migrate off
        // string equality.
        var annotated = new NominalBoundType("System.String", NullableAnnotation.Annotated);
        var notAnnotated = new NominalBoundType("System.String", NullableAnnotation.NotAnnotated);
        Assert.Equal("System.String", annotated.DisplayString);
        Assert.Equal("System.String", notAnnotated.DisplayString);
    }

    [Fact]
    public void NominalBoundType_Equality_ConsidersAnnotation()
    {
        var annotated = new NominalBoundType("System.String", NullableAnnotation.Annotated);
        var oblivious = new NominalBoundType("System.String", NullableAnnotation.Oblivious);
        Assert.NotEqual<BoundType>(annotated, oblivious);
    }

    // --- GenericInstantiationBoundType ---

    [Fact]
    public void GenericInstantiation_SimpleArg_FormatsCorrectly()
    {
        var listOfInt = new GenericInstantiationBoundType(
            new NominalBoundType("System.Collections.Generic.List"),
            ImmutableArray.Create<BoundType>(new PrimitiveBoundType("INT")));
        Assert.Equal("System.Collections.Generic.List<INT>", listOfInt.DisplayString);
    }

    [Fact]
    public void GenericInstantiation_NestedArgs_FormatsCorrectly()
    {
        var dictType = new GenericInstantiationBoundType(
            new NominalBoundType("Dictionary"),
            ImmutableArray.Create<BoundType>(
                new NominalBoundType("STRING"),
                new GenericInstantiationBoundType(
                    new NominalBoundType("List"),
                    ImmutableArray.Create<BoundType>(new PrimitiveBoundType("INT")))));
        Assert.Equal("Dictionary<STRING, List<INT>>", dictType.DisplayString);
    }

    // --- ArrayBoundType ---

    [Fact]
    public void ArrayBoundType_Rank1_UsesSquareBrackets()
    {
        var t = new ArrayBoundType(new PrimitiveBoundType("INT"));
        Assert.Equal("INT[]", t.DisplayString);
    }

    [Fact]
    public void ArrayBoundType_MultiDim_UsesCommaSeparators()
    {
        var t = new ArrayBoundType(new PrimitiveBoundType("INT"), rank: 3);
        Assert.Equal("INT[,,]", t.DisplayString);
    }

    [Fact]
    public void ArrayBoundType_ZeroRank_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ArrayBoundType(new PrimitiveBoundType("INT"), rank: 0));
    }

    // --- FunctionBoundType ---

    [Fact]
    public void FunctionBoundType_Nullary_FormatsCorrectly()
    {
        var t = new FunctionBoundType(
            ImmutableArray<BoundType>.Empty,
            new PrimitiveBoundType("VOID"));
        Assert.Equal("() -> VOID", t.DisplayString);
    }

    [Fact]
    public void FunctionBoundType_MultiArg_FormatsCorrectly()
    {
        var t = new FunctionBoundType(
            ImmutableArray.Create<BoundType>(
                new PrimitiveBoundType("INT"),
                new PrimitiveBoundType("STRING")),
            new PrimitiveBoundType("BOOL"));
        Assert.Equal("(INT, STRING) -> BOOL", t.DisplayString);
    }

    // --- UnresolvedBoundType ---

    [Fact]
    public void UnresolvedBoundType_Reason_AppearsInDisplayString()
    {
        var t = new UnresolvedBoundType("no method group");
        Assert.Equal("<unresolved: no method group>", t.DisplayString);
        Assert.Equal("no method group", t.Reason);
    }

    [Fact]
    public void UnresolvedBoundType_Equality_MatchesOnReason()
    {
        var a = new UnresolvedBoundType("missing metadata");
        var b = new UnresolvedBoundType("missing metadata");
        Assert.Equal<BoundType>(a, b);
    }
}
