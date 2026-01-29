using Opal.Compiler.Evaluation.Core;

namespace Opal.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 5: Error Detection Calculator
/// Measures the presence of contracts, assertions, and error detection mechanisms.
/// OPAL's built-in §REQ/§ENS/§INV provides explicit error detection.
/// </summary>
public class ErrorDetectionCalculator : IMetricCalculator
{
    public string Category => "ErrorDetection";

    public string Description => "Measures contracts, assertions, and error detection mechanisms";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var opalScore = CalculateOpalErrorDetection(context);
        var csharpScore = CalculateCSharpErrorDetection(context);

        var details = new Dictionary<string, object>
        {
            ["opalContracts"] = CountOpalContracts(context.OpalSource),
            ["csharpGuards"] = CountCSharpGuards(context.CSharpSource),
            ["opalPreconditions"] = CountOccurrences(context.OpalSource, "§REQ"),
            ["opalPostconditions"] = CountOccurrences(context.OpalSource, "§ENS"),
            ["opalInvariants"] = CountOccurrences(context.OpalSource, "§INV"),
            ["csharpExceptions"] = CountOccurrences(context.CSharpSource, "throw ")
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "ContractCoverage",
            opalScore,
            csharpScore,
            details));
    }

    private static double CalculateOpalErrorDetection(EvaluationContext context)
    {
        var source = context.OpalSource;
        var score = 0.0;

        // Preconditions (§REQ)
        var reqCount = CountOccurrences(source, "§REQ");
        score += Math.Min(0.3, reqCount * 0.1);

        // Postconditions (§ENS)
        var ensCount = CountOccurrences(source, "§ENS");
        score += Math.Min(0.3, ensCount * 0.1);

        // Invariants (§INV)
        var invCount = CountOccurrences(source, "§INV");
        score += Math.Min(0.2, invCount * 0.1);

        // Effect declarations (explicit side effect documentation)
        var effectCount = CountOccurrences(source, "§E[");
        score += Math.Min(0.1, effectCount * 0.05);

        // Assert statements
        var assertCount = CountOccurrences(source, "§ASSERT");
        score += Math.Min(0.1, assertCount * 0.05);

        // Null checks - OPAL uses prefix notation: (!= var null) or (== var null)
        var nullChecks = CountOccurrences(source, " null)") + CountOccurrences(source, " null]");
        score += Math.Min(0.15, nullChecks * 0.03);

        return Math.Min(1.0, score);
    }

    private static double CalculateCSharpErrorDetection(EvaluationContext context)
    {
        var source = context.CSharpSource;
        var score = 0.0;

        // Null checks
        var nullChecks = CountOccurrences(source, "!= null") + CountOccurrences(source, "is not null");
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

    private static Dictionary<string, int> CountOpalContracts(string source)
    {
        return new Dictionary<string, int>
        {
            ["requires"] = CountOccurrences(source, "§REQ"),
            ["ensures"] = CountOccurrences(source, "§ENS"),
            ["invariants"] = CountOccurrences(source, "§INV"),
            ["effects"] = CountOccurrences(source, "§E["),
            ["asserts"] = CountOccurrences(source, "§ASSERT")
        };
    }

    private static Dictionary<string, int> CountCSharpGuards(string source)
    {
        return new Dictionary<string, int>
        {
            ["nullChecks"] = CountOccurrences(source, "!= null") + CountOccurrences(source, "is not null"),
            ["argumentExceptions"] = CountOccurrences(source, "ArgumentException") +
                                     CountOccurrences(source, "ArgumentNullException"),
            ["throws"] = CountOccurrences(source, "throw "),
            ["debugAsserts"] = CountOccurrences(source, "Debug.Assert"),
            ["contracts"] = CountOccurrences(source, "Contract.Requires") +
                           CountOccurrences(source, "Contract.Ensures")
        };
    }

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
