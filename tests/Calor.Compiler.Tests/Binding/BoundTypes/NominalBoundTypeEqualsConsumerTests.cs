using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.14 nullability — consumer contract for <see cref="NominalBoundType.Equals"/>.
///
/// <para>Since PR #1055 (Calor-native STRING literals default to
/// <see cref="NullableAnnotation.NotAnnotated"/>) the <c>Equals</c>
/// implementation compares the annotation field. This is correct and
/// intentional — but it creates a subtle trap: a consumer that writes
/// <c>new NominalBoundType("STRING")</c> gets the parameterless-default
/// annotation (<see cref="NullableAnnotation.Oblivious"/>) and its
/// <c>Equals</c> against the literal's emitted <c>Type</c>
/// (<see cref="NullableAnnotation.NotAnnotated"/>) silently returns
/// <c>false</c>. Symptoms surface far from the cause: a switch on
/// <c>expr.Type is NominalBoundType n &amp;&amp; n.Equals(expected)</c>
/// stops matching, and the analysis that depended on it becomes
/// permissive without any diagnostic.</para>
///
/// <para>This file pins the contract from the consumer's side: for every
/// well-known literal / expression shape that emits a <c>NominalBoundType</c>,
/// a hand-constructed <c>NominalBoundType(name)</c> using the same
/// qualified name AND the emitted annotation must equal the emitted
/// type. If a future edit changes an emitted annotation without updating
/// the expected-annotation table below, this test flips red — flagging
/// the caller that the hand-built comparand needs to be updated to
/// match.</para>
///
/// <para>Companion source-scan test <see cref="NominalBoundType_NameOnlyConstruction_DefaultsToOblivious"/>
/// pins the constructor's default so a well-meaning refactor to
/// <c>NotAnnotated</c> can't silently change equality semantics for every
/// existing name-only call site (there are ~30+ in <c>BoundNodes.cs</c>
/// alone).</para>
/// </summary>
public class NominalBoundTypeEqualsConsumerTests
{
    /// <summary>
    /// Contract table: (qualified name, expected emitted annotation).
    /// This mirrors the observable annotation on the emitted <c>Type</c>
    /// property of each well-known literal / bound-expression shape.
    /// A new emitted annotation must land here; a change to an existing
    /// one flips the corresponding sub-assertion red.
    /// </summary>
    public static TheoryData<string, NullableAnnotation, System.Func<BoundExpression>> EmittedTypeSamples =>
        new()
        {
            // v0.14 slice 1 (#1055): Calor-native string literal emits NotAnnotated.
            {
                "STRING",
                NullableAnnotation.NotAnnotated,
                () => new BoundStringLiteral(new TextSpan(0, 0, 0, 0), "hi", isMultiline: false, isUtf8: false)
            },
            // v0.14 slice 1 (#1055): UTF-8 string literal emits NotAnnotated on ReadOnlySpan<BYTE>.
            {
                "ReadOnlySpan<BYTE>",
                NullableAnnotation.NotAnnotated,
                () => new BoundStringLiteral(new TextSpan(0, 0, 0, 0), "hi", isMultiline: false, isUtf8: true)
            },
            // Interpolated string is provably non-null (#1057 follow-up).
            {
                "STRING",
                NullableAnnotation.NotAnnotated,
                () => new BoundInterpolatedStringExpression(
                    new TextSpan(0, 0, 0, 0),
                    System.Array.Empty<BoundInterpolatedStringPart>())
            },
            // Bool literal stays at Oblivious (no nullability semantics on value types today).
            {
                "BOOL",
                NullableAnnotation.Oblivious,
                () => new BoundBoolLiteral(new TextSpan(0, 0, 0, 0), true)
            },
            // Decimal literal stays at Oblivious.
            {
                "DECIMAL",
                NullableAnnotation.Oblivious,
                () => new BoundDecimalLiteral(new TextSpan(0, 0, 0, 0), 1.5m)
            },
            // Int literal stays at Oblivious.
            {
                "INT",
                NullableAnnotation.Oblivious,
                () => new BoundIntLiteral(new TextSpan(0, 0, 0, 0), 42)
            },
            // Float literal stays at Oblivious.
            {
                "FLOAT",
                NullableAnnotation.Oblivious,
                () => new BoundFloatLiteral(new TextSpan(0, 0, 0, 0), 1.5)
            },
        };

    /// <summary>
    /// For every well-known emitted type shape, a hand-constructed
    /// <c>NominalBoundType(qualifiedName, expectedAnnotation)</c> must
    /// equal the emitted <c>Type</c>. Consumers that build comparands
    /// with the wrong annotation (typically parameterless-default
    /// Oblivious against a NotAnnotated emission) will fail this test —
    /// the anti-pattern surfaces here rather than as a silent
    /// false-negative in downstream analysis.
    /// </summary>
    [Theory]
    [MemberData(nameof(EmittedTypeSamples))]
    public void HandConstructed_Matches_EmittedType_WhenAnnotationExplicit(
        string qualifiedName,
        NullableAnnotation expectedAnnotation,
        System.Func<BoundExpression> emit)
    {
        var emitted = emit().Type;
        var handBuilt = new NominalBoundType(qualifiedName, expectedAnnotation);

        Assert.Equal(handBuilt, emitted);
        Assert.Equal(handBuilt.GetHashCode(), emitted.GetHashCode());
    }

    /// <summary>
    /// Adversarial complement — for every sample whose emitted annotation
    /// is NOT the parameterless default (<see cref="NullableAnnotation.Oblivious"/>),
    /// a hand-constructed <c>new NominalBoundType(name)</c> that RELIES on
    /// the default MUST NOT equal the emitted type. This is the exact
    /// anti-pattern described in the trap: a consumer forgets to pass
    /// the annotation, compares equality, and silently mismatches. If
    /// this assertion ever flips, either <c>Equals</c> stopped considering
    /// the annotation (regression against PR #1055) or the sample's
    /// emitted annotation was quietly downgraded to Oblivious (regression
    /// against #1055/#1057/#1060).
    /// </summary>
    [Theory]
    [MemberData(nameof(EmittedTypeSamples))]
    public void HandConstructed_NameOnly_MismatchesEmittedType_WhenEmissionIsNotOblivious(
        string qualifiedName,
        NullableAnnotation expectedAnnotation,
        System.Func<BoundExpression> emit)
    {
        if (expectedAnnotation == NullableAnnotation.Oblivious)
        {
            // Sample is default-annotation; name-only construction MUST match.
            // This half of the theory table validates the default is
            // still Oblivious — the source-scan-style test below pins it
            // more directly, but re-checking here catches the case where
            // NominalBoundType's default silently flipped.
            var emittedType = emit().Type;
            Assert.Equal(new NominalBoundType(qualifiedName), emittedType);
            return;
        }

        var emitted = emit().Type;
        var handBuiltDefaultAnnotation = new NominalBoundType(qualifiedName);
        Assert.NotEqual<BoundType>(handBuiltDefaultAnnotation, emitted);
    }

    /// <summary>
    /// Source-of-truth pin: <c>NominalBoundType</c>'s parameterless
    /// annotation default is <see cref="NullableAnnotation.Oblivious"/>.
    /// Approximately three dozen call sites in <c>BoundNodes.cs</c> and
    /// elsewhere rely on this default (they construct
    /// <c>new NominalBoundType(name)</c> without passing an annotation).
    /// If this default is ever silently flipped, every one of those sites
    /// would emit with a different annotation and the
    /// <c>Equals</c>-considers-annotation contract from PR #1055 would
    /// produce a cascade of silent equality failures across the analyses.
    /// Bumping the default should be a deliberate, PR-visible change and
    /// this test forces it to be one.
    /// </summary>
    [Fact]
    public void NominalBoundType_NameOnlyConstruction_DefaultsToOblivious()
    {
        var t = new NominalBoundType("ANY_TYPE_NAME");
        Assert.Equal(NullableAnnotation.Oblivious, t.NullableAnnotation);
    }

    /// <summary>
    /// The Equals-considers-annotation contract itself. This is asserted
    /// once in <c>BoundTypeTests.NominalBoundType_Equality_ConsidersAnnotation</c>
    /// but is repeated here because the entire consumer-contract file's
    /// value depends on it — if <c>Equals</c> ever stops distinguishing
    /// Oblivious from NotAnnotated, every sub-assertion above becomes
    /// vacuous and the trap this file guards against reopens.
    /// </summary>
    [Fact]
    public void NominalBoundType_Equals_DistinguishesObliviousFromNotAnnotated()
    {
        var oblivious = new NominalBoundType("STRING", NullableAnnotation.Oblivious);
        var notAnnotated = new NominalBoundType("STRING", NullableAnnotation.NotAnnotated);
        var annotated = new NominalBoundType("STRING", NullableAnnotation.Annotated);

        Assert.NotEqual<BoundType>(oblivious, notAnnotated);
        Assert.NotEqual<BoundType>(oblivious, annotated);
        Assert.NotEqual<BoundType>(notAnnotated, annotated);

        // Self-equality still holds — regression guard against
        // over-eager equality tightening.
        Assert.Equal<BoundType>(oblivious, new NominalBoundType("STRING", NullableAnnotation.Oblivious));
        Assert.Equal<BoundType>(notAnnotated, new NominalBoundType("STRING", NullableAnnotation.NotAnnotated));
    }
}
