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

    // -------------------------------------------------- P18, ordinal cases ---

    [Fact]
    public void EffVariableOrdinal_AlphaEquivalent()
    {
        // v0.15 E3 slice b. The IDENTITY of an effect variable is its position in
        // its declaration's `eff` list, not its spelling: two declarations that
        // differ only in what they call the binder produce the same ordinals.
        // This is what makes §7.5's R2 hold without a rank-1-specific branch in
        // CheckEffectVariance.
        //
        // Discriminating revert: stop persisting the ordinal (return the parser
        // to slice a's boolean `IsEffectVariableInScope`) and both rows read -1,
        // which unifies with nothing.
        var module = Parse("""
            §M{m001:Alpha}
              §F{f001:A:pub}<eff e> (Func<i32>:f §E{e}) -> i32
                §E{e}
                §R INT:0
              §F{f002:B:pub}<eff zzz> (Func<i32>:f §E{zzz}) -> i32
                §E{zzz}
                §R INT:0
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var a = module.Functions[0];
        var b = module.Functions[1];

        Assert.Equal(new[] { "e" }, a.Effects!.EffectVariables);
        Assert.Equal(new[] { "zzz" }, b.Effects!.EffectVariables);
        // The names differ; the ordinals do not.
        Assert.Equal(a.Effects!.EffectVariableOrdinals, b.Effects!.EffectVariableOrdinals);
        Assert.Equal(new[] { 0 }, a.Effects!.EffectVariableOrdinals);
        Assert.Equal(
            a.Parameters[0].Row!.EffectVariableOrdinals,
            b.Parameters[0].Row!.EffectVariableOrdinals);
    }

    [Fact]
    public void EffVariableOrdinal_UnifiesAcrossInterfaceAndImpl()
    {
        // A3-middleware-alpha's shape, at the AST level: the interface member
        // binds `eff e`, the implementation binds `eff f`, and site 5 must
        // identify them. It can, because both are ordinal 0.
        //
        // The behavioural half of this pin is
        // SpikeVerdictTests.A3Fixtures_AreExactlyZeroCalor0418_PostE4,
        // which compiles the frozen fixture and asserts zero Calor0421.
        var module = Parse("""
            §M{m001:MiddlewareAlpha}
              §IFACE{i001:IPipelineBehavior}
                §MT{mt001:Handle}<eff e> (i32:request, Func<i32>:next §E{e}) -> i32
                  §E{e}

              §CL{c001:PassThroughBehavior:pub}
                §IMPL{IPipelineBehavior}
                §MT{mt002:Handle:pub}<eff f> (i32:request, Func<i32>:next §E{f}) -> i32
                  §E{f}
                  §R §C{next} §/C
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var signature = module.Interfaces[0].Methods[0];
        var implementation = module.Classes[0].Methods[0];

        Assert.Equal(new[] { "e" }, signature.Effects!.EffectVariables);
        Assert.Equal(new[] { "f" }, implementation.Effects!.EffectVariables);
        Assert.Equal(new[] { 0 }, signature.Effects!.EffectVariableOrdinals);
        Assert.Equal(new[] { 0 }, implementation.Effects!.EffectVariableOrdinals);
        Assert.Equal(new[] { 0 }, signature.Parameters[1].Row!.EffectVariableOrdinals);
        Assert.Equal(new[] { 0 }, implementation.Parameters[1].Row!.EffectVariableOrdinals);
    }

    [Fact]
    public void EffVariableOrdinal_IsRelativeToTheEffListNotTheTypeParameterList()
    {
        // The ordinal deliberately does NOT reuse EffectParameterInfo.Ordinal,
        // which is the emitter's interleaving position in the COMBINED list. If
        // it did, `<T, eff e>` (combined position 1) and `<eff e>` (position 0)
        // would stop unifying for a reason that has nothing to do with effects.
        var module = Parse("""
            §M{m001:Mixed}
              §F{f001:A:pub}<T, eff e> (Func<i32>:f §E{e}) -> i32
                §E{e}
                §R INT:0
              §F{f002:B:pub}<eff e> (Func<i32>:f §E{e}) -> i32
                §E{e}
                §R INT:0
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var withTypeParameter = module.Functions[0];
        var without = module.Functions[1];

        // The emitter's interleaving position differs...
        Assert.Equal(1, withTypeParameter.EffectParameters[0].Ordinal);
        Assert.Equal(0, without.EffectParameters[0].Ordinal);
        // ...and the ROW's ordinal, which is the identity, does not.
        Assert.Equal(new[] { 0 }, withTypeParameter.Effects!.EffectVariableOrdinals);
        Assert.Equal(new[] { 0 }, without.Effects!.EffectVariableOrdinals);
    }
}
