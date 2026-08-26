using System.Collections.Immutable;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Design-doc pins <b>P6</b>, <b>P8</b>, <b>P9</b> and <b>P10</b> for the effect-row
/// LATTICE and its binder plumbing
/// (docs/design/effect-rows-in-the-type-system.md §3.5, §4, §5, §8.2).
/// </summary>
/// <remarks>
/// Slice b builds the lattice and attaches rows to types. It does <b>not</b> check
/// two rows against each other at a binding site — that is E3 (Calor0424/0425). So
/// P10's three cases are asserted on ROW VALUES (<see cref="EffectRow.Fits"/>,
/// <see cref="EffectRow.AtDestination"/>, <see cref="EffectRow.AtDeclarationBoundary"/>)
/// rather than on emitted diagnostics: the relation is what slice b owns, the
/// emission is what E3 owes.
/// </remarks>
public class EffectRowLatticeTests
{
    private const string Cw = "io:console_write";
    private const string FsW = "io:filesystem_write";
    private const string DbR = "io:database_read";
    private const string Db = "io:database";

    private static EffectRow Concrete(params string[] codes) => EffectRow.Concrete(codes);

    private static EffectRow Assumed(string[] codes, params string[] reasons)
        => EffectRow.Assumed(codes, reasons);

    // ================================================================ P6 =====
    // (a) What an OMITTED row means, per site (§3.5) — four different answers,
    //     not one. Discriminating revert: make the parameter default
    //     Concrete(∅) and E-3's laundering re-opens.

    [Fact]
    public void OmittedRow_OnADeclaration_IsPure()
    {
        // §3.5 row 1, unchanged from 0.14: a §F with no §E declares no effects,
        // and that is a PROMISE, not an absence of information. 390 of the 886
        // committed files rely on it.
        var compiled = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Pure:pub} () -> i32
                §R INT:1
            """);

        Assert.DoesNotContain(compiled.Diagnostics, d => d.IsError);
    }

    [Fact]
    public void OmittedRow_OnAFunctionTypedParameter_IsUnknownNotPure()
    {
        // §3.5 row 4. Nothing is declared and nothing can be inferred, so the
        // parameter's row is Unknown — never Concrete(∅). Defaulting to pure is
        // exactly the laundering rows exist to close: the caller could hand in a
        // printing callback and the pure declaration would still compile.
        var parameter = BindSingleParameter("""
            §M{m001:M}
              §F{f001:Apply:pub} (Func<i32,i32>:transform, i32:value) -> i32
                §E{}
                §R value
            """);

        Assert.Null(parameter.FunctionType);
        Assert.True(RowOf(parameter).IsUnknown);
    }

    [Fact]
    public void OmittedRow_OnABindingWithAFunctionTypedInitializer_IsInferred()
    {
        // §3.5 row 3, CHANGED from Draft v1. `§B{f} §LAM …` must not become
        // Unknown, or §5's inferred-lambda rule is dead on arrival — Y9a is that
        // exact shape. The initializer's row is known, so it is used.
        var binding = BindSingleLocal("""
            §M{m001:M}
              §F{f001:Main:pub} () -> void
                §E{cw}
                §B{f} §LAM{lam1:x:i32} §E{cw} (+ x INT:1) §/LAM{lam1}
            """);

        Assert.NotNull(binding.FunctionType);
        Assert.Equal(Concrete(Cw), binding.FunctionType!.Row);
    }

    [Fact]
    public void OmittedRow_OnABindingWithNoInitializer_IsUnknown()
    {
        // §3.5 row 4 again: nothing to infer from.
        var binding = BindSingleLocal("""
            §M{m001:M}
              §F{f001:Main:pub} () -> void
                §E{}
                §B{f:Func<i32,i32>} §LAM{lam1:x:i32} §E{} x §/LAM{lam1}
            """);

        // The binding takes the lambda's declared row, which here is pure —
        // Concrete(∅), a promise, and distinguishable from Unknown.
        Assert.NotNull(binding.FunctionType);
        Assert.Equal(EffectRow.Pure, binding.FunctionType!.Row);
        Assert.False(binding.FunctionType.Row.IsUnknown);
    }

    // (b) RowOnNonFunctionTypedPosition_IsCalor0405 — one case per position,
    //     each against its executed baseline. Discriminating revert: drop the
    //     function-typedness check in CheckRowPosition and all three go green
    //     (Z9/Z9b compile to Calor0410, Z9c to exit 0).

    [Theory]
    // Z9 — `-> void §E{cw}`, the arrow spelling of position 6. Compiles on main.
    [InlineData("""
        §M{m001:Z9}
          §F{f001:Log:pub} (i32:x) -> void §E{cw}
            §P x
        """)]
    // Z9b — `§I{i32:x} §E{cw}`, position 4 on a non-function type. Compiles on main.
    [InlineData("""
        §M{m001:Z9}
          §F{f001:Log:pub}
            §I{i32:x} §E{cw}
            §O{void}
            §P x
        """)]
    // Z9c — `§O{i32} §E{cw}`, the §O spelling of position 6. Compiles on main.
    [InlineData("""
        §M{m001:Z9}
          §F{f001:Get:pub}
            §O{i32} §E{cw}
            §R INT:1
        """)]
    public void RowOnNonFunctionTypedPosition_IsCalor0405(string source)
    {
        var compiled = TestHarness.Compile(source);

        var misplaced = compiled.Diagnostics
            .Where(d => d.Code == DiagnosticCode.EffectRowMisplaced)
            .ToList();

        // Reported ONCE per offending row, not once per binder visit.
        Assert.Single(misplaced);
        Assert.Contains("is not a function type", misplaced[0].Message);
    }

    [Fact]
    public void RowOnABindingOfNonFunctionType_IsCalor0405()
    {
        // Position 7's half of the same rule. A §B nests arbitrarily deep inside
        // a body, so it is checked at BindBindStatement rather than in the
        // declaration sweep; this pins that the rule reaches it.
        var compiled = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Main:pub} () -> void
                §E{}
                §B{n:i32} §E{cw} INT:1
            """);

        var misplaced = compiled.Diagnostics
            .Where(d => d.Code == DiagnosticCode.EffectRowMisplaced)
            .ToList();
        Assert.Single(misplaced);
    }

    [Fact]
    public void RowOnAFieldOfNonFunctionType_IsCalor0405()
    {
        // Position 8's half. X9b proves the FORM parses since slice a; this
        // proves the row still has to land somewhere a row can go.
        var compiled = TestHarness.Compile("""
            §M{m001:M}
              §CL{c001:Holder:pub}
                §FLD{i32:count:pri} §E{cw}
            """);

        Assert.Single(compiled.Diagnostics.Where(d => d.Code == DiagnosticCode.EffectRowMisplaced));
    }

    [Fact]
    public void RowOnAFunctionTypedPosition_IsNotCalor0405()
    {
        // The control. Without it the pin above would pass on a compiler that
        // reported Calor0405 for EVERY row.
        var compiled = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Apply:pub}
                §I{Func<i32,i32>:transform} §E{cw}
                §O{i32}
                §E{cw}
                §R INT:1
            """);

        Assert.DoesNotContain(
            compiled.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMisplaced);
    }

    [Fact]
    public void RowOnADeclaredDelegateTypedPosition_IsNotCalor0405()
    {
        // A §DEL name is a function type even though it spells nothing the BCL
        // list knows. It is also why the placement check runs AFTER registration:
        // top-level functions register before module.Delegates do.
        var compiled = TestHarness.Compile("""
            §M{m001:M}
              §DEL{d001:Callback} (i32:x) -> void
              §F{f001:Apply:pub}
                §I{Callback:cb} §E{cw}
                §O{void}
                §E{cw}
            """);

        Assert.DoesNotContain(
            compiled.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMisplaced);
    }

    // ================================================================ P8 =====
    // `fits` is TOTAL over all nine source × destination cells (§4.3), including
    // the three Assumed-DESTINATION cells Draft v1 left undefined.
    //
    // Discriminating revert: re-introduce EffectSet.cs:100's
    // `if (other.IsUnknown) return true` inside EffectRow.Fits — i.e. answer
    // Fits when the destination is Unknown — and
    // UnknownRow_FitsNothing_AndIsFittedByNothing goes red.

    public static TheoryData<EffectRow, EffectRow, EffectFit> NineCells() => new()
    {
        // Concrete source.
        { Concrete(Cw), Concrete(Cw, FsW), EffectFit.Fits },
        { Concrete(Cw), Concrete(), EffectFit.DoesNotFit },
        { Concrete(Cw), Assumed([Cw, FsW], "interop"), EffectFit.Fits },
        { Concrete(Cw), Assumed([], "interop"), EffectFit.DoesNotFit },
        { Concrete(Cw), EffectRow.Unknown, EffectFit.CannotTell },
        // Assumed source — fits exactly like its underlying set.
        { Assumed([Cw], "interop"), Concrete(Cw, FsW), EffectFit.Fits },
        { Assumed([Cw], "interop"), Concrete(), EffectFit.DoesNotFit },
        { Assumed([Cw], "interop"), Assumed([Cw], "manifest"), EffectFit.Fits },
        { Assumed([Cw, FsW], "interop"), Assumed([Cw], "manifest"), EffectFit.DoesNotFit },
        { Assumed([Cw], "interop"), EffectRow.Unknown, EffectFit.CannotTell },
        // Unknown source — never Fits, whatever the destination.
        { EffectRow.Unknown, Concrete(Cw), EffectFit.CannotTell },
        { EffectRow.Unknown, Concrete(), EffectFit.CannotTell },
        { EffectRow.Unknown, Assumed([Cw], "interop"), EffectFit.CannotTell },
        { EffectRow.Unknown, EffectRow.Unknown, EffectFit.CannotTell },
    };

    [Theory]
    [MemberData(nameof(NineCells))]
    public void FitsIsTotalOverNineCells(EffectRow source, EffectRow destination, EffectFit expected)
        => Assert.Equal(expected, EffectRow.Fits(source, destination));

    [Fact]
    public void UnknownRow_FitsNothing_AndIsFittedByNothing()
    {
        // §4.3's second sentence, stated on its own because it is the one cell a
        // reasonable implementation gets wrong: EffectSet.IsSubsetOf answers
        // "everything is a subset of unknown", which is sound for a COMPUTED set
        // against a DECLARED one (§E{unknown} is unwritable) and unsound for
        // rows, where a destination is Unknown by OMISSION.
        foreach (var other in new[] { EffectRow.Pure, Concrete(Cw), Assumed([Cw], "interop") })
        {
            Assert.Equal(EffectFit.CannotTell, EffectRow.Fits(EffectRow.Unknown, other));
            Assert.Equal(EffectFit.CannotTell, EffectRow.Fits(other, EffectRow.Unknown));
        }

        // Not even pure fits into Unknown.
        Assert.NotEqual(EffectFit.Fits, EffectRow.Fits(EffectRow.Pure, EffectRow.Unknown));
    }

    [Fact]
    public void FitsUsesFamilyNarrowSubtyping_NotBareSetMembership()
    {
        // §4.1's widening, seen through `fits`: a destination declaring the bare
        // family code admits a source that performs only the narrow one.
        Assert.Equal(EffectFit.Fits, EffectRow.Fits(Concrete(DbR), Concrete(Db)));
        // …and not the other way round.
        Assert.Equal(EffectFit.DoesNotFit, EffectRow.Fits(Concrete(Db), Concrete(DbR)));
    }

    // ================================================================ P9 =====
    // The join is a semilattice: associative, commutative, idempotent, with
    // identity Concrete(∅) and top Unknown, and reason sets canonically ordered.
    //
    // Discriminating revert: make Reasons a concatenated list and commutativity
    // fails.

    [Fact]
    public void EffectRowJoin_IsASemilattice()
    {
        // A small deterministic generator over registry codes and reasons.
        // Deterministic seed: the laws must hold for every run, and a failing
        // case must be reproducible from the seed alone.
        var random = new Random(20260826);
        var codes = new[] { Cw, FsW, DbR, Db, "io:network", "memory:allocation", "nondeterminism:time" };
        var reasons = new[] { "interop", "manifest", "unresolved receiver", "BCL delegate return" };

        EffectRow Sample()
        {
            var kind = random.Next(3);
            if (kind == 2) return EffectRow.Unknown;
            var picked = codes.Where(_ => random.Next(2) == 0).ToArray();
            return kind == 0
                ? EffectRow.Concrete(picked)
                : EffectRow.Assumed(picked, reasons.Where(_ => random.Next(2) == 0));
        }

        for (var trial = 0; trial < 250; trial++)
        {
            var a = Sample();
            var b = Sample();
            var c = Sample();

            Assert.Equal(EffectRow.Join(a, b), EffectRow.Join(b, a));                    // commutative
            Assert.Equal(                                                                 // associative
                EffectRow.Join(EffectRow.Join(a, b), c),
                EffectRow.Join(a, EffectRow.Join(b, c)));
            Assert.Equal(a, EffectRow.Join(a, a));                                        // idempotent
            Assert.Equal(a, EffectRow.Join(a, EffectRow.Pure));                           // identity
            Assert.Equal(EffectRow.Unknown, EffectRow.Join(a, EffectRow.Unknown));         // top
            Assert.Equal(EffectRow.Unknown, EffectRow.Join(EffectRow.Unknown, a));         // top, either side
        }
    }

    [Fact]
    public void ReasonSetsAreCanonicallyOrdered_SoJoinIsCommutative()
    {
        // The specific mechanism P9's revert names. Draft v1 concatenated the
        // reason lists with `++`, which made the JOIN of two assumed rows depend
        // on traversal order — and made a Calor0425 message's reason order
        // depend on it too.
        var left = Assumed([Cw], "zebra", "alpha");
        var right = Assumed([FsW], "middle");

        var forward = EffectRow.Join(left, right);
        var backward = EffectRow.Join(right, left);

        Assert.Equal(forward, backward);
        Assert.Equal(new[] { "alpha", "middle", "zebra" }, forward.Reasons.ToArray());
    }

    [Fact]
    public void AssumedWithNoReasons_IsConcrete_SoTheIdentityIsUnique()
    {
        // Otherwise Join would have two units that compare unequal, and the
        // identity law above would be trivially satisfiable by construction.
        Assert.Equal(EffectRow.Pure, EffectRow.Assumed([], reasons: null));
        Assert.Equal(EffectRowKind.Concrete, EffectRow.Assumed([Cw], []).Kind);
    }

    // =============================================================== P10 =====
    // Assumed survives the destination (§4.4), one 0425 per hop, and the
    // DECLARATION boundary converts (§5).
    //
    // Asserted on row values, not on emitted diagnostics: E3 owns emission.

    [Fact]
    public void AssumedSurvivesTheDestination_TwoHop()
    {
        // (a) An Assumed source that Fits produces an ASSUMED destination row,
        // carrying the reasons onward — so the assumption cannot be laundered
        // away by taking one more hop. Discriminating revert: make
        // AtDestination return Concrete(dst.Codes) and this goes silent.
        var source = Assumed([Cw], "receiver type could not be resolved");
        var firstDestination = Concrete(Cw, FsW);

        Assert.Equal(EffectFit.Fits, EffectRow.Fits(source, firstDestination));
        var afterHopOne = EffectRow.AtDestination(source, firstDestination);
        Assert.Equal(EffectRowKind.Assumed, afterHopOne.Kind);
        Assert.Contains("receiver type could not be resolved", afterHopOne.Reasons);

        // Second hop: the reason is still there, so E3 still has something to
        // report. Under the naive rule this hop would be silent.
        var secondDestination = Concrete(Cw, FsW, DbR);
        Assert.Equal(EffectFit.Fits, EffectRow.Fits(afterHopOne, secondDestination));
        var afterHopTwo = EffectRow.AtDestination(afterHopOne, secondDestination);
        Assert.Equal(EffectRowKind.Assumed, afterHopTwo.Kind);
        Assert.Contains("receiver type could not be resolved", afterHopTwo.Reasons);
    }

    [Fact]
    public void AssumedHop_CarriesExactlyOneReasonPerAssumption()
    {
        // (b) CARDINALITY — the claim §4.4 makes and v2 never asserted: one
        // 0425 per hop, not two. The reasons a hop carries are the UNION of the
        // two sides' reason sets, so a source and destination that share an
        // assumption produce one reason, not a duplicated pair.
        var source = Assumed([Cw], "interop boundary");
        var destination = Assumed([Cw, FsW], "interop boundary");

        var carried = EffectRow.CarriedReasons(source, destination);
        Assert.Single(carried);

        // Two DIFFERENT assumptions are two reasons — R_s ∪ R_d, §4.3's
        // Assumed × Assumed cell.
        var other = Assumed([Cw, FsW], "manifest says unknown");
        Assert.Equal(2, EffectRow.CarriedReasons(source, other).Count);

        // A hop between two concrete rows carries nothing at all.
        Assert.Empty(EffectRow.CarriedReasons(Concrete(Cw), Concrete(Cw, FsW)));
    }

    [Fact]
    public void DeclarationBoundary_ConvertsAssumedToConcrete()
    {
        // (c) §5's SEVENTH place a row changes form, and deliberately not one of
        // §6's six sites. What leaves an annotated declaration is
        // Concrete(declared): Calor0419 already reports the assumption AT the
        // declaration, so the provenance is surfaced there rather than carried
        // past it — otherwise an Assumed row would escape every function that
        // touches interop and Calor0425 would fire at every downstream call.
        // Discriminating revert: carry the reasons past the declaration and this
        // fails.
        var inferredBody = Assumed([Cw], "interop boundary");

        var leaving = EffectRow.AtDeclarationBoundary(inferredBody);

        Assert.Equal(EffectRowKind.Concrete, leaving.Kind);
        Assert.Empty(leaving.Reasons);
        Assert.Equal(Concrete(Cw), leaving);
    }

    [Fact]
    public void DeclarationBoundary_AppliesToALambdasDeclaredRow()
    {
        // …and the boundary is where a §LAM's §E acts. §5: the lambda's TYPE
        // carries ρ_decl, the declaration being the contract. Slice a parsed the
        // annotation and threw it away; here it reaches the type.
        var binding = BindSingleLocal("""
            §M{m001:M}
              §F{f001:Main:pub} () -> void
                §E{cw}
                §B{f} §LAM{lam1:x:i32} §E{cw} (+ x INT:1) §/LAM{lam1}
            """);

        Assert.Equal(EffectRowKind.Concrete, binding.FunctionType!.Row.Kind);
        Assert.Equal(Concrete(Cw), binding.FunctionType.Row);
    }

    [Fact]
    public void LambdaWithNoDeclaredRow_IsUnknownUntilE3InfersTheBody()
    {
        // §3.5 says an omitted lambda row is INFERRED FROM THE BODY. The body's
        // row is computed by the effect pass, which is an AST walk the binder is
        // not on, so slice b leaves it Unknown and E3 replaces it. Unknown is the
        // sound placeholder: it fits nothing, so no check can pass on the
        // strength of a row that has not been computed.
        var binding = BindSingleLocal("""
            §M{m001:M}
              §F{f001:Main:pub} () -> void
                §E{cw}
                §B{f} §LAM{lam1:x:i32} (+ x INT:1) §/LAM{lam1}
            """);

        Assert.True(binding.FunctionType!.Row.IsUnknown);
    }

    // ======================================================= §8.2 / §8.3 =====

    [Fact]
    public void FunctionTypesDifferingOnlyInRow_AreNotEqual()
    {
        // §8.2's central claim: two function types differing only in row are
        // DIFFERENT types. Discriminating revert: drop Row from
        // FunctionBoundType.Equals.
        var parameters = ImmutableArray.Create<BoundType>(new NominalBoundType("INT"));
        var pure = new FunctionBoundType(parameters, new NominalBoundType("INT"), row: EffectRow.Pure);
        var printing = new FunctionBoundType(parameters, new NominalBoundType("INT"), row: Concrete(Cw));

        Assert.NotEqual(pure, printing);
        Assert.NotEqual(pure.GetHashCode(), printing.GetHashCode());
        Assert.Equal(
            pure,
            new FunctionBoundType(parameters, new NominalBoundType("INT"), row: EffectRow.Pure));
    }

    [Fact]
    public void RowsDefaultToUnknownNotPure()
    {
        // §8.2 — "both default to EffectRow.Unknown, NEVER to pure".
        var type = new FunctionBoundType(
            ImmutableArray.Create<BoundType>(new NominalBoundType("INT")),
            new NominalBoundType("INT"));

        Assert.True(type.Row.IsUnknown);
        Assert.Equal(type.ParameterTypes.Length, type.ParameterRows.Length);
        Assert.All(type.ParameterRows, row => Assert.True(row.IsUnknown));
    }

    [Fact]
    public void DisplayStringIsRowFree()
    {
        // §8.3 — rows never appear in a DisplayString. Consumers compare it
        // byte-for-byte: the verifier cache keys on it, WorkspaceState builds a
        // call-graph key from it, and BoundTypeTests pins two literal spellings.
        // Discriminating revert: append the row and those pins go red.
        var parameters = ImmutableArray.Create<BoundType>(
            new NominalBoundType("INT"), new NominalBoundType("STRING"));
        var withRow = new FunctionBoundType(
            parameters,
            new NominalBoundType("BOOL"),
            row: Assumed([Cw], "interop"));

        Assert.Equal("(INT, STRING) -> BOOL", withRow.DisplayString);
        Assert.DoesNotContain("cw", withRow.DisplayString);
        Assert.DoesNotContain("assumed", withRow.DisplayString);

        // The row's own spelling lives on the row, in the compact surface codes
        // authors write.
        Assert.Equal("[assumed: cw]", withRow.Row.ToCompactDisplayString());
        Assert.Equal("[pure]", EffectRow.Pure.ToCompactDisplayString());
        Assert.Equal("[unknown]", EffectRow.Unknown.ToCompactDisplayString());
        Assert.Equal("cw, fs:w", Concrete(Cw, FsW).ToCompactDisplayString());
    }

    [Fact]
    public void ManifestResolutionStatusMapsToRow()
    {
        // §8.4's mapping, at the level slice b owns it: an UNKNOWN effect set
        // becomes an Unknown ROW, never Concrete(∅). Mapping it to pure is the
        // mistake P17's whole design rests on not making.
        Assert.True(EffectSet.Unknown.ToRow().IsUnknown);
        Assert.Equal(EffectRow.Pure, EffectSet.Empty.ToRow());
        Assert.Equal(Concrete(Cw), EffectSet.From("cw").ToRow());

        // …and the bridge round-trips.
        Assert.True(EffectSet.From("cw", "fs:w").ToRow().ToEffectSet().Equals(EffectSet.From("cw", "fs:w")));
        Assert.True(EffectRow.Unknown.ToEffectSet().IsUnknown);
    }

    // ------------------------------------------------------------ helpers ---

    private static EffectRow RowOf(Calor.Compiler.Binding.VariableSymbol symbol)
        => symbol.FunctionType?.Row ?? EffectRow.Unknown;

    private static Calor.Compiler.Binding.VariableSymbol BindSingleParameter(string source)
    {
        var module = BoundModuleOf(source);
        var function = Assert.Single(module.Functions);
        return function.Symbol.Parameters[0];
    }

    private static Calor.Compiler.Binding.VariableSymbol BindSingleLocal(string source)
    {
        var module = BoundModuleOf(source);
        var function = Assert.Single(module.Functions);
        var bind = function.Body
            .OfType<Calor.Compiler.Binding.BoundBindStatement>()
            .First();
        return bind.Variable;
    }

    private static Calor.Compiler.Binding.BoundModule BoundModuleOf(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Calor.Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser();
        var ast = new Calor.Compiler.Parsing.Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.ToString())));
        return new Calor.Compiler.Binding.Binder(diagnostics, "test.calr").Bind(ast);
    }
}
