using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for <see cref="NullabilityChecker.IsPossiblyNullAssignedTo"/> —
/// the single predicate that S3's <c>Calor0272</c>, S4's <c>Calor0273</c>/
/// <c>Calor0274</c>, and future consumers all reach through. See F-5 pin in
/// <c>docs/plans/v0.14-nullability-enforcement-scoping.md</c> §4.
///
/// The tests verify decision-table coverage:
/// (a) scalar STRING target × (Annotated | Oblivious | NotAnnotated) source
/// (b) target scope (STRING accepted; non-STRING silently skipped per D6)
/// (c) target already nullable (:?string) accepts null source
/// </summary>
public class NullabilityCheckerTests
{
    private static readonly TextSpan S = new(0, 0, 0, 0);

    private static BoundExpression SourceWith(NullableAnnotation annotation) =>
        // BoundNoneLiteral is the simplest expression with a mutable-annotation
        // shape — construct by wrapping a nominal type in a fake bound
        // expression harness via BoundStringLiteral for NotAnnotated and a
        // custom fixture for others.
        new NullabilityTestFixtureExpression(new NominalBoundType("STRING", annotation));

    private static NominalBoundType TargetString(NullableAnnotation annotation) =>
        new("STRING", annotation);

    // ================================================================
    // Positive: possibly-null source into non-null string target
    // ================================================================

    [Fact]
    public void Annotated_Source_Into_NonNull_String_Target_IsPossiblyNull()
    {
        var source = SourceWith(NullableAnnotation.Annotated);
        var target = TargetString(NullableAnnotation.NotAnnotated);
        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    [Fact]
    public void Oblivious_Source_Into_NonNull_String_Target_IsPossiblyNull_PerD3()
    {
        // Roslyn Oblivious (unannotated 3rd-party surface) is treated as
        // possibly-null per docs/plans/v0.14-nullability-enforcement-scoping.md D3.
        var source = SourceWith(NullableAnnotation.Oblivious);
        var target = TargetString(NullableAnnotation.NotAnnotated);
        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    // ================================================================
    // Negative: source is provably non-null OR target accepts null
    // ================================================================

    [Fact]
    public void NotAnnotated_Source_Into_NonNull_String_Target_IsSafe()
    {
        var source = SourceWith(NullableAnnotation.NotAnnotated);
        var target = TargetString(NullableAnnotation.NotAnnotated);
        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    [Fact]
    public void Annotated_Source_Into_Nullable_String_Target_IsSafe()
    {
        // Target is declared :?string — it accepts null by design.
        var source = SourceWith(NullableAnnotation.Annotated);
        var target = TargetString(NullableAnnotation.Annotated);
        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    [Fact]
    public void Oblivious_Source_Into_Nullable_String_Target_IsSafe()
    {
        var source = SourceWith(NullableAnnotation.Oblivious);
        var target = TargetString(NullableAnnotation.Annotated);
        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    // ================================================================
    // Scope gate (D6): non-STRING targets silently skip
    // ================================================================

    [Fact]
    public void NonString_Target_Skips_Silently_PerD6()
    {
        var source = SourceWith(NullableAnnotation.Annotated);
        var target = new NominalBoundType("MyClass", NullableAnnotation.NotAnnotated);
        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    [Fact]
    public void ArrayOfString_Target_Skips_Silently_PerD6()
    {
        // Arrays are out of scope for S3 — deferred to D6 follow-on slice.
        var source = SourceWith(NullableAnnotation.Annotated);
        var target = new ArrayBoundType(
            new NominalBoundType("STRING", NullableAnnotation.Annotated),
            rank: 1,
            nullableAnnotation: NullableAnnotation.NotAnnotated);
        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    // ================================================================
    // Null argument handling
    // ================================================================

    [Fact]
    public void Null_Source_ReturnsFalse()
    {
        var target = TargetString(NullableAnnotation.NotAnnotated);
        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(null!, target));
    }

    [Fact]
    public void Null_Target_ReturnsFalse()
    {
        var source = SourceWith(NullableAnnotation.Annotated);
        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, null!));
    }

    // ================================================================
    // Surface-alias handling: STRING / string / str / System.String
    // ================================================================

    [Theory]
    [InlineData("STRING")]
    [InlineData("string")]
    [InlineData("str")]
    [InlineData("System.String")]
    public void All_String_Surface_Aliases_Are_In_Scope(string qualifiedName)
    {
        var source = SourceWith(NullableAnnotation.Annotated);
        var target = new NominalBoundType(qualifiedName, NullableAnnotation.NotAnnotated);
        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    // ================================================================
    // Local test fixture: BoundExpression with a mutable Type. Kept in
    // the test file (not in production) — production BoundExpression
    // subclasses set Type at construction, but tests need to vary it.
    // ================================================================
    private sealed class NullabilityTestFixtureExpression : BoundExpression
    {
        public override BoundType Type { get; }
        public NullabilityTestFixtureExpression(BoundType type) : base(S)
        {
            Type = type;
        }
    }
}
