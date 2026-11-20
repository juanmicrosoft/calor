using Calor.Compiler.Evaluation.Core;
using Calor.Compiler.Evaluation.Metrics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// The benchmark metric calculators in src/Calor.Compiler/Evaluation/Metrics read native (0.30)
/// Calor. Before the port they looked for retired section markers and measured zero on every
/// native input; these tests pin the native counts so that cannot recur silently.
/// </summary>
public class BenchmarkMetricsNativeSyntaxTests
{
    private const string NativeSource = """
        module Spans Spans
          func TotalCovered public (int[]:starts, int[]:ends) → int
            !
            requires (starts.Length == ends.Length)
            => (result >= 0)
            var int:total = 0
            var int:i = 0
            while (i < starts.Length)
              set total (total + (ends[i] - starts[i]))
              set i (i + 1)
            return total

          func Report public (int:x) → void
            ! cw
            if (x > 0)
              Console.WriteLine(x)
            else
              Console.WriteLine(0)
        """;

    private const string CSharpSource = """
        using System;

        namespace Spans;

        public static class SpansModule
        {
            public static int TotalCovered(int[] starts, int[] ends)
            {
                if (starts.Length != ends.Length) throw new ArgumentException("length");
                var total = 0;
                for (var i = 0; i < starts.Length; i++) total += ends[i] - starts[i];
                return total;
            }
        }
        """;

    private static EvaluationContext Context(string calor = NativeSource) => new()
    {
        CalorSource = calor,
        CSharpSource = CSharpSource,
        FileName = "spans"
    };

    [Fact]
    public async Task ErrorDetection_CountsNativeContractsAndEffectRows()
    {
        var result = await new ErrorDetectionCalculator().CalculateAsync(Context());
        var contracts = (Dictionary<string, int>)result.Details["calorContracts"];

        Assert.Equal(1, contracts["requires"]);
        Assert.Equal(1, contracts["ensures"]);
        Assert.Equal(0, contracts["invariants"]);
        Assert.Equal(2, contracts["effects"]);
        // 0.1 (requires) + 0.1 (ensures) + 0.1 (two effect rows, capped)
        Assert.Equal(0.3, result.CalorScore, 6);
    }

    [Fact]
    public async Task Comprehension_CountsNativeDeclarations()
    {
        var result = await new ComprehensionCalculator().CalculateAsync(Context());
        var markers = (Dictionary<string, int>)result.Details["calorExplicitMarkers"];

        Assert.Equal(1, markers["module"]);
        Assert.Equal(2, markers["function"]);
        Assert.Equal(2, markers["variable"]);
        Assert.Equal(1, markers["requires"]);
        Assert.Equal(1, markers["ensures"]);
        Assert.Equal(0, markers["invariant"]);
        Assert.Equal(2, markers["effects"]);
        // Every weighted construct but the invariant (1.0 of 7.3) is present.
        Assert.Equal(6.3 / 7.3, result.CalorScore, 6);
    }

    [Fact]
    public async Task InformationDensity_CountsNativeSemanticElements()
    {
        var result = await new InformationDensityCalculator().CalculateAsync(Context());
        var elements = (Dictionary<string, int>)result.Details["calorSemanticElements"];

        Assert.Equal(1, elements["modules"]);
        Assert.Equal(2, elements["functions"]);
        Assert.Equal(2, elements["variables"]);
        Assert.Equal(5, elements["parameters"]); // three typed parameters + two return types
        Assert.Equal(2, elements["requires"]);   // weight 2x
        Assert.Equal(2, elements["ensures"]);    // weight 2x
        Assert.Equal(4, elements["effects"]);    // two rows, weight 2x
        Assert.Equal(2, elements["conditionals"]); // if + else
        Assert.Equal(1, elements["loops"]);
        Assert.Equal(1, elements["returns"]);
        Assert.Equal(2, elements["calls"]);
        Assert.Equal(2, elements["mutability"]);
        Assert.Equal(0, elements["closingTags"]);
    }

    [Fact]
    public async Task EditPrecision_CountsDeclarationIdsFromTheIdentityStore()
    {
        var result = await new EditPrecisionCalculator().CalculateAsync(Context());

        // The module and its two functions each get an ID; local bindings do not.
        Assert.Equal(3, (int)result.Details["calorUniqueIds"]);
        Assert.Equal(3, (int)result.Details["calorAddressableElements"]);
        // min(1.0, 3 * 0.15) + min(0.3, 2 * 0.1) + min(0.2, 1 * 0.1)
        Assert.Equal(0.75, result.CalorScore, 6);
    }

    [Fact]
    public async Task EditPrecision_UnparsableSourceHasNoIds()
    {
        var result = await new EditPrecisionCalculator().CalculateAsync(Context("module (((("));

        Assert.Equal(0, (int)result.Details["calorUniqueIds"]);
        Assert.Equal(0.0, result.CalorScore);
    }

    [Fact]
    public async Task GenerationAccuracy_FindsNativeStructure()
    {
        var result = await new GenerationAccuracyCalculator().CalculateAsync(Context());

        // module, func, balanced braces, typed parameters/return types: 4 of 4 checks.
        Assert.Equal(1.0, (double)result.Details["calorStructuralScore"], 6);
    }
}
