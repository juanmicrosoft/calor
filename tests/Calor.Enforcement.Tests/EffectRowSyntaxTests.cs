using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Design-doc pins <b>P1</b>, <b>P2</b> and <b>P3</b> for effect-row SYNTAX
/// (docs/design/effect-rows-in-the-type-system.md §3, Decision 1).
/// </summary>
/// <remarks>
/// This slice parses and emits rows. It does <b>not</b> check them, so every
/// assertion here is about <i>what the parser attached the row to</i> — never about
/// whether two rows are compatible. Row checking is E3 (Calor0424/0425).
/// </remarks>
public class EffectRowSyntaxTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        return new Parser(tokens, diagnostics).Parse();
    }

    private static IReadOnlyList<Diagnostic> ErrorsOf(DiagnosticBag bag, string code)
        => bag.Where(d => d.Code == code).ToList();

    // ---------------------------------------------------------------- P1 -----
    // The line rule attaches a same-line §E to the type it follows, not to the
    // enclosing declaration. Discriminating revert: drop the Span.Line comparison
    // in TryParseSameLineRow and the row becomes the declaration's again — the
    // parameter's Row goes null and the function's Effects becomes non-null.
    //
    // RE-SPECIFIED BY E2 SLICE B (design-doc §13.2's blockquote, recorded in
    // review round 1 of PR #1101). As written in slice a this pin used **Y1b** —
    // `§I{str:m} §E{cw}` — and asserted Calor0410 end-to-end. But `str` is not a
    // function type, so under §3.5 that row is a row on a position which cannot
    // carry one, and pin **P6** says it must be **Calor0405**. Two pins named
    // different answers for one source. P1 and P6 are both right about their own
    // claim, and the collision is the seam between a slice that CONSUMES rows
    // and a slice that checks WHAT THEY ARE ATTACHED TO.
    //
    // So P1 moves onto a function-typed subject — `§I{Func<i32,i32>:f} §E{cw}`
    // against a pure declaration, which is design-doc §3.6's E-3 — and the
    // non-function-typed cases (Y1a, Y1b, Y1c, Y5a, X2a, X2b, Z9, Z9b, Z9c) are
    // handed to `EffectRowLatticeTests.RowOnNonFunctionTypedPosition_IsCalor0405`.
    // What P1 still owns is the LINE RULE: which type the row attached to.

    [Fact]
    public void RowSuffix_SameLineOnI_IsParameterRow_NotDeclarationRow()
    {
        // A function-typed parameter carrying a row, on a declaration that
        // declares no effects of its own. On main the §F section loop reads the
        // §E as the DECLARATION's row, so `Apply` would be declared `cw`.
        const string source = """
            module P1 P1 block
              func Apply (Func<int, int>:transform ! cw, int:value) → int
                !
                return value
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var function = Assert.Single(module.Functions);
        var transform = function.Parameters[0];

        // The row is the PARAMETER's…
        Assert.NotNull(transform.Row);
        Assert.Contains("io", transform.Row!.Effects.Keys);
        Assert.Null(function.Parameters[1].Row);

        // …and the declaration's own row is the separate, later-line §E{}, which
        // stays pure. Under the pre-line-rule reading these would be one row.
        Assert.NotNull(function.Effects);
        Assert.Empty(function.Effects!.Effects);

        // End-to-end: a function-typed subject carries the row rather than being
        // rejected, so no Calor0405 — the control that keeps this pin from being
        // satisfied by P6's rule firing everywhere.
        var compiled = TestHarness.Compile(source);
        Assert.DoesNotContain(
            compiled.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMisplaced);

        // And the row reaches the TYPE SYSTEM, which is what slice b adds: the
        // parameter's symbol carries a FunctionBoundType whose Row is the
        // declared one. Checking it against an argument is E3 (Calor0424/0425).
        var bindingDiagnostics = new DiagnosticBag();
        var bound = new Calor.Compiler.Binding.Binder(bindingDiagnostics, "test.calr").Bind(module);
        var boundParameter = Assert.Single(bound.Functions).Symbol.Parameters[0];
        Assert.NotNull(boundParameter.FunctionType);
        Assert.Equal(
            Calor.Compiler.Binding.BoundTypes.EffectRow.Concrete(["io:console_write"]),
            boundParameter.FunctionType!.Row);
    }

    // ---------------------------------------------------------------- P3 -----
    // One case per §3.3 position, including the three that already parsed on main
    // (§LAM, §DEL, and a §MT declaration row) and which no test covered before.

    [Fact]
    public void Position1_DeclarationRow_Parses()
    {
        var module = Parse("""
            module P P block
              func F () → void
                ! cw
                Console.WriteLine("x")
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(Assert.Single(module.Functions).Effects);
    }

    [Fact]
    public void Position2_LambdaRow_Parses()
    {
        var module = Parse("""
            module P P block
              func Main () → void
                !
                let f = (int:x) ! => (x + 1)
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var bind = Assert.IsType<BindStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
        var lambda = Assert.IsType<LambdaExpressionNode>(bind.Initializer);
        Assert.NotNull(lambda.Effects);
    }

    [Fact]
    public void Position3_DelegateRow_Parses()
    {
        var module = Parse("""
            module P P block
              delegate Handler (int:x) → void
                ! cw

              func Main () → void
                !
                return
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var del = Assert.Single(module.Delegates);
        Assert.NotNull(del.Effects);
    }

    [Fact]
    public void Position4_ParameterTagRow_Parses()
    {
        var module = Parse("""
            module P P block
              func Apply (Func<int, int>:transform ! cw) → int
                ! cw
                return 0
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(Assert.Single(Assert.Single(module.Functions).Parameters).Row);
    }

    [Fact]
    public void Position5_InlineParameterRow_Parses()
    {
        // Executed baseline X9c: twelve Calor0100 on main, starting
        // "Expected CloseParen but found Effects".
        var module = Parse("""
            module P P block
              func Apply (Func<int, int>:transform ! cw, int:value) → int
                ! cw
                return 0
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var parameters = Assert.Single(module.Functions).Parameters;
        Assert.Equal(2, parameters.Count);
        Assert.NotNull(parameters[0].Row);
        Assert.Null(parameters[1].Row);
    }

    [Fact]
    public void Position6_ReturnRow_ParsesInBothSpellings()
    {
        var tagged = Parse("""
            module P P block
              func Make () → Func<int> ! cw
                ! cw
                return () => 0
            """, out var d1);
        Assert.False(d1.HasErrors);
        Assert.NotNull(Assert.Single(tagged.Functions).Output!.Row);

        var arrowed = Parse("""
            module P P block
              func Make () → Func<int> ! cw
                ! cw
                return () => 0
            """, out var d2);
        Assert.False(d2.HasErrors);
        Assert.NotNull(Assert.Single(arrowed.Functions).Output!.Row);
    }

    [Fact]
    public void Position7_BindingRow_Parses()
    {
        // Executed baseline Y3a: sixteen Calor0100 on main.
        var module = Parse("""
            module P P block
              func Main () → void
                !
                let Func<int, int>:f ! cw = (int:x) => (x + 1)
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var bind = Assert.IsType<BindStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
        Assert.NotNull(bind.Row);
    }

    [Fact]
    public void Position8_FieldRow_Parses()
    {
        // Executed baseline X9b: four Calor0100 on main, which is what disproved
        // Draft v1's claim that §FLD already parsed a row (§14.1).
        var module = Parse("""
            module P P block
              class C
                field Action<int>:onChange private ! cw
                func M () → void
                  !
                  return
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var field = Assert.Single(Assert.Single(module.Classes).Fields);
        Assert.NotNull(field.Row);
    }

    [Theory]
    // Z10 — the single-line spelling the design doc names.
    [InlineData("""
        §M{Z10}
          §F{Helper:pub} (i32:n) -> i32
            §E{}
            §R n
          §F{Main:pub} () -> void
            §E{}
            §R §C{Helper} §A INT:1 §E{cw} §/C
        """)]
    // The MULTI-LINE, closer-elided spelling. This is the discriminating case: it puts
    // the §E at the START of its own line, inside an argument list. An earlier draft of
    // this slice guarded the recovery on "the §E starts its line" and therefore reported
    // Calor0405 here — extending the diagnostic into exactly the place §3.3 forbids,
    // while the single-line Z10 above stayed green and hid it. Anchoring the recovery to
    // the §B / §FLD production that owns the row is what actually closes it.
    [InlineData("""
        §M{Z10b}
          §F{Helper:pub} (i32:n) -> i32
            §E{}
            §R n
          §F{Main:pub} () -> void
            §E{}
            §R §C{Helper}
              §A INT:1
              §E{cw}
        """)]
    public void Row_IsNotExtendedIntoACallArgumentList(string source)
    {
        // §3.3's explicit carve-out: "Arguments are values, not declarations; they
        // have no row, and Calor0405 is not extended there."
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Empty(ErrorsOf(diagnostics, DiagnosticCode.EffectRowMisplaced));
    }

    [Fact]
    public void Row_IsNotExtendedOntoADeclarationsOwnEWhenItsSignatureFailedToParse()
    {
        // Executed case X4. The inline signature `-> str!str` does not parse, which
        // leaves the function's own — entirely correct — §E{} line sitting in statement
        // position. Telling the author to move THAT is worse than useless. Anchoring
        // the recovery to §B / §FLD means it cannot fire here at all, and X4's committed
        // transcript is unchanged by this slice.
        Parse("""
            §M{X4}
              §F{M:pub} (i32:x) -> str!str
                §E{}
                §R "x"
            """, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Empty(ErrorsOf(diagnostics, DiagnosticCode.EffectRowMisplaced));
    }

    [Fact]
    public void Row_RecoverySubjectNeverNamesADeclarationInAnotherScope()
    {
        // The recovery names a declaration so the author knows which line to move the
        // row onto. An earlier draft remembered that declaration in a parser field,
        // which outlived its scope: a stray §E at the top of class D was told to move
        // onto a field line in class C. The subject is now the declaration in hand, so
        // a §E with no §B or §FLD before it produces no Calor0405 at all.
        Parse("""
            §M{F5}
              §CL{C:pub}
                §FLD{i32:x:priv}
                §MT{M:pub} () -> void
                  §E{}
              §CL{D:pub}
                §E{cw}
                §MT{N:pub} () -> void
                  §E{}
            """, out var diagnostics);

        Assert.Empty(ErrorsOf(diagnostics, DiagnosticCode.EffectRowMisplaced));
    }

    [Fact]
    public void EmptyRow_IsRecordedAndIsNotTheSameAsNoRow()
    {
        // §3.5 gives §E{} (declared pure) and an omitted row (Unknown) different
        // meanings, so the parser must keep them distinguishable even though this
        // slice does not yet act on the difference.
        var module = Parse("""
            module P P block
              func Apply (Func<int, int>:a !, Func<int, int>:b) → int
                !
                return 0
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var parameters = Assert.Single(module.Functions).Parameters;
        Assert.NotNull(parameters[0].Row);
        Assert.Empty(parameters[0].Row!.Effects);
        Assert.Null(parameters[1].Row);
    }
}
