using Calor.Compiler.Binding.BoundTypes;

namespace Calor.Compiler.Binding;

/// <summary>Expression-local transfers; these do not narrow variables or change declared types.</summary>
internal static class ExpressionResultTypes
{
    internal static bool IsNever(BoundType type) =>
        type is NominalBoundType { QualifiedName: "NEVER" };

    internal static BoundType Join(string resultName, IEnumerable<BoundType> alternatives)
    {
        var returning = alternatives.Where(type => !IsNever(type)).ToArray();
        if (returning.Length == 0)
            return new NominalBoundType("NEVER", NullableAnnotation.NotAnnotated);

        var annotations = returning.Select(NullabilityChecker.GetAnnotation).ToArray();
        var annotation = annotations.Contains(NullableAnnotation.Annotated)
            ? NullableAnnotation.Annotated
            : annotations.All(value => value == NullableAnnotation.NotAnnotated)
                ? NullableAnnotation.NotAnnotated
                : NullableAnnotation.Oblivious;
        return PreserveIdentity(resultName, annotation, returning);
    }

    internal static BoundType Coalesce(string resultName, BoundType left, BoundType right)
    {
        if (IsNever(left))
            return left;
        var annotation = IsNever(right)
            || NullabilityChecker.GetAnnotation(left) == NullableAnnotation.NotAnnotated
                ? NullableAnnotation.NotAnnotated
                : NullabilityChecker.GetAnnotation(right) ?? NullableAnnotation.Oblivious;
        return PreserveIdentity(resultName, annotation, [left, right]);
    }

    private static BoundType PreserveIdentity(
        string resultName, NullableAnnotation annotation, IReadOnlyList<BoundType> alternatives)
    {
        var identity = alternatives.OfType<NominalBoundType>()
            .Where(type => type.QualifiedName == resultName)
            .OrderByDescending(type => type.IsKnownReferenceType)
            .FirstOrDefault();
        return new NominalBoundType(resultName, annotation, identity?.Declaration, identity?.RoslynSymbol);
    }
}
