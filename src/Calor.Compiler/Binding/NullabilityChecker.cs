using Calor.Compiler.Binding.BoundTypes;

namespace Calor.Compiler.Binding;

/// <summary>
/// v0.14 nullability workstream — the single-predicate home mandated by
/// <c>docs/plans/v0.14-nullability-enforcement-scoping.md</c> §D2. Every
/// call site that emits <c>Calor0272</c> (bind), <c>Calor0273</c> (return),
/// or <c>Calor0274</c> (call) reaches into this class through
/// <see cref="IsPossiblyNullAssignedTo"/>. Keeping the predicate in one
/// place prevents drift across the three future call sites — the F-5 pin
/// (see scoping doc) asserts the invariant via architecture test.
///
/// <para><b>Scope (task #7 Phase-C):</b> S3–S5 addressed scalar <c>STRING</c>
/// targets. S6 widens to <b>array-element</b> STRING (target
/// <c>[str]</c> vs source with <c>[?str]</c> elements). Generic
/// instantiations (S7) and user-declared reference types (S8) follow.
/// Non-in-scope target shapes return <c>false</c>.</para>
///
/// <para><b>Roslyn Oblivious handling (per D3):</b> Roslyn's
/// <c>NullableAnnotation.None</c> is mapped to
/// <c>BoundTypes.NullableAnnotation.Oblivious</c> upstream at
/// <c>MetadataBinderResult.MapAnnotation</c>, and Oblivious is treated
/// as possibly-null here. This is the conservative-for-safety default.</para>
/// </summary>
internal static class NullabilityChecker
{
    /// <summary>
    /// Returns true iff a value produced by <paramref name="source"/> may
    /// be null AND the <paramref name="target"/> BoundType is a
    /// non-nullable scalar <c>STRING</c> OR an <b>array whose element
    /// type is a non-nullable STRING</b> (S6). Callers that observe true
    /// emit the appropriate <c>Calor027X</c> diagnostic at their
    /// call-site severity.
    ///
    /// <para>Returns false — no diagnostic — when:</para>
    /// <list type="bullet">
    ///   <item>The source's <c>Type.NullableAnnotation</c> is
    ///     <c>NotAnnotated</c> (declared non-null).</item>
    ///   <item>The target is not a scalar STRING or an array of STRING.</item>
    ///   <item>The target's <c>NullableAnnotation</c> is <c>Annotated</c>
    ///     (target accepts null), OR the target array's ELEMENT
    ///     annotation is <c>Annotated</c> (element may be null by design).</item>
    /// </list>
    /// </summary>
    public static bool IsPossiblyNullAssignedTo(BoundExpression source, BoundType target)
    {
        if (source is null) return false;
        if (target is null) return false;

        return target switch
        {
            NominalBoundType nominal => CheckScalarStringTarget(source, nominal),
            // S6 — array-element STRING nullability. The array container's
            // own annotation is orthogonal (we only diagnose the element
            // mismatch); a possibly-null-elements source assigned to a
            // non-null-elements array target trips the same predicate.
            ArrayBoundType array => CheckArrayStringElementTarget(source, array),
            // S7 — whitelisted generic-instantiation STRING nullability
            // (Option<T>, List<T>, IList<T>, IEnumerable<T>,
            // IReadOnlyList<T>, ICollection<T>, IReadOnlyCollection<T>).
            // Container's own annotation is orthogonal — only the
            // position-0 type argument (payload / element) mismatch
            // matters, symmetric to the S6 array shape.
            GenericInstantiationBoundType generic => CheckGenericStringArgumentTarget(source, generic),
            _ => false,
        };
    }

    private static bool CheckScalarStringTarget(BoundExpression source, NominalBoundType nominalTarget)
    {
        // Target already declared nullable — accepting null is by design.
        if (nominalTarget.NullableAnnotation == NullableAnnotation.Annotated) return false;

        // v0.14 §S8 (task #7 Phase-C) — widened from scalar STRING to also
        // accept user-declared reference types. Value types (INT/BOOL/…)
        // still fall through since they can never be null. The source-shape
        // check below matches on the nominal QualifiedName so a :Foo target
        // vs a :?Foo source (both NominalBoundType, both named "Foo") fires
        // symmetrically with the scalar STRING gate. STRING itself takes
        // the fast path because Binder.TryBuildStringTarget promises the
        // target name will canonicalize; user-refs preserve whatever the
        // declaration wrote.
        var isString = IsScalarString(nominalTarget);
        var isUserRef = !isString && IsUserReferenceType(nominalTarget);
        if (!isString && !isUserRef) return false;

        var sourceType = source.Type;
        // User-ref path: source must also be a NominalBoundType with a
        // matching QualifiedName (short-name, to bridge dotted namespace
        // variants) — otherwise we would spuriously fire on unrelated
        // assignments (e.g. Annotated Foo source assigned to non-null
        // Bar target is a type error, not a nullability error, and
        // out-of-scope for S8).
        //
        // §F-3C — S8-Oblivious widening: user-ref sources now fire on
        // BOTH Annotated AND Oblivious source annotations, matching the
        // scalar STRING D3 discipline below (only NotAnnotated is safe).
        // This was previously narrowed to Annotated-only because pure-
        // Calor callees returned Oblivious by default, tripping every
        // legitimate §B{x:Foo} someUnannotatedCall pattern in existing
        // corpora. F-3A (call-site parameter check, SHA 284470b7) and
        // F-3B (return-site annotation flow, SHA 321baf1f) thread real
        // annotations through pure-Calor call sites, so an unannotated
        // `someCall() -> Foo` now returns `NotAnnotated Foo` rather than
        // Oblivious. Oblivious now genuinely means "unknown-nullable-BCL
        // surface" for user-ref types too — exactly the case S8 was
        // designed to surface.
        if (isUserRef)
        {
            if (sourceType is not NominalBoundType sourceNominal) return false;
            if (!ShortNameEquals(sourceNominal.QualifiedName, nominalTarget.QualifiedName)) return false;
            return sourceNominal.NullableAnnotation != NullableAnnotation.NotAnnotated;
        }

        // Scalar STRING path (S3/S4/S5) — unchanged.
        var sourceAnnotation = GetAnnotation(sourceType);
        return sourceAnnotation != NullableAnnotation.NotAnnotated;
    }

    /// <summary>
    /// v0.14 §S8 predicate — a nominal type is a user-declared reference
    /// type when it is neither a built-in scalar (STRING is handled by
    /// <see cref="IsScalarString"/>) nor a value-type primitive (INT,
    /// BOOL, …, whose <see cref="Binder.TryBuildStringTarget"/> guard
    /// rejects them upstream). We approximate the classification by
    /// rejecting the well-known value-type names — anything else is
    /// treated as a reference type carrying a meaningful annotation.
    /// Kept as a mirror of <c>Binder.IsBuiltInValueTypeName</c> to keep
    /// the two gates consistent; if a name lands here that <c>Binder</c>
    /// would have rejected, the predicate silently returns false and
    /// no diagnostic fires.
    /// </summary>
    private static bool IsUserReferenceType(NominalBoundType type) => type.QualifiedName switch
    {
        "INT" or "int" or "i32" => false,
        "LONG" or "long" or "i64" => false,
        "SHORT" or "short" or "i16" => false,
        "BYTE" or "byte" or "i8" => false,
        "UINT" or "uint" or "u32" => false,
        "ULONG" or "ulong" or "u64" => false,
        "USHORT" or "ushort" or "u16" => false,
        "UBYTE" or "ubyte" or "u8" => false,
        "FLOAT" or "float" or "f32" => false,
        "DOUBLE" or "double" or "f64" => false,
        "DECIMAL" or "decimal" => false,
        "BOOL" or "bool" => false,
        "CHAR" or "char" => false,
        "VOID" or "void" => false,
        "" => false,
        _ => true,
    };

    /// <summary>
    /// S6 shape gate: target is an array whose element type is a
    /// non-nullable STRING. Source must be an array with a STRING
    /// element type carrying a possibly-null (Annotated / Oblivious)
    /// annotation on the ELEMENT. Non-array or non-string-element
    /// sources yield false — S6 does not widen the check to unrelated
    /// shapes.
    /// </summary>
    private static bool CheckArrayStringElementTarget(BoundExpression source, ArrayBoundType arrayTarget)
    {
        // Only string-element arrays participate in S6.
        if (arrayTarget.ElementType is not NominalBoundType targetElement) return false;
        if (!IsScalarString(targetElement)) return false;

        // Target element declared nullable — accepting null elements is by design.
        if (targetElement.NullableAnnotation == NullableAnnotation.Annotated) return false;

        // Source must also be an array of string element to compare.
        if (source.Type is not ArrayBoundType sourceArray) return false;
        if (sourceArray.ElementType is not NominalBoundType sourceElement) return false;
        if (!IsScalarString(sourceElement)) return false;

        // Source element must be provably non-null (NotAnnotated) to pass.
        return sourceElement.NullableAnnotation != NullableAnnotation.NotAnnotated;
    }

    /// <summary>
    /// S7 shape gate: target is a whitelisted generic instantiation whose
    /// position-0 type argument is a non-nullable STRING. The Binder
    /// (<see cref="Binder.TryParseGenericStringTarget"/>) is the only site
    /// that builds this target shape today, so the whitelist is already
    /// enforced upstream — this method only performs the source vs. target
    /// symmetry check. Non-generic sources or generic sources whose
    /// definition or payload shape don't match yield false (S7 does not
    /// widen the check to unrelated shapes).
    /// </summary>
    private static bool CheckGenericStringArgumentTarget(BoundExpression source, GenericInstantiationBoundType genericTarget)
    {
        if (genericTarget.TypeArguments.Length != 1) return false;
        if (genericTarget.TypeArguments[0] is not NominalBoundType targetInner) return false;
        if (!IsScalarString(targetInner)) return false;

        // Target payload declared nullable — accepting null payloads is by design.
        if (targetInner.NullableAnnotation == NullableAnnotation.Annotated) return false;

        // Source must be the SAME whitelisted generic definition (compared
        // by short name — the Binder builds the target with the surface
        // spelling, e.g. "List", so we match on the trailing dotted
        // segment to bridge Roslyn's "System.Collections.Generic.List").
        if (source.Type is not GenericInstantiationBoundType sourceGeneric) return false;
        if (sourceGeneric.TypeArguments.Length != 1) return false;
        if (!ShortNameEquals(sourceGeneric.Definition.QualifiedName, genericTarget.Definition.QualifiedName)) return false;
        if (sourceGeneric.TypeArguments[0] is not NominalBoundType sourceInner) return false;
        if (!IsScalarString(sourceInner)) return false;

        // Source payload must be provably non-null (NotAnnotated) to pass.
        return sourceInner.NullableAnnotation != NullableAnnotation.NotAnnotated;
    }

    private static bool ShortNameEquals(string a, string b)
    {
        static string Short(string s)
        {
            var lastDot = s.LastIndexOf('.');
            return lastDot < 0 ? s : s[(lastDot + 1)..];
        }
        return string.Equals(Short(a), Short(b), System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a nominal type is the scalar <c>STRING</c> (either
    /// canonical spelling or the surface aliases handled elsewhere).
    /// String is the only in-scope element type for S3+S6 targets.
    /// </summary>
    private static bool IsScalarString(NominalBoundType type) =>
        type.QualifiedName switch
        {
            "STRING" => true,
            "string" => true,
            "str" => true,
            "System.String" => true,
            _ => false,
        };

    /// <summary>
    /// Reads the <c>NullableAnnotation</c> from any <c>BoundType</c>
    /// subclass that carries one. Types without an explicit annotation
    /// (e.g. primitives, unresolved) return <c>NotAnnotated</c> — value
    /// types are never null.
    /// </summary>
    private static NullableAnnotation GetAnnotation(BoundType type) => type switch
    {
        NominalBoundType n => n.NullableAnnotation,
        GenericInstantiationBoundType g => g.NullableAnnotation,
        ArrayBoundType a => a.NullableAnnotation,
        _ => NullableAnnotation.NotAnnotated,
    };
}
