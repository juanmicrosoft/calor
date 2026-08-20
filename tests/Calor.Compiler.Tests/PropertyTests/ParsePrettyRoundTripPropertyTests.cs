using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Calor.Compiler.Tests.PropertyTests;

/// <summary>
/// Property-based smoke tests for parse -> pretty-print -> parse round-trip.
///
/// The property being asserted is the weakest useful one for a first cut:
/// if we take a hand-generated Calor source string that parses cleanly,
/// re-emit it through <see cref="CalorEmitter"/>, and parse the emitter's
/// output, the second parse must also succeed with no diagnostics of
/// severity Error and must produce a <see cref="ModuleNode"/> with the
/// same module name and same number of top-level functions.
///
/// Structural AST equality across the round-trip is a much stronger claim
/// that the emitter does not (yet) uphold in every corner (the emitter
/// hoists ternaries, elides some §/C closers, and rewrites certain call
/// argument shapes). Those refinements are tracked separately; this test
/// exists to catch regressions where the emitter produces text that the
/// lexer or parser will reject outright.
/// </summary>
public class ParsePrettyRoundTripPropertyTests
{
    #region Generators

    /// <summary>
    /// A tiny, tightly-bounded Calor-source generator: one module with one
    /// function containing between one and three §P print statements over
    /// integer, string, or boolean typed literals. Small on purpose — the
    /// point is a smoke test of the parser+emitter pipeline, not exhaustive
    /// AST coverage.
    /// </summary>
    public static Arbitrary<string> SmallCalorPrograms()
    {
        // Typed literals the P statement definitely accepts.
        var literals = new[]
        {
            "INT:0", "INT:1", "INT:42", "INT:-7",
            "STR:\"hello\"", "STR:\"world\"", "STR:\"\"",
            "BOOL:true", "BOOL:false",
        };

        var literalGen = Gen.Elements(literals);
        var statementGen = literalGen.Select(lit => $"    §P {lit}");
        var stmtCountGen = Gen.Choose(1, 3);
        var funcIdGen = Gen.Choose(1, 999).Select(n => $"f{n:D3}");
        var modIdGen = Gen.Choose(1, 999).Select(n => $"m{n:D3}");

        // Module name generator — restrict to a safe alphabet.
        var modNameChar = Gen.Elements("ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());
        var modNameTailChar = Gen.Elements(
            "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray());
        var modNameGen =
            from head in modNameChar
            from tailLen in Gen.Choose(2, 6)
            from tail in Gen.ArrayOf(tailLen, modNameTailChar)
            select head + new string(tail);

        return Arb.From(
            from modId in modIdGen
            from modName in modNameGen
            from funcId in funcIdGen
            from stmtCount in stmtCountGen
            from stmts in Gen.ArrayOf(stmtCount, statementGen)
            select
                $"§M{{{modId}:{modName}}}\n"
                + $"  §F{{{funcId}:Main:pub}} () -> void\n"
                + "    §E{cw}\n"
                + string.Join("\n", stmts)
                + "\n");
    }

    #endregion

    #region Helpers

    private static (ModuleNode? Module, DiagnosticBag Diagnostics) TryParse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        if (diagnostics.HasErrors) return (null, diagnostics);

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        return (module, diagnostics);
    }

    private static string PrettyPrint(ModuleNode module)
    {
        var emitter = new CalorEmitter();
        return emitter.Emit(module);
    }

    #endregion

    #region Sanity

    /// <summary>
    /// Sanity check that the generator's parseable-sample RATE is above a
    /// minimum threshold — otherwise the properties below "pass" vacuously
    /// by short-circuiting on parse failure. Adversarial review flagged
    /// that the previous check (`parseable > 0`) was satisfied by 1-of-30,
    /// which still allowed 96% of property samples to skip.
    /// </summary>
    [Fact]
    public void Generator_ProducesMostlyParseableInputs()
    {
        var arb = SmallCalorPrograms();
        var samples = Gen.Sample(20, 30, arb.Generator);
        var parseable = 0;
        foreach (var sample in samples)
        {
            var (module, diags) = TryParse(sample);
            if (module is not null && !diags.HasErrors) parseable++;
        }
        // Threshold is deliberately loose (half the samples). Tight enough
        // that a generator regression that mostly emits garbage fails here
        // instead of silently gutting the properties; loose enough to tolerate
        // parser quirks the generator can't anticipate.
        Assert.True(parseable >= samples.Count() / 2,
            $"generator produced only {parseable}/{samples.Count()} parseable samples — the properties below would pass vacuously");
    }

    #endregion

    #region Property Tests

    [Property(MaxTest = 50)]
    public Property Parse_Pretty_Parse_ProducesNoErrors()
    {
        // Property: for any source produced by our small generator, if the
        // first parse succeeds, the emitter's output must also parse without
        // errors. This is the minimum guarantee we ask of the pretty printer.
        return Prop.ForAll(SmallCalorPrograms(), source =>
        {
            var (firstModule, firstDiags) = TryParse(source);

            // If our generator produced something the parser rejects, we
            // skip that sample — the property only holds for parseable
            // inputs. Report skips as passes so FsCheck's sample count is
            // preserved. Generator_ProducesMostlyParseableInputs above
            // guards against the vacuous-mass-skip failure mode.
            if (firstModule is null || firstDiags.HasErrors) return true;

            var reemitted = PrettyPrint(firstModule);
            var (secondModule, secondDiags) = TryParse(reemitted);

            return secondModule is not null && !secondDiags.HasErrors;
        });
    }

    [Property(MaxTest = 50)]
    public Property Parse_Pretty_Parse_PreservesModuleShape()
    {
        // Property: the round-trip preserves module name, function count,
        // AND per-function body statement count. Node-for-node AST
        // equality is out of scope for this smoke test (see the class-level
        // comment). Body-count assertion added per adversarial review:
        // without it, an emitter regression that dropped every §P
        // statement, preserved the module and function shell, and returned
        // an empty body would still pass.
        return Prop.ForAll(SmallCalorPrograms(), source =>
        {
            var (firstModule, firstDiags) = TryParse(source);
            if (firstModule is null || firstDiags.HasErrors) return true;

            var reemitted = PrettyPrint(firstModule);
            var (secondModule, secondDiags) = TryParse(reemitted);
            if (secondModule is null || secondDiags.HasErrors) return false;

            if (!string.Equals(firstModule.Name, secondModule.Name, StringComparison.Ordinal))
                return false;

            if (firstModule.Functions.Count != secondModule.Functions.Count)
                return false;

            // Zip functions in declaration order and require body-statement
            // count to survive the round-trip. Any drop or duplicate at
            // the statement level trips this.
            for (var i = 0; i < firstModule.Functions.Count; i++)
            {
                var firstBody = firstModule.Functions[i].Body?.Count ?? 0;
                var secondBody = secondModule.Functions[i].Body?.Count ?? 0;
                if (firstBody != secondBody) return false;
            }

            return true;
        });
    }

    #endregion
}
