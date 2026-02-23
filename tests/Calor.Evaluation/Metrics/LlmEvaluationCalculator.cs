using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// LLM-based evaluation calculator that uses AI models to assess code comprehension.
/// Uses Claude API to answer questions about Calor and C# code, then uses LLM-as-judge
/// to score answers against ground truth. This captures the genuine comprehension advantage
/// of Calor's explicit contracts and effects.
/// </summary>
public class LlmEvaluationCalculator : IMetricCalculator
{
    public string Category => "LlmEvaluation";

    public string Description => "Measures code comprehension using LLM-based question answering";

    private readonly LlmEvaluationOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private List<LlmQuestionSet>? _loadedQuestions;

    private const string AnthropicApiEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicApiVersion = "2023-06-01";
    private const string DefaultModel = "claude-sonnet-4-20250514";
    private const string JudgeModel = "claude-haiku-4-5-20251001";

    public LlmEvaluationCalculator(LlmEvaluationOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new LlmEvaluationOptions();
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
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

        var apiKey = _options.AnthropicApiKey ??
                     Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        if (string.IsNullOrEmpty(apiKey))
        {
            return new MetricResult(
                Category,
                "Comprehension",
                0, 0, 1.0,
                new Dictionary<string, object> { ["status"] = "no_api_keys" });
        }

        // Load questions for this file from the question bank
        var questions = GetQuestionsForFile(context.FileName);

        // If no curated questions, generate structural ones
        if (questions.Count == 0)
            questions = GenerateStructuralQuestions(context);

        if (questions.Count == 0)
        {
            return new MetricResult(
                Category,
                "Comprehension",
                0, 0, 1.0,
                new Dictionary<string, object> { ["status"] = "no_questions" });
        }

        var calorTotalScore = 0.0;
        var csharpTotalScore = 0.0;
        var totalCalorTokens = 0;
        var totalCsharpTokens = 0;
        var questionDetails = new List<Dictionary<string, object>>();

        foreach (var question in questions)
        {
            // Ask the answering LLM about Calor code
            var calorResponse = await AskQuestionViaApi(
                apiKey, context.CalorSource, question.Question, "Calor");

            // Ask the answering LLM about C# code
            var csharpResponse = await AskQuestionViaApi(
                apiKey, context.CSharpSource, question.Question, "C#");

            // Score both answers using LLM-as-judge (or fallback to heuristic)
            double calorScore, csharpScore;
            if (_options.UseLlmJudge && question.ExpectedAnswer != null)
            {
                calorScore = await JudgeAnswerViaApi(
                    apiKey, question.Question, calorResponse.Answer, question.ExpectedAnswer);
                csharpScore = await JudgeAnswerViaApi(
                    apiKey, question.Question, csharpResponse.Answer, question.ExpectedAnswer);
            }
            else
            {
                calorScore = EvaluateAnswerHeuristic(calorResponse.Answer, question.ExpectedAnswer);
                csharpScore = EvaluateAnswerHeuristic(csharpResponse.Answer, question.ExpectedAnswer);
            }

            calorTotalScore += calorScore;
            csharpTotalScore += csharpScore;
            totalCalorTokens += calorResponse.TokensUsed;
            totalCsharpTokens += csharpResponse.TokensUsed;

            questionDetails.Add(new Dictionary<string, object>
            {
                ["questionId"] = question.Id,
                ["question"] = question.Question,
                ["category"] = question.Category.ToString(),
                ["calorScore"] = calorScore,
                ["csharpScore"] = csharpScore,
                ["calorAnswer"] = calorResponse.Answer,
                ["csharpAnswer"] = csharpResponse.Answer
            });
        }

        var questionCount = questions.Count;
        var calorAvg = questionCount > 0 ? calorTotalScore / questionCount : 0;
        var csharpAvg = questionCount > 0 ? csharpTotalScore / questionCount : 0;

        var details = new Dictionary<string, object>
        {
            ["questionsEvaluated"] = questionCount,
            ["calorTokensUsed"] = totalCalorTokens,
            ["csharpTokensUsed"] = totalCsharpTokens,
            ["usedLlmJudge"] = _options.UseLlmJudge,
            ["questionResults"] = questionDetails
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "Comprehension",
            calorAvg,
            csharpAvg,
            details);
    }

    /// <summary>
    /// Loads questions for a specific file from the question bank.
    /// </summary>
    private List<LlmComprehensionQuestion> GetQuestionsForFile(string fileName)
    {
        _loadedQuestions ??= LoadQuestionsFromBank();

        var set = _loadedQuestions.FirstOrDefault(s =>
            string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (set == null) return new List<LlmComprehensionQuestion>();

        return set.Questions
            .Take(_options.QuestionsPerBenchmark)
            .Select(q => new LlmComprehensionQuestion
            {
                Id = q.Id,
                Question = q.Question,
                Category = ParseCategory(q.Category),
                ExpectedAnswer = q.Answer
            })
            .ToList();
    }

    /// <summary>
    /// Loads the question bank from the Comprehension/questions.json file.
    /// </summary>
    private List<LlmQuestionSet> LoadQuestionsFromBank()
    {
        if (!string.IsNullOrEmpty(_options.QuestionsFilePath) && File.Exists(_options.QuestionsFilePath))
        {
            try
            {
                var json = File.ReadAllText(_options.QuestionsFilePath);
                return ParseQuestionSets(json);
            }
            catch
            {
                // Fall through to empty
            }
        }

        return new List<LlmQuestionSet>();
    }

    private static List<LlmQuestionSet> ParseQuestionSets(string json)
    {
        var doc = JsonDocument.Parse(json);
        var sets = new List<LlmQuestionSet>();

        if (!doc.RootElement.TryGetProperty("questionSets", out var setsArray))
            return sets;

        foreach (var setEl in setsArray.EnumerateArray())
        {
            var set = new LlmQuestionSet
            {
                FileId = setEl.GetProperty("fileId").GetString() ?? "",
                FileName = setEl.GetProperty("fileName").GetString() ?? ""
            };

            if (setEl.TryGetProperty("questions", out var questionsArray))
            {
                foreach (var qEl in questionsArray.EnumerateArray())
                {
                    set.Questions.Add(new LlmQuestionEntry
                    {
                        Id = qEl.GetProperty("id").GetString() ?? "",
                        Question = qEl.GetProperty("question").GetString() ?? "",
                        Answer = qEl.GetProperty("answer").GetString() ?? "",
                        Category = qEl.TryGetProperty("category", out var cat)
                            ? cat.GetString() ?? "behavior" : "behavior",
                        Difficulty = qEl.TryGetProperty("difficulty", out var diff)
                            ? diff.GetInt32() : 1
                    });
                }
            }

            sets.Add(set);
        }

        return sets;
    }

    private static QuestionCategory ParseCategory(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "semantics" => QuestionCategory.Semantics,
            "behavior" => QuestionCategory.Behavior,
            "structure" => QuestionCategory.Structure,
            "contracts" => QuestionCategory.Contracts,
            "effects" => QuestionCategory.Effects,
            "algorithm" => QuestionCategory.Algorithm,
            _ => QuestionCategory.Behavior
        };
    }

    /// <summary>
    /// Generates structural comprehension questions when no curated questions exist.
    /// </summary>
    private static List<LlmComprehensionQuestion> GenerateStructuralQuestions(EvaluationContext context)
    {
        var questions = new List<LlmComprehensionQuestion>
        {
            new()
            {
                Id = "purpose",
                Question = "What is the main purpose of this code? Answer in one sentence.",
                Category = QuestionCategory.Semantics,
                ExpectedAnswer = null
            },
            new()
            {
                Id = "inputs",
                Question = "What are the input parameters and their types? List each parameter.",
                Category = QuestionCategory.Structure,
                ExpectedAnswer = null
            },
            new()
            {
                Id = "outputs",
                Question = "What does this code return? Describe the return type and meaning.",
                Category = QuestionCategory.Behavior,
                ExpectedAnswer = null
            }
        };

        // Add contract-specific questions if contracts are present
        if (context.CalorSource.Contains("§Q ") || context.CalorSource.Contains("§S "))
        {
            questions.Add(new LlmComprehensionQuestion
            {
                Id = "contracts",
                Question = "What preconditions must be satisfied before calling this function? What postconditions does it guarantee?",
                Category = QuestionCategory.Contracts,
                ExpectedAnswer = null
            });
        }

        // Add effect-specific questions if effects are present
        if (context.CalorSource.Contains("§E{"))
        {
            questions.Add(new LlmComprehensionQuestion
            {
                Id = "effects",
                Question = "What side effects does this code have? (e.g., console output, file I/O, network, database)",
                Category = QuestionCategory.Effects,
                ExpectedAnswer = null
            });
        }

        return questions;
    }

    /// <summary>
    /// Calls the Claude API to answer a question about code.
    /// </summary>
    private async Task<LlmResponse> AskQuestionViaApi(
        string apiKey, string code, string question, string languageName)
    {
        var systemPrompt = $"You are a code analysis expert. You will be given {languageName} code and a question about it. " +
                           "Answer concisely and accurately. Focus only on what can be determined from the code shown.";

        var userPrompt = $"Given the following {languageName} code:\n\n```\n{code}\n```\n\n{question}\n\nAnswer concisely:";

        try
        {
            var result = await CallClaudeApi(apiKey, systemPrompt, userPrompt, DefaultModel,
                _options.MaxTokensPerRequest);

            return new LlmResponse
            {
                Answer = result.text,
                TokensUsed = result.inputTokens + result.outputTokens
            };
        }
        catch (Exception ex)
        {
            return new LlmResponse
            {
                Answer = $"[API error: {ex.Message}]",
                TokensUsed = 0
            };
        }
    }

    /// <summary>
    /// Uses an LLM as a judge to score an answer against the expected answer on a 0-1 scale.
    /// This replaces naive string matching with semantic evaluation.
    /// </summary>
    private async Task<double> JudgeAnswerViaApi(
        string apiKey, string question, string actualAnswer, string expectedAnswer)
    {
        if (string.IsNullOrWhiteSpace(actualAnswer) || actualAnswer.StartsWith("[API error"))
            return 0.0;

        var systemPrompt = "You are a grading assistant. Rate how well the actual answer matches the expected answer. " +
                           "Output ONLY a number between 0.0 and 1.0. " +
                           "1.0 = perfect match (same meaning), 0.8 = mostly correct, 0.5 = partially correct, 0.0 = wrong.";

        var userPrompt = $"Question: {question}\n\nExpected answer: {expectedAnswer}\n\nActual answer: {actualAnswer}\n\nScore (0.0-1.0):";

        try
        {
            var result = await CallClaudeApi(apiKey, systemPrompt, userPrompt, JudgeModel, 50);

            // Parse the score from the response
            var text = result.text.Trim();
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var score))
            {
                return Math.Clamp(score, 0.0, 1.0);
            }

            // Try to extract a number from the response
            var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d+\.?\d*)");
            if (match.Success && double.TryParse(match.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var extractedScore))
            {
                return Math.Clamp(extractedScore, 0.0, 1.0);
            }

            // Fallback to heuristic
            return EvaluateAnswerHeuristic(actualAnswer, expectedAnswer);
        }
        catch
        {
            return EvaluateAnswerHeuristic(actualAnswer, expectedAnswer);
        }
    }

    /// <summary>
    /// Calls the Claude API with the given prompts.
    /// </summary>
    private async Task<(string text, int inputTokens, int outputTokens)> CallClaudeApi(
        string apiKey, string systemPrompt, string userPrompt, string model, int maxTokens)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AnthropicApiEndpoint);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicApiVersion);

        var body = new
        {
            model = model,
            max_tokens = maxTokens,
            temperature = 0.0,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(body, jsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await _httpClient.SendAsync(request, cts.Token);
        var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Claude API error: {response.StatusCode} - {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var text = "";
        if (root.TryGetProperty("content", out var content))
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                    block.TryGetProperty("text", out var textEl))
                {
                    text += textEl.GetString();
                }
            }
        }

        var inputTokens = 0;
        var outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var inTok))
                inputTokens = inTok.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var outTok))
                outputTokens = outTok.GetInt32();
        }

        return (text, inputTokens, outputTokens);
    }

    /// <summary>
    /// Evaluates if an answer is correct using heuristic matching (fallback when LLM judge is unavailable).
    /// </summary>
    private static double EvaluateAnswerHeuristic(string actual, string? expected)
    {
        if (string.IsNullOrEmpty(actual) || actual.StartsWith("[API error") || actual.StartsWith("[Claude API"))
            return 0;

        if (expected == null)
            return 0.5; // For open-ended questions, assume partial credit

        // Exact match
        if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        // Check if answer contains expected content
        if (actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            return 0.8;

        // Expected contains actual
        if (expected.Contains(actual, StringComparison.OrdinalIgnoreCase))
            return 0.6;

        // Word overlap scoring
        var expectedWords = expected.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var actualWords = actual.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var overlap = expectedWords.Count(e => actualWords.Contains(e));
        var overlapScore = expectedWords.Length > 0 ? (double)overlap / expectedWords.Length : 0;

        return Math.Min(overlapScore, 0.6); // Cap at 0.6 for partial matches
    }

    /// <summary>
    /// Calculates agreement between different evaluation runs.
    /// </summary>
    private static double CalculateCrossModelAgreement(List<LlmEvaluationResult> results)
    {
        if (results.Count < 2)
            return 1.0;

        var calorScores = results.Select(r => r.CalorScore).ToList();
        var csharpScores = results.Select(r => r.CSharpScore).ToList();

        var calorCv = CalculateCoefficientOfVariation(calorScores);
        var csharpCv = CalculateCoefficientOfVariation(csharpScores);

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
    public int MaxTokensPerRequest { get; set; } = 500;
    public int QuestionsPerBenchmark { get; set; } = 5;

    /// <summary>
    /// Path to the questions.json file containing the question bank.
    /// </summary>
    public string? QuestionsFilePath { get; set; }

    /// <summary>
    /// Whether to use LLM-as-judge for answer evaluation (more accurate but costs tokens).
    /// When false, falls back to heuristic string matching.
    /// </summary>
    public bool UseLlmJudge { get; set; } = true;
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

/// <summary>
/// A question set loaded from the question bank file.
/// </summary>
internal class LlmQuestionSet
{
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public List<LlmQuestionEntry> Questions { get; init; } = new();
}

/// <summary>
/// A single question entry from the question bank.
/// </summary>
internal class LlmQuestionEntry
{
    public required string Id { get; init; }
    public required string Question { get; init; }
    public required string Answer { get; init; }
    public string Category { get; init; } = "behavior";
    public int Difficulty { get; init; } = 1;
}

#endregion
