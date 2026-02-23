using System.Text.Json;
using System.Text.RegularExpressions;
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

    // Pre-compiled regexes for Calor markers (literal patterns escaped at compile time)
    private static readonly Regex RxModuleOpen = new(@"§M\{", RegexOptions.Compiled);
    private static readonly Regex RxFuncOpen = new(@"§F\{", RegexOptions.Compiled);
    private static readonly Regex RxInputOpen = new(@"§I\{", RegexOptions.Compiled);
    private static readonly Regex RxOutputOpen = new(@"§O\{", RegexOptions.Compiled);
    private static readonly Regex RxReturn = new(@"§R", RegexOptions.Compiled);
    private static readonly Regex RxEffectOpen = new(@"§E\{", RegexOptions.Compiled);
    private static readonly Regex RxRequires = new(@"§Q ", RegexOptions.Compiled);
    private static readonly Regex RxEnsures = new(@"§S ", RegexOptions.Compiled);
    private static readonly Regex RxFuncClose = new(@"§/F\{", RegexOptions.Compiled);
    private static readonly Regex RxModuleClose = new(@"§/M\{", RegexOptions.Compiled);
    private static readonly Regex RxClosingTag = new(@"§/", RegexOptions.Compiled);
    private static readonly Regex RxEffectBody = new(@"§E\{([^}]+)\}", RegexOptions.Compiled);

    // Pre-compiled regexes for C# patterns
    private static readonly Regex RxNamespace = new(@"namespace", RegexOptions.Compiled);
    private static readonly Regex RxClass = new(@"class", RegexOptions.Compiled);
    private static readonly Regex RxPublic = new(@"public", RegexOptions.Compiled);
    private static readonly Regex RxPrivate = new(@"private", RegexOptions.Compiled);
    private static readonly Regex RxCsReturn = new(@"return", RegexOptions.Compiled);
    private static readonly Regex RxDocComment = new(@"///", RegexOptions.Compiled);
    private static readonly Regex RxLineComment = new(@"(?<!/)//(?!/)", RegexOptions.Compiled);
    private static readonly Regex RxTypeAnnotation = new(@"\b(int|string|bool|double|float|long|decimal)\s", RegexOptions.Compiled);
    private static readonly Regex RxCsContract = new(@"(Contract\.(Requires|Ensures)|Debug\.Assert)", RegexOptions.Compiled);

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
    /// Uses heuristic scoring by default.
    /// </summary>
    public Task<List<MetricResult>> EvaluateQuestionsAsync(
        EvaluationContext context,
        List<ComprehensionQuestion> questions,
        Func<string, string, Task<string>> answerGenerator)
    {
        return EvaluateQuestionsAsync(context, questions, answerGenerator, answerScorer: null);
    }

    /// <summary>
    /// Evaluates comprehension questions with an optional LLM-based answer scorer.
    /// When answerScorer is provided, it's used instead of heuristic string matching.
    /// </summary>
    public async Task<List<MetricResult>> EvaluateQuestionsAsync(
        EvaluationContext context,
        List<ComprehensionQuestion> questions,
        Func<string, string, Task<string>> answerGenerator,
        Func<string, string, string, Task<double>>? answerScorer)
    {
        var results = new List<MetricResult>();

        foreach (var question in questions)
        {
            // Generate answers for both Calor and C#
            var calorAnswer = await answerGenerator(context.CalorSource, question.Question);
            var csharpAnswer = await answerGenerator(context.CSharpSource, question.Question);

            // Score answers against expected — use LLM judge if available
            double calorScore, csharpScore;
            if (answerScorer != null)
            {
                calorScore = await answerScorer(question.Question, calorAnswer, question.ExpectedAnswer);
                csharpScore = await answerScorer(question.Question, csharpAnswer, question.ExpectedAnswer);
            }
            else
            {
                calorScore = ScoreAnswer(calorAnswer, question.ExpectedAnswer);
                csharpScore = ScoreAnswer(csharpAnswer, question.ExpectedAnswer);
            }

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
    /// Proportional scoring helper: applies diminishing returns via log2.
    /// Score = weight * min(log2(1 + count) / 3.0, 1.0)
    /// This rewards density: 1 occurrence ≈ 33%, 3 ≈ 67%, 7 ≈ 100% of weight.
    /// Uses pre-compiled regex for performance at scale (250+ programs).
    /// </summary>
    private static double ProportionalScore(string source, Regex compiledPattern, double weight)
    {
        var count = compiledPattern.Matches(source).Count;
        if (count == 0) return 0.0;
        return weight * Math.Min(Math.Log2(1 + count) / 3.0, 1.0);
    }

    /// <summary>
    /// Calculates clarity score for Calor based on explicit structure markers.
    /// Uses proportional counting with diminishing returns for density-sensitive scoring.
    /// </summary>
    private static double CalculateCalorClarityScore(EvaluationContext context)
    {
        var score = 0.0;
        var source = context.CalorSource;

        // Proportional clarity indicators — count occurrences with diminishing returns
        score += ProportionalScore(source, RxModuleOpen, 0.15);   // Module declarations
        score += ProportionalScore(source, RxFuncOpen, 0.15);     // Function declarations
        score += ProportionalScore(source, RxInputOpen, 0.10);    // Input parameters
        score += ProportionalScore(source, RxOutputOpen, 0.10);   // Output types
        score += ProportionalScore(source, RxReturn, 0.10);       // Return statements
        score += ProportionalScore(source, RxEffectOpen, 0.15);   // Effect declarations
        score += ProportionalScore(source, RxRequires, 0.15);     // Requires contracts (preconditions)
        score += ProportionalScore(source, RxEnsures, 0.10);      // Ensures contracts (postconditions)

        // Closing tags — proportional
        score += ProportionalScore(source, RxFuncClose, 0.05);
        score += ProportionalScore(source, RxModuleClose, 0.05);

        // Strategy 5: Contract-depth and effect-specificity dimensions

        // Contract completeness: having BOTH pre and post creates a behavioral spec
        if (source.Contains("§Q ") && source.Contains("§S "))
            score += 0.10;

        // Effect specificity: §E{cw,db:rw} is more informative than §E{cw}
        var effectMatches = RxEffectBody.Matches(source);
        if (effectMatches.Count > 0)
        {
            var maxEffectCount = effectMatches
                .Select(m => m.Groups[1].Value.Split(',').Length)
                .Max();
            score += Math.Min(0.02 * maxEffectCount, 0.10);
        }

        // ID consistency: matched open/close pairs aid navigation
        var openCount = RxFuncOpen.Matches(source).Count;
        var closeCount = RxFuncClose.Matches(source).Count;
        if (openCount > 0 && openCount == closeCount)
            score += 0.05;

        // Cap at 1.5 to accommodate depth dimensions while keeping scores comparable.
        // Comprehension uses CreateHigherIsBetter (ratio = calor/csharp), not percentage display.
        return Math.Min(score, 1.5);
    }

    /// <summary>
    /// Calculates clarity score for C# based on code patterns.
    /// Uses proportional counting with the same diminishing returns formula for fairness.
    /// </summary>
    private static double CalculateCSharpClarityScore(EvaluationContext context)
    {
        var score = 0.0;
        var source = context.CSharpSource;

        // Proportional clarity indicators — same log2 formula as Calor
        score += ProportionalScore(source, RxNamespace, 0.15);
        score += ProportionalScore(source, RxClass, 0.10);
        score += ProportionalScore(source, RxPublic, 0.05);
        score += ProportionalScore(source, RxPrivate, 0.05);
        score += ProportionalScore(source, RxCsReturn, 0.10);

        // Documentation — proportional
        var docCount = RxDocComment.Matches(source).Count;
        score += 0.20 * Math.Min(Math.Log2(1 + docCount) / 3.0, 1.0);
        var commentCount = RxLineComment.Matches(source).Count;
        score += 0.05 * Math.Min(Math.Log2(1 + commentCount) / 3.0, 1.0);

        // Type annotations — proportional
        var typeCount = RxTypeAnnotation.Matches(source).Count;
        score += 0.10 * Math.Min(Math.Log2(1 + typeCount) / 3.0, 1.0);

        // Contracts — proportional
        var contractCount = RxCsContract.Matches(source).Count;
        score += 0.15 * Math.Min(Math.Log2(1 + contractCount) / 3.0, 1.0);

        // Strategy 5: Documentation-depth dimensions (fair equivalent)

        // Documentation completeness: BOTH summary AND param docs
        if (source.Contains("<summary>") && source.Contains("<param"))
            score += 0.10;

        // Assertion specificity
        var assertCount = RxCsContract.Matches(source).Count;
        score += Math.Min(0.02 * assertCount, 0.10);

        // Same cap as Calor for fairness. Both use CreateHigherIsBetter (ratio), not percentage display.
        return Math.Min(score, 1.5);
    }

    private static Dictionary<string, object> GetCalorClarityFactors(EvaluationContext context)
    {
        var source = context.CalorSource;
        var funcOpen = RxFuncOpen.Matches(source).Count;
        var funcClose = RxFuncClose.Matches(source).Count;
        return new Dictionary<string, object>
        {
            ["moduleCount"] = RxModuleOpen.Matches(source).Count,
            ["functionCount"] = funcOpen,
            ["inputCount"] = RxInputOpen.Matches(source).Count,
            ["outputCount"] = RxOutputOpen.Matches(source).Count,
            ["effectCount"] = RxEffectOpen.Matches(source).Count,
            ["requiresCount"] = RxRequires.Matches(source).Count,
            ["ensuresCount"] = RxEnsures.Matches(source).Count,
            ["closingTagCount"] = RxClosingTag.Matches(source).Count,
            ["hasContractCompleteness"] = source.Contains("§Q ") && source.Contains("§S "),
            ["hasMatchedPairs"] = funcOpen == funcClose && funcOpen > 0
        };
    }

    private static Dictionary<string, object> GetCSharpClarityFactors(EvaluationContext context)
    {
        var source = context.CSharpSource;
        return new Dictionary<string, object>
        {
            ["namespaceCount"] = RxNamespace.Matches(source).Count,
            ["classCount"] = RxClass.Matches(source).Count,
            ["docCommentCount"] = RxDocComment.Matches(source).Count,
            ["commentCount"] = RxLineComment.Matches(source).Count,
            ["typeAnnotationCount"] = RxTypeAnnotation.Matches(source).Count,
            ["contractCount"] = RxCsContract.Matches(source).Count,
            ["hasDocCompleteness"] = source.Contains("<summary>") && source.Contains("<param")
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
