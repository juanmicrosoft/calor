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

    /// <summary>
    /// v0.15 E2 slice b, design-doc §8.2 — the callee's OWN effect row: the
    /// effects a value of this function type may perform when invoked.
    ///
    /// <para>Defaults to <see cref="EffectRow.Unknown"/>, <b>never</b> to pure.
    /// Defaulting to <c>Concrete(∅)</c> would re-open exactly the laundering
    /// hole rows exist to close: a row-less function-typed parameter would claim
    /// to be provably pure when in fact nothing is known about it.</para>
    ///
    /// <para>Part of <see cref="Equals"/> and <see cref="GetHashCode"/> — two
    /// function types differing only in row are DIFFERENT types, which is the
    /// central claim of the effect-row work. Deliberately absent from
    /// <see cref="DisplayString"/> (§8.3): consumers compare display strings
    /// byte-for-byte, and the verifier cache and the LSP call-graph key are
    /// built from them.</para>
    /// </summary>
    public EffectRow Row { get; }

    /// <summary>
    /// v0.15 E2 slice b, design-doc §8.2/§4.6 — the row of each parameter, in
    /// declaration order, for parameters that are themselves function-typed.
    /// Always the same length as <see cref="ParameterTypes"/>; entries default
    /// to <see cref="EffectRow.Unknown"/>.
    ///
    /// <para>Required by §4.6's CONTRAvariance rule, which E3 implements: a
    /// destination's parameter row must fit into the source's, because a
    /// destination that promises to supply a printing callback is not satisfied
    /// by a source that accepts only pure ones.</para>
    /// </summary>
    public ImmutableArray<EffectRow> ParameterRows { get; }

    /// <param name="displayOverride">v0.15 E1 slice 2b. Lambdas bind to this kind
    /// now, and their historical <c>LAMBDA(str)-&gt;i32</c> spelling is
    /// load-bearing: <c>Binder.BindStatement</c> infers an untyped <c>§B</c>'s
    /// <c>TypeName</c> from the initializer's <see cref="DisplayString"/>
    /// (<c>Binder.cs:1320</c>), so the lambda's string becomes a
    /// <see cref="BoundVariableExpression"/>'s type string, the verifier cache
    /// key, and the LSP call-graph key. Changing it would break
    /// <c>DisplayString</c> byte-identity for expressions that are not lambdas,
    /// which is exactly what the corpus golden pins. So the KIND changes and the
    /// STRING does not; §8.3's canonical <c>(p1, p2) -&gt; ret</c> stays the
    /// default for every other construction. Note <see cref="Equals"/> compares
    /// shape only — two function types with the same parameters and return type
    /// are equal whatever they display as. E2 decides whether to unify the
    /// spelling when rows land.</param>
    /// <param name="row">The function type's own effect row (§8.2). Omitted means
    /// <see cref="EffectRow.Unknown"/> — "nothing is known" — never pure.</param>
    /// <param name="parameterRows">Per-parameter rows (§4.6). Omitted, short or
    /// <c>default</c> is padded with <see cref="EffectRow.Unknown"/>; a longer
    /// list is truncated, so <see cref="ParameterRows"/> and
    /// <see cref="ParameterTypes"/> always agree in length.</param>
    public FunctionBoundType(
        ImmutableArray<BoundType> parameterTypes,
        BoundType returnType,
        string? displayOverride = null,
        EffectRow? row = null,
        ImmutableArray<EffectRow> parameterRows = default)
    {
        if (returnType is null) throw new ArgumentNullException(nameof(returnType));
        if (parameterTypes.IsDefault) parameterTypes = ImmutableArray<BoundType>.Empty;
        ParameterTypes = parameterTypes;
        ReturnType = returnType;
        Row = row ?? EffectRow.Unknown;
        ParameterRows = AlignRows(parameterRows, parameterTypes.Length);
        var parms = string.Join(", ", parameterTypes.Select(p => p.DisplayString));
        // §8.3 — the row is NOT part of the display string, by decision.
        DisplayString = displayOverride ?? $"({parms}) -> {returnType.DisplayString}";
    }

    private static ImmutableArray<EffectRow> AlignRows(ImmutableArray<EffectRow> rows, int arity)
    {
        if (arity == 0) return ImmutableArray<EffectRow>.Empty;
        var builder = ImmutableArray.CreateBuilder<EffectRow>(arity);
        for (var index = 0; index < arity; index++)
        {
            builder.Add(!rows.IsDefault && index < rows.Length && rows[index] is { } row
                ? row
                : EffectRow.Unknown);
        }
        return builder.MoveToImmutable();
    }

    public override bool Equals(BoundType? other) =>
        other is FunctionBoundType f
        && f.ParameterTypes.SequenceEqual(ParameterTypes)
        && f.ReturnType.Equals(ReturnType)
        // §8.2 — rows are part of type IDENTITY. Note this is equality, not
        // assignability: EffectRow.Fits is the separate, three-valued relation.
        && f.Row.Equals(Row)
        && f.ParameterRows.SequenceEqual(ParameterRows);

    public override int GetHashCode()
    {
        var h = ReturnType.GetHashCode();
        foreach (var p in ParameterTypes)
        {
            h = HashCode.Combine(h, p);
        }
        h = HashCode.Combine(h, Row);
        foreach (var r in ParameterRows)
        {
            h = HashCode.Combine(h, r);
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

    /// <summary>
    /// v0.15 E1 slice 2b — true when the binder REPORTED this unresolvedness to
    /// the author (Calor0270). PR #1095 already split marking from reporting:
    /// every unresolved receiver is marked, but only the shapes an author can
    /// act on — an inferred local with no explicit type, a type string that
    /// cannot be canonicalized — are reported. Member chains and
    /// converter-synthesized <c>_chainNNN</c> temporaries are marked silently,
    /// because they are binder LIMITATIONS rather than facts about the program.
    ///
    /// <para>Consumers that want to fail closed on "the binder looked and could
    /// not name this" must key on this flag, not on the type alone. Measured:
    /// treating every <c>UnresolvedBoundType</c> as authoritative and
    /// suppressing the effect pass's AST fallback deletes resolution the
    /// fallback still performs — <c>tests/Calor.Conversion.Tests/Snapshots/05-02
    /// .approved.calr</c> and <c>05-03</c> go from clean to Calor0411 +
    /// Calor0410 on <c>_chainWhere005.ToList</c>.</para>
    ///
    /// <para>Deliberately NOT part of <see cref="Equals"/>: two unresolved types
    /// with the same reason are the same type whether or not one of them was
    /// also reported. Reporting is a diagnostic decision, not type identity.</para>
    /// </summary>
    public bool Reported { get; }

    public UnresolvedBoundType(string reason, bool reported = false)
    {
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        Reported = reported;
        DisplayString = $"<unresolved: {reason}>";
    }

    public override bool Equals(BoundType? other) =>
        other is UnresolvedBoundType u && u.Reason == Reason;

    public override int GetHashCode() => Reason.GetHashCode(StringComparison.Ordinal);
}

/// <summary>
/// The three-valued verdict of <see cref="EffectRow.Fits"/> — design-doc
/// §4.3. Deliberately NOT a bool: "we cannot tell" is a distinct answer from
/// "no", and collapsing them is what let effects launder through function-typed
/// values in 0.14.
/// </summary>
public enum EffectFit
{
    /// <summary>The source row is admissible at the destination.</summary>
    Fits,

    /// <summary>The source row is provably wider than the destination's. Calor0424 in E3; never waived.</summary>
    DoesNotFit,

    /// <summary>At least one side is <see cref="EffectRow.Unknown"/>. Calor0425 in E3; waived by <c>--permissive-effects</c>.</summary>
    CannotTell,
}

/// <summary>The three shapes a row can take — design-doc §4.</summary>
public enum EffectRowKind
{
    /// <summary>A known, complete effect set.</summary>
    Concrete,

    /// <summary>A known set that rests on one or more assumptions, each named by a reason.</summary>
    Assumed,

    /// <summary>No information. Top of the lattice; fits nothing and is fitted by nothing.</summary>
    Unknown,
}

/// <summary>
/// v0.15 E2 slice b — the effect-row lattice of design-doc §4:
/// <c>Row ::= Concrete(S) | Assumed(S, R) | Unknown</c>.
///
/// <para><b>Why this type lives in <c>Binding/BoundTypes/</c> and not in
/// <c>Effects/</c>.</b> <see cref="FunctionBoundType"/> carries a row (§8.2),
/// and <c>ArchitectureTests.BindingLayer_HasNoReferenceToEffectsNamespace</c>
/// forbids <c>Binding/</c> from naming the <c>Effects</c> namespace at all —
/// the binder is upstream of effect enforcement. A row whose carrier were
/// <c>Effects.EffectSet</c> would force exactly that reference. So the carrier
/// is a canonically ordered set of <b>compact surface codes</b> (<c>"cw"</c>,
/// <c>"db:r"</c>) — the same vocabulary <c>BoundFunction.DeclaredEffects</c>
/// already uses — and the family/narrow relation (<see cref="FamilySubtypes"/>)
/// moves here with it, exactly as <c>MapShortTypeNameToFullName</c> moved to
/// <c>Binding/TypeIdentity</c> in E1 slice 2b. <c>Effects.EffectSubtyping</c>
/// now DERIVES its internal table from <see cref="FamilySubtypes"/>, so there
/// is one source of truth rather than two that can drift.</para>
///
/// <para><b>Reasons are an ordered SET, not a list.</b> Draft v1 concatenated
/// them, which made <see cref="Join"/> non-commutative and the semilattice
/// claim false. With an ordinal-sorted set, <see cref="Join"/> is associative,
/// commutative and idempotent, with identity <see cref="Pure"/> and top
/// <see cref="Unknown"/> (pinned by P9).</para>
///
/// <para><b>Not in <c>DisplayString</c>.</b> §8.3 — rows never appear in a
/// <see cref="BoundType.DisplayString"/>; <see cref="ToDisplayString"/> is the
/// separate spelling diagnostics and hover use.</para>
/// </summary>
public sealed class EffectRow : IEquatable<EffectRow>
{
    private static readonly ImmutableSortedSet<string> NoStrings =
        ImmutableSortedSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    /// <summary>
    /// Top of the lattice: no information. Absorbing under <see cref="Join"/>,
    /// and <see cref="Fits"/> answers <see cref="EffectFit.CannotTell"/> for it
    /// on EITHER side — <c>EffectSet.IsSubsetOf</c>'s "everything is a subset of
    /// unknown" is deliberately NOT carried into the row relation (§4.3).
    /// </summary>
    public static readonly EffectRow Unknown =
        new(EffectRowKind.Unknown, NoStrings, NoStrings);

    /// <summary>Identity under <see cref="Join"/>: <c>Concrete(∅)</c>, a provably pure row.</summary>
    public static readonly EffectRow Pure =
        new(EffectRowKind.Concrete, NoStrings, NoStrings);

    public EffectRowKind Kind { get; }

    /// <summary>
    /// The row's effect set, as compact surface codes, ordinal-sorted. Empty for
    /// <see cref="Unknown"/> — read <see cref="Kind"/>, not <c>Codes.Count</c>,
    /// to tell "pure" from "no information".
    /// </summary>
    public ImmutableSortedSet<string> Codes { get; }

    /// <summary>
    /// Why this row is only assumed, one sentence per reason, ordinal-sorted so
    /// <see cref="Join"/> is commutative and diagnostics are traversal-order
    /// independent. Empty unless <see cref="Kind"/> is
    /// <see cref="EffectRowKind.Assumed"/>.
    /// </summary>
    public ImmutableSortedSet<string> Reasons { get; }

    private EffectRow(
        EffectRowKind kind,
        ImmutableSortedSet<string> codes,
        ImmutableSortedSet<string> reasons)
    {
        Kind = kind;
        Codes = codes;
        Reasons = reasons;
    }

    public bool IsUnknown => Kind == EffectRowKind.Unknown;
    public bool IsAssumed => Kind == EffectRowKind.Assumed;
    public bool IsConcrete => Kind == EffectRowKind.Concrete;

    /// <summary>A row that is known exactly.</summary>
    public static EffectRow Concrete(IEnumerable<string>? codes)
    {
        var set = Normalize(codes);
        return set.IsEmpty ? Pure : new EffectRow(EffectRowKind.Concrete, set, NoStrings);
    }

    /// <summary>
    /// A row that rests on assumptions. An empty reason set is not an assumption,
    /// so it degrades to <see cref="Concrete"/> — otherwise <see cref="Join"/>'s
    /// identity law would have two distinct units.
    /// </summary>
    public static EffectRow Assumed(IEnumerable<string>? codes, IEnumerable<string>? reasons)
    {
        var reasonSet = Normalize(reasons);
        if (reasonSet.IsEmpty) return Concrete(codes);
        return new EffectRow(EffectRowKind.Assumed, Normalize(codes), reasonSet);
    }

    /// <summary>
    /// The join <c>⊔</c> of design-doc §4.2 — the INFERENCE operator. Top is
    /// <see cref="Unknown"/> (absorbing on either side); identity is
    /// <see cref="Pure"/>. Associative, commutative, idempotent (P9).
    /// </summary>
    public static EffectRow Join(EffectRow? left, EffectRow? right)
    {
        if (left is null) return right ?? Unknown;
        if (right is null) return left;
        if (left.IsUnknown || right.IsUnknown) return Unknown;

        var codes = left.Codes.Union(right.Codes);
        var reasons = left.Reasons.Union(right.Reasons);
        return reasons.IsEmpty
            ? Concrete(codes)
            : new EffectRow(EffectRowKind.Assumed, codes, reasons);
    }

    /// <summary>
    /// The three-valued CHECKING relation of design-doc §4.3, total over all
    /// nine source × destination cells. Deliberately a different relation from
    /// <see cref="Equals(EffectRow)"/>: this is assignability, that is identity.
    ///
    /// <para>Unknown on EITHER side is <see cref="EffectFit.CannotTell"/>. In
    /// particular <c>fits(Concrete(∅), Unknown)</c> is NOT
    /// <see cref="EffectFit.Fits"/>: <c>EffectSet.IsSubsetOf</c>'s
    /// <c>if (other.IsUnknown) return true</c> is sound for a computed set
    /// against a DECLARED set (which can never be unknown, because
    /// <c>§E{unknown}</c> is unwritable) and unsound for rows, where a
    /// destination is Unknown by omission. P8 pins the whole table and names
    /// re-introducing that line as its discriminating revert.</para>
    ///
    /// <para><see cref="EffectRowKind.Assumed"/> fits exactly like its
    /// underlying set; the reasons it carries are what E3 reports as Calor0425,
    /// not a change of verdict. Reading them off a <see cref="EffectFit.Fits"/>
    /// verdict is <see cref="CarriedReasons"/>.</para>
    /// </summary>
    public static EffectFit Fits(EffectRow? source, EffectRow? destination)
    {
        var src = source ?? Unknown;
        var dst = destination ?? Unknown;

        if (src.IsUnknown || dst.IsUnknown)
            return EffectFit.CannotTell;

        return IsSubsetWithSubtyping(src.Codes, dst.Codes)
            ? EffectFit.Fits
            : EffectFit.DoesNotFit;
    }

    /// <summary>
    /// The reasons a hop must carry, from whichever side the assumption came —
    /// §4.3's "Assumed … always propagates 0425". Empty when neither side is
    /// assumed.
    /// </summary>
    public static ImmutableSortedSet<string> CarriedReasons(EffectRow? source, EffectRow? destination)
        => (source?.Reasons ?? NoStrings).Union(destination?.Reasons ?? NoStrings);

    /// <summary>
    /// Design-doc §4.4 — the row a value HAS once it has arrived at a
    /// destination it fits. An <see cref="EffectRowKind.Assumed"/> source
    /// produces an <see cref="EffectRowKind.Assumed"/> destination, so the
    /// assumption cannot be laundered away by one more hop. Pinned by P10(a).
    /// </summary>
    public static EffectRow AtDestination(EffectRow? source, EffectRow? destination)
    {
        var dst = destination ?? Unknown;
        if (dst.IsUnknown) return Unknown;

        var reasons = CarriedReasons(source, destination);
        return reasons.IsEmpty ? Concrete(dst.Codes) : Assumed(dst.Codes, reasons);
    }

    /// <summary>
    /// Design-doc §5 — the DECLARATION boundary, which is deliberately NOT one
    /// of §6's six binding sites. When an author writes <c>§E{…}</c> on a
    /// function or a lambda, the type that leaves the declaration is
    /// <c>Concrete(declared)</c> even if the body's inferred row was
    /// <see cref="EffectRowKind.Assumed"/>: Calor0419 already surfaces the
    /// assumption AT the declaration, so the provenance is reported there rather
    /// than carried past it. Pinned by P10(c).
    /// </summary>
    public static EffectRow AtDeclarationBoundary(EffectRow? declared)
    {
        var row = declared ?? Unknown;
        return row.IsUnknown ? Unknown : Concrete(row.Codes);
    }

    /// <summary>
    /// The family/narrow table of design-doc §4.1, over compact surface codes,
    /// and the single source of truth for effect subtyping —
    /// <c>Effects.EffectSubtyping</c> derives its internal <c>(kind, value)</c>
    /// table from this one.
    ///
    /// <para>0.15 WIDENS it: a bare family code (<c>db</c>, <c>net</c>,
    /// <c>env</c>) now encompasses its narrow siblings, which 0.14 did not.
    /// Under rows that gap would surface at every binding site instead of only
    /// at a declaration. <c>filesystem</c> has no bare code, so <c>fs:rw</c>
    /// stays the filesystem top; <c>proc</c> and <c>http</c> have no narrow
    /// siblings. Widening only — nothing that compiled stops compiling.</para>
    ///
    /// <para>Order matters for <c>EffectSubtyping.GetBroadestEncompassing</c>,
    /// which returns the FIRST broad code covering a narrow one: the
    /// <c>:rw</c> rows are listed first so its answers stay byte-identical to
    /// 0.14's.</para>
    /// </summary>
    public static readonly IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> FamilySubtypes =
    [
        new("fs:rw", new[] { "fs:r", "fs:w" }),
        new("net:rw", new[] { "net:r", "net:w" }),
        new("db:rw", new[] { "db:r", "db:w" }),
        new("env:rw", new[] { "env:r", "env:w" }),
        // 0.15 §4.1 — the bare family codes.
        new("net", new[] { "net:r", "net:w", "net:rw" }),
        new("db", new[] { "db:r", "db:w", "db:rw" }),
        new("env", new[] { "env:r", "env:w", "env:rw" }),
    ];

    private static readonly Dictionary<string, HashSet<string>> FamilyIndex =
        FamilySubtypes.ToDictionary(
            entry => entry.Key,
            entry => new HashSet<string>(entry.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>
    /// True when the compact code <paramref name="broad"/> admits
    /// <paramref name="narrow"/> — an exact match, or the family/narrow relation
    /// of <see cref="FamilySubtypes"/>. This is <c>⊆ₑ</c> (§4.3) at the element
    /// level.
    /// </summary>
    public static bool Encompasses(string broad, string narrow)
    {
        if (string.Equals(broad, narrow, StringComparison.Ordinal)) return true;
        return FamilyIndex.TryGetValue(broad, out var narrower) && narrower.Contains(narrow);
    }

    private static bool IsSubsetWithSubtyping(
        ImmutableSortedSet<string> source,
        ImmutableSortedSet<string> destination)
    {
        foreach (var code in source)
        {
            var covered = false;
            foreach (var declared in destination)
            {
                if (Encompasses(declared, code))
                {
                    covered = true;
                    break;
                }
            }

            if (!covered) return false;
        }

        return true;
    }

    /// <summary>
    /// Extends <c>EffectSet.ToDisplayString()</c>'s <c>[unknown]</c> /
    /// <c>[pure]</c> / <c>"cw, fs:w"</c> with <c>[assumed: cw]</c> (§8.3).
    /// Never appears in a <see cref="BoundType.DisplayString"/>.
    /// </summary>
    public string ToDisplayString()
    {
        if (IsUnknown) return "[unknown]";
        if (IsAssumed) return $"[assumed: {(Codes.IsEmpty ? "pure" : string.Join(", ", Codes))}]";
        return Codes.IsEmpty ? "[pure]" : string.Join(", ", Codes);
    }

    private static ImmutableSortedSet<string> Normalize(IEnumerable<string>? values)
    {
        if (values is null) return NoStrings;
        var builder = NoStrings.ToBuilder();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                builder.Add(value);
        }
        return builder.ToImmutable();
    }

    public bool Equals(EffectRow? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && Codes.SetEquals(other.Codes)
            && Reasons.SetEquals(other.Reasons);
    }

    public override bool Equals(object? obj) => obj is EffectRow other && Equals(other);

    public override int GetHashCode()
    {
        var hash = (int)Kind;
        foreach (var code in Codes) hash = HashCode.Combine(hash, code);
        foreach (var reason in Reasons) hash = HashCode.Combine(hash, reason);
        return hash;
    }

    public override string ToString() => ToDisplayString();
}
