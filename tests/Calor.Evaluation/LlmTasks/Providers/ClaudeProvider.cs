using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Evaluation.LlmTasks.Providers;

/// <summary>
/// LLM provider implementation for Anthropic Claude API.
/// </summary>
public sealed class ClaudeProvider : ILlmProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly bool _ownsHttpClient;

    private const string ApiEndpoint = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    /// <summary>
    /// Claude Sonnet 4 (good balance of quality and cost).
    /// </summary>
    public const string ModelSonnet = "claude-sonnet-4-20250514";

    /// <summary>
    /// Claude Haiku 3.5 (fast and cheap).
    /// </summary>
    public const string ModelHaiku = "claude-3-5-haiku-latest";

    /// <summary>
    /// Claude Opus 4.5 (highest quality).
    /// </summary>
    public const string ModelOpus = "claude-opus-4-5-20251101";

    // Pricing per million tokens (as of Feb 2025, from Anthropic docs)
    // See: https://docs.anthropic.com/en/docs/build-with-claude/prompt-caching#pricing
    private static readonly Dictionary<string, (decimal Input, decimal Output)> Pricing = new()
    {
        [ModelSonnet] = (3.00m, 15.00m),      // Sonnet 4
        [ModelHaiku] = (0.80m, 4.00m),        // Haiku 3.5
        [ModelOpus] = (5.00m, 25.00m),        // Opus 4.5 (updated from $15/$75)
        // Fallback for unknown models
        ["default"] = (3.00m, 15.00m)
    };

    public string Name => "claude";
    public string DefaultModel => ModelSonnet;

    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
    public string? UnavailabilityReason =>
        string.IsNullOrEmpty(_apiKey) ? "ANTHROPIC_API_KEY environment variable is not set" : null;

    /// <summary>
    /// Creates a new Claude provider.
    /// </summary>
    /// <param name="apiKey">API key (uses ANTHROPIC_API_KEY env var if null).</param>
    /// <param name="httpClient">Optional HTTP client to use.</param>
    public ClaudeProvider(string? apiKey = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
        }
    }

    public async Task<LlmGenerationResult> GenerateCodeAsync(
        string prompt,
        string language,
        LlmGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return LlmGenerationResult.Failed(Name, UnavailabilityReason!);
        }

        options ??= LlmGenerationOptions.Default;
        var model = options.Model ?? DefaultModel;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var systemPrompt = options.SystemPrompt ?? LlmGenerationOptions.Default.SystemPrompt ?? "You are a helpful coding assistant.";
            var request = new ClaudeRequest
            {
                Model = model,
                MaxTokens = options.MaxTokens,
                Temperature = options.Temperature,
                // Use structured system prompt with cache_control for prompt caching
                // This gives 90% discount on cached tokens for requests within 5 minutes
                System = new List<ClaudeSystemBlock>
                {
                    new()
                    {
                        Type = "text",
                        Text = systemPrompt,
                        CacheControl = new CacheControl { Type = "ephemeral" }
                    }
                },
                Messages = new List<ClaudeMessage>
                {
                    new() { Role = "user", Content = prompt }
                }
            };

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(request, jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

            var response = await _httpClient.PostAsync(ApiEndpoint, content, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return LlmGenerationResult.Failed(Name,
                    $"API error: {response.StatusCode} - {responseBody}");
            }

            var claudeResponse = JsonSerializer.Deserialize<ClaudeResponse>(responseBody, jsonOptions);
            if (claudeResponse == null)
            {
                return LlmGenerationResult.Failed(Name, "Failed to parse API response");
            }

            var generatedCode = ExtractCode(claudeResponse.Content);
            var usage = claudeResponse.Usage;
            var inputTokens = usage?.InputTokens ?? EstimateTokenCount(prompt);
            var outputTokens = usage?.OutputTokens ?? EstimateTokenCount(generatedCode);
            var cacheReadTokens = usage?.CacheReadInputTokens ?? 0;
            var cacheCreationTokens = usage?.CacheCreationInputTokens ?? 0;

            var cost = EstimateCostWithCache(
                inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens, model);

            return LlmGenerationResult.Successful(
                generatedCode,
                Name,
                model,
                inputTokens + cacheReadTokens + cacheCreationTokens,
                outputTokens,
                cost,
                stopwatch.Elapsed.TotalMilliseconds,
                fromCache: false,
                stopReason: claudeResponse.StopReason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LlmGenerationResult.Failed(Name, "Request was cancelled");
        }
        catch (OperationCanceledException)
        {
            return LlmGenerationResult.Failed(Name, "Request timed out");
        }
        catch (Exception ex)
        {
            return LlmGenerationResult.Failed(Name, $"Unexpected error: {ex.Message}");
        }
    }

    public decimal EstimateCost(int inputTokens, int outputTokens, string? model = null)
    {
        return EstimateCostWithCache(inputTokens, outputTokens, 0, 0, model);
    }

    /// <summary>
    /// Estimates cost accounting for prompt caching savings.
    /// Five-minute cache writes cost 125% of base input; reads cost 10%.
    /// </summary>
    private decimal EstimateCostWithCache(
        int inputTokens, int outputTokens, int cacheReadTokens,
        int cacheCreationTokens, string? model = null)
    {
        model ??= DefaultModel;
        var (inputPrice, outputPrice) = Pricing.GetValueOrDefault(model, Pricing["default"]);

        // Anthropic reports uncached input, cache reads and cache writes separately.
        var inputCost = (inputTokens / 1_000_000m) * inputPrice;
        var cacheWriteCost = (cacheCreationTokens / 1_000_000m) * inputPrice * 1.25m;

        // Cached tokens at 10% price (90% discount)
        var cachedInputCost = (cacheReadTokens / 1_000_000m) * inputPrice * 0.1m;

        // Output tokens at full price
        var outputCost = (outputTokens / 1_000_000m) * outputPrice;

        return Math.Round(inputCost + cacheWriteCost + cachedInputCost + outputCost, 6);
    }

    public int EstimateTokenCount(string text)
    {
        // Rough estimation: ~4 characters per token for code
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Extracts code from Claude's response content.
    /// Handles both plain text and code blocks.
    /// </summary>
    private static string ExtractCode(List<ClaudeContentBlock>? content)
    {
        if (content == null || content.Count == 0)
            return "";

        var text = string.Join("\n", content
            .Where(c => c.Type == "text")
            .Select(c => c.Text ?? ""));

        // Try to extract code from markdown code blocks
        var codeBlockPattern = @"```(?:calor|csharp|cs|c#)?\s*\n([\s\S]*?)```";
        var match = System.Text.RegularExpressions.Regex.Match(text, codeBlockPattern);

        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // If no code block, return the raw text (might be just code)
        return text.Trim();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    #region API Models

    private class ClaudeRequest
    {
        public required string Model { get; set; }
        public int MaxTokens { get; set; }
        public double Temperature { get; set; }
        // System prompt as array of content blocks for prompt caching support
        public List<ClaudeSystemBlock>? System { get; set; }
        public required List<ClaudeMessage> Messages { get; set; }
    }

    private class ClaudeSystemBlock
    {
        public required string Type { get; set; }
        public required string Text { get; set; }
        public CacheControl? CacheControl { get; set; }
    }

    private class CacheControl
    {
        public required string Type { get; set; }
    }

    private class ClaudeMessage
    {
        public required string Role { get; set; }
        public required string Content { get; set; }
    }

    private class ClaudeResponse
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Role { get; set; }
        public List<ClaudeContentBlock>? Content { get; set; }
        public string? Model { get; set; }
        public string? StopReason { get; set; }
        public ClaudeUsage? Usage { get; set; }
    }

    private class ClaudeContentBlock
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
    }

    private class ClaudeUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        // Prompt caching metrics
        public int CacheCreationInputTokens { get; set; }
        public int CacheReadInputTokens { get; set; }
    }

    #endregion
}
