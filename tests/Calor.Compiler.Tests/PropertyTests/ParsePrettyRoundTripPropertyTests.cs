using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Calor.Compiler.Tests.PropertyTests;

/// <summary>
/// Literal smoke coverage for parse -> pretty-print -> parse. Every generated
/// sample must parse; structural comparison includes values and child structure,
/// not just counts. GeneratedProductionPipelinePropertyTests adds bounded
/// production execution, independent behavioral oracles, and explicit rejections.
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
    /// Every sample in the intentionally well-typed subset must be accepted.
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
        Assert.True(parseable == samples.Count(),
            $"accepted={parseable}, rejected={samples.Count() - parseable}, total={samples.Count()}");
    }

    #endregion

    #region Property Tests

    [Property(MaxTest = 50)]
    public Property Parse_Pretty_Parse_ProducesNoErrors()
    {
        return Prop.ForAll(SmallCalorPrograms(), source =>
        {
            var (firstModule, firstDiags) = TryParse(source);

            Assert.NotNull(firstModule);
            Assert.False(firstDiags.HasErrors, string.Join("; ", firstDiags.Errors));

            var reemitted = PrettyPrint(firstModule);
            var (secondModule, secondDiags) = TryParse(reemitted);

            return secondModule is not null && !secondDiags.HasErrors;
        });
    }

    [Property(MaxTest = 50)]
    public Property Parse_Pretty_Parse_PreservesModuleShape()
    {
        return Prop.ForAll(SmallCalorPrograms(), source =>
        {
            var (firstModule, firstDiags) = TryParse(source);
            Assert.NotNull(firstModule);
            Assert.False(firstDiags.HasErrors, string.Join("; ", firstDiags.Errors));

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

            return GeneratedProductionPipelinePropertyTests.SemanticTree(firstModule)
                == GeneratedProductionPipelinePropertyTests.SemanticTree(secondModule);
        });
    }

    #endregion
}
