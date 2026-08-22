using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.14 §S3b integration tests (nullability workstream / issue #875).
/// Exercise the full lex→parse→bind pipeline against Calor source that mixes
/// BCL calls and §B bindings. Verifies:
///   (a) BCL-shaped calls carry MetadataBinder-resolved NullableAnnotation
///       on their BoundCallExpression.Type
///   (b) Calor0272 fires for §B{x:string} = &lt;possibly-null-source&gt;
///   (c) Calor0272 does NOT fire for :?string targets
///   (d) Calor0272 does NOT fire for Calor-local bindings (no BCL involvement)
/// </summary>
public class NullabilityIntegrationTests
{
    private static (Calor.Compiler.Binding.BoundModule Module, DiagnosticBag Diagnostics)
        BindSource(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        var bound = new Binder(diagnostics).Bind(module);
        return (bound, diagnostics);
    }

    /// <summary>
    /// The canonical D3 repro from issue #875: binding
    /// <c>Environment.GetEnvironmentVariable</c>'s Annotated string return
    /// into a non-nullable <c>:string</c> target must emit
    /// <see cref="DiagnosticCode.NullableToNonNullableBinding"/> (Calor0272)
    /// at Info severity.
    /// </summary>
    [Fact]
    public void Calor0272_FiresFor_EnvironmentGetVariable_AssignedToNonNullString()
    {
        const string source = """
            §M{m1:D3Repro}
              §F{f1:Bad:pub} () -> void
                §E{env}
                §B{bad:string} §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && d.Severity == DiagnosticSeverity.Info);
    }

    /// <summary>
    /// Change the target to <c>:?string</c> — the same possibly-null source
    /// is now safe, and Calor0272 must NOT fire.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_When_TargetIs_NullableString()
    {
        const string source = """
            §M{m1:Nullable}
              §F{f1:Ok:pub} () -> void
                §E{env}
                §B{ok:?string} §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// Calor-native string literals are provably non-null. Binding a
    /// <c>STR:"..."</c> literal into <c>:string</c> must NOT fire Calor0272.
    /// Before the BoundStringLiteral NotAnnotated fix this tripped D3's
    /// conservative Oblivious=possibly-null rule and produced noise on
    /// ordinary Calor-only code that touched no BCL surface at all.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_CalorNative_StringLiteral_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:LocalLiteral}
              §F{f1:Ok:pub} () -> void
                §B{greeting:string} STR:"hello"
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// Interpolated strings share the same non-null guarantee as string
    /// literals — binding a <c>$"..."</c>-shaped expression into
    /// <c>:string</c> must not trip Calor0272.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_InterpolatedString_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:Interp}
              §F{f1:Ok:pub} () -> void
                §B{name:string} STR:"world"
                §B{greeting:string} §INTERP "hello, " §EXP name §/INTERP
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// String concatenation is provably non-null per .NET spec
    /// (<c>String.Concat</c> substitutes an empty string for null operands).
    /// Before the BoundBinaryExpression annotation fix this was the single
    /// largest Calor0272 false-positive bucket (CONCAT=18 in the S1 corpus
    /// baseline).
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_StringConcatenation_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:Concat}
              §F{f1:Ok:pub} () -> void
                §B{a:string} STR:"hello"
                §B{b:string} STR:"world"
                §B{joined:string} (+ a b)
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// <c>nameof(x)</c> lowers to a <c>BoundStringLiteral</c> (see
    /// <c>Binder.cs</c>: NameOfExpressionNode → BoundStringLiteral), so it
    /// inherits the same <c>NotAnnotated</c> annotation as textual literals.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_NameOf_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:NameOf}
              §F{f1:Ok:pub} () -> void
                §B{a:string} STR:"hello"
                §B{name:string} (nameof a)
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    // ================================================================
    // Direct-inspection unit tests: verify the annotation itself on the
    // bound-tree node, not just the absence of a diagnostic. Guards
    // against a future refactor that keeps Calor0272 quiet for the
    // wrong reason (e.g. checker predicate change) rather than
    // preserving the "literal is non-null" invariant.
    // ================================================================

    [Fact]
    public void BoundStringLiteral_Type_Is_NotAnnotated_String()
    {
        var literal = new BoundStringLiteral(default, "hello");
        var nominal = Assert.IsType<NominalBoundType>(literal.Type);
        Assert.Equal("STRING", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundStringLiteral_Utf8_Type_Is_NotAnnotated_ReadOnlySpanBytes()
    {
        var literal = new BoundStringLiteral(default, "bytes", isMultiline: false, isUtf8: true);
        var nominal = Assert.IsType<NominalBoundType>(literal.Type);
        Assert.Equal("ReadOnlySpan<BYTE>", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundInterpolatedStringExpression_Type_Is_NotAnnotated_String()
    {
        var expression = new BoundInterpolatedStringExpression(
            default,
            new List<BoundInterpolatedStringPart>());
        var nominal = Assert.IsType<NominalBoundType>(expression.Type);
        Assert.Equal("STRING", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundBinaryExpression_StringConcat_Type_Is_NotAnnotated_String()
    {
        var left = new BoundStringLiteral(default, "a");
        var right = new BoundStringLiteral(default, "b");
        var concat = new BoundBinaryExpression(
            default, BinaryOperator.Add, left, right, "STRING");
        var nominal = Assert.IsType<NominalBoundType>(concat.Type);
        Assert.Equal("STRING", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundBinaryExpression_NonString_Type_Stays_Oblivious()
    {
        // Regression guard: the STRING→NotAnnotated propagation must not
        // leak to arithmetic ops. Numeric binary results keep the default
        // Oblivious annotation until later slices touch them.
        var left = new BoundIntLiteral(default, 1, unsignedValue: 1, isUnsigned: false, "INT");
        var right = new BoundIntLiteral(default, 2, unsignedValue: 2, isUnsigned: false, "INT");
        var sum = new BoundBinaryExpression(
            default, BinaryOperator.Add, left, right, "INT");
        var nominal = Assert.IsType<NominalBoundType>(sum.Type);
        Assert.Equal("INT", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.Oblivious, nominal.NullableAnnotation);
    }
}
