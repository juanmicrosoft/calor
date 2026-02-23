using System.Net;
using System.Text;
using System.Text.Json;
using Calor.Evaluation.Core;
using Calor.Evaluation.Metrics;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Tests for LlmEvaluationCalculator — verifies question loading, heuristic scoring,
/// API call structure, and disabled/no-key behavior without requiring real API keys.
/// </summary>
public class LlmEvaluationCalculatorTests
{
    #region Disabled / No-Key Tests

    [Fact]
    public async Task CalculateAsync_WhenDisabled_ReturnsDisabledStatus()
    {
        var calculator = new LlmEvaluationCalculator(new LlmEvaluationOptions
        {
            Enabled = false
        });

        var context = CreateContext();
        var result = await calculator.CalculateAsync(context);

        Assert.Equal("LlmEvaluation", result.Category);
        Assert.Equal("disabled", result.Details["status"]);
        Assert.Equal(0.0, result.CalorScore);
        Assert.Equal(0.0, result.CSharpScore);
    }

    [Fact]
    public async Task CalculateAsync_WithNoApiKey_ReturnsNoApiKeysStatus()
    {
        // Clear the env var to ensure no key is found
        var original = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);

            var calculator = new LlmEvaluationCalculator(new LlmEvaluationOptions
            {
                Enabled = true,
                AnthropicApiKey = null
            });

            var context = CreateContext();
            var result = await calculator.CalculateAsync(context);

            Assert.Equal("no_api_keys", result.Details["status"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", original);
        }
    }

    #endregion

    #region Question Loading Tests

    [Fact]
    public async Task CalculateAsync_WithQuestionsFile_LoadsQuestions()
    {
        // Create a temp questions file
        var tempFile = Path.GetTempFileName();
        try
        {
            var questionsJson = """
            {
              "version": "2.0",
              "questionSets": [
                {
                  "fileId": "test",
                  "fileName": "test",
                  "questions": [
                    {
                      "id": "q1",
                      "question": "What does this return?",
                      "answer": "42",
                      "category": "behavior",
                      "difficulty": 1
                    }
                  ]
                }
              ]
            }
            """;
            File.WriteAllText(tempFile, questionsJson);

            // Use a mock HTTP handler that returns a valid Claude response
            var handler = new MockHttpHandler("""
                {
                    "content": [{"type": "text", "text": "42"}],
                    "usage": {"input_tokens": 100, "output_tokens": 10}
                }
                """);

            var calculator = new LlmEvaluationCalculator(
                new LlmEvaluationOptions
                {
                    Enabled = true,
                    AnthropicApiKey = "test-key-not-real",
                    QuestionsFilePath = tempFile,
                    QuestionsPerBenchmark = 5,
                    UseLlmJudge = false // Use heuristic to avoid extra API calls
                },
                new HttpClient(handler));

            var context = CreateContext();
            var result = await calculator.CalculateAsync(context);

            Assert.Equal("LlmEvaluation", result.Category);
            // Should have evaluated questions
            Assert.True(result.Details.ContainsKey("questionsEvaluated"));
            var questionsEvaluated = Convert.ToInt32(result.Details["questionsEvaluated"]);
            Assert.Equal(1, questionsEvaluated);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CalculateAsync_WithNoQuestionsForFile_GeneratesStructural()
    {
        // Create a temp questions file with questions for a different file
        var tempFile = Path.GetTempFileName();
        try
        {
            var questionsJson = """
            {
              "version": "2.0",
              "questionSets": [
                {
                  "fileId": "other",
                  "fileName": "OtherFile",
                  "questions": [
                    { "id": "q1", "question": "test?", "answer": "yes", "category": "behavior", "difficulty": 1 }
                  ]
                }
              ]
            }
            """;
            File.WriteAllText(tempFile, questionsJson);

            var handler = new MockHttpHandler("""
                {
                    "content": [{"type": "text", "text": "some answer"}],
                    "usage": {"input_tokens": 50, "output_tokens": 10}
                }
                """);

            var calculator = new LlmEvaluationCalculator(
                new LlmEvaluationOptions
                {
                    Enabled = true,
                    AnthropicApiKey = "test-key-not-real",
                    QuestionsFilePath = tempFile,
                    UseLlmJudge = false
                },
                new HttpClient(handler));

            // Context file is "test" which doesn't match "OtherFile"
            var context = CreateContext();
            var result = await calculator.CalculateAsync(context);

            // Should fall back to structural questions
            var questionsEvaluated = Convert.ToInt32(result.Details["questionsEvaluated"]);
            Assert.True(questionsEvaluated >= 3, // At least purpose, inputs, outputs
                $"Should generate at least 3 structural questions, got {questionsEvaluated}");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CalculateAsync_WithMissingQuestionsFile_GeneratesStructural()
    {
        var handler = new MockHttpHandler("""
            {
                "content": [{"type": "text", "text": "answer"}],
                "usage": {"input_tokens": 50, "output_tokens": 10}
            }
            """);

        var calculator = new LlmEvaluationCalculator(
            new LlmEvaluationOptions
            {
                Enabled = true,
                AnthropicApiKey = "test-key-not-real",
                QuestionsFilePath = "/nonexistent/path/questions.json",
                UseLlmJudge = false
            },
            new HttpClient(handler));

        var context = CreateContext();
        var result = await calculator.CalculateAsync(context);

        var questionsEvaluated = Convert.ToInt32(result.Details["questionsEvaluated"]);
        Assert.True(questionsEvaluated >= 3, "Should generate structural questions as fallback");
    }

    #endregion

    #region Heuristic Scoring Tests

    [Fact]
    public async Task CalculateAsync_WithHeuristicScoring_ProducesValidScores()
    {
        var handler = new MockHttpHandler("""
            {
                "content": [{"type": "text", "text": "Returns 42"}],
                "usage": {"input_tokens": 50, "output_tokens": 10}
            }
            """);

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "version": "2.0",
              "questionSets": [
                {
                  "fileId": "test",
                  "fileName": "test",
                  "questions": [
                    { "id": "q1", "question": "What does this return?", "answer": "Returns 42", "category": "behavior", "difficulty": 1 }
                  ]
                }
              ]
            }
            """);

            var calculator = new LlmEvaluationCalculator(
                new LlmEvaluationOptions
                {
                    Enabled = true,
                    AnthropicApiKey = "test-key-not-real",
                    QuestionsFilePath = tempFile,
                    UseLlmJudge = false
                },
                new HttpClient(handler));

            var context = CreateContext();
            var result = await calculator.CalculateAsync(context);

            // Exact match should give high score
            Assert.True(result.CalorScore >= 0.8, $"Exact match should score >= 0.8, got {result.CalorScore}");
            Assert.True(result.CSharpScore >= 0.8, $"Exact match should score >= 0.8, got {result.CSharpScore}");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region API Call Structure Tests

    [Fact]
    public async Task CalculateAsync_SendsCorrectHeaders()
    {
        var handler = new MockHttpHandler("""
            {
                "content": [{"type": "text", "text": "test"}],
                "usage": {"input_tokens": 10, "output_tokens": 5}
            }
            """);

        var calculator = new LlmEvaluationCalculator(
            new LlmEvaluationOptions
            {
                Enabled = true,
                AnthropicApiKey = "test-api-key",
                QuestionsFilePath = null, // Will use structural questions
                UseLlmJudge = false
            },
            new HttpClient(handler));

        var context = CreateContext();
        await calculator.CalculateAsync(context);

        // Verify at least one request was made
        Assert.True(handler.RequestCount > 0, "Should have made API requests");

        // Verify the API key header was set
        var lastRequest = handler.LastRequest!;
        Assert.Contains("test-api-key", lastRequest.Headers.GetValues("x-api-key"));
        Assert.Contains("2023-06-01", lastRequest.Headers.GetValues("anthropic-version"));
    }

    [Fact]
    public async Task CalculateAsync_WithApiError_ProducesZeroScores()
    {
        var handler = new MockHttpHandler("Internal Server Error", HttpStatusCode.InternalServerError);

        var calculator = new LlmEvaluationCalculator(
            new LlmEvaluationOptions
            {
                Enabled = true,
                AnthropicApiKey = "test-api-key",
                UseLlmJudge = false
            },
            new HttpClient(handler));

        var context = CreateContext();
        var result = await calculator.CalculateAsync(context);

        // API errors should result in 0 scores (answers will contain "[API error: ...]")
        Assert.Equal(0.0, result.CalorScore);
        Assert.Equal(0.0, result.CSharpScore);
    }

    #endregion

    #region Contract/Effect Question Generation Tests

    [Fact]
    public async Task CalculateAsync_WithContracts_GeneratesContractQuestions()
    {
        var handler = new MockHttpHandler("""
            {
                "content": [{"type": "text", "text": "answer"}],
                "usage": {"input_tokens": 10, "output_tokens": 5}
            }
            """);

        var calculator = new LlmEvaluationCalculator(
            new LlmEvaluationOptions
            {
                Enabled = true,
                AnthropicApiKey = "test-key",
                UseLlmJudge = false
            },
            new HttpClient(handler));

        // Source with contracts
        var context = new EvaluationContext
        {
            CalorSource = "§M{m001:T} §F{f001:F:pub} §I{i32:x} §O{i32} §Q (> x 0) §S (>= result 0) §R x §/F{f001} §/M{m001}",
            CSharpSource = "class T { int F(int x) { return x; } }",
            FileName = "unmatched_name", // Won't find in question bank
            Level = 1,
            Features = new List<string>()
        };

        var result = await calculator.CalculateAsync(context);

        var questionsEvaluated = Convert.ToInt32(result.Details["questionsEvaluated"]);
        // Should have at least 4: purpose + inputs + outputs + contracts
        Assert.True(questionsEvaluated >= 4,
            $"Source with contracts should generate >= 4 questions, got {questionsEvaluated}");
    }

    [Fact]
    public async Task CalculateAsync_WithEffects_GeneratesEffectQuestions()
    {
        var handler = new MockHttpHandler("""
            {
                "content": [{"type": "text", "text": "answer"}],
                "usage": {"input_tokens": 10, "output_tokens": 5}
            }
            """);

        var calculator = new LlmEvaluationCalculator(
            new LlmEvaluationOptions
            {
                Enabled = true,
                AnthropicApiKey = "test-key",
                UseLlmJudge = false
            },
            new HttpClient(handler));

        var context = new EvaluationContext
        {
            CalorSource = "§M{m001:T} §F{f001:Log:pub} §I{str:msg} §O{void} §E{cw} §P msg §/F{f001} §/M{m001}",
            CSharpSource = "class T { void Log(string msg) { Console.WriteLine(msg); } }",
            FileName = "unmatched_name",
            Level = 1,
            Features = new List<string>()
        };

        var result = await calculator.CalculateAsync(context);

        var questionsEvaluated = Convert.ToInt32(result.Details["questionsEvaluated"]);
        // Should have at least 4: purpose + inputs + outputs + effects
        Assert.True(questionsEvaluated >= 4,
            $"Source with effects should generate >= 4 questions, got {questionsEvaluated}");
    }

    #endregion

    #region Helper Classes

    private static EvaluationContext CreateContext()
    {
        return new EvaluationContext
        {
            CalorSource = "§M{m001:T} §F{f001:F:pub} §O{i32} §R 42 §/F{f001} §/M{m001}",
            CSharpSource = "class T { int F() { return 42; } }",
            FileName = "test",
            Level = 1,
            Features = new List<string>()
        };
    }

    /// <summary>
    /// Mock HTTP handler for testing API calls without hitting real endpoints.
    /// </summary>
    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public int RequestCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        public MockHttpHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = _statusCode,
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    #endregion
}
