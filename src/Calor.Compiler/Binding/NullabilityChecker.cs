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
/// <para><b>Scope (per D6 of the scoping doc):</b> S3 addresses scalar
/// <c>STRING</c> targets only. Non-<c>STRING</c> targets return
/// <c>false</c> — arrays, generic instantiations, and user reference
/// types land in a follow-on slice.</para>
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
    /// non-nullable scalar <c>STRING</c>. Callers that observe true emit
    /// the appropriate <c>Calor027X</c> diagnostic at their call-site
    /// severity.
    ///
    /// <para>Returns false — no diagnostic — when:</para>
    /// <list type="bullet">
    ///   <item>The source's <c>Type.NullableAnnotation</c> is
    ///     <c>NotAnnotated</c> (declared non-null).</item>
    ///   <item>The target is not a scalar <c>STRING</c>
    ///     (S3-scope restriction).</item>
    ///   <item>The target's <c>NullableAnnotation</c> is
    ///     <c>Annotated</c> (the target IS <c>?string</c>, which
    ///     accepts null).</item>
    /// </list>
    /// </summary>
    public static bool IsPossiblyNullAssignedTo(BoundExpression source, BoundType target)
    {
        if (source is null) return false;
        if (target is null) return false;

        // Scope gate (D6): only scalar STRING targets in S3.
        if (target is not NominalBoundType nominalTarget) return false;
        if (!IsScalarString(nominalTarget)) return false;

        // Target already declared nullable — accepting null is by design.
        if (nominalTarget.NullableAnnotation == NullableAnnotation.Annotated) return false;

        // Source nullability decides. Only NotAnnotated is safe.
        var sourceType = source.Type;
        var sourceAnnotation = GetAnnotation(sourceType);
        return sourceAnnotation != NullableAnnotation.NotAnnotated;
    }

    /// <summary>
    /// Whether a nominal type is the scalar <c>STRING</c> (either
    /// canonical spelling or the surface aliases handled elsewhere).
    /// String is the only in-scope target for S3.
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
