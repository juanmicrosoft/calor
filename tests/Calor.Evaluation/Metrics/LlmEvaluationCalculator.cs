using System.Text.Json;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// LLM-based evaluation calculator that uses AI models to assess code comprehension.
/// Supports multiple LLM providers (Anthropic Claude, OpenAI GPT-4) for cross-validation.
/// </summary>
public class LlmEvaluationCalculator : IMetricCalculator
{
    public string Category => "LlmEvaluation";

    public string Description => "Measures code comprehension using LLM-based question answering";

    private readonly LlmEvaluationOptions _options;

    public LlmEvaluationCalculator(LlmEvaluationOptions? options = null)
    {
        _options = options ?? new LlmEvaluationOptions();
    }

    public async Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        if (!_options.Enabled)
        {
            return new MetricResult(
                Category,
                "Comprehension",
                0, 0, 1.0,
                new Dictionary<string, object> { ["status"] = "disabled" });
        }

        // Check for API keys
        var hasAnthropicKey = !string.IsNullOrEmpty(_options.AnthropicApiKey) ||
                              !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
        var hasOpenAiKey = !string.IsNullOrEmpty(_options.OpenAiApiKey) ||
                           !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        if (!hasAnthropicKey && !hasOpenAiKey)
        {
            return new MetricResult(
                Category,
                "Comprehension",
                0, 0, 1.0,
                new Dictionary<string, object> { ["status"] = "no_api_keys" });
        }

        var results = new List<LlmEvaluationResult>();

        // Run evaluation with available providers
        if (hasAnthropicKey)
        {
            var claudeResult = await EvaluateWithProvider(context, LlmProvider.Claude);
            results.Add(claudeResult);
        }

        if (hasOpenAiKey)
        {
            var gptResult = await EvaluateWithProvider(context, LlmProvider.Gpt4);
            results.Add(gptResult);
        }

        // Aggregate results
        var calorAvg = results.Average(r => r.CalorScore);
        var csharpAvg = results.Average(r => r.CSharpScore);

        var details = new Dictionary<string, object>
        {
            ["providers"] = results.Select(r => r.Provider.ToString()).ToList(),
            ["results"] = results,
            ["crossModelAgreement"] = CalculateCrossModelAgreement(results)
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "Comprehension",
            calorAvg,
            csharpAvg,
            details);
    }

    /// <summary>
    /// Evaluates code comprehension using a specific LLM provider.
    /// </summary>
    private async Task<LlmEvaluationResult> EvaluateWithProvider(
        EvaluationContext context,
        LlmProvider provider)
    {
        var questions = GenerateLlmComprehensionQuestions(context);
        var calorScore = 0.0;
        var csharpScore = 0.0;
        var totalCalorTokens = 0;
        var totalCsharpTokens = 0;

        foreach (var question in questions)
        {
            // Evaluate Calor comprehension
            var calorResponse = await AskQuestion(
                provider,
                context.CalorSource,
                question.Question,
                "Calor");

            var calorCorrect = EvaluateAnswer(calorResponse.Answer, question.ExpectedAnswer);
            calorScore += calorCorrect;
            totalCalorTokens += calorResponse.TokensUsed;

            // Evaluate C# comprehension
            var csharpResponse = await AskQuestion(
                provider,
                context.CSharpSource,
                question.Question,
                "C#");

            var csharpCorrect = EvaluateAnswer(csharpResponse.Answer, question.ExpectedAnswer);
            csharpScore += csharpCorrect;
            totalCsharpTokens += csharpResponse.TokensUsed;
        }

        var questionCount = questions.Count;
        return new LlmEvaluationResult
        {
            Provider = provider,
            CalorScore = questionCount > 0 ? calorScore / questionCount : 0,
            CSharpScore = questionCount > 0 ? csharpScore / questionCount : 0,
            QuestionsAnswered = questionCount,
            CalorTokensUsed = totalCalorTokens,
            CSharpTokensUsed = totalCsharpTokens
        };
    }

    /// <summary>
    /// Generates comprehension questions based on code structure.
    /// </summary>
    private List<LlmComprehensionQuestion> GenerateLlmComprehensionQuestions(EvaluationContext context)
    {
        var questions = new List<LlmComprehensionQuestion>
        {
            new()
            {
                Id = "purpose",
                Question = "What is the main purpose of this code? Answer in one sentence.",
                Category = QuestionCategory.Semantics,
                ExpectedAnswer = null // Will be evaluated semantically
            },
            new()
            {
                Id = "inputs",
                Question = "What are the input parameters and their types?",
                Category = QuestionCategory.Structure
            },
            new()
            {
                Id = "outputs",
                Question = "What does this code return?",
                Category = QuestionCategory.Behavior
            }
        };

        // Add contract-specific questions if contracts are present
        if (context.CalorSource.Contains("§REQ") || context.CalorSource.Contains("§ENS"))
        {
            questions.Add(new LlmComprehensionQuestion
            {
                Id = "contracts",
                Question = "What preconditions must be satisfied before calling this function?",
                Category = QuestionCategory.Contracts
            });
        }

        // Add effect-specific questions if effects are present
        if (context.CalorSource.Contains("§E{"))
        {
            questions.Add(new LlmComprehensionQuestion
            {
                Id = "effects",
                Question = "What side effects does this code have?",
                Category = QuestionCategory.Effects
            });
        }

        return questions;
    }

    /// <summary>
    /// Asks an LLM a question about code and returns the response.
    /// </summary>
    private async Task<LlmResponse> AskQuestion(
        LlmProvider provider,
        string code,
        string question,
        string languageName)
    {
        // TODO: Implement actual LLM API calls
        // For now, return a placeholder response

        var prompt = $@"Given the following {languageName} code:

```
{code}
```

{question}";

        // Simulate API call delay
        await Task.Delay(10);

        return provider switch
        {
            LlmProvider.Claude => await CallClaudeApi(prompt),
            LlmProvider.Gpt4 => await CallOpenAiApi(prompt),
            _ => new LlmResponse { Answer = "", TokensUsed = 0 }
        };
    }

    private async Task<LlmResponse> CallClaudeApi(string prompt)
    {
        // TODO: Implement Anthropic API call
        // var apiKey = _options.AnthropicApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        // Placeholder - actual implementation would use HttpClient to call the Anthropic API
        await Task.Delay(1);
        return new LlmResponse
        {
            Answer = "[Claude API not implemented]",
            TokensUsed = 0
        };
    }

    private async Task<LlmResponse> CallOpenAiApi(string prompt)
    {
        // TODO: Implement OpenAI API call
        // var apiKey = _options.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        // Placeholder - actual implementation would use HttpClient to call the OpenAI API
        await Task.Delay(1);
        return new LlmResponse
        {
            Answer = "[OpenAI API not implemented]",
            TokensUsed = 0
        };
    }

    /// <summary>
    /// Evaluates if an answer is correct compared to expected answer.
    /// </summary>
    private static double EvaluateAnswer(string actual, string? expected)
    {
        if (string.IsNullOrEmpty(actual))
            return 0;

        if (expected == null)
            return 0.5; // For open-ended questions, assume partial credit

        // Exact match
        if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        // Check if answer contains expected content
        if (actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            return 0.8;

        // Word overlap scoring
        var expectedWords = expected.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var actualWords = actual.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var overlap = expectedWords.Count(e => actualWords.Contains(e));
        var overlapScore = expectedWords.Length > 0 ? (double)overlap / expectedWords.Length : 0;

        return Math.Min(overlapScore, 0.6); // Cap at 0.6 for partial matches
    }

    /// <summary>
    /// Calculates agreement between different LLM providers.
    /// </summary>
    private static double CalculateCrossModelAgreement(List<LlmEvaluationResult> results)
    {
        if (results.Count < 2)
            return 1.0;

        var calorScores = results.Select(r => r.CalorScore).ToList();
        var csharpScores = results.Select(r => r.CSharpScore).ToList();

        // Calculate coefficient of variation (lower = more agreement)
        var calorCv = CalculateCoefficientOfVariation(calorScores);
        var csharpCv = CalculateCoefficientOfVariation(csharpScores);

        // Convert to agreement score (1 - CV, capped at 0)
        var avgCv = (calorCv + csharpCv) / 2;
        return Math.Max(0, 1 - avgCv);
    }

    private static double CalculateCoefficientOfVariation(List<double> values)
    {
        if (values.Count == 0) return 0;
        var mean = values.Average();
        if (mean == 0) return 0;
        var stdDev = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
        return stdDev / mean;
    }
}

#region Supporting Types

/// <summary>
/// Options for configuring LLM evaluation.
/// </summary>
public class LlmEvaluationOptions
{
    public bool Enabled { get; set; }
    public string? AnthropicApiKey { get; set; }
    public string? OpenAiApiKey { get; set; }
    public int MaxTokensPerRequest { get; set; } = 1000;
    public int QuestionsPerBenchmark { get; set; } = 5;
}

/// <summary>
/// Supported LLM providers.
/// </summary>
public enum LlmProvider
{
    Claude,
    Gpt4,
    Gemini
}

/// <summary>
/// A comprehension question for LLM evaluation.
/// </summary>
public class LlmComprehensionQuestion
{
    public required string Id { get; init; }
    public required string Question { get; init; }
    public QuestionCategory Category { get; init; }
    public string? ExpectedAnswer { get; init; }
}

/// <summary>
/// Categories of comprehension questions.
/// </summary>
public enum QuestionCategory
{
    Semantics,
    Behavior,
    Structure,
    Contracts,
    Effects,
    Algorithm
}

/// <summary>
/// Response from an LLM API call.
/// </summary>
public class LlmResponse
{
    public required string Answer { get; init; }
    public int TokensUsed { get; init; }
}

/// <summary>
/// Result from evaluating with a single LLM provider.
/// </summary>
public class LlmEvaluationResult
{
    public LlmProvider Provider { get; init; }
    public double CalorScore { get; init; }
    public double CSharpScore { get; init; }
    public int QuestionsAnswered { get; init; }
    public int CalorTokensUsed { get; init; }
    public int CSharpTokensUsed { get; init; }
}

#endregion
