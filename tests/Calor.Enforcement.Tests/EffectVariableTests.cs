using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Design-doc pin <b>P18</b> — the parse-side half: <c>eff</c> binders and where an
/// effect variable may be written (§7.2, §7.3).
/// </summary>
/// <remarks>
/// Instantiation, the join, and "used but never bound" all belong to E3, which owns
/// the binder. What is pinned here is exactly what the parser decides: which names
/// become binders, which rows may mention them, and that the compatibility
/// obligation of §14 Q2 holds.
/// </remarks>
public class EffectVariableTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        return new Parser(tokens, diagnostics).Parse();
    }

    private static IReadOnlyList<Diagnostic> ScopeErrors(DiagnosticBag bag)
        => bag.Where(d => d.Code == DiagnosticCode.EffectVariableScope).ToList();

    // ------------------------------------------------------------ declaring ---

    [Fact]
    public void Declares_EffModifier()
    {
        // Executed baseline X6a: on main this is a 14-line Calor0100/Calor0114
        // cascade starting "Expected Greater but found Identifier" — `eff e` is new
        // syntax (§7.2).
        var module = Parse("""
            §M{m001:X6}
              §F{f001:Map:pub}<T, U, eff e> (Func<i32,i32>:f §E{e}, i32:v) -> i32
                §E{e}
                §R INT:0
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);

        var function = Assert.Single(module.Functions);
        var binder = Assert.Single(function.EffectParameters);
        Assert.Equal("e", binder.Name);
        Assert.Equal(2, binder.Ordinal);

        // The binder is NOT a type parameter: it must be erased at codegen
        // (G-CODEGEN, §12.2), and keeping it out of the list is how that is achieved.
        Assert.Equal(["T", "U"], function.TypeParameters.Select(tp => tp.Name));
    }

    [Fact]
    public void TypeParamNamedEff_StillWorks()
    {
        // §14 Q2, the one v3 decision that rested on reasoning rather than execution.
        // Z4 (`§F{f001:M:pub}<eff> (eff:x) -> void`) compiles on main; the one-token
        // lookahead in ParseOptionalTypeParameterList must keep it compiling.
        var module = Parse("""
            §M{m001:Z4}
              §F{f001:M:pub}<eff> (eff:x) -> void
                §E{}
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);

        var function = Assert.Single(module.Functions);
        Assert.Equal("eff", Assert.Single(function.TypeParameters).Name);
        Assert.Empty(function.EffectParameters);
    }

    [Fact]
    public void EffectVariableNamedLikeACode_IsCalor0404()
    {
        // §7.2(c): variables resolve before the taxonomy, so `eff cw` would make the
        // console-write code unwritable inside this declaration.
        Parse("""
            §M{m001:N}
              §F{f001:M:pub}<T, eff cw> (T:a) -> void
                §E{}
            """, out var diagnostics);

        var error = Assert.Single(ScopeErrors(diagnostics));
        Assert.Contains("named after the effect code 'cw'", error.Message);
    }

    [Theory]
    // Z6 and Z6b: an ordinary type parameter named after a code compiles on main and
    // must keep compiling — the ban is on `eff` names only, and both polarities are
    // pinned so the ban cannot quietly widen.
    [InlineData("cw")]
    [InlineData("fs")]
    public void OrdinaryTypeParameterNamedLikeACode_StaysGreen(string name)
    {
        var source = $$"""
            §M{m001:Z6}
              §F{f001:M:pub}<T, {{name}}> (T:a, {{name}}:b) -> void
                §E{}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(["T", name], Assert.Single(module.Functions).TypeParameters.Select(tp => tp.Name));
    }

    [Fact]
    public void MemberLevelEffOnInterfaceMember_Parses()
    {
        // The spelling the emitter spike chose (spike-verdict.json
        // a3MiddlewareSpelling: MEMBER-LEVEL), anchored on W1a/W1b. It is position 1,
        // which §7.3 already permits, which is why §9's seventh insertion point
        // stayed at zero cost.
        var module = Parse("""
            §M{m001:W1}
              §IFACE{i001:IH}
                §MT{mt001:Handle}<eff e> (Func<i32>:next §E{e}) -> i32
                  §E{e}
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);

        var member = Assert.Single(Assert.Single(module.Interfaces).Methods);
        Assert.Equal("e", Assert.Single(member.EffectParameters).Name);
        Assert.Equal(["e"], member.Effects!.EffectVariables);
        Assert.Equal(["e"], Assert.Single(member.Parameters).Row!.EffectVariables);
    }

    // -------------------------------------------------------------- in-scope ---

    [Fact]
    public void InScope_DoesNotRaise0403()
    {
        // §7.2(b): the variable resolves BEFORE EffectCodes.TryParseCompact, so a
        // bound name never reaches the taxonomy lookup.
        var module = Parse("""
            §M{m001:S}
              §F{f001:M:pub}<eff e> (Func<i32>:g §E{e}) -> i32
                §E{cw, e}
                §R INT:0
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.UnknownEffectCode);

        // MixedRow_IsJoin, parse side: a row may carry both a concrete code and a
        // variable, and they are stored apart.
        var effects = Assert.Single(module.Functions).Effects!;
        Assert.Contains("io", effects.Effects.Keys);
        Assert.Equal(["e"], effects.EffectVariables);
    }

    [Fact]
    public void OutOfScope_KeepsTodaysCalor0403_AndIsE3sToMove()
    {
        // Executed baseline X5b. Routing an UNBOUND name to Calor0404 needs the
        // binder, which this slice does not have — §7.2(b)'s second half is E3's.
        // Pinned so the boundary is observed rather than assumed.
        Parse("""
            §M{m001:X5}
              §F{f001:M:pub} () -> void
                §E{e}
            """, out var diagnostics);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UnknownEffectCode);
        Assert.Empty(ScopeErrors(diagnostics));
    }

    [Fact]
    public void EffScope_DoesNotLeakToASiblingDeclaration()
    {
        // The spike prototype used a single mutable list on Parser reset by every
        // type-parameter list; spike-verdict.json calls that a shortcut E2 must
        // replace. A scope stack means 'e' is unknown in the sibling, not silently
        // still bound.
        var module = Parse("""
            §M{m001:S}
              §F{f001:Bound:pub}<eff e> (Func<i32>:g §E{e}) -> i32
                §E{e}
                §R INT:0
              §F{f002:Sibling:pub} () -> void
                §E{e}
            """, out var diagnostics);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UnknownEffectCode);
        Assert.Empty(module.Functions[1].Effects!.EffectVariables);
    }

    // ------------------------------------------------------------- forbidden ---

    [Theory]
    // §7.3's partition. Each case binds `eff e` on an enclosing declaration that MAY
    // bind it, then mentions it at a position that may not carry one.
    [InlineData("""
        §M{m001:R}
          §F{f001:M:pub}<eff e> (Func<i32>:g §E{e}) -> Func<i32> §E{e}
            §E{e}
            §R g
        """, "a return row")]
    [InlineData("""
        §M{m001:R}
          §F{f001:M:pub}<eff e> (Func<i32>:g §E{e}) -> i32
            §E{e}
            §B{h:Func<i32>} §E{e} g
            §R INT:0
        """, "a binding's row")]
    [InlineData("""
        §M{m001:R}
          §F{f001:M:pub}<eff e> (Func<i32>:g §E{e}) -> i32
            §E{e}
            §B{f} §LAM{lam1:x:i32} §E{e} x §/LAM{lam1}
            §R INT:0
        """, "a lambda literal's row")]
    public void Rejected_AtAForbiddenPosition_IsCalor0404(string source, string expectedReason)
    {
        Parse(source, out var diagnostics);

        var error = Assert.Single(ScopeErrors(diagnostics));
        Assert.Contains("Effect variable 'e' cannot be used in", error.Message);
        Assert.Contains(expectedReason, error.Message);
    }

    // §7.3 forbids effect variables at positions 8 (field) and 3 (delegate) too, but
    // neither is REACHABLE with a bound variable in 0.15, so neither can raise
    // Calor0404 — and pretending otherwise would be a test asserting a code path that
    // does not exist. A field is not lexically inside the member that could bind one,
    // and a §DEL has no type-parameter list at all (executed baseline Z8) and is a
    // sibling of every declaration that has one. Both are therefore refused by the
    // taxonomy, as Calor0403. The rejection is what §7.3 needs; the code is not the
    // one P18 names, and these two names say so.

    [Fact]
    public void Rejected_InFieldRow_TheNameIsNotBound()
    {
        Parse("""
            §M{m001:R}
              §CL{c001:C:pub}
                §FLD{Action<i32>:onChange:pri} §E{e}
                §MT{mt001:M:pub}<eff e> (Func<i32>:g §E{e}) -> void
                  §E{e}
            """, out var diagnostics);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UnknownEffectCode);
        Assert.Empty(ScopeErrors(diagnostics));
    }

    [Fact]
    public void Rejected_InDelegateRow_TheNameIsNotBound()
    {
        Parse("""
            §M{m001:R}
              §DEL{d001:H}
                §I{i32:x}
                §O{void}
                §E{e}
              §F{f001:Main:pub} () -> void
                §E{}
            """, out var diagnostics);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UnknownEffectCode);
        Assert.Empty(ScopeErrors(diagnostics));
    }

    [Theory]
    [InlineData("""
        §M{m001:R}
          §CL{c001:C:pub}<T, eff e>
            §MT{mt001:M:pub} () -> void
              §E{}
        """, "a class")]
    [InlineData("""
        §M{m001:R}
          §IFACE{i001:I}<T, eff e>
            §MT{mt001:M} () -> void
              §E{}
        """, "an interface")]
    public void Rejected_ClassOrInterfaceLevel_IsCalor0404(string source, string owner)
    {
        // §7.3's last row, CONFIRMED by the spike: class/interface-level `eff` stays
        // forbidden in 0.15 because member-level expressed R2. The message points at
        // the member-level spelling that does work.
        Parse(source, out var diagnostics);

        var error = Assert.Single(ScopeErrors(diagnostics));
        Assert.Contains($"cannot be declared on {owner}", error.Message);
        Assert.Contains("§MT{mt001:Handle:pub}<eff e>", error.Message);
    }

    [Fact]
    public void Rejected_InGenericArgument_ProducesNoRow()
    {
        // §7.3's generic-argument cell. Inline types are read as STRINGS
        // (ReadInlineTypeToken), so a §E inside `<…>` never reaches a row parse at
        // all. The honest pin is that no row is produced — not a Calor0404 the
        // compiler has no way to reach.
        var module = Parse("""
            §M{m001:R}
              §F{f001:M:pub}<eff e> (List<Func<i32,i32>>:fs §E{e}) -> i32
                §E{e}
                §R INT:0
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var parameter = Assert.Single(Assert.Single(module.Functions).Parameters);
        Assert.Equal("List<Func<i32,i32>>", parameter.TypeName);
        Assert.NotNull(parameter.Row);
    }
}
