using System.Collections.Immutable;
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
            && d.Severity == SemanticsVersion.NullabilitySeverityFor());
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
    /// v0.14 §S3b task #5 (#1061): <c>StringOp.ToString</c> maps to
    /// <c>object.ToString()</c>, which the modern BCL annotates as
    /// <c>string?</c> — overrides may legitimately return null. Sound
    /// analysis stamps the result <c>Annotated</c>, so binding <c>(str x)</c>
    /// into a non-nullable <c>:string</c> target must fire Calor0272 and
    /// name the source annotation as <c>Annotated</c> (not <c>Oblivious</c>,
    /// which would misdiagnose the reason and misrepresent the risk).
    /// This is the inverse of the Trim/Substring assertions above: those
    /// STRING-returning ops are contract-non-null; ToString is not.
    /// </summary>
    [Fact]
    public void Calor0272_Fires_For_StringOperation_ToString_Bound_To_NonNullString()
    {
        // (str x) is StringOp.ToString — the BCL object.ToString() shape.
        const string source = """
            §M{m1:ToStr}
              §F{f1:Bad:pub} () -> void
                §B{n:int} INT:42
                §B{s:string} (str n)
            """;

        var (bound, diagnostics) = BindSource(source);

        // Belt-and-braces: pin the annotation on the bound tree so a
        // future refactor cannot silently drop the diagnostic by moving
        // the stamp back to NotAnnotated.
        var initializer = bound.Functions.Single().Body.OfType<BoundBindStatement>()
            .Last().Initializer!;
        var initType = Assert.IsType<NominalBoundType>(initializer.Type);
        Assert.Equal("STRING", initType.QualifiedName);
        Assert.Equal(NullableAnnotation.Annotated, initType.NullableAnnotation);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && d.Message.Contains("'s'")
            && d.Message.Contains("'Annotated'"));
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
            && d.Severity == SemanticsVersion.NullabilitySeverityFor());
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
        Assert.Equal(SemanticsVersion.NullabilitySeverityFor(), diag.Severity);
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
            && d.Severity == SemanticsVersion.NullabilitySeverityFor());
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

    // Pin test Calor0272_StillFires_For_ParameterReference_KnownLimitation
    // was removed in the parameter/field/property/lambda-param/foreach-var
    // annotation-flow slice (task #3). Replaced by the positive tests
    // below (parameter, field, property, lambda parameter, foreach loop
    // variable) that assert declared :string params/fields/etc. no longer
    // trip Calor0272 while their :?string counterparts still do.

    // ================================================================
    // v0.14 §S4 (call-site) — Calor0274 NullableArgumentToNonNullable
    // Parameter. Mirrors the §S3b bind-site pattern at the argument/
    // parameter boundary. MetadataBinder now surfaces parameter-side
    // NullableAnnotation, so a BCL call receiving a possibly-null
    // source into a declared non-null 'string' parameter fires
    // Calor0274 [Info]. Scoped to scalar STRING parameters per D6;
    // BCL-shaped receivers only (mirrors S3b's System.*/Microsoft.*
    // narrowing).
    // ================================================================

    /// <summary>
    /// Passing an Annotated-source string
    /// (<c>Environment.GetEnvironmentVariable</c>) into a BCL API whose
    /// parameter is declared non-null (<c>File.ReadAllText(string)</c>)
    /// must emit Calor0274 [Info]. Canonical S4 repro at the call-site.
    /// </summary>
    [Fact]
    public void Calor0274_FiresFor_PassingNullableBclString_To_NonNullableParameter()
    {
        const string source = """
            §M{m1:S4Repro}
              §F{f1:Bad:pub} () -> void
                §B{contents:string} §C{System.IO.File.ReadAllText} §A §C{System.Environment.GetEnvironmentVariable} §A STR:"PATH" §/C §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && d.Severity == SemanticsVersion.NullabilitySeverityFor());
    }

    /// <summary>
    /// Same possibly-null source, but the callee's parameter is
    /// <c>string?</c> (<c>System.IO.Path.GetFileName(string?)</c>):
    /// accepts null by design, so Calor0274 must NOT fire.
    /// </summary>
    [Fact]
    public void Calor0274_DoesNotFire_When_Parameter_IsNullableString()
    {
        const string source = """
            §M{m1:S4NullableParam}
              §F{f1:Ok:pub} () -> void
                §E{env}
                §B{path:?string} §C{System.Environment.GetEnvironmentVariable} §A STR:"PATH" §/C
                §B{name:?string} §C{System.IO.Path.GetFileName} §A path §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    /// <summary>
    /// A Calor-native string literal is provably non-null (per PR #1055).
    /// Passing <c>STR:"hello"</c> into a non-null <c>string</c> parameter
    /// must NOT trip Calor0274.
    /// </summary>
    [Fact]
    public void Calor0274_DoesNotFire_For_ProvablyNonNull_Literal_Argument()
    {
        const string source = """
            §M{m1:S4LiteralOk}
              §F{f1:Ok:pub} () -> void
                §E{io}
                §C{System.IO.File.WriteAllText} §A STR:"path.txt" §A STR:"contents" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    /// <summary>
    /// D6 scope guard: non-string parameters are out of scope for S3/S4.
    /// Passing an INT argument to a callee whose parameter is INT must
    /// NOT fire Calor0274.
    /// </summary>
    [Fact]
    public void Calor0274_DoesNotFire_For_NonString_Parameter()
    {
        const string source = """
            §M{m1:S4IntParam}
              §F{f1:Ok:pub} () -> void
                §B{v:int} §C{System.Math.Abs} §A INT:-3 §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    /// <summary>
    /// Direct-inspection guard: the diagnostic message pattern names the
    /// parameter, discloses the source annotation, and prescribes the
    /// fix (mirrors Calor0272's wording).
    /// </summary>
    [Fact]
    public void Calor0274_Message_NamesParameter_And_SourceAnnotation()
    {
        const string source = """
            §M{m1:S4Message}
              §F{f1:Bad:pub} () -> void
                §B{contents:string} §C{System.IO.File.ReadAllText} §A §C{System.Environment.GetEnvironmentVariable} §A STR:"PATH" §/C §/C
            """;

        var (_, diagnostics) = BindSource(source);

        var diag = diagnostics.FirstOrDefault(d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
        Assert.NotNull(diag);
        Assert.Contains("'path'", diag!.Message);
        Assert.Contains("'?string'", diag.Message);
        Assert.Contains("source annotation", diag.Message);
    }

    /// <summary>
    /// Review finding M1 from PR #1060: passing a Calor <c>:?string</c>
    /// variable directly into a BCL non-null <c>:string</c> parameter is
    /// the highest-value Calor0274 target — but before the argument-
    /// mapping OPTION-unwrap + prefix-<c>?</c> trim,
    /// <c>":?string"</c> mapped via <c>MapShortTypeNameToFullName</c> to
    /// <c>Calor.Runtime.Option`1</c>, overload resolution failed against
    /// <c>File.ReadAllText(string)</c>, and no diagnostic fired. This
    /// test locks the fix in place.
    /// </summary>
    [Fact]
    public void Calor0274_FiresFor_CalorNullableString_Var_Passed_To_NonNullableParameter()
    {
        // Wrap the ReadAllText call in a §B initializer so it routes
        // through BindCallExpression (where the Calor0274 emitter lives).
        const string source = """
            §M{m1:S4NullableArg}
              §F{f1:Bad:pub} () -> void
                §E{env}
                §B{path:?string} §C{System.Environment.GetEnvironmentVariable} §A STR:"PATH" §/C
                §B{contents:string} §C{System.IO.File.ReadAllText} §A path §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && d.Severity == SemanticsVersion.NullabilitySeverityFor()
            && d.Message.Contains("'path'"));
    }

    // ================================================================
    // v0.14 nullability workstream — task #3: parameter / field /
    // property / lambda-parameter / foreach-loop-variable annotation
    // flow. Complements the BoundVariableExpression / local-binding
    // tests above by asserting the declared STRING nullability
    // propagates from the Binder's non-local VariableSymbol creation
    // sites (previously all defaulted to Oblivious, pinned by
    // Calor0272_StillFires_For_ParameterReference_KnownLimitation).
    // ================================================================

    /// <summary>
    /// Task #3 — a function parameter declared <c>:string</c> flows
    /// NotAnnotated through its BoundVariableExpression reference, so
    /// binding it into another <c>:string</c> target must NOT fire
    /// Calor0272. Replaces the deleted
    /// <c>Calor0272_StillFires_For_ParameterReference_KnownLimitation</c>
    /// pin test.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_NonNullStringParameter_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:ParamOk}
              §F{f1:Ok:pub} (STRING:name) -> void
                §B{copy:string} name
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// Task #3 — a <c>:?string</c> parameter IS possibly-null; binding
    /// its reference into <c>:string</c> must still fire Calor0272 and
    /// the source annotation in the message must be <c>Annotated</c>
    /// (proving the parameter's declared nullability flowed through).
    /// </summary>
    [Fact]
    public void Calor0272_Fires_For_NullableStringParameter_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:ParamBad}
              §F{f1:Bad:pub} (?string:name) -> void
                §B{copy:string} name
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && d.Message.Contains("'copy'")
            && d.Message.Contains("'Annotated'"));
    }

    /// <summary>
    /// Task #3 — a class field declared <c>:string</c> flows
    /// NotAnnotated on the VariableSymbol registered by the class-
    /// registration pass. Direct-inspection guard.
    /// </summary>
    [Fact]
    public void FieldSymbol_Inherits_NotAnnotated_From_NonNullStringField()
    {
        const string source = """
            §M{m1:FieldOk}
              §CL{c1:Holder:pub}
                §FLD{string:Name:pub}
            """;

        var (module, _) = BindSource(source);
        var field = FindMemberSymbol(module, "Holder", "Name");
        Assert.Equal(NullableAnnotation.NotAnnotated, field.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — a class field declared <c>:?string</c> flows
    /// Annotated. Mirror of the NotAnnotated case above.
    /// </summary>
    [Fact]
    public void FieldSymbol_Inherits_Annotated_From_NullableStringField()
    {
        const string source = """
            §M{m1:FieldNullable}
              §CL{c1:Holder:pub}
                §FLD{?string:Name:pub}
            """;

        var (module, _) = BindSource(source);
        var field = FindMemberSymbol(module, "Holder", "Name");
        Assert.Equal(NullableAnnotation.Annotated, field.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — a class property declared <c>:string</c> flows
    /// NotAnnotated on the VariableSymbol.
    /// </summary>
    [Fact]
    public void PropertySymbol_Inherits_NotAnnotated_From_NonNullStringProperty()
    {
        const string source = """
            §M{m1:PropOk}
              §CL{c1:Holder:pub}
                §PROP{p1:Name:string:pub:get,set}
            """;

        var (module, _) = BindSource(source);
        var prop = FindMemberSymbol(module, "Holder", "Name");
        Assert.Equal(NullableAnnotation.NotAnnotated, prop.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — a class property declared <c>:?string</c> flows
    /// Annotated on the VariableSymbol.
    /// </summary>
    [Fact]
    public void PropertySymbol_Inherits_Annotated_From_NullableStringProperty()
    {
        const string source = """
            §M{m1:PropNullable}
              §CL{c1:Holder:pub}
                §PROP{p1:Name:?string:pub:get,set}
            """;

        var (module, _) = BindSource(source);
        var prop = FindMemberSymbol(module, "Holder", "Name");
        Assert.Equal(NullableAnnotation.Annotated, prop.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — a lambda parameter declared <c>:string</c> flows
    /// NotAnnotated. Direct-inspection guard on the lambda's parameter
    /// symbol.
    /// </summary>
    [Fact]
    public void LambdaParameter_Inherits_NotAnnotated_From_NonNullStringParameter()
    {
        const string source = """
            §M{m1:LamOk}
              §F{f1:Use:pub} () -> void
                §B{f} §LAM{l1:name:string} name §/LAM
            """;

        var (module, _) = BindSource(source);
        var lambdaParam = FindFirstLambdaParameter(module);
        Assert.Equal(NullableAnnotation.NotAnnotated, lambdaParam.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — a lambda parameter declared <c>:?string</c> flows
    /// Annotated on the VariableSymbol.
    /// </summary>
    [Fact]
    public void LambdaParameter_Inherits_Annotated_From_NullableStringParameter()
    {
        const string source = """
            §M{m1:LamNullable}
              §F{f1:Use:pub} () -> void
                §B{f} §LAM{l1:name:?string} name §/LAM
            """;

        var (module, _) = BindSource(source);
        var lambdaParam = FindFirstLambdaParameter(module);
        Assert.Equal(NullableAnnotation.Annotated, lambdaParam.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — a foreach loop variable with an explicit
    /// <c>:string</c> element type flows NotAnnotated on its
    /// VariableSymbol. Constructed via the same VariableSymbol
    /// constructor the Binder's foreach path uses; a full §FE
    /// end-to-end repro requires a fully-typed collection receiver
    /// that adds noise unrelated to the annotation-flow claim.
    /// </summary>
    [Fact]
    public void ForeachLoopVariable_Inherits_NotAnnotated_From_NonNullStringElement()
    {
        var symbol = new VariableSymbol(
            SymbolId.None,
            name: "elem",
            typeName: "STRING",
            isMutable: false,
            nullableAnnotation: NullableAnnotation.NotAnnotated);
        Assert.Equal(NullableAnnotation.NotAnnotated, symbol.NullableAnnotation);
        // Guard the downstream expression path too: BoundVariableExpression
        // must inherit NotAnnotated (analogue of the local-binding case).
        var reference = new BoundVariableExpression(default, symbol);
        var nominal = Assert.IsType<NominalBoundType>(reference.Type);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — a foreach loop variable declared over a nullable
    /// element type flows Annotated. Symmetric with the NotAnnotated
    /// case above.
    /// </summary>
    [Fact]
    public void ForeachLoopVariable_Inherits_Annotated_From_NullableStringElement()
    {
        var symbol = new VariableSymbol(
            SymbolId.None,
            name: "elem",
            typeName: "STRING",
            isMutable: false,
            nullableAnnotation: NullableAnnotation.Annotated);
        Assert.Equal(NullableAnnotation.Annotated, symbol.NullableAnnotation);
        var reference = new BoundVariableExpression(default, symbol);
        var nominal = Assert.IsType<NominalBoundType>(reference.Type);
        Assert.Equal(NullableAnnotation.Annotated, nominal.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — end-to-end binder-path guard: a §EACH with an
    /// explicit <c>:string</c> element type registers a foreach loop
    /// variable whose VariableSymbol carries NotAnnotated. This
    /// complements the direct-inspection tests above by proving the
    /// annotation flow is threaded through <c>BindForeachStatement</c>
    /// (not just <c>VariableSymbol</c>'s constructor).
    /// </summary>
    [Fact]
    public void ForeachLoopVariable_BinderPath_Inherits_NotAnnotated_From_NonNullString()
    {
        const string source = """
            §M{m1:ForeachStr}
              §F{f1:Use:pub} () -> void
                §EACH{e1:s:string} STR:"unused"
                  §P s
                §/EACH{e1}
            """;

        var (module, _) = BindSource(source);
        var loopVar = FindFirstForeachLoopVariable(module);
        Assert.Equal(NullableAnnotation.NotAnnotated, loopVar.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — mirror of the NotAnnotated case: <c>:?string</c>
    /// element type flows Annotated.
    /// </summary>
    [Fact]
    public void ForeachLoopVariable_BinderPath_Inherits_Annotated_From_NullableString()
    {
        const string source = """
            §M{m1:ForeachNullStr}
              §F{f1:Use:pub} () -> void
                §EACH{e1:s:?string} STR:"unused"
                  §P s
                §/EACH{e1}
            """;

        var (module, _) = BindSource(source);
        var loopVar = FindFirstForeachLoopVariable(module);
        Assert.Equal(NullableAnnotation.Annotated, loopVar.NullableAnnotation);
    }

    /// <summary>
    /// Task #3 — non-STRING parameters must keep Oblivious (§D6 scope
    /// guard). A <c>:int</c> parameter must not have its declared
    /// nullability spuriously promoted.
    /// </summary>
    [Fact]
    public void ParameterSymbol_NonString_Keeps_Oblivious()
    {
        const string source = """
            §M{m1:IntParam}
              §F{f1:Ok:pub} (INT:n) -> void
                §B{~x} n
            """;

        var (module, _) = BindSource(source);
        var parameter = FindFirstFunctionParameter(module);
        Assert.Equal(NullableAnnotation.Oblivious, parameter.NullableAnnotation);
    }

    /// <summary>
    /// Locates a class field / property symbol by (className, memberName)
    /// via the module's flat <see cref="BoundModule.SymbolsById"/>
    /// dictionary — fields and properties are registered as
    /// <see cref="VariableSymbol"/>s with <c>DeclaringTypeName</c> set on
    /// the class-registration pass, but they are not directly hung off a
    /// <c>BoundClass</c> node (BoundModule.Functions is the only child
    /// collection today).
    /// </summary>
    private static VariableSymbol FindMemberSymbol(BoundModule module, string className, string memberName)
    {
        foreach (var symbol in module.SymbolsById.Values)
        {
            if (symbol is VariableSymbol variable
                && (variable.IsField || variable.IsProperty)
                && variable.Name == memberName
                && variable.DeclaringTypeName != null
                && (variable.DeclaringTypeName == className
                    || variable.DeclaringTypeName.EndsWith("." + className,
                        StringComparison.Ordinal)))
            {
                return variable;
            }
        }
        throw new Xunit.Sdk.XunitException(
            $"Could not find member '{className}.{memberName}' in bound module.");
    }

    private static VariableSymbol FindFirstLambdaParameter(BoundModule module)
    {
        foreach (var fn in module.Functions)
        {
            foreach (var statement in fn.Body)
            {
                if (statement is BoundBindStatement bind
                    && bind.Initializer is BoundLambdaExpression lambda
                    && lambda.Parameters.Count > 0)
                {
                    return lambda.Parameters[0];
                }
            }
        }
        throw new Xunit.Sdk.XunitException("Could not find lambda parameter in bound module.");
    }

    private static VariableSymbol FindFirstFunctionParameter(BoundModule module)
    {
        foreach (var fn in module.Functions)
        {
            if (fn.Symbol.Parameters.Count > 0)
                return fn.Symbol.Parameters[0];
        }
        throw new Xunit.Sdk.XunitException("Could not find function parameter in bound module.");
    }

    private static VariableSymbol FindFirstForeachLoopVariable(BoundModule module)
    {
        foreach (var fn in module.Functions)
        {
            foreach (var statement in fn.Body)
            {
                if (statement is BoundForeachStatement fe)
                    return fe.LoopVariable;
            }
        }
        throw new Xunit.Sdk.XunitException("Could not find foreach loop variable in bound module.");
    }

    // ================================================================
    // v0.14 §S5 severity flip — Calor0272/0273/0274 promote from Info
    // to Error once SemanticsVersion.Major crosses the >=2 gate.
    // The gate is documented in D7 / F-3 of
    // docs/plans/v0.14-nullability-enforcement-scoping.md and is
    // consulted by SemanticsVersion.NullabilitySeverityFor. Task #14
    // bumped Major to 2, so the emit sites now yield Error by default.
    // ================================================================

    /// <summary>
    /// The S5 gate helper returns <see cref="DiagnosticSeverity.Error"/>
    /// when the effective SemVer.Major is at or past 2. This is the
    /// precise condition Task #4 introduced; changing the threshold
    /// silently would demote the three nullability diagnostics.
    /// </summary>
    [Fact]
    public void NullabilitySeverity_S5Gate_Error_At_Major2()
    {
        Assert.Equal(DiagnosticSeverity.Error, SemanticsVersion.NullabilitySeverityFor(2));
        // Ratchet against a future Major bump — anything >= 2 must
        // remain Error to preserve the S5 contract.
        Assert.Equal(DiagnosticSeverity.Error, SemanticsVersion.NullabilitySeverityFor(3));
    }

    /// <summary>
    /// Legacy-SemVer branch: modules declaring <c>§SEMVER[1.0.0]</c>
    /// (or any effective Major &lt; 2) must still see the diagnostics
    /// at <see cref="DiagnosticSeverity.Info"/>. This test guards the
    /// legacy fall-through the moment the SEMVER directive is threaded
    /// through the binder in a follow-up slice; today the callers pass
    /// the compiler's <see cref="SemanticsVersion.Major"/> instead.
    /// </summary>
    [Fact]
    public void NullabilitySeverity_S5Gate_Info_At_LegacyMajor()
    {
        Assert.Equal(DiagnosticSeverity.Info, SemanticsVersion.NullabilitySeverityFor(1));
        Assert.Equal(DiagnosticSeverity.Info, SemanticsVersion.NullabilitySeverityFor(0));
    }

    /// <summary>
    /// End-to-end: Calor0272 fires at Error under the current compiler
    /// <see cref="SemanticsVersion.Major"/> (= 2). Observes the actual
    /// severity carried on the emitted diagnostic, not just the helper's
    /// return value — a regression that miswires the emit site (e.g.
    /// reverts to <c>ReportInfo</c>) would flip THIS test red even if
    /// the helper stays correct.
    /// </summary>
    [Fact]
    public void S5_Calor0272_IsError_Under_CurrentSemVer()
    {
        Assert.True(SemanticsVersion.Major >= 2,
            "This test only exercises the S5 Error branch; if Major reverts below 2 the assertion below is invalid.");

        const string source = """
            §M{m1:S5Bind}
              §F{f1:Bad:pub} () -> void
                §E{env}
                §B{bad:string} §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        var diag = Assert.Single(diagnostics.Where(d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    /// <summary>
    /// End-to-end: Calor0273 fires at Error under the current compiler
    /// <see cref="SemanticsVersion.Major"/> (= 2).
    /// </summary>
    [Fact]
    public void S5_Calor0273_IsError_Under_CurrentSemVer()
    {
        Assert.True(SemanticsVersion.Major >= 2,
            "This test only exercises the S5 Error branch; if Major reverts below 2 the assertion below is invalid.");

        const string source = """
            §M{m1:S5Return}
              §F{f1:GetEnv:pub} () -> string
                §E{env}
                §R §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        var diag = Assert.Single(diagnostics.Where(d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    /// <summary>
    /// End-to-end: Calor0274 fires at Error under the current compiler
    /// <see cref="SemanticsVersion.Major"/> (= 2).
    /// </summary>
    [Fact]
    public void S5_Calor0274_IsError_Under_CurrentSemVer()
    {
        Assert.True(SemanticsVersion.Major >= 2,
            "This test only exercises the S5 Error branch; if Major reverts below 2 the assertion below is invalid.");

        const string source = """
            §M{m1:S5Call}
              §F{f1:Bad:pub} () -> void
                §B{contents:string} §C{System.IO.File.ReadAllText} §A §C{System.Environment.GetEnvironmentVariable} §A STR:"PATH" §/C §/C
            """;

        var (_, diagnostics) = BindSource(source);

        var diag = Assert.Single(diagnostics.Where(d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    /// <summary>
    /// Symmetry guard: all three diagnostics share the same S5 gate and
    /// must therefore emit at the same severity end-to-end. If a future
    /// change accidentally wires one code to a different gate (e.g. a
    /// literal <see cref="DiagnosticSeverity.Warning"/> at the call
    /// site), this test flips red. Complements the per-code checks
    /// above by pinning the invariant across the three sites.
    /// </summary>
    [Fact]
    public void S5_AllThreeCodes_ShareSameGatedSeverity()
    {
        const string bindSource = """
            §M{m1:S5Bind}
              §F{f1:Bad:pub} () -> void
                §E{env}
                §B{bad:string} §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;
        const string returnSource = """
            §M{m1:S5Return}
              §F{f1:GetEnv:pub} () -> string
                §E{env}
                §R §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;
        const string callSource = """
            §M{m1:S5Call}
              §F{f1:Bad:pub} () -> void
                §B{contents:string} §C{System.IO.File.ReadAllText} §A §C{System.Environment.GetEnvironmentVariable} §A STR:"PATH" §/C §/C
            """;

        var (_, bindDiags) = BindSource(bindSource);
        var (_, returnDiags) = BindSource(returnSource);
        var (_, callDiags) = BindSource(callSource);

        var gated = SemanticsVersion.NullabilitySeverityFor();
        var bindDiag = Assert.Single(bindDiags.Where(d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding));
        var returnDiag = Assert.Single(returnDiags.Where(d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable));
        var callDiag = Assert.Single(callDiags.Where(d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));

        Assert.Equal(gated, bindDiag.Severity);
        Assert.Equal(gated, returnDiag.Severity);
        Assert.Equal(gated, callDiag.Severity);
    }

    // ================================================================
    // v0.14 §S6 — task #7 Phase-C: array-element STRING nullability.
    // Widens the D6 scope from scalar STRING to arrays whose element is
    // a STRING. Target `[str]` (non-null elements) vs source with `[?str]`
    // elements is the mismatch that trips Calor0272/0273/0274 here.
    // ================================================================

    /// <summary>
    /// S6 predicate — bind-site direct check: an <see cref="ArrayBoundType"/>
    /// source whose element is <c>Annotated</c> STRING assigned to a
    /// declared non-null-element array target (<see cref="NullabilityChecker.IsPossiblyNullAssignedTo"/>)
    /// returns true. The container-array annotation is orthogonal — only
    /// the element mismatch matters at S6.
    /// </summary>
    [Fact]
    public void Calor0272_Fires_When_NullableStringElement_Assigned_To_NonNullElementArray()
    {
        var source = new NullabilityTestExpr(
            new ArrayBoundType(
                new NominalBoundType("STRING", NullableAnnotation.Annotated)));
        var target = new ArrayBoundType(
            new NominalBoundType("STRING", NullableAnnotation.NotAnnotated));

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S6 predicate — round-trip guard: a NotAnnotated-element source
    /// array assigned to a NotAnnotated-element target must NOT fire.
    /// Prevents the widened check from becoming a false-positive on the
    /// common <c>string[]</c> → <c>string[]</c> round-trip.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_NonNullElementArray_Roundtrip()
    {
        var source = new NullabilityTestExpr(
            new ArrayBoundType(
                new NominalBoundType("STRING", NullableAnnotation.NotAnnotated)));
        var target = new ArrayBoundType(
            new NominalBoundType("STRING", NullableAnnotation.NotAnnotated));

        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S6 predicate — target-nullability guard: a nullable-element target
    /// (<c>[?str]</c>) accepts a possibly-null-element source by design.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_When_TargetElement_IsNullableString()
    {
        var source = new NullabilityTestExpr(
            new ArrayBoundType(
                new NominalBoundType("STRING", NullableAnnotation.Annotated)));
        var target = new ArrayBoundType(
            new NominalBoundType("STRING", NullableAnnotation.Annotated));

        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S6 scope guard: an array whose element type is NOT a string is
    /// out of scope even under the S6 widening. Element-annotation
    /// mismatches on non-STRING element types return false.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_NonStringElement_Array()
    {
        var source = new NullabilityTestExpr(
            new ArrayBoundType(
                new NominalBoundType("INT", NullableAnnotation.Annotated)));
        var target = new ArrayBoundType(
            new NominalBoundType("INT", NullableAnnotation.NotAnnotated));

        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S6 bind-site integration: binding an array source with
    /// Annotated-element STRING into a Calor <c>[str]</c> target fires
    /// Calor0272 at Info severity. Uses a hand-crafted source (the corpus
    /// has no natural Annotated-element array constructor) via a
    /// synthetic §B{x:[str]} y where y is bound from a BCL API whose
    /// return has Annotated elements.
    /// </summary>
    [Fact]
    public void Calor0272_Fires_For_ArrayStringTarget_Direct_Predicate()
    {
        // Direct-inspection sibling of the integration case, guarding
        // the shape gate on the target string. The integration path
        // requires an Annotated-element BCL return (rare in .NET's
        // canonical shape database); this test locks the predicate wire.
        var source = new NullabilityTestExpr(
            new ArrayBoundType(
                new NominalBoundType("STRING", NullableAnnotation.Oblivious)));
        var target = new ArrayBoundType(
            new NominalBoundType("STRING", NullableAnnotation.NotAnnotated));

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S6 return-site direct check: the predicate composed with the
    /// return-type shape gate. Bind a Calor-shape source directly against
    /// an ArrayBoundType return-type target; this mirrors what
    /// <c>BindReturnStatement</c> observes when <c>_currentFunctionReturnType</c>
    /// resolves to <c>[str]</c>.
    /// </summary>
    [Fact]
    public void Calor0273_Predicate_Fires_For_NullableElementArray_Return()
    {
        var source = new NullabilityTestExpr(
            new ArrayBoundType(
                new NominalBoundType("STRING", NullableAnnotation.Annotated)));
        var target = new ArrayBoundType(
            new NominalBoundType("STRING", NullableAnnotation.NotAnnotated));

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S6 call-site direct check: same predicate wiring, argument →
    /// parameter direction. Bearing a possibly-null-element source array
    /// into a non-null-element parameter fires Calor0274.
    /// </summary>
    [Fact]
    public void Calor0274_Predicate_Fires_For_NullableElementArray_Argument()
    {
        var source = new NullabilityTestExpr(
            new ArrayBoundType(
                new NominalBoundType("STRING", NullableAnnotation.Annotated)));
        var parameterTarget = new ArrayBoundType(
            new NominalBoundType("STRING", NullableAnnotation.NotAnnotated));

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, parameterTarget));
    }

    // ================================================================
    // v0.14 §S7 whitelisted-generic tests (Phase-C scope widening).
    // Widens the string-scope nullability gate from scalar STRING (S3)
    // and array-of-STRING elements (S6) to a whitelisted set of generic
    // instantiations: Option<T>, List<T>, IList<T>, IEnumerable<T>,
    // IReadOnlyList<T>, ICollection<T>, IReadOnlyCollection<T> — where
    // T is STRING. The container's own annotation is orthogonal; only
    // the position-0 payload/element mismatch trips the predicate.
    // Non-whitelisted definitions (e.g. Dictionary) are out-of-scope
    // per the D6 discipline that ships each widening slice-by-slice.
    // ================================================================

    /// <summary>
    /// S7 predicate — Option payload widening: an
    /// <c>Option&lt;Annotated STRING&gt;</c> source assigned to a declared
    /// non-null-payload <c>Option&lt;NotAnnotated STRING&gt;</c> target
    /// trips <see cref="NullabilityChecker.IsPossiblyNullAssignedTo"/>.
    /// Mirrors the S6 array-element check on the payload axis rather
    /// than the element axis.
    /// </summary>
    [Fact]
    public void S7_Calor0272_Fires_When_NullableOption_Bound_To_NonNullOption()
    {
        var optionDef = new NominalBoundType("Option", NullableAnnotation.Oblivious);
        var source = new NullabilityTestExpr(
            new GenericInstantiationBoundType(
                optionDef,
                ImmutableArray.Create<BoundType>(
                    new NominalBoundType("STRING", NullableAnnotation.Annotated))));
        var target = new GenericInstantiationBoundType(
            optionDef,
            ImmutableArray.Create<BoundType>(
                new NominalBoundType("STRING", NullableAnnotation.NotAnnotated)));

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S7 predicate — round-trip guard: an <c>Option&lt;NotAnnotated STRING&gt;</c>
    /// source assigned to a <c>Option&lt;NotAnnotated STRING&gt;</c> target
    /// must NOT fire. Prevents the widened check from becoming a false-
    /// positive on the ordinary safe round-trip.
    /// </summary>
    [Fact]
    public void S7_Calor0272_DoesNotFire_For_NonNullOption_Roundtrip()
    {
        var optionDef = new NominalBoundType("Option", NullableAnnotation.Oblivious);
        var source = new NullabilityTestExpr(
            new GenericInstantiationBoundType(
                optionDef,
                ImmutableArray.Create<BoundType>(
                    new NominalBoundType("STRING", NullableAnnotation.NotAnnotated))));
        var target = new GenericInstantiationBoundType(
            optionDef,
            ImmutableArray.Create<BoundType>(
                new NominalBoundType("STRING", NullableAnnotation.NotAnnotated)));

        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S7 predicate — List element widening: a
    /// <c>List&lt;Annotated STRING&gt;</c> source assigned to a declared
    /// non-null-element <c>List&lt;NotAnnotated STRING&gt;</c> target
    /// trips the predicate. Same shape gate as Option but for one of the
    /// five whitelisted collection containers.
    /// </summary>
    [Fact]
    public void S7_Calor0272_Fires_When_NullableListElement_Bound_To_NonNullListElement()
    {
        var listDef = new NominalBoundType("List", NullableAnnotation.Oblivious);
        var source = new NullabilityTestExpr(
            new GenericInstantiationBoundType(
                listDef,
                ImmutableArray.Create<BoundType>(
                    new NominalBoundType("STRING", NullableAnnotation.Annotated))));
        var target = new GenericInstantiationBoundType(
            listDef,
            ImmutableArray.Create<BoundType>(
                new NominalBoundType("STRING", NullableAnnotation.NotAnnotated)));

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S7 scope guard: a non-whitelisted generic container
    /// (<c>Dictionary&lt;string, string&gt;</c>) is out-of-scope per D6
    /// even under the S7 widening. The predicate must not fire when the
    /// definition is not one of the six whitelisted containers, proving
    /// scope stays narrow.
    /// </summary>
    [Fact]
    public void S7_Calor0272_DoesNotFire_For_NonWhitelistedContainer()
    {
        // Construct a Dictionary<STRING, STRING> shape directly. Even if a
        // predicate implementation naively "walked into" the type arguments,
        // Dictionary is not on the S7 whitelist, so the widened target
        // gate must reject it and return false.
        var dictDef = new NominalBoundType("Dictionary", NullableAnnotation.Oblivious);
        var source = new NullabilityTestExpr(
            new GenericInstantiationBoundType(
                dictDef,
                ImmutableArray.Create<BoundType>(
                    new NominalBoundType("STRING", NullableAnnotation.Annotated),
                    new NominalBoundType("STRING", NullableAnnotation.Annotated))));
        var target = new GenericInstantiationBoundType(
            dictDef,
            ImmutableArray.Create<BoundType>(
                new NominalBoundType("STRING", NullableAnnotation.NotAnnotated),
                new NominalBoundType("STRING", NullableAnnotation.NotAnnotated)));

        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S7 return-site predicate check: an Option-of-Annotated-STRING
    /// source returned from a function whose declared return type is
    /// <c>Option&lt;string&gt;</c> (non-null payload) trips the same
    /// predicate — this is what <c>BindReturnStatement</c> composes
    /// once <c>_currentFunctionReturnType</c> resolves through
    /// <see cref="Binder"/>'s widened <c>TryBuildStringTarget</c>.
    /// </summary>
    [Fact]
    public void S7_Calor0273_Fires_For_NullableOption_Return_Into_NonNullDeclared()
    {
        var optionDef = new NominalBoundType("Option", NullableAnnotation.Oblivious);
        var source = new NullabilityTestExpr(
            new GenericInstantiationBoundType(
                optionDef,
                ImmutableArray.Create<BoundType>(
                    new NominalBoundType("STRING", NullableAnnotation.Annotated))));
        var returnTarget = new GenericInstantiationBoundType(
            optionDef,
            ImmutableArray.Create<BoundType>(
                new NominalBoundType("STRING", NullableAnnotation.NotAnnotated)));

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, returnTarget));
    }

    /// <summary>
    /// S7 call-site predicate check: same predicate wiring, argument →
    /// parameter direction. Passing an Option-of-Annotated-STRING source
    /// into a parameter declared <c>Option&lt;string&gt;</c> (non-null
    /// payload) fires Calor0274. Mirrors the S6 array-argument test.
    /// </summary>
    [Fact]
    public void S7_Calor0274_Fires_For_NullableOption_Passed_To_NonNullParam()
    {
        var optionDef = new NominalBoundType("Option", NullableAnnotation.Oblivious);
        var source = new NullabilityTestExpr(
            new GenericInstantiationBoundType(
                optionDef,
                ImmutableArray.Create<BoundType>(
                    new NominalBoundType("STRING", NullableAnnotation.Annotated))));
        var parameterTarget = new GenericInstantiationBoundType(
            optionDef,
            ImmutableArray.Create<BoundType>(
                new NominalBoundType("STRING", NullableAnnotation.NotAnnotated)));

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, parameterTarget));
    }

    // ================================================================
    // v0.14 §S8 — task #7 Phase-C follow-on: user-declared reference
    // types. `:Foo` is non-null; `:?Foo` is nullable. Trips Calor0272
    // (bind) and Calor0273 (return) when a possibly-null user-class
    // source is funneled into a non-null user-class target. Call-site
    // emission (Calor0274) for pure-Calor callees is still BCL-only per
    // the S4 scoping comment in BindCallExpression — this test slice
    // locks the predicate wiring so the future widening only threads
    // annotations, not re-derives the user-ref shape gate.
    // ================================================================

    /// <summary>
    /// S8 bind-site — a §B{b:Foo} initialized from a :?Foo parameter
    /// reference fires Calor0272 at the S5-gated severity.
    /// </summary>
    [Fact]
    public void S8_Calor0272_Fires_When_NullableUserClass_Bound_To_NonNullUserClass()
    {
        const string source = """
            §M{m1:S8Bind}
              §CL{c1:Foo:pub}
                §MT{m1:Use:pub} (?Foo:a) -> void
                  §B{b:Foo} a
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && d.Severity == SemanticsVersion.NullabilitySeverityFor());
    }

    /// <summary>
    /// S8 round-trip guard — a non-null user-class local bound into a
    /// non-null user-class target must NOT fire Calor0272. Guards
    /// against the widened target-shape gate becoming a false-positive
    /// on ordinary Foo → Foo assignments.
    /// </summary>
    [Fact]
    public void S8_Calor0272_DoesNotFire_For_NonNullUserClass_Roundtrip()
    {
        const string source = """
            §M{m1:S8Roundtrip}
              §CL{c1:Foo:pub}
                §MT{m1:Use:pub} (Foo:a) -> void
                  §B{b:Foo} a
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// S8 return-site — a function declared -> Foo that returns a
    /// possibly-null :?Foo-typed local must emit Calor0273 at the
    /// S5-gated severity. Symmetric with the S4 return-site test on
    /// scalar STRING.
    /// </summary>
    [Fact]
    public void S8_Calor0273_Fires_For_NullableUserClass_Return_Into_NonNullDeclared()
    {
        const string source = """
            §M{m1:S8Return}
              §CL{c1:Foo:pub}
                §MT{m1:Get:pub} (?Foo:a) -> Foo
                  §R a
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable
            && d.Severity == SemanticsVersion.NullabilitySeverityFor());
    }

    /// <summary>
    /// S8 call-site direct-predicate — an Annotated user-class source
    /// assigned into a NotAnnotated user-class target fires the same
    /// predicate the S4 call-site consumes on BCL calls. Predicate
    /// form (mirrors S6 and S7) because pure-Calor call-site emission
    /// still runs BCL-only ("the parameter-side annotation flow
    /// requires a resolved Roslyn IMethodSymbol, and non-BCL Calor
    /// callees do not yet carry annotated parameter BoundTypes"; see
    /// BindCallExpression scoping comment). This test locks the
    /// predicate wiring so the pure-Calor widening only needs to
    /// thread annotations, not re-derive the user-ref shape gate.
    /// </summary>
    [Fact]
    public void S8_Calor0274_Predicate_Fires_For_NullableUserClass_Argument()
    {
        var source = new NullabilityTestExpr(
            new NominalBoundType("Bar", NullableAnnotation.Annotated));
        var parameterTarget = new NominalBoundType("Bar", NullableAnnotation.NotAnnotated);

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, parameterTarget));
    }

    /// <summary>
    /// S8 primitive guard — value-type primitives (INT, BOOL, …) are
    /// never null and must not participate in the widened check. A
    /// synthetic Annotated INT source assigned to a NotAnnotated INT
    /// target must NOT fire. Direct-predicate test — no source-level
    /// repro because INT already lacks a nullable-declaration syntax.
    /// </summary>
    [Fact]
    public void S8_Does_Not_Fire_For_PrimitiveType_INT()
    {
        var source = new NullabilityTestExpr(
            new NominalBoundType("INT", NullableAnnotation.Annotated));
        var target = new NominalBoundType("INT", NullableAnnotation.NotAnnotated);

        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S8 unresolved-type guard — an UnresolvedBoundType source (e.g. a
    /// callee whose type resolution failed) must NOT crash the
    /// predicate and must NOT fire against a user-ref target. The
    /// syntactic user-ref-shape gate is the whole point: we return
    /// false rather than propagating a resolution failure as a
    /// nullability diagnostic.
    /// </summary>
    [Fact]
    public void S8_Does_Not_Fire_For_UnresolvedType()
    {
        var source = new NullabilityTestExpr(
            new UnresolvedBoundType("test-unresolved"));
        var target = new NominalBoundType("Foo", NullableAnnotation.NotAnnotated);

        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S8 dotted-namespace bridge — <see cref="NullabilityChecker"/>'s
    /// short-name comparator (<c>ShortNameEquals</c>) treats
    /// <c>My.Custom.Foo</c> and <c>Foo</c> as equivalent for the purpose
    /// of source-vs-target shape matching. A Roslyn-resolved BCL type
    /// (fully qualified) assigned into a Calor target (bare identifier)
    /// must therefore fire the same predicate. Guards against a future
    /// change that drops the dotted-name bridging and silently breaks
    /// BCL-source / Calor-target user-ref cases.
    /// </summary>
    [Fact]
    public void S8_Bridges_Dotted_Namespace_On_ShortName_Match()
    {
        var source = new NullabilityTestExpr(
            new NominalBoundType("My.Custom.Foo", NullableAnnotation.Annotated));
        var target = new NominalBoundType("Foo", NullableAnnotation.NotAnnotated);

        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    /// <summary>
    /// S8 dotted-namespace mismatch — different short-name segments
    /// (<c>My.Custom.Foo</c> vs <c>Bar</c>) must NOT fire. Complements
    /// the bridge test above: the widened check accepts the SAME
    /// user-ref type on both sides, not any pair of nominal types.
    /// Type-mismatch is a different diagnostic family (out-of-scope
    /// for S8).
    /// </summary>
    [Fact]
    public void S8_Does_Not_Fire_For_DifferentShortName_Nominals()
    {
        var source = new NullabilityTestExpr(
            new NominalBoundType("My.Custom.Foo", NullableAnnotation.Annotated));
        var target = new NominalBoundType("Bar", NullableAnnotation.NotAnnotated);

        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(source, target));
    }

    // ================================================================
    // v0.14 §F-3A — Calor-native call-site parameter check. Widens
    // Calor0274 emission (previously BCL-only per the §S4 scoping
    // comment in BindCallExpression) to fire when a pure-Calor callee's
    // declared parameter TypeName runs through TryBuildStringTarget and
    // yields a NotAnnotated shape. Reuses the same predicate
    // (NullabilityChecker.IsPossiblyNullAssignedTo) as the BCL branch —
    // this slice threads the resolved FunctionSymbol.Parameters, it
    // does NOT re-derive the shape gate. Precursor to S8-Oblivious.
    // ================================================================

    /// <summary>
    /// F-3A scope-surprise pin (fire path blocked by overload gap).
    /// The natural repro — a <c>:?string</c> local or an inline
    /// <c>Environment.GetEnvironmentVariable</c> call passed into a
    /// Calor-native <c>:string</c> parameter — cannot fire Calor0274
    /// today because pure-Calor <see cref="Binder.ResolveCall"/> does
    /// not OPTION-unwrap argument types before overload matching
    /// (only the BCL branch's <c>TryResolveBclCall</c> does that
    /// stripping, per PR #1060 review finding M1). The call therefore
    /// fails resolution with <c>Calor0208 NoMatchingOverload</c> and
    /// my widening — which only fires on <c>Kind == Resolved</c> —
    /// never gets a chance to fire.
    ///
    /// This test pins that behavior. A future slice (either PR B
    /// threading Calor-native return-type annotation flow so an
    /// inline call can surface a matching DisplayString, or a
    /// dedicated widening of <see cref="Binder.ResolveCall"/> to
    /// treat OPTION as an implicit conversion) will flip the
    /// assertion. The <c>S8_Calor0274_Predicate_Fires_For_NullableUserClass_Argument</c>
    /// direct-predicate test above already locks the predicate
    /// wiring — this fixture guards the emission path so a future
    /// widening doesn't accidentally regress.
    /// </summary>
    [Fact]
    public void F3A_Calor0274_ScopeBlocked_When_NullableArg_Fails_CalorOverload()
    {
        const string source = """
            §M{m1:F3ANativeBlocked}
              §F{f1:Take:pub} (string:name) -> void
                §E{env}
              §F{f2:Caller:pub} () -> void
                §E{env}
                §C{Take} §A §C{System.Environment.GetEnvironmentVariable} §A STR:"PATH" §/C §/C
            """;

        var (_, diagnostics) = BindSource(source);

        // Current behavior: overload resolution fails (Calor0208)
        // before Calor0274 can fire. Flip both asserts in the follow-on
        // slice that widens ResolveCall or threads Calor-native return
        // annotations to matching-DisplayString shapes.
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NoMatchingOverload);
        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    /// <summary>
    /// F-3A round-trip guard — passing a non-null user-ref parameter
    /// reference to a Calor-native user-ref parameter must NOT fire
    /// Calor0274. Complements the fire-case below so the widened
    /// emission does not become a false-positive on ordinary
    /// non-null Calor→Calor user-ref calls. Routes through
    /// <see cref="Binder.BindCallExpression"/> via <c>§R</c> so the
    /// widening code path is actually exercised (statement-form
    /// <c>§C{...}</c> uses BindCallStatement which does not carry
    /// the check — mirrors the BCL path).
    /// </summary>
    [Fact]
    public void F3A_Calor0274_DoesNotFire_For_NonNullArg_To_NonNullable_CalorParameter()
    {
        const string source = """
            §M{m1:F3AUserRefOk}
              §CL{c1:Bar:pub}
                §MT{m1:Take:pub} (Bar:a) -> Bar
                  §R a
                §MT{m2:Caller:pub} (Bar:x) -> Bar
                  §R §C{this.Take} §A x §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    /// <summary>
    /// F-3A parameter-declared-nullable guard — when the Calor-native
    /// user-ref parameter is <c>:?Bar</c> the callee accepts null by
    /// design. Passing a possibly-null <c>:?Bar</c> parameter reference
    /// to it must NOT fire Calor0274 (parallels
    /// <c>Calor0274_DoesNotFire_When_Parameter_IsNullableString</c>
    /// for the BCL branch). Routes through
    /// <see cref="Binder.BindCallExpression"/> via <c>§R</c>.
    /// </summary>
    [Fact]
    public void F3A_Calor0274_DoesNotFire_When_TargetParameter_IsNullable()
    {
        const string source = """
            §M{m1:F3AUserRefNullableParam}
              §CL{c1:Bar:pub}
                §MT{m1:Take:pub} (?Bar:a) -> Bar
                  §R a
                §MT{m2:Caller:pub} (?Bar:x) -> Bar
                  §R §C{this.Take} §A x §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    /// <summary>
    /// F-3A canonical fire — user-ref variant. A <c>:?Foo</c>
    /// parameter reference passed into a <c>:Foo</c> parameter of a
    /// peer method fires Calor0274 at the S5-gated severity. Routes
    /// through <see cref="Binder.BindCallExpression"/> via the <c>§R</c>
    /// return so the widened emission is exercised (statement-form
    /// <c>§C{...}</c> goes through <see cref="Binder.BindCallStatement"/>
    /// which does not carry the Calor0274 emitter — parallels the BCL
    /// path which also only fires from <c>BindCallExpression</c>).
    ///
    /// User-ref works end-to-end today because
    /// <see cref="BoundVariableExpression.BuildStringAnnotatedTypeOrDefault"/>
    /// strips the leading '?' from the QualifiedName on user-ref
    /// references (per PR #1074's dotted-namespace bridge), so the
    /// argument's DisplayString "Foo" canonicalizes cleanly and
    /// overload resolution succeeds. The scalar STRING variant does
    /// NOT have that stripping (STRING's OPTION-of-STRING branch keeps
    /// "?string" on the QualifiedName so downstream Option-shaped
    /// consumers stay unchanged), so the STRING fire path stays
    /// blocked pending a follow-on slice — see the scope-blocked pin
    /// above.
    /// </summary>
    [Fact]
    public void F3A_Calor0274_Fires_For_NullableUserRefArg_To_NonNullable_CalorParameter()
    {
        const string source = """
            §M{m1:F3AUserRef}
              §CL{c1:Bar:pub}
                §MT{m1:Take:pub} (Bar:a) -> Bar
                  §R a
                §MT{m2:Caller:pub} (?Bar:x) -> Bar
                  §R §C{this.Take} §A x §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && d.Severity == SemanticsVersion.NullabilitySeverityFor()
            && d.Message.Contains("'a'"));
    }

    // ================================================================
    // v0.14 F-3B (nullability) — Calor-native return-site annotation
    // flow: BoundCallExpression.Type carries the declared return
    // annotation for a resolved pure-Calor callee. Prior to this slice
    // pure-Calor callees produced an Oblivious BoundCallExpression.Type
    // regardless of `-> string` vs `-> ?string` / `-> Foo` vs `-> ?Foo`.
    // This is the S8-Oblivious precursor — once the return-side
    // annotation flows, S8 can widen call-site emission from BCL-only
    // to include pure-Calor callees without re-deriving the shape gate.
    // BCL-resolved returns take priority (MetadataBinder still wins).
    // ================================================================

    /// <summary>
    /// F-3B — a pure-Calor callee declared <c>-> ?string</c> must
    /// produce a <c>BoundCallExpression</c> whose <c>.Type</c> is a
    /// STRING <see cref="NominalBoundType"/> with <see cref="NullableAnnotation.Annotated"/>.
    /// Prior to the F-3B slice this stayed <see cref="NullableAnnotation.Oblivious"/>.
    /// </summary>
    [Fact]
    public void F3B_BoundCall_Type_Inherits_Declared_ReturnAnnotation_ForCalorFunction()
    {
        const string source = """
            §M{m1:F3BNullableStringReturn}
              §F{f1:GetFoo:pub} () -> ?string
                §R STR:"hi"
              §F{f2:Use:pub} () -> void
                §B{tmp:?string} §C{GetFoo} §/C
            """;

        var (bound, _) = BindSource(source);
        var call = FindFirstBoundCallInFunction(bound, "Use");
        var nominal = Assert.IsType<NominalBoundType>(call.Type);
        // QualifiedName preserves the declared surface form (matches
        // BinderOverloadSetTests contract of not canonicalizing raw types
        // on BoundCallExpression.Type). The annotation is the F-3B payload.
        Assert.Equal(NullableAnnotation.Annotated, nominal.NullableAnnotation);
    }

    /// <summary>
    /// F-3B — a pure-Calor callee declared <c>-> string</c> must
    /// produce a <c>BoundCallExpression</c> whose <c>.Type</c> is a
    /// STRING <see cref="NominalBoundType"/> with
    /// <see cref="NullableAnnotation.NotAnnotated"/> — not Oblivious.
    /// This is what unblocks S8-Oblivious: a downstream widening can
    /// diff NotAnnotated-vs-Annotated on Calor-native returns.
    /// </summary>
    [Fact]
    public void F3B_BoundCall_Type_Inherits_NotAnnotated_For_NonNullableCalorReturn()
    {
        const string source = """
            §M{m1:F3BNonNullStringReturn}
              §F{f1:GetFoo:pub} () -> string
                §R STR:"hi"
              §F{f2:Use:pub} () -> void
                §B{tmp:string} §C{GetFoo} §/C
            """;

        var (bound, _) = BindSource(source);
        var call = FindFirstBoundCallInFunction(bound, "Use");
        var nominal = Assert.IsType<NominalBoundType>(call.Type);
        // The annotation is the F-3B payload: previously Oblivious, now
        // NotAnnotated so a future S8-Oblivious widening on pure-Calor
        // call-sites can diff NotAnnotated-vs-Annotated correctly.
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    /// <summary>
    /// F-3B user-ref channel — a pure-Calor callee declared
    /// <c>-> ?Foo</c> (where Foo is a Calor user class) must produce a
    /// <c>BoundCallExpression</c> whose <c>.Type</c> is a
    /// <see cref="NominalBoundType"/> for <c>Foo</c> with
    /// <see cref="NullableAnnotation.Annotated"/>. Mirrors the scalar
    /// STRING case on the user-ref channel the §S8 target-side gate
    /// already handles.
    /// </summary>
    [Fact]
    public void F3B_BoundCall_Type_Inherits_Annotated_For_NullableUserClassReturn()
    {
        // Foo defined as a §CL then used both as parameter/return type of
        // sibling §MT methods. Static Foo.GetFoo call from Use exercises
        // the same user-ref channel §S8's target-side gate already handles.
        const string source = """
            §M{m1:F3BNullableUserClassReturn}
              §CL{c1:Foo:pub}
                §MT{m1:GetFoo:pub:static} (?Foo:x) -> ?Foo
                  §R x
                §MT{m2:Use:pub:static} (?Foo:y) -> void
                  §B{tmp:?Foo} §C{Foo.GetFoo} §A y §/C
            """;

        var (bound, diagnostics) = BindSource(source);
        var call = FindFirstBoundCallInFunction(bound, "Use");
        Assert.True(
            call.ResolvedSymbol is not null,
            "GetFoo callee failed to resolve. Diagnostics: "
            + string.Join(", ", diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var nominal = Assert.IsType<NominalBoundType>(call.Type);
        // The annotation is the F-3B payload — user-ref channel mirrors
        // the scalar STRING case. Raw declared form (?Foo) is preserved
        // in the QualifiedName to keep DisplayString byte-identical to
        // pre-F-3B behavior for pure-Calor calls.
        Assert.Equal(NullableAnnotation.Annotated, nominal.NullableAnnotation);
    }

    /// <summary>
    /// F-3B user-ref roundtrip — a pure-Calor callee declared
    /// <c>-> Foo</c> must produce a <c>BoundCallExpression</c> whose
    /// <c>.Type</c> is a <see cref="NominalBoundType"/> for <c>Foo</c>
    /// with <see cref="NullableAnnotation.NotAnnotated"/>. Guards
    /// against the widened channel collapsing every user-ref return
    /// to Annotated.
    /// </summary>
    [Fact]
    public void F3B_BoundCall_Type_Inherits_NotAnnotated_For_NonNullableUserClassReturn()
    {
        const string source = """
            §M{m1:F3BNonNullUserClassReturn}
              §CL{c1:Foo:pub}
                §MT{m1:GetFoo:pub:static} (Foo:x) -> Foo
                  §R x
                §MT{m2:Use:pub:static} (Foo:y) -> void
                  §B{tmp:Foo} §C{Foo.GetFoo} §A y §/C
            """;

        var (bound, diagnostics) = BindSource(source);
        var call = FindFirstBoundCallInFunction(bound, "Use");
        Assert.True(
            call.ResolvedSymbol is not null,
            "GetFoo callee failed to resolve. Diagnostics: "
            + string.Join(", ", diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var nominal = Assert.IsType<NominalBoundType>(call.Type);
        // Non-nullable Foo roundtrip: annotation flips from Oblivious
        // (pre-F-3B) to NotAnnotated. Raw QualifiedName ("Foo") is
        // preserved via the declared return-type string, not
        // TryBuildStringTarget's canonicalized shape.
        Assert.Equal("Foo", nominal.QualifiedName);
        Assert.Equal(NullableAnnotation.NotAnnotated, nominal.NullableAnnotation);
    }

    /// <summary>
    /// F-3B helper — locate the first <see cref="BoundCallExpression"/>
    /// under a function's body. Prefers the initializer of the first
    /// <see cref="BoundBindStatement"/> so tests can co-locate the
    /// callee (<c>GetFoo</c>) and the exercising binding
    /// (<c>§B{tmp:...} §C{GetFoo} …</c>) in the same module.
    /// </summary>
    private static BoundCallExpression FindFirstBoundCallInFunction(BoundModule module, string functionName)
    {
        foreach (var fn in module.Functions)
        {
            // Class methods surface as "Foo.Use"; top-level functions as
            // bare "Use". Match either by exact name OR by trailing
            // ".<functionName>" so the same helper works for both shapes.
            var matches = fn.Symbol.Name == functionName
                || fn.Symbol.Name.EndsWith("." + functionName, StringComparison.Ordinal);
            if (!matches) continue;
            foreach (var statement in fn.Body)
            {
                if (statement is BoundBindStatement bind
                    && bind.Initializer is BoundCallExpression call)
                {
                    return call;
                }
            }
        }
        var available = string.Join(
            ", ",
            module.Functions.Select(f =>
                $"{f.Symbol.Name}({f.Body.Count} stmts, container={f.ContainingTypeName})"));
        throw new Xunit.Sdk.XunitException(
            $"Could not find BoundCallExpression under function '{functionName}' in bound module. "
            + $"Available functions: [{available}]");
    }
}
