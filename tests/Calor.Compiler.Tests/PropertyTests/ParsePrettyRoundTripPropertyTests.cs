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
    /// Sanity check that the generator produces at least one input the
    /// parser accepts — otherwise the properties above pass vacuously.
    /// </summary>
    [Fact]
    public void Generator_ProducesAtLeastOneParseableInput()
    {
        var arb = SmallCalorPrograms();
        var samples = Gen.Sample(20, 30, arb.Generator);
        var parseable = 0;
        foreach (var sample in samples)
        {
            var (module, diags) = TryParse(sample);
            if (module is not null && !diags.HasErrors) parseable++;
        }
        Assert.True(parseable > 0, "generator produced zero parseable samples");
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
            // preserved.
            if (firstModule is null || firstDiags.HasErrors) return true;

            var reemitted = PrettyPrint(firstModule);
            var (secondModule, secondDiags) = TryParse(reemitted);

            return secondModule is not null && !secondDiags.HasErrors;
        });
    }

    [Property(MaxTest = 50)]
    public Property Parse_Pretty_Parse_PreservesModuleShape()
    {
        // Property: the round-trip preserves the module name and the number
        // of top-level function declarations. Node-for-node AST equality is
        // out of scope for this smoke test (see the class-level comment);
        // this is the shape-level check that a regression would trip.
        return Prop.ForAll(SmallCalorPrograms(), source =>
        {
            var (firstModule, firstDiags) = TryParse(source);
            if (firstModule is null || firstDiags.HasErrors) return true;

            var reemitted = PrettyPrint(firstModule);
            var (secondModule, secondDiags) = TryParse(reemitted);
            if (secondModule is null || secondDiags.HasErrors) return false;

            if (!string.Equals(firstModule.Name, secondModule.Name, StringComparison.Ordinal))
                return false;

            return firstModule.Functions.Count == secondModule.Functions.Count;
        });
    }

    #endregion
}
