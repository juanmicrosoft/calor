using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 5: Error Detection Calculator
/// Measures the presence of contracts, assertions, and error detection mechanisms.
/// Calor's built-in <c>requires</c>, postconditions (<c>=&gt;</c>) and <c>invariant</c> provide
/// explicit error detection.
/// </summary>
public class ErrorDetectionCalculator : IMetricCalculator
{
    public string Category => "ErrorDetection";

    public string Description => "Measures contracts, assertions, and error detection mechanisms";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var calorScore = CalculateCalorErrorDetection(context);
        var csharpScore = CalculateCSharpErrorDetection(context);

        var details = new Dictionary<string, object>
        {
            ["calorContracts"] = CountCalorContracts(context.CalorSource),
            ["csharpGuards"] = CountCSharpGuards(context.CSharpSource),
            ["calorPreconditions"] = NativeCalorSyntax.Precondition.Matches(context.CalorSource).Count,
            ["calorPostconditions"] = NativeCalorSyntax.Ensures.Matches(context.CalorSource).Count,
            ["calorInvariants"] = NativeCalorSyntax.Invariant.Matches(context.CalorSource).Count,
            ["csharpExceptions"] = CountOccurrences(context.CSharpSource, "throw ")
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "ContractCoverage",
            calorScore,
            csharpScore,
            details));
    }

    private static double CalculateCalorErrorDetection(EvaluationContext context)
    {
        var source = context.CalorSource;
        var score = 0.0;

        // Preconditions (`requires expr`)
        var reqCount = NativeCalorSyntax.Precondition.Matches(source).Count;
        score += Math.Min(0.3, reqCount * 0.1);

        // Postconditions (line-head `=> expr`)
        var ensCount = NativeCalorSyntax.Ensures.Matches(source).Count;
        score += Math.Min(0.3, ensCount * 0.1);

        // Invariants (`invariant expr`)
        var invCount = NativeCalorSyntax.Invariant.Matches(source).Count;
        score += Math.Min(0.2, invCount * 0.1);

        // Effect declarations (explicit side effect documentation): one `! ...` row per function
        var effectCount = NativeCalorSyntax.EffectRow.Matches(source).Count;
        score += Math.Min(0.1, effectCount * 0.05);

        // Native Calor has no assert statement (the legacy term counted a retired marker), so
        // there is no assert term; Calor.Evaluation's copy of this metric counts none either.

        // Null checks. Native Calor writes comparisons infix, like C#, so both languages are
        // counted with the same patterns.
        var nullChecks = CountNullChecks(source);
        score += Math.Min(0.15, nullChecks * 0.03);

        return Math.Min(1.0, score);
    }

    private static double CalculateCSharpErrorDetection(EvaluationContext context)
    {
        var source = context.CSharpSource;
        var score = 0.0;

        // Null checks
        var nullChecks = CountNullChecks(source);
        score += Math.Min(0.15, nullChecks * 0.03);

        // ArgumentNullException
        var argNullEx = CountOccurrences(source, "ArgumentNullException");
        score += Math.Min(0.1, argNullEx * 0.05);

        // ArgumentException variations
        var argEx = CountOccurrences(source, "ArgumentException") +
                    CountOccurrences(source, "ArgumentOutOfRangeException");
        score += Math.Min(0.1, argEx * 0.05);

        // InvalidOperationException
        var invalidOp = CountOccurrences(source, "InvalidOperationException");
        score += Math.Min(0.1, invalidOp * 0.05);

        // Debug.Assert
        var debugAssert = CountOccurrences(source, "Debug.Assert");
        score += Math.Min(0.1, debugAssert * 0.05);

        // Contract.Requires (Code Contracts)
        var contractReq = CountOccurrences(source, "Contract.Requires");
        score += Math.Min(0.15, contractReq * 0.05);

        // Contract.Ensures
        var contractEns = CountOccurrences(source, "Contract.Ensures");
        score += Math.Min(0.15, contractEns * 0.05);

        // Nullable reference type annotations
        if (source.Contains("?") && (source.Contains("string?") || source.Contains("int?")))
        {
            score += 0.05;
        }

        // Guard clauses (if throwing)
        var guardPatterns = CountOccurrences(source, "if (") + CountOccurrences(source, "if(");
        var throwCount = CountOccurrences(source, "throw ");
        if (throwCount > 0 && guardPatterns > 0)
        {
            score += Math.Min(0.1, (throwCount / (double)guardPatterns) * 0.1);
        }

        return Math.Min(1.0, score);
    }

    private static Dictionary<string, int> CountCalorContracts(string source)
    {
        return new Dictionary<string, int>
        {
            ["requires"] = NativeCalorSyntax.Precondition.Matches(source).Count,
            ["ensures"] = NativeCalorSyntax.Ensures.Matches(source).Count,
            ["invariants"] = NativeCalorSyntax.Invariant.Matches(source).Count,
            ["effects"] = NativeCalorSyntax.EffectRow.Matches(source).Count
        };
    }

    private static Dictionary<string, int> CountCSharpGuards(string source)
    {
        return new Dictionary<string, int>
        {
            ["nullChecks"] = CountNullChecks(source),
            ["argumentExceptions"] = CountOccurrences(source, "ArgumentException") +
                                     CountOccurrences(source, "ArgumentNullException"),
            ["throws"] = CountOccurrences(source, "throw "),
            ["debugAsserts"] = CountOccurrences(source, "Debug.Assert"),
            ["contracts"] = CountOccurrences(source, "Contract.Requires") +
                           CountOccurrences(source, "Contract.Ensures")
        };
    }

    private static int CountNullChecks(string source) =>
        CountOccurrences(source, "!= null") + CountOccurrences(source, "is not null");

    private static int CountOccurrences(string source, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
