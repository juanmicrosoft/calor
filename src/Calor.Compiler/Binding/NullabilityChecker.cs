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
            _ => false,
        };
    }

    private static bool CheckScalarStringTarget(BoundExpression source, NominalBoundType nominalTarget)
    {
        if (!IsScalarString(nominalTarget)) return false;

        // Target already declared nullable — accepting null is by design.
        if (nominalTarget.NullableAnnotation == NullableAnnotation.Annotated) return false;

        // Source nullability decides. Only NotAnnotated is safe.
        var sourceType = source.Type;
        var sourceAnnotation = GetAnnotation(sourceType);
        return sourceAnnotation != NullableAnnotation.NotAnnotated;
    }

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
