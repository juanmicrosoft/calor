using System.Collections.Immutable;
using Calor.Compiler.Binding.BoundTypes;

namespace Calor.Compiler.Binding;

/// <summary>Joins structural nullability without changing legacy expression type spellings.</summary>
internal static class NullabilityResultTypes
{
    internal static BoundType? Join(IEnumerable<BoundExpression> alternatives)
    {
        BoundType? result = null;
        var includesNull = false;
        foreach (var alternative in alternatives)
        {
            if (ExpressionResultTypes.IsNever(alternative.Type))
                continue;
            if (alternative is BoundNullLiteral)
            {
                includesNull = true;
                continue;
            }

            var shape = Shape(alternative);
            if (shape is null)
                return null;
            result = result is null ? shape : Merge(result, shape);
            if (result is null)
                return null;
        }
        return includesNull && result is not null
            ? WithAnnotation(result, NullableAnnotation.Annotated)
            : result;
    }

    internal static BoundType? Coalesce(BoundExpression left, BoundExpression right)
    {
        if (ExpressionResultTypes.IsNever(left.Type))
            return null;
        if (left is BoundNullLiteral)
            return Shape(right);
        var leftShape = Shape(left);
        if (leftShape is null)
            return null;
        if (NullabilityChecker.GetAnnotation(leftShape) == NullableAnnotation.NotAnnotated
            || ExpressionResultTypes.IsNever(right.Type))
            return WithAnnotation(leftShape, NullableAnnotation.NotAnnotated);

        var nonNullLeft = WithAnnotation(leftShape, NullableAnnotation.NotAnnotated);
        if (right is BoundNullLiteral)
            return WithAnnotation(leftShape, NullableAnnotation.Annotated);
        var rightShape = Shape(right);
        return rightShape is null ? null : Merge(nonNullLeft, rightShape);
    }

    private static BoundType? Shape(BoundExpression expression) =>
        (BoundType?)NullabilityChecker.GetMethodInputArrayType(expression)
        ?? expression.GenericNullabilityType
        ?? expression.Type as GenericInstantiationBoundType;

    private static BoundType? Merge(BoundType left, BoundType right)
    {
        var annotation = JoinAnnotation(
            NullabilityChecker.GetAnnotation(left), NullabilityChecker.GetAnnotation(right));
        if (left is ArrayBoundType leftArray && right is ArrayBoundType rightArray
            && leftArray.Rank == rightArray.Rank)
        {
            var element = Merge(leftArray.ElementType, rightArray.ElementType);
            return element is null ? null : new ArrayBoundType(element, leftArray.Rank, annotation)
            {
                RoslynSymbol = leftArray.RoslynSymbol ?? rightArray.RoslynSymbol
            };
        }
        if (left is GenericInstantiationBoundType leftGeneric
            && right is GenericInstantiationBoundType rightGeneric
            && NullabilityChecker.SameGenericDefinition(
                leftGeneric.Definition.QualifiedName, rightGeneric.Definition.QualifiedName)
            && leftGeneric.TypeArguments.Length == 1 && rightGeneric.TypeArguments.Length == 1)
        {
            var payload = Merge(leftGeneric.TypeArguments[0], rightGeneric.TypeArguments[0]);
            return payload is null ? null : new GenericInstantiationBoundType(
                leftGeneric.Definition, ImmutableArray.Create(payload), annotation);
        }
        if (left is NominalBoundType leftNominal && right is NominalBoundType rightNominal
            && (leftNominal.HasSameUnderlyingReferenceType(rightNominal)
                || !leftNominal.IsKnownReferenceType && !rightNominal.IsKnownReferenceType
                    && TypeIdentity.Canonicalize(leftNominal.QualifiedName)
                        == TypeIdentity.Canonicalize(rightNominal.QualifiedName)))
            return new NominalBoundType(leftNominal.QualifiedName, annotation,
                leftNominal.Declaration, leftNominal.RoslynSymbol ?? rightNominal.RoslynSymbol);
        return left.Equals(right) ? left : null;
    }

    private static NullableAnnotation JoinAnnotation(NullableAnnotation? left, NullableAnnotation? right) =>
        left == NullableAnnotation.Annotated || right == NullableAnnotation.Annotated
            ? NullableAnnotation.Annotated
            : left == NullableAnnotation.NotAnnotated && right == NullableAnnotation.NotAnnotated
                ? NullableAnnotation.NotAnnotated
                : NullableAnnotation.Oblivious;

    private static BoundType WithAnnotation(BoundType type, NullableAnnotation annotation) => type switch
    {
        ArrayBoundType array => new ArrayBoundType(array.ElementType, array.Rank, annotation)
        {
            RoslynSymbol = array.RoslynSymbol
        },
        GenericInstantiationBoundType generic =>
            new GenericInstantiationBoundType(generic.Definition, generic.TypeArguments, annotation),
        _ => throw new InvalidOperationException("Expected an array or generic nullability shape.")
    };
}
