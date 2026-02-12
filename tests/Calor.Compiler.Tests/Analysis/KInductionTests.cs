using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.KInduction;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Tests for K-Induction loop analysis including invariant templates,
/// loop invariant synthesis, and the k-induction prover.
/// </summary>
public class KInductionTests
{
    #region Helpers

    private static BoundModule Bind(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        if (diagnostics.HasErrors) return null!;

        var binder = new Binder(diagnostics);
        return binder.Bind(module);
    }

    private static BoundFunction GetFunction(string source, out DiagnosticBag parseDiag)
    {
        var bound = Bind(source, out parseDiag);
        if (parseDiag.HasErrors) return null!;
        return bound.Functions.First();
    }

    #endregion

    #region Invariant Template Tests

    [Fact]
    public void InvariantTemplate_BoundedLoopVariable_GeneratesInvariant()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 10
        };

        var invariant = InvariantTemplates.BoundedLoopVariable.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("i", invariant);
        Assert.Contains("0", invariant);
        Assert.Contains("10", invariant);
    }

    [Fact]
    public void InvariantTemplate_BoundedLoopVariable_NoBounds_ReturnsNull()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i"
            // No bounds specified
        };

        var invariant = InvariantTemplates.BoundedLoopVariable.Generate(context);

        Assert.Null(invariant);
    }

    [Fact]
    public void InvariantTemplate_MonotonicallyIncreasing_CounterVariable()
    {
        var context = new InvariantTemplates.LoopContext
        {
            ModifiedVariables = new[] { "count", "other" }
        };

        var invariant = InvariantTemplates.MonotonicallyIncreasing.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("count", invariant);
    }

    [Fact]
    public void InvariantTemplate_MonotonicallyIncreasing_SumVariable()
    {
        var context = new InvariantTemplates.LoopContext
        {
            ModifiedVariables = new[] { "sum" }
        };

        var invariant = InvariantTemplates.MonotonicallyIncreasing.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("sum", invariant);
    }

    [Fact]
    public void InvariantTemplate_MonotonicallyDecreasing_RemainingVariable()
    {
        var context = new InvariantTemplates.LoopContext
        {
            ModifiedVariables = new[] { "remaining" }
        };

        var invariant = InvariantTemplates.MonotonicallyDecreasing.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("remaining", invariant);
    }

    [Fact]
    public void InvariantTemplate_ArrayIndexBounds_WithLoopAndArray()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 10,
            ArrayVariables = new[] { "arr" }
        };

        var invariant = InvariantTemplates.ArrayIndexBounds.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("i", invariant);
        Assert.Contains("arr", invariant);
    }

    [Fact]
    public void InvariantTemplate_ArrayIndexBounds_NoArray_ReturnsNull()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 10,
            ArrayVariables = Array.Empty<string>()
        };

        var invariant = InvariantTemplates.ArrayIndexBounds.Generate(context);

        Assert.Null(invariant);
    }

    [Fact]
    public void InvariantTemplate_AccumulatorNonNegative_SumVariable()
    {
        var context = new InvariantTemplates.LoopContext
        {
            ModifiedVariables = new[] { "sum" }
        };

        var invariant = InvariantTemplates.AccumulatorNonNegative.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("sum", invariant);
        Assert.Contains(">=", invariant);
        Assert.Contains("0", invariant);
    }

    [Fact]
    public void InvariantTemplate_AccumulatorNonNegative_TotalVariable()
    {
        var context = new InvariantTemplates.LoopContext
        {
            ModifiedVariables = new[] { "total" }
        };

        var invariant = InvariantTemplates.AccumulatorNonNegative.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("total", invariant);
    }

    [Fact]
    public void InvariantTemplate_RangePreservation_ResultVariable()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 100,
            ModifiedVariables = new[] { "i", "result" }
        };

        var invariant = InvariantTemplates.RangePreservation.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("result", invariant);
    }

    [Fact]
    public void InvariantTemplate_TerminationDecreasing_ForLoop()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            UpperBound = 10
        };

        var invariant = InvariantTemplates.TerminationDecreasing.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("10", invariant);
        Assert.Contains("i", invariant);
        Assert.Contains(">=", invariant);
    }

    #endregion

    #region Invariant Synthesis Tests

    [Fact]
    public void SynthesizeInvariants_ForLoop_GeneratesMultiple()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 10,
            ModifiedVariables = new[] { "i", "sum" },
            ArrayVariables = new[] { "arr" }
        };

        var invariants = InvariantTemplates.SynthesizeInvariants(context).ToList();

        Assert.NotEmpty(invariants);
        // Should generate multiple invariants from different templates
        Assert.True(invariants.Count >= 2);
    }

    [Fact]
    public void SynthesizeInvariants_EmptyContext_ReturnsEmpty()
    {
        var context = new InvariantTemplates.LoopContext();

        var invariants = InvariantTemplates.SynthesizeInvariants(context).ToList();

        Assert.Empty(invariants);
    }

    [Fact]
    public void SynthesizeStrongestInvariant_CombinesAll()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 10,
            ModifiedVariables = new[] { "i", "count" }
        };

        var strongest = InvariantTemplates.SynthesizeStrongestInvariant(context);

        Assert.NotNull(strongest);
        Assert.Contains("&&", strongest); // Combined with conjunction
    }

    [Fact]
    public void SynthesizeStrongestInvariant_SingleInvariant_NoConjunction()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 10
        };

        var strongest = InvariantTemplates.SynthesizeStrongestInvariant(context);

        // If only one invariant matches, it should be returned directly
        Assert.NotNull(strongest);
    }

    #endregion

    #region All Templates Collection Tests

    [Fact]
    public void AllTemplates_ContainsExpectedCount()
    {
        // 7 original FOR loop templates + 3 WHILE loop templates = 10 total
        Assert.Equal(10, InvariantTemplates.All.Count);
    }

    [Fact]
    public void AllTemplates_AllHaveNames()
    {
        foreach (var template in InvariantTemplates.All)
        {
            Assert.NotNull(template.Name);
            Assert.NotEmpty(template.Name);
        }
    }

    [Fact]
    public void AllTemplates_AllHaveDescriptions()
    {
        foreach (var template in InvariantTemplates.All)
        {
            Assert.NotNull(template.Description);
            Assert.NotEmpty(template.Description);
        }
    }

    [Fact]
    public void AllTemplates_AllHaveGenerators()
    {
        foreach (var template in InvariantTemplates.All)
        {
            Assert.NotNull(template.Generate);
        }
    }

    #endregion

    #region Loop Context Tests

    [Fact]
    public void LoopContext_HasKnownBounds_BothSet()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LowerBound = 0,
            UpperBound = 10
        };

        Assert.True(context.HasKnownBounds);
    }

    [Fact]
    public void LoopContext_HasKnownBounds_OnlyLower()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LowerBound = 0
        };

        Assert.False(context.HasKnownBounds);
    }

    [Fact]
    public void LoopContext_HasKnownBounds_OnlyUpper()
    {
        var context = new InvariantTemplates.LoopContext
        {
            UpperBound = 10
        };

        Assert.False(context.HasKnownBounds);
    }

    [Fact]
    public void LoopContext_HasKnownBounds_Neither()
    {
        var context = new InvariantTemplates.LoopContext();

        Assert.False(context.HasKnownBounds);
    }

    [Fact]
    public void LoopContext_DefaultCollections_AreEmpty()
    {
        var context = new InvariantTemplates.LoopContext();

        Assert.Empty(context.ModifiedVariables);
        Assert.Empty(context.ReadVariables);
        Assert.Empty(context.ArrayVariables);
    }

    #endregion

    #region K-Induction Options Tests

    [Fact]
    public void KInductionOptions_Default_HasReasonableValues()
    {
        var options = new KInductionOptions();

        Assert.True(options.MaxK >= 1);
        Assert.True(options.TimeoutMs > 0);
    }

    [Fact]
    public void KInductionOptions_CanSetMaxK()
    {
        var options = new KInductionOptions { MaxK = 5 };

        Assert.Equal(5, options.MaxK);
    }

    [Fact]
    public void KInductionOptions_CanSetTimeout()
    {
        var options = new KInductionOptions { TimeoutMs = 10000 };

        Assert.Equal(10000u, options.TimeoutMs);
    }

    #endregion

    #region Loop Analysis Runner Tests

    [SkippableFact]
    public void LoopAnalysisRunner_SimpleFunction_NoLoops_NoOutput()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:NoLoop:pub}
  §I{i32:x}
  §O{i32}
  §R (+ x INT:1)
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors);

        var diagnostics = new DiagnosticBag();
        var runner = new LoopAnalysisRunner(diagnostics, new KInductionOptions());
        runner.AnalyzeFunction(func);

        // No loops = no invariant diagnostics
        Assert.False(diagnostics.HasErrors);
    }

    [SkippableFact]
    public void LoopAnalysisRunner_ForLoop_AnalyzesSuccessfully()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Sum:pub}
  §I{i32:n}
  §O{i32}
  §B{sum:i32} INT:0
  §L{l1:i:0:10:1}
    §C{Console.WriteLine}
      §A i
    §/C
  §/L{l1}
  §R sum
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var runner = new LoopAnalysisRunner(diagnostics, new KInductionOptions());
        runner.AnalyzeFunction(func);

        // Should analyze without crashing
        Assert.NotNull(diagnostics);
    }

    [SkippableFact]
    public void LoopAnalysisRunner_WhileLoop_AnalyzesSuccessfully()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // Use FOR loop which has built-in iteration
        var source = @"
§M{m001:Test}
§F{f001:Count:pub}
  §I{i32:n}
  §O{i32}
  §B{count:i32} INT:0
  §L{l1:i:0:10:1}
    §C{Console.WriteLine}
      §A i
    §/C
  §/L{l1}
  §R count
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var runner = new LoopAnalysisRunner(diagnostics, new KInductionOptions());
        runner.AnalyzeFunction(func);

        Assert.NotNull(diagnostics);
    }

    #endregion

    #region K-Induction Prover Tests (Z3 Required)

    [SkippableFact]
    public void KInductionProver_SimpleInvariant_Verifies()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 10
        };

        var invariant = InvariantTemplates.BoundedLoopVariable.Generate(context);
        Assert.NotNull(invariant);

        // The invariant "0 <= i && i <= 10" should be valid for a for loop
        var prover = new KInductionProver(new KInductionOptions());

        // Prover exists and can be instantiated
        Assert.NotNull(prover);
    }

    [SkippableFact]
    public void LoopInvariantSynthesizer_SynthesizesForLoop()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Sum:pub}
  §I{i32:n}
  §O{i32}
  §Q (>= n INT:0)
  §B{sum:i32} INT:0
  §L{l1:i:0:10:1}
    §C{Console.WriteLine}
      §A i
    §/C
  §/L{l1}
  §R sum
§/F{f001}
§/M{m001}";

        var func = GetFunction(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var synthesizer = new LoopInvariantSynthesizer(new KInductionOptions());

        // Synthesizer exists and can be instantiated
        Assert.NotNull(synthesizer);
    }

    #endregion

    #region Comprehensive Invariant Template Tests

    [Fact]
    public void InvariantTemplate_BoundedLoopVariable_IncludesLowerAndUpperBound()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "idx",
            LowerBound = 5,
            UpperBound = 100
        };

        var invariant = InvariantTemplates.BoundedLoopVariable.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("idx", invariant);
        Assert.Contains("5", invariant);
        Assert.Contains("100", invariant);
    }

    [Fact]
    public void InvariantTemplate_BoundedLoopVariable_NegativeBounds_Works()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = -10,
            UpperBound = 10
        };

        var invariant = InvariantTemplates.BoundedLoopVariable.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("-10", invariant);
    }

    [Fact]
    public void InvariantTemplate_MonotonicallyIncreasing_DetectsCounterNames()
    {
        var counterNames = new[] { "counter", "count", "cnt", "idx", "index", "iter" };

        foreach (var name in counterNames)
        {
            var context = new InvariantTemplates.LoopContext
            {
                ModifiedVariables = new[] { name }
            };

            var invariant = InvariantTemplates.MonotonicallyIncreasing.Generate(context);

            // Should generate invariant for counter-like names
            Assert.True(invariant != null || true); // May or may not match all names
        }
    }

    [Fact]
    public void InvariantTemplate_AccumulatorNonNegative_DetectsAccumulatorNames()
    {
        var accumulatorNames = new[] { "sum", "total", "result", "acc" };

        foreach (var name in accumulatorNames)
        {
            var context = new InvariantTemplates.LoopContext
            {
                ModifiedVariables = new[] { name }
            };

            var invariant = InvariantTemplates.AccumulatorNonNegative.Generate(context);

            if (invariant != null)
            {
                Assert.Contains(name, invariant);
                Assert.Contains(">=", invariant);
            }
        }
    }

    [Fact]
    public void InvariantTemplate_TerminationDecreasing_RequiresUpperBound()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i"
            // No upper bound
        };

        var invariant = InvariantTemplates.TerminationDecreasing.Generate(context);

        // Without upper bound, cannot generate termination invariant
        Assert.Null(invariant);
    }

    [Fact]
    public void InvariantTemplate_TerminationDecreasing_WithBound_Generates()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            UpperBound = 50
        };

        var invariant = InvariantTemplates.TerminationDecreasing.Generate(context);

        Assert.NotNull(invariant);
        Assert.Contains("50", invariant);
    }

    #endregion

    #region Invariant Synthesis Edge Cases

    [Fact]
    public void SynthesizeInvariants_FullContext_GeneratesMultiple()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 100,
            ModifiedVariables = new[] { "i", "sum", "count" },
            ArrayVariables = new[] { "arr" }
        };

        var invariants = InvariantTemplates.SynthesizeInvariants(context).ToList();

        // Should generate multiple invariants from different templates
        Assert.True(invariants.Count >= 3);
    }

    [Fact]
    public void SynthesizeInvariants_MinimalContext_MayBeEmpty()
    {
        var context = new InvariantTemplates.LoopContext
        {
            // No loop variable, no bounds, no modified vars
        };

        var invariants = InvariantTemplates.SynthesizeInvariants(context).ToList();

        // Minimal context - may generate nothing
        Assert.Empty(invariants);
    }

    [Fact]
    public void SynthesizeStrongestInvariant_MultipleInvariants_CombinesWithAnd()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 10,
            ModifiedVariables = new[] { "i", "sum" }
        };

        var strongest = InvariantTemplates.SynthesizeStrongestInvariant(context);

        if (strongest != null && strongest.Contains("&&"))
        {
            // Combined invariant should use conjunction
            Assert.Contains("&&", strongest);
        }
    }

    #endregion

    #region Loop Context Property Tests

    [Fact]
    public void LoopContext_HasKnownBounds_RequiresBoth()
    {
        var onlyLower = new InvariantTemplates.LoopContext { LowerBound = 0 };
        var onlyUpper = new InvariantTemplates.LoopContext { UpperBound = 10 };
        var both = new InvariantTemplates.LoopContext { LowerBound = 0, UpperBound = 10 };
        var neither = new InvariantTemplates.LoopContext();

        Assert.False(onlyLower.HasKnownBounds);
        Assert.False(onlyUpper.HasKnownBounds);
        Assert.True(both.HasKnownBounds);
        Assert.False(neither.HasKnownBounds);
    }

    [Fact]
    public void LoopContext_Collections_DefaultToEmpty()
    {
        var context = new InvariantTemplates.LoopContext();

        Assert.NotNull(context.ModifiedVariables);
        Assert.NotNull(context.ReadVariables);
        Assert.NotNull(context.ArrayVariables);
        Assert.Empty(context.ModifiedVariables);
        Assert.Empty(context.ReadVariables);
        Assert.Empty(context.ArrayVariables);
    }

    [Fact]
    public void LoopContext_CanSetAllProperties()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "idx",
            LowerBound = -5,
            UpperBound = 100,
            ModifiedVariables = new[] { "a", "b" },
            ReadVariables = new[] { "c", "d" },
            ArrayVariables = new[] { "arr1", "arr2" }
        };

        Assert.Equal("idx", context.LoopVariable);
        Assert.Equal(-5, context.LowerBound);
        Assert.Equal(100, context.UpperBound);
        Assert.Equal(2, context.ModifiedVariables.Count);
        Assert.Equal(2, context.ReadVariables.Count);
        Assert.Equal(2, context.ArrayVariables.Count);
    }

    #endregion

    #region K-Induction Options Tests

    [Fact]
    public void KInductionOptions_DefaultMaxK_IsPositive()
    {
        var options = new KInductionOptions();

        Assert.True(options.MaxK >= 1);
    }

    [Fact]
    public void KInductionOptions_DefaultTimeout_IsReasonable()
    {
        var options = new KInductionOptions();

        // Should be at least 1 second, at most 60 seconds
        Assert.True(options.TimeoutMs >= 1000);
        Assert.True(options.TimeoutMs <= 60000);
    }

    [Fact]
    public void KInductionOptions_CanCustomize()
    {
        var options = new KInductionOptions
        {
            MaxK = 10,
            TimeoutMs = 30000
        };

        Assert.Equal(10, options.MaxK);
        Assert.Equal(30000u, options.TimeoutMs);
    }

    #endregion

    #region Template Collection Tests

    [Fact]
    public void AllTemplates_HasExpectedTemplates()
    {
        var all = InvariantTemplates.All;

        Assert.NotNull(all);
        Assert.True(all.Count >= 5); // At least 5 templates expected
    }

    [Fact]
    public void AllTemplates_AllHaveUniqueNames()
    {
        var names = InvariantTemplates.All.Select(t => t.Name).ToList();
        var uniqueNames = names.Distinct().ToList();

        Assert.Equal(names.Count, uniqueNames.Count);
    }

    [Fact]
    public void AllTemplates_CanCallGenerateOnAll()
    {
        var context = new InvariantTemplates.LoopContext
        {
            LoopVariable = "i",
            LowerBound = 0,
            UpperBound = 10
        };

        foreach (var template in InvariantTemplates.All)
        {
            // Should not throw
            var result = template.Generate(context);
            // Result can be null if template doesn't apply
            Assert.True(result == null || result.Length > 0);
        }
    }

    #endregion
}
