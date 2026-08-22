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

    // ================================================================
    // #1056 — same-class follow-up: BoundStructuralExpression (Substring
    // / Trim / …) and BoundConditionalExpression (ternary) STRING
    // results now also carry NotAnnotated when the sources are provably
    // non-null.
    // ================================================================

    /// <summary>
    /// String operations routed through <c>BindStringOperation</c>
    /// (Substring, Trim, ToUpper, ToLower, Replace, PadLeft/Right, …)
    /// return non-null strings per BCL contract. Binding their result
    /// into <c>:string</c> must not fire Calor0272.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_StringOperation_Trim_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:Trim}
              §F{f1:Ok:pub} () -> void
                §B{a:string} STR:"  hello  "
                §B{trimmed:string} (trim a)
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    [Fact]
    public void Calor0272_DoesNotFire_For_StringOperation_Substr_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:Substr}
              §F{f1:Ok:pub} () -> void
                §B{a:string} STR:"hello"
                §B{sub:string} (substr a INT:0 INT:3)
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// Ternary result over two provably-non-null STRING branches (both
    /// literals here) is itself non-null. Binding into <c>:string</c>
    /// must not fire Calor0272.
    ///
    /// <para>Note: a common variant — one branch being a variable
    /// reference into a declared <c>:string</c> local — currently still
    /// trips Calor0272 because <c>BoundVariableExpression.Type</c>
    /// defaults to <c>Oblivious</c>. Variable-annotation flow is a
    /// separate slice from #1056 (see PR discussion / follow-up).</para>
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_Ternary_BothBranches_NotAnnotated_String()
    {
        const string source = """
            §M{m1:Tern}
              §F{f1:Ok:pub} () -> void
                §B{picked:string} (? BOOL:true STR:"yes" STR:"no")
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    // ================================================================
    // Direct-inspection unit tests (mirror the #1055 set).
    // ================================================================

    [Fact]
    public void BoundStructuralExpression_String_Type_Is_NotAnnotated()
    {
        var s = new BoundStringLiteral(default, "hello");
        var op = new BoundStructuralExpression(
            default,
            nodeTypeName: "StringOperationNode",
            typeName: "STRING",
            children: [s]);
        var nominal = Assert.IsType<NominalBoundType>(op.Type);
        Assert.Equal("STRING", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundStructuralExpression_NonString_Type_Stays_Oblivious()
    {
        // Regression guard: propagation must not leak to non-STRING
        // structural expressions (e.g. String.Length → INT, Contains → BOOL).
        var s = new BoundStringLiteral(default, "hello");
        var op = new BoundStructuralExpression(
            default,
            nodeTypeName: "StringOperationNode",
            typeName: "INT",
            children: [s]);
        var nominal = Assert.IsType<NominalBoundType>(op.Type);
        Assert.Equal("INT", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.Oblivious, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundConditionalExpression_BothBranches_NotAnnotated_Yields_NotAnnotated_String()
    {
        var cond = new BoundBoolLiteral(default, true);
        var whenTrue = new BoundStringLiteral(default, "a");
        var whenFalse = new BoundStringLiteral(default, "b");
        var ternary = new BoundConditionalExpression(
            default, cond, whenTrue, whenFalse, "STRING");
        var nominal = Assert.IsType<NominalBoundType>(ternary.Type);
        Assert.Equal("STRING", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundConditionalExpression_AnnotatedBranch_Yields_Annotated_String()
    {
        // Simulates §B{r:string} (? cond nullableSource "safe"):
        // the ternary result IS possibly-null. Annotating explicitly
        // preserves diagnostic precision so Calor0272's "source
        // annotation" message shows 'Annotated' rather than 'Oblivious'.
        var cond = new BoundBoolLiteral(default, true);
        var whenTrue = new NullabilityTestExpr(
            new NominalBoundType("STRING", NullableAnnotation.Annotated));
        var whenFalse = new BoundStringLiteral(default, "safe");
        var ternary = new BoundConditionalExpression(
            default, cond, whenTrue, whenFalse, "STRING");
        var nominal = Assert.IsType<NominalBoundType>(ternary.Type);
        Assert.Equal(NullableAnnotation.Annotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundConditionalExpression_ObliviousBranch_Yields_Oblivious_String()
    {
        // Conservative fallback: a NotAnnotated + Oblivious mix cannot
        // conclude non-null, so the result stays Oblivious (treated as
        // possibly-null by NullabilityChecker per D3).
        var cond = new BoundBoolLiteral(default, true);
        var whenTrue = new NullabilityTestExpr(
            new NominalBoundType("STRING", NullableAnnotation.Oblivious));
        var whenFalse = new BoundStringLiteral(default, "safe");
        var ternary = new BoundConditionalExpression(
            default, cond, whenTrue, whenFalse, "STRING");
        var nominal = Assert.IsType<NominalBoundType>(ternary.Type);
        Assert.Equal(NullableAnnotation.Oblivious, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundConditionalExpression_NonString_Type_Stays_Oblivious()
    {
        // Regression guard: propagation must not leak to non-STRING
        // conditional results.
        var cond = new BoundBoolLiteral(default, true);
        var whenTrue = new BoundIntLiteral(default, 1, 1, false, "INT");
        var whenFalse = new BoundIntLiteral(default, 2, 2, false, "INT");
        var ternary = new BoundConditionalExpression(
            default, cond, whenTrue, whenFalse, "INT");
        var nominal = Assert.IsType<NominalBoundType>(ternary.Type);
        Assert.Equal("INT", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.Oblivious, nominal.NullableAnnotation);
    }

    /// <summary>
    /// Minimal test-only BoundExpression with a caller-controlled Type,
    /// used to exercise annotation-propagation branches (Annotated,
    /// Oblivious) that no production BoundExpression currently produces
    /// as a scalar STRING.
    /// </summary>
    private sealed class NullabilityTestExpr : BoundExpression
    {
        public override BoundType Type { get; }
        public NullabilityTestExpr(BoundType type) : base(default) { Type = type; }
    }
}
