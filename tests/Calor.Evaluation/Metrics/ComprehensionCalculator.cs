using System.Text.Json;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Category 3: Comprehension Calculator
/// Measures semantic understanding quality for Calor vs C#.
/// Calor's explicit contracts and effects are hypothesized to aid understanding.
/// </summary>
public class ComprehensionCalculator : IMetricCalculator
{
    public string Category => "Comprehension";

    public string Description => "Measures semantic understanding based on code structure clarity";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Calculate comprehension scores based on structural clarity metrics
        var calorClarity = CalculateCalorClarityScore(context);
        var csharpClarity = CalculateCSharpClarityScore(context);

        var details = new Dictionary<string, object>
        {
            ["calorClarityFactors"] = GetCalorClarityFactors(context),
            ["csharpClarityFactors"] = GetCSharpClarityFactors(context)
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "StructuralClarity",
            calorClarity,
            csharpClarity,
            details));
    }

    /// <summary>
    /// Evaluates comprehension questions if provided in context metadata.
    /// </summary>
    public async Task<List<MetricResult>> EvaluateQuestionsAsync(
        EvaluationContext context,
        List<ComprehensionQuestion> questions,
        Func<string, string, Task<string>> answerGenerator)
    {
        var results = new List<MetricResult>();

        foreach (var question in questions)
        {
            // Generate answers for both Calor and C#
            var calorAnswer = await answerGenerator(context.CalorSource, question.Question);
            var csharpAnswer = await answerGenerator(context.CSharpSource, question.Question);

            // Score answers against expected
            var calorScore = ScoreAnswer(calorAnswer, question.ExpectedAnswer);
            var csharpScore = ScoreAnswer(csharpAnswer, question.ExpectedAnswer);

            results.Add(MetricResult.CreateHigherIsBetter(
                Category,
                $"Question_{question.Id}",
                calorScore,
                csharpScore,
                new Dictionary<string, object>
                {
                    ["question"] = question.Question,
                    ["expected"] = question.ExpectedAnswer,
                    ["calorAnswer"] = calorAnswer,
                    ["csharpAnswer"] = csharpAnswer
                }));
        }

        return results;
    }

    /// <summary>
    /// Calculates clarity score for Calor based on explicit structure markers.
    /// </summary>
    private static double CalculateCalorClarityScore(EvaluationContext context)
    {
        var score = 0.0;
        var source = context.CalorSource;

        // Calor-specific clarity indicators
        if (source.Contains("§M{")) score += 0.15;  // Module declaration
        if (source.Contains("§F{")) score += 0.15;  // Function declaration
        if (source.Contains("§I{")) score += 0.10;  // Input parameters
        if (source.Contains("§O{")) score += 0.10;  // Output type
        if (source.Contains("§R")) score += 0.10;   // Return statement
        if (source.Contains("§E{")) score += 0.15;  // Effect declaration
        if (source.Contains("§REQ")) score += 0.15; // Requires contract
        if (source.Contains("§ENS")) score += 0.10; // Ensures contract

        // Explicit closing tags aid comprehension
        if (source.Contains("§/F{")) score += 0.05;
        if (source.Contains("§/M{")) score += 0.05;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Calculates clarity score for C# based on code patterns.
    /// </summary>
    private static double CalculateCSharpClarityScore(EvaluationContext context)
    {
        var score = 0.0;
        var source = context.CSharpSource;

        // C# clarity indicators
        if (source.Contains("namespace")) score += 0.15;
        if (source.Contains("class")) score += 0.10;
        if (source.Contains("public")) score += 0.05;
        if (source.Contains("private")) score += 0.05;
        if (source.Contains("return")) score += 0.10;

        // Documentation improves comprehension
        if (source.Contains("///")) score += 0.20;
        if (source.Contains("//")) score += 0.05;

        // Type annotations
        if (source.Contains("int ") || source.Contains("string ") || source.Contains("bool ")) score += 0.10;

        // Contracts (if using Code Contracts or Debug.Assert)
        if (source.Contains("Contract.") || source.Contains("Debug.Assert")) score += 0.15;

        return Math.Min(score, 1.0);
    }

    private static Dictionary<string, bool> GetCalorClarityFactors(EvaluationContext context)
    {
        var source = context.CalorSource;
        return new Dictionary<string, bool>
        {
            ["hasModuleDeclaration"] = source.Contains("§M{"),
            ["hasFunctionDeclaration"] = source.Contains("§F{"),
            ["hasInputParameters"] = source.Contains("§I{"),
            ["hasOutputType"] = source.Contains("§O{"),
            ["hasEffects"] = source.Contains("§E{"),
            ["hasContracts"] = source.Contains("§REQ") || source.Contains("§ENS"),
            ["hasClosingTags"] = source.Contains("§/")
        };
    }

    private static Dictionary<string, bool> GetCSharpClarityFactors(EvaluationContext context)
    {
        var source = context.CSharpSource;
        return new Dictionary<string, bool>
        {
            ["hasNamespace"] = source.Contains("namespace"),
            ["hasClass"] = source.Contains("class"),
            ["hasDocumentation"] = source.Contains("///"),
            ["hasComments"] = source.Contains("//"),
            ["hasTypeAnnotations"] = source.Contains("int ") || source.Contains("string "),
            ["hasContracts"] = source.Contains("Contract.") || source.Contains("Debug.Assert")
        };
    }

    /// <summary>
    /// Scores an answer against the expected answer using simple matching.
    /// </summary>
    private static double ScoreAnswer(string actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(actual))
            return 0.0;

        var normalizedActual = actual.ToLowerInvariant().Trim();
        var normalizedExpected = expected.ToLowerInvariant().Trim();

        // Exact match
        if (normalizedActual == normalizedExpected)
            return 1.0;

        // Contains expected answer
        if (normalizedActual.Contains(normalizedExpected))
            return 0.8;

        // Expected contains actual (partial match)
        if (normalizedExpected.Contains(normalizedActual))
            return 0.6;

        // Word overlap scoring
        var actualWords = normalizedActual.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var expectedWords = normalizedExpected.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (expectedWords.Count == 0)
            return 0.0;

        var overlap = actualWords.Intersect(expectedWords).Count();
        return (double)overlap / expectedWords.Count * 0.5;
    }
}

/// <summary>
/// Represents a comprehension question for evaluation.
/// </summary>
public class ComprehensionQuestion
{
    public required string Id { get; init; }
    public required string Question { get; init; }
    public required string ExpectedAnswer { get; init; }
    public string? Category { get; init; }
}

/// <summary>
/// Collection of questions for a specific file.
/// </summary>
public class ComprehensionQuestionSet
{
    public required string FileId { get; init; }
    public List<ComprehensionQuestion> Questions { get; init; } = new();
}
