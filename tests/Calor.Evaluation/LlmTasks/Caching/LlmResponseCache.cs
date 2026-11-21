using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calor.Evaluation.LlmTasks.Providers;

namespace Calor.Evaluation.LlmTasks.Caching;

/// <summary>
/// Caches LLM API responses to avoid redundant API calls.
/// Uses file-based storage with hash-based keys.
/// </summary>
public sealed class LlmResponseCache
{
    private readonly string _cacheDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a new response cache.
    /// </summary>
    /// <param name="cacheDirectory">Directory to store cache files (defaults to .llm-cache in temp).</param>
    public LlmResponseCache(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ??
            Path.Combine(Path.GetTempPath(), "calor-llm-cache");

        Directory.CreateDirectory(_cacheDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Gets a cached response if available.
    /// </summary>
    /// <param name="provider">Provider name.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="options">Generation options.</param>
    /// <returns>Cached result or null if not found.</returns>
    public async Task<LlmGenerationResult?> GetAsync(
        string provider,
        string prompt,
        LlmGenerationOptions? options)
    {
        var key = ComputeCacheKey(provider, prompt, options);
        var cachePath = GetCachePath(key);

        if (!File.Exists(cachePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(cachePath);
            var entry = JsonSerializer.Deserialize<CacheEntry>(json, _jsonOptions);

            if (entry == null)
                return null;

            // Check if cache is expired (optional TTL)
            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                // Cache expired, delete it
                File.Delete(cachePath);
                return null;
            }

            // Mark as from cache
            return entry.Result with { FromCache = true, Cost = 0m };
        }
        catch
        {
            // Corrupted cache entry, delete it
            try { File.Delete(cachePath); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Stores a response in the cache.
    /// </summary>
    /// <param name="provider">Provider name.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="options">Generation options.</param>
    /// <param name="result">The result to cache.</param>
    /// <param name="ttl">Optional time-to-live.</param>
    public async Task SetAsync(
        string provider,
        string prompt,
        LlmGenerationOptions? options,
        LlmGenerationResult result,
        TimeSpan? ttl = null)
    {
        // Only cache successful results
        if (!result.Success)
            return;

        var key = ComputeCacheKey(provider, prompt, options);
        var cachePath = GetCachePath(key);

        var entry = new CacheEntry
        {
            Key = key,
            Provider = provider,
            PromptHash = ComputeHash(prompt),
            Result = result,
            CachedAt = DateTimeOffset.UtcNow,
            ExpiresAt = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : null
        };

        var json = JsonSerializer.Serialize(entry, _jsonOptions);
        await File.WriteAllTextAsync(cachePath, json);
    }

    /// <summary>
    /// Checks if a response is cached.
    /// </summary>
    public bool Contains(string provider, string prompt, LlmGenerationOptions? options)
    {
        var key = ComputeCacheKey(provider, prompt, options);
        var cachePath = GetCachePath(key);
        return File.Exists(cachePath);
    }

    /// <summary>
    /// Removes a cached response.
    /// </summary>
    public void Remove(string provider, string prompt, LlmGenerationOptions? options)
    {
        var key = ComputeCacheKey(provider, prompt, options);
        var cachePath = GetCachePath(key);

        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }
    }

    /// <summary>
    /// Clears all cached responses.
    /// </summary>
    public void Clear()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    /// <summary>
    /// Gets statistics about the cache.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            return new CacheStatistics();
        }

        var files = Directory.GetFiles(_cacheDirectory, "*.json");
        var totalSize = files.Sum(f => new FileInfo(f).Length);

        return new CacheStatistics
        {
            EntryCount = files.Length,
            TotalSizeBytes = totalSize,
            CacheDirectory = _cacheDirectory
        };
    }

    /// <summary>
    /// Gets all cached entries for a provider.
    /// </summary>
    public async IAsyncEnumerable<CacheEntry> GetAllEntriesAsync(string? provider = null)
    {
        if (!Directory.Exists(_cacheDirectory))
            yield break;

        foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
        {
            CacheEntry? entry = null;
            try
            {
                var json = await File.ReadAllTextAsync(file);
                entry = JsonSerializer.Deserialize<CacheEntry>(json, _jsonOptions);
            }
            catch
            {
                // Skip corrupted entries
            }

            if (entry != null)
            {
                if (provider == null || entry.Provider == provider)
                {
                    yield return entry;
                }
            }
        }
    }

    /// <summary>
    /// Counts cached responses.
    /// </summary>
    public int CountCached(IEnumerable<(string Provider, string Prompt, LlmGenerationOptions? Options)> requests)
    {
        return requests.Count(r => Contains(r.Provider, r.Prompt, r.Options));
    }

    private string ComputeCacheKey(string provider, string prompt, LlmGenerationOptions? options)
    {
        var keyData = new
        {
            Provider = provider,
            Prompt = prompt,
            Model = options?.Model,
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxTokens,
            SystemPrompt = options?.SystemPrompt
        };

        var json = JsonSerializer.Serialize(keyData);
        return ComputeHash(json);
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GetCachePath(string key)
    {
        return Path.Combine(_cacheDirectory, $"{key}.json");
    }

    /// <summary>
    /// Cache entry stored on disk.
    /// </summary>
    public record CacheEntry
    {
        public required string Key { get; init; }
        public required string Provider { get; init; }
        public required string PromptHash { get; init; }
        public required LlmGenerationResult Result { get; init; }
        public DateTimeOffset CachedAt { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
    }

    /// <summary>
    /// Cache statistics.
    /// </summary>
    public record CacheStatistics
    {
        public int EntryCount { get; init; }
        public long TotalSizeBytes { get; init; }
        public string? CacheDirectory { get; init; }

        public string TotalSizeFormatted =>
            TotalSizeBytes switch
            {
                < 1024 => $"{TotalSizeBytes} B",
                < 1024 * 1024 => $"{TotalSizeBytes / 1024.0:F1} KB",
                _ => $"{TotalSizeBytes / (1024.0 * 1024):F1} MB"
            };
    }
}

/// <summary>
/// Extension methods for using cache with providers.
/// </summary>
public static class CachedProviderExtensions
{
    /// <summary>
    /// Wraps a provider with caching support.
    /// </summary>
    public static CachedLlmProvider WithCache(this ILlmProvider provider, LlmResponseCache cache)
    {
        return new CachedLlmProvider(provider, cache);
    }
}

/// <summary>
/// Wrapper that adds caching to any LLM provider.
/// </summary>
public sealed class CachedLlmProvider : ILlmProvider
{
    private readonly ILlmProvider _inner;
    private readonly LlmResponseCache _cache;

    public CachedLlmProvider(ILlmProvider inner, LlmResponseCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public string Name => _inner.Name;
    public string DefaultModel => _inner.DefaultModel;
    public bool IsAvailable => _inner.IsAvailable;
    public string? UnavailabilityReason => _inner.UnavailabilityReason;

    /// <summary>
    /// Whether to bypass cache and make fresh API calls.
    /// </summary>
    public bool RefreshCache { get; set; }

    public async Task<LlmGenerationResult> GenerateCodeAsync(
        string prompt,
        string language,
        LlmGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Try cache first (unless refresh mode)
        if (!RefreshCache)
        {
            var cached = await _cache.GetAsync(Name, prompt, options);
            if (cached != null)
            {
                return cached;
            }
        }

        // Make actual API call
        var result = await _inner.GenerateCodeAsync(prompt, language, options, cancellationToken);

        // Cache successful results
        if (result.Success)
        {
            await _cache.SetAsync(Name, prompt, options, result);
        }

        return result;
    }

    public decimal EstimateCost(int inputTokens, int outputTokens, string? model = null)
        => _inner.EstimateCost(inputTokens, outputTokens, model);

    public int EstimateTokenCount(string text)
        => _inner.EstimateTokenCount(text);
}
