using System.Collections.Immutable;
using Calor.Compiler.Binding;
using Microsoft.CodeAnalysis;

namespace Calor.Compiler.Binding.BoundTypes;

/// <summary>
/// v0.14 §D1/D2 — ground-truth type representation on bound-tree nodes,
/// introduced by the metadata-binding entry spike as the eventual replacement
/// for <see cref="BoundExpression.TypeName"/> string comparisons.
///
/// <para>In S2 (this phase) <see cref="BoundExpression.Type"/> is a virtual
/// property with a default that wraps the existing <c>TypeName</c> string in a
/// <see cref="NominalBoundType"/>. Subclasses will override in S3–S6 to expose
/// precise <c>BoundType</c> shapes derived from real metadata. The shim is
/// removed in S7 (F-5, v0.14 release exit criterion).</para>
///
/// <para>Six concrete kinds; count is exhaustive:
/// <see cref="PrimitiveBoundType"/>, <see cref="NominalBoundType"/>,
/// <see cref="GenericInstantiationBoundType"/>, <see cref="ArrayBoundType"/>,
/// <see cref="FunctionBoundType"/>, <see cref="UnresolvedBoundType"/>.
/// No inference variables — all types ground.</para>
///
/// <para>Anti-pattern registered: parsing <c>DisplayString</c> back to a
/// <c>BoundType</c> is prohibited. Structural information lives on the
/// symbol; the string is a leaf artifact for diagnostics and the shim.
/// (Enforced by <c>ArchitectureTests.BoundType_HasNo_ParseFromString_Method</c>.)</para>
/// </summary>
public abstract class BoundType : IEquatable<BoundType>
{
    /// <summary>
    /// Stable display string used by the <see cref="BoundExpression.TypeName"/>
    /// shim and by diagnostics. Must be byte-identical across independent
    /// constructions of the same logical type — verifier caches key on it.
    /// </summary>
    public abstract string DisplayString { get; }

    /// <summary>
    /// Fully qualified name where meaningful (nominal, generic instantiation).
    /// Falls back to <see cref="DisplayString"/> for shapes without a QN.
    /// </summary>
    public virtual string FullyQualifiedName => DisplayString;

    public abstract bool Equals(BoundType? other);
    public override bool Equals(object? obj) => obj is BoundType t && Equals(t);
    public abstract override int GetHashCode();
    public override string ToString() => DisplayString;
}

/// <summary>Nullable annotation on a reference-typed <see cref="BoundType"/>.
/// <see cref="Oblivious"/> is first-class and never silently upgraded to
/// <see cref="NotAnnotated"/> — §D6.</summary>
public enum NullableAnnotation
{
    Annotated,
    NotAnnotated,
    Oblivious,
}

/// <summary>Kind 1: primitive value type (INT / LONG / UINT / ULONG / FLOAT /
/// BOOL / STRING / CHAR / DECIMAL / VOID). Width and signedness annotations
/// mirror the existing string-suffix convention (<c>INT[bits=16][signed=true]</c>).
/// </summary>
public sealed class PrimitiveBoundType : BoundType
{
    public string Name { get; }
    public int? Bits { get; }
    public bool? Signed { get; }
    public override string DisplayString { get; }

    public PrimitiveBoundType(string name, int? bits = null, bool? signed = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Bits = bits;
        Signed = signed;
        DisplayString = (bits, signed) switch
        {
            (int b, bool s) => $"{name}[bits={b}][signed={s.ToString().ToLowerInvariant()}]",
            (int b, null) => $"{name}[bits={b}]",
            _ => name,
        };
    }

    public override bool Equals(BoundType? other) =>
        other is PrimitiveBoundType p
        && p.Name == Name
        && p.Bits == Bits
        && p.Signed == Signed;

    public override int GetHashCode() => HashCode.Combine(Name, Bits, Signed);
}

/// <summary>Kind 2: resolved user-defined or .NET type. Carries a
/// <see cref="NullableAnnotation"/> and either a Calor
/// <see cref="TypeSymbol"/> back-reference (user-declared) or a Roslyn
/// <see cref="INamedTypeSymbol"/> back-reference (BCL). At S2 this kind is
/// used by <see cref="BoundExpression.Type"/>'s default shim to wrap the
/// existing string <c>TypeName</c>; back-references are populated in later
/// phases.</summary>
public sealed class NominalBoundType : BoundType
{
    public string QualifiedName { get; }
    public NullableAnnotation NullableAnnotation { get; }
    public TypeSymbol? Declaration { get; }
    public INamedTypeSymbol? RoslynSymbol { get; }
    public override string DisplayString { get; }
    public override string FullyQualifiedName => QualifiedName;

    public NominalBoundType(
        string qualifiedName,
        NullableAnnotation nullableAnnotation = NullableAnnotation.Oblivious,
        TypeSymbol? declaration = null,
        INamedTypeSymbol? roslynSymbol = null)
    {
        QualifiedName = qualifiedName ?? throw new ArgumentNullException(nameof(qualifiedName));
        NullableAnnotation = nullableAnnotation;
        Declaration = declaration;
        RoslynSymbol = roslynSymbol;
        // Deliberately NOT appending '?' for Annotated in S2 — the shim must
        // preserve byte-identical string equivalence with the existing
        // TypeName format. Nullable annotations become visible in S3+ once
        // downstream consumers migrate off string equality.
        DisplayString = qualifiedName;
    }

    public override bool Equals(BoundType? other) =>
        other is NominalBoundType n
        && n.QualifiedName == QualifiedName
        && n.NullableAnnotation == NullableAnnotation;

    public override int GetHashCode() => HashCode.Combine(QualifiedName, NullableAnnotation);
}

/// <summary>Kind 3: generic instantiation (<c>List&lt;int&gt;</c>,
/// <c>Dictionary&lt;string, List&lt;int&gt;&gt;</c>). Type arguments carry
/// their own <see cref="NullableAnnotation"/>s — Shape 14 in F-1.</summary>
public sealed class GenericInstantiationBoundType : BoundType
{
    public NominalBoundType Definition { get; }
    public ImmutableArray<BoundType> TypeArguments { get; }
    public NullableAnnotation NullableAnnotation { get; }
    public override string DisplayString { get; }

    public GenericInstantiationBoundType(
        NominalBoundType definition,
        ImmutableArray<BoundType> typeArguments,
        NullableAnnotation nullableAnnotation = NullableAnnotation.Oblivious)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        // default(ImmutableArray) has IsDefault=true; enumerating throws NRE.
        // Normalize to Empty so downstream code (Select, SequenceEqual) is safe.
        if (typeArguments.IsDefault) typeArguments = ImmutableArray<BoundType>.Empty;
        Definition = definition;
        TypeArguments = typeArguments;
        NullableAnnotation = nullableAnnotation;
        var args = string.Join(", ", typeArguments.Select(t => t.DisplayString));
        DisplayString = $"{definition.QualifiedName}<{args}>";
    }

    public override bool Equals(BoundType? other) =>
        other is GenericInstantiationBoundType g
        && g.Definition.Equals(Definition)
        && g.TypeArguments.SequenceEqual(TypeArguments)
        && g.NullableAnnotation == NullableAnnotation;

    public override int GetHashCode()
    {
        var h = HashCode.Combine(Definition, NullableAnnotation);
        foreach (var arg in TypeArguments)
        {
            h = HashCode.Combine(h, arg);
        }
        return h;
    }
}

/// <summary>Kind 4: array (single-rank or multi-dim). Rank is 1-based.</summary>
public sealed class ArrayBoundType : BoundType
{
    public BoundType ElementType { get; }
    public int Rank { get; }
    public NullableAnnotation NullableAnnotation { get; }
    public override string DisplayString { get; }

    public ArrayBoundType(
        BoundType elementType,
        int rank = 1,
        NullableAnnotation nullableAnnotation = NullableAnnotation.Oblivious)
    {
        if (elementType is null) throw new ArgumentNullException(nameof(elementType));
        if (rank < 1) throw new ArgumentOutOfRangeException(nameof(rank), "Rank must be ≥ 1.");
        ElementType = elementType;
        Rank = rank;
        NullableAnnotation = nullableAnnotation;
        var commas = new string(',', rank - 1);
        DisplayString = $"{elementType.DisplayString}[{commas}]";
    }

    public override bool Equals(BoundType? other) =>
        other is ArrayBoundType a
        && a.ElementType.Equals(ElementType)
        && a.Rank == Rank
        && a.NullableAnnotation == NullableAnnotation;

    public override int GetHashCode() =>
        HashCode.Combine(ElementType, Rank, NullableAnnotation);
}

/// <summary>Kind 5: function type (for lambdas / delegates). Effect rows
/// attach here in 0.15 (§4.2); the kind ships in 0.14 so downstream analyses
/// have the shape ready.</summary>
public sealed class FunctionBoundType : BoundType
{
    public ImmutableArray<BoundType> ParameterTypes { get; }
    public BoundType ReturnType { get; }
    public override string DisplayString { get; }

    public FunctionBoundType(ImmutableArray<BoundType> parameterTypes, BoundType returnType)
    {
        if (returnType is null) throw new ArgumentNullException(nameof(returnType));
        if (parameterTypes.IsDefault) parameterTypes = ImmutableArray<BoundType>.Empty;
        ParameterTypes = parameterTypes;
        ReturnType = returnType;
        var parms = string.Join(", ", parameterTypes.Select(p => p.DisplayString));
        DisplayString = $"({parms}) -> {returnType.DisplayString}";
    }

    public override bool Equals(BoundType? other) =>
        other is FunctionBoundType f
        && f.ParameterTypes.SequenceEqual(ParameterTypes)
        && f.ReturnType.Equals(ReturnType);

    public override int GetHashCode()
    {
        var h = ReturnType.GetHashCode();
        foreach (var p in ParameterTypes)
        {
            h = HashCode.Combine(h, p);
        }
        return h;
    }
}

/// <summary>Kind 6: explicit exit ramp (§D6). Emitted when metadata lookup or
/// overload resolution fails. Carries the reason so callers can emit
/// Calor0270 / Calor0271 with helpful context; downstream analyses treat
/// unresolved as "do not claim".</summary>
public sealed class UnresolvedBoundType : BoundType
{
    public string Reason { get; }
    public override string DisplayString { get; }

    public UnresolvedBoundType(string reason)
    {
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        DisplayString = $"<unresolved: {reason}>";
    }

    public override bool Equals(BoundType? other) =>
        other is UnresolvedBoundType u && u.Reason == Reason;

    public override int GetHashCode() => Reason.GetHashCode(StringComparison.Ordinal);
}
