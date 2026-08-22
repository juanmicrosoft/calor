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
    public void BoundStructuralExpression_String_WithNotAnnotated_Param_Yields_NotAnnotated()
    {
        // The ctor takes an opt-in typeAnnotation param (per-call-site
        // control, no ctor-level typeName sniffing). Sites that know
        // their operation is provably non-null pass NotAnnotated
        // explicitly (BindStringOperation, BindStringBuilderOperation).
        var s = new BoundStringLiteral(default, "hello");
        var op = new BoundStructuralExpression(
            default,
            nodeTypeName: "StringOperationNode",
            typeName: "STRING",
            children: [s],
            typeAnnotation: NullableAnnotation.NotAnnotated);
        var nominal = Assert.IsType<NominalBoundType>(op.Type);
        Assert.Equal("STRING", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundStructuralExpression_String_DefaultAnnotation_Stays_Oblivious()
    {
        // Regression guard: the ctor MUST default to Oblivious rather
        // than sniff typeName == "STRING". Sites like BindNullCoalesce
        // and BindAwaitExpression that produce STRING results without
        // guaranteed non-null semantics rely on this — over-eager
        // NotAnnotated stamping would silently suppress real Calor0272
        // diagnostics (false negatives).
        var s = new BoundStringLiteral(default, "hello");
        var op = new BoundStructuralExpression(
            default,
            nodeTypeName: "SomeMaybeNullNode",
            typeName: "STRING",
            children: [s]);
        var nominal = Assert.IsType<NominalBoundType>(op.Type);
        Assert.Equal("STRING", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.Oblivious, nominal.NullableAnnotation);
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
    public void BoundConditionalExpression_BothBranches_Annotated_Yields_Annotated_String()
    {
        // Both branches nullable — the ternary result is definitely
        // possibly-null, and the propagated Annotated preserves that
        // precision for Calor0272's source-annotation message.
        var cond = new BoundBoolLiteral(default, true);
        var whenTrue = new NullabilityTestExpr(
            new NominalBoundType("STRING", NullableAnnotation.Annotated));
        var whenFalse = new NullabilityTestExpr(
            new NominalBoundType("STRING", NullableAnnotation.Annotated));
        var ternary = new BoundConditionalExpression(
            default, cond, whenTrue, whenFalse, "STRING");
        var nominal = Assert.IsType<NominalBoundType>(ternary.Type);
        Assert.Equal(NullableAnnotation.Annotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundConditionalExpression_AnnotatedAndOblivious_Yields_Annotated_String()
    {
        // Annotated + Oblivious: the "either Annotated → Annotated"
        // short-circuit wins over the "both NotAnnotated → NotAnnotated"
        // check. Confirms the table's ordering, since Oblivious alone
        // would drop to Oblivious.
        var cond = new BoundBoolLiteral(default, true);
        var whenTrue = new NullabilityTestExpr(
            new NominalBoundType("STRING", NullableAnnotation.Annotated));
        var whenFalse = new NullabilityTestExpr(
            new NominalBoundType("STRING", NullableAnnotation.Oblivious));
        var ternary = new BoundConditionalExpression(
            default, cond, whenTrue, whenFalse, "STRING");
        var nominal = Assert.IsType<NominalBoundType>(ternary.Type);
        Assert.Equal(NullableAnnotation.Annotated, nominal.NullableAnnotation);
    }

    [Fact]
    public void BoundConditionalExpression_NeverBranch_Uses_OtherBranch_Annotation()
    {
        // A NEVER-typed branch (throw / return arm) never produces a
        // value; the ternary's nullability is fully determined by the
        // other branch. Without folding NEVER out, the previously-safe
        // (? cond (throw ...) STR:"safe") would drop to Oblivious and
        // trip the same false-positive Calor0272 the slice aims to fix.
        var cond = new BoundBoolLiteral(default, true);
        var whenTrue = new NullabilityTestExpr(
            new NominalBoundType("NEVER", NullableAnnotation.Oblivious));
        var whenFalse = new BoundStringLiteral(default, "safe");
        var ternary = new BoundConditionalExpression(
            default, cond, whenTrue, whenFalse, "STRING");
        var nominal = Assert.IsType<NominalBoundType>(ternary.Type);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
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

    // ================================================================
    // Variable-annotation flow (follow-up to #1057) — reads of a
    // declared-non-null local flow NotAnnotated into downstream
    // nullability checks, unblocking cases the ternary slice could not
    // reach because BoundVariableExpression.Type defaulted to Oblivious.
    // ================================================================

    /// <summary>
    /// Declared-non-null local rebound into another <c>:string</c> target
    /// via a variable reference must NOT trip Calor0272. Prior to the
    /// variable-annotation flow slice the reference lowered to a
    /// <c>BoundVariableExpression</c> with default Oblivious annotation,
    /// which §D3 treats as possibly-null.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_When_NonNullStringVariable_BoundToNonNullString()
    {
        const string source = """
            §M{m1:VarFlow}
              §F{f1:Ok:pub} () -> void
                §B{a:string} STR:"x"
                §B{b:string} a
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// The mirror case: a <c>:?string</c> local IS possibly-null. Binding
    /// its reference into a non-nullable <c>:string</c> target must fire
    /// Calor0272, and the reported source annotation must be
    /// <c>Annotated</c> (not <c>Oblivious</c>) — that precision is the
    /// whole point of flowing the declared annotation through.
    /// </summary>
    [Fact]
    public void Calor0272_Fires_When_NullableStringVariable_BoundToNonNullString()
    {
        const string source = """
            §M{m1:NullableVar}
              §F{f1:Bad:pub} () -> void
                §B{a:?string} STR:"x"
                §B{b:string} a
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && d.Message.Contains("'b'")
            && d.Message.Contains("'Annotated'"));
    }

    /// <summary>
    /// The case the ternary slice (#1057) could not close: a ternary arm
    /// that references a declared-non-null local. Both operands are now
    /// provably non-null once the variable-annotation flow lands, so
    /// binding the result into <c>:string</c> must NOT fire Calor0272.
    /// (Was previously pinned by <c>Calor0272_StillFires_For_Ternary_VariableBranch_KnownLimitation</c>,
    /// deleted alongside this fix.)
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_Ternary_VariableBranch_NonNullString()
    {
        const string source = """
            §M{m1:TernVar}
              §F{f1:Ok:pub} () -> void
                §B{a:string} STR:"x"
                §B{r:string} (? BOOL:true a STR:"safe")
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// Direct-inspection guard: constructing a <c>BoundVariableExpression</c>
    /// for a <c>VariableSymbol</c> that declared <c>NotAnnotated</c> must
    /// yield a STRING <c>NominalBoundType</c> whose
    /// <c>NullableAnnotation</c> is also <c>NotAnnotated</c>. This is the
    /// invariant the ternary/binding regressions above depend on and is
    /// cheaper to diagnose here if the flow regresses.
    /// </summary>
    [Fact]
    public void BoundVariableExpression_Inherits_NotAnnotated_From_StringSymbol()
    {
        var symbol = new VariableSymbol(
            SymbolId.None,
            name: "a",
            typeName: "STRING",
            isMutable: false,
            nullableAnnotation: NullableAnnotation.NotAnnotated);
        var reference = new BoundVariableExpression(default, symbol);
        var nominal = Assert.IsType<NominalBoundType>(reference.Type);
        Assert.Equal("STRING", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    /// <summary>
    /// Symmetric guard: a <c>:?string</c> symbol yields an Annotated
    /// STRING reference. Needed so Calor0272's source-annotation message
    /// reports <c>Annotated</c> (informative) rather than <c>Oblivious</c>
    /// (spurious).
    /// </summary>
    [Fact]
    public void BoundVariableExpression_Inherits_Annotated_From_NullableStringSymbol()
    {
        var symbol = new VariableSymbol(
            SymbolId.None,
            name: "a",
            typeName: "STRING",
            isMutable: false,
            nullableAnnotation: NullableAnnotation.Annotated);
        var reference = new BoundVariableExpression(default, symbol);
        var nominal = Assert.IsType<NominalBoundType>(reference.Type);
        Assert.Equal(NullableAnnotation.Annotated, nominal.NullableAnnotation);
    }

    /// <summary>
    /// Scope guard (§D6): flow is restricted to STRING targets in S3.
    /// A non-STRING symbol keeps the conservative Oblivious annotation on
    /// its BoundVariableExpression, even if the symbol was constructed
    /// with a non-Oblivious annotation. This preserves the S3 boundary
    /// and matches the analogous non-STRING guards on
    /// BoundBinaryExpression / BoundConditionalExpression.
    /// </summary>
    [Fact]
    public void BoundVariableExpression_NonString_Type_Stays_Oblivious()
    {
        var symbol = new VariableSymbol(
            SymbolId.None,
            name: "n",
            typeName: "INT",
            isMutable: false,
            nullableAnnotation: NullableAnnotation.NotAnnotated);
        var reference = new BoundVariableExpression(default, symbol);
        var nominal = Assert.IsType<NominalBoundType>(reference.Type);
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

    // ================================================================
    // v0.14 §S4 return-site tests (Calor0273 NullableReturnFromNonNullable).
    // Return-site sibling of the §S3b bind-time Calor0272 check. Reuses
    // the same NullabilityChecker.IsPossiblyNullAssignedTo predicate but
    // fires from BindReturnStatement against the current function's
    // declared return type. Scope (per D6): scalar STRING only.
    // ================================================================

    /// <summary>
    /// A function declared <c>-> string</c> that returns
    /// <c>Environment.GetEnvironmentVariable</c>'s Annotated string must
    /// emit Calor0273 at Info severity. This is the return-site analogue
    /// of the S3b canonical D3 repro on <c>§B{x:string}</c>.
    /// </summary>
    [Fact]
    public void Calor0273_FiresFor_ReturningBcl_NullableString_FromNonNullReturn()
    {
        const string source = """
            §M{m1:S4Repro}
              §F{f1:GetEnv:pub} () -> string
                §E{env}
                §R §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable
            && d.Severity == DiagnosticSeverity.Info);
    }

    /// <summary>
    /// Change the return type to <c>-> ?string</c> — the same
    /// possibly-null return source is now safe, and Calor0273 must NOT
    /// fire. This mirrors the S3b <c>:?string</c> target test.
    /// </summary>
    [Fact]
    public void Calor0273_DoesNotFire_When_ReturnType_IsNullableString()
    {
        const string source = """
            §M{m1:S4Ok}
              §F{f1:GetEnv:pub} () -> ?string
                §E{env}
                §R §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    /// <summary>
    /// A function declared <c>-> string</c> returning a Calor-native
    /// string literal (<c>STR:"hello"</c>) is provably non-null; the
    /// literal's BoundType is NotAnnotated. Calor0273 must NOT fire —
    /// mirrors the equivalent §B{x:string} STR:"..." case from #1055.
    /// </summary>
    [Fact]
    public void Calor0273_DoesNotFire_For_ProvablyNonNull_StringLiteral()
    {
        const string source = """
            §M{m1:S4Literal}
              §F{f1:Hi:pub} () -> string
                §R STR:"hello"
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    /// <summary>
    /// Non-STRING return types are out of scope for S3 (per D6).
    /// A function <c>-> int</c> returning <c>INT:42</c> must NOT fire
    /// Calor0273 regardless of source annotation. Regression guard
    /// against scope creep.
    /// </summary>
    [Fact]
    public void Calor0273_DoesNotFire_For_NonString_Return()
    {
        const string source = """
            §M{m1:S4Int}
              §F{f1:Answer:pub} () -> int
                §R INT:42
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    /// <summary>
    /// Direct-inspection guard: the emitted diagnostic must carry the
    /// exact Calor0273 code + Info severity, mentioning the source's
    /// nullability annotation. Ratchets against future refactors that
    /// keep the count right but change the code or severity.
    /// </summary>
    [Fact]
    public void Calor0273_Diagnostic_Message_MentionsSourceAnnotation()
    {
        const string source = """
            §M{m1:S4Msg}
              §F{f1:GetEnv:pub} () -> string
                §E{env}
                §R §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        var diag = Assert.Single(diagnostics.Where(d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.Equal(DiagnosticSeverity.Info, diag.Severity);
        Assert.Contains("non-nullable 'string'", diag.Message);
        Assert.Contains("source annotation", diag.Message);
    }

    /// <summary>
    /// Entry-point coverage: return-type context is pushed at every
    /// function-binding entry point. A method inside a class is bound via
    /// <c>BindMethod</c>, NOT <c>BindFunction</c>. If the PushReturnTypeContext
    /// call is dropped from BindMethod, THIS test flips red — otherwise
    /// the earlier tests only exercise the free-function entry point.
    /// </summary>
    [Fact]
    public void Calor0273_FiresFor_ReturningBcl_NullableString_FromMethod()
    {
        const string source = """
            §M{m1:S4Method}
              §CL{c1:Bad:pub}
                §MT{f1:GetEnv:pub} () -> string
                  §E{env}
                  §R §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable
            && d.Severity == DiagnosticSeverity.Info);
    }

    // NOTE: The lambda-inherits-enclosing-context claim in the PR body
    // (BindLambdaExpression intentionally does NOT push its own return-
    // type context) is not covered by a test here — a proper repro
    // requires precise §LAM surface syntax that is easier to exercise
    // via a C#→Calor conversion test than a hand-written §M source.
    // Tracked as a follow-up.

    // ================================================================
    // Review-integration for PR #1059:
    //   M1 — inferred-type locals (§B{x} STR:"hi") now inherit the
    //        initializer's annotation.
    //   M2 — postfix nullable form (§B{x:string?}) is now recognized
    //        alongside the prefix :?string form.
    //   N1 — parameters/fields/properties don't flow annotations yet;
    //        pinned with a still-fires test so the follow-up slice
    //        has an anchor.
    // ================================================================

    /// <summary>
    /// Review finding M1: inferred-type local from a STRING literal
    /// initializer must inherit NotAnnotated, so a subsequent binding
    /// into <c>:string</c> doesn't trip Calor0272 falsely.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_InferredType_StringLocal_FromLiteral()
    {
        const string source = """
            §M{m1:InferredOk}
              §F{f1:Ok:pub} () -> void
                §B{a} STR:"hi"
                §B{b:string} a
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// Review finding M2: postfix nullable form <c>:string?</c> is a
    /// legal Calor spelling and must flow Annotated (not silently
    /// degrade to Oblivious). A subsequent binding into <c>:string</c>
    /// MUST fire Calor0272 — before the fix this was a false-negative.
    /// </summary>
    [Fact]
    public void Calor0272_Fires_For_PostfixNullableString_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:PostfixNullable}
              §F{f1:Bad:pub} () -> void
                §E{env}
                §B{a:string?} §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
                §B{b:string} a
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && d.Message.Contains("'b'"));
    }

    /// <summary>
    /// Pin for review finding N1: parameters do NOT flow annotations
    /// yet — <c>CreateParameter</c> and its callers omit the
    /// <c>nullableAnnotation</c> argument, so a parameter's Variable
    /// Symbol stays Oblivious even when declared <c>:string</c>. This
    /// test locks that behavior in: a parameter of type STRING passed
    /// into a <c>:string</c> binding STILL trips Calor0272. When the
    /// follow-up slice teaches parameter creation to flow annotations,
    /// this test will flip red — that's the signal to remove it.
    /// Analogous pin also applies to fields and properties (not tested
    /// here).
    /// </summary>
    [Fact]
    public void Calor0272_StillFires_For_ParameterReference_KnownLimitation()
    {
        const string source = """
            §M{m1:ParamPin}
              §F{f1:Bad:pub} (STRING:name) -> void
                §B{copy:string} name
            """;

        var (_, diagnostics) = BindSource(source);

        // If this Assert.Contains starts failing, the parameter-annotation
        // flow follow-up has landed. Delete this test and update the
        // PR body callout on parameters/fields/properties.
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && d.Message.Contains("'copy'"));
    }
}
