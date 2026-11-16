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
            module X6 X6 block
              func Map <T,U,! e> (Func<int, int>:f ! e, int:v) → int
                ! e
                return 0
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
        // Z4 (`§F{M:pub}<eff> (eff:x) -> void`) compiles on main; the one-token
        // lookahead in ParseOptionalTypeParameterList must keep it compiling.
        var module = Parse("""
            module Z4 Z4 block
              func M<eff> (eff:x) → void
                !
                return
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);

        var function = Assert.Single(module.Functions);
        Assert.Equal("eff", Assert.Single(function.TypeParameters).Name);
        Assert.Empty(function.EffectParameters);
    }

    [Fact]
    public void MemberLevelEffOnInterfaceMember_Parses()
    {
        // The spelling the emitter spike chose (spike-verdict.json
        // a3MiddlewareSpelling: MEMBER-LEVEL), anchored on W1a/W1b. It is position 1,
        // which §7.3 already permits, which is why §9's seventh insertion point
        // stayed at zero cost.
        var module = Parse("""
            module W1 W1 block
              interface IH
                func Handle <! e> (Func<int>:next ! e) → int
                  ! e
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
            module S S block
              func M <! e> (Func<int>:g ! e) → int
                ! cw, e
                return 0
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
    public void Rejected_InGenericArgument_ProducesNoRow()
    {
        // §7.3's generic-argument cell. Inline types are read as STRINGS
        // (ReadInlineTypeToken), so a §E inside `<…>` never reaches a row parse at
        // all. The honest pin is that no row is produced — not a Calor0404 the
        // compiler has no way to reach.
        var module = Parse("""
            module R R block
              func M <! e> (List<Func<int, int>>:fs ! e) → int
                ! e
                return 0
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
            module Alpha Alpha block
              func A <! e> (Func<int>:f ! e) → int
                ! e
                return 0

              func B <! zzz> (Func<int>:f ! zzz) → int
                ! zzz
                return 0
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
            module MiddlewareAlpha MiddlewareAlpha block
              interface IPipelineBehavior
                func Handle <! e> (int:request, Func<int>:next ! e) → int
                  ! e

              class PassThroughBehavior
                implements IPipelineBehavior

                func Handle <! f> (int:request, Func<int>:next ! f) → int
                  ! f
                  return next()
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
            module Mixed Mixed block
              func A <T,! e> (Func<int>:f ! e) → int
                ! e
                return 0

              func B <! e> (Func<int>:f ! e) → int
                ! e
                return 0
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
